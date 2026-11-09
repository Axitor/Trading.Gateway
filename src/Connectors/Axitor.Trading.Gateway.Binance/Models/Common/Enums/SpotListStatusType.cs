using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>What a spot order list update is about.</summary>
[SuppressMessage("Naming", "CA1707", Justification = "Binance identifiers are spelled as the exchange spells them.")]
public enum SpotListStatusType : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>Answers a failed action, e.g. a list placement or cancel.</summary>
    RESPONSE,

    /// <summary>The list was placed or its status changed.</summary>
    EXEC_STARTED,

    /// <summary>The client order id of an order in the list changed.</summary>
    UPDATED,

    /// <summary>The list finished and is no longer active.</summary>
    ALL_DONE,
}
