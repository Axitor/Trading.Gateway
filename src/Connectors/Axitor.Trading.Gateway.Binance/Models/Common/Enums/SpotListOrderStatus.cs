using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Status of a spot order list.</summary>
[SuppressMessage("Naming", "CA1707", Justification = "Binance identifiers are spelled as the exchange spells them.")]
public enum SpotListOrderStatus : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>The list was placed or its status changed.</summary>
    EXECUTING,

    /// <summary>The list finished and is no longer active.</summary>
    ALL_DONE,

    /// <summary>Answers a failed placement or cancel.</summary>
    REJECT,
}
