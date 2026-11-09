// Records USD-M futures mainnet data for the gateway benchmarks: REST bodies, WebSocket API responses and stream frames.
// Run from bench\tools: dotnet run recorder.cs -- [outputDir] [minutes] [SYMBOL,SYMBOL,...]; outputDir defaults to ..\Data.
// Signed REST and WebSocket API answers are recorded only when BINANCE_API_KEY and BINANCE_API_SECRET are set; a read-only key is enough.
//
// Frame file: "AXREC001", then records of [i64 microseconds since start][u8 1 = binary frame][i32 length][bytes], little-endian.

using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

string outputDir = args.Length > 0 ? args[0] : Path.Combine("..", "Data");
int minutes = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 10;
string[] symbols = args.Length > 2 ? args[2].Split(',', StringSplitOptions.RemoveEmptyEntries) : Recorder.DefaultSymbols;

using CancellationTokenSource stop = new(TimeSpan.FromMinutes(minutes));
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

await Recorder.RunAsync(outputDir, symbols, stop.Token);

internal static class Recorder
{
    public static readonly string[] DefaultSymbols =
    [
        "BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT", "DOGEUSDT", "BNBUSDT", "ADAUSDT", "LINKUSDT", "AVAXUSDT", "SUIUSDT",
        "LTCUSDT", "DOTUSDT", "BCHUSDT", "TRXUSDT", "NEARUSDT", "APTUSDT", "ARBUSDT", "OPUSDT", "FILUSDT", "ETCUSDT",
    ];

    private const string RestHost = "https://fapi.binance.com";
    private const string WsApiUri = "wss://ws-fapi.binance.com/ws-fapi/v1";
    private const string PublicStreamUri = "wss://fstream.binance.com/public/stream";
    private const string MarketStreamUri = "wss://fstream.binance.com/market/stream";

    // Diffs recorded before the snapshot let a local order book sync on replay as it does live.
    private static readonly TimeSpan SnapshotDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SnapshotSpacing = TimeSpan.FromMilliseconds(250);

    public static async Task RunAsync(string outputDir, string[] symbols, CancellationToken stopToken)
    {
        string restDir = Path.Combine(outputDir, "rest");
        string wsApiDir = Path.Combine(outputDir, "ws-api");
        string streamDir = Path.Combine(outputDir, "ws");

        Directory.CreateDirectory(restDir);
        Directory.CreateDirectory(wsApiDir);
        Directory.CreateDirectory(streamDir);

        string? apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
        string? apiSecret = Environment.GetEnvironmentVariable("BINANCE_API_SECRET");
        bool hasKeys = !string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret);

        using HttpClient http = new();

        // REST and WebSocket API answers are taken once, before the streams.
        await RestRecorder.RecordPublicAsync(http, RestHost, restDir, CancellationToken.None);
        await WsApiRecorder.RecordPublicAsync(new Uri(WsApiUri), wsApiDir, CancellationToken.None);

        if (hasKeys)
        {
            await RestRecorder.RecordSignedAsync(http, RestHost, restDir, apiKey!, apiSecret!, CancellationToken.None);
            await WsApiRecorder.RecordSignedAsync(new Uri(WsApiUri), wsApiDir, apiKey!, apiSecret!, CancellationToken.None);
        }
        else
        {
            Console.WriteLine("No API keys: signed answers skipped.");
        }

        string[] lower = symbols.Select(static symbol => symbol.ToLowerInvariant()).ToArray();

        Task<int>[] streams =
        [
            StreamRecorder.RecordAsync(new Uri(PublicStreamUri), lower.Select(static s => s + "@depth@100ms").ToArray(), Path.Combine(streamDir, "depth100.axrec"), stopToken),
            StreamRecorder.RecordAsync(new Uri(PublicStreamUri), lower.Select(static s => s + "@depth20@100ms").ToArray(), Path.Combine(streamDir, "depth20.axrec"), stopToken),
            StreamRecorder.RecordAsync(new Uri(PublicStreamUri), lower.Select(static s => s + "@bookTicker").ToArray(), Path.Combine(streamDir, "bookticker.axrec"), stopToken),
            StreamRecorder.RecordAsync(new Uri(MarketStreamUri), lower.Select(static s => s + "@aggTrade").ToArray(), Path.Combine(streamDir, "aggtrade.axrec"), stopToken),
            StreamRecorder.RecordAsync(new Uri(MarketStreamUri), lower.Select(static s => s + "@kline_1m").ToArray(), Path.Combine(streamDir, "kline1m.axrec"), stopToken),
        ];

