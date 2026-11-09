using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.UserData;

/// <summary>Part of the spot balance was locked or unlocked by another system, e.g. as margin collateral: externalLockUpdate.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct SpotExternalLockUpdateModel
{
    /// <summary>Asset name, e.g. BTC.</summary>
    public Symbol Asset;

    /// <summary>Event time, milliseconds since the Unix epoch.</summary>
    public long EventTime;

    /// <summary>Transaction time, milliseconds since the Unix epoch.</summary>
    public long TransactionTime;

    /// <summary>Amount locked or unlocked.</summary>
    public double Delta;
}
