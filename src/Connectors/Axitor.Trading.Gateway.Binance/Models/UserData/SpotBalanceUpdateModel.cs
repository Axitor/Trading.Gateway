using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.UserData;

/// <summary>A deposit, a withdrawal or a transfer between wallets: balanceUpdate.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct SpotBalanceUpdateModel
{
    /// <summary>Asset name, e.g. BTC.</summary>
    public Symbol Asset;

    /// <summary>Event time, milliseconds since the Unix epoch.</summary>
    public long EventTime;

    /// <summary>Clear time, milliseconds since the Unix epoch.</summary>
    public long ClearTime;

    /// <summary>Balance change. A delta: adding it twice counts the change twice.</summary>
    public double Delta;
}
