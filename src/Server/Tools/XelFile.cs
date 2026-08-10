using Microsoft.SqlServer.XEvent.XELite;
using ModelContextProtocol.Server;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Threading;
using System.Text.Json;
using System.Linq;
using System;
namespace Bubbles.XEvent.MCPServer.Tools;

[McpServerToolType]
public static class XelFileTools
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = false
    };

    [McpServerTool(Name = "xel_file_read")]
    [Description("Reads events from a .xel file. Accepts an optional byte offset and a maximum number of events to read. By default it returns 100 events from the start of the file.")]
    public static async Task<string> ListEventsInXelFile(string filePath, CancellationToken cancellationToken, [Description("The maximum number of events to read. 0 means no maximum.")] long maxEvents = 100, [Description("The byte offset to start reading from.")] long byteOffset = 0)
    {
        if (!System.IO.File.Exists(filePath))
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
        var fileSize = new System.IO.FileInfo(filePath).Length;
        var reader = new XEFileEventStreamer(filePath);
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
                        eventCount++;
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
            // Expected when maxEvents is reached. Check if the incoming cancellation token is canceled, if so, rethrow the exception to propagate the cancellation.
            cancellationToken.ThrowIfCancellationRequested();
        }

        var isFinished = maxEvents == 0 || eof || eventList.Count == 0 || 
          (maxEvents > 0 && eventList.Count < maxEvents) || 
          eventList.LastOrDefault()?.XEventEndOffsetInBytes == fileSize;
        var eventData = JsonSerializer.Serialize(eventList, jsonOptions);
        return isFinished
            ? $"The end of the file has been reached. Total events read: {eventList.Count}. Events: {eventData}"
            : $"Total events read: {eventList.Count}. More events may be available at byte offset {eventList[^1].XEventEndOffsetInBytes}. Events: {eventData}";
    }

}
