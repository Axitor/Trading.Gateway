namespace Axitor.Trading.Gateway.Tests.Frames;

/// <summary>USD-M and COIN-M stream data as the exchange sends it, without the combined stream envelope.</summary>
internal static class FuturesFrames
{
    public static ReadOnlySpan<byte> BookTicker =>
        """{"e":"bookTicker","u":400900217,"s":"BNBUSDT","ps":"BNBUSDT","b":"25.35190000","B":"31.21000000","a":"25.36520000","A":"40.66000000","T":1568014460891,"E":1568014460893}"""u8;

    /// <summary>USD-M sends nq, which the docs do not describe.</summary>
    public static ReadOnlySpan<byte> AggTrade =>
        """{"e":"aggTrade","E":123456789,"a":5933014,"s":"BTCUSDT","p":"0.001","q":"100","nq":"100","f":100,"l":105,"T":123456785,"m":true}"""u8;

    public static ReadOnlySpan<byte> AggTradeCoinM =>
        """{"e":"aggTrade","E":123456789,"a":5933014,"s":"BTCUSD_PERP","p":"0.001","q":"100","f":100,"l":105,"T":123456785,"m":true}"""u8;

    /// <summary>One bid, two asks: the second ask removes its level.</summary>
    public static ReadOnlySpan<byte> DepthUpdate =>
        """{"e":"depthUpdate","E":123456789,"T":123456788,"s":"BTCUSDT","ps":"BTCUSDT","U":157,"u":160,"pu":149,"b":[["0.0024","10"]],"a":[["0.0026","100"],["0.0027","0"]]}"""u8;

    public static ReadOnlySpan<byte> PartialDepth =>
        """{"e":"depthUpdate","E":1571889248277,"T":1571889248276,"s":"BTCUSDT","ps":"BTCUSDT","U":390497796,"u":390497878,"pu":390497794,"b":[["7403.89","0.002"],["7403.90","3.906"]],"a":[["7405.96","3.340"],["7406.63","4.525"]]}"""u8;

    public static ReadOnlySpan<byte> Kline =>
        """{"e":"kline","E":1638747660000,"s":"BTCUSDT","k":{"t":1638747660000,"T":1638747719999,"s":"BTCUSDT","i":"1m","f":100,"L":200,"o":"0.0010","c":"0.0020","h":"0.0025","l":"0.0015","v":"1000","n":100,"x":false,"q":"1.0000","V":"500","Q":"0.500","B":"123456"}}"""u8;

    /// <summary>The closing push of the same candle, with the same event time.</summary>
    public static ReadOnlySpan<byte> KlineClosed =>
        """{"e":"kline","E":1638747660000,"s":"BTCUSDT","k":{"t":1638747660000,"T":1638747719999,"s":"BTCUSDT","i":"1m","f":100,"L":200,"o":"0.0010","c":"0.0020","h":"0.0025","l":"0.0015","v":"1000","n":100,"x":true,"q":"1.0000","V":"500","Q":"0.500","B":"123456"}}"""u8;
}
