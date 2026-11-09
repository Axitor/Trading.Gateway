namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Margin mode of a position. ACCOUNT_UPDATE spells it cross or isolated, MARGIN_CALL CROSSED or ISOLATED.</summary>
public enum MarginType : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>Isolated margin.</summary>
    ISOLATED,

    /// <summary>Cross margin.</summary>
    CROSS,
}
