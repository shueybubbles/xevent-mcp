using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bubbles.XEvent.MCPServer.Helpers;
using Bubbles.XEvent.MCPServer.Services;
using Bubbles.XEvent.MCPServer.Tools;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using NUnit.Framework;

namespace Bubbles.XEvent.Tests
{
    [TestFixture]
    public class XeSessionToolsTests
    {
        [Test]
        public async Task ReadXeSessionTarget_reads_live_target()
        {
            var connectionString = Environment.GetEnvironmentVariable(EnvironmentConnectionProvider.ConnectionStringEnvVar);
            if (string.IsNullOrEmpty(connectionString))
            {
                Assert.Ignore($"Environment variable '{EnvironmentConnectionProvider.ConnectionStringEnvVar}' is not set. Skipping test.");
            }
            var server = new Server(new ServerConnection() { ConnectionString = connectionString });
            var sessionName = Guid.NewGuid().ToString("N");
            using var disposer = CreateSession(sessionName, server);

            using var tokenSource = new System.Threading.CancellationTokenSource();
            using var eventRecorder = new SqlClientEventRecorder() { EnableTraceLogging = true };
            eventRecorder.Start();
            // Need more than 10 seconds to allow for Entra auth token refresh
            var readTask = XeSessionTools.ReadXeSessionTarget(sessionName, tokenSource.Token, null, new EnvironmentConnectionProvider(), eventNames: "sql_batch_starting", actionsAndFields: "batch_text", timeLimitMs: 30000, maxEvents: 100000);
            await Task.Delay(2000); // Wait for the session to start and the reader to be ready
            _ = await server.ExecutionManager.ConnectionContext.ExecuteScalarAsync("select count(name) from sys.tables");
            _ = server.ExecutionManager.ConnectionContext.ExecuteScalar("select count(name) from sys.tables");
            _ = server.ExecutionManager.ConnectionContext.ExecuteScalar("select count(name) from sys.tables");
            _ = server.ExecutionManager.ConnectionContext.ExecuteScalar("select count(name) from sys.tables");
            var results = await readTask;
            eventRecorder.Stop();
            var messages = string.Join(Environment.NewLine, eventRecorder.Events.SelectMany(e => e.Payload).Select(p => p.ToString()));
            Trace.TraceInformation(results);
            Assert.That(results, Is.Not.Null.And.Contains("select count(name) from sys.tables"));

        }

        [Test]
        public async Task ReadXeSessionTarget_reads_ringbuffer_target()
        {
            var connectionString = Environment.GetEnvironmentVariable(EnvironmentConnectionProvider.ConnectionStringEnvVar);
            if (string.IsNullOrEmpty(connectionString))
            {
                Assert.Ignore($"Environment variable '{EnvironmentConnectionProvider.ConnectionStringEnvVar}' is not set. Skipping test.");
            }
            var server = new Server(new ServerConnection() { ConnectionString = connectionString });
            var sessionName = Guid.NewGuid().ToString("N");
            using var disposer = CreateSession(sessionName, server);

            using var tokenSource = new System.Threading.CancellationTokenSource();
            using var eventRecorder = new SqlClientEventRecorder() { EnableTraceLogging = true };
            eventRecorder.Start();
            _ = await server.ExecutionManager.ConnectionContext.ExecuteScalarAsync("select count(name) from sys.tables");
            _ = server.ExecutionManager.ConnectionContext.ExecuteScalar("select count(name) from sys.tables");
            // Need more than 10 seconds to allow for Entra auth token refresh
            var results = await XeSessionTools.ReadXeSessionTarget(sessionName, tokenSource.Token, null, new EnvironmentConnectionProvider(), targetName: "ring buffer", eventNames: "sql_batch_starting", actionsAndFields: "batch_text", timeLimitMs: 30000, maxEvents: 10);
            eventRecorder.Stop();
            var messages = string.Join(Environment.NewLine, eventRecorder.Events.SelectMany(e => e.Payload).Select(p => p.ToString()));
            Trace.TraceInformation(results);
            Assert.That(results, Is.Not.Null.And.Contains("select count(name) from sys.tables"));

        }
        [Test]
        public async Task ReadXeSessionTarget_reads_file_target()
        {
            var connectionString = Environment.GetEnvironmentVariable(EnvironmentConnectionProvider.ConnectionStringEnvVar);
            if (string.IsNullOrEmpty(connectionString))
            {
                Assert.Ignore($"Environment variable '{EnvironmentConnectionProvider.ConnectionStringEnvVar}' is not set. Skipping test.");
            }
            var server = new Server(new ServerConnection() { ConnectionString = connectionString });
            var sessionName = Guid.NewGuid().ToString("N");
            using var disposer = CreateSession(sessionName, server, addFileTarget: true);

            using var tokenSource = new System.Threading.CancellationTokenSource();
            using var eventRecorder = new SqlClientEventRecorder() { EnableTraceLogging = true };
            eventRecorder.Start();
            _ = await server.ExecutionManager.ConnectionContext.ExecuteScalarAsync("select count(name) from sys.tables");
            _ = await server.ExecutionManager.ConnectionContext.ExecuteScalarAsync("select count(name) from sys.tables");
            await Task.Delay(2000); // Wait for the session to write the file
            // Need more than 10 seconds to allow for Entra auth token refresh
            var results = await XeSessionTools.ReadXeSessionTarget(sessionName, tokenSource.Token, null, new EnvironmentConnectionProvider(), targetName: "file", eventNames: "sql_batch_starting", actionsAndFields: "batch_text", timeLimitMs: 30000, maxEvents: 1);
                        
            eventRecorder.Stop();
            Trace.TraceInformation(results);
            // On a busy server this may log an unrelated batch
            Assert.That(results, Is.Not.Null.And.Contains("select count(name) from sys.tables"));
            Assert.That(results, Contains.Substring("More events may be available. Call again with continuation token '"));
            // Read the continuation token value and call again to get the next event
            var continuationTokenStart = results.IndexOf("continuation token '") + "continuation token '".Length;
            var continuationTokenEnd = results.IndexOf('\'', continuationTokenStart);
            var continuationToken = results[continuationTokenStart..continuationTokenEnd];
            var results2 = await XeSessionTools.ReadXeSessionTarget(sessionName, tokenSource.Token, null, new EnvironmentConnectionProvider(), targetName: "file", timeLimitMs: 30000, maxEvents: 1, continuationToken: continuationToken);
            Assert.That(results2, Is.Not.Null.And.Contains("Total events read: 1"));
            Trace.TraceInformation(results2);

        }

