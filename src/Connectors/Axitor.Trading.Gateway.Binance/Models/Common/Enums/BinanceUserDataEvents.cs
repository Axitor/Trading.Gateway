namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>User data events a session subscribes to. Combine flags, e.g. OrderTradeUpdate | AccountUpdate; the exchange sends only these.</summary>
[Flags]
public enum BinanceUserDataEvents : byte
{
    /// <summary>No events.</summary>
    None = 0,

    /// <summary>ORDER_TRADE_UPDATE: an order was created or changed.</summary>
    OrderTradeUpdate = 1 << 0,

    /// <summary>ACCOUNT_UPDATE: balances or positions changed.</summary>
    AccountUpdate = 1 << 1,

    /// <summary>
    /// TRADE_LITE: an order was filled, reported faster and with fewer fields. The fill also comes in ORDER_TRADE_UPDATE: count fills from one of them.
    /// USD-M only.
    /// </summary>
    TradeLite = 1 << 2,

    /// <summary>ALGO_UPDATE: an algo order, e.g. a stop loss or take profit, was created or changed. USD-M only.</summary>
    AlgoUpdate = 1 << 3,

    /// <summary>ACCOUNT_CONFIG_UPDATE: the leverage of a symbol or, on USD-M, Multi-Assets mode changed.</summary>
    AccountConfigUpdate = 1 << 4,

    /// <summary>MARGIN_CALL: position risk is too high.</summary>
    MarginCall = 1 << 5,
}
