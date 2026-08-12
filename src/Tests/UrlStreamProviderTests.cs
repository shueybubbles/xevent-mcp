using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bubbles.XEvent.MCPServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Bubbles.XEvent.Tests
{
    [TestFixture]
    public class UrlStreamProviderTests
    {
        private static async Task<(IHost Host, HttpClient Client, Uri Url, Func<int> GetRequestCount)> CreateTestServerAsync(byte[] payload, int chunkSize = 32, int delayMs = 0)
        {
            var requestCount = 0;
            var host = await new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.Configure(app =>
                    {
                        app.Run(async context =>
                        {
                            if (!string.Equals(context.Request.Path.Value, "/data", StringComparison.Ordinal))
                            {
                                context.Response.StatusCode = StatusCodes.Status404NotFound;
                                return;
                            }

                            Interlocked.Increment(ref requestCount);
                            context.Response.StatusCode = StatusCodes.Status200OK;
                            context.Response.ContentType = "application/octet-stream";

                            for (var offset = 0; offset < payload.Length; offset += chunkSize)
                            {
                                var bytesToWrite = Math.Min(chunkSize, payload.Length - offset);
                                await context.Response.Body.WriteAsync(payload, offset, bytesToWrite);
                                await context.Response.Body.FlushAsync();
                                if (delayMs > 0)
                                {
                                    await Task.Delay(delayMs);
                                }
                            }
                        });
                    });
                })
                .StartAsync();

            var client = host.GetTestClient();
            client.BaseAddress = new Uri("http://localhost");

            return (host, client, new Uri(client.BaseAddress!, "/data"), () => Volatile.Read(ref requestCount));
        }

        [Test]
        public async Task GetStream_parallel_same_url_and_offset_uses_single_http_read()
        {
            var payload = Enumerable.Range(0, 131072).Select(i => (byte)(i % 251)).ToArray();
            var (host, client, url, getRequestCount) = await CreateTestServerAsync(payload, chunkSize: 1024, delayMs: 1);

            try
            {
                var provider = new UrlStreamProvider(client);
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                Task<byte[]> ReadAllAsync()
                    => Task.Run(async () =>
                    {
                        await gate.Task;
                        await using var stream = await provider.GetStream(url.ToString(), 0);
                        return await ReadAllBytesAsync(stream);
                    });

                var firstReadTask = ReadAllAsync();
                var secondReadTask = ReadAllAsync();

                gate.SetResult();
                await Task.WhenAll(firstReadTask, secondReadTask);

                Assert.That(firstReadTask.Result, Is.EqualTo(payload));
                Assert.That(secondReadTask.Result, Is.EqualTo(payload));
                Assert.That(getRequestCount(), Is.EqualTo(1));
            }
            finally
            {
                await host.StopAsync();
                host.Dispose();
            }
        }

        [Test]
        public async Task GetStream_parallel_same_url_different_offsets_share_same_http_read()
        {
            var payload = Enumerable.Range(0, 200000).Select(i => (byte)(i % 239)).ToArray();
            var startOffset = 50000;
            var secondExpected = payload.AsSpan(startOffset).ToArray();
            var (host, client, url, getRequestCount) = await CreateTestServerAsync(payload, chunkSize: 2048, delayMs: 1);

            try
            {
                var provider = new UrlStreamProvider(client);
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                Task<byte[]> ReadFromOffsetAsync(long offset)
                    => Task.Run(async () =>
                    {
                        await gate.Task;
                        await using var stream = await provider.GetStream(url.ToString(), offset);
                        return await ReadAllBytesAsync(stream);
                    });

                var fromStartTask = ReadFromOffsetAsync(0);
                var fromOffsetTask = ReadFromOffsetAsync(startOffset);

                gate.SetResult();
                await Task.WhenAll(fromStartTask, fromOffsetTask);

                Assert.That(fromStartTask.Result, Is.EqualTo(payload));
                Assert.That(fromOffsetTask.Result, Is.EqualTo(secondExpected));
                Assert.That(getRequestCount(), Is.EqualTo(1));
            }
            finally
            {
                await host.StopAsync();
                host.Dispose();
            }
        }

        private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            return memoryStream.ToArray();
        }
    }
}
