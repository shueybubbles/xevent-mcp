using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Bubbles.XEvent.MCPServer.Services
{
    /// <summary>
    /// Provides an interface for a URL stream provider, which is responsible for providing a stream of data from a specified URL.
    /// This interface can be implemented to support various protocols and data sources, allowing for flexible and extensible data streaming capabilities.
    /// </summary>
    public interface IUrlStreamProvider
    {
        /// <summary>
        /// Gets a stream of data from the specified URL, starting at the given byte offset.
        /// </summary>
        /// <param name="url">The URL to retrieve the stream from.</param>
        /// <param name="byteOffset">The byte offset to start reading from.</param>
        /// <returns>A stream of data from the specified URL.</returns>
        Task<Stream> GetStream(string url, long byteOffset = 0);
    }

    /// <summary>
    /// A concrete implementation of the IUrlStreamProvider interface that provides a stream of data from a specified URL.
    /// Uses a single reader per URL and serves all clients from a shared in-memory buffer.
    /// </summary>
    public class UrlStreamProvider : IUrlStreamProvider
    {
        private static readonly HttpClient defaultHttpClient = new();
        private readonly HttpClient httpClient;
        private readonly ConcurrentDictionary<string, Lazy<UrlBufferDownload>> downloads = new(StringComparer.Ordinal);

        public UrlStreamProvider()
            : this(defaultHttpClient)
        {
        }

        public UrlStreamProvider(HttpClient httpClient)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public Task<Stream> GetStream(string url, long byteOffset = 0)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("A URL is required.", nameof(url));
            }

            if (byteOffset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteOffset), "byteOffset must be >= 0.");
            }

            var normalizedUrl = url.Trim();
            var lazyDownload = downloads.GetOrAdd(
                normalizedUrl,
                key => new Lazy<UrlBufferDownload>(
                    () => StartDownload(key),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            UrlBufferDownload download;
            try
            {
                download = lazyDownload.Value;
            }
            catch
            {
                downloads.TryRemove(new KeyValuePair<string, Lazy<UrlBufferDownload>>(normalizedUrl, lazyDownload));
                throw;
            }

            Stream stream = new BufferedUrlStream(download.Buffer, byteOffset);
            return Task.FromResult(stream);
        }

        private UrlBufferDownload StartDownload(string url)
        {
            var sharedBuffer = new SharedUrlBuffer();
            var producerTask = FillBufferAsync(url, sharedBuffer);

            _ = producerTask.ContinueWith(
                _ =>
                {
                    downloads.TryRemove(url, out Lazy<UrlBufferDownload>? _);
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return new UrlBufferDownload(sharedBuffer, producerTask);
        }

        private async Task FillBufferAsync(string url, SharedUrlBuffer sharedBuffer)
        {
            try
            {
                using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                sharedBuffer.SetExpectedLength(response.Content.Headers.ContentLength);

                await using var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var tempBuffer = new byte[64 * 1024];

                while (true)
                {
                    var bytesRead = await responseStream.ReadAsync(tempBuffer).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    var chunk = new byte[bytesRead];
                    Buffer.BlockCopy(tempBuffer, 0, chunk, 0, bytesRead);
                    sharedBuffer.Append(chunk);
                }

                sharedBuffer.Complete();
            }
            catch (Exception ex)
            {
                sharedBuffer.Fail(ex);
                throw;
            }
        }

        private sealed class UrlBufferDownload(UrlStreamProvider.SharedUrlBuffer buffer, Task producerTask)
        {
            public SharedUrlBuffer Buffer { get; } = buffer;
            public Task ProducerTask { get; } = producerTask;
        }

        private sealed class SharedUrlBuffer
        {
            private readonly object sync = new();
            private readonly List<byte[]> chunks = new();
            private readonly List<long> chunkOffsets = new();
            private TaskCompletionSource<bool> dataChanged = CreateSignal();
            private long totalBytes;
            private long? expectedLength;
            private bool completed;
            private ExceptionDispatchInfo? failure;

            public bool IsCompleted
            {
                get
                {
                    lock (sync)
                    {
                        return completed;
                    }
                }
            }

            public void Append(byte[] chunk)
            {
                lock (sync)
                {
                    if (completed || failure is not null)
                    {
                        return;
                    }

                    chunkOffsets.Add(totalBytes);
                    chunks.Add(chunk);
                    totalBytes += chunk.LongLength;
                    SignalDataChanged();
                }
            }

            public void Complete()
            {
                lock (sync)
                {
                    if (completed)
                    {
                        return;
                    }

                    completed = true;
                    SignalDataChanged();
                }
            }

            public void SetExpectedLength(long? length)
            {
                if (length is null)
                {
                    return;
                }

                lock (sync)
                {
                    if (length >= 0)
                    {
                        expectedLength = length;
                        SignalDataChanged();
                    }
                }
            }

            public void Fail(Exception exception)
            {
                lock (sync)
                {
                    if (failure is not null)
                    {
                        return;
                    }

                    failure = ExceptionDispatchInfo.Capture(exception);
                    completed = true;
                    SignalDataChanged();
                }
            }

            public int ReadAvailable(long offset, Span<byte> destination)
            {
                lock (sync)
                {
                    if (offset < 0 || destination.Length == 0 || offset >= totalBytes)
                    {
                        return 0;
                    }

                    var bytesAvailable = totalBytes - offset;
                    var bytesToCopy = (int)Math.Min(bytesAvailable, destination.Length);

                    var destinationOffset = 0;
                    var readPosition = offset;

                    for (var i = 0; i < chunks.Count && destinationOffset < bytesToCopy; i++)
                    {
                        var chunkStart = chunkOffsets[i];
                        var chunk = chunks[i];
                        var chunkEnd = chunkStart + chunk.LongLength;

                        if (readPosition >= chunkEnd)
                        {
                            continue;
                        }

                        var sourceOffset = readPosition <= chunkStart
                            ? 0
                            : (int)(readPosition - chunkStart);

                        var copyCount = Math.Min(chunk.Length - sourceOffset, bytesToCopy - destinationOffset);
                        chunk.AsSpan(sourceOffset, copyCount).CopyTo(destination.Slice(destinationOffset, copyCount));

                        destinationOffset += copyCount;
                        readPosition += copyCount;
                    }

                    return destinationOffset;
                }
            }

            public async ValueTask WaitForDataAsync(long requiredBytes, CancellationToken cancellationToken)
            {
                while (true)
                {
                    Task signalTask;

                    lock (sync)
                    {
                        failure?.Throw();

                        if (totalBytes >= requiredBytes || completed)
                        {
                            return;
                        }

                        signalTask = dataChanged.Task;
                    }

                    await signalTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            public void ThrowIfFailed()
            {
                lock (sync)
                {
                    failure?.Throw();
                }
            }

            public long GetLength()
            {
                while (true)
                {
                    Task signalTask;

                    lock (sync)
                    {
                        failure?.Throw();

                        if (expectedLength is long knownLength)
                        {
                            return knownLength;
                        }

                        if (completed)
                        {
                            return totalBytes;
                        }

                        signalTask = dataChanged.Task;
                    }

                    signalTask.GetAwaiter().GetResult();
                }
            }

            private static TaskCompletionSource<bool> CreateSignal()
                => new(TaskCreationOptions.RunContinuationsAsynchronously);

            private void SignalDataChanged()
            {
                var signal = dataChanged;
                dataChanged = CreateSignal();
                signal.TrySetResult(true);
            }
        }

        private sealed class BufferedUrlStream(UrlStreamProvider.SharedUrlBuffer sharedBuffer, long startOffset) : Stream
        {
            private bool disposed;

            public override bool CanRead => !disposed;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length
            {
                get
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    return sharedBuffer.GetLength();
                }
            }

            public override long Position
            {
                get => startOffset;
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                ObjectDisposedException.ThrowIf(disposed, this);

                if (buffer.Length == 0)
                {
                    return 0;
                }

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var bytesRead = sharedBuffer.ReadAvailable(startOffset, buffer.Span);
                    if (bytesRead > 0)
                    {
                        startOffset += bytesRead;
                        return bytesRead;
                    }

                    if (sharedBuffer.IsCompleted)
                    {
                        sharedBuffer.ThrowIfFailed();
                        return 0;
                    }

                    await sharedBuffer.WaitForDataAsync(startOffset + 1, cancellationToken).ConfigureAwait(false);
                }
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override void Flush() { }

            protected override void Dispose(bool disposing)
            {
                disposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
