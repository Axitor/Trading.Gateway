namespace Axitor.Trading.Gateway.Tests.Frames;

/// <summary>Spot stream data as the exchange sends it, without the combined stream envelope.</summary>
internal static class SpotFrames
{
    /// <summary>Spot sends no event type or times.</summary>
    public static ReadOnlySpan<byte> BookTicker =>
        """{"u":400900217,"s":"BNBUSDT","b":"25.35190000","B":"31.21000000","a":"25.36520000","A":"40.66000000"}"""u8;

    public static ReadOnlySpan<byte> AggTrade =>
        """{"e":"aggTrade","E":1672515782136,"s":"BNBBTC","a":12345,"p":"0.001","q":"100","f":100,"l":105,"T":1672515782136,"m":true,"M":true}"""u8;

    /// <summary>Spot sends no pu.</summary>
    public static ReadOnlySpan<byte> DepthUpdate =>
        """{"e":"depthUpdate","E":1672515782136,"s":"BNBBTC","U":157,"u":160,"b":[["0.0024","10"]],"a":[["0.0026","100"]]}"""u8;

    /// <summary>Spot sends no event type or symbol.</summary>
    public static ReadOnlySpan<byte> PartialDepth =>
        """{"lastUpdateId":160,"bids":[["0.0024","10"]],"asks":[["0.0026","100"]]}"""u8;
}
