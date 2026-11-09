# ADR-0018: Benchmarks that measure the real thing

**Status:** Accepted
**Date:** 2026-11

## TL;DR

I don't benchmark pieces of my code against pieces of other code. I benchmark whole clients, through their public APIs, against the libraries people actually use: Binance.Net in .NET, and the official Binance Rust client and NautilusTrader's adapter in Rust. Every client reads the same recorded mainnet traffic from the same local exchange stub, on the same machine. A raw socket loop gives the floor, so the numbers show what each client itself costs. Latency is measured at the real pace of the market, throughput at full speed, and the competitor's fastest mode is always included.

## The problem

Performance claims are easy to make and easy to fake, often by accident:

- **Microbenchmarks flatter.** A parser benchmark says nothing about routing, dispatch, the socket, the GC under load or the queue that builds in a burst.
- **Live exchanges can't be repeated.** Traffic, network and server load change every second, and rate limits cap how hard you can push.
- **Medians hide the problem.** At the median, network and kernel dominate and every client looks alike. The money is lost in the tail.
- **Unfair setups.** A competitor in its default mode, with its own rate limiter on, or fed different data, loses for reasons that have nothing to do with its code.

## Decision

### Whole clients, real libraries

Every client is driven through its public API, the way a bot would use it:

- **Axitor**, JIT and Native AOT builds;
- **Binance.Net 13.8.0**, in its default mode and its high-performance mode, with its rate limiters turned off;
- **binance-sdk 77.1.0**, the official Binance Rust connector;
- **nautilus-binance 0.66.0**, NautilusTrader's Binance adapter in Rust. Its rate limiter can't be turned off, so each of its REST benchmarks runs as a burst of at most 2,300 calls (290 for new order), and the 61-second wait before the next burst is not measured. It has no WebSocket API run: its client only places and cancels orders, under a fixed quota of 20 per second.

### One stub, recorded mainnet data

A local exchange stub on Kestrel serves the Binance endpoints on loopback:

- **streams** replay frames recorded on Binance mainnet with `bench/tools/recorder.cs`: about 640k book tickers, 98k depth updates, 90k partial depth updates, 21k aggregated trades and 10k klines for 20 symbols;
- **REST and WebSocket API** answer with bodies recorded on mainnet; order answers come from the samples in the Binance docs;
- **depth** starts at the update that covers the snapshot, as the exchange docs describe: older updates are left out, as a live client would never see them.

The recorded data stays local and is not published. Anyone can record their own with the recorder and run the same script.

### Two paces, two questions

- **Pace 1: latency.** The stub replays frames at their recorded timing. It replaces each frame's event time with its send time on the machine-wide monotonic clock, in microseconds, and each client's handler records now minus event time on the same clock, whatever language it is written in. Busy streams are measured for 120 s after a warmup; aggregated trades and klines run the whole 10-minute pass to get enough samples. Binance.Net's high-performance mode carries no event time, so it is left out here.
- **Max: throughput.** The stub sends as fast as the client reads, in 64 KB chunks, so the stub is never the bottleneck. Each client gets a warmup pass, then a whole fresh pass is measured. The result is the median of 5 rounds, with rounds as the outer loop, so every client meets the machine in the same state. Latency at max pace measures the queue, not the client, and is not reported.

### Raw floors

- **Raw stream client:** reads whole messages from the same socket and drops them. Its throughput is the ceiling for any client.
- **Raw WebSocket API client:** sends a prepared frame and waits for its answer.

**Client cost** is the time a client adds on top of raw: for streams, one second divided by the client's throughput minus one second divided by raw throughput. It removes the transport everyone shares and shows the price of each client's own code.

### REST and WebSocket API in BenchmarkDotNet

.NET clients run in BenchmarkDotNet with the memory diagnoser, one run per benchmark class so each class keeps its own baseline. The AOT build runs as one joined run. The Rust clients use a harness built to match: 5 warmup and 20 measured iterations of about 250 ms, with the error as half the 99.9% confidence interval. Rust allocations count every allocation on every thread during the measured iterations, freed or not.

### Why tails, not medians

At live pace the median is mostly network and kernel, the same for everyone. A tail is roughly the cost per message times the size of a burst: a client that is ten times cheaper per message drains a burst ten times faster. That is why the gap grows from 1.2–2x at the median to 10–43x at p99.9 on the busiest stream.

