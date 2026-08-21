using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bubbles.XEvent.MCPServer.Helpers;
using Bubbles.XEvent.MCPServer.Services;
using Bubbles.XEvent.MCPServer.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using NUnit.Framework;

namespace Bubbles.XEvent.Tests
{
    [TestFixture]
    public class XelFileToolsTests
    {
        private static string TestFilePath => Path.Combine(TestContext.CurrentContext.TestDirectory, "twoevents.xel");

        [Test]
        public async Task ListEventsInXelFile_read_full_file_returns_all_events()
        {
            // Arrange
            var filePath = TestFilePath;
            long maxEvents = 10;
            long byteOffset = 0;
            var cancellationToken = new CancellationToken();
            // Act
            var result = await XelFileTools.ListEventsInXelFile(filePath, cancellationToken, urlStreamProvider:null, connectionProvider: new EnvironmentConnectionProvider(), progress: null, maxEvents, byteOffset);
            var filteredResult = await XelFileTools.ListEventsInXelFile(filePath, cancellationToken, urlStreamProvider: null, connectionProvider: new EnvironmentConnectionProvider(), progress: null, maxEvents, 0, "", "session_id,database_name");
            var noResult = await XelFileTools.ListEventsInXelFile(filePath, cancellationToken, urlStreamProvider: null, connectionProvider: new EnvironmentConnectionProvider(), progress: null, maxEvents, 0, "non_existent_event");
            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null.And.StartsWith("The end of the file has been reached. Total events read: 2. Events: [{\"Name\":\"sql_batch_starting\""));
                Assert.That(result, Contains.Substring("\"Fields\":{\"batch_text\":\"SELECT\\nSCHEMA_NAME(tbl.schema_id) AS [Schema],\\ntbl.name AS [Name],\\ntbl.object_id AS [ID]\\nFROM\\nsys.tables AS tbl\\nORDER BY\\n[Schema] ASC,[Name] ASC\"}"), "Expected the fields to contain the batch text.");
                Assert.That(filteredResult, Contains.Substring("\"Fields\":{}"), "Expected the fields to be empty when specifying actions and fields.");
                Assert.That(filteredResult, Contains.Substring("\"Actions\":{\"database_name\":\"powerpricing-shared\",\"session_id\":64}"), "Expected the actions to contain only the specified entries when specifying actions and fields.");
                Assert.That(noResult, Is.Not.Null.And.StartsWith("The end of the file has been reached. Total events read: 0. Events: []"), "Expected no events to be returned when filtering by a non-existent event name.");
            });
        }

        [Test]
        public async Task ListEventsInXelFile_read_single_event_returns_byteoffset_for_subsequent_reads()
        {
            // Arrange
            var filePath = TestFilePath;
            long maxEvents = 1;
            long byteOffset = 0;
            var cancellationToken = new CancellationToken();
            // Act
            var result = await XelFileTools.ListEventsInXelFile(filePath, cancellationToken, null, null, null, maxEvents, byteOffset);
            // Assert
            Assert.That(result, Is.Not.Null.And.StartsWith("Total events read: 1. More events may be available at byte offset 25109"));

            result = await XelFileTools.ListEventsInXelFile(filePath, cancellationToken, null, null, null,  maxEvents, 25109);
            var filteredResult = await XelFileTools.ListEventsInXelFile(filePath, cancellationToken, null, null, null, maxEvents, 25109, "", "session_id,database_name");
            var noResult = await XelFileTools.ListEventsInXelFile(filePath, cancellationToken, null, null, null, maxEvents, 25109, "non_existent_event");

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null.And.StartsWith("Total events read: 1. More events may be available at byte offset 25325. Events: [{\"Name\":\"sql_batch_starting\",\"UUID\":\"64e8eccc-1de0-405c-9b49-a4ea488fe9a4\","));
                Assert.That(result, Contains.Substring("\"Fields\":{\"batch_text\":\"select * from sys.tables\"}"), "Expected the fields to contain the batch text.");
                Assert.That(filteredResult, Contains.Substring("\"Fields\":{}"), "Expected the fields to be empty when specifying actions and fields.");
                Assert.That(filteredResult, Contains.Substring("\"Actions\":{\"database_name\":\"powerpricing-shared\",\"session_id\":69}"), "Expected the actions to contain only the specified entries when specifying actions and fields.");
                Assert.That(noResult, Is.Not.Null.And.StartsWith("The end of the file has been reached. Total events read: 0. Events: []"), "Expected no events to be returned when filtering by a non-existent event name.");
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ListEventsInXelFile_read_full_http_file_returns_all_events(bool includeContentLength)
        {
            var payload = await File.ReadAllBytesAsync(TestFilePath);
            var (host, client, url) = await CreateTestServerAsync(payload, includeContentLength);

            try
            {
                var provider = new UrlStreamProvider(client);
                var result = await XelFileTools.ListEventsInXelFile(url.ToString(), CancellationToken.None, provider, new EnvironmentConnectionProvider(), progress: null, maxEvents: 10, byteOffset: 0);

                Assert.That(result, Is.Not.Null.And.StartsWith("The end of the stream has been reached. Total events read: 2. Events: [{\"Name\":\"sql_batch_starting\""));
            }
            finally
            {
                await host.StopAsync();
                host.Dispose();
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ListEventsInXelFile_read_single_http_event_returns_byteoffset_for_subsequent_reads(bool includeContentLength)
        {
            var payload = await File.ReadAllBytesAsync(TestFilePath);
            var (host, client, url) = await CreateTestServerAsync(payload, includeContentLength);

            try
            {
                var provider = new UrlStreamProvider(client);
                var result = await XelFileTools.ListEventsInXelFile(url.ToString(), CancellationToken.None, provider, new EnvironmentConnectionProvider(), progress: null, maxEvents: 1, byteOffset: 0);

                Assert.That(result, Is.Not.Null.And.StartsWith("Total events read: 1. More events may be available at byte offset 25109"));

                result = await XelFileTools.ListEventsInXelFile(url.ToString(), CancellationToken.None, provider, new EnvironmentConnectionProvider(), progress: null, maxEvents: 1, byteOffset: 25109);
                Assert.That(result, Is.Not.Null.And.StartsWith("Total events read: 1. More events may be available at byte offset ").And.Contains("Events: [{\"Name\":\"sql_batch_starting\",\"UUID\":\"64e8eccc-1de0-405c-9b49-a4ea488fe9a4\","));
            }
            finally
            {
                await host.StopAsync();
                host.Dispose();
            }
        }

        [Test]
        public async Task ListEventsInXelFile_with_useSqlServer_true_reads_file_target()
        {
            var connectionString = Environment.GetEnvironmentVariable(EnvironmentConnectionProvider.ConnectionStringEnvVar);
            if (string.IsNullOrEmpty(connectionString))
            {
                Assert.Ignore($"Environment variable '{EnvironmentConnectionProvider.ConnectionStringEnvVar}' is not set. Skipping test.");
            }
            var server = new Server(new ServerConnection() { ConnectionString = connectionString });
            var sessionName = Guid.NewGuid().ToString("N");
            var filePath = string.Empty;
            using (var session = XeSessionToolsTests.CreateSession(sessionName, server, addFileTarget: true))
            {
                _ = await server.ExecutionManager.ConnectionContext.ExecuteNonQueryAsync("select count(name) from sys.views");
                _ = await server.ExecutionManager.ConnectionContext.ExecuteNonQueryAsync("select count(name) from sys.tables");
                filePath = session.FilePath;
            }
            await Task.Delay(2000); // Wait for events to be written to the file target
            using var recorder = new SqlClientEventRecorder() { EnableTraceLogging = true };
            recorder.Start();
            var results = await XelFileTools.ListEventsInXelFile(filePath, CancellationToken.None, urlStreamProvider: null, connectionProvider: new EnvironmentConnectionProvider(), progress: null, eventNames:"sql_batch_starting", maxEvents: 1, byteOffset: 0, useSqlServer: true);
            Trace.TraceInformation($"Results: {results}");
            Assert.That(results, Is.Not.Null.And.Contains("Total events read: 1. More events may be available at byte offset "));
            Assert.That(results, Contains.Substring("sql_batch_starting"), "Expected the event name to be present in the results.");
            // Get the file name and offset from the results and read from there
            var offsetStartIndex = results.IndexOf("More events may be available at byte offset ") + "More events may be available at byte offset ".Length;
            var offsetEndIndex = results.IndexOf(' ', offsetStartIndex);
            var byteOffset = long.Parse(results[offsetStartIndex..offsetEndIndex]);
            var nextFileNameStartIndex = results.IndexOf("in file '") + "in file '".Length;
            var nextFileNameEndIndex = results.IndexOf('\'', nextFileNameStartIndex);
            var nextFileName = results[nextFileNameStartIndex..nextFileNameEndIndex];
            Assert.That(Path.GetExtension(nextFileName), Is.EqualTo(".xel"), "Expected the next file name to have a .xel extension.");
            // Filed issue https://feedback.azure.com/d365community/idea/3a30375d-499b-f111-9b47-7c1e52f66aea. https files do not have accurate file_offset values, so we will skip this part of the test if the next file name is a URL.
            if (!nextFileName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var results2 = await XelFileTools.ListEventsInXelFile(nextFileName, CancellationToken.None, urlStreamProvider: null, connectionProvider: new EnvironmentConnectionProvider(), progress: null, maxEvents: 1, byteOffset: byteOffset, useSqlServer: true);
                recorder.Stop();
                Trace.TraceInformation($"Results2: {results2}");
                Assert.That(results2, Is.Not.Null.And.Contains("Total events read: 1. More events may be available at byte offset "));
                var nextOffset = long.Parse(results2[offsetStartIndex..offsetEndIndex]);
                Assert.That(nextOffset, Is.GreaterThan(byteOffset), "Expected the next byte offset to be greater than the previous byte offset.");
            }
        }

        private static async Task<(IHost Host, HttpClient Client, Uri Url)> CreateTestServerAsync(byte[] payload, bool includeContentLength)
        {
            var host = await new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.Configure(app =>
                    {
                        app.Run(async context =>
                        {
                            if (!string.Equals(context.Request.Path.Value, "/twoevents.xel", StringComparison.Ordinal))
                            {
                                context.Response.StatusCode = StatusCodes.Status404NotFound;
                                return;
                            }

                            context.Response.StatusCode = StatusCodes.Status200OK;
                            context.Response.ContentType = "application/octet-stream";

                            if (includeContentLength)
                            {
                                context.Response.ContentLength = payload.LongLength;
                                await context.Response.Body.WriteAsync(payload);
                            }
                            else
                            {
                                var midpoint = payload.Length / 2;
                                await context.Response.Body.WriteAsync(payload.AsMemory(0, midpoint));
                                await context.Response.Body.FlushAsync();
                                await context.Response.Body.WriteAsync(payload.AsMemory(midpoint));
                            }
                        });
                    });
                })
                .StartAsync();

            var client = host.GetTestClient();
            client.BaseAddress = new Uri("http://localhost");

            return (host, client, new Uri(client.BaseAddress!, "/twoevents.xel"));
        }
    }
}
