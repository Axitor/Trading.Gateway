using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Why an order expired. Values equal the exchange codes; a code this version does not know is kept as a number and reported to telemetry.</summary>
[SuppressMessage("Naming", "CA1707", Justification = "Binance identifiers are spelled as the exchange spells them.")]
public enum ExpiryReason : byte
{
    /// <summary>Did not expire.</summary>
    NONE = 0,

    /// <summary>Expired by self-trade prevention.</summary>
    SELF_TRADE_PREVENTION = 1,

    /// <summary>IOC order not filled in full; the rest canceled.</summary>
    IOC_UNFILLED = 2,

    /// <summary>IOC order not filled in full due to self-trade prevention; the rest canceled.</summary>
    IOC_SELF_TRADE_PREVENTION = 3,

    /// <summary>Knocked out by a higher priority reduce-only order, or it would open a reversed position.</summary>
    REDUCE_ONLY_KNOCKED_OUT = 4,

    /// <summary>The account was liquidated.</summary>
    LIQUIDATION = 5,

    /// <summary>GTE condition not met.</summary>
    GTE_UNSATISFIED = 6,

    /// <summary>The symbol was delisted.</summary>
    DELISTED = 7,

    /// <summary>The initial order expired after its stop order triggered.</summary>
    STOP_TRIGGERED = 8,

    /// <summary>Market order not filled in full; the rest canceled.</summary>
    MARKET_UNFILLED = 9,

    /// <summary>FOK order could not be filled in full; canceled.</summary>
    FOK_UNFILLED = 10,

    /// <summary>Canceled: the order failed the post-only check.</summary>
    POST_ONLY_REJECTED = 11,
}