## Machine

Windows 10 21H2, Intel Core i7-5820K (Broadwell, 6 cores, 12 threads, 3.3 GHz), .NET 10.0.14, BenchmarkDotNet 0.15.8. All traffic on loopback over plain WebSocket and HTTP. TLS would add the same cost to every client.

## Results, 2026-11-07

### Streams, max throughput, msg/s

One connection, 20 symbols, median of 5 runs.

| Stream | Raw | Axitor JIT | Axitor AOT | Binance.Net | Binance.Net fast mode | binance-sdk | Nautilus |
|---|---:|---:|---:|---:|---:|---:|---:|
| Book ticker | 2,428k | **1,145k** | 979k | 291k | 526k | 129k | 181k |
| Depth | 908k | **484k** | 439k | 62.2k | 62.9k | 30.1k | 46.7k |
| Depth 20 | 800k | **327k** | 318k | 39.2k | 37.9k | 19.6k | 30.6k |
| Order book set | — | **300k** | 269k | 82.4k | — | — | — |

### Streams, client cost over raw, µs per message

| Stream | Axitor JIT | Axitor AOT | Binance.Net | Binance.Net fast mode | binance-sdk | Nautilus |
|---|---:|---:|---:|---:|---:|---:|
| Book ticker | **0.46** | 0.61 | 3.03 | 1.49 | 7.36 | 5.11 |
| Depth | **0.97** | 1.18 | 15.0 | 14.8 | 32.1 | 20.3 |
| Depth 20 | **1.81** | 1.89 | 24.2 | 25.1 | 49.7 | 31.4 |

### Streams, latency at live pace, µs

p50 / p99 / p99.9.

| Stream | Axitor JIT | Axitor AOT | Binance.Net | binance-sdk | Nautilus |
|---|---:|---:|---:|---:|---:|
| Book ticker | 102 / 215 / 237 | **84 / 198** / 294 | 163 / 2,363 / 3,911 | 164 / 6,354 / 10,223 | 208 / 4,178 / 7,042 |
| Agg trade | 97 / 207 / 240 | **80 / 164 / 189** | 127 / 1,017 / 3,200 | 109 / 1,949 / 2,732 | 121 / 2,094 / 2,189 |
| Depth | 96 / 210 / 246 | **75 / 152 / 211** | 129 / 334 / 674 | 122 / 401 / 1,076 | 133 / 372 / 820 |
| Depth 20 | 95 / 207 / 240 | **76 / 159 / 200** | 133 / 334 / 629 | 142 / 364 / 920 | 149 / 356 / 1,058 |
| Order book set | 100 / 215 / 277 | **78 / 167 / 268** | 152 / 390 / 2,159 | — | — |

### Streams, memory per message

| Stream | Axitor | Binance.Net | binance-sdk | Nautilus |
|---|---:|---:|---:|---:|
| Book ticker | **0 B** | 1,056 B | 3,584 B (69 allocations) | 1,751 B (40) |
| Agg trade | **1 B** | 1,065 B | 7,604 B (72) | 4,430 B (42) |
| Depth | **7 B** | 6,580 B | 17,192 B (314) | 12,134 B (189) |
| Depth 20 | **0 B** | 10,157 B | 26,290 B (471) | 19,177 B (284) |
| Kline 1m | **2 B** | 1,193 B | 10,308 B (127) | 6,090 B (78) |

At max pace Binance.Net runs 177 to 285 Gen0 collections per pass, with Gen1 and Gen2, and 36 to 72 ms of GC pauses. Axitor runs none.

### REST, JIT

Time and memory per call.

| Benchmark | Axitor | Binance.Net | binance-sdk | Nautilus |
|---|---:|---:|---:|---:|
| Klines, 500 | **252 µs, 3.0 KB** | 1,961 µs, 589 KB | 2,900 µs, 1.37 MB | 1,367 µs, 944 KB |
| Klines, 1,500 | **638 µs, 3.1 KB** | 5,493 µs, 1.76 MB | 8,178 µs, 3.97 MB | 3,662 µs, 2.91 MB |
| Order book, 1,000 levels | **207 µs, 34 KB** | 1,243 µs, 415 KB | 917 µs, 484 KB | 707 µs, 339 KB |
| Order book into buffers | **189 µs, 3.1 KB** | — | — | — |
| Exchange info | **3.70 ms, 97 KB** | 14.95 ms, 5.34 MB | 11.50 ms, 9.86 MB | 14.90 ms, 10.48 MB |
| Account info | **1.15 ms, 100 KB** | 2.08 ms, 283 KB | 2.68 ms, 1.88 MB | 2.68 ms, 1.42 MB |
| Leverage brackets | **4.14 ms, 388 KB** | 7.23 ms, 1.06 MB | 11.96 ms, 12.76 MB | — |
| New order | **103 µs, 4.8 KB** | 125 µs, 12.1 KB | 153 µs, 31.8 KB | 170 µs, 20.6 KB |

