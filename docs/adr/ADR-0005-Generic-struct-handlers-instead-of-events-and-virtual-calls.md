# ADR-0005: Generic struct handlers instead of events and virtual calls

**Status:** Accepted
**Date:** 2026-11

## TL;DR

Every per-message call in the library, into a parser, the user's handler, the telemetry handler or the rate limiter, is a direct call the compiler can inline. There are no events, no delegates and no interface calls on the hot path. The trick is old but rarely used to the end: make the target a struct, pass it as a generic parameter, and let .NET build specialized native code for it. It works the same under the JIT and under Native AOT, where there is no runtime profiling to rescue a virtual call.

## The problem

The usual .NET way to deliver a message to user code is an event or a callback:

    client.OnBookTicker += ticker => Process(ticker);

It reads well, and it costs on every message. A delegate is a heap object; a lambda that captures anything makes a closure object; the call goes through an indirect pointer that the JIT can't inline. An interface-typed handler isn't much better: each call is an interface dispatch. Do that four times per message (parse, deliver, report, count) at a million messages per second, and the plumbing costs more than the work.

Modern .NET can remove some of it at run time: Dynamic PGO watches which type is really behind an interface and adds a fast path for it. But that works only under the JIT, only after warmup, and only behind a type check. Under Native AOT there is no runtime profiling at all.

## Decision

### Handlers are structs passed as generic parameters

The user's stream handler is a struct that implements an interface:

    readonly struct MyHandler : IBinanceStreamHandler<BookTickerModel>
    {
        public void OnData(int subscription, in BookTickerModel model) { ... }
        ...
    }

    session = client.Futures.UsdM.WsStreams.BookTicker.CreateSession(new MyHandler(...));

`CreateSession<THandler>` is generic with `where THandler : struct`. For a struct type argument, .NET does not share code: the JIT, or the AOT compiler ahead of time, builds a separate copy of the dispatcher for `MyHandler`. Inside that copy the call to `OnData` is a plain call to a known method, and a small one is inlined into the receive loop. The interface is only a contract checked at compile time; nothing dispatches through it at run time.

### The same pattern all the way down

- **Telemetry.** `BinanceClient<TTelemetry>` takes a struct telemetry handler. Every report (request failed, retry, reconnect, gap) is a direct call. With the default `NoOpTelemetryHandler` every method is empty, so after inlining the reports vanish from the machine code.
- **Parsers.** Stream parsers and WebSocket API result parsers expose `static abstract` methods (`TParser.TryParse(...)`). There is no parser object at all, just a static call picked at compile time. REST parsers that need state, such as the klines parser that writes into the caller's buffer, are readonly structs with an instance method, passed by value and specialized the same way.
- **Dispatchers.** The connector's stream dispatcher is a readonly struct generic over parser, model and handler. Parse, drop duplicates and deliver become one specialized method per stream type.
- **The rate limiter.** The limiter must be a class: it holds shared state behind a lock. A class type argument would make .NET share one copy of the code for all reference types and dispatch through the interface. So the class is wrapped in a one-field readonly struct, `BinanceRateLimiterRef`, which forwards the calls. The struct gets specialized code, and the call into the sealed class is direct.

### Handlers are readonly structs that point to their state

A struct handler is copied into the dispatcher, so it must not keep mutable state in its own fields. The rule is simple: implement it as a `readonly struct` and keep state in an object it holds, an array or a class. Copies of the struct then share that state. `readonly` also matters for speed: calling a method on a non-readonly struct held in a readonly field makes the compiler emit a hidden defensive copy on every call.

The ready-made handlers follow the same rule. `LatestCache<TModel>` is a class that holds the data, and its `Handler` property returns a small readonly struct that points back to it.

### Generic inside, plain type outside

The user shouldn't carry generic types around. `CreateSession<THandler>` returns a plain `BinanceStreamSession`; the generic `BinanceStreamSessionCore<TDialect, TDispatcher, TTelemetry>` behind it stays internal. Subscribe and unsubscribe go through one virtual call, and that is fine because they are cold. The per-message path never leaves specialized code.

## Results

The full per-message cost of a book ticker, from raw socket bytes to the user's handler (parse, route, drop duplicates, deliver), is 0.46 µs. Binance.Net, which delivers through events, spends 3.03 µs on the same path, and 1.49 µs in its high-performance mode. Native AOT keeps the same design with no warmup and no profiling: all dispatch is decided at compile time.

## What it costs

- **A less familiar API.** Writing a struct handler takes a minute more than writing a lambda. The ready-made handlers cover the common cases, so most users never write one.
- **More native code.** Each handler type gets its own copy of the dispatcher. For a handful of stream types that is a few kilobytes, a fair trade for zero indirect calls.
- **The client type carries the telemetry type,** as in `BinanceClient<NoOpTelemetryHandler>`. That is the price of zero-cost telemetry.

## Alternatives I rejected

**Events and delegates.** A heap object per subscription, a closure when the lambda captures, and an indirect call that never inlines, on every message.

**Interface-typed handler fields.** An interface dispatch per message. Dynamic PGO can guard it under the JIT, but not under Native AOT, and not before warmup.

**A channel for every message.** It moves each message to another thread: a queue operation and a thread hop on the hottest path. The library offers `ChannelSink` for users who need it, as an opt-in handler, not as the only road. (ADR-0015)

## Related

ADR-0003, ADR-0004, ADR-0006, ADR-0015