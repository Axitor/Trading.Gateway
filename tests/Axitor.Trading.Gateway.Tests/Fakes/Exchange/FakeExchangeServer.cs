using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace Axitor.Trading.Gateway.Tests.Fakes.Exchange;

/// <summary>A local exchange for WebSocket tests: accepts upgrades on a loopback port and hands each connection to the route of its path.</summary>
internal sealed class FakeExchangeServer : IAsyncDisposable
{
    public const string FuturesPublicPath = "/public/stream";
    public const string FuturesMarketPath = "/market/stream";
    public const string FuturesPrivatePath = "/private/stream";
    public const string FuturesWsApiPath = "/ws-fapi/v1";
    public const string CoinMStreamPath = "/coinm/stream";
    public const string CoinMWsApiPath = "/ws-dapi/v1";
    public const string SpotStreamPath = "/spot/stream";
    public const string SpotWsApiPath = "/ws-api/v3";
    public const string SpotSbePath = "/sbe/stream";

    /// <summary>The stream the client watchdog listens to by default.</summary>
    public const string WatchdogStream = "btcusdt@bookTicker";

    // RFC 6455: the server proves the upgrade by hashing the client key with this GUID.
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private const string KeyHeader = "Sec-WebSocket-Key";
    private const int NotFoundStatus = 404;
    private const int MaxRequestBytes = 8192;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, FakeStreamRoute> _streamRoutes;
    private readonly Dictionary<string, FakeWsApiRoute> _wsApiRoutes;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, Queue<Refusal>> _refusals = new(StringComparer.Ordinal);
    private readonly List<FakeConnection> _connections = [];
    private readonly Task _acceptLoop;

    private Task _feedLoop = Task.CompletedTask;
    private int _feedPaused;
    private long _watchdogFrames;

    public FakeExchangeServer()
    {
        _streamRoutes = new(StringComparer.Ordinal)
        {
            [FuturesPublicPath] = new FakeStreamRoute(),
            [FuturesMarketPath] = new FakeStreamRoute(),
            [FuturesPrivatePath] = new FakeStreamRoute(),
            [CoinMStreamPath] = new FakeStreamRoute(),
            [SpotStreamPath] = new FakeStreamRoute(),
            [SpotSbePath] = new FakeStreamRoute(),
        };

        _wsApiRoutes = new(StringComparer.Ordinal)
        {
            [FuturesWsApiPath] = new FakeWsApiRoute(),
            [CoinMWsApiPath] = new FakeWsApiRoute(),
            [SpotWsApiPath] = new FakeWsApiRoute(),
        };

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync(_stop.Token);
    }

    public int Port { get; }

    /// <summary>Frames the feed sent while the watchdog stream was subscribed.</summary>
    public long WatchdogFrames => Interlocked.Read(ref _watchdogFrames);

    /// <summary>WebSocket address of a path on this server.</summary>
    public Uri UriOf(string path) => new(string.Create(CultureInfo.InvariantCulture, $"ws://127.0.0.1:{Port}{path}"));

    /// <summary>The stream route of a path.</summary>
    public FakeStreamRoute Stream(string path) => _streamRoutes[path];

    /// <summary>The WebSocket API route of a path.</summary>
    public FakeWsApiRoute WsApi(string path) => _wsApiRoutes[path];

    /// <summary>Refuses the next handshakes on the path with the status; Retry-After is sent when above zero.</summary>
    public void Refuse(string path, int statusCode, int retryAfterSeconds = 0, int count = 1)
    {
        lock (_sync)
        {
            if (!_refusals.TryGetValue(path, out Queue<Refusal>? queue))
            {
                queue = new Queue<Refusal>();
                _refusals[path] = queue;
            }

            for (int i = 0; i < count; i++)
            {
                queue.Enqueue(new Refusal(statusCode, retryAfterSeconds));
            }
        }
    }

    /// <summary>Sends the watchdog stream every interval, as the exchange does. Without it the watchdog never arms. Call once.</summary>
    public void StartWatchdogFeed(TimeSpan interval) => _feedLoop = FeedWatchdogAsync(interval, _stop.Token);

    /// <summary>The watchdog stream goes quiet while every connection stays open.</summary>
    public void PauseWatchdogFeed() => Volatile.Write(ref _feedPaused, 1);

    public void ResumeWatchdogFeed() => Volatile.Write(ref _feedPaused, 0);

    /// <summary>Stops accepting and cuts every connection still open.</summary>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Dispose();

        FakeConnection[] connections;

        lock (_sync)
        {
            connections = [.. _connections];
        }

        foreach (FakeConnection connection in connections)
        {
            connection.Abort();
        }