        [Test]
        public async Task XEFileTargetStreamer_reads_file_target()
        {
            var connectionString = Environment.GetEnvironmentVariable(EnvironmentConnectionProvider.ConnectionStringEnvVar);
            if (string.IsNullOrEmpty(connectionString))
            {
                Assert.Ignore($"Environment variable '{EnvironmentConnectionProvider.ConnectionStringEnvVar}' is not set. Skipping test.");
            }
            using var sqlConnection = new SqlConnection(connectionString);
            var server = new Server(new ServerConnection() { ConnectionString = connectionString });
            var filePath = string.Empty;
            using (var sessionDisposal = CreateSession(Guid.NewGuid().ToString("N"), server, addFileTarget: true))
            {
                _ = await server.ExecutionManager.ConnectionContext.ExecuteScalarAsync("select count(name) from sys.tables");
                await Task.Delay(1000); // Wait for the event to be written to the file target
                filePath = sessionDisposal.FilePath;
            }
            var streamer = new XEFileTargetStreamer(sqlConnection);
            using var tokenSource = new System.Threading.CancellationTokenSource();
            var eventCount = 0;
            IFileExtendedEvent foundEvent = null;
            try
            {
                using var eventRecorder = new SqlClientEventRecorder() { EnableTraceLogging = true };
                await streamer.ReadEventStream(
                    xeConnectionOpen: async () => await Task.CompletedTask,
                    xeEventHandler: async (fileXEvent) => { if (fileXEvent.Name == "sql_batch_starting") { eventCount++; foundEvent = fileXEvent; tokenSource.Cancel(); } },
                    path: Path.ChangeExtension(filePath, null),
                    fileName: null,
                    fileOffset: null,
                    fieldsAndActionsFilter: "batch_text,event_sequence",
                    cancellationToken: tokenSource.Token
                );
            }
            catch (OperationCanceledException)
            {
                // Expected due to cancellation after first event
            }
            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(foundEvent, Is.Not.Null);
            Assert.That(foundEvent.Fields.Keys, Is.EqualTo(["batch_text"]));
            Assert.That(foundEvent.Actions.Keys, Is.EqualTo(["event_sequence"]));
        }