        try
        {
            await Task.Delay(SnapshotDelay, stopToken);
            await RestRecorder.RecordSnapshotsAsync(http, RestHost, restDir, symbols, SnapshotSpacing, stopToken);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Stopped before the order book snapshots.");
        }

        int[] frames = await Task.WhenAll(streams);

        Console.WriteLine();
        Console.WriteLine($"depth100: {frames[0]}, depth20: {frames[1]}, bookTicker: {frames[2]}, aggTrade: {frames[3]}, kline: {frames[4]} frames.");
        Console.WriteLine($"Saved to {Path.GetFullPath(outputDir)}");
    }
}

internal static class RestRecorder
{
    public static async Task RecordPublicAsync(HttpClient http, string host, string dir, CancellationToken cancellationToken)
    {
        await SaveAsync(http, host + "/fapi/v1/klines?symbol=BTCUSDT&interval=1m&limit=1500", Path.Combine(dir, "klines_1500.json"), null, cancellationToken);
        await SaveAsync(http, host + "/fapi/v1/klines?symbol=BTCUSDT&interval=1m&limit=500", Path.Combine(dir, "klines_500.json"), null, cancellationToken);
        await SaveAsync(http, host + "/fapi/v1/exchangeInfo", Path.Combine(dir, "exchange_info.json"), null, cancellationToken);
    }

    public static async Task RecordSignedAsync(HttpClient http, string host, string dir, string apiKey, string apiSecret, CancellationToken cancellationToken)
    {
        await SaveAsync(http, host + "/fapi/v3/account?" + Sign(string.Empty, apiSecret), Path.Combine(dir, "account_v3.json"), apiKey, cancellationToken);
        await SaveAsync(http, host + "/fapi/v2/account?" + Sign(string.Empty, apiSecret), Path.Combine(dir, "account_v2.json"), apiKey, cancellationToken);
        await SaveAsync(http, host + "/fapi/v1/symbolConfig?" + Sign(string.Empty, apiSecret), Path.Combine(dir, "symbol_config.json"), apiKey, cancellationToken);
        await SaveAsync(http, host + "/fapi/v1/leverageBracket?" + Sign(string.Empty, apiSecret), Path.Combine(dir, "leverage_bracket.json"), apiKey, cancellationToken);
    }

    public static async Task RecordSnapshotsAsync(HttpClient http, string host, string dir, string[] symbols, TimeSpan spacing, CancellationToken cancellationToken)
    {
        foreach (string symbol in symbols)
        {
            await SaveAsync(http, $"{host}/fapi/v1/depth?symbol={symbol}&limit=1000", Path.Combine(dir, $"depth_1000_{symbol}.json"), null, cancellationToken);
            await Task.Delay(spacing, cancellationToken);
        }
    }

    /// <summary>Query with timestamp and HMAC-SHA256 signature appended.</summary>
    private static string Sign(string query, string apiSecret)
    {
        string timestamp = "timestamp=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        string signed = query.Length == 0 ? timestamp : query + "&" + timestamp;
        return signed + "&signature=" + Hmac.Hex(apiSecret, signed);
    }

    private static async Task SaveAsync(HttpClient http, string url, string path, string? apiKey, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);

        if (apiKey is not null)
        {
            request.Headers.Add("X-MBX-APIKEY", apiKey);
        }

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"{(int)response.StatusCode} {Path.GetFileName(path)}: {Encoding.UTF8.GetString(body)}");
            return;
        }

        await File.WriteAllBytesAsync(path, body, cancellationToken);
        Console.WriteLine($"{Path.GetFileName(path)}: {body.Length} bytes");
    }
}

internal static class WsApiRecorder
{
    public static async Task RecordPublicAsync(Uri uri, string dir, CancellationToken cancellationToken)
    {
        using ClientWebSocket socket = new();
        await socket.ConnectAsync(uri, cancellationToken);

        await SaveAsync(socket, "{\"id\":1,\"method\":\"ticker.price\",\"params\":{\"symbol\":\"BTCUSDT\"}}", Path.Combine(dir, "ticker_price.json"), cancellationToken);
    }

    public static async Task RecordSignedAsync(Uri uri, string dir, string apiKey, string apiSecret, CancellationToken cancellationToken)
    {
        using ClientWebSocket socket = new();
        await socket.ConnectAsync(uri, cancellationToken);

        await SaveAsync(socket, SignedRequest(2, "account.status", apiKey, apiSecret), Path.Combine(dir, "account_status.json"), cancellationToken);
        await SaveAsync(socket, SignedRequest(3, "v2/account.balance", apiKey, apiSecret), Path.Combine(dir, "account_balance.json"), cancellationToken);
    }

