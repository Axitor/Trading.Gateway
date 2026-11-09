# ADR-0002: Business safety before technical beauty

**Status:** Accepted
**Date:** 2026-11

## TL;DR

In trading code, a clever optimization that is wrong once a month costs more than it ever saves. Every design choice in this library goes through one test first: what does it do to the money when something goes wrong? Only after the business logic is safe do I pick the fastest way to build it. That order is why the library can be fast and still trust its own rate limiter, retries and order book.

## The problem

Here is a real trap. A rate limiter checks whether a request fits in the window, then charges its weight. Make that lock-free to save about 20 nanoseconds, and during a burst two threads can both pass the check before either one charges. Both requests go out. The limit is crossed, the exchange bans the IP, and every order is rejected until the ban ends. Binance bans grow for repeat offenders, from 2 minutes up to 3 days. The code was faster, and it cost the account.

Another one. A request is sent near the end of a rate limit window, so it feels right to charge it to the old window. But I don't know how long the network took, or in which window the exchange counted it. If the exchange counted it in the new one and I didn't, the next burst crosses the limit. Same ban.

Both ideas look smart in a code review. Both are wrong for the business. Exchange client code is full of choices like these: what to do on a timeout, on an unknown error code, on a field the parser can't read, on a quiet socket. The technically elegant answer and the safe answer are often different.

## The rule

1. **Safe first.** When the docs are unclear or the two markets disagree, I take the reading that can't lose money or get the account banned.
2. **Nothing fails silently.** Every failure becomes an explicit result, a telemetry event or a fatal stop. A bot must never keep trading on wrong data without knowing it.
3. **Fast second.** Only then do I look for the fastest way to build the safe behavior. Usually it exists. When it doesn't, safety wins.

## How it shows up in the code

**The rate limiter takes a lock.** Check and charge across all windows happen as one step under a single lock. An uncontended lock costs a couple of atomic operations. That is nothing next to a network round trip, and it makes a burst impossible to slip through. (ADR-0007)

**Weight is never un-charged.** A request is charged to the window it was reserved in. When the exchange reports its own count in the response headers, the limiter raises its count to match and never lowers it.

**Unclear docs get the careful reading.** The spot docs say any 5xx means the execution status is unknown. The futures docs say a 503 can go either way. I treat every 5xx as unknown on every market. The WebSocket API weight rules have been unclear and have changed over time, so by default WebSocket API requests are charged to the shared REST window. Splitting them later is one flag. (ADR-0008)

**The library never resends an order on its own.** Every order needs a client order id from the user. Placing an order is not retried by default; canceling is. If the exchange answers "duplicate order", that is read as "already placed", not as an error. The library never guesses an order's status by querying the exchange behind the user's back; the strategy knows its own state better. (ADR-0009)

**Fatal means stop.** A bad signature, a revoked key or an account in liquidation throws a fatal exception: no retry can fix them. A long ban is fatal too, and it also sets the shared ban gate for as long as the exchange says. Every extra request during a Binance ban makes it longer, so even if the user catches the exception and carries on, nothing goes out until the ban ends. A fatal on the WebSocket API stops that transport for good. A clock rejection is the opposite case: one late packet on a busy VPS must not kill a trading bot, so it is skipped and shows up in telemetry. (ADR-0008)

**No event is lost without a trace.** In user data, an enum value the library doesn't know reads as UNKNOWN, the event is still delivered, and telemetry reports it. A required field that can't be read means the exchange changed its format, and the session stops: a lost fill leaves the position wrong. Account parsers throw on an unreadable margin or leverage field rather than return a zero that would mislead risk checks. (ADR-0016)

**No queue drops data quietly.** When the channel handler overflows, the consumer gets an explicit gap item, not a missing message. An order book update that can't be parsed is dropped; the next update then breaks the update id chain, and the book is rebuilt from a fresh snapshot. (ADR-0014, ADR-0015)

**Recovery never hurts trading.** When the watchdog sees the network go silent, it drops the stream connections but never the WebSocket API connections: dropping those would turn every order in flight into an unknown status. Order book resyncs may use at most half of the request weight, so a resync storm can never block orders and cancels. (ADR-0010, ADR-0014)

## What it costs

- **A lock in the limiter.** A few tens of nanoseconds per request, in exchange for a guarantee that a burst can't cross the limit.
- **Less of the exchange limit is used.** Charging the WebSocket API to the REST window and reserving weight for orders leaves some capacity unused. A ban costs far more than that capacity is worth.
- **A fatal stop is harsh.** The app stops instead of limping on. That's on purpose: a bot that can't sign requests or read its own fills must not trade.
- **Unknown status goes back to the user.** After a timeout on an order, the library reports "unknown" and lets the strategy decide. That's more work for the user, and it's the only honest answer.

## Alternatives I rejected

**Lock-free limiter with compare-and-swap per window.** A request is charged to several windows at once. Per-window CAS can't make that one atomic step, so a burst can still pass the check twice.

**Automatic order retries.** They make demos look robust. In live trading they double positions.

**Automatic reconciliation by order query.** It spends request weight at the worst moment, during an outage, and the answer can be stale. The strategy has the context to decide; the library doesn't.

**Log and continue on bad data.** A log line nobody reads while the bot trades on a wrong balance is the most expensive kind of silence.

## Related

ADR-0001, ADR-0007, ADR-0008, ADR-0009, ADR-0010, ADR-0014, ADR-0015, ADR-0016