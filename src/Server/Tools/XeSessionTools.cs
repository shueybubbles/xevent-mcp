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
using Microsoft.SqlServer.XEvent.XELite;
using ModelContextProtocol;
using ModelContextProtocol.Server;
#pragma warning disable CA1068 // CancellationToken parameters must come last. Suppressed because we have optional parameters after the CancellationToken, which is a design choice for this tool.

namespace Bubbles.XEvent.MCPServer.Tools
{
    [McpServerToolType]
    public static class XeSessionTools
    {
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
        [Description("Reads events from an extended event session target. Accepts maximum number of events to read and a time limit in milliseconds. It can also filter by event names and actions/fields to minimize data read. By default it returns 100 events from the live session.")]
        public static async Task<string> ReadXeSessionTarget(string sessionName,
            CancellationToken cancellationToken,
            IProgress<ProgressNotificationValue> progress,
            IConnectionProvider connectionProvider,
            [Description("The name of the target within the session. Defaults to the live session")] string targetName = "",
            [Description("The maximum number of events to read. 0 means no maximum.")] int maxEvents = 100,
            [Description("The time limit in milliseconds for reading events.")] int timeLimitMs = 1000,
            [Description("The comma-separated list of event names to include. Defaults to all events.")] string eventNames = "",
            [Description("The comma-separated list of actions and field names to include. Defaults to all.")] string actionsAndFields = "")
        {
            if (string.IsNullOrEmpty(sessionName))
            {
                throw new ArgumentException("Session name cannot be null or empty.", nameof(sessionName));
            }

            if (!string.IsNullOrEmpty(targetName))
            {
                return "Currently only the live target is supported.";
            }

            if (maxEvents < 0)
            {
                throw new ArgumentException("Max events cannot be negative.", nameof(maxEvents));
            }

            var eventFilter = eventNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var actionsAndFieldsFilter = actionsAndFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var connection = connectionProvider.GetConnections().First().ConnectionString;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var stopWatch = Stopwatch.StartNew();
            
            var xeStream = new XELiveEventStreamer(connection, sessionName);
            var eventList = new List<IXEvent>();
            var eventCount = 0;
            Exception? ex = null;
            cts.CancelAfter(timeLimitMs);
            try
            {
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
                }, cts.Token);
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Microsoft.Data.SqlClient.SqlException) when (cts.IsCancellationRequested)
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
            var eventData = JsonSerializer.Serialize(eventList);
            if (ex != null)
            {
                return $"Error reading events from session '{sessionName}': {ex.Message}. Total events read: {eventList.Count}. Events: {eventData}. Elapsed Time: {stopWatch.ElapsedMilliseconds} ms";
            }
            return $"Total events read: {eventList.Count}. Events: {eventData}. Elapsed Time: {stopWatch.ElapsedMilliseconds} ms";
        }
    }
}
