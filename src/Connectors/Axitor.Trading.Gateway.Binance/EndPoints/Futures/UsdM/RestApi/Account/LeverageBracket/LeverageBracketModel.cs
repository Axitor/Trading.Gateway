using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.LeverageBracket;

/// <summary>Notional and Leverage Brackets Response Model.</summary>
public struct LeverageBracketModel
{
    /// <summary>One entry per symbol. Empty rather than null when there are none.</summary>
    public SymbolBrackets[] Symbols;
}

/// <summary>Notional brackets of one symbol, lowest notional first.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct SymbolBrackets
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Brackets of the symbol. Empty rather than null when there are none.</summary>
    public LeverageBracketLevel[] Brackets;

    /// <summary>Multiplier of the account's brackets for the symbol. 1 when the exchange did not adjust them.</summary>
    public double NotionalCoef;
}

/// <summary>One notional range with its highest initial leverage and maintenance margin ratio.</summary>
public struct LeverageBracketLevel
{
    /// <summary>Lowest notional of the range.</summary>
    public double NotionalFloor;

    /// <summary>Highest notional of the range.</summary>
    public double NotionalCap;

    /// <summary>Maintenance margin ratio of the range.</summary>
    public double MaintMarginRatio;

    /// <summary>Maintenance amount: maintenance margin is notional × MaintMarginRatio − Cum.</summary>
    public double Cum;

    /// <summary>Bracket number, from 1.</summary>
    public int Bracket;

    /// <summary>Highest initial leverage of the range.</summary>
    public int InitialLeverage;
}
