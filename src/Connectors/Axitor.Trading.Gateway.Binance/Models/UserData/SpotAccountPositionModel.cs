using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.UserData;

/// <summary>Spot balances changed: outboundAccountPosition. Its balances come as a span in the same call.</summary>
public struct SpotAccountPositionHeader
{
    /// <summary>Event time, milliseconds since the Unix epoch.</summary>
    public long EventTime;

    /// <summary>Time of the account change, milliseconds since the Unix epoch.</summary>
    public long LastUpdateTime;
}

/// <summary>Balance of one asset after the change. Only assets the change may have touched are sent.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct SpotAccountBalance
{
    /// <summary>Asset name, e.g. BTC.</summary>
    public Symbol Asset;

    /// <summary>Free balance.</summary>
    public double Free;

    /// <summary>Locked balance.</summary>
    public double Locked;
}
