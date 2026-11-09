using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;
using Axitor.Trading.Gateway.Core.Time;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Correlation;

/// <summary>One request awaiting its response. Order of use: acquire, publish, send, arm the deadline, await, read Message, release.</summary>
internal sealed class WsApiPendingSlot : IValueTaskSource<WsApiPendingCompletion>
{
    // The caller holds the slot until it has read the response, the completer until SetResult returns.
    private const int OwnersPerRequest = 2;

    private ManualResetValueTaskSourceCore<WsApiPendingCompletion> _source;
    private byte[] _buffer;
    private int _length;
    private int _owners;
    private int _connectionEpoch;
    private long _pendingId;
    private long _deadlineTicks;
    private long _sentTicks;
    private long _receivedTicks;

    /// <summary>Creates a free slot with an initial response buffer.</summary>
    public WsApiPendingSlot(int bufferBytes)
    {
        // The caller parses the response in its continuation, which must not run on the receive loop.
        _source.RunContinuationsAsynchronously = true;
        _buffer = GC.AllocateUninitializedArray<byte>(bufferBytes);
    }

    /// <summary>The id awaiting a response, or zero.</summary>
    public long PendingId
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _pendingId);
    }

    /// <summary>When the request times out, in monotonic ticks. MaxValue until it is on the wire.</summary>
    public long DeadlineTicks => Volatile.Read(ref _deadlineTicks);

    /// <summary>When the frame was handed to the socket, in monotonic ticks. Zero if it never was.</summary>
    public long SentTicks => _sentTicks;

    /// <summary>When the response arrived, in monotonic ticks. Zero without a response. Valid once the wait has completed.</summary>
    public long ReceivedTicks => _receivedTicks;

    /// <summary>The connection the request was sent on.</summary>
    public int ConnectionEpoch => Volatile.Read(ref _connectionEpoch);

    /// <summary>The response. Valid until Release.</summary>
    public ReadOnlySpan<byte> Message => _buffer.AsSpan(0, _length);

    /// <summary>Takes the slot if both sides of the previous request have let it go.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAcquire() => Interlocked.CompareExchange(ref _owners, OwnersPerRequest, 0) == 0;

    /// <summary>Makes the slot wait for a response with this id.</summary>
    public void Publish(long requestId, int connectionEpoch)
    {
        _length = 0;
        _sentTicks = 0;
        _receivedTicks = 0;
        _source.Reset();
        _connectionEpoch = connectionEpoch;
        _deadlineTicks = long.MaxValue;

        // Last: from here on a response with this id can complete the slot.
        Volatile.Write(ref _pendingId, requestId);
    }

    /// <summary>Completes once, when any side ends the wait. Must always be awaited.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<WsApiPendingCompletion> WaitAsync() => new(this, _source.Version);

    /// <summary>Records when the frame was handed to the socket. Called under the send lock, once the frame is written.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MarkSent(long sentTicks) => _sentTicks = sentTicks;

    /// <summary>Starts the response timeout. Call once the request is on the wire.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ArmDeadline(long deadlineTicks) => Volatile.Write(ref _deadlineTicks, deadlineTicks);

    /// <summary>Copies the response in and wakes the caller. False when this id is no longer waiting.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryComplete(long requestId, ReadOnlySpan<byte> message)
    {
        // Plain read first: a late id should not take the cache line for a CAS that fails.
        if (Volatile.Read(ref _pendingId) != requestId || Interlocked.CompareExchange(ref _pendingId, 0, requestId) != requestId)
        {
            return false;
        }

        // Taken before the copy, so network time does not include it.
        _receivedTicks = SysTime.GetTimestamp();

        if (_buffer.Length < message.Length)
        {
            Grow(message.Length);
        }

        message.CopyTo(_buffer);
        _length = message.Length;

        Complete(WsApiPendingOutcome.Response, WsDisconnectReason.None);
        return true;
    }

    /// <summary>Ends the wait without a response. False when this id is no longer waiting.</summary>
    public bool TryFail(long requestId, WsApiPendingOutcome outcome, WsDisconnectReason reason)
    {
        if (Interlocked.CompareExchange(ref _pendingId, 0, requestId) != requestId)
        {
            return false;
        }

        Complete(outcome, reason);
        return true;
    }

    /// <summary>The caller is done with Message.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release() => Interlocked.Decrement(ref _owners);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Complete(WsApiPendingOutcome outcome, WsDisconnectReason reason)
    {
        _source.SetResult(new WsApiPendingCompletion() { Outcome = outcome, Reason = reason });
        Interlocked.Decrement(ref _owners);
    }

    /// <summary>Cold path. Enlarges the buffer for a response bigger than any before; never shrinks.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int required)
    {
        uint rounded = BitOperations.RoundUpToPowerOf2((uint)required);
        _buffer = GC.AllocateUninitializedArray<byte>(rounded <= (uint)Array.MaxLength ? (int)rounded : required);
    }

    WsApiPendingCompletion IValueTaskSource<WsApiPendingCompletion>.GetResult(short token) => _source.GetResult(token);

    ValueTaskSourceStatus IValueTaskSource<WsApiPendingCompletion>.GetStatus(short token) => _source.GetStatus(token);

    void IValueTaskSource<WsApiPendingCompletion>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
        _source.OnCompleted(continuation, state, token, flags);
}
