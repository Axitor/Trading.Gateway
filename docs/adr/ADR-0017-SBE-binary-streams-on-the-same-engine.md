# ADR-0017: SBE binary streams on the same engine

**Status:** Accepted
**Date:** 2026-11

## TL;DR

Binance spot offers market data in SBE, a binary format: fields at fixed offsets, prices as integer mantissas, no text to scan. I added it as a fourth dialect on the same core, with no change to the transport, the routing or the handlers. SBE depth feeds the same order books as JSON depth, and a price from a binary frame lands on exactly the same double as the same price from a JSON snapshot. The reader checks every byte against the frame length, so a broken frame never throws on the receive loop. A newer schema version either parses or is reported as unknown, never misread.

## The problem

SBE is fast because it is raw: a header, a fixed root block, repeating groups, variable-length strings. That makes it unforgiving:

- **No envelope, no stream name.** A JSON stream message says which stream it belongs to. An SBE frame doesn't. Routing has to come from the bytes themselves.
- **Schemas evolve.** A newer version can append fields to a block or add a group. A reader hard-wired to version 0 offsets misreads everything after the change.
- **Binary means no second chance.** A truncated or corrupted frame read without bounds checks is a crash on the receive loop, or worse, garbage that looks valid.
- **Two formats, one book.** If a spot book is synced from a JSON REST snapshot and updated from SBE depth, the price `67123.45` must be the same double in both. Otherwise one level splits into two.

## Decision

### A fourth dialect, nothing else changes

SBE is one more `IExchangeDialectWsStream` (ADR-0004). The core transport, shards, rotation, watchdog and gap reports are exactly the ones the JSON streams use. Subscribe replies and `serverShutdown` still come as JSON in text frames, and the dialect reads them the same way the JSON dialect does.

### Routing by what the frame carries

Binary market data frames are routed by their template id as the channel and their symbol as the key. The symbol sits at the end of the message, after the groups, and the dialect steps over the groups to reach it. The registry lookup is the same lock-free table as for JSON stream names. (ADR-0012)

### Bounds-checked, version-tolerant reading

`BinanceSbeReader` reads header, blocks, groups, strings and decimals straight from the frame:

- **every read is checked against the frame length first,** so a broken frame reads as false, is counted as unreadable, and never throws on the receive loop;
- **root blocks and group entries are stepped over by the lengths the frame itself sends,** not the version 0 lengths, so fields a newer version appends are skipped cleanly;
- **a version that adds a group** shifts the symbol out of reach, so its frames are reported as unknown, not misread.

### One price, one double

SBE sends decimals as a mantissa and a power-of-ten exponent. `MantissaDecimal` turns them into a double with one exact division or multiplication when both parts are exact doubles, the same rule `FastDecimalParser` applies to text (ADR-0013). `6712345` with exponent -2 and the text `"67123.45"` become the same double. SBE depth feeds the same `IBinanceDepthHandler` and the same `OrderBookSet` as JSON depth, with the same sync checks. (ADR-0014)

### Trades: one frame, many trades

An SBE trades event can carry several trades. The dispatcher opens the frame once, then hands each trade to the handler on its own, dropping any trade id it already delivered during a rotation overlap.

### Time in microseconds where the format has it

SBE models keep event times in microseconds (`*Micros` fields), as the format sends them. The shared depth header stays in milliseconds, because the order book doesn't use event time and JSON depth has only milliseconds.

### Ed25519 key, checked at the door

SBE streams need an Ed25519 API key in the handshake header. A session can't be created without one, and the error says which option to set. A wrong key is rejected at the handshake and stops the session with a fatal error at once: no reconnect loop hammering the exchange with a key that will never work.

## What it costs

- **A reader per template.** Four stream types, four parsers, each pinned by unit tests on frames built from Binance's official schema.
- **Schemas that add groups need a new version of the library.** Their frames are reported as unknown in the meantime, never misread.
- **An extra key.** SBE needs an Ed25519 key in addition to the HMAC key used for everything else.

## Alternatives I rejected

**A generated SBE codec.** Code generators produce flyweight objects and accessors for the whole schema. I need four messages, read once each, into flat structs. Hand-written reads are smaller and allocate nothing.

**Trust the frame length implicitly.** Faster by a few compares, and one corrupted frame away from an exception on the receive loop.

**Read by version 0 offsets.** Simple until the first schema update, then quietly wrong.

**A separate transport for binary streams.** Duplicate reconnect, rotation and watchdog logic for a different byte format. The dialect split exists precisely to avoid that.

## Related

ADR-0004, ADR-0012, ADR-0013, ADR-0014