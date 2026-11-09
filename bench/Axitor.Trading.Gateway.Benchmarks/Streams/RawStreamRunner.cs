using System.Net.WebSockets;
using System.Text;
using Axitor.Trading.Gateway.Benchmarks.Clients;

namespace Axitor.Trading.Gateway.Benchmarks.Streams;

/// <summary>Bare ClientWebSocket that reads whole messages and drops them: the floor under any client on the same transport.</summary>
/// <remarks>Ours receives through ClientWebSocket as well, so the gap to this run is what parsing and dispatch cost.</remarks>
internal static class RawStreamRunner
{
    private const int ReceiveBufferBytes = 64 * 1024;

    /// <summary>Connects with every stream in the query and starts reading. Dispose the result to stop.</summary>
    public static async Task<IAsyncDisposable> StartAsync(StreamKind kind, LatencyCollector collector)
    {
        ClientWebSocket socket = new();

        try
        {
            await socket.ConnectAsync(BuildUri(kind), CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new Run(socket, collector);
    }

    /// <summary>Combined stream route with the streams in the query, e.g. /public/stream?streams=btcusdt@bookTicker/ethusdt@bookTicker.</summary>
    private static Uri BuildUri(StreamKind kind)
    {
        (string route, string suffix) = kind switch
        {
            StreamKind.BookTicker => ("/public/stream", "@bookTicker"),
            StreamKind.AggTrade => ("/market/stream", "@aggTrade"),
            StreamKind.Depth => ("/public/stream", "@depth@100ms"),
            StreamKind.Depth20 => ("/public/stream", "@depth20@100ms"),
            StreamKind.Kline => ("/market/stream", "@kline_1m"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The raw client has no order book."),
        };

        StringBuilder builder = new();
        builder.Append("ws://").Append(BenchmarkClients.StubHost).Append(route).Append("?streams=");

        string[] symbols = BenchmarkClients.StreamSymbolNames;

        for (int i = 0; i < symbols.Length; i++)
        {
            if (0 < i)
            {
                builder.Append('/');
            }

            builder.Append(symbols[i].ToLowerInvariant()).Append(suffix);
        }

        return new Uri(builder.ToString());
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket;
        private readonly LatencyCollector _collector;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _receive;

        public Run(ClientWebSocket socket, LatencyCollector collector)
        {
            _socket = socket;
            _collector = collector;
            _receive = ReceiveAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            _socket.Abort();
            await _receive.ConfigureAwait(false);
            _socket.Dispose();
            _stop.Dispose();
        }

        private async Task ReceiveAsync()
        {
            byte[] buffer = new byte[ReceiveBufferBytes];
            int length = 0;

            try
            {
                while (true)
                {
                    // Cold path: a message larger than the buffer.
                    if (length == buffer.Length)
                    {
                        Array.Resize(ref buffer, buffer.Length * 2);
                    }

                    ValueWebSocketReceiveResult result = await _socket.ReceiveAsync(buffer.AsMemory(length), _stop.Token).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    if (!result.EndOfMessage)
                    {
                        length += result.Count;
                        continue;
                    }

                    length = 0;
                    _collector.Count();
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped.
            }
            catch (WebSocketException) when (_stop.IsCancellationRequested)
            {
                // Aborted on stop.
            }
            catch (WebSocketException)
            {
                _collector.AddGap();
            }
        }
    }
}
