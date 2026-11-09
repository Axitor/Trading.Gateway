using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Price taken from the order book instead of a set price.</summary>
[SuppressMessage("Naming", "CA1707", Justification = "Binance identifiers are spelled as the exchange spells them.")]
public enum PriceMatch : byte
{
    /// <summary>A value this version does not know.</summary>
    UNKNOWN,

    /// <summary>No price match.</summary>
    NONE,

    /// <summary>Best price on the other side.</summary>
    OPPONENT,

    /// <summary>5th best price on the other side.</summary>
    OPPONENT_5,

    /// <summary>10th best price on the other side.</summary>
    OPPONENT_10,

    /// <summary>20th best price on the other side.</summary>
    OPPONENT_20,

    /// <summary>Best price on the same side.</summary>
    QUEUE,

    /// <summary>5th best price on the same side.</summary>
    QUEUE_5,

    /// <summary>10th best price on the same side.</summary>
    QUEUE_10,

    /// <summary>20th best price on the same side.</summary>
    QUEUE_20,
}
