using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Benchmarks.ExchangeStub.Streams;

/// <summary>Connection stream that can hold writes and send them in 64 KB chunks, so a fast replay is not held back by one socket write per frame.</summary>
internal sealed class CoalescingStream : Stream
{
    private const int CapacityBytes = 64 * 1024;

    private readonly Stream _inner;
    private readonly byte[] _buffer = new byte[CapacityBytes];

    [SuppressMessage("Usage", "CA2213", Justification = "No wait handle is ever created; disposing it would make a late Release throw.")]
    private readonly SemaphoreSlim _gate = new(1, 1);

    private int _count;
    private volatile bool _isCoalescing;

    public CoalescingStream(Stream inner) => _inner = inner;

    /// <summary>While true, writes collect in the buffer and go out when it fills or on DrainAsync, and flushes are ignored.</summary>
    public bool IsCoalescing
    {
        get => _isCoalescing;
        set => _isCoalescing = value;
    }

    public override bool CanRead => _inner.CanRead;

    public override bool CanWrite => _inner.CanWrite;

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer, cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!_isCoalescing)
            {
                await WriteBufferedAsync(cancellationToken).ConfigureAwait(false);
                await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (_buffer.Length - _count < buffer.Length)
            {
                await WriteBufferedAsync(cancellationToken).ConfigureAwait(false);

                if (_buffer.Length < buffer.Length)
                {
                    await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            buffer.Span.CopyTo(_buffer.AsSpan(_count));
            _count += buffer.Length;
        }
        finally
        {
            _gate.Release();
        }
    }

    public override void Flush()
    {
        if (!_isCoalescing)
        {
            DrainAsync().GetAwaiter().GetResult();
        }
    }

    // The WebSocket flushes after every frame; while coalescing that would undo the batching.
    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _isCoalescing ? Task.CompletedTask : DrainAsync(cancellationToken);

    /// <summary>Sends what the buffer holds and flushes the connection.</summary>
    public async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await WriteBufferedAsync(cancellationToken).ConfigureAwait(false);
            await _inner.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Writes out the buffer. Call under the gate.</summary>
    private async ValueTask WriteBufferedAsync(CancellationToken cancellationToken)
    {
        if (_count == 0)
        {
            return;
        }

        int count = _count;
        _count = 0;
        await _inner.WriteAsync(_buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
    }
}
