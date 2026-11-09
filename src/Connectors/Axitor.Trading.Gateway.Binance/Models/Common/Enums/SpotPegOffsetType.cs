using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>How the offset of a pegged spot order is counted.</summary>
[SuppressMessage("Naming", "CA1707", Justification = "Binance identifiers are spelled as the exchange spells them.")]
public enum SpotPegOffsetType : byte
{
    /// <summary>Not pegged, or a value this version does not know, which is reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>In price levels of the book.</summary>
    PRICE_LEVEL,
}