        internal static XeSessionDisposable CreateSession(string sessionName, Server server, long maxDurationSeconds = 1000, bool addFileTarget = false)
        {
            var onDatabase = server.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase ? "ON DATABASE" : "ON SERVER";
            var fileTarget = "";
            var filePath = "xetest_" + sessionName + ".xel";
            if (addFileTarget)
            {
                if (server.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase)
                {
                    var storageUrl = Environment.GetEnvironmentVariable("XESessionToolsTests_StorageUrl")?.TrimEnd('/');
                    if (string.IsNullOrEmpty(storageUrl))
                    {
                        Assert.Ignore("Environment variable 'XESessionToolsTests_StorageUrl' is not set. Cannot create file target for Azure SQL Database.");
                    }
                    var database = server.Databases[server.ExecutionManager.ConnectionContext.SqlConnectionObject.Database];
                    var dbScopedCredential = database.DatabaseScopedCredentials;
                    if (!dbScopedCredential.Contains(storageUrl))
                    {
                        var credential = new DatabaseScopedCredential(database, storageUrl) { Identity = "MANAGED IDENTITY" };
                        credential.Create();
                    }
                    fileTarget = $",ADD TARGET package0.event_file(SET filename = N'{storageUrl}/{filePath}')";
                    filePath = storageUrl + "/" + filePath;
                }
                else
                {
                    fileTarget = $",ADD TARGET package0.event_file(SET filename = N'{filePath}')";
                }
            }
            _ = server.ExecutionManager.ConnectionContext.ExecuteNonQuery(
            $@"CREATE EVENT SESSION [{sessionName}] {onDatabase}
ADD EVENT sqlserver.error_reported(
    ACTION(package0.callstack_rva,sqlserver.database_id,sqlserver.session_id,sqlserver.sql_text,sqlserver.tsql_stack)
    WHERE ([severity]>=(20) OR ([error_number]=(17803) OR [error_number]=(701) OR [error_number]=(802) OR [error_number]=(8645) OR [error_number]=(8651) OR [error_number]=(8657) OR [error_number]=(8902) OR [error_number]=(41354) OR [error_number]=(41355) OR [error_number]=(41367) OR [error_number]=(41384) OR [error_number]=(41336) OR [error_number]=(41309) OR [error_number]=(41312) OR [error_number]=(41313)))),
ADD EVENT sqlserver.existing_connection(
    ACTION(package0.event_sequence,sqlserver.client_hostname,sqlserver.session_id)),
ADD EVENT sqlserver.login(SET collect_options_text=(1)
    ACTION(package0.event_sequence,sqlserver.client_hostname,sqlserver.session_id)),
ADD EVENT sqlserver.logout(
    ACTION(package0.event_sequence,sqlserver.session_id)),
ADD EVENT sqlserver.rpc_starting(
    ACTION(package0.event_sequence,sqlserver.database_name,sqlserver.session_id)
    WHERE ([package0].[equal_boolean]([sqlserver].[is_system],(0)))),
ADD EVENT sqlserver.sql_batch_starting(
    ACTION(package0.event_sequence,sqlserver.database_name,sqlserver.session_id)
    WHERE ([package0].[equal_boolean]([sqlserver].[is_system],(0)))),
ADD EVENT sqlserver.sql_batch_completed(
    ACTION(package0.event_sequence,sqlserver.database_name,sqlserver.session_id)
    WHERE ([package0].[equal_boolean]([sqlserver].[is_system],(0))))
ADD TARGET package0.ring_buffer{fileTarget}
WITH (MAX_MEMORY=16384 KB,EVENT_RETENTION_MODE=ALLOW_SINGLE_EVENT_LOSS,MAX_DISPATCH_LATENCY=1 SECONDS,MAX_EVENT_SIZE=0 KB,MEMORY_PARTITION_MODE=PER_CPU,TRACK_CAUSALITY=ON,MAX_DURATION={maxDurationSeconds} SECONDS)");
            
            _ = server.ExecutionManager.ConnectionContext.ExecuteNonQuery($"ALTER EVENT SESSION [{sessionName}] {onDatabase} STATE = START");
            return new XeSessionDisposable(server, sessionName, filePath, onDatabase);
        }
    }

    internal class XeSessionDisposable(Server server, string sessionName, string filePath, string onDatabase) : IDisposable
    {
        public string SessionName { get; } = sessionName;

        public string FilePath { get; } = filePath;

        public string OnDatabase { get; } = onDatabase;

        public Server Server { get; } = server;

        public void Dispose()
        {
            try
            {
                _ = Server.ExecutionManager.ConnectionContext.ExecuteNonQuery($"ALTER EVENT SESSION [{SessionName}] {OnDatabase} STATE = STOP");
            }
            catch
            {
            }// probably already stopped }
            try
            {
                _ = Server.ExecutionManager.ConnectionContext.ExecuteNonQuery($"DROP EVENT SESSION [{SessionName}] {OnDatabase}");
            }
            catch
            { // probably already dropped }
            }
        }    
    }
}
