using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Core.Transport.Common.Payload;

/// <summary>Stack-only builder for a UTF-8 network payload. Writes into a caller-owned span and never allocates.</summary>
internal ref struct PayloadBuilder
{
    private readonly Span<byte> _buffer;
    private int _position;
    private int _queryStart;

    /// <summary>Bytes written so far.</summary>
    public readonly int Position => _position;

    /// <summary>Creates a builder over a buffer.</summary>
    public PayloadBuilder(Span<byte> buffer)
    {
        _buffer = buffer;
        _queryStart = -1;
    }

    /// <summary>Appends a raw span with no separator: a host, a path, a literal.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(scoped ReadOnlySpan<byte> value)
    {
        EnsureCapacity(value.Length);
        value.CopyTo(_buffer[_position..]);
        _position += value.Length;
    }

    /// <summary>Appends a prefix and a query parameter. An ampersand is inserted when one was already written.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(scoped ReadOnlySpan<byte> prefix, scoped ReadOnlySpan<byte> value)
    {
        WriteOpenQueryOrSeparator();
        EnsureCapacity(prefix.Length + value.Length);

        prefix.CopyTo(_buffer[_position..]);
        _position += prefix.Length;

        value.CopyTo(_buffer[_position..]);
        _position += value.Length;
    }

    /// <summary>Appends a prefix and a ticker, written to the buffer as one store.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(scoped ReadOnlySpan<byte> prefix, Symbol symbol)
    {
        WriteOpenQueryOrSeparator();
        EnsureCapacity(prefix.Length + Symbol.MaxLength);

        prefix.CopyTo(_buffer[_position..]);
        _position += prefix.Length;

        _position += symbol.WriteUnchecked16(_buffer[_position..]);
    }

    /// <summary>Appends a prefix and a formatted numeric parameter, e.g. an int, long or decimal. Not for double: it may come out in exponent form, use AppendDecimal.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append<TValue>(scoped ReadOnlySpan<byte> prefix, TValue value, ReadOnlySpan<char> format = default) where TValue : IUtf8SpanFormattable
    {
        WriteOpenQueryOrSeparator();
        EnsureCapacity(prefix.Length);

        prefix.CopyTo(_buffer[_position..]);
        _position += prefix.Length;

        if (!value.TryFormat(_buffer[_position..], out int written, format, provider: null))
        {
            ThrowOverflow();
        }

        _position += written;
    }

    /// <summary>Appends a prefix and a boolean as "true" or "false".</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(scoped ReadOnlySpan<byte> prefix, bool value)
    {
        Append(prefix, value ? "true"u8 : "false"u8);
    }

    /// <summary>Appends a prefix and a price or quantity as plain decimal text, rounded to 15 significant digits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendDecimal(scoped ReadOnlySpan<byte> prefix, double value)
    {
        WriteOpenQueryOrSeparator();
        EnsureCapacity(prefix.Length);

        prefix.CopyTo(_buffer[_position..]);
        _position += prefix.Length;

        if (!PlainDecimalFormatter.TryFormat(value, _buffer[_position..], out int written))
        {
            ThrowOverflow();
        }

        _position += written;
    }

    /// <summary>Appends an optional numeric parameter, or writes nothing when null.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendOptional<TValue>(scoped ReadOnlySpan<byte> prefix, TValue? value, ReadOnlySpan<char> format = default) where TValue : struct, IUtf8SpanFormattable
    {
        if (value.HasValue)
        {
            Append(prefix, value.Value, format);
        }
    }

    /// <summary>Appends an optional boolean parameter, or writes nothing when null.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendOptional(scoped ReadOnlySpan<byte> prefix, bool? value)
    {
        if (value.HasValue)
        {
            Append(prefix, value.Value ? "true"u8 : "false"u8);
        }
    }

    /// <summary>Appends an optional price or quantity, or writes nothing when null.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendOptionalDecimal(scoped ReadOnlySpan<byte> prefix, double? value)
    {
        if (value.HasValue)
        {
            AppendDecimal(prefix, value.Value);
        }
    }

    /// <summary>Writes the query mark before the first parameter and an ampersand before the rest.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteOpenQueryOrSeparator()
    {
        if (_queryStart < 0)
        {
            Append("?"u8);
            _queryStart = _position;
        }
        else
        {
            Append("&"u8);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly void EnsureCapacity(int required)
    {
        if (_buffer.Length - _position < required)
        {
            ThrowOverflow();
        }
    }

    /// <summary>Free space after what is written. Commit bytes written into it with Advance.</summary>
    public readonly Span<byte> FreeSpan() => _buffer[_position..];

    /// <summary>Commits bytes written into FreeSpan.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count) => _position += count;

    /// <summary>The payload written so far.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<byte> AsSpan() => _buffer[.._position];

    /// <summary>The query portion: everything after the '?'. Empty when no query was opened.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<byte> QuerySpan() => _queryStart < 0 ? default : _buffer[_queryStart.._position];

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOverflow() => throw new InvalidOperationException("Payload buffer is too small for this endpoint.");
}
