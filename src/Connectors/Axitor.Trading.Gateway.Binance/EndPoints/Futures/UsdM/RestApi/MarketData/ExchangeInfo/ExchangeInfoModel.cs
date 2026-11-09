using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.ExchangeInfo;

/// <summary>Exchange Information Response Model. Only what orders need.</summary>
public struct ExchangeInfoModel
{
    /// <summary>Listed symbols. Empty rather than null when none are listed.</summary>
    public ExchangeSymbol[] Symbols;
}

/// <summary>Trading rules of one symbol.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct ExchangeSymbol
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Filters to pass with every order of the symbol.</summary>
    public SymbolFilters Filters;

    /// <summary>Trading status.</summary>
    public SymbolStatus Status;
}
