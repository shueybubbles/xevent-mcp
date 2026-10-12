// Copyright (c) David Shiflet. All rights reserved.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bubbles.XEvent.MCPServer.Helpers;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.XEvent.XELite;

namespace Bubbles.XEvent.Listen
{
    internal class SessionCommand
    {
        private readonly Argument<string> sessionNameArgument = new("session-name")
        {
            Description = "The name of the Extended Events session to listen to."
        };

        private readonly Option<TargetType?> targetArgument = new("--target", "-t")
        {
            Description = "The target type to listen to. Defaults to Live.",
            Required = false,
        };


        public void AddCommand(RootCommand rootCommand)
        {
            var command = new Command("session", "Listen to a specific Extended Events session.")
            {
                sessionNameArgument,
                targetArgument
            };
            command.SetAction(async (parseResult, cancellationToken) =>
            {
                var context = new SessionCommandContext(parseResult);
                context.PromptForPasswordIfRequired();
                await ReadSession(context, cancellationToken);
            });
            rootCommand.Subcommands.Add(command);
        }

        public static async Task ReadSession(SessionCommandContext context, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (context.Timeout > 0)
            {
                cts.CancelAfter(TimeSpan.FromSeconds(context.Timeout));
            }

            try
            {
                switch (context.Target)
                {
                    case TargetType.Ring:
                        await ReadRingTarget(context, cts).ConfigureAwait(false);
                        break;
                    case TargetType.File:
                        await ReadFileTarget(context, cts).ConfigureAwait(false);
                        break;
                    default:
                        await ReadLiveTarget(context, cts).ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (SqlException) when (cts.IsCancellationRequested)
            {
            }
        }

        private static async Task ReadLiveTarget(SessionCommandContext context, CancellationTokenSource cts)
        {
            var reader = new XELiveEventStreamer(context.ConnectionString, context.SessionName);
            var eventFilter = context.Events.ToHashSet();
            var actionsAndFieldsFilter = context.Fields.ToHashSet();
            long eventCount = 0;

            await reader.ReadEventStream(xevent =>
            {
                if (eventFilter.Count > 0 && !eventFilter.Contains(xevent.Name))
                {
                    return Task.CompletedTask;
                }

                eventCount++;
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
            }, cts.Token).ConfigureAwait(false);
        }

        private static async Task ReadRingTarget(SessionCommandContext context, CancellationTokenSource cts)
        {
            using var sqlConnection = new SqlConnection(context.ConnectionString);
            var reader = new RingBufferTargetStreamer(sqlConnection, context.SessionName);
            var maxEvents = context.MaxEvents > 0 ? (int)Math.Min(context.MaxEvents, int.MaxValue) : int.MaxValue;
            long eventCount = 0;

            await reader.ReadEventStream(
                xeConnectionOpen: () => Task.CompletedTask,
                xeEventHandler: ringBufferXEvent =>
                {
                    eventCount++;
                    context.EventOutput.WriteRow(context, new RowData(
                        ringBufferXEvent.Timestamp,
                        ringBufferXEvent.Name,
                        new Dictionary<string, object>(ringBufferXEvent.Fields.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value))),
                        new Dictionary<string, object>(ringBufferXEvent.Actions.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value)))));

                    if (context.MaxEvents > 0 && eventCount >= context.MaxEvents)
                    {
                        cts.Cancel();
                    }

                    return Task.CompletedTask;
                },
                maxEvents: maxEvents,
                events: string.Join(',', context.Events),
                fieldsAndActions: string.Join(',', context.Fields),
                cancellationToken: cts.Token).ConfigureAwait(false);
        }

        private static async Task ReadFileTarget(SessionCommandContext context, CancellationTokenSource cts)
        {
            using var sessionHelper = new XeSessionHelper(context.ConnectionString);
            var reader = new XEFileTargetStreamer((SqlConnection)((ICloneable)sessionHelper.Connection).Clone());
            var eventFilter = context.Events.ToHashSet();
            var actionsAndFieldsFilter = context.Fields.ToHashSet();
            long eventCount = 0;

            await reader.ReadEventStream(
                xeConnectionOpen: () => Task.CompletedTask,
                xeEventHandler: fileXEvent =>
                {
                    if (eventFilter.Count > 0 && !eventFilter.Contains(fileXEvent.Name))
                    {
                        return Task.CompletedTask;
                    }

                    eventCount++;
                    context.EventOutput.WriteRow(context, new RowData(
                        fileXEvent.Timestamp,
                        fileXEvent.Name,
                        new Dictionary<string, object>(fileXEvent.Fields.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value))),
                        new Dictionary<string, object>(fileXEvent.Actions.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value)))));

                    if (context.MaxEvents > 0 && eventCount >= context.MaxEvents)
                    {
                        cts.Cancel();
                    }

                    return Task.CompletedTask;
                },
                path: sessionHelper.GetFileTargetFilePath(context.SessionName),
                fileName: null,
                fileOffset: null,
                fieldsAndActionsFilter: actionsAndFieldsFilter,
                cancellationToken: cts.Token).ConfigureAwait(false);
        }

        public class SessionCommandContext : CommandContextBase
        {
            public SessionCommandContext(ParseResult parseResult) : base(parseResult)
            {
                Server = string.IsNullOrEmpty(Server) ? "localhost" : Server;
                Target = parseResult.GetValueOrEnvVar("--target", "xelisten.target", TargetType.Live);
                SessionName = parseResult.GetRequiredValue<string>("session-name");
            }

            public string SessionName { get; }

            public TargetType Target { get; }
        }
    }
}