### REST, Native AOT

| Benchmark | Time | Memory |
|---|---:|---:|
| Klines, 500 | 229 µs | 3.0 KB |
| Klines, 1,500 | 554 µs | 3.0 KB |
| Order book, 1,000 levels | 198 µs | 34 KB |
| Order book into buffers | 182 µs | 3.1 KB |
| Exchange info | 4.20 ms | 97 KB |
| Account info | 1.55 ms | 100 KB |
| Leverage brackets | 5.57 ms | 388 KB |
| New order | 99 µs | 4.8 KB |

### WebSocket API, JIT

Time and memory per call. Raw is the floor.

| Benchmark | Axitor | Binance.Net | binance-sdk | Raw |
|---|---:|---:|---:|---:|
| Ticker price | **90.0 µs, 305 B** | 121.0 µs, 8.8 KB | 102.2 µs, 13.4 KB | 75.8 µs, 897 B |
| New order | **99.4 µs, 394 B** | 144.2 µs, 13.2 KB | 149.2 µs, 23.9 KB | 75.7 µs, 905 B |
| Cancel order | **97.0 µs, 294 B** | 141.7 µs, 11.6 KB | 142.8 µs, 21.6 KB | 75.5 µs, 899 B |
| Account balance | **113.8 µs, 5.0 KB** | 159.4 µs, 13.6 KB | 197.7 µs, 45.9 KB | 78.3 µs, 911 B |
| Account info | **1.84 ms, 98 KB** | 2.41 ms, 1.28 MB | 12.40 ms, 7.56 MB | 0.47 ms, 890 B |
| Burst of 100, per request | **1.16 KB** | 13.5 KB | 13.7 KB | 10 B |

Under a burst the time per request is within the noise of the machine, so only memory is reported for it.

### WebSocket API, Native AOT

| Benchmark | Time | Memory |
|---|---:|---:|
| Ticker price | 90.1 µs | 300 B |
| New order | 99.8 µs | 361 B |
| Cancel order | 96.5 µs | 299 B |
| Account balance | 121.1 µs | 5.0 KB |
| Account info | 2.44 ms | 98 KB |

## Fairness

- The competitor's fastest mode is in every throughput table.
- Binance.Net's rate limiters are off; Axitor's are on.
- Every .NET client runs with the same GC settings, on the same machine, against the same stub and data.
- In the order book set run, Binance.Net opened two connections on its own, and the stub fed them in parallel. That is in its favor.

## What it costs

- **Loopback, not the internet.** Real network latency is the same for every client and would only add to the medians. Client cost and memory don't change.
- **One machine, Windows.** Other CPUs and Linux will give other absolute numbers. The ratios come from the code, not the machine.
- **One latency run per client.** Throughput is a median of 5 runs; latency at pace 1 takes 2 to 10 minutes per stream, so it runs once.
- **A full run takes about 5 hours,** mostly because BenchmarkDotNet builds the AOT project and the pace-1 passes run at market speed.

## Alternatives I rejected

**Microbenchmarks of parsers.** Easy to win, and they say nothing about routing, sockets, the GC or bursts.

**Benchmarks against the live exchange.** Not repeatable, capped by rate limits, and dominated by network noise.

**Medians only.** They hide the tail, which is exactly where clients differ.

**A custom high-speed replay server.** Kestrel with write coalescing already reads 2.4 million messages per second into the raw client. More engineering there would not change a single conclusion.

## How to reproduce

1. Record data with `bench/tools/recorder.cs` into `bench/Data`.
2. Build the Rust competitors in `competitors/rust`.
3. Run `bench/run-bench.ps1`. Results land in `bench/Results`.

## Related

ADR-0001, ADR-0006, ADR-0012, ADR-0013, ADR-0014