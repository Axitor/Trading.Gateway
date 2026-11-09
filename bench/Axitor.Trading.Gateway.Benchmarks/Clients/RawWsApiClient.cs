using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace Axitor.Trading.Gateway.Benchmarks.Clients;

/// <summary>Bare ClientWebSocket on the stub's WebSocket API: sends prepared bytes and reads the answer without parsing.</summary>
public sealed class RawWsApiClient : IDisposable
{
    // The placeholder key the clients send; the stub checks neither key nor signature.
    private const string ApiKey = "stub-key";
    private const string Timestamp = "1761638400000";

    // Enough of an answer to hold its status.
    private const int StatusPrefixBytes = 256;

    private static readonly string _signature = new('a', 64);

    private readonly ClientWebSocket _socket = new();
    private readonly byte[] _buffer = new byte[64 * 1024];

    public Task ConnectAsync() =>
        _socket.ConnectAsync(new Uri("ws://" + BenchmarkClients.StubHost + "/ws-fapi/v1"), CancellationToken.None);

    /// <summary>Request bytes of a public method. Parameters are JSON members without braces.</summary>
    public static byte[] CreatePublic(string method, string parameters) =>
        Encoding.UTF8.GetBytes("{\"id\":1,\"method\":\"" + method + "\",\"params\":{" + parameters + "}}");

    /// <summary>Request bytes of a signed method, with a key, timestamp and signature of real length.</summary>
    public static byte[] CreateSigned(string method, string parameters)
    {
        string separator = parameters.Length == 0 ? string.Empty : ",";

        return CreatePublic(
            method,
            parameters + separator + "\"apiKey\":\"" + ApiKey + "\",\"timestamp\":" + Timestamp + ",\"signature\":\"" + _signature + "\"");
    }

    /// <summary>Sends one request and reads its whole answer. Returns the answer length.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> RequestAsync(ReadOnlyMemory<byte> request)
    {
        await SendAsync(request).ConfigureAwait(false);
        return await ReceiveAsync().ConfigureAwait(false);
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> request) =>
        _socket.SendAsync(request, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

    /// <summary>Reads one whole answer into a reused buffer. Returns its length.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> ReceiveAsync()
    {
        int length = 0;

        while (true)
        {
            ValueWebSocketReceiveResult result = await _socket.ReceiveAsync(_buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false);
            ThrowIfClosed(result);
            length += result.Count;

            if (result.EndOfMessage)
            {
                return length;
            }
        }
    }

    /// <summary>Reads the given number of whole answers.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    public async ValueTask ReceiveManyAsync(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ReceiveAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Sends one request and throws unless the answer has status 200.</summary>
    public async Task CheckAsync(byte[] request, string name)
    {
        await SendAsync(request).ConfigureAwait(false);

        int length = 0;
        bool isEnd = false;

        while (!isEnd && length < StatusPrefixBytes)
        {
            ValueWebSocketReceiveResult result = await _socket.ReceiveAsync(_buffer.AsMemory(length), CancellationToken.None).ConfigureAwait(false);
            ThrowIfClosed(result);
            length += result.Count;
            isEnd = result.EndOfMessage;
        }

        string? failure = _buffer.AsSpan(0, length).IndexOf("\"status\":200"u8) < 0
            ? Encoding.UTF8.GetString(_buffer, 0, Math.Min(length, StatusPrefixBytes))
            : null;

        while (!isEnd)
        {
            ValueWebSocketReceiveResult result = await _socket.ReceiveAsync(_buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false);
            ThrowIfClosed(result);
            isEnd = result.EndOfMessage;
        }

        if (failure is not null)
        {
            throw new InvalidOperationException($"{name}: {failure}. Is the stub running on {BenchmarkClients.StubHost} with the recorded data?");
        }
    }

    public void Dispose() => _socket.Dispose();

    private static void ThrowIfClosed(ValueWebSocketReceiveResult result)
    {
        if (result.MessageType == WebSocketMessageType.Close)
        {
            throw new InvalidOperationException("The stub closed the WebSocket API connection.");
        }
    }
}
