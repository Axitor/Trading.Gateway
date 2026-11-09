using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Correlation;

/// <summary>Requests in flight on the connections of one role, matched to responses by id.</summary>
internal sealed class WsApiPendingTable
{
    private readonly WsApiPendingSlot[] _slots;
    private readonly long _mask;
    private long _lastRequestId;

    /// <summary>Creates a table. Capacity must be a power of two.</summary>
    public WsApiPendingTable(int capacity, int slotBufferBytes)
    {
        _slots = new WsApiPendingSlot[capacity];

        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new WsApiPendingSlot(slotBufferBytes);
        }

        _mask = capacity - 1;
    }

    /// <summary>Takes a free slot and a fresh id for a request on the given connection. False when every slot is busy.</summary>
    public bool TryAcquire(int connectionEpoch, [NotNullWhen(true)] out WsApiPendingSlot? slot, out long requestId)
    {
        // Ids that land on a busy slot are skipped; they never reach the wire.
        for (int attempt = 0; attempt < _slots.Length; attempt++)
        {
            long id = Interlocked.Increment(ref _lastRequestId);
            WsApiPendingSlot candidate = _slots[(int)(id & _mask)];

            if (candidate.TryAcquire())
            {
                candidate.Publish(id, connectionEpoch);
                slot = candidate;
                requestId = id;
                return true;
            }
        }

        slot = null;
        requestId = 0;
        return false;
    }

    /// <summary>Hands a response to the request waiting for it. False for an unknown or late id. Runs on the receive loop.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryComplete(long requestId, ReadOnlySpan<byte> message)
    {
        // Zero means the response carried no id; it would match a free slot.
        if (requestId <= 0)
        {
            return false;
        }

        return _slots[(int)(requestId & _mask)].TryComplete(requestId, message);
    }

    /// <summary>Times out requests whose deadline has passed.</summary>
    public void Sweep(long nowTicks)
    {
        WsApiPendingSlot[] slots = _slots;

        for (int i = 0; i < slots.Length; i++)
        {
            WsApiPendingSlot slot = slots[i];
            long id = slot.PendingId;

            if (id == 0 || nowTicks < slot.DeadlineTicks)
            {
                continue;
            }

            slot.TryFail(id, WsApiPendingOutcome.Timeout, WsDisconnectReason.None);
        }
    }

    /// <summary>Ends every request sent on a connection that has gone.</summary>
    public void FailConnection(int connectionEpoch, WsDisconnectReason reason)
    {
        WsApiPendingOutcome outcome = GetLostOutcome(reason);
        WsApiPendingSlot[] slots = _slots;

        for (int i = 0; i < slots.Length; i++)
        {
            WsApiPendingSlot slot = slots[i];
            long id = slot.PendingId;

            if (id == 0 || slot.ConnectionEpoch != connectionEpoch)
            {
                continue;
            }

            slot.TryFail(id, outcome, reason);
        }
    }

    /// <summary>Ends every request in flight, e.g. when the transport stops.</summary>
    public void FailAll(WsDisconnectReason reason)
    {
        WsApiPendingOutcome outcome = GetLostOutcome(reason);
        WsApiPendingSlot[] slots = _slots;

        for (int i = 0; i < slots.Length; i++)
        {
            WsApiPendingSlot slot = slots[i];
            long id = slot.PendingId;

            if (id == 0)
            {
                continue;
            }

            slot.TryFail(id, outcome, reason);
        }
    }

    private static WsApiPendingOutcome GetLostOutcome(WsDisconnectReason reason) =>
        reason == WsDisconnectReason.AppShutdown ? WsApiPendingOutcome.AppShutdown : WsApiPendingOutcome.ConnectionLost;
}
