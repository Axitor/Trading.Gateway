using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Which best price a pegged spot order follows.</summary>
[SuppressMessage("Naming", "CA1707", Justification = "Binance identifiers are spelled as the exchange spells them.")]
public enum SpotPegPriceType : byte
{
    /// <summary>Not pegged, or a value this version does not know, which is reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>Best price on the same side of the book.</summary>
    PRIMARY_PEG,

    /// <summary>Best price on the other side of the book.</summary>
    MARKET_PEG,
}
