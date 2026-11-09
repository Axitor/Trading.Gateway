using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Price a stop order is triggered by.</summary>
[SuppressMessage("Naming", "CA1707", Justification = "Binance identifiers are spelled as the exchange spells them.")]
public enum WorkingType : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>Mark price.</summary>
    MARK_PRICE,

    /// <summary>Last price.</summary>
    CONTRACT_PRICE,
}
