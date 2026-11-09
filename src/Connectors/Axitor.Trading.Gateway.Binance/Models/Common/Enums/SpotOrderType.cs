using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Type of a spot order, as the exchange reports it.</summary>
[SuppressMessage("Naming", "CA1707", Justification = "Binance identifiers are spelled as the exchange spells them.")]
public enum SpotOrderType : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>Limit.</summary>
    LIMIT,

    /// <summary>Market.</summary>
    MARKET,

    /// <summary>Market order placed once the stop price is reached.</summary>
    STOP_LOSS,

    /// <summary>Limit order placed once the stop price is reached.</summary>
    STOP_LOSS_LIMIT,

    /// <summary>Market order placed once the take profit price is reached.</summary>
    TAKE_PROFIT,

    /// <summary>Limit order placed once the take profit price is reached.</summary>
    TAKE_PROFIT_LIMIT,

    /// <summary>Post only limit: rejected if it would take liquidity.</summary>
    LIMIT_MAKER,
}
