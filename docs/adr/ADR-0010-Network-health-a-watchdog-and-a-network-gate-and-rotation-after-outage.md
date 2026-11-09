# ADR-0010: Network health: a watchdog, a network gate and rotation after every outage

**Status:** Accepted
**Date:** 2026-11

## TL;DR

The worst network failure is the one that looks healthy: the socket is open, pings come back, and no data arrives. The bot trades on a frozen price. This library listens to a stream that is never quiet. When it goes silent, every part of the client learns about it at once through one shared network gate. REST, new WebSocket API requests and reconnects wait until the network really works again. Then every connection is replaced make-before-break, because any of them may have died quietly during the outage.

## The problem

Network trouble in live trading rarely looks like a clean disconnect:

- **Half-dead connections.** After a route change, a NAT timeout or a VPN reconnect, a socket can stay open with nothing behind it. Sometimes the exchange's stream node gets stuck, pongs still come back, and data stops. Ping and pong can't see this.
- **Quiet is not dead.** A small altcoin stream can be silent for minutes for good reason. A per-connection "no data" timeout either fires on healthy quiet streams or is set so long that it's useless. That's why such timeouts usually ship turned off.
- **Everyone finds out alone.** When the network drops, REST requests time out one by one and burn their retries. Every socket starts its own reconnect loop. The connection limit fills with useless attempts. No part of the client knows what the others already know.
- **"Back" is not "healthy".** When the network returns, old connections may still look open. An order sent into one of them disappears.

In my own live trading, silently dead sockets were the failure that cost the most, and the hardest to see.

## Decision

### One network gate for the whole client

REST, the WebSocket API and the streams share one `NetworkGate`. Any transport that sees a network fault trips it. Network faults are socket, HTTP and IO errors, a missed keep-alive, and a watchdog bark. Only the thread that wins the switch from healthy to down starts the repair loop, so a hundred failing requests start one repair, not a hundred.

The repair loop sends a plain REST ping to the exchange at a fixed interval. The first successful answer marks the gate healthy. While the gate is down:

- **REST requests wait** at the gate instead of failing one by one;
- **new WebSocket API requests wait** for a working lane;
- **every reconnect waits**, so outage time doesn't fill the connection limit.

When the gate heals, waiting REST requests resume after a small random delay, so they don't all hit the exchange in the same millisecond. On the healthy path the gate costs one volatile read.

### A watchdog on a stream that is never quiet

The watchdog holds its own connection to a stream that is busy in any normal market: by default the BTCUSDT book ticker, which updates many times per second. If no message arrives for the timeout (5 seconds by default), it barks:

- telemetry gets a watchdog event with the length of the silence, so frequent barking is visible;
- the watchdog's own connection and every user stream connection are dropped and reconnected;
- every subscription gets an explicit gap, and is subscribed again on the new connection;
- the network gate trips, and the repair loop starts.

The watchdog counts silence only while its connection is up and has already delivered data, and one silence barks once.

Two rules keep recovery from doing harm:

- **The WebSocket API is never dropped on a bark.** Dropping it would turn every order in flight into an unknown status, and the WebSocket API may be healthy: the silence can come from one stuck stream node.
- **A user stream is never dropped for its own silence.** A quiet altcoin is normal. Only the watchdog's always-busy stream decides that the network is dead.

### Every connection is replaced after every repair

The gate counts repairs. After each one, whoever tripped the gate, every WebSocket API lane and every stream connection opened before the repair is replaced make-before-break:

- a new connection opens and takes new requests at once;
- streams are subscribed on it before it becomes current;
- the old connection drains and closes, and requests already on it still get their replies if it lived.

During an outage a connection can die without noticing; ping and pong need seconds to find out. Replacing it right after the repair means no new order ever goes into a socket that only looks alive.

## What it costs

- **One extra connection,** always open for the watchdog.
- **A false bark costs a resubscribe.** If one exchange stream node gets stuck while the network is fine, user streams are dropped and get a gap. That's a gap the user can see and handle, instead of a frozen price they can't.
- **New handshakes after every repair,** and attempts charged to the connection limit. Repairs are rare; trading into a dead socket is worse.
- **REST waits even if only a socket failed.** The gate is shared on purpose: a network fault is never local to one transport.

## Alternatives I rejected

**Per-connection no-data timeouts.** They can't tell a quiet altcoin from a dead network, so they either fire on healthy streams or never fire.

**Ping and pong only.** They prove the connection is open, not that data flows.

**Independent reconnect loops per transport.** Reconnect storms, burned retries, and three different ideas of whether the network works.

**Keep using old connections after an outage.** Fine until the first order goes into a socket that died quietly.

## Related

ADR-0002, ADR-0008, ADR-0011, ADR-0012