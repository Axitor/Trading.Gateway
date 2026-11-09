# ADR-0006: Native AOT from day one

**Status:** Accepted
**Date:** 2026-11

## TL;DR

The whole library compiles to Native AOT and is safe to trim, and the build enforces it: any reflection or runtime code generation is a compile error. Hot-path parsing is hand-written over UTF-8, and the few small cold answers use source-generated System.Text.Json. I benchmark both builds against each other. AOT starts at full speed with no warmup and gives lower latency at live pace. The JIT with Dynamic PGO wins on raw throughput and heavy JSON. Both builds beat the competitors by a wide margin, so you pick the build per deployment, not per compromise.

## The problem

A trading bot restarts more often than people think: after a deploy, a crash, a config change, a moved server. Under the JIT, the first minutes after start run partly on quickly compiled, unoptimized code until the runtime gathers a profile and recompiles the hot methods. That is exactly when the bot reconnects, resyncs its books and may need to place orders.

Native AOT removes that: the code is fully compiled before the process starts. It also gives a single self-contained binary, a small container image and no JIT work at run time. But AOT has a strict rule: no reflection-driven code. Many exchange clients rely on reflection-based JSON, dynamic converters or runtime code generation, and they either break under AOT or need fragile workarounds.

## Decision

### AOT safety is enforced by the build

Every library project sets `IsAotCompatible` and `IsTrimmable`, and treats warnings as errors. The AOT and trim analyzers run on every build, so a call that would break under AOT fails the build on the developer's machine, not in production.

### No reflection anywhere

- **Hot path:** stream messages, depth updates, klines and order books are read by hand-written parsers straight from UTF-8 bytes. User data and account answers use `Utf8JsonReader`, a forward-only reader with no reflection. (ADR-0013)
- **Cold path:** a few small answers, such as server time, ticker price and account balance assets, go through System.Text.Json with a source-generated context. The serializer metadata is generated at compile time.
- **Dispatch:** handlers, parsers, dialects and the telemetry handler are generic structs (ADR-0005). The AOT compiler specializes the code for them, so every hot call is direct even without runtime profiling.

### No culture surprises

`InvariantGlobalization` is on. No culture data ships with the binary, and no parsing or formatting can change with the server's locale.

## Results

From the full benchmark run of 2026-11-07. Same machine, same recorded traffic. All tables are in ADR-0018.

| Benchmark | JIT | Native AOT |
|---|---:|---:|
| Book ticker throughput, msg/s | 1.14M | 979k |
| Depth throughput, msg/s | 484k | 439k |
| Book ticker p50 / p99 / p99.9 at live pace, µs | 102 / 215 / 237 | 84 / 198 / 294 |
| Agg trade p50 / p99 at live pace, µs | 97 / 207 | 80 / 164 |
| Depth p50 / p99 at live pace, µs | 96 / 210 | 75 / 152 |
| Klines, 1,500 candles over REST | 638 µs | 554 µs |
| Order book snapshot into buffers, REST | 189 µs | 182 µs |
| Account info over REST | 1.15 ms | 1.55 ms |
| Exchange info over REST | 3.70 ms | 4.20 ms |

What the numbers say:

- **AOT is faster on lean code.** The positional klines parser, the order book snapshot and the order path run 4–13% faster under AOT. At live pace, AOT medians are 18–22% lower on every stream, and p99 is lower on every stream too.
- **The JIT is faster on heavy JSON and at full load.** Dynamic PGO tunes hot loops to the real traffic, and AOT can't. Large `Utf8JsonReader` answers such as account info, leverage brackets and exchange info run 14–35% slower under AOT, and peak stream throughput is 3–15% lower.
- **Both builds win where it counts.** The AOT build still does 979k book tickers per second, against 291k for Binance.Net and 129k for the official Rust client, and at live pace it beats every client on every stream, medians included.

### Which build to use

- **JIT** for a long-running bot that needs the highest throughput and handles large account answers often. Warm the hot paths at startup.
- **Native AOT** for fast restarts, small containers, no JIT work at run time, and the lowest latency at live pace.

## What it costs

- **Hand-written parsers.** A reflection serializer needs one attribute per model; a hand-written reader needs real code and tests per message type. That is the same code that makes the hot path allocation-free, so it pays twice. (ADR-0013)
- **Heavy JSON is slower under AOT.** Without runtime profiling, big document reads lose 14–35%. Those answers sit on cold paths: account snapshots and exchange info, not per-message data.

## Alternatives I rejected

**Reflection-based System.Text.Json.** Breaks under trimming, needs runtime metadata, and allocates per object. It is not an option for the hot path anyway.

**JIT only.** It keeps the best peak throughput, but closes the door on fast-start, small-footprint deployments, and keeps a warmup window after every restart.

**ReadyToRun only.** It shortens startup, but the code still runs through tiered JIT and keeps the runtime and its JIT in the process. AOT gives one self-contained binary.

## Related

ADR-0003, ADR-0005, ADR-0013, ADR-0018