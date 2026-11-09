using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>What happened to a spot order in this update.</summary>
[SuppressMessage("Naming", "CA1707", Justification = "Binance identifiers are spelled as the exchange spells them.")]
public enum SpotExecutionType : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>Accepted by the engine.</summary>
    NEW,

    /// <summary>Canceled by the user.</summary>
    CANCELED,

    /// <summary>Amended.</summary>
    REPLACED,

    /// <summary>Rejected, e.g. the new order of a cancel-replace whose cancel went through.</summary>
    REJECTED,

    /// <summary>Filled, fully or in part.</summary>
    TRADE,

    /// <summary>Canceled by the order type's rules or by the exchange.</summary>
    EXPIRED,

    /// <summary>Expired by self-trade prevention.</summary>
    TRADE_PREVENTION,
}
