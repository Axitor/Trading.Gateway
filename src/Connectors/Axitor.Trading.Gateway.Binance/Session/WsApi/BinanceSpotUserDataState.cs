using System.Diagnostics.CodeAnalysis;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Core.Time;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Deduplication;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Session.WsApi;

/// <summary>State a spot user data session shares with its dispatcher.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: The duplicate filter and the buffers are touched by the dispatcher only, under the dispatch lock of the transport.
/// </para>
/// <para>
/// DESIGN NOTE: Handler calls come from the receive loop, the lane supervisor and the session, so all of them take HandlerSync.
/// Locks are taken in one order only: the dispatch lock, then HandlerSync. The session never calls the transport while holding HandlerSync.
/// </para>
/// <para>
/// DESIGN NOTE: Wanted changes under HandlerSync and is checked there before every handler call, so no event reaches the handler once unsubscribe starts.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001", Justification = "The semaphore holds no unmanaged state without a wait handle; disposing it would make a late Release throw.")]
internal sealed class BinanceSpotUserDataState
{
    // A copy from the other connection comes within a rotation overlap, so a few hundred events are plenty.
    private const int DuplicateSlots = 256;
    private const int DuplicateSlotBytes = 2 * 1024;

    // Starting size of the reused buffers; they double when an event has more items.
    private const int InitialItems = 16;

    private readonly SemaphoreSlim _wake = new(0);
    private int _wanted;
    private int _subscribedEpoch;
    private int _needsResubscribe;

    // Touched under HandlerSync only.
    private bool _gapOpen;
    private bool _fatalReported;
    private long _gapStartMillis;
    private WsDisconnectReason _gapReason;

    public BinanceSpotUserDataState()
    {
        Duplicates = new WsStreamDuplicateFilter(DuplicateSlots, DuplicateSlotBytes);
        Balances = new SpotAccountBalance[InitialItems];
        ListOrders = new SpotListStatusOrder[InitialItems];
    }

    /// <summary>Taken around every handler call.</summary>
    public Lock HandlerSync { get; } = new();

    /// <summary>Copies of events from an overlapping rotation.</summary>
    public WsStreamDuplicateFilter Duplicates { get; }

    /// <summary>Reused balance buffer of outboundAccountPosition.</summary>
    public SpotAccountBalance[] Balances { get; set; }

    /// <summary>Reused order buffer of listStatus.</summary>
    public SpotListStatusOrder[] ListOrders { get; set; }

    /// <summary>Whether the user is subscribed.</summary>
    public bool IsWanted => Volatile.Read(ref _wanted) == 1;

    /// <summary>Epoch of the connection holding our subscription, zero when none.</summary>
    public int SubscribedEpoch
    {
        get => Volatile.Read(ref _subscribedEpoch);
        set => Volatile.Write(ref _subscribedEpoch, value);
    }

    /// <summary>Whether the current connection must be subscribed again.</summary>
    public bool NeedsResubscribe => Volatile.Read(ref _needsResubscribe) == 1;

    /// <summary>The user subscribes. Call under HandlerSync.</summary>
    public void SetWanted() => Volatile.Write(ref _wanted, 1);

    /// <summary>The user unsubscribes: events stop and an open gap is dropped silently. Call under HandlerSync.</summary>
    public void ClearWanted()
    {
        Volatile.Write(ref _wanted, 0);
        _gapOpen = false;
    }

    /// <summary>Clears the subscribed epoch if it is the given one. True for the call that cleared it.</summary>
    public bool TryEndSubscription(int epoch) => epoch != 0 && Interlocked.CompareExchange(ref _subscribedEpoch, 0, epoch) == epoch;

    /// <summary>Asks the session to subscribe the current connection again and wakes its loop.</summary>
    public void RequestResubscribe()
    {
        Volatile.Write(ref _needsResubscribe, 1);
        _wake.Release();
    }

    /// <summary>The current connection is subscribed.</summary>
    public void ClearResubscribe() => Volatile.Write(ref _needsResubscribe, 0);

    /// <summary>Waits for a signal or the timeout. True when signaled.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _wake.WaitAsync(timeout, cancellationToken);

    /// <summary>Opens a gap. True only for the call that opened it. Call under HandlerSync.</summary>
    public bool TryOpenGap(WsDisconnectReason reason)
    {
        if (_gapOpen)
        {
            return false;
        }

        _gapOpen = true;
        _gapReason = reason;
        _gapStartMillis = SysTime.UtcMillis();
        return true;
    }

    /// <summary>Closes the open gap from the earlier of its start and the hint. False when none is open. Call under HandlerSync.</summary>
    public bool TryCloseGap(long startHintMillis, out WsStreamGap gap)
    {
        if (!_gapOpen)
        {
            gap = default;
            return false;
        }

        _gapOpen = false;
        gap = new WsStreamGap()
        {
            Reason = _gapReason,
            StartUtcMillis = Math.Min(_gapStartMillis, startHintMillis),
            EndUtcMillis = SysTime.UtcMillis(),
        };

        return true;
    }

    /// <summary>True only for the first fatal report. Call under HandlerSync.</summary>
    public bool TryReportFatal()
    {
        if (_fatalReported)
        {
            return false;
        }

        _fatalReported = true;
        return true;
    }
}
