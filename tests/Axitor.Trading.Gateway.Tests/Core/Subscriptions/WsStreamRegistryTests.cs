using System.Globalization;
using System.Text;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Subscriptions;

namespace Axitor.Trading.Gateway.Tests.Core.Subscriptions;

public sealed class WsStreamRegistryTests
{
    // Streams routed by their name.
    private const int NameChannel = 0;

    // SBE streams are routed by template id and symbol.
    private const int TradeTemplate = 10000;
    private const int BestBidAskTemplate = 10001;

    [Fact]
    public void SameKeyOnDifferentChannelsIsTwoSubscriptions()
    {
        WsStreamRegistry registry = new(maxShards: 1, maxStreamsPerShard: 4);

        Assert.True(registry.TryAdd("btcusdt@trade"u8, TradeTemplate, "BTCUSDT"u8, out int trade, out _));
        Assert.True(registry.TryAdd("btcusdt@bestBidAsk"u8, BestBidAskTemplate, "BTCUSDT"u8, out int quote, out _));

        Assert.NotEqual(trade, quote);
        Assert.Equal(trade, registry.Find(TradeTemplate, "BTCUSDT"u8, out _));
        Assert.Equal(quote, registry.Find(BestBidAskTemplate, "BTCUSDT"u8, out _));
        Assert.Equal(-1, registry.Find(NameChannel, "BTCUSDT"u8, out _));
    }

    [Fact]
    public void SubscribingTwiceReturnsTheSameHandle()
    {
        WsStreamRegistry registry = new(maxShards: 1, maxStreamsPerShard: 4);

        Assert.True(registry.TryAdd("btcusdt@aggTrade"u8, NameChannel, "btcusdt@aggTrade"u8, out int first, out int firstShard));
        Assert.False(registry.TryAdd("btcusdt@aggTrade"u8, NameChannel, "btcusdt@aggTrade"u8, out int second, out int secondShard));

        Assert.Equal(first, second);
        Assert.Equal(firstShard, secondShard);
    }

    [Fact]
    public void FillsConnectionsInTurnAndRefusesWhenFull()
    {
        WsStreamRegistry registry = new(maxShards: 2, maxStreamsPerShard: 2);
        int[] shards = new int[4];

        for (int i = 0; i < shards.Length; i++)
        {
            byte[] name = Name(i);
            Assert.True(registry.TryAdd(name, NameChannel, name, out _, out shards[i]));
        }

        byte[] extra = Name(shards.Length);

        Assert.Equal([0, 0, 1, 1], shards);
        Assert.False(registry.TryAdd(extra, NameChannel, extra, out int handle, out int shard));
        Assert.Equal(-1, handle);
        Assert.Equal(-1, shard);
    }

    [Fact]
    public void ReusedHandleIsNotCurrentForTheOldStream()
    {
        WsStreamRegistry registry = new(maxShards: 1, maxStreamsPerShard: 4);

        registry.TryAdd("btcusdt@bookTicker"u8, NameChannel, "btcusdt@bookTicker"u8, out int handle, out _);
        Assert.Equal(handle, registry.Find(NameChannel, "btcusdt@bookTicker"u8, out byte[]? old));
        Assert.True(registry.IsCurrent(handle, old!));

        Assert.True(registry.TryRemove(handle));
        Assert.False(registry.TryRemove(handle));
        Assert.False(registry.IsCurrent(handle, old!));

        // A freed handle goes to the next stream: data found for the old one must not reach it.
        Assert.True(registry.TryAdd("ethusdt@bookTicker"u8, NameChannel, "ethusdt@bookTicker"u8, out int reused, out _));
        Assert.Equal(handle, reused);
        Assert.False(registry.IsCurrent(handle, old!));
        Assert.Equal(-1, registry.Find(NameChannel, "btcusdt@bookTicker"u8, out _));
    }

    [Fact]
    public void FindsEveryStreamAfterManyChanges()
    {
        WsStreamRegistry registry = new(maxShards: 3, maxStreamsPerShard: 200);
        int[] handles = new int[registry.Capacity];

        for (int i = 0; i < handles.Length; i++)
        {
            byte[] name = Name(i);
            Assert.True(registry.TryAdd(name, NameChannel, name, out handles[i], out _));
        }

        for (int i = 0; i < handles.Length; i += 2)
        {
            Assert.True(registry.TryRemove(handles[i]));
        }

        for (int i = 0; i < handles.Length; i++)
        {
            int expected = i % 2 == 0 ? -1 : handles[i];
            Assert.Equal(expected, registry.Find(NameChannel, Name(i), out _));
        }
    }

    private static byte[] Name(int index) =>
        Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"s{index}@aggTrade"));
}
