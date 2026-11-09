namespace Axitor.Trading.Gateway.Core.Configuration;

/// <summary>Buffer size configuration.</summary>
internal record BufferOptions
{

}

/// <summary>Buffer size configuration for REST API transport.</summary>
internal sealed record BufferRestApiOptions : BufferOptions
{
    /// <summary>Bytes reserved for building one request line.</summary>
    public int PayloadBufferBytes { get; init; }

    /// <summary>Size of the buffer an error body is read into. Bodies longer than this are truncated.</summary>
    public int ErrorBufferBytes { get; init; }
}

/// <summary>Buffer size configuration for WebSocket API command routing.</summary>
internal sealed record BufferWsApiOptions : BufferOptions
{
    /// <summary>The largest frame that can be sent.</summary>
    public int SendBufferBytes { get; init; }

    /// <summary>Initial size of the receive buffer.</summary>
    public int ReceiveBufferBytes { get; init; }

    /// <summary>Initial size of the buffer each in-flight request keeps its response in.</summary>
    public int PendingSlotBufferBytes { get; init; }
}

/// <summary>Buffer size configuration for unidirectional WebSocket data streams.</summary>
internal sealed record BufferWsStreamOptions : BufferOptions
{
    /// <summary>Initial size of the receive buffer.</summary>
    public int ReceiveBufferBytes { get; init; }

    /// <summary>The largest frame that can be sent.</summary>
    public int SendBufferBytes { get; init; }
}

