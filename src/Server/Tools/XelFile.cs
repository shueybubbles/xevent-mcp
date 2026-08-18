using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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
namespace Bubbles.XEvent.MCPServer.Tools;
#pragma warning disable CA1068 // CancellationToken parameters must come last. Suppressed because we have optional parameters after the CancellationToken for maxEvents and byteOffset, which is a design choice for this tool.

[McpServerToolType]
public static class XelFileTools
{
    internal static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = false
    };

    [McpServerTool(Name = "xel_file_read")]
    [Description("Reads events from a .xel file. The file path can be a local file or a URL. Accepts an optional byte offset and a maximum number of events to read. It can also filter by event names and actions/fields to minimize data read. By default it returns 100 events from the start of the file.")]
    public static async Task<string> ListEventsInXelFile(string filePath,
        CancellationToken cancellationToken,
        IUrlStreamProvider urlStreamProvider,
        IConnectionProvider connectionProvider,
        IProgress<ProgressNotificationValue> progress,
        [Description("The maximum number of events to read. 0 means no maximum.")] long maxEvents = 100, 
        [Description("The byte offset to start reading from. If byte offset > 0 or if useSqlServer is false, the filePath parameter must include the xel extension.")] long byteOffset = 0,
        [Description("The comma-separated list of event names to include. Defaults to all events.")] string eventNames = "",
        [Description("The comma-separated list of actions and field names to include. Defaults to all.")] string actionsAndFields = "",
        [Description("When true, uses SQL Server to read the file. Defaults to false.")] bool useSqlServer = false)
    {

        var eventFilter = eventNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var actionsAndFieldsFilter = actionsAndFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var isHttpPath = urlStreamProvider != null
            && Uri.TryCreate(filePath, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        if (!useSqlServer && !isHttpPath && !System.IO.File.Exists(filePath))
        {
            return $"The specified file '{filePath}' does not exist.";
        }
        if (maxEvents < 0)
        {
            return "maxEvents must be >= 0.";
        }
        if (byteOffset < 0)
        {
            return "byteOffset must be >= 0.";
        }

        if (useSqlServer)
        {
            return await GetSqlFileStreamAsync(connectionProvider, progress, filePath, maxEvents, byteOffset, eventNames, actionsAndFields, cancellationToken).ConfigureAwait(false);
        }


        if (isHttpPath)
        {
            return await GetUrlStreamAsync(filePath, urlStreamProvider!, maxEvents, byteOffset, eventFilter, actionsAndFieldsFilter, cancellationToken).ConfigureAwait(false);
        }

        var reader = new XEFileEventStreamer(filePath);
        var fileSize = new System.IO.FileInfo(filePath).Length;

        var eventList = new List<IXEvent>();
        var eventCount = 0;
        // Cancel read when eventCount == maxEvents
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var eof = false;
        try
        {
            if (byteOffset > 0)
            {
                await reader.ReadEventStreamFromOffset(
                    () => Task.CompletedTask,
                    xevent =>
                    {
                        if (eventFilter.Length > 0 && !eventFilter.Contains(xevent.Name))
                        {
                            return Task.CompletedTask; // Skip this event if it doesn't match the filter
                        }
                        eventCount++;
                        // If actionsAndFields is specified, filter the event's actions and fields
                        if (actionsAndFieldsFilter.Length > 0)
                        {                            
                            xevent = new ExtendedEvent(xevent, actionsAndFieldsFilter);
                        }
                        eventList.Add(xevent);
                        if (maxEvents > 0 && eventCount >= maxEvents)
                        {
                            cts.Cancel();
                        }
                        return Task.CompletedTask;
                    },
                    _ =>
                    {
                        eof = true;
                        cts.Cancel(); // Cancel the read operation when EOF is reached
                        return Task.CompletedTask;
                    },
                    byteOffset,
                    cts.Token
                ).ConfigureAwait(false);
            }
            else
            {
                await reader.ReadEventStream(
                    xevent =>
                    {
                        if (eventFilter.Length > 0 && !eventFilter.Contains(xevent.Name))
                        {
                            return Task.CompletedTask; // Skip this event if it doesn't match the filter
                        }
                        eventCount++;
                        // If actionsAndFields is specified, filter the event's actions and fields
                        if (actionsAndFieldsFilter.Length > 0)
                        {                            
                            xevent = new ExtendedEvent(xevent, actionsAndFieldsFilter);
                        }
                        eventList.Add(xevent);
                        if (maxEvents > 0 && eventCount >= maxEvents)
                        {
                            cts.Cancel();
                        }
                        return Task.CompletedTask;
                    },
                    cts.Token
                ).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when maxEvents or eof is reached. Check if the incoming cancellation token is canceled, if so, rethrow the exception to propagate the cancellation.
            cancellationToken.ThrowIfCancellationRequested();
        }

        var isFinished = maxEvents == 0 || eof || eventList.Count == 0 || 
          (maxEvents > 0 && eventList.Count < maxEvents) || (fileSize > 0 && 
          eventList.LastOrDefault()?.XEventEndOffsetInBytes == fileSize);
        var eventData = JsonSerializer.Serialize(eventList, jsonOptions);
        return isFinished
            ? $"The end of the file has been reached. Total events read: {eventList.Count}. Events: {eventData}"
            : $"Total events read: {eventList.Count}. More events may be available at byte offset {eventList[^1].XEventEndOffsetInBytes}. Events: {eventData}";
    }

    // use XEFileTargetStreamer to read the file using SQL Server. When byteOffset is 0, omit fileName parameter from the reader.
    // When byteOffset is > 0, pass the filePath with its xel extension to the reader. Return the events read as a JSON string, along with a message indicating whether the end of the file has been reached or if more events may be available at a specific byte offset.

    private static async Task<string> GetSqlFileStreamAsync(IConnectionProvider connectionProvider, IProgress<ProgressNotificationValue> progress, string filePath, long maxEvents, long byteOffset, string eventFilter, string actionsAndFieldsFilter, CancellationToken cancellationToken)
    {
        var connection = connectionProvider.GetConnections().FirstOrDefault();
        if ( connection == null)
        {
            return "No connections have been configured. Please configure a connection to use SQL Server to read the file.";
        }
        var pathPrefix = Path.ChangeExtension(filePath, null);
        var filter = eventFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var eventCount = 0;
        var eventList = new List<IFileExtendedEvent>();
        using var sqlConnection = new SqlConnection(connection.ConnectionString);
        var streamer = new XEFileTargetStreamer(sqlConnection);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Exception? exception = null;
        try
        {
            await streamer.ReadEventStream(
                xeConnectionOpen: async () =>
                {
                    progress?.Report(new ProgressNotificationValue
                    {
                        Message = $"Connected to SQL. Reading events from file {filePath} using SQL Server connection {connection.Name}.",
                        Progress = 0
                    });
                    await Task.CompletedTask;
                },
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
                        Message = $"Reading events from file '{filePath}'...",
                        Total = eventCount,
                        Progress = maxEvents > 0 ? (float)eventCount / maxEvents : 0
                    });
                    if (maxEvents > 0 && eventCount >= maxEvents)
                    {
                        cts.Cancel();
                    }
                },
                path: pathPrefix,
                fileName: byteOffset > 0 ? filePath : null,
                fileOffset: byteOffset > 0 ? byteOffset : null,
                fieldsAndActionsFilter: actionsAndFieldsFilter,
                cancellationToken: cts.Token

            ).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when maxEvents is reached. Check if the incoming cancellation token is canceled, if so, rethrow the exception to propagate the cancellation.
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (SqlException) when (cts.IsCancellationRequested)
        {
            // SQL often converts cancellation into a SqlException, so we catch it and treat it as a cancellation.
        }
        catch (Exception ex)
        {
            exception = ex;
        }
        var isFinished = maxEvents == 0 || eventList.Count == 0 || (maxEvents > 0 && eventList.Count < maxEvents);
        return !isFinished ? $"Total events read: {eventCount}. More events may be available at byte offset {eventList[^1].FileOffset} in file '{eventList[^1].FileName}'. Events: {JsonSerializer.Serialize(eventList)}."
            : $"Total events read: {eventCount}. Events: {JsonSerializer.Serialize(eventList)}.";
    }

    private static async Task<string> GetUrlStreamAsync(string filePath, IUrlStreamProvider urlStreamProvider, long maxEvents, long byteOffset, string[] eventFilter, string[] actionsAndFieldsFilter, CancellationToken cancellationToken)
    {

        // If the file path is a URL, use the IUrlStreamProvider to get a stream and read from it.
        using var stream = await urlStreamProvider.GetStream(filePath, 0);
        var reader = new XEFileEventStreamer(stream);
        var eventList = new List<IXEvent>();
        var eventCount = 0;
        // Cancel read when eventCount == maxEvents
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var eof = false;
        try
        {
            if (byteOffset > 0)
            {
                await reader.ReadEventStreamFromOffset(
                    () => Task.CompletedTask,
                    xevent =>
                    {
                        if (eventFilter.Length > 0 && !eventFilter.Contains(xevent.Name))
                        {
                            return Task.CompletedTask; // Skip this event if it doesn't match the filter
                        }
                        eventCount++;
                        // If actionsAndFields is specified, filter the event's actions and fields
                        if (actionsAndFieldsFilter.Length > 0)
                        {                            
                            xevent = new ExtendedEvent(xevent, actionsAndFieldsFilter);
                        }
                        eventList.Add(xevent);
                        if (maxEvents > 0 && eventCount >= maxEvents)
                        {
                            cts.Cancel();
                        }
                        return Task.CompletedTask;
                    },
                    _ =>
                    {
                        eof = true;
                        cts.Cancel(); // Cancel the read operation when EOF is reached
                        return Task.CompletedTask;
                    },
                    byteOffset,
                    cts.Token
                ).ConfigureAwait(false);
            }
            else
            {
                await reader.ReadEventStream(
                    xevent =>
                    {
                        eventCount++;
                        if (eventFilter.Length > 0 && !eventFilter.Contains(xevent.Name))
                        {
                            return Task.CompletedTask; // Skip this event if it doesn't match the filter
                        }
                        // If actionsAndFields is specified, filter the event's actions and fields
                        if (actionsAndFieldsFilter.Length > 0)
                        {                            
                            xevent = new ExtendedEvent(xevent, actionsAndFieldsFilter);
                        }
                        eventList.Add(xevent);
                        if (maxEvents > 0 && eventCount >= maxEvents)
                        {
                            cts.Cancel();
                        }
                        return Task.CompletedTask;
                    },
                    cts.Token
                ).ConfigureAwait(false);
                eof = true; // If we reach here, we've read to the end of the stream.
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when maxEvents or eof is reached. Check if the incoming cancellation token is canceled, if so, rethrow the exception to propagate the cancellation.
            cancellationToken.ThrowIfCancellationRequested();
        }
        var isFinished = maxEvents == 0 || eof || eventList.Count == 0;
        var eventData = JsonSerializer.Serialize(eventList, jsonOptions);
        return isFinished
            ? $"The end of the stream has been reached. Total events read: {eventList.Count}. Events: {eventData}"
            : $"Total events read: {eventList.Count}. More events may be available at byte offset {eventList[^1].XEventEndOffsetInBytes}. Events: {eventData}";
    }
}
