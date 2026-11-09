# ADR-0007: A rate limiter that can't get you banned

**Status:** Accepted
**Date:** 2026-11

## TL;DR

The rate limiter is the one part of an exchange client where a small bug ends the trading day. Mine charges every request to all of its windows in one atomic step under a lock, so a burst can never slip through. It never lowers a count the exchange reported. It keeps a share of the weight for orders, so order book resyncs can't block a cancel. And it holds a request until it fits, inside the request's time to live, instead of handing a rejection back to the strategy.

## The problem

Binance counts what every IP and account sends in several windows at once. For futures that is request weight per minute and order count per minute and per 10 seconds. For spot it is request weight per minute and orders per 10 seconds and per day. Stream connections have their own limit on attempts per 5 minutes. Cross any of them and the answer is 429, then 418: an IP ban that grows with every repeat.

Getting this right is harder than it looks:

- **One request hits several windows.** A futures order counts against request weight and both order windows. Checking them one by one, without holding them together, lets a burst pass the first check twice.
- **The client's count is never exact.** Other apps may share the IP or account. A request sent at the end of a minute may be counted by the exchange in the next one. Headers report the real count, but only after the response comes back, while a burst is already in flight.
- **Not all requests are equal.** A burst of order book snapshots after a reconnect can eat the whole minute of weight. Then the cancel that should close a losing position has to wait.
- **The docs move.** Whether WebSocket API requests share weight with REST has been described differently across markets and across doc updates.

## Decision

### One lock, all windows, all or nothing

Each endpoint declares a set of windows as `[Flags]` buckets, for example request weight plus both futures order windows. `TryReserve` takes one lock, checks every named window, and only if all of them fit, charges all of them. If any one is full, nothing is charged, and the caller gets the exact time until that window resets. The lock is `System.Threading.Lock`; uncontended, it costs a couple of atomic operations.

### Fixed windows, raised by the exchange, never lowered

Each window counts weight inside the current period: one minute, ten seconds, five minutes or a day. The period index and the spent weight are packed into one 64-bit value. After a response, the dialect reads the exchange's own count (`X-MBX-USED-WEIGHT-1M`, `X-MBX-ORDER-COUNT-10S` and the others on REST, the `rateLimits` block on the WebSocket API) and raises the window to match. It never lowers it: if the exchange counts less than I do, my number stays. If it counts more, for example because another app shares the IP, mine catches up at once.

Most responses report nothing new. A lock-free pre-check reads the packed value and returns without taking the lock unless a raise is really needed.

### Wait, don't reject

When a request doesn't fit, the transport waits until the window resets, plus a small random jitter so that a queue of requests doesn't hit the next window all at the same moment, and tries again. The ban gate and the network gate are checked first. The wait is bounded by the request’s time to live when the caller sets one: a request that can no longer be sent in time is dropped with an explicit “dropped by TTL” result and a telemetry event.

### A reserved share for orders

Order book snapshots are the heaviest routine requests. They are charged to the market's request weight and to a second, smaller window of their own: by default 50% of the request weight. However many books resync at once, at least half of every minute stays free for orders and cancels. (ADR-0014)

### Shared weight by default, one flag to split

By default, WebSocket API requests are charged to the REST request weight window of their market. The USD-M docs say the two are now separate; I still keep them shared by default, because a wrong guess in that direction only costs unused capacity, while the other direction costs a ban. Each market has its own flag to split them. USD-M and COIN-M share one set of futures windows: the docs say so, and a live probe confirmed it.

### Limits come from configuration

Request and order limits depend on the account tier, so the user sets them in `BinanceLimits`. The library makes no hidden request at startup to fetch them, and it doesn't second-guess the values.

## What it costs

- **A lock on every request.** A few tens of nanoseconds, against a network round trip of tens of microseconds at the very least. It buys a guarantee that no burst crosses a limit.
- **Some capacity left on the table.** Shared WebSocket API weight and the reserved share for orders mean the bot will never use 100% of what Binance allows. That is the point.
- **Requests may wait.** Under heavy load a request waits for the next window instead of failing fast. The TTL caps the wait, and critical requests have their own slots.
- **Correct limits are the user's job.** A wrong tier in the configuration means a wrong limiter.

## Alternatives I rejected

**Lock-free compare-and-swap per window.** It can't charge several windows as one step, so a burst still slips through. (ADR-0002)

**Trusting the headers alone.** Headers arrive after the response. During a burst, dozens of requests are in flight before the first count comes back.

**Rejecting a request that doesn't fit.** It pushes retry timing into every strategy. The library knows exactly when the window resets; the strategy doesn't.

**Token bucket or sliding window.** A different shape from the windows the exchange reports. It either wastes capacity or drifts away from the exchange's own count, and then the headers can't correct it.

**Fetching limits from exchange info at startup.** A hidden request on every start, and the answer still doesn't know about other apps on the same IP.

## Related

ADR-0002, ADR-0008, ADR-0011, ADR-0014