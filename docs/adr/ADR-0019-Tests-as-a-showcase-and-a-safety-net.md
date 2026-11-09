# ADR-0019: Tests as a showcase and a safety net

**Status:** Accepted
**Date:** 2026-11

## TL;DR

Tests here guard what matters to the money: business rules, the exact bytes on the wire, and behavior when things break. There are three layers. Unit tests run against real exchange messages. Integration tests drive the whole client against a local fake exchange over real sockets. Live tests run against Binance mainnet without placing a single order. There are no mocks, no assertion libraries and no `Thread.Sleep`. The test names read like a spec of how the library behaves under failure.

## The problem

Tests can give a false sense of safety:

- **Mocks test the mock.** A mocked socket that "returns an error" proves the code handles the error the author imagined, not the one the exchange sends.
- **Sleeps make tests slow and flaky.** A test that waits 500 ms "for the reconnect" fails on a busy CI box and wastes time everywhere else.
- **Tests of internals freeze the design.** A test per private method makes every refactor a rewrite of the tests.
- **More tests is not more safety.** A thousand trivial tests hide the ten that matter.

For a trading library the question is narrow and hard: does it keep its promises when the exchange misbehaves?

## Decision

### Test three things

1. **Business rules.** The limiter charges all windows or none. An order is rounded to the tick and down to the step. A book never applies an update that breaks the chain. An event is never lost without a trace.
2. **The wire contract.** The exact bytes of requests and signatures. Parsing of real recorded exchange messages. Every error code the classifier knows, row by row, with its status and market.
3. **Behavior under failure.** Bans, dead networks, silent sockets, rotations, lost connections with orders in flight, expired keys, unanswered subscribes.

### Unit tests on real messages

Parsers are tested on frames and bodies recorded from Binance, and on SBE frames built from Binance's official schema. Around them: the decimal parser and formatter, `Symbol` and `ClientOrderId`, the seqlock, the subscription registry, the rate limiter, the order rules, both sides of the order book, and the ready-made handlers. Error classification is a table: one row per exchange code, status and market.

### Integration tests on real sockets

`FakeExchangeServer` is a real WebSocket server on loopback. It speaks the Binance protocol per route, can refuse handshakes with a given status and `Retry-After`, drop connections, go silent or announce a shutdown. The client under test is the real client, with real transports, gates, lanes and sessions. Only the REST socket is replaced, with an HTTP handler that answers from recorded bodies.

The test names say what is guaranteed:

- `OrderOnServerErrorIsSentOnce`
- `OrderLostWithConnectionIsNotResent`
- `TimedOutOrderIsUnknownAndLateAnswerIsUnmatched`
- `FullInFlightTableRejectsAtOnce`
- `RuleViolationIsNotChargedToLimiter`
- `ExpiredRequestIsNeverSent`
- `LongBanIsFatalAndStays`
- `HandshakeIpBanIsFatalAndStopsRest`
- `NetworkFailureRepairsThenRetries`
- `SilenceRepairsNetworkAndReconnectsStreams`
- `QuietUserStreamIsNeverDropped`
- `WatchdogSilenceRotatesWsApiMakeBeforeBreak`
- `AgedConnectionRotatesWithoutGap`
- `ServerShutdownRotatesWithoutGapOrCopies`
- `ExpiredKeyMovesToNewKeyWithOneGap`
- `UnansweredSubscribeStartsOverUntilConfirmed`
- `DepthOverSbeSyncsOrderBookFromRestSnapshot`

### No sleeps, no mocks, no assertion library

- **No sleeps.** A test waits for a condition, polled every 5 ms with a hard limit. Timeouts that matter, like the watchdog's, are set short through the options: 300 ms instead of 5 s. A test takes as long as the behavior takes, not a guess.
- **No mocks.** Fakes are real servers and real handlers. Telemetry and stream handlers in tests are struct handlers that record what they receive, the same kind of handler a user would write.
- **No assertion library.** xUnit v3 and plain `Assert`. One dependency less, and nothing between the test and what it checks.

### Live tests on mainnet, no orders

`Tests.Live` runs outside CI, with keys from the environment, against Binance mainnet:

- REST and WebSocket API market data;
- streams on all three markets: book ticker, aggregated trades, klines, partial depth, and the order book set with `ResyncCount == 0`;
- signed account endpoints;
- the test order endpoint, which validates an order and places nothing;
- user data on USD-M, COIN-M and spot. A leverage change on BTCUSDT moves one step and back to make the exchange send a real config update event;
- SBE: a wrong key is refused at the handshake and stops the session at once. SBE data tests run when an Ed25519 key is provided.

A raw capture helper connects with a bare `ClientWebSocket`, bypassing the library, and prints the first frames as hex. When the exchange changes something, it shows exactly what came over the wire.

### What I don't test

Trivial readers and helpers that the higher-level tests already go through, request weights copied from the docs, and stress tests of primitives whose correctness rests on a short, reviewed proof. A test that only repeats the code it tests adds work, not safety.

## Results

About 300 tests, many of them tables of cases. Unit, integration and live suites are green. The integration suite runs the whole client over real sockets in seconds.

## What it costs

- **Integration tests are slower than unit tests.** Real sockets take real milliseconds. They still finish in seconds, because nothing sleeps.
- **The fake exchange must match the real one.** It's kept in line with live probes and the live tests; when they disagree, the live tests win.
- **Live tests need keys and a network.** They stay out of CI and run before a release.

## Alternatives I rejected

**Mocking frameworks.** They test the author's idea of the exchange, not the exchange.

**FluentAssertions and similar.** A dependency for nicer messages. Plain asserts with clear names say the same.

**Sleeps and fixed delays.** Slow on fast machines, flaky on slow ones.

**Coverage targets.** They reward tests of getters and punish the hard tests that matter.

## Related

ADR-0002, ADR-0008, ADR-0010, ADR-0014, ADR-0016, ADR-0017, ADR-0018