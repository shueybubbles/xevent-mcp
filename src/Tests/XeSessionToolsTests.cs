using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bubbles.XEvent.MCPServer.Helpers;
using Bubbles.XEvent.MCPServer.Services;
using Bubbles.XEvent.MCPServer.Tools;
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
            var onDatabase = server.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase ? "ON DATABASE" : "ON SERVER";
            var sessionName = Guid.NewGuid().ToString("N");
            _ = server.ExecutionManager.ConnectionContext.ExecuteNonQuery(
                $"CREATE EVENT SESSION [{sessionName}] {onDatabase}\r\nADD EVENT sqlserver.error_reported(\r\nACTION(package0.callstack_rva,sqlserver.database_id,sqlserver.session_id,sqlserver.sql_text,sqlserver.tsql_stack)\r\n    WHERE ([severity]>=(20) OR ([error_number]=(17803) OR [error_number]=(701) OR [error_number]=(802) OR [error_number]=(8645) OR [error_number]=(8651) OR [error_number]=(8657) OR [error_number]=(8902) OR [error_number]=(41354) OR [error_number]=(41355) OR [error_number]=(41367) OR [error_number]=(41384) OR [error_number]=(41336) OR [error_number]=(41309) OR [error_number]=(41312) OR [error_number]=(41313)))),\r\nADD EVENT sqlserver.existing_connection(\r\n    ACTION(package0.event_sequence,sqlserver.client_hostname,sqlserver.session_id)),\r\nADD EVENT sqlserver.login(SET collect_options_text=(1)\r\n    ACTION(package0.event_sequence,sqlserver.client_hostname,sqlserver.session_id)),\r\nADD EVENT sqlserver.logout(\r\n    ACTION(package0.event_sequence,sqlserver.session_id)),\r\nADD EVENT sqlserver.rpc_starting(\r\n    ACTION(package0.event_sequence,sqlserver.database_name,sqlserver.session_id)\r\n    WHERE ([package0].[equal_boolean]([sqlserver].[is_system],(0)))),\r\nADD EVENT sqlserver.sql_batch_starting(\r\n    ACTION(package0.event_sequence,sqlserver.database_name,sqlserver.session_id)\r\n    WHERE ([package0].[equal_boolean]([sqlserver].[is_system],(0))))\r\nWITH (MAX_MEMORY=16384 KB,EVENT_RETENTION_MODE=ALLOW_SINGLE_EVENT_LOSS,MAX_DISPATCH_LATENCY=1 SECONDS,MAX_EVENT_SIZE=0 KB,MEMORY_PARTITION_MODE=PER_CPU,TRACK_CAUSALITY=ON,STARTUP_STATE=ON)");
            _ = server.ExecutionManager.ConnectionContext.ExecuteNonQuery($"ALTER EVENT SESSION [{sessionName}] {onDatabase} STATE = START");

            try
            {

                using var tokenSource = new System.Threading.CancellationTokenSource();
                using var eventRecorder = new SqlClientEventRecorder() { EnableTraceLogging = true };
                eventRecorder.Start();
                // Need more than 10 seconds to allow for Entra auth token refresh
                var readTask = XeSessionTools.ReadXeSessionTarget(sessionName, tokenSource.Token, null, new EnvironmentConnectionProvider(), eventNames: "sql_batch_starting", actionsAndFields: "batch_text", timeLimitMs: 30000, maxEvents: 100000);
                await Task.Delay(2000); // Wait for the session to start and the reader to be ready
                _ =  await server.ExecutionManager.ConnectionContext.ExecuteScalarAsync ("select count(name) from sys.tables");
                _ = server.ExecutionManager.ConnectionContext.ExecuteScalar("select count(name) from sys.tables");
                _ = server.ExecutionManager.ConnectionContext.ExecuteScalar("select count(name) from sys.tables");
                _ = server.ExecutionManager.ConnectionContext.ExecuteScalar("select count(name) from sys.tables");
                var results = await readTask;
                eventRecorder.Stop();
                var messages = string.Join(Environment.NewLine, eventRecorder.Events.SelectMany(e => e.Payload).Select(p => p.ToString()));
                Trace.TraceInformation(results);
                Assert.That(results, Is.Not.Null.And.Contains("select count(name) from sys.tables"));
                
            }
            finally
            {
                _ = server.ExecutionManager.ConnectionContext.ExecuteNonQuery($"ALTER EVENT SESSION [{sessionName}] {onDatabase} STATE = STOP");
                _ = server.ExecutionManager.ConnectionContext.ExecuteNonQuery($"DROP EVENT SESSION [{sessionName}] {onDatabase}");
            }
        }
    }
}
