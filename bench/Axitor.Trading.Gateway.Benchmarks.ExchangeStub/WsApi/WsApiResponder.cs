using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Axitor.Trading.Gateway.Benchmarks.ExchangeStub.WsApi;

/// <summary>Answers WebSocket API requests with recorded responses carrying the request id.</summary>
internal sealed class WsApiResponder
{
    private static ReadOnlySpan<byte> IdKey => "\"id\":"u8;
    private static ReadOnlySpan<byte> NullId => "null"u8;

    private readonly Dictionary<string, Template> _templates;
    private readonly Template _unknown;

    private WsApiResponder(Dictionary<string, Template> templates, Template unknown)
    {
        _templates = templates;
        _unknown = unknown;
    }

    /// <summary>Reads the recorded responses and wraps the order samples into the WebSocket API envelope.</summary>
    public static WsApiResponder Load(string dataDir)
    {
        string wsApiDir = Path.Combine(dataDir, "ws-api");
        string ordersDir = Path.Combine(dataDir, "orders");

        Dictionary<string, Template> templates = [];
        AddRecorded(templates, "ticker.price", Path.Combine(wsApiDir, "ticker_price.json"));
        AddRecorded(templates, "account.status", Path.Combine(wsApiDir, "account_status.json"));
        AddRecorded(templates, "v2/account.balance", Path.Combine(wsApiDir, "account_balance.json"));
        AddOrder(templates, "order.place", Path.Combine(ordersDir, "new_order.json"));
        AddOrder(templates, "order.cancel", Path.Combine(ordersDir, "cancel_order.json"));

        Template unknown = CreateTemplate(Encoding.UTF8.GetBytes("{\"id\":0,\"status\":400,\"error\":{\"code\":-1100,\"msg\":\"The stub has no answer for this method.\"}}"));

        Console.WriteLine($"WS API: {templates.Count} methods.");
        return new WsApiResponder(templates, unknown);
    }

    /// <summary>Answers requests in order until the client closes.</summary>
    public async Task RunAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64 * 1024];

        try
        {
            while (true)
            {
                int length = 0;

                while (true)
                {
                    if (length == buffer.Length)
                    {
                        Array.Resize(ref buffer, buffer.Length * 2);
                    }

                    ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    length += result.Count;

                    if (result.EndOfMessage)
                    {
                        break;
                    }
                }

                byte[] answer = Answer(buffer.AsSpan(0, length));
                await socket.SendAsync(answer, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (WebSocketException)
        {
            // The client went away.
        }
        catch (OperationCanceledException)
        {
            // The stub is stopping.
        }
        catch (JsonException)
        {
            // A request that is not JSON ends the connection.
        }
    }

    private byte[] Answer(ReadOnlySpan<byte> request)
    {
        Utf8JsonReader reader = new(request);
        int idStart = 0;
        int idLength = 0;
        string? method = null;

        if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    idStart = (int)reader.TokenStartIndex;

                    // A text id keeps its quotes.
                    idLength = reader.TokenType == JsonTokenType.String ? reader.ValueSpan.Length + 2 : reader.ValueSpan.Length;
                }
                else if (reader.ValueTextEquals("method"u8))
                {
                    reader.Read();
                    method = reader.GetString();
                }
                else
                {
                    reader.Read();
                }

                reader.Skip();
            }
        }

        Template template = method is not null && _templates.TryGetValue(method, out Template found) ? found : _unknown;
        ReadOnlySpan<byte> id = idLength == 0 ? NullId : request.Slice(idStart, idLength);

        return Render(in template, id);
    }

    private static void AddRecorded(Dictionary<string, Template> templates, string method, string path)
    {
        if (File.Exists(path))
        {
            templates[method] = CreateTemplate(File.ReadAllBytes(path));
        }
    }

    private static void AddOrder(Dictionary<string, Template> templates, string method, string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        byte[] sample = File.ReadAllBytes(path);
        byte[] envelope = [.. "{\"id\":0,\"status\":200,\"result\":"u8, .. sample, .. "}"u8];
        templates[method] = CreateTemplate(envelope);
    }

    /// <summary>Splits a response around the value of its id.</summary>
    private static Template CreateTemplate(byte[] response)
    {
        int key = response.AsSpan().IndexOf(IdKey);

        if (key < 0)
        {
            throw new InvalidDataException("A WebSocket API response has no id.");
        }

        int valueStart = key + IdKey.Length;
        int valueEnd = valueStart + response.AsSpan(valueStart).IndexOfAny((byte)',', (byte)'}');

        return new Template() { Prefix = response[..valueStart], Suffix = response[valueEnd..] };
    }

    private static byte[] Render(in Template template, ReadOnlySpan<byte> id) =>
        [.. template.Prefix, .. id, .. template.Suffix];

    private struct Template
    {
        public byte[] Prefix;
        public byte[] Suffix;
    }
}
