using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Status of a spot order after this update.</summary>
[SuppressMessage("Naming", "CA1707", Justification = "Binance identifiers are spelled as the exchange spells them.")]
public enum SpotOrderStatus : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>Accepted by the engine.</summary>
    NEW,

    /// <summary>Waits until the working order of its order list is filled.</summary>
    PENDING_NEW,

    /// <summary>Filled in part.</summary>
    PARTIALLY_FILLED,

    /// <summary>Filled in full.</summary>
    FILLED,

    /// <summary>Canceled by the user.</summary>
    CANCELED,

    /// <summary>Not used by the exchange now.</summary>
    PENDING_CANCEL,

    /// <summary>Not accepted by the engine.</summary>
    REJECTED,

    /// <summary>Canceled by the order type's rules or by the exchange, e.g. an IOC order not filled in full.</summary>
    EXPIRED,

    /// <summary>Expired by self-trade prevention.</summary>
    EXPIRED_IN_MATCH,
}
