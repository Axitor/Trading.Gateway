namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Kind of a spot order list.</summary>
public enum SpotContingencyType : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>One cancels the other.</summary>
    OCO,

    /// <summary>One triggers the other.</summary>
    OTO,
}
