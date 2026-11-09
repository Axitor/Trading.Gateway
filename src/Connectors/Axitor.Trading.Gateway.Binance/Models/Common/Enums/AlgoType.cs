namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Type of an algo order.</summary>
public enum AlgoType : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>Conditional order: stop, take profit or trailing stop.</summary>
    CONDITIONAL,
}
