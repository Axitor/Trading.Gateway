using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>What expires when an order would trade against another order of the same account.</summary>
[SuppressMessage("Naming", "CA1707", Justification = "Binance identifiers are spelled as the exchange spells them.")]
public enum SelfTradePreventionMode : byte
{
    /// <summary>A value this version does not know.</summary>
    UNKNOWN,

    /// <summary>No self-trade prevention.</summary>
    NONE,

    /// <summary>The taker order expires.</summary>
    EXPIRE_TAKER,

    /// <summary>The maker order expires.</summary>
    EXPIRE_MAKER,

    /// <summary>Both orders expire.</summary>
    EXPIRE_BOTH,
}
