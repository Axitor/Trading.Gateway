using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

/// <summary>
/// Writes request params as JSON and, for signed requests, the same params as a query string for the signature.
/// Endpoint params come in name order; auth params are merged in on the fly, so nothing is sorted.
/// </summary>
internal ref struct WsApiRequestBuilder
{
    private const byte KindText = 0;
    private const byte KindInteger = 1;
    private const int MaxInt64Digits = 20;

    // Plain text of any price or quantity an exchange lists, down to 1E-47.
    private const int MaxDecimalBytes = 64;

    private readonly Span<byte> _json;
    private readonly Span<byte> _query;
    private readonly Span<byte> _pending;
    private ReadOnlySpan<byte> _lastKey;

    private int _jsonLength;
    private int _queryLength;
    private int _pendingWritten;
    private int _pendingRead;

    /// <summary>Creates a builder. An empty query buffer means the request is not signed.</summary>
    public WsApiRequestBuilder(Span<byte> json, Span<byte> query, Span<byte> pending)
    {
        _json = json;
        _query = query;
        _pending = pending;
    }

    /// <summary>JSON bytes written so far.</summary>
    public readonly int Length => _jsonLength;

    /// <summary>The signed params as a query string.</summary>
    public readonly ReadOnlySpan<byte> Query => _query[.._queryLength];

    /// <summary>Queues a text auth param. Call in name order, before any endpoint param.</summary>
    public void AddAuthText(scoped ReadOnlySpan<byte> key, scoped ReadOnlySpan<byte> value) => AddPending(KindText, key, value);

    /// <summary>Queues an integer auth param. Call in name order, before any endpoint param.</summary>
    public void AddAuthInteger(scoped ReadOnlySpan<byte> key, long value)
    {
        Span<byte> digits = stackalloc byte[MaxInt64Digits];
        value.TryFormat(digits, out int written, default, CultureInfo.InvariantCulture);
        AddPending(KindInteger, key, digits[..written]);
    }

    /// <summary>Appends an integer param, sent as a JSON number.</summary>
    public void AppendInteger(ReadOnlySpan<byte> key, long value)
    {
        Span<byte> digits = stackalloc byte[MaxInt64Digits];
        value.TryFormat(digits, out int written, default, CultureInfo.InvariantCulture);
        AppendParam(key, digits[..written], quoted: false);
    }

    /// <summary>Appends a text param. The value must be plain ASCII that needs no escaping.</summary>
    public void AppendText(ReadOnlySpan<byte> key, scoped ReadOnlySpan<byte> value) => AppendParam(key, value, quoted: true);

    /// <summary>Appends a symbol param, sent as a JSON string.</summary>
    public void AppendSymbol(ReadOnlySpan<byte> key, Symbol symbol)
    {
        Span<byte> text = stackalloc byte[Symbol.MaxLength];
        symbol.TryWriteTo(text, out int written);
        AppendParam(key, text[..written], quoted: true);
    }

    /// <summary>Appends a price or quantity as plain decimal text, rounded to 15 significant digits. Sent as a JSON string.</summary>
    public void AppendDecimal(ReadOnlySpan<byte> key, double value)
    {
        Span<byte> text = stackalloc byte[MaxDecimalBytes];

        if (!PlainDecimalFormatter.TryFormat(value, text, out int written))
        {
            ThrowOverflow();
        }

        AppendParam(key, text[..written], quoted: true);
    }

    /// <summary>Appends a flag, sent as the JSON string "true" or "false".</summary>
    public void AppendBoolean(ReadOnlySpan<byte> key, bool value) => AppendParam(key, value ? "true"u8 : "false"u8, quoted: true);

    /// <summary>Appends a client order id, sent as a JSON string.</summary>
    public void AppendClientOrderId(ReadOnlySpan<byte> key, in ClientOrderId id)
    {
        Span<byte> text = stackalloc byte[ClientOrderId.MaxLength];
        id.TryWriteTo(text, out int written);
        AppendParam(key, text[..written], quoted: true);
    }

    /// <summary>Appends the signature. Goes to the JSON only, never into the signed query.</summary>
    public void AppendSignature(scoped ReadOnlySpan<byte> key, scoped ReadOnlySpan<byte> value) => WriteJson(key, value, quoted: true);

    /// <summary>Writes the auth params that sort after every endpoint param. Call once the endpoint is done.</summary>
    public void Finish()
    {
        while (_pendingRead < _pendingWritten)
        {
            WritePending();
        }
    }

    private void AppendParam(ReadOnlySpan<byte> key, scoped ReadOnlySpan<byte> value, bool quoted)
    {
        // Merge: auth params that sort before this key go first.
        while (_pendingRead < _pendingWritten && PendingKey().SequenceCompareTo(key) < 0)
        {
            WritePending();
        }

        WriteParam(key, value, quoted);
    }

    private void WritePending()
    {
        byte kind = _pending[_pendingRead];
        int keyLength = _pending[_pendingRead + 1];
        int valueLength = _pending[_pendingRead + 2];

        ReadOnlySpan<byte> key = _pending.Slice(_pendingRead + 3, keyLength);
        ReadOnlySpan<byte> value = _pending.Slice(_pendingRead + 3 + keyLength, valueLength);
        _pendingRead += 3 + keyLength + valueLength;

        WriteParam(key, value, quoted: kind == KindText);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly ReadOnlySpan<byte> PendingKey() => _pending.Slice(_pendingRead + 3, _pending[_pendingRead + 1]);

    /// <summary>Queued entry: kind, key length, value length, key, value.</summary>
    private void AddPending(byte kind, scoped ReadOnlySpan<byte> key, scoped ReadOnlySpan<byte> value)
    {
        int required = 3 + key.Length + value.Length;

        if (byte.MaxValue < key.Length || byte.MaxValue < value.Length || _pending.Length - _pendingWritten < required)
        {
            ThrowOverflow();
        }

        Span<byte> entry = _pending[_pendingWritten..];
        entry[0] = kind;
        entry[1] = (byte)key.Length;
        entry[2] = (byte)value.Length;
        key.CopyTo(entry[3..]);
        value.CopyTo(entry[(3 + key.Length)..]);
        _pendingWritten += required;
    }

    private void WriteParam(ReadOnlySpan<byte> key, scoped ReadOnlySpan<byte> value, bool quoted)
    {
        AssertNameOrder(key);
        WriteJson(key, value, quoted);

        if (!_query.IsEmpty)
        {
            WriteQuery(key, value);
        }
    }

    private void WriteJson(scoped ReadOnlySpan<byte> key, scoped ReadOnlySpan<byte> value, bool quoted)
    {
        // Comma, two quotes and a colon around the key, two quotes around a text value.
        int required = key.Length + value.Length + 6;

        if (_json.Length - _jsonLength < required)
        {
            ThrowOverflow();
        }

        Span<byte> json = _json[_jsonLength..];
        int position = 0;

        if (_jsonLength != 0)
        {
            json[position++] = (byte)',';
        }

        json[position++] = (byte)'"';
        key.CopyTo(json[position..]);
        position += key.Length;
        json[position++] = (byte)'"';
        json[position++] = (byte)':';

        if (quoted)
        {
            json[position++] = (byte)'"';
        }

        value.CopyTo(json[position..]);
        position += value.Length;

        if (quoted)
        {
            json[position++] = (byte)'"';
        }

        _jsonLength += position;
    }

    private void WriteQuery(scoped ReadOnlySpan<byte> key, scoped ReadOnlySpan<byte> value)
    {
        int required = key.Length + value.Length + 2;

        if (_query.Length - _queryLength < required)
        {
            ThrowOverflow();
        }

        Span<byte> query = _query[_queryLength..];
        int position = 0;

        if (_queryLength != 0)
        {
            query[position++] = (byte)'&';
        }

        key.CopyTo(query[position..]);
        position += key.Length;
        query[position++] = (byte)'=';
        value.CopyTo(query[position..]);
        position += value.Length;

        _queryLength += position;
    }

    [Conditional("DEBUG")]
    private void AssertNameOrder(ReadOnlySpan<byte> key)
    {
        Debug.Assert(_lastKey.IsEmpty || _lastKey.SequenceCompareTo(key) < 0, "WebSocket API params must come in ordinal name order.");
        _lastKey = key;
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOverflow() => throw new InvalidOperationException("Request buffer is too small for this endpoint.");
}
