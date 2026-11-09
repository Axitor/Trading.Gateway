using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Type of an order, as the exchange reports it.</summary>
[SuppressMessage("Naming", "CA1707", Justification = "Binance identifiers are spelled as the exchange spells them.")]
public enum OrderType : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>Limit.</summary>
    LIMIT,

    /// <summary>Market.</summary>
    MARKET,

    /// <summary>Stop limit.</summary>
    STOP,

    /// <summary>Stop market.</summary>
    STOP_MARKET,

    /// <summary>Take profit limit.</summary>
    TAKE_PROFIT,

    /// <summary>Take profit market.</summary>
    TAKE_PROFIT_MARKET,

    /// <summary>Trailing stop market.</summary>
    TRAILING_STOP_MARKET,

    /// <summary>Liquidation order placed by the exchange.</summary>
    LIQUIDATION,
}
