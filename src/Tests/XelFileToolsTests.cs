using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bubbles.XEvent.MCPServer.Services;
using Bubbles.XEvent.MCPServer.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
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
            var result = await XelFileTools.ListEventsInXelFile(filePath, cancellationToken, null, maxEvents, byteOffset);
            var filteredResult = await XelFileTools.ListEventsInXelFile(filePath, cancellationToken, null, maxEvents, 0, "", "session_id,database_name");
            var noResult = await XelFileTools.ListEventsInXelFile(filePath, cancellationToken, null, maxEvents, 0, "non_existent_event");
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
            var result = await XelFileTools.ListEventsInXelFile(filePath, cancellationToken, null, maxEvents, byteOffset);
            // Assert
            Assert.That(result, Is.Not.Null.And.StartsWith("Total events read: 1. More events may be available at byte offset 25109"));

            result = await XelFileTools.ListEventsInXelFile(filePath, cancellationToken, null, maxEvents, 25109);
            var filteredResult = await XelFileTools.ListEventsInXelFile(filePath, cancellationToken, null, maxEvents, 25109, "", "session_id,database_name");
            var noResult = await XelFileTools.ListEventsInXelFile(filePath, cancellationToken, null, maxEvents, 25109, "non_existent_event");

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
                var result = await XelFileTools.ListEventsInXelFile(url.ToString(), CancellationToken.None, provider, maxEvents: 10, byteOffset: 0);

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
                var result = await XelFileTools.ListEventsInXelFile(url.ToString(), CancellationToken.None, provider, maxEvents: 1, byteOffset: 0);

                Assert.That(result, Is.Not.Null.And.StartsWith("Total events read: 1. More events may be available at byte offset 25109"));

                result = await XelFileTools.ListEventsInXelFile(url.ToString(), CancellationToken.None, provider, maxEvents: 1, byteOffset: 25109);
                Assert.That(result, Is.Not.Null.And.StartsWith("Total events read: 1. More events may be available at byte offset ").And.Contains("Events: [{\"Name\":\"sql_batch_starting\",\"UUID\":\"64e8eccc-1de0-405c-9b49-a4ea488fe9a4\","));
            }
            finally
            {
                await host.StopAsync();
                host.Dispose();
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
