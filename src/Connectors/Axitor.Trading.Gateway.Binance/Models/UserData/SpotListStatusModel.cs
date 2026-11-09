using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.UserData;

/// <summary>An order list changed: listStatus. Comes with the executionReport of its orders; the orders come as a span in the same call.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct SpotListStatusHeader
{
    /// <summary>Why the list action failed, as the exchange words it. Null when it did not.</summary>
    public string? RejectReason;

    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>List client order id. Empty when it does not fit, which is reported to telemetry.</summary>
    public ClientOrderId ListClientOrderId;

    /// <summary>Order list id.</summary>
    public long OrderListId;

    /// <summary>Event time, milliseconds since the Unix epoch.</summary>
    public long EventTime;

    /// <summary>Transaction time, milliseconds since the Unix epoch.</summary>
    public long TransactionTime;

    /// <summary>Kind of the list.</summary>
    public SpotContingencyType ContingencyType;

    /// <summary>What this update is about.</summary>
    public SpotListStatusType ListStatusType;

    /// <summary>Status of the list.</summary>
    public SpotListOrderStatus ListOrderStatus;
}

/// <summary>One order of the list.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct SpotListStatusOrder
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Client order id. Empty when it does not fit, which is reported to telemetry.</summary>
    public ClientOrderId ClientOrderId;

    /// <summary>Order id.</summary>
    public long OrderId;
}
