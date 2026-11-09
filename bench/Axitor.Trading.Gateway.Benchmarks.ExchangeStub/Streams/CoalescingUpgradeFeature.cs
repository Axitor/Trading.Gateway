using Microsoft.AspNetCore.Http.Features;

namespace Axitor.Trading.Gateway.Benchmarks.ExchangeStub.Streams;

/// <summary>Upgrade feature that gives the WebSocket a CoalescingStream over the connection.</summary>
internal sealed class CoalescingUpgradeFeature : IHttpUpgradeFeature
{
    private readonly IHttpUpgradeFeature _inner;

    public CoalescingUpgradeFeature(IHttpUpgradeFeature inner) => _inner = inner;

    public bool IsUpgradableRequest => _inner.IsUpgradableRequest;

    /// <summary>The connection stream once upgraded, or null before.</summary>
    public CoalescingStream? Transport { get; private set; }

    public async Task<Stream> UpgradeAsync()
    {
        Stream inner = await _inner.UpgradeAsync().ConfigureAwait(false);
        Transport = new CoalescingStream(inner);
        return Transport;
    }
}
