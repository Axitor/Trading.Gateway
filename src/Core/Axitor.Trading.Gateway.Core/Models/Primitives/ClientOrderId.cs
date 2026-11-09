using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Axitor.Trading.Gateway.Core.Models.Primitives;

/// <summary>
/// A client order id of up to 36 ASCII characters, packed into a 36-byte value.
/// Allowed characters: A-Z, a-z, 0-9, '.', ':', '/', '_', '-'. Case is kept as given.
/// </summary>
/// <remarks>
/// DESIGN NOTE: A packed value instead of a string, so every order event carries the id with no allocation.
/// Bytes after the id are zero and the id itself has none, so its length ends at the last non-zero byte.
/// </remarks>
public readonly struct ClientOrderId
    : IEquatable<ClientOrderId>,
      ISpanFormattable,
      IUtf8SpanFormattable
{
    /// <summary>Maximum number of characters an id can hold.</summary>
    public const int MaxLength = 36;

    private static readonly SearchValues<byte> _allowedBytes =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.:/_-"u8);

    private static readonly SearchValues<char> _allowedChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.:/_-");

    private readonly Buffer _buffer;

    private ClientOrderId(in Buffer buffer) => _buffer = buffer;

    /// <summary>The empty id. Equivalent to the default value.</summary>
    public static ClientOrderId Empty => default;

    /// <summary>True when the id holds no characters.</summary>
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer[0] == 0;
    }

    /// <summary>Number of characters in the id.</summary>
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ReadOnlySpan<byte> bytes = _buffer;
            return bytes.LastIndexOfAnyExcept((byte)0) + 1;
        }
    }

    /// <summary>Cold path. Parses an id from characters. Throws when it is too long or has a character outside the allowed set.</summary>
    public static ClientOrderId Parse(ReadOnlySpan<char> source)
        => TryParse(source, out ClientOrderId result)
            ? result
            : throw new ArgumentException(
                $"'{source}' is not a valid client order id: expected up to {MaxLength} characters from A-Z, a-z, 0-9, '.', ':', '/', '_', '-'.",
                nameof(source));

    /// <summary>Cold path. Parses an id from UTF-8 bytes. Throws when it is too long or has a byte outside the allowed set.</summary>
    public static ClientOrderId Parse(ReadOnlySpan<byte> source)
        => TryParse(source, out ClientOrderId result)
            ? result
            : throw new ArgumentException(
                $"Input is not a valid client order id: expected up to {MaxLength} bytes from A-Z, a-z, 0-9, '.', ':', '/', '_', '-'.",
                nameof(source));

    /// <summary>Cold path. Parses an id from a string. Throws when the argument is null, too long, or has a character outside the allowed set.</summary>
    public static ClientOrderId Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Parse(source.AsSpan());
    }

    /// <summary>Attempts to parse an id from characters.</summary>
    public static bool TryParse(ReadOnlySpan<char> source, out ClientOrderId result)
    {
        if (MaxLength < source.Length || source.ContainsAnyExcept(_allowedChars))
        {
            result = default;
            return false;
        }

        Buffer buffer = default;
        Span<byte> bytes = buffer;

        for (int i = 0; i < source.Length; i++)
        {
            bytes[i] = (byte)source[i];
        }

        result = new ClientOrderId(in buffer);
        return true;
    }

    /// <summary>Attempts to parse an id from UTF-8 bytes.</summary>
    public static bool TryParse(ReadOnlySpan<byte> source, out ClientOrderId result)
    {
        if (MaxLength < source.Length || source.ContainsAnyExcept(_allowedBytes))
        {
            result = default;
            return false;
        }

        Buffer buffer = default;
        Span<byte> bytes = buffer;
        source.CopyTo(bytes);

        result = new ClientOrderId(in buffer);
        return true;
    }

    /// <summary>Attempts to parse an id from a possibly-null string.</summary>
    public static bool TryParse([NotNullWhen(true)] string? source, out ClientOrderId result)
    {
        if (source is null)
        {
            result = default;
            return false;
        }

        return TryParse(source.AsSpan(), out result);
    }

    /// <summary>Writes the id as ASCII bytes. On a short buffer nothing is written.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWriteTo(Span<byte> destination, out int bytesWritten)
        => TryFormat(destination, out bytesWritten, default, null);

    /// <summary>Writes the id as ASCII bytes. On a short buffer nothing is written.</summary>
    public bool TryFormat(
        Span<byte> utf8Destination,
        out int bytesWritten,
        ReadOnlySpan<char> format = default,
        IFormatProvider? provider = null)
    {
        ReadOnlySpan<byte> bytes = _buffer;
        int length = bytes.LastIndexOfAnyExcept((byte)0) + 1;

        if (utf8Destination.Length < length)
        {
            bytesWritten = 0;
            return false;
        }

        bytes[..length].CopyTo(utf8Destination);
        bytesWritten = length;
        return true;
    }

    /// <summary>Cold path. Writes the id as characters. On a short buffer nothing is written.</summary>
    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default,
        IFormatProvider? provider = null)
    {
        ReadOnlySpan<byte> bytes = _buffer;
        int length = bytes.LastIndexOfAnyExcept((byte)0) + 1;

        if (destination.Length < length)
        {
            charsWritten = 0;
            return false;
        }

        Ascii.ToUtf16(bytes[..length], destination, out charsWritten);
        return true;
    }

    /// <summary>Cold path. The id as a string. Allocates: for logs and exceptions, not for event handling.</summary>
    public override string ToString()
    {
        Span<char> chars = stackalloc char[MaxLength];
        TryFormat(chars, out int written);

        return written == 0 ? string.Empty : new string(chars[..written]);
    }

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ClientOrderId other) => AreEqual(in this, in other);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ClientOrderId other && AreEqual(in this, in other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        ReadOnlySpan<byte> bytes = _buffer;
        HashCode hash = default;
        hash.AddBytes(bytes);
        return hash.ToHashCode();
    }

    /// <summary>Equality operator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(in ClientOrderId left, in ClientOrderId right) => AreEqual(in left, in right);

    /// <summary>Inequality operator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(in ClientOrderId left, in ClientOrderId right) => !AreEqual(in left, in right);

    /// <summary>Parses an id from a string. Throws on invalid input.</summary>
    public static explicit operator ClientOrderId(string source) => Parse(source);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AreEqual(in ClientOrderId left, in ClientOrderId right)
    {
        ReadOnlySpan<byte> leftBytes = left._buffer;
        ReadOnlySpan<byte> rightBytes = right._buffer;
        return leftBytes.SequenceEqual(rightBytes);
    }

    [InlineArray(MaxLength)]
    private struct Buffer
    {
        private byte _element;
    }
}
