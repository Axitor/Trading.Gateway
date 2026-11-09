using System.Globalization;
using System.Text.Json;

namespace Axitor.Trading.Gateway.Tests.Fakes.Exchange;

/// <summary>A stream route of the fake exchange: answers SUBSCRIBE and UNSUBSCRIBE, keeps each connection's streams and pushes data to them.</summary>
internal sealed class FakeStreamRoute
{
    private const string SubscribeMethod = "SUBSCRIBE";

    private readonly Lock _sync = new();
    private readonly List<FakeConnection> _connections = [];
    private readonly Dictionary<FakeConnection, HashSet<string>> _subscriptions = new();
    private readonly Queue<ScriptedReply> _scripted = new();

    /// <summary>Every connection the route accepted, in order.</summary>
    public IReadOnlyList<FakeConnection> Connections
    {
        get
        {
            lock (_sync)
            {
                return [.. _connections];
            }
        }
    }

    /// <summary>The combined stream envelope the exchange wraps data in.</summary>
    public static string Envelope(string name, string data) => "{\"stream\":\"" + name + "\",\"data\":" + data + "}";

    /// <summary>The next control request is applied but never answered.</summary>
    public void IgnoreNext()
    {
        lock (_sync)
        {
            _scripted.Enqueue(new ScriptedReply(IsIgnored: true, 0, null));
        }
    }

    /// <summary>The next control request is refused with the code and message, and not applied.</summary>
    public void FailNext(int code, string message)
    {
        lock (_sync)
        {
            _scripted.Enqueue(new ScriptedReply(IsIgnored: false, code, message));
        }
    }

    /// <summary>Whether an open connection has the stream subscribed.</summary>
    public bool IsSubscribed(string name)
    {
        lock (_sync)
        {
            foreach ((FakeConnection connection, HashSet<string> names) in _subscriptions)
            {
                if (connection.IsOpen && names.Contains(name))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Sends stream data in the combined envelope to every open connection subscribed to it.</summary>
    public async Task PushAsync(string name, string data)
    {
        string message = Envelope(name, data);

        foreach (FakeConnection connection in Subscribers(name))
        {
            await connection.SendTextAsync(message);
        }
    }

    /// <summary>Sends a text message to every open connection of the route, subscribed or not.</summary>
    public async Task BroadcastAsync(string text)
    {
        foreach (FakeConnection connection in Connections)
        {
            if (connection.IsOpen)
            {
                await connection.SendTextAsync(text);
            }
        }
    }

    public void Attach(FakeConnection connection)
    {
        lock (_sync)
        {
            _connections.Add(connection);
            _subscriptions[connection] = new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>Applies a control request and answers it, unless a scripted reply says otherwise.</summary>
    public async Task HandleAsync(FakeConnection connection, string text)
    {
        (long id, bool isSubscribe, string[] names) = ReadControl(text);
        ScriptedReply reply;

        lock (_sync)
        {
            reply = _scripted.TryDequeue(out ScriptedReply scripted) ? scripted : default;

            if (reply.Code == 0)
            {
                HashSet<string> subscribed = _subscriptions[connection];

                foreach (string name in names)
                {
                    if (isSubscribe)
                    {
                        subscribed.Add(name);
                    }
                    else
                    {
                        subscribed.Remove(name);
                    }
                }
            }
        }

        if (reply.IsIgnored)
        {
            return;
        }

        string idText = id.ToString(CultureInfo.InvariantCulture);
        string answer = reply.Code == 0
            ? "{\"result\":null,\"id\":" + idText + "}"
            : "{\"code\":" + reply.Code.ToString(CultureInfo.InvariantCulture) + ",\"msg\":\"" + reply.Message + "\",\"id\":" + idText + "}";

        await connection.SendTextAsync(answer);
    }

    private List<FakeConnection> Subscribers(string name)
    {
        lock (_sync)
        {
            List<FakeConnection> found = [];

            foreach ((FakeConnection connection, HashSet<string> names) in _subscriptions)
            {
                if (connection.IsOpen && names.Contains(name))
                {
                    found.Add(connection);
                }
            }

            return found;
        }
    }

    private static (long Id, bool IsSubscribe, string[] Names) ReadControl(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;
        JsonElement parameters = root.GetProperty("params");
        string[] names = new string[parameters.GetArrayLength()];
        int index = 0;

        foreach (JsonElement name in parameters.EnumerateArray())
        {
            names[index++] = name.GetString()!;
        }

        return (root.GetProperty("id").GetInt64(), root.GetProperty("method").GetString() == SubscribeMethod, names);
    }

    /// <summary>How to answer one control request. Default: apply and confirm.</summary>
    private readonly record struct ScriptedReply(bool IsIgnored, int Code, string? Message);
}
