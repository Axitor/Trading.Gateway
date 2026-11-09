namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Where a spot order works. Sent only for orders that may have allocations.</summary>
public enum SpotWorkingFloor : byte
{
    /// <summary>Not sent, or a value this version does not know, which is reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>The exchange order book.</summary>
    EXCHANGE,

    /// <summary>Smart order routing.</summary>
    SOR,
}
