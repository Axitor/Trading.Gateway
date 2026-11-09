# Axitor.Trading.Gateway

**Faster than every major Binance client. In .NET, and in Rust.**

A Binance gateway for .NET 10, built for trading bots where the slow message, the dead socket and the ban cost real money. Zero allocations on the hot path. Resilience in the core. Native AOT from day one.

[![CI](https://github.com/Axitor/Trading.Gateway/actions/workflows/ci.yml/badge.svg)](https://github.com/Axitor/Trading.Gateway/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Native AOT](https://img.shields.io/badge/Native%20AOT-ready-2ea44f)
![Hot path](https://img.shields.io/badge/hot%20path-zero%20alloc-2ea44f)
![License](https://img.shields.io/badge/license-MIT-blue)

## In numbers

- **33x lower cost per message** than the official Binance Rust SDK. **15x** lower than Binance.Net.
- **1.14 million messages per second** on a single connection, on a desktop CPU from 2014. Up to **17x** the throughput of the Rust SDK, up to **8.6x** Binance.Net, even in its high-performance mode.
- **43x better p99.9 latency** than the Rust SDK, **16x** better than Binance.Net, on the busiest stream.
- **0 bytes, 0 GC** on the stream hot path. Binance.Net makes up to 285 collections per run, with GC pauses up to 72 ms.
- **1,500 candles in 3 KB.** Binance.Net needs 1.76 MB, the Rust SDK 3.97 MB. Up to **1,270x** less memory.
- **Faster on every REST, WebSocket API and stream benchmark** I ran, against Binance.Net, binance-sdk and NautilusTrader.

## Why it exists

A trading bot rarely loses money on the average message. It loses on the slow one, on the socket that looks alive but carries no data, on the burst of retries that gets the IP banned, and on the order that timed out and was sent twice.

Existing clients are built for API coverage. They build objects for every message, pause for garbage collection in exactly the bursts that matter, and leave live-trading failures to the user. This library is built the other way around: business safety first, then the fastest way to do it. [Read the full story →](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0001-Why-another-Binance-client.md)

## Benchmarks

Whole clients, through their public APIs, on the same recorded Binance mainnet traffic, the same machine and the same local exchange stub. The competitor's fastest mode is always included. [Full method and all tables →](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0018-Benchmarks-that-measure-the-real-thing.md)

### What each client costs per message

Time a client spends per message on top of a raw socket read: parsing, routing and delivery. The transport everyone shares is taken out, so this is the price of the client's own code.

| Stream | **Axitor** | Binance.Net | Binance.Net fast mode | binance-sdk (Rust) | Nautilus (Rust) |
|---|---:|---:|---:|---:|---:|
| Book ticker | **0.46 µs** | 3.03 µs | 1.49 µs | 7.36 µs | 5.11 µs |
| Depth | **0.97 µs** | 15.0 µs | 14.8 µs | 32.1 µs | 20.3 µs |
| Depth 20 | **1.81 µs** | 24.2 µs | 25.1 µs | 49.7 µs | 31.4 µs |

### Throughput on one connection

Messages per second, median of 5 runs. Raw is a loop that only reads and drops messages: the ceiling for any client.

| Stream | Raw | **Axitor** | Binance.Net | binance-sdk (Rust) | Nautilus (Rust) |
|---|---:|---:|---:|---:|---:|
| Book ticker | 2.43M | **1.14M** | 291k | 129k | 181k |
| Depth | 908k | **484k** | 62.2k | 30.1k | 46.7k |
| Depth 20 | 800k | **327k** | 39.2k | 19.6k | 30.6k |
| Local order book | — | **300k** | 82.4k | — | — |

### Tail latency at live market pace

p99 / p99.9 in microseconds. Medians are close for everyone, because the network costs the same. The tail is where bursts hit, and where money is lost.

| Stream | **Axitor** | Binance.Net | binance-sdk (Rust) | Nautilus (Rust) |
|---|---:|---:|---:|---:|
| Book ticker | **215 / 237** | 2,363 / 3,911 | 6,354 / 10,223 | 4,178 / 7,042 |
| Agg trade | **207 / 240** | 1,017 / 3,200 | 1,949 / 2,732 | 2,094 / 2,189 |
| Depth | **210 / 246** | 334 / 674 | 401 / 1,076 | 372 / 820 |
| Depth 20 | **207 / 240** | 334 / 629 | 364 / 920 | 356 / 1,058 |

### Memory per stream message

| Stream | **Axitor** | Binance.Net | binance-sdk (Rust) | Nautilus (Rust) |
|---|---:|---:|---:|---:|
| Book ticker | **0 B** | 1,056 B | 3,584 B | 1,751 B |
| Depth | **7 B\*** | 6,580 B | 17,192 B | 12,134 B |
| Depth 20 | **0 B** | 10,157 B | 26,290 B | 19,177 B |

\* Everything the process allocates during the run, the benchmark harness and its timers included, divided by the number of messages. The smallest .NET object takes 24 bytes, so 7 bytes per message is background spread over the run, not an allocation per message.

### REST

Time and memory per call.

| Call | **Axitor** | Binance.Net | binance-sdk (Rust) | Nautilus (Rust) |
|---|---:|---:|---:|---:|
| Klines, 1,500 candles | **638 µs, 3 KB** | 5.49 ms, 1.76 MB | 8.18 ms, 3.97 MB | 3.66 ms, 2.91 MB |
| Order book, 1,000 levels | **207 µs, 34 KB** | 1.24 ms, 415 KB | 917 µs, 484 KB | 707 µs, 339 KB |
| Exchange info | **3.70 ms, 97 KB** | 14.95 ms, 5.34 MB | 11.50 ms, 9.86 MB | 14.90 ms, 10.48 MB |
| Account info | **1.15 ms, 100 KB** | 2.08 ms, 283 KB | 2.68 ms, 1.88 MB | 2.68 ms, 1.42 MB |
| New order | **103 µs, 4.8 KB** | 125 µs, 12.1 KB | 153 µs, 31.8 KB | 170 µs, 20.6 KB |

The order book snapshot can also be written into reused buffers: **189 µs and 3 KB** for 1,000 levels per side.

### WebSocket API

Client cost on top of a raw WebSocket round trip, and memory per call.

| Call | **Axitor** | Binance.Net | binance-sdk (Rust) |
|---|---:|---:|---:|
| New order | **24 µs, 394 B** | 69 µs, 13.2 KB | 73 µs, 23.9 KB |
| Cancel order | **22 µs, 294 B** | 66 µs, 11.6 KB | 67 µs, 21.6 KB |
| Ticker price | **14 µs, 305 B** | 45 µs, 8.8 KB | 26 µs, 13.4 KB |
| Account info | **1.37 ms, 98 KB** | 1.94 ms, 1.28 MB | 11.9 ms, 7.56 MB |

Measured on an aging Intel Core i7-5820K from 2014, where inefficiency has nowhere to hide: 1.14 million messages per second even on twelve-year-old silicon. Windows 10, .NET 10.0.14, BenchmarkDotNet 0.15.8. Binance.Net 13.8.0, binance-sdk 77.1.0, nautilus-binance 0.66.0.

## Rust is not faster by default

It's a common belief that Rust wins by default and .NET is the slow, managed option. The numbers above say otherwise: against the official Binance Rust client, this .NET 10 library is up to 33x cheaper per message, 17x faster on throughput and 43x better at p99.9.

Rust has no garbage collector, but it doesn't stop you from allocating. The Rust SDK makes 69 heap allocations for one book ticker message. Speed comes from how the code is built: parse in place, allocate nothing, dispatch without virtual calls. Modern .NET has every tool for that. Done right, C# is not just "not slower" than Rust. Here it is faster, by an order of magnitude.

## Built for live trading

Speed is half the job. The other half is surviving the failures that no benchmark shows.

- **A watchdog for silent sockets.** A dedicated connection listens to a stream that is never quiet. When it goes silent, the whole client knows the network is down, not just one socket. [ADR-0010](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0010-Network-health-a-watchdog-and-a-network-gate-and-rotation-after-outage.md)
- **One network gate for everything.** REST, new WebSocket API requests and reconnects wait for a real repair instead of burning retries and connection limits.
- **Make-before-break, everywhere.** After every outage, on the daily rotation and on server shutdown notices, every connection is replaced by a new one that is already subscribed. No hole in the data, no order sent into a dead socket. [ADR-0012](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0012-A-stream-transport-that-keeps-up-with-the-market.md)
- **A rate limiter that can't get you banned.** All windows checked and charged in one atomic step, raised by the exchange's own counts, never lowered, with a reserved share for orders. [ADR-0007](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0007-A-rate-limiter-that-can-not-get-you-banned.md)
- **Orders that can't be placed twice by accident.** A client order id is required by the compiler. Placing is never retried unless you say so. "Duplicate order" reads as "already placed". [ADR-0009](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0009-Orders-that-can-not-be-placed-twice-by-accident.md)
- **Honest outcomes.** Every request says whether it could have reached the exchange: answered, never sent, or maybe. Any 5xx is "unknown", never "failed". [ADR-0008](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0008-Error-classification-and-retries.md)
- **Orders checked before they leave.** Price rounded to the tick, quantity down to the step, limits and notional checked locally. A bad order costs no round trip and no order limit.
- **A local order book that never drifts silently.** Binance's sync procedure to the letter, every update checked against the last, background refresh without a gap. [ADR-0014](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0014-A-local-order-book-that-never-drifts-silently.md)
- **User data that never goes blind.** Per-event subscriptions with an expiry anchor, key checks after every reconnect, events kept in exchange order, every gap reported once. [ADR-0016](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0016-User-data-that-never-goes-blind.md)
- **Nothing fails silently.** Every failure is an explicit result, a telemetry event or a fatal stop. [ADR-0002](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0002-Business-safety-first.md)

## Modern .NET under the hood

- Generic struct handlers: every per-message call is direct and inlined, under the JIT and under Native AOT. No events, no delegates, no virtual calls on the hot path. [ADR-0005](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0005-Generic-struct-handlers-instead-of-events-and-virtual-calls.md)
- `static abstract` interface members for dialects and parsers, resolved at compile time.
- Hand-written UTF-8 readers and an exact fast-path decimal parser (the Clinger fast path). No JSON tree, no strings, no reflection. [ADR-0013](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0013-Parsing-without-a-parser.md)
- `IValueTaskSource` slots and `PoolingAsyncValueTaskMethodBuilder`: a WebSocket API request with no allocation per await. [ADR-0011](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0011-A-WebSocket-API-built-for-order.md)
- A seqlock for lock-free reads of the latest price and the local order book from any thread. [ADR-0015](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0015-Ready-made-handlers-latest-value-and-order-book-and-channel.md)
- `System.Threading.Lock`, `[InlineArray]`, `SearchValues`, `stackalloc` signing, `ArrayPool` buffers copied out once at exact size.
- Flat structs with public fields, packed `Symbol` (one `UInt128`) and `ClientOrderId` values: no strings in market data or order events. [ADR-0003](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0003-Hot-path-and-cold-path.md)
- Native AOT and trimming enforced by the build: any reflection is a compile error. [ADR-0006](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0006-Native-AOT-from-day-one.md)

## Quick start

```csharp
// One client per app. Pass the app shutdown token: it stops every connection.
using BinanceClient<NoOpTelemetryHandler> client = new(apiKey, apiSecret, appCancellationToken: appToken);
```

**Latest best bid and ask,** readable from any thread without locks:

```csharp
LatestCache<BookTickerModel> cache = new(client.StreamSessionCapacity);
BinanceStreamSession session = client.Futures.UsdM.WsStreams.BookTicker.CreateSession(cache.Handler);
WsStreamSubscribeResult btc = await session.SubscribeAsync(Symbol.Parse("BTCUSDT"), appToken);

// False before the first message and while the data is not live.
if (cache.TryRead(btc.Handle, out BookTickerModel ticker, out long version))
{
    Console.WriteLine($"{ticker.BidPrice} / {ticker.AskPrice}");
}
```

**A local order book,** synced and refreshed by itself:

```csharp
OrderBookSet<NoOpTelemetryHandler> books = new(client, BinanceMarkets.FuturesUsdM);
BinanceStreamSession depth = client.Futures.UsdM.WsStreams.Depth.CreateSession(books.Handler, DepthUpdateSpeed.Ms100);
WsStreamSubscribeResult book = await depth.SubscribeAsync(Symbol.Parse("BTCUSDT"), appToken);

// False while the book is not synced.
if (books.TryGetBest(book.Handle, out DepthLevel bid, out DepthLevel ask, out long updateId))
{
    Console.WriteLine($"{bid.Price} x {bid.Quantity} / {ask.Price} x {ask.Quantity}");
}
```

**An order over the WebSocket API,** rounded and checked before it leaves:

```csharp
NewOrderRequest request = new()
{
    Symbol = Symbol.Parse("BTCUSDT"),
    ClientOrderId = ClientOrderId.Parse("my-order-1"),
    Filters = filters,
    Side = OrderSide.BUY,
    Type = OrderType.LIMIT,
    TimeInForce = TimeInForce.GTC,
    Quantity = 0.01,
    Price = 60000,
};

WsApiRequestResult<OrderModel> result = await client.Futures.UsdM.WsApi.Trade.NewOrder.PlaceAsync(in request);
```

More in [samples](https://github.com/Axitor/Trading.Gateway/tree/main/samples/Axitor.Trading.Gateway.Samples): one sample per endpoint.

## What's inside

| | USD-M futures | Spot | COIN-M futures |
|---|:---:|:---:|:---:|
| REST | ✓ | ✓ | ✓ |
| WebSocket API | ✓ | ✓ | |
| JSON streams | ✓ | ✓ | ✓ |
| SBE binary streams | | ✓ | |
| User data | ✓ | ✓ | ✓ |
| Local order book | ✓ | ✓ | ✓ |

**USD-M futures**
- **REST market data:** server time, order book, klines, exchange info
- **REST account:** account info (v2 and v3), leverage brackets, symbol config
- **REST trade:** new order, test order, modify, cancel, cancel all; new, cancel and cancel-all algo orders; open orders, open algo orders; change leverage, change margin type
- **WebSocket API:** ticker price, account balance, account info; new, modify and cancel order; new and cancel algo order
- **Streams:** book ticker, aggregated trades, diff depth, partial depth, klines
- **User data:** order updates, account updates, trade lite, algo updates, account config updates, margin calls

**Spot**
- **REST:** server time, order book
- **WebSocket API:** ticker price; user data with execution reports, account positions, balance updates, list status, external lock updates
- **JSON streams:** book ticker, aggregated trades, diff depth, partial depth
- **SBE streams:** best bid and ask, trades, diff depth, partial depth

**COIN-M futures**
- **REST:** server time, order book
- **Streams:** diff depth, user data

### Ready-made handlers

- **`LatestCache`:** the latest value of each stream, lock-free, never stale data passed off as live.
- **`OrderBookSet`:** local order books, synced, refreshed in the background, readable from any thread.
- **`ChannelSink`:** every message on your own thread, with explicit overflow reports instead of silent drops.

## Tested like it trades

- **Unit tests on real exchange messages,** and every error code the classifier knows, row by row.
- **Integration tests on real sockets** against a local fake exchange that bans, drops connections, goes silent and shuts down on cue. The test names read like a spec: `OrderLostWithConnectionIsNotResent`, `SilenceRepairsNetworkAndReconnectsStreams`, `AgedConnectionRotatesWithoutGap`.
- **Live tests on Binance mainnet** for all three markets, user data included, without placing a single order.
- **No mocks, no sleeps.**

[How the tests are built →](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0019-Tests-as-a-showcase-and-a-safety-net.md)

## Architecture decisions

Every design choice is written down: the problem, the decision, what it costs and what was rejected.

| ADR | Decision |
|---|---|
| [0001](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0001-Why-another-Binance-client.md) | Why I built another Binance client |
| [0002](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0002-Business-safety-first.md) | Business safety before technical beauty |
| [0003](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0003-Hot-path-and-cold-path.md) | Hot path and cold path, two sets of rules |
| [0004](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0004-An-exchange-agnostic-core-and-exchange-connectors.md) | An exchange-agnostic core and exchange connectors |
| [0005](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0005-Generic-struct-handlers-instead-of-events-and-virtual-calls.md) | Generic struct handlers instead of events and virtual calls |
| [0006](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0006-Native-AOT-from-day-one.md) | Native AOT from day one |
| [0007](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0007-A-rate-limiter-that-can-not-get-you-banned.md) | A rate limiter that can't get you banned |
| [0008](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0008-Error-classification-and-retries.md) | Error classification and retries you can trust with orders |
| [0009](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0009-Orders-that-can-not-be-placed-twice-by-accident.md) | Orders that can't be placed twice by accident |
| [0010](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0010-Network-health-a-watchdog-and-a-network-gate-and-rotation-after-outage.md) | Network health: a watchdog, a network gate and rotation after every outage |
| [0011](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0011-A-WebSocket-API-built-for-order.md) | A WebSocket API built for orders |
| [0012](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0012-A-stream-transport-that-keeps-up-with-the-market.md) | A stream transport that keeps up with the market |
| [0013](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0013-Parsing-without-a-parser.md) | Parsing without a parser |
| [0014](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0014-A-local-order-book-that-never-drifts-silently.md) | A local order book that never drifts silently |
| [0015](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0015-Ready-made-handlers-latest-value-and-order-book-and-channel.md) | Ready-made handlers: latest value, order book, channel |
| [0016](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0016-User-data-that-never-goes-blind.md) | User data that never goes blind |
| [0017](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0017-SBE-binary-streams-on-the-same-engine.md) | SBE binary streams on the same engine |
| [0018](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0018-Benchmarks-that-measure-the-real-thing.md) | Benchmarks that measure the real thing |
| [0019](https://github.com/Axitor/Trading.Gateway/blob/main/docs/adr/ADR-0019-Tests-as-a-showcase-and-a-safety-net.md) | Tests as a showcase and a safety net |

## Coming next

The rest of the Binance API, FIX and Protobuf transports, and more exchanges on the same core.

## About the author

Built by **Daniel Lebedev**: Principal .NET Architect with 25+ years of experience, focused on low-latency, high-load trading systems. Open to remote roles.

[LinkedIn](https://www.linkedin.com/in/axitor/) · [GitHub](https://github.com/axitor)

## License

MIT