    /// <summary>A request whose only params are apiKey and timestamp, signed in name order.</summary>
    private static string SignedRequest(int id, string method, string apiKey, string apiSecret)
    {
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        string signature = Hmac.Hex(apiSecret, $"apiKey={apiKey}&timestamp={timestamp}");

        return $"{{\"id\":{id},\"method\":\"{method}\",\"params\":{{\"apiKey\":\"{apiKey}\",\"timestamp\":{timestamp},\"signature\":\"{signature}\"}}}}";
    }

    private static async Task SaveAsync(ClientWebSocket socket, string request, string path, CancellationToken cancellationToken)
    {
        await socket.SendAsync(Encoding.UTF8.GetBytes(request), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

        byte[] buffer = new byte[1 << 16];
        int length = 0;

        while (true)
        {
            if (length == buffer.Length)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
            }

            ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(length), cancellationToken);
            length += result.Count;

            if (result.EndOfMessage)
            {
                break;
            }
        }

        await File.WriteAllBytesAsync(path, buffer.AsMemory(0, length).ToArray(), cancellationToken);
        Console.WriteLine($"{Path.GetFileName(path)}: {length} bytes");
    }
}

internal static class StreamRecorder
{
    private static ReadOnlySpan<byte> Magic => "AXREC001"u8;
    private static ReadOnlySpan<byte> StreamPrefix => "{\"stream\":"u8;

    private const int RecordHeaderBytes = sizeof(long) + sizeof(byte) + sizeof(int);

    /// <summary>Subscribes the streams on one connection and writes every data frame until stopped. Returns the frame count.</summary>
    public static async Task<int> RecordAsync(Uri uri, string[] streams, string path, CancellationToken stopToken)
    {
        using ClientWebSocket socket = new();
        await socket.ConnectAsync(uri, CancellationToken.None);

        string subscribe = "{\"method\":\"SUBSCRIBE\",\"params\":[" + string.Join(',', streams.Select(static s => "\"" + s + "\"")) + "],\"id\":1}";
        await socket.SendAsync(Encoding.UTF8.GetBytes(subscribe), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

        await using FileStream file = new(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
        file.Write(Magic);

        byte[] buffer = new byte[1 << 20];
        long start = Stopwatch.GetTimestamp();
        int frames = 0;

        try
        {
            while (true)
            {
                int length = 0;
                bool isBinary = false;

                while (true)
                {
                    if (length == buffer.Length)
                    {
                        Array.Resize(ref buffer, buffer.Length * 2);
                    }

                    ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(length), stopToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine($"{Path.GetFileName(path)}: closed by the server after {frames} frames.");
                        return frames;
                    }

                    isBinary = result.MessageType == WebSocketMessageType.Binary;
                    length += result.Count;

                    if (result.EndOfMessage)
                    {
                        break;
                    }
                }

                long micros = Stopwatch.GetElapsedTime(start).Ticks / 10;

                // Only stream data is replayed; replies and anything else are shown.
                if (!WriteFrame(file, buffer, length, micros, isBinary))
                {
                    Console.WriteLine($"{Path.GetFileName(path)}: {Encoding.UTF8.GetString(buffer, 0, Math.Min(length, 200))}");
                    continue;
                }

                frames++;
            }
        }
        catch (OperationCanceledException)
        {
            // Recording time is over.
        }
        catch (WebSocketException exception)
        {
            Console.WriteLine($"{Path.GetFileName(path)}: connection lost after {frames} frames: {exception.Message}");
        }

        return frames;
    }

    private static bool WriteFrame(FileStream file, byte[] buffer, int length, long micros, bool isBinary)
    {
        ReadOnlySpan<byte> message = buffer.AsSpan(0, length);

        if (!isBinary && !message.StartsWith(StreamPrefix))
        {
            return false;
        }

        Span<byte> header = stackalloc byte[RecordHeaderBytes];
        BinaryPrimitives.WriteInt64LittleEndian(header, micros);
        header[sizeof(long)] = isBinary ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(header[(sizeof(long) + sizeof(byte))..], length);

        file.Write(header);
        file.Write(message);
        return true;
    }
}

internal static class Hmac
{
    public static string Hex(string secret, string data) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data)));
}