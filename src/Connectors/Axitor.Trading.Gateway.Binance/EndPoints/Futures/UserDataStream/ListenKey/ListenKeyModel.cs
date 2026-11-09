namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UserDataStream.ListenKey;

/// <summary>Start User Data Stream Response Model.</summary>
internal struct ListenKeyModel
{
    /// <summary>Listen key as ASCII bytes, ready to build stream names from.</summary>
    public byte[] ListenKey;
}
