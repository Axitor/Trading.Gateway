# ADR-0001: Why I built another Binance client

**Status:** Accepted
**Date:** 2026-11

## TL;DR

Axitor.Trading.Gateway is a .NET 10 Binance client that beats every major Binance client in .NET and Rust, on every benchmark I ran: up to 33x lower cost per message, up to 17x more throughput, up to 43x better tail latency, and up to 1,270x less memory on REST. The stream hot path allocates nothing and never wakes the garbage collector. Resilience for live trading is built into the core, not left to the user.

## The problem

A trading bot rarely loses money on the average message. It loses on the slow one, on the dead socket, and on the ban.

Look at a latency chart of any exchange client and the median looks fine. Network and kernel cost the same for everyone, so at the median the clients sit close together. The real difference is in the tail. When the market moves fast, messages come in bursts. A client that spends microseconds on every message and makes garbage along the way builds a queue, the garbage collector pauses the process, and the price you needed arrives milliseconds late. That is exactly the moment that decides the trade.

I measured the popular clients on the same recorded Binance traffic:

- **Binance.Net**, the standard .NET client. It builds objects for every message: about 1 KB per book ticker and 6–10 KB per depth update. Under load that means up to 285 collections per run, Gen1 and Gen2 included, with pauses of up to 72 ms.
- **binance-sdk**, the official Binance Rust client. It is generated from the OpenAPI spec. Every frame is copied into a string, parsed into a JSON tree and cloned twice, and every number becomes its own heap string: 69 allocations for one book ticker message.
- **NautilusTrader's Binance adapter**, written in Rust as part of a full trading platform, with unbounded queues and a copy of every frame.

Speed is only half the story. Live trading breaks in ways no benchmark shows:

- A socket stays open and answers pings, but no data comes in. The bot keeps trading on a frozen price. Binance.Net offers a per-connection no-data timeout, off by default, and nothing that tells the whole client the network is down. In my own live trading, its reconnects did not bring dead sockets back.
- A burst of retries crosses a rate limit. The exchange bans the IP, and every order is rejected until the ban ends.
- An order request times out. Was the order placed or not? Sending it again may double the position.
- The local order book misses an update during sync and quietly drifts away from the exchange.

Each of these costs real money, and each one hits when the market moves fast.

## What I built

A Binance gateway on .NET 10 with four goals, in this order:

1. **Safe for the business first.** Rate limits, errors and order retries follow the most careful reading of the exchange docs. Nothing fails silently. (ADR-0002)
2. **Zero garbage on the hot path.** Stream messages are parsed straight from UTF-8 bytes into flat structs and passed to your handler. No strings, no objects per message, no events, no GC. (ADR-0003, ADR-0005, ADR-0013)
3. **Resilience in the core.** A network-wide watchdog for silent sockets. A network gate that holds REST, new WebSocket API requests and reconnects until the network is back. Make-before-break replacement of every connection after an outage and on the daily rotation. Explicit gap reports instead of silent holes. A ban-safe rate limiter and clear rules for order retries. (ADR-0007 to ADR-0011)
4. **Fast code that is easy to write.** One line creates the client. Ready-made handlers give you the latest price, a synced local order book or a channel to another thread, all with zero allocations. REST, WebSocket API, JSON streams, SBE streams and user data share one client and one set of rules. (ADR-0004, ADR-0015)

## Results

From the full benchmark run of 2026-11-07, JIT build. Same machine, same recorded traffic, same transport for every client. The method and all tables are in ADR-0018. In brackets: how many times Axitor is better.

| Benchmark | Axitor | Binance.Net | binance-sdk (Rust) | Nautilus (Rust) |
|---|---:|---:|---:|---:|
| Client cost per depth update, over a raw socket read | **0.97 µs** | 15.0 µs (15x) | 32.1 µs (33x) | 20.3 µs (21x) |
| Depth throughput, one connection | **484k msg/s** | 62.2k (7.8x) | 30.1k (16x) | 46.7k (10.4x) |
| Book ticker throughput, one connection | **1.14M msg/s** | 291k (3.9x) | 129k (8.9x) | 181k (6.3x) |
| Book ticker p99.9 latency at live pace | **237 µs** | 3,911 µs (16.5x) | 10,223 µs (43x) | 7,042 µs (30x) |
| Memory per book ticker message | **0 B** | 1,056 B | 3.6 KB | 1.8 KB |
| GC collections per stream run | **0** | up to 285, pauses up to 72 ms | no GC | no GC |
| Klines, 1,500 candles over REST | **638 µs, 3 KB** | 5.5 ms, 1.76 MB (8.6x, 577x) | 8.2 ms, 4.0 MB (12.8x, 1,270x) | 3.7 ms, 2.9 MB (5.7x, 930x) |
| New order over WebSocket API, client cost over raw | **24 µs** | 69 µs (2.9x) | 73 µs (3.1x) | — |

Binance.Net was also measured in its high-performance mode. On depth it still costs 14.8 µs per update, 15x more than Axitor.

## Rust is not faster by default

There is a common belief that Rust is fast by default and .NET is the slow, managed option. These numbers say otherwise. Against the official Binance Rust client, my client is up to 33x cheaper per message, 17x faster on throughput and 43x better at p99.9, and it allocates up to 1,270x less memory on REST. Against NautilusTrader's adapter the gap is still 21x, 11x and 30x.

Rust has no garbage collector, but it doesn't stop you from allocating. binance-sdk makes 69 heap allocations for one book ticker message and pays for every malloc and free inside the message itself. You don't see it as GC pauses; you see it as time.

Speed comes from how the code is built: parse in place, allocate nothing, dispatch without virtual calls. Modern .NET gives you everything for that: structs, spans, generic specialization and Native AOT. Done right, C# is not just "not slower" than Rust. Here it is faster, by an order of magnitude.

## Alternatives I rejected

**Tune Binance.Net.** Its high-performance mode is in the benchmarks above. The cost sits in the design: an object per message, a general-purpose JSON layer and event-based dispatch. No configuration flag removes it.

**Fork and patch an existing client.** Removing per-message allocations means rewriting the parsing, the dispatch and the public models. That is a new client that still carries the old constraints.

**Write it in Rust or C++.** The numbers above show the language is not the bottleneck. .NET 10 reaches zero garbage, and the gateway runs inside .NET trading code with no language boundary in between.

## Consequences

- Faster than every major Binance client in .NET and Rust, on every REST, WebSocket API and stream benchmark I ran.
- Zero allocations and zero GC on the stream hot path, so tail latency stays flat in the bursts that matter most.
- Hundreds of times less memory on heavy REST calls: 3 KB for 1,500 candles, where others need megabytes.
- Live trading failures are handled in the core: silent sockets, outages, bans, unknown order status and order book drift. Every failure comes back as an explicit result, a telemetry event or a fatal stop, never as silent drift.
- Fast code is the default path. You don't fight the library to get the speed; the ready-made handlers already run at full speed.

## Related

ADR-0002, ADR-0003, ADR-0004, ADR-0005, ADR-0006, ADR-0007, ADR-0011, ADR-0013, ADR-0015, ADR-0018