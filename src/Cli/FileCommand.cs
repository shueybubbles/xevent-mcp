// Copyright (c) David Shiflet. All rights reserved.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bubbles.XEvent.MCPServer.Helpers;
using Bubbles.XEvent.MCPServer.Services;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.XEvent.XELite;

namespace Bubbles.XEvent.Listen
{
    public class FileCommand
    {
        private readonly Argument<string> fileArgument = new("file-name")
        {
            Description = "The name of the file to read Extended Events from. Do not include .xel extension when specifying --server."
        };

        private readonly Option<string> fileNameOption = new("--file-name", "-F")
        {
            Description = "Path to the specific .xel file to read when specifying --server and --byte-offset.",            
        };

        private readonly Option<long?> byteOffsetOption = new("--byte-offset", "-b")
        {
            Description = "The byte offset to start reading from. ",
            Required = false,
            Validators =
            {
                result =>
                {
                    if (result.GetValueOrDefault<long>() < 0)
                    {
                        result.AddError("The byte offset must be a non-negative integer.");
                    }
                }
            }
        };

        public void AddCommand(RootCommand rootCommand)
        {
            var command = new Command("file", "Read Extended Events from a file.")
            {
                fileArgument,
                fileNameOption,
                byteOffsetOption
            };
            command.SetAction(async (parseResult, cancellationToken) =>
            {
                var context = new FileCommandContext(parseResult);
                if (!string.IsNullOrEmpty(context.Server))
                {
                    context.PromptForPasswordIfRequired();
                }
                await ReadXelFile(context, cancellationToken);
            });
            rootCommand.Subcommands.Add(command);
        }

        public static async Task ReadXelFile(FileCommandContext context, CancellationToken cancellationToken)
        {
            var isHttpPath = Uri.TryCreate(context.FileName, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

            if (!string.IsNullOrEmpty(context.Server))
            {
                await ReadSqlXelFile(context, cancellationToken);
            }
            else if (isHttpPath)
            {
                await ReadHttpXelFile(context, cancellationToken);
            }
            else 
            {
                await ReadLocalXelFile(context, cancellationToken);
            }
        }

        private static async Task ReadLocalXelFile(FileCommandContext context, CancellationToken cancellationToken)
        {
            var reader = new XEFileEventStreamer(context.FileName);            
            await ReadXEventFetcher(context, reader, cancellationToken).ConfigureAwait(false);
        }

        private static async Task ReadHttpXelFile(FileCommandContext context, CancellationToken cancellationToken)
        {
            var urlStreamProvider = new UrlStreamProvider();
            using var stream = await urlStreamProvider.GetStream(context.FileName, 0);
            var reader = new XEFileEventStreamer(stream);
            await ReadXEventFetcher(context, reader, cancellationToken).ConfigureAwait(false);
        }

        private static async Task ReadSqlXelFile(FileCommandContext context, CancellationToken cancellationToken)
        {

            var connectionString = context.ConnectionString;
            using var sqlConnection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
            var streamer = new XEFileTargetStreamer(sqlConnection);
            var eventFilter = context.Events.ToHashSet();
            var actionsAndFieldsFilter = context.Fields.ToHashSet();
            long lastByteOffset = 0;
            var eventCount = 0L;
            var pathPrefix = Path.ChangeExtension(context.FileName, null);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                await streamer.ReadEventStream(
                    xeConnectionOpen: async () =>
                    {                        
                        await Task.CompletedTask;
                    },
                    xeEventHandler: async (fileXEvent) =>
                    {                        
                        if (context.MaxEvents > 0 && eventCount >= context.MaxEvents && fileXEvent.FileOffset != lastByteOffset)
                        {
                            cts.Cancel();
                            return;
                        }
                        if (eventFilter.Count > 0 && !eventFilter.Contains(fileXEvent.Name))
                        {
                            return;
                        }
                        eventCount++;
                        lastByteOffset = fileXEvent.FileOffset;

                        var rowData = new RowData(fileXEvent.Timestamp, fileXEvent.Name,
                            new Dictionary<string, object>(fileXEvent.Fields.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value))),
                            new Dictionary<string, object>(fileXEvent.Actions.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value))));
                        
                        if (context.ByteOffset > -1)
                        { 
                            rowData.FileName = fileXEvent.FileName;
                            rowData.FileOffset = fileXEvent.FileOffset.ToString();
                        }
                        
                        context.EventOutput.WriteRow(context, rowData);
                    },
                    path: pathPrefix,
                    fileName: !string.IsNullOrEmpty(context.SpecificFileName) ? context.SpecificFileName : null,
                    fileOffset: context.ByteOffset > 0 ? context.ByteOffset : null,
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
        }

        internal static async Task ReadXEventFetcher(FileCommandContext context, IXEventFetcher reader, CancellationToken cancellationToken)
        {
            // Cancel read when eventCount == maxEvents
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var eventFilter = context.Events.ToHashSet();
            var actionsAndFieldsFilter = context.Fields.ToHashSet();
            long eventCount = 0;
            long lastByteOffset = 0;
            try
            {
                if (context.ByteOffset >= 0)
                {
                    await reader.ReadEventStreamFromOffset(
                        () => Task.CompletedTask,
                        xevent =>
                        {
                            if (eventFilter.Count > 0 && !eventFilter.Contains(xevent.Name))
                            {
                                return Task.CompletedTask; // Skip this event if it doesn't match the filter
                            }
                            eventCount++;
                            // If actionsAndFields is specified, filter the event's actions and fields
                            if (actionsAndFieldsFilter.Count > 0)
                            {
                                xevent = new ExtendedEvent(xevent, [.. actionsAndFieldsFilter]);
                            }
                            lastByteOffset = xevent.XEventEndOffsetInBytes;
                            if (context.MaxEvents > 0 && eventCount >= context.MaxEvents)
                            {
                                cts.Cancel();
                            }
                            context.EventOutput.WriteRow(context, new RowData(xevent.Timestamp, xevent.Name, xevent.Fields, xevent.Actions));
                            return Task.CompletedTask;
                        },
                        _ =>
                        {
                            cts.Cancel(); // Cancel the read operation when EOF is reached
                            return Task.CompletedTask;
                        },
                        context.ByteOffset,
                        cts.Token
                    ).ConfigureAwait(false);
                }
                else
                {
                    await reader.ReadEventStream(
                        xevent =>
                        {
                            if (eventFilter.Count > 0 && !eventFilter.Contains(xevent.Name))
                            {
                                return Task.CompletedTask; // Skip this event if it doesn't match the filter
                            }
                            eventCount++;
                            // If actionsAndFields is specified, filter the event's actions and fields
                            if (actionsAndFieldsFilter.Count > 0)
                            {
                                xevent = new ExtendedEvent(xevent, [.. actionsAndFieldsFilter]);
                            }
                            context.EventOutput.WriteRow(context, new RowData(xevent.Timestamp, xevent.Name, xevent.Fields, xevent.Actions));
                            if (context.MaxEvents > 0 && eventCount >= context.MaxEvents)
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

        }

    }

    public class FileCommandContext(ParseResult parseResult) : CommandContextBase(parseResult)
    {
        public string FileName { get; } = parseResult.GetRequiredValue<string>("file-name");
        public long ByteOffset { get; } = parseResult.GetValueOrEnvVar("--byte-offset", "xelisten.byte-offset", -1);

        public string SpecificFileName { get; } = parseResult.GetValueOrEnvVar("--file-name", "xelisten.file-name", "");
    }
}
