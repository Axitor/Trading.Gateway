using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.UserData;

/// <summary>
/// Account configuration changed: ACCOUNT_CONFIG_UPDATE. Either the leverage of a symbol or, on USD-M, Multi-Assets mode.
/// When neither is set, the change is of a kind this version does not know, which is reported to telemetry.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct AccountConfigUpdateModel
{
    /// <summary>Symbol whose leverage changed. Empty for other changes.</summary>
    public Symbol Symbol;

    /// <summary>Event time, milliseconds since the Unix epoch.</summary>
    public long EventTime;

    /// <summary>Transaction time, milliseconds since the Unix epoch.</summary>
    public long TransactionTime;

    /// <summary>New leverage of the symbol. Zero for other changes.</summary>
    public int Leverage;

    /// <summary>Whether Multi-Assets mode changed; the new mode is in IsMultiAssetsMode.</summary>
    public bool IsMultiAssetsModeChange;

    /// <summary>Whether Multi-Assets mode is on. Meaningful only when IsMultiAssetsModeChange is true.</summary>
    public bool IsMultiAssetsMode;
}
