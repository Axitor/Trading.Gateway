using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>What happens when a spot order would trade against an order of the same account or trade group.</summary>
[SuppressMessage("Naming", "CA1707", Justification = "Binance identifiers are spelled as the exchange spells them.")]
public enum SpotSelfTradePreventionMode : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>No self-trade prevention.</summary>
    NONE,

    /// <summary>The maker order expires.</summary>
    EXPIRE_MAKER,

    /// <summary>The taker order expires.</summary>
    EXPIRE_TAKER,

    /// <summary>Both orders expire.</summary>
    EXPIRE_BOTH,

    /// <summary>Both orders are reduced by the quantity that would have traded.</summary>
    DECREMENT,

    /// <summary>The quantity is transferred between the accounts instead of trading.</summary>
    TRANSFER,
}
