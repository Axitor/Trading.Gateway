# ADR-0015: Ready-made handlers: latest value, order book, channel

**Status:** Accepted
**Date:** 2026-11

## TL;DR

Most strategies need one of three things from a stream: the latest price, a synced order book, or every message in order on their own thread. The library ships all three as ready-made handlers that run at full speed with no per-message allocations. `LatestCache` gives any thread the latest value through a seqlock: no locks, never a torn read, never a stale price passed off as live. `OrderBookSet` keeps local books in sync (ADR-0014). `ChannelSink` moves messages to a consumer thread without ever holding up the receive loop, and when it has to drop, it says exactly what was lost.

## The problem

Handlers run on the receive loop and must return fast (ADR-0012). That's easy for counting messages, and hard for the things strategies actually do:

- **The strategy reads on another thread.** It wants the latest best bid and ask, now, from its own loop. A lock between the reader and the receive loop makes one of them wait. No lock risks a torn read: half of one message and half of the next.
- **A stale price looks like a live one.** After a disconnect, the last value is still in memory. A strategy that reads it as current trades on a price that no longer exists.
- **Some streams need every message.** Trades build volume and candles. Moving them to another thread through a queue is natural, but a full queue forces a choice: block the receive loop and stall every stream on the connection, or drop messages and lose count.

Every user writing these by hand gets at least one of them subtly wrong.

## Decision

### LatestCache: the latest value, lock-free and honest

`LatestCache<TModel>` keeps one slot per subscription. The receive loop writes each message into its slot; any thread reads it at any time.

The slot is a seqlock. A sequence number is odd while a write is in progress:

- the writer bumps it to odd with a full fence, copies the struct, and bumps it to even with a release write;
- a reader takes the sequence, copies the struct, fences, and checks that the sequence hasn't moved;
- if it has, the reader copies again; if a write is in progress, it spins briefly, since a write takes nanoseconds, and never sleeps.

The writer never waits for a reader, and a reader never sees a half-written message. The fences are explicit, so it holds on ARM as well as x64. Each read also returns a version, a cheap way to see whether anything changed since the last look.

Each slot has a state: empty, live or stale. `TryRead` returns data only while the subscription is live: not before the first message, not after a disconnect until fresh data arrives, not after unsubscribe. A strategy can't act on a dead price by accident. `TryReadLast` is there for the cases where the last known value is still useful, such as display.

    LatestCache<BookTickerModel> cache = new(client.StreamSessionCapacity);
    BinanceStreamSession session = client.Futures.UsdM.WsStreams.BookTicker.CreateSession(cache.Handler);
    WsStreamSubscribeResult subscription = await session.SubscribeAsync(Symbol.Parse("BTCUSDT"), appToken);

    // Any thread, any time:
    if (cache.TryRead(subscription.Handle, out BookTickerModel ticker, out long version)) { ... }

### OrderBookSet: synced books, read without locks

Books are written by the receive loop and the snapshot worker, and read through the same seqlock. `TryGetBest` and `TryCopyTop` copy the best levels from any thread and return false while a book is out of sync. (ADR-0014)

### ChannelSink: every message on your thread, losses never hidden

`ChannelSink<TModel>` queues messages and connection events of every subscription for one consumer, on a bounded channel of flat struct items.

- **The receive loop never waits.** If the queue is full, the message is dropped, not blocked on. Blocking would stall every stream on the connection, and in the end the exchange would drop the connection anyway.
- **A drop is never silent.** The first drop opens an overflow period for that subscription. Before its next message, the consumer gets an `Overflow` item with the period and the number of messages lost. It reads like a reconnect: rebuild what was built from the lost messages. `DroppedCount` shows how often it happens.
- **Connection events are never dropped.** The channel keeps extra room for them, so a disconnect or a gap always reaches the consumer. If the consumer stops reading for so long that even that room fills up, the channel ends with an error rather than lose an event.
- **A fatal session ends the channel** with the cause, so the consumer's read loop sees it.

### All three follow the same rules

Each handler is a class that holds the state, with a small readonly struct `Handler` that points to it and is passed to `CreateSession` (ADR-0005). They run inline on the receive loop with no virtual calls, and they handle `OnDisconnected`, `OnReconnected` and `OnUnsubscribed`, so stale state never leaks into the next stream on the same handle.

## What it costs

- **A seqlock reader may retry.** Under a constant stream of writes a read can take a second pass. The copy is a few dozen bytes, so a retry costs nanoseconds.
- **One slot per possible subscription.** `LatestCache` is sized by the session capacity up front: memory spent once, at startup.
- **ChannelSink can drop.** That is the honest choice when the consumer falls behind. The alternative, blocking, loses the whole connection.

## Alternatives I rejected

**A lock around the latest value.** The receive loop would wait for readers, and in a burst every wait adds to the tail.

**`Interlocked` swap of a reference to an immutable object.** Lock-free and simple, but one heap object per message.

**A blocking channel.** A slow consumer stalls the receive loop, then every stream on the connection, then the connection itself.

**An unbounded channel.** A slow consumer turns into growing memory and growing latency, and nobody finds out until the process is in trouble.

**Drop silently when full.** Volume and candles built from the stream are wrong, and nothing says so.

## Related

ADR-0005, ADR-0012, ADR-0014