using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace Axitor.Trading.Gateway.Tests.Fakes.Exchange;

/// <summary>One server-side connection of the fake exchange: reads client messages, sends text or binary, closes or cuts the link.</summary>
[SuppressMessage("Design", "CA1001", Justification = "The receive loop disposes the socket and the client when it ends; the semaphore holds no wait handle.")]
internal sealed class FakeConnection
{
    private const int ReceiveChunkBytes = 16 * 1024;

    private readonly TcpClient _client;
    private readonly WebSocket _socket;
    private readonly CancellationToken _stopToken;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Lock _sync = new();
    private readonly List<string> _received = [];
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeConnection(TcpClient client, WebSocket socket, string path, IReadOnlyDictionary<string, string> headers, CancellationToken stopToken)
    {
        _client = client;
        _socket = socket;
        _stopToken = stopToken;
        Path = path;
        Headers = headers;
    }

    /// <summary>Path of the upgrade request.</summary>
    public string Path { get; }

    /// <summary>Headers of the upgrade request; names match ignoring case.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Text messages from the client, in order.</summary>
    public IReadOnlyList<string> Received
    {
        get
        {
            lock (_sync)
            {
                return [.. _received];
            }
        }
    }

    /// <summary>False once either side has ended the connection.</summary>
    public bool IsOpen => !_closed.Task.IsCompleted;

    /// <summary>Completes when the connection has ended.</summary>
    public Task Closed => _closed.Task;

    public Task SendTextAsync(string text) => SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text);

    public Task SendBinaryAsync(byte[] frame) => SendAsync(frame, WebSocketMessageType.Binary);

    /// <summary>Sends a close frame: the client reads it as ClosedByServer.</summary>
    public async Task CloseAsync()
    {
        await _sendLock.WaitAsync(_stopToken);

        try
        {
            if (IsOpen)
            {
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, _stopToken);
            }
        }
        catch (Exception exception) when (IsLinkGone(exception))
        {
            // The client left first.
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Cuts the TCP link with a reset and no close frame, as a dropped network would.</summary>
    public void Abort()
    {
        try
        {
            Socket? socket = _client.Client;

            if (socket is not null)
            {
                socket.LingerState = new LingerOption(true, 0);
            }
        }
        catch (Exception exception) when (exception is ObjectDisposedException or SocketException)
        {
            // Already gone.
        }

        _client.Dispose();
    }

    /// <summary>Reads client messages until the connection ends, handing each text message to the route.</summary>
    public async Task RunAsync(Func<FakeConnection, string, Task> onText)
    {
        byte[] chunk = new byte[ReceiveChunkBytes];
        using MemoryStream message = new();

        try
        {
            while (true)
            {
                ValueWebSocketReceiveResult result = await _socket.ReceiveAsync(chunk.AsMemory(), _stopToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                message.Write(chunk, 0, result.Count);

                if (!result.EndOfMessage)
                {
                    continue;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);

                    lock (_sync)
                    {
                        _received.Add(text);
                    }

                    await onText(this, text);
                }

                message.SetLength(0);
            }
        }
        catch (Exception exception) when (IsLinkGone(exception))
        {
            // The client went away or the server is stopping.
        }
        finally
        {
            _closed.TrySetResult();
            _socket.Dispose();
            _client.Dispose();
        }
    }

    private async Task SendAsync(byte[] payload, WebSocketMessageType type)
    {
        await _sendLock.WaitAsync(_stopToken);

        try
        {
            if (IsOpen)
            {
                await _socket.SendAsync(payload.AsMemory(), type, endOfMessage: true, _stopToken);
            }
        }
        catch (Exception exception) when (IsLinkGone(exception))
        {
            // A message to a connection the client is closing is lost, as on a real network.
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static bool IsLinkGone(Exception exception) =>
        exception is WebSocketException or IOException or ObjectDisposedException or OperationCanceledException;
}
