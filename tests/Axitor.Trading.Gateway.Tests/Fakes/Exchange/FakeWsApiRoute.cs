using System.Text.Json;
using Axitor.Trading.Gateway.Tests.Frames;

namespace Axitor.Trading.Gateway.Tests.Fakes.Exchange;

/// <summary>A WebSocket API route of the fake exchange: answers each request with the result set for its method, unless scripted otherwise.</summary>
internal sealed class FakeWsApiRoute
{
    // -1100 ILLEGAL_CHARS: Illegal characters found in a parameter. The fake's own answer to a method it has no result for.
    private const int NoAnswerCode = -1100;
    private const int NoAnswerStatus = 400;
    private const string NoAnswerMessage = "The fake exchange has no answer for this method.";

    private readonly Lock _sync = new();
    private readonly List<FakeConnection> _connections = [];
    private readonly List<FakeWsApiRequest> _requests = [];
    private readonly Dictionary<string, string> _results = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<ScriptedReply>> _scripted = new(StringComparer.Ordinal);
    private readonly List<HeldAnswer> _held = [];

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

    /// <summary>Requests of the method, in the order they came.</summary>
    public IReadOnlyList<FakeWsApiRequest> Of(string method)
    {
        lock (_sync)
        {
            List<FakeWsApiRequest> found = [];

            foreach (FakeWsApiRequest request in _requests)
            {
                if (request.Method == method)
                {
                    found.Add(request);
                }
            }

            return found;
        }
    }

    /// <summary>Result the method answers with from now on.</summary>
    public void Reply(string method, string result)
    {
        lock (_sync)
        {
            _results[method] = result;
        }
    }

    /// <summary>The next request of the method gets the answer of the moment it came, sent only on Release.</summary>
    public void HoldNext(string method) => Script(method, new ScriptedReply(ScriptedKind.Hold, 0, 0, null, 0));

    /// <summary>The next request of the method is never answered.</summary>
    public void IgnoreNext(string method) => Script(method, new ScriptedReply(ScriptedKind.Ignore, 0, 0, null, 0));

    /// <summary>The next request of the method is refused with the status, code and message. A ban length above zero adds the error data.</summary>
    public void FailNext(string method, int statusCode, int code, string message, long retryAfterMillis = 0) =>
        Script(method, new ScriptedReply(ScriptedKind.Fail, statusCode, code, message, retryAfterMillis));

    /// <summary>Sends every held answer, oldest first, on the connection its request came on.</summary>
    public async Task ReleaseAsync()
    {
        HeldAnswer[] held;

        lock (_sync)
        {
            held = [.. _held];
            _held.Clear();
        }

        foreach (HeldAnswer answer in held)
        {
            await answer.Connection.SendTextAsync(answer.Text);
        }
    }

    public void Attach(FakeConnection connection)
    {
        lock (_sync)
        {
            _connections.Add(connection);
        }
    }

    /// <summary>Records a request and answers it as set for its method.</summary>
    public async Task HandleAsync(FakeConnection connection, string text)
    {
        (long id, string method) = ReadRequest(text);
        string? answer = null;

        lock (_sync)
        {
            _requests.Add(new FakeWsApiRequest(connection, id, method));

            ScriptedReply reply = _scripted.TryGetValue(method, out Queue<ScriptedReply>? queue) && queue.TryDequeue(out ScriptedReply scripted) ? scripted : default;

            if (reply.Kind != ScriptedKind.Ignore)
            {
                string reaction = reply.Kind == ScriptedKind.Fail
                    ? WsApiFrames.Failure(id, reply.StatusCode, reply.Code, reply.Message!, reply.RetryAfterMillis)
                    : Answer(id, method);

                if (reply.Kind == ScriptedKind.Hold)
                {
                    _held.Add(new HeldAnswer(connection, reaction));
                }
                else
                {
                    answer = reaction;
                }
            }
        }

        if (answer is not null)
        {
            await connection.SendTextAsync(answer);
        }
    }

    private void Script(string method, ScriptedReply reply)
    {
        lock (_sync)
        {
            if (!_scripted.TryGetValue(method, out Queue<ScriptedReply>? queue))
            {
                queue = new Queue<ScriptedReply>();
                _scripted[method] = queue;
            }

            queue.Enqueue(reply);
        }
    }

    /// <summary>The success for the method, or the fake's own error when no result is set. Call under the lock.</summary>
    private string Answer(long id, string method) =>
        _results.TryGetValue(method, out string? result)
            ? WsApiFrames.Success(id, result)
            : WsApiFrames.Failure(id, NoAnswerStatus, NoAnswerCode, NoAnswerMessage, 0);

    private static (long Id, string Method) ReadRequest(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;

        // A request sent without an id is answered with a null one, as the exchange does.
        long id = root.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.Number ? idElement.GetInt64() : 0;

        return (id, root.GetProperty("method").GetString()!);
    }

    private enum ScriptedKind : byte
    {
        Answer,
        Hold,
        Ignore,
        Fail,
    }

    /// <summary>How to answer one request. Default: the result set for its method.</summary>
    private readonly record struct ScriptedReply(ScriptedKind Kind, int StatusCode, int Code, string? Message, long RetryAfterMillis);

    /// <summary>An answer waiting for Release.</summary>
    private readonly record struct HeldAnswer(FakeConnection Connection, string Text);
}

/// <summary>One request the route saw. Zero id when the request had none.</summary>
internal readonly record struct FakeWsApiRequest(FakeConnection Connection, long Id, string Method);
