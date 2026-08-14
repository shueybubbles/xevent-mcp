using Microsoft.SqlServer.XEvent.XELite;
using ModelContextProtocol.Server;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Threading;
using System.Text.Json;
using System.Linq;
using System;
using Bubbles.XEvent.MCPServer.Services;
using Bubbles.XEvent.MCPServer.Helpers;
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
        [Description("The maximum number of events to read. 0 means no maximum.")] long maxEvents = 100, 
        [Description("The byte offset to start reading from.")] long byteOffset = 0,
        [Description("The comma-separated list of event names to include. Defaults to all events.")] string eventNames = "",
        [Description("The comma-separated list of actions and field names to include. Defaults to all.")] string actionsAndFields = "")
    {

        var eventFilter = eventNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var actionsAndFieldsFilter = actionsAndFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var isHttpPath = urlStreamProvider != null
            && Uri.TryCreate(filePath, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        if (!isHttpPath && !System.IO.File.Exists(filePath))
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
        
        if (isHttpPath)
        {
            return await GetUrlStreamAsync(filePath, urlStreamProvider!, maxEvents, byteOffset, eventFilter, actionsAndFieldsFilter, cancellationToken);
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
                );
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
                );
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
                );
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
                );
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
