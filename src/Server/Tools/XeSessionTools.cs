using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bubbles.XEvent.MCPServer.Helpers;
using Bubbles.XEvent.MCPServer.Services;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.XEvent.XELite;
using ModelContextProtocol;
using ModelContextProtocol.Server;
#pragma warning disable CA1068 // CancellationToken parameters must come last. Suppressed because we have optional parameters after the CancellationToken, which is a design choice for this tool.

namespace Bubbles.XEvent.MCPServer.Tools
{
    [McpServerToolType]
    public static class XeSessionTools
    {

        private static readonly string EmptyCollection = JsonSerializer.Serialize(new List<IXEvent>());
        /// <summary>
        /// Reads events from an extended event session target. Accepts maximum number of events to read and a time limit in milliseconds. It can also filter by event names and actions/fields to minimize data read. By default it returns 100 events from the live session.
        /// </summary>
        /// <param name="sessionName"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="connectionProvider"></param>
        /// <param name="targetName"></param>
        /// <param name="maxEvents"></param>
        /// <param name="timeLimitMs"></param>
        /// <param name="eventNames"></param>
        /// <param name="actionsAndFields"></param>
        /// <returns></returns>
        [McpServerTool(Name = "xesession_read_target")]
        [Description("Reads events from either a file target or the live target of a SQL Server extended event session. Accepts maximum number of events to read and a time limit in milliseconds. It can also filter by event names and actions/fields to minimize data read. By default it returns 100 events.")]
        public static async Task<string> ReadXeSessionTarget(string sessionName,
            CancellationToken cancellationToken,
            IProgress<ProgressNotificationValue> progress,
            IConnectionProvider connectionProvider,
            [Description("The name of the target within the session. Defaults to the live session. Must be 'live' or 'file'.")] string targetName = "",
            [Description("The maximum number of events to read. 0 means no maximum.")] int maxEvents = 100,
            [Description("The time limit in milliseconds for reading events. Default is 1000.")] int timeLimitMs = 1000,
            [Description("The comma-separated list of event names to include. Defaults to all events.")] string eventNames = "",
            [Description("The comma-separated list of actions and field names to include. Defaults to all.")] string actionsAndFields = "",
            [Description("The continuation token for reading events starting at the last position where reading stopped. Only use values returned by previous calls to the same tool for the same session and target.")] string continuationToken = "")
        {
            if (string.IsNullOrEmpty(sessionName))
            {
                throw new ArgumentException("Session name cannot be null or empty.", nameof(sessionName));
            }

            if (targetName != "live" && targetName != "file" && targetName != string.Empty)
            {
                return "Currently only the live target and the file target are supported.";
            }

            if (maxEvents < 0)
            {
                throw new ArgumentException("Max events cannot be negative.", nameof(maxEvents));
            }
            
            var connection = connectionProvider.GetConnections().First().ConnectionString;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var stopWatch = Stopwatch.StartNew();
            var eventData = EmptyCollection;
            int? eventCount = null;
            var eventList = new List<IXEvent>();
            var fileEventList = new List<IFileExtendedEvent>();
            var continuationTokenValue = string.Empty;
            Exception? ex = null;
            cts.CancelAfter(timeLimitMs);
            try
            {
                if (targetName != "file")
                {
                    eventCount = await ReadLiveTarget(sessionName, progress, maxEvents, timeLimitMs, eventNames, actionsAndFields, connection, cts, stopWatch, eventList).ConfigureAwait(false);
                }
                else
                {
                    string? fileName = null;
                    long? fileOffset = null;
                    
                    if (continuationToken != string.Empty) 
                    {
                        var fileOffsetStart = continuationToken.LastIndexOf(':');
                        if (fileOffsetStart != -1)
                        {
                            if (long.TryParse(continuationToken[(fileOffsetStart + 1)..].TrimEnd('\''), out var offset))
                            {
                                fileOffset = offset;
                                fileName = continuationToken[..fileOffsetStart].TrimStart('\'');
                            }
                        }
                    }
                    eventCount = await ReadFileTarget(sessionName, progress, maxEvents, timeLimitMs, eventNames, actionsAndFields, connection, cts, stopWatch, fileEventList, fileName, fileOffset).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (SqlException) when (cts.IsCancellationRequested)
            {
                // SQL often converts cancellation into a SqlException, so we catch it and treat it as a cancellation.
            }
            catch (Exception e)
            {
                ex = e;
            }
            finally
            {
                stopWatch.Stop();
            }
            if (eventList.Count > 0)
            {
                eventCount ??= eventList.Count;
                eventData = JsonSerializer.Serialize(eventList);
            }
            else if (fileEventList.Count > 0)
            {
                if (cts.IsCancellationRequested)
                {
                    var lastEvent = fileEventList.LastOrDefault();
                    if (lastEvent != null)
                    {
                        continuationTokenValue = $"More events may be available. Call again with continuation token '{lastEvent.FileName}:{lastEvent.FileOffset}' to resume reading the next position.";
                    }
                }
                eventCount ??= fileEventList.Count;
                eventData = JsonSerializer.Serialize(fileEventList);
            }
            return ex != null
                ? $"Error reading events from session '{sessionName}': {ex.Message}. Total events read: {eventCount}. Events: {eventData}. Elapsed Time: {stopWatch.ElapsedMilliseconds} ms. {continuationTokenValue}"
                : $"Total events read: {eventCount}. Events: {eventData}. Elapsed Time: {stopWatch.ElapsedMilliseconds} ms. {continuationTokenValue}";
        }

        private static async Task<int> ReadFileTarget(string sessionName, 
            IProgress<ProgressNotificationValue> progress, 
            int maxEvents, 
            int timeLimitMs, 
            string eventNames, 
            string actionsAndFields, 
            string connection, 
            CancellationTokenSource cts, 
            Stopwatch stopWatch, 
            List<IFileExtendedEvent> eventList, 
            string? fileName,
            long? fileOffset)
        {
            using var sessionHelper = new XeSessionHelper(connection);
            var fileReader = new XEFileTargetStreamer((SqlConnection)((ICloneable)sessionHelper.Connection).Clone());
            var eventCount = 0;
            var filter = eventNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            await fileReader.ReadEventStream(
                xeConnectionOpen: async () => { progress?.Report(new ProgressNotificationValue { Progress = 0, Message = $"Connected. Opening file for '{sessionName}'..." }); },
                xeEventHandler: async (fileXEvent) =>
                {
                    if (filter.Length > 0 && !filter.Contains(fileXEvent.Name))
                    {
                        return;
                    }
                    eventCount++;
                    eventList.Add(fileXEvent);
                    progress?.Report(new ProgressNotificationValue
                    {
                        Message = $"Reading events from session '{sessionName}'...",
                        Total = eventCount,
                        Progress = maxEvents > 0 ? (float)eventCount / maxEvents : (float)stopWatch.ElapsedMilliseconds / timeLimitMs
                    });
                    if (maxEvents > 0 && eventCount >= maxEvents)
                    {
                        cts.Cancel();
                    }
                },
                path: sessionHelper.GetFileTargetFilePath(sessionName),
                fileName: fileName,
                fileOffset: fileOffset,
                fieldsAndActionsFilter: actionsAndFields,
                cts.Token).ConfigureAwait(false);
            return eventCount;
        }

        private static async Task<int> ReadLiveTarget(string sessionName, IProgress<ProgressNotificationValue> progress, int maxEvents, int timeLimitMs, string eventNames, string actionsAndFields, string connection, CancellationTokenSource cts, Stopwatch stopWatch, List<IXEvent> eventList)
        {
            var eventFilter = eventNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var actionsAndFieldsFilter = actionsAndFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var xeStream = new XELiveEventStreamer(connection, sessionName);
            var eventCount = 0;
            await xeStream.ReadEventStream(xevent =>
            {
                if (eventFilter.Length > 0 && !eventFilter.Contains(xevent.Name))
                {
                    return Task.CompletedTask;
                }

                eventCount++;

                if (actionsAndFieldsFilter.Length > 0)
                {
                    xevent = new ExtendedEvent(xevent, actionsAndFieldsFilter);
                }

                eventList.Add(xevent);
                progress?.Report(new ProgressNotificationValue
                {
                    Message = $"Reading events from session '{sessionName}'...",
                    Total = eventCount,
                    Progress = maxEvents > 0 ? (float)eventCount / maxEvents : (float)stopWatch.ElapsedMilliseconds / timeLimitMs
                });
                if (maxEvents > 0 && eventCount >= maxEvents)
                {
                    cts.Cancel();
                }

                return Task.CompletedTask;
            }, cts.Token).ConfigureAwait(false);
            return eventCount;
        }
    }
}
