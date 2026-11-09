namespace Axitor.Trading.Gateway.Tests.Frames;

/// <summary>REST response bodies as the exchange sends them.</summary>
internal static class RestBodies
{
    public static ReadOnlySpan<byte> FuturesOrderBook =>
        """{"lastUpdateId":1027024,"E":1589436922972,"T":1589436922959,"bids":[["4.00000000","431.00000000"]],"asks":[["4.00000200","12.00000000"]]}"""u8;

    /// <summary>COIN-M opens with its symbol and pair, in the order the live API writes them.</summary>
    public static ReadOnlySpan<byte> CoinMOrderBook =>
        """{"symbol":"BTCUSD_PERP","pair":"BTCUSD","lastUpdateId":16769853,"E":1591250106370,"T":1591250106368,"bids":[["9638.0","431"]],"asks":[["9638.2","12"]]}"""u8;

    /// <summary>Spot sends no event or transaction time.</summary>
    public static ReadOnlySpan<byte> SpotOrderBook =>
        """{"lastUpdateId":1027024,"bids":[["4.00000000","431.00000000"]],"asks":[["4.00000200","12.00000000"]]}"""u8;

    /// <summary>Two candles; each row ends with the unused "ignore" field.</summary>
    public static ReadOnlySpan<byte> Klines =>
        """[[1499040000000,"0.01634790","0.80000000","0.01575800","0.01577100","148976.11427815",1499644799999,"2434.19055334",308,"1756.87402397","28.46694368","0"],[1499644800000,"0.01577100","0.01600000","0.01570000","0.01590000","1000.5",1500249599999,"15.9",42,"500.25","7.95","0"]]"""u8;

    public static ReadOnlySpan<byte> ServerTime =>
        """{"serverTime":1499827319559}"""u8;

    /// <summary>USD-M answer to a new order.</summary>
    public static ReadOnlySpan<byte> FuturesOrder =>
        """{"clientOrderId":"testOrder","cumQty":"0","cumQuote":"0","executedQty":"0","orderId":22542179,"avgPrice":"0.00000","origQty":"10","price":"0","reduceOnly":false,"side":"BUY","positionSide":"SHORT","status":"NEW","stopPrice":"9300","closePosition":false,"symbol":"BTCUSDT","timeInForce":"GTD","type":"TRAILING_STOP_MARKET","origType":"TRAILING_STOP_MARKET","activatePrice":"9020","priceRate":"0.3","updateTime":1566818724722,"workingType":"CONTRACT_PRICE","priceProtect":false,"priceMatch":"NONE","selfTradePreventionMode":"NONE","goodTillDate":1693207680000}"""u8;
}
