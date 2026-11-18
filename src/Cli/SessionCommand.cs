// Copyright (c) David Shiflet. All rights reserved.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bubbles.XEvent.MCPServer.Helpers;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
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

        private readonly Option<bool> startArgument = new("--start")
        {
            Description = "Start the session if it is not already running.",
            Required = false,
        };

        private readonly Option<bool> stopArgument = new("--stop")
        {
            Description = "Stop the session before exiting.",
            Required = false,
        };

        public void AddCommand(RootCommand rootCommand)
        {
            var command = new Command("session", "Listen to a specific Extended Events session.")
            {
                sessionNameArgument,
                targetArgument,
                startArgument,
                stopArgument
            };
            command.SetAction(async (parseResult, cancellationToken) =>
            {
                var context = new SessionCommandContext(parseResult);
                context.PromptForPasswordIfRequired();

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var escapeTask = WatchForEscapeKey(cts);
                try
                {
                    await ReadSession(context, cts.Token);
                }
                finally
                {
                    cts.Cancel();
                    await escapeTask.ConfigureAwait(false);

                    if (context.StopSessionOnExit)
                    {
                        try
                        {
                            await StopSessionIfRunning(context, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (SqlException ex) when (IsSessionNotRunningForStop(ex))
                        {
                        }
                        catch (SqlException ex)
                        {
                            Console.Error.WriteLine($"Unable to stop session. SQL error {ex.Number}: {ex.Message}");
                            Environment.ExitCode = 1;
                        }
                    }
                }
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
                if (context.StartSessionIfNeeded)
                {
                    await EnsureSessionIsRunning(context, cts.Token).ConfigureAwait(false);
                }

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
            catch (SqlException ex) when (IsSessionNotFoundOrStopped(ex))
            {
                Console.Error.WriteLine($"Session '{context.SessionName}' is not running. Start it first, or run with --start.");
                Environment.ExitCode = 1;
            }
            catch (SqlException ex)
            {
                Console.Error.WriteLine($"SQL error {ex.Number}: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }

        private static Task EnsureSessionIsRunning(SessionCommandContext context, CancellationToken cancellationToken)
            => SetSessionRunningState(context, shouldBeRunning: true, cancellationToken);

        private static Task StopSessionIfRunning(SessionCommandContext context, CancellationToken cancellationToken)
            => SetSessionRunningState(context, shouldBeRunning: false, cancellationToken);

        private static async Task SetSessionRunningState(SessionCommandContext context, bool shouldBeRunning, CancellationToken cancellationToken)
        {
            using var sqlConnection = new SqlConnection(context.ConnectionString);
            await sqlConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var serverConnection = new ServerConnection(sqlConnection);
            var databaseScope = serverConnection.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase;
            var sessionsView = databaseScope ? "sys.dm_xe_database_sessions" : "sys.dm_xe_sessions";

            using var checkCommand = new SqlCommand($"SELECT COUNT(*) FROM {sessionsView} WHERE name = @session_name", sqlConnection);
            _ = checkCommand.Parameters.AddWithValue("@session_name", context.SessionName);
            var activeSessionCount = Convert.ToInt32(await checkCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            if (shouldBeRunning ? activeSessionCount > 0 : activeSessionCount == 0)
            {
                return;
            }

            var onScope = databaseScope ? "ON DATABASE" : "ON SERVER";
            var sessionState = shouldBeRunning ? "START" : "STOP";
            var escapedSessionName = context.SessionName.Replace("]", "]]", StringComparison.Ordinal);
            using var stateCommand = new SqlCommand($"ALTER EVENT SESSION [{escapedSessionName}] {onScope} STATE = {sessionState}", sqlConnection);
            _ = await stateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private static bool IsSessionNotFoundOrStopped(SqlException ex)
            => ex.Number == 25728 || ex.Message.Contains("could not be found", StringComparison.OrdinalIgnoreCase);

        private static bool IsSessionNotRunningForStop(SqlException ex)
            => IsSessionNotFoundOrStopped(ex)
                || ex.Message.Contains("not started", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("not running", StringComparison.OrdinalIgnoreCase);

        private static async Task ReadLiveTarget(SessionCommandContext context, CancellationTokenSource cts)
        {
            var reader = new XELiveEventStreamer(context.ConnectionString, context.SessionName);
            var eventFilter = context.Events.ToHashSet();
            var actionsAndFieldsFilter = context.Fields.ToHashSet();
            long eventCount = 0;
            var maxEventsReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var readEventStreamTask = reader.ReadEventStream(xevent =>
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
                    // work around https://github.com/dotnet/SqlClient/issues/4957 by not waiting for the ReadEventStream to complete, but instead signaling that the max events have been reached and canceling the token source.
                    _ = maxEventsReached.TrySetResult(true);
                    cts.Cancel();
                }

                return Task.CompletedTask;
            }, cts.Token);

            _ = readEventStreamTask.ContinueWith(
                task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            var completedTask = await Task.WhenAny(readEventStreamTask, maxEventsReached.Task).ConfigureAwait(false);
            if (completedTask == readEventStreamTask)
            {
                await readEventStreamTask.ConfigureAwait(false);
            }
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
                        DateTime.SpecifyKind(fileXEvent.Timestamp, DateTimeKind.Utc),
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

        private static async Task WatchForEscapeKey(CancellationTokenSource cts)
        {
            if (Console.IsInputRedirected)
            {
                return;
            }

            while (!cts.IsCancellationRequested)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        var keyInfo = Console.ReadKey(intercept: true);
                        if (keyInfo.Key == ConsoleKey.Escape)
                        {
                            cts.Cancel();
                            return;
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    return;
                }

                try
                {
                    await Task.Delay(50, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        public class SessionCommandContext(ParseResult parseResult) : CommandContextBase(parseResult)
        {
            public string SessionName { get; } = parseResult.GetRequiredValue<string>("session-name");

            public TargetType Target { get; } = parseResult.GetValueOrEnvVar("--target", "xelisten.target", TargetType.Live);

            public bool StartSessionIfNeeded { get; } = parseResult.GetValueOrEnvVar("--start", "xelisten.start", false);

            public bool StopSessionOnExit { get; } = parseResult.GetValueOrEnvVar("--stop", "xelisten.stop", false);
        }
    }
}
