using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Axitor.Trading.Gateway.Core.Models.Primitives;

/// <summary>
/// An exchange ticker of up to 16 ASCII characters, packed into a single 16-byte value.
/// Case is preserved but not validated; matching the target exchange's exact format is the caller's responsibility.
/// </summary>
/// <remarks>
/// DESIGN NOTE: ASCII-only by design. 
/// Enforce standard international Latin-based ticker conventions (e.g., BTCUSDT).
/// UTF-8 outliers (like Chinese meme-coins) are intentionally rejected to guarantee 
/// low latency and preserve a strict 16-byte struct memory footprint.
/// </remarks>
public readonly struct Symbol
    : IEquatable<Symbol>,
      IComparable<Symbol>,
      ISpanFormattable,
      IUtf8SpanFormattable
{
    private Symbol(UInt128 value) => _value = value;

    /// <summary>Allowed ticker characters.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAllowed(uint c) => (c | 0x20) - 'a' <= 'z' - 'a' || c - '0' <= '9' - '0' || c == '-' || c == '_' || c == '.' || c == '/';

    /// <summary>Maximum number of characters a symbol can hold.</summary>
    public const int MaxLength = 16;
    private readonly UInt128 _value;   // the field

    /// <summary>The empty symbol. Equivalent to the default value.</summary>
    public static Symbol Empty => default;

    /// <summary>True when this symbol holds no characters.</summary>
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value == UInt128.Zero;
    }

    /// <summary>Number of characters in the symbol.</summary>
    /// <remarks>
    /// Derived from the position of the highest set bit rather than by scanning
    /// for a terminator, so it costs one instruction regardless of length.
    /// Correct only because construction forbids interior zero bytes.
    /// </remarks>
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_value == UInt128.Zero)
            {
                return 0;
            }

            int significantBits = 128 - (int)UInt128.LeadingZeroCount(_value);
            return (significantBits + 7) / 8;
        }
    }

    /// <summary>
    /// Cold path with throw exception.
    /// Parses a symbol from characters. Throws when the input is too long or has a character outside the allowed set.
    /// </summary>
    public static Symbol Parse(ReadOnlySpan<char> source)
        => TryParse(source, out Symbol result)
            ? result
            : throw new ArgumentException(
                $"'{source}' is not a valid symbol: expected up to {MaxLength} characters from A-Z, a-z, 0-9, '-', '_', '.', '/'.",
                nameof(source));

    /// <summary>
    /// Cold path with throw exception.
    /// Parses a symbol from UTF-8 bytes. Throws when the input is too long or has a byte outside the allowed set.
    /// </summary>
    public static Symbol Parse(ReadOnlySpan<byte> source)
        => TryParse(source, out Symbol result)
            ? result
            : throw new ArgumentException(
                $"Input is not a valid symbol: expected up to {MaxLength} bytes from A-Z, a-z, 0-9, '-', '_', '.', '/'.",
                nameof(source));

    /// <summary>
    /// Cold path with throw exception.
    /// Parses a symbol from a string. Throws when the argument is null, too long, or has a character outside the allowed set.
    /// </summary>
    public static Symbol Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Parse(source.AsSpan());
    }

    /// <summary>
    /// Hot path without exception.
    /// Attempts to parse a symbol from characters.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> source, out Symbol result)
    {
        result = default;

        if (source.Length > MaxLength)
        {
            return false;
        }

        UInt128 value = UInt128.Zero;

        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];

            if (!IsAllowed(c))
            {
                return false;
            }

            value |= (UInt128)(byte)c << (i * 8);
        }

        result = new Symbol(value);
        return true;
    }

    /// <summary>
    /// Hot path without exception.
    /// Attempts to parse a symbol from UTF-8 bytes.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> source, out Symbol result)
    {
        result = default;

        if (source.Length > MaxLength)
        {
            return false;
        }

        UInt128 value = UInt128.Zero;

        for (int i = 0; i < source.Length; i++)
        {
            byte b = source[i];

            if (!IsAllowed(b))
            {
                return false;
            }

            value |= (UInt128)b << (i * 8);
        }

        result = new Symbol(value);
        return true;
    }

    /// <summary>
    /// Just in case to check a possibly-null string.
    /// Attempts to parse a symbol from a possibly-null string.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? source, out Symbol result)
    {
        if (source is null)
        {
            result = default;
            return false;
        }

        return TryParse(source.AsSpan(), out result);
    }

    /// <summary>
    /// Hot path.
    /// Writes the symbol as ASCII bytes for building a request URL or frame.
    /// All-or-nothing: on a short buffer nothing is written. Allocation-free.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWriteTo(Span<byte> destination, out int bytesWritten)
        => TryFormat(destination, out bytesWritten, default, null);

    /// <summary>
    /// Unsafe hot path.
    /// Writes all 16 bytes as a single unaligned store and returns the
    /// significant length. The caller MUST guarantee at least
    /// MaxLength bytes of headroom in destination;
    /// bytes past the symbol are written as zero and are expected to be
    /// overwritten by the next append. Advance the cursor by the returned count,
    /// never by 16. Little-endian only (every runtime target is).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int WriteUnchecked16(Span<byte> destination)
    {
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), _value);
        return Length;
    }

    /// <summary>
    /// Hot path.
    /// Writes the symbol as ASCII bytes.
    /// </summary>
    public bool TryFormat(
        Span<byte> utf8Destination,
        out int bytesWritten,
        ReadOnlySpan<char> format = default,
        IFormatProvider? provider = null)
    {
        int length = Length;
        bytesWritten = 0;

        if (utf8Destination.Length < length)
        {
            return false;
        }

        for (int i = 0; i < length; i++)
        {
            utf8Destination[i] = (byte)(_value >> (i * 8));
        }

        bytesWritten = length;
        return true;
    }

    /// <summary>
    /// Cold path.
    /// Writes the symbol as span chars (mainly for string reconstruct).
    /// </summary>
    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default,
        IFormatProvider? provider = null)
    {
        int length = Length;
        charsWritten = 0;

        if (destination.Length < length)
        {
            return false;
        }

        for (int i = 0; i < length; i++)
        {
            destination[i] = (char)(byte)(_value >> (i * 8));
        }

        charsWritten = length;
        return true;
    }

    /// <summary>
    /// Cold path.
    /// Reconstructs the symbol as a string. Allocates — intended for logs,
    /// exceptions and configuration, not for message handling.
    /// </summary>
    public override string ToString()
    {
        int length = Length;

        if (length == 0)
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[MaxLength];
        TryFormat(buffer, out _);

        return new string(buffer[..length]);
    }

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Symbol other) => _value == other._value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Symbol other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        ulong low = (ulong)_value;
        ulong high = (ulong)(_value >> 64);

        ulong mixed = low ^ (high * 0x9E3779B97F4A7C15UL);

        mixed ^= mixed >> 33;
        mixed *= 0xFF51AFD7ED558CCDUL;
        mixed ^= mixed >> 33;

        return (int)mixed ^ (int)(mixed >> 32);
    }

    /// <summary>Compares two symbols in ordinal order, matching an ordinal string compare.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareTo(Symbol other)
    {
        UInt128 left = BinaryPrimitives.ReverseEndianness(_value);
        UInt128 right = BinaryPrimitives.ReverseEndianness(other._value);

        return left.CompareTo(right);
    }

    /// <summary>Equality operator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Symbol left, Symbol right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Symbol left, Symbol right) => !left.Equals(right);

    /// <summary>Ordinal less-than.</summary>
    public static bool operator <(Symbol left, Symbol right) => left.CompareTo(right) < 0;

    /// <summary>Ordinal less-than-or-equal.</summary>
    public static bool operator <=(Symbol left, Symbol right) => left.CompareTo(right) <= 0;

    /// <summary>Ordinal greater-than.</summary>
    public static bool operator >(Symbol left, Symbol right) => left.CompareTo(right) > 0;

    /// <summary>Ordinal greater-than-or-equal.</summary>
    public static bool operator >=(Symbol left, Symbol right) => left.CompareTo(right) >= 0;

    /// <summary>Parses a symbol from a string. Throws on invalid input.</summary>
    public static explicit operator Symbol(string source) => Parse(source);
}
