namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.UserDataStream.Subscription;

/// <summary>Answer to a subscribe or unsubscribe request.</summary>
internal struct SubscriptionModel
{
    /// <summary>Id of the new subscription, counted per connection. -1 when the answer has none, as for unsubscribe.</summary>
    public long SubscriptionId;
}
