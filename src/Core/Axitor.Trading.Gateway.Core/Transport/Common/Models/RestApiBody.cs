namespace Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

/// <summary>A response body read into a buffer rented from the shared byte pool. Return the buffer once parsed.</summary>
internal struct RestApiBody
{
    /// <summary>Rented buffer. The body is its first Length bytes.</summary>
    public byte[] Buffer;

    /// <summary>Body length in bytes.</summary>
    public int Length;
}
