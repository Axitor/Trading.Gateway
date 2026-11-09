# ADR-0013: Parsing without a parser

**Status:** Accepted
**Date:** 2026-11

## TL;DR

Most exchange clients spend their time and memory in JSON parsing: they build a tree or an object graph for every message, then copy the values out of it. I read the bytes the exchange sends straight into flat structs, in one pass, with no tree, no strings and no reflection. Prices go through a fast decimal parser that gives exactly the double a full parser would. Big REST answers are written into the caller's own buffers. The result: 1,500 candles in 3 KB instead of 1.76 MB, and a depth update in 0.97 µs instead of 15.0.

## The problem

A typical Binance depth update is a few hundred bytes of JSON: an event header and two arrays of `["price","quantity"]` pairs. A general-purpose client turns it into:

- a string for every price and quantity;
- an object or a tree node for every level;
- a list for every side;
- and finally the typed model, copied out of all of the above.

The official Rust client does exactly this through a JSON value tree: 314 allocations for one depth update, 38 per candle in a klines answer. Binance.Net allocates 6–10 KB per depth message. All of that becomes garbage a moment later.

And the cost is not only speed. If the order book is keyed by price, the price text in a stream and the same text in a REST snapshot must turn into the very same double. If they don't, one level splits into two.

## Decision

### Hand-written UTF-8 readers for market data

Stream messages are flat JSON objects with a stable key order. `BinanceUtf8Reader` reads them straight from the UTF-8 bytes:

- each read first checks for the expected key right at the cursor, which is almost always a hit;
- if a field was skipped, it searches ahead; if the order changed, it searches the whole object;
- fields the parser doesn't need are skipped at the cursor, so a new field added by the exchange keeps the next read on the fast path.

Level arrays are read in one pass by `BinanceDepthReader`. Depth streams and the REST snapshot use the same reader, so one price text always becomes one double.

### A decimal parser that is fast and exact

Exchanges send prices and quantities as short plain decimals, such as `"67123.45"`. `FastDecimalParser` collects the digits as an integer. When the integer fits a double exactly (at most 2^53) and there are at most 22 decimal places, the value is one division by an exact power of ten. That single division rounds the same way a full parser does: the classic Clinger fast path. Anything else, such as an exponent or too many digits, falls back to the BCL's `Utf8Parser`. The fast path is exact, not approximate.

For SBE binary streams, which send a mantissa and an exponent, `MantissaDecimal` applies the same rule. The same price from a binary stream and from a JSON snapshot lands on the same order book level.

On the way out, `PlainDecimalFormatter` writes doubles as plain decimal text rounded to 15 significant digits: no exponent form, which the exchange rejects, and no binary noise like `0.30000000000000004`.

### Strict about layout, loud about surprises

The exchange writes levels and kline rows compactly, with no spaces. The readers parse exactly that layout in one pass. Any other layout reads as malformed rather than being guessed at, and a malformed message is reported, never silently patched. (ADR-0002)

### Positional reading for klines

A klines answer is an array of arrays with no field names. The parser reads it by position in one pass: times and counts bare, prices and volumes quoted. Each row is written straight into a `Kline` struct in a `Memory<Kline>` buffer the caller owns. The buffer length is the limit, and the call returns how many candles it wrote. There is no result list, no per-candle object, nothing to collect.

### Caller buffers for the order book snapshot

The REST order book snapshot has an overload that writes levels into the caller's bid and ask buffers, the same way the order book set loads its own snapshots (ADR-0014). A 1,000-level snapshot then costs 3 KB, all of it HTTP plumbing.

### Pooled, then copied once at exact size

Answers with an unknown number of entries, such as exchange info, leverage brackets and account positions, return arrays the user keeps. They are collected in a pooled buffer first, then copied out once at their exact size. A growing list would allocate every size along the way. For answers big enough to land on the Large Object Heap, that means several large arrays per call instead of one.

### Plumbing kept thin

The REST body is read whole into a pooled buffer and parsed as one span. Response decompression, cookies and redirects are turned off: exchanges don't need them, and each one adds work to every request. User data events and account answers, which are rarer and richer, use `Utf8JsonReader`: no tree, no reflection. (ADR-0006)

## Results

| Benchmark | Axitor | Binance.Net | binance-sdk (Rust) |
|---|---:|---:|---:|
| Klines, 1,500 candles | **638 µs, 3 KB** | 5.49 ms, 1.76 MB | 8.18 ms, 3.97 MB |
| Klines, 500 candles | **252 µs, 3 KB** | 1.96 ms, 589 KB | 2.90 ms, 1.37 MB |
| Order book snapshot, 1,000 levels | **189 µs, 3 KB** | 1.24 ms, 415 KB | 917 µs, 484 KB |
| Depth update, client cost over raw | **0.97 µs** | 15.0 µs | 32.1 µs |
| Depth 20 update, client cost over raw | **1.81 µs** | 24.2 µs | 49.7 µs |

## What it costs

- **More code per message type.** A reader per format, and tests that pin it to real exchange messages. It's the code that makes the hot path allocation-free.
- **Strict layout.** If the exchange changes how it writes levels or rows, the reader reports malformed data instead of guessing. That shows up at once in telemetry and in the live tests, not as quietly wrong prices.
- **Caller buffers ask for a size up front.** The user decides how many candles or levels to read. That's the same decision as the limit parameter, made once.

## Alternatives I rejected

**System.Text.Json deserialization into classes.** An object per message and per level, and reflection or generated metadata on top.

**A JSON tree (`JsonDocument`, or `serde_json::Value` in Rust).** A full tree for every message, then a second pass to copy values out. Exactly what makes the Rust client slow.

**`double.Parse` on a string.** A string per number. And `Utf8Parser` alone, with no fast path, is slower on the short decimals exchanges actually send.

**Decimal everywhere.** Covered in ADR-0003: twice the memory and software math, for precision the exchange data doesn't need.

## Related

ADR-0002, ADR-0003, ADR-0006, ADR-0012, ADR-0014, ADR-0017