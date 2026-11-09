using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Status of an order after this update.</summary>
[SuppressMessage("Naming", "CA1707", Justification = "Binance identifiers are spelled as the exchange spells them.")]
public enum OrderStatus : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>Open, nothing filled.</summary>
    NEW,

    /// <summary>Open, filled in part.</summary>
    PARTIALLY_FILLED,

    /// <summary>Filled in full.</summary>
    FILLED,

    /// <summary>Canceled.</summary>
    CANCELED,

    /// <summary>Expired.</summary>
    EXPIRED,

    /// <summary>Expired by self-trade prevention.</summary>
    EXPIRED_IN_MATCH,

    /// <summary>Rejected by the exchange.</summary>
    REJECTED,
}
