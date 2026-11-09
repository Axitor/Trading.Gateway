using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.SymbolConfig;

/// <summary>Symbol Configuration Response Model.</summary>
public struct SymbolConfigModel
{
    /// <summary>One entry per symbol. Empty rather than null when there are none.</summary>
    public SymbolConfigEntry[] Symbols;
}

/// <summary>Margin type and leverage of one symbol in the account.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct SymbolConfigEntry
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Largest position notional allowed at the current leverage.</summary>
    public double MaxNotionalValue;

    /// <summary>Initial leverage.</summary>
    public int Leverage;

    /// <summary>Margin type.</summary>
    public MarginType MarginType;

    /// <summary>Whether margin is added to an isolated position automatically.</summary>
    public bool IsAutoAddMargin;
}
