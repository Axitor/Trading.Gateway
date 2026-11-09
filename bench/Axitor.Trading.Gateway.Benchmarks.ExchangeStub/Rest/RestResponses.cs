using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Axitor.Trading.Gateway.Benchmarks.ExchangeStub.Rest;

/// <summary>Recorded REST bodies held in memory and picked by path and query.</summary>
internal sealed class RestResponses
{
    private const int DefaultKlinesLimit = 500;
    private const string KlinesPrefix = "klines_";
    private const string DepthPrefix = "depth_1000_";
    private const string OrderPath = "/fapi/v1/order";

    private static readonly byte[] _notFound = Encoding.UTF8.GetBytes("{\"code\":-1121,\"msg\":\"The stub has no recording for this request.\"}");
    private static readonly byte[] _ping = Encoding.UTF8.GetBytes("{}");

    private readonly Dictionary<string, byte[]> _byPath;
    private readonly Dictionary<int, byte[]> _klinesByLimit;
    private readonly Dictionary<string, byte[]> _depthBySymbol;
    private readonly byte[]? _newOrder;
    private readonly byte[]? _cancelOrder;

    private RestResponses(
        Dictionary<string, byte[]> byPath,
        Dictionary<int, byte[]> klinesByLimit,
        Dictionary<string, byte[]> depthBySymbol,
        Dictionary<string, long> snapshotUpdateIds,
        byte[]? newOrder,
        byte[]? cancelOrder)
    {
        _byPath = byPath;
        _klinesByLimit = klinesByLimit;
        _depthBySymbol = depthBySymbol;
        SnapshotUpdateIds = snapshotUpdateIds;
        _newOrder = newOrder;
        _cancelOrder = cancelOrder;
    }

    private static ReadOnlySpan<byte> LastUpdateIdKey => "\"lastUpdateId\":"u8;

    /// <summary>Last update id of each recorded order book snapshot, by uppercase symbol.</summary>
    public IReadOnlyDictionary<string, long> SnapshotUpdateIds { get; }

    /// <summary>Reads the recorder output and the order samples. Missing files leave their path unanswered.</summary>
    public static RestResponses Load(string dataDir)
    {
        string restDir = Path.Combine(dataDir, "rest");
        string ordersDir = Path.Combine(dataDir, "orders");

        Dictionary<string, byte[]> byPath = [];
        AddIfExists(byPath, "/fapi/v1/exchangeInfo", Path.Combine(restDir, "exchange_info.json"));
        AddIfExists(byPath, "/fapi/v3/account", Path.Combine(restDir, "account_v3.json"));
        AddIfExists(byPath, "/fapi/v2/account", Path.Combine(restDir, "account_v2.json"));
        AddIfExists(byPath, "/fapi/v1/symbolConfig", Path.Combine(restDir, "symbol_config.json"));
        AddIfExists(byPath, "/fapi/v1/leverageBracket", Path.Combine(restDir, "leverage_bracket.json"));

        Dictionary<int, byte[]> klinesByLimit = [];

        foreach (string path in Directory.GetFiles(restDir, KlinesPrefix + "*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            klinesByLimit[int.Parse(name.AsSpan(KlinesPrefix.Length), CultureInfo.InvariantCulture)] = File.ReadAllBytes(path);
        }

        Dictionary<string, byte[]> depthBySymbol = [];
        Dictionary<string, long> snapshotUpdateIds = [];

        foreach (string path in Directory.GetFiles(restDir, DepthPrefix + "*.json"))
        {
            string symbol = Path.GetFileNameWithoutExtension(path)[DepthPrefix.Length..];
            byte[] body = File.ReadAllBytes(path);
            depthBySymbol[symbol] = body;

            if (TryReadLastUpdateId(body, out long lastUpdateId))
            {
                snapshotUpdateIds[symbol] = lastUpdateId;
            }
        }

        byte[]? newOrder = ReadIfExists(Path.Combine(ordersDir, "new_order.json"));
        byte[]? cancelOrder = ReadIfExists(Path.Combine(ordersDir, "cancel_order.json"));

        Console.WriteLine($"REST: {byPath.Count} bodies, {klinesByLimit.Count} kline sets, {depthBySymbol.Count} order book snapshots.");
        Console.WriteLine($"REST: new order sample {Describe(newOrder)}, cancel order sample {Describe(cancelOrder)}.");
        return new RestResponses(byPath, klinesByLimit, depthBySymbol, snapshotUpdateIds, newOrder, cancelOrder);
    }

    /// <summary>Writes the recorded body with the weight headers the exchange sends, or 404 when nothing is recorded.</summary>
    public async Task WriteAsync(HttpContext context)
    {
        HttpRequest request = context.Request;
        HttpResponse response = context.Response;
        string path = request.Path.Value ?? string.Empty;
        byte[]? body = Find(request, path);

        // Every client reads the weight headers, so every client gets them.
        response.Headers["X-MBX-USED-WEIGHT-1M"] = "1";

        if (path == OrderPath)
        {
            response.Headers["X-MBX-ORDER-COUNT-10S"] = "1";
            response.Headers["X-MBX-ORDER-COUNT-1M"] = "1";
        }

        response.StatusCode = body is null ? StatusCodes.Status404NotFound : StatusCodes.Status200OK;
        body ??= _notFound;

        response.ContentType = "application/json";
        response.ContentLength = body.Length;
        await response.Body.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
    }

    private byte[]? Find(HttpRequest request, string path) =>
        path switch
        {
            "/fapi/v1/time" => ServerTime(),
            "/fapi/v1/ping" => _ping,
            "/fapi/v1/klines" => _klinesByLimit.GetValueOrDefault(ReadLimit(request)),
            "/fapi/v1/depth" => _depthBySymbol.GetValueOrDefault(request.Query["symbol"].ToString()),
            OrderPath => HttpMethods.IsDelete(request.Method) ? _cancelOrder : _newOrder,
            _ => _byPath.GetValueOrDefault(path),
        };

    private static int ReadLimit(HttpRequest request) =>
        int.TryParse(request.Query["limit"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit) ? limit : DefaultKlinesLimit;

    private static byte[] ServerTime() =>
        Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{{\"serverTime\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}}"));

    private static bool TryReadLastUpdateId(ReadOnlySpan<byte> body, out long lastUpdateId)
    {
        int key = body.IndexOf(LastUpdateIdKey);

        if (key < 0)
        {
            lastUpdateId = 0;
            return false;
        }

        return Utf8Parser.TryParse(body[(key + LastUpdateIdKey.Length)..], out lastUpdateId, out _);
    }

    private static string Describe(byte[]? body) => body is null ? "missing" : "loaded";

    private static void AddIfExists(Dictionary<string, byte[]> byPath, string requestPath, string filePath)
    {
        byte[]? body = ReadIfExists(filePath);

        if (body is not null)
        {
            byPath[requestPath] = body;
        }
    }

    private static byte[]? ReadIfExists(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
}
