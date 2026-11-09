using System.Runtime.CompilerServices;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Connection;

/// <summary>Contiguous buffer for one incoming message. Grows on demand up to a ceiling and never shrinks.</summary>
internal sealed class WsReceiveBuffer
{
    private byte[] _buffer;
    private int _length;
    private readonly int _maxBytes;

    /// <summary>Creates a buffer with an initial size and a ceiling.</summary>
    public WsReceiveBuffer(int initialBytes, int maxBytes)
    {
        // PERF: pinned heap. The socket pins this buffer for every pending receive, and it lives as long as the connection.
        _buffer = GC.AllocateUninitializedArray<byte>(initialBytes, pinned: true);
        _maxBytes = maxBytes;
    }

    /// <summary>The message received so far.</summary>
    public ReadOnlySpan<byte> Written
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.AsSpan(0, _length);
    }

    /// <summary>Starts a new message.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset() => _length = 0;

    /// <summary>Free space for the next read. Grows the buffer when full; false once the ceiling is reached.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetFreeMemory(out Memory<byte> free)
    {
        if (_length < _buffer.Length)
        {
            free = _buffer.AsMemory(_length);
            return true;
        }

        return TryGrow(out free);
    }

    /// <summary>Commits the bytes written by the last read.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count) => _length += count;

    /// <summary>Cold path. Doubles the buffer up to the ceiling, keeping the bytes already received.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryGrow(out Memory<byte> free)
    {
        if (_maxBytes <= _buffer.Length)
        {
            free = default;
            return false;
        }

        int newSize = (int)Math.Min((long)_buffer.Length * 2, _maxBytes);
        byte[] larger = GC.AllocateUninitializedArray<byte>(newSize, pinned: true);
        _buffer.AsSpan(0, _length).CopyTo(larger);
        _buffer = larger;

        free = _buffer.AsMemory(_length);
        return true;
    }
}