        await _acceptLoop;
        await _feedLoop;
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = ServeAsync(client, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException or InvalidOperationException)
        {
            // The server is stopping. A client accepted just before the stop makes the next accept throw InvalidOperationException: the listener is no longer listening.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A broken test connection must not take the server down; the test sees the outcome on the client side.")]
    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            NetworkStream stream = client.GetStream();
            (string Path, Dictionary<string, string> Headers)? request = await ReadRequestAsync(stream, cancellationToken);

            if (request is null || !request.Value.Headers.TryGetValue(KeyHeader, out string? key))
            {
                client.Dispose();
                return;
            }

            string path = request.Value.Path;
            Refusal refusal = TakeRefusal(path);
            bool isStream = _streamRoutes.TryGetValue(path, out FakeStreamRoute? streamRoute);
            bool isWsApi = _wsApiRoutes.TryGetValue(path, out FakeWsApiRoute? wsApiRoute);

            if (refusal.StatusCode == 0 && !isStream && !isWsApi)
            {
                refusal = new Refusal(NotFoundStatus, 0);
            }

            if (refusal.StatusCode != 0)
            {
                await WriteAsync(stream, RefusalResponse(refusal), cancellationToken);
                client.Dispose();
                return;
            }

            await WriteAsync(stream, AcceptResponse(key), cancellationToken);

            // No server pings: the client keeps the link alive, and the server answers its pings while it reads.
            WebSocket socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions() { IsServer = true, KeepAliveInterval = TimeSpan.Zero });
            FakeConnection connection = new(client, socket, path, request.Value.Headers, cancellationToken);

            lock (_sync)
            {
                _connections.Add(connection);
            }

            Func<FakeConnection, string, Task> onText;

            if (streamRoute is not null)
            {
                streamRoute.Attach(connection);
                onText = streamRoute.HandleAsync;
            }
            else
            {
                wsApiRoute!.Attach(connection);
                onText = wsApiRoute.HandleAsync;
            }

            await connection.RunAsync(onText);
        }
        catch (Exception)
        {
            client.Dispose();
        }
    }

    private async Task FeedWatchdogAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        FakeStreamRoute route = _streamRoutes[FuturesPublicPath];
        long updateId = 0;

        try
        {
            while (true)
            {
                await Task.Delay(interval, cancellationToken);

                if (Volatile.Read(ref _feedPaused) == 1 || !route.IsSubscribed(WatchdogStream))
                {
                    continue;
                }

                await route.PushAsync(WatchdogStream, WatchdogTicker(++updateId));
                Interlocked.Increment(ref _watchdogFrames);
            }
        }
        catch (OperationCanceledException)
        {
            // The server is stopping.
        }
    }

    private Refusal TakeRefusal(string path)
    {
        lock (_sync)
        {
            return _refusals.TryGetValue(path, out Queue<Refusal>? queue) && queue.TryDequeue(out Refusal refusal) ? refusal : default;
        }
    }

    /// <summary>Reads the upgrade request up to the blank line. Null when the client sent no complete request.</summary>
    private static async Task<(string Path, Dictionary<string, string> Headers)?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[MaxRequestBytes];
        int length = 0;
        int end;

        while ((end = buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8)) < 0)
        {
            if (length == buffer.Length)
            {
                return null;
            }

            int read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);

            if (read == 0)
            {
                return null;
            }

            length += read;
        }

        string[] lines = Encoding.ASCII.GetString(buffer, 0, end).Split("\r\n");
        string[] requestLine = lines[0].Split(' ');

        if (requestLine.Length < 2)
        {
            return null;
        }

        string target = requestLine[1];
        int queryAt = target.IndexOf('?', StringComparison.Ordinal);
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':', StringComparison.Ordinal);

            if (0 < colon)
            {
                headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
            }
        }

        return (queryAt < 0 ? target : target[..queryAt], headers);
    }

    private static async Task WriteAsync(NetworkStream stream, string text, CancellationToken cancellationToken) =>
        await stream.WriteAsync(Encoding.ASCII.GetBytes(text), cancellationToken);

    private static string RefusalResponse(Refusal refusal)
    {
        string statusLine = string.Create(CultureInfo.InvariantCulture, $"HTTP/1.1 {refusal.StatusCode} Refused\r\n");
        string retryAfter = 0 < refusal.RetryAfterSeconds
            ? string.Create(CultureInfo.InvariantCulture, $"Retry-After: {refusal.RetryAfterSeconds}\r\n")
            : string.Empty;

        return statusLine + retryAfter + "Content-Length: 0\r\nConnection: close\r\n\r\n";
    }

    [SuppressMessage("Security", "CA5350", Justification = "RFC 6455 fixes SHA-1 for the handshake accept key.")]
    private static string AcceptResponse(string key)
    {
        string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
        return "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n";
    }

    /// <summary>USD-M book ticker of the watchdog stream in the documented key order; only the update id changes.</summary>
    private static string WatchdogTicker(long updateId) =>
        string.Create(CultureInfo.InvariantCulture,
            $$"""{"e":"bookTicker","u":{{updateId}},"s":"BTCUSDT","ps":"BTCUSDT","b":"25.35190000","B":"31.21000000","a":"25.36520000","A":"40.66000000","T":1568014460891,"E":1568014460893}""");

    /// <summary>A scripted handshake refusal. Zero status means none.</summary>
    private readonly record struct Refusal(int StatusCode, int RetryAfterSeconds);
}
