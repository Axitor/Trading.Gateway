using System.Buffers;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Core.Transport.RestApi.Helpers;

/// <summary>Reads a whole response body into a pooled buffer, for parsers that need it as one span.</summary>
internal static class RestApiBodyReader
{
    // Order and account responses fit; a larger body doubles the buffer.
    private const int InitialBytes = 16 * 1024;

    /// <summary>
    /// Reads the body to its end. The caller returns the buffer to the shared pool once parsed.
    /// A known likely size saves regrowing the buffer; it never starts below 16 KB.
    /// </summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<RestApiBody> ReadAsync(Stream body, CancellationToken cancellationToken, int expectedBytes = 0)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(expectedBytes, InitialBytes));
        int length = 0;

        try
        {
            while (true)
            {
                if (length == buffer.Length)
                {
                    byte[] larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, length).CopyTo(larger);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = larger;
                }

                int read = await body.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                length += read;
            }
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }

        return new RestApiBody() { Buffer = buffer, Length = length };
    }
}
