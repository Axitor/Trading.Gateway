# ADR-0003: Hot path and cold path, two sets of rules

**Status:** Accepted
**Date:** 2026-11

## TL;DR

I split all code into two kinds. The hot path runs for every message and every request: parsing, dispatch, signing, rate limiting. There, nothing allocates, nothing logs, nothing boxes and nothing hides a virtual call. The cold path runs once or rarely: startup, subscribe, reconnect, big reference data. There, clear code wins over clever code. That split is how the stream hot path reaches zero bytes per message while the rest of the library stays readable.

## The problem

Most libraries apply one set of rules to all their code, and that fails in one of two ways.

If everything is written for convenience, the hot path fills with garbage: a string per field, an object per message, a closure per callback, a log line per event. Each one is cheap alone. At a million messages per second they add up to hundreds of garbage collections, and every collection is a pause in the one place you can't afford it.

If everything is written for speed, the startup code turns into pooled buffers and hand-rolled parsers nobody can maintain, for code that runs once per process.

The answer is to know which path you are on and to apply the rules that fit it.

## Decision

### The hot path

Code that runs per message, per request or per order event. The rules:

- **No allocations.** No strings, no per-message objects, no LINQ, no closures. Temporary buffers live on the stack (`stackalloc`) or come from a pool. The HMAC signature, for example, is hashed and hex-encoded in two stack buffers, and the query string is built by a `ref struct` builder that writes into a caller's buffer and never touches the heap.
- **Flat structs with public fields.** Messages are plain data: public fields, no properties, no methods. A property on a struct passed by `in` can force a hidden defensive copy; a field can't. Logic that works on the data lives in static helpers, not in the struct.
- **Fixed-size data stays inline.** A partial depth update holds its 20 levels in an `[InlineArray(20)]` field: no array, no allocation, one contiguous block of memory.
- **Packed identifiers instead of strings.** A ticker is a 16-byte value (`Symbol`, one `UInt128`), a client order id a 36-byte inline buffer. Comparing two symbols is one 128-bit compare. Every order event carries its id with zero allocations.
- **Prices are double.** A double is 8 bytes and the CPU does its math in hardware. A decimal is 16 bytes, and its math runs in software. (Why that is safe is below.)
- **No logging.** The library never writes a log line. Every event goes to a telemetry handler: a struct the user passes as a generic parameter. The JIT specializes the code for it, so the default no-op handler compiles to nothing at all. (ADR-0005)
- **No throw in hot methods.** Throws sit in small helper methods marked `[DoesNotReturn]`, so the hot method stays small enough to inline.

### The cold path

Code that runs at startup, on subscribe, on reconnect, or for big reference data like exchange info. Here I relax the rules on purpose:

- A subscribe request may allocate a `TaskCompletionSource`. Subscribes are rare, and simple code is worth more there.
- Options are read once in constructors into readonly fields. The hot path never touches the options object again.
- Exchange info and account answers return arrays sized to their content. They are collected in a pooled buffer first and copied out once, at their exact size.

### Why double is safe for prices

The exchange sends prices and quantities as short decimal text with at most 15 significant digits, which is exactly what a double holds without loss. The parser turns each text into the nearest double (ADR-0013), so one price text always becomes one and the same double. On the way out, order endpoints round every price to the tick and every quantity to the step, by the symbol filters passed with the order, and write them as plain decimal text. The library never sums long chains of prices, which is where float error would show up.

## Results

- **0 bytes per book ticker message** and a few bytes per depth update on the stream hot path. Binance.Net allocates 1–10 KB per message.
- **Zero garbage collections** in every stream benchmark run, at up to 1.14 million messages per second on one connection.
- **3 KB for 1,500 candles over REST**, where Binance.Net needs 1.76 MB: the candles go straight into the caller's buffer as flat structs.

## What it costs

- **Doubles need care at the edges.** Display and order output must go through the rounding and formatting helpers, not `ToString()`. Order endpoints do that for every order they send.
- **Flat structs are less friendly.** No computed properties, no validation in setters, no `ToString()`. That is the price of zero copies and zero hidden calls.
- **Two rule sets need judgment.** Every new piece of code needs a decision about which path it is on. A wrong call either wastes speed or wastes readability.

## Alternatives I rejected

**Decimal for prices.** Twice the memory per price, math in software, and no real gain: the exchange data fits a double exactly, and outbound values are rounded to the tick anyway.

**Records and classes for messages.** Convenient, but every message becomes a heap object, and that is exactly the garbage this library exists to remove.

**ILogger with source-generated messages.** Better than string logging, but still a call through an interface on every event, and it forces one logging stack on every user. A struct telemetry handler costs nothing when unused and lets the user log, count or ignore events as they like.

## Related

ADR-0001, ADR-0005, ADR-0013, ADR-0015