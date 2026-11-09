using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Time;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Connection;

/// <summary>One open WebSocket: whole-message receive, sends under a lock within the message budget, close and abort.</summary>
internal sealed class WsConnection : IDisposable
{
    private readonly ClientWebSocket _socket;
    private readonly WsReceiveBuffer _receiveBuffer;
    private readonly WsMessageBudget _messageBudget;
    private readonly byte[] _sendBuffer;

    // One sender at a time, as the socket requires. Never disposed: no wait handle is ever taken from it.
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private int _state;
    private int _reason;

    /// <summary>Sequence number of the connection within its transport.</summary>
    public int Epoch { get; }

    /// <summary>Wraps a socket that has just completed its handshake.</summary>
    public WsConnection(ClientWebSocket socket, int epoch, int sendBufferBytes, int receiveBufferBytes, int maxMessageBytes, int messagesPerSecond, TimeSpan keepAliveInterval)
    {
        Epoch = epoch;
        _socket = socket;
        _receiveBuffer = new WsReceiveBuffer(receiveBufferBytes, maxMessageBytes);
        _messageBudget = new WsMessageBudget(messagesPerSecond, keepAliveInterval);

        // Not pinned: the socket copies an outgoing frame to mask it.
        _sendBuffer = GC.AllocateUninitializedArray<byte>(sendBufferBytes);
        OpenedTicks = SysTime.GetTimestamp();
    }

    /// <summary>When the connection opened, in monotonic ticks.</summary>
    public long OpenedTicks { get; }

    /// <summary>WebSocketError behind a network failure, if any.</summary>
    public WebSocketError WebSocketError { get; private set; }

    /// <summary>SocketError behind a network failure, if any.</summary>
    public SocketError SocketError { get; private set; }

    /// <summary>Current lifecycle state.</summary>
    public WsConnectionState State
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (WsConnectionState)Volatile.Read(ref _state);
    }

    /// <summary>Why the connection ended. None while it is open or draining.</summary>
    public WsDisconnectReason DisconnectReason => (WsDisconnectReason)Volatile.Read(ref _reason);

    /// <summary>The last received message. Valid until the next receive.</summary>
    public ReadOnlySpan<byte> Message
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _receiveBuffer.Written;
    }

    /// <summary>Whether the last received message came in a binary frame.</summary>
    public bool IsBinary { get; private set; }

    /// <summary>Reads one whole message into Message. False when the connection has ended; DisconnectReason says why.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<bool> ReceiveAsync(CancellationToken cancellationToken)
    {
        _receiveBuffer.Reset();

        try
        {
            while (true)
            {
                if (!_receiveBuffer.TryGetFreeMemory(out Memory<byte> free))
                {
                    Abort(WsDisconnectReason.MessageTooBig);
                    return false;
                }

                ValueWebSocketReceiveResult result = await _socket.ReceiveAsync(free, cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Abort(WsDisconnectReason.ClosedByServer);
                    return false;
                }

                // Every fragment of a message has the type of its first frame.
                IsBinary = result.MessageType == WebSocketMessageType.Binary;
                _receiveBuffer.Advance(result.Count);

                if (result.EndOfMessage)
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Abort(WsDisconnectReason.AppShutdown);
            return false;
        }
        catch (OperationCanceledException)
        {
            // The socket aborts a pending receive from inside when a pong misses the keep-alive timeout.
            Abort(WsDisconnectReason.KeepAliveTimeout);
            return false;
        }
        catch (WebSocketException exception)
        {
            AbortOnFailure(exception);
            return false;
        }
    }

    /// <summary>Writes and sends one text message within the message budget. NotSent is guaranteed to have never reached the wire.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<WsSendStatus> SendAsync<TPayload>(TPayload payload, CancellationToken cancellationToken)
        where TPayload : struct, IPayloadWriter
    {
        if (Volatile.Read(ref _state) != (int)WsConnectionState.Open)
        {
            return WsSendStatus.NotSent;
        }

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            while (!_messageBudget.CanSend(SysTime.GetTimestamp(), out long waitMillis))
            {
                await Task.Delay((int)waitMillis, cancellationToken).ConfigureAwait(false);
            }

            // A close or abort may have landed while waiting.
            if (Volatile.Read(ref _state) != (int)WsConnectionState.Open)
            {
                return WsSendStatus.NotSent;
            }

            // Written after the budget wait, so a signed request carries a fresh timestamp.
            int length = WriteFrame(in payload);

            try
            {
                await _socket.SendAsync(_sendBuffer.AsMemory(0, length), WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
                return WsSendStatus.Sent;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Abort(WsDisconnectReason.KeepAliveTimeout);
                return WsSendStatus.Unknown;
            }
            catch (WebSocketException exception)
            {
                AbortOnFailure(exception);
                return WsSendStatus.Unknown;
            }
            finally
            {
                // Counted even on failure: part of the frame may have gone out.
                _messageBudget.Record(SysTime.GetTimestamp());
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Builds the frame in the send buffer and returns its length. Kept out of the async method: the builder cannot live across an await.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int WriteFrame<TPayload>(in TPayload payload) where TPayload : struct, IPayloadWriter
    {
        PayloadBuilder builder = new(_sendBuffer);
        payload.Write(ref builder);
        return builder.Position;
    }

    /// <summary>Stops new sends while receiving continues. False when the connection is no longer open.</summary>
    public bool BeginDrain() =>
        Interlocked.CompareExchange(ref _state, (int)WsConnectionState.Draining, (int)WsConnectionState.Open) == (int)WsConnectionState.Open;

    /// <summary>Cold path. Sends a close frame within the timeout, then aborts. The peer's close reply is not awaited.</summary>
    public async ValueTask CloseAsync(WsDisconnectReason reason, TimeSpan timeout)
    {
        TrySetReason(reason);
        Volatile.Write(ref _state, (int)WsConnectionState.Closed);

        try
        {
            if (await _sendLock.WaitAsync(timeout).ConfigureAwait(false))
            {
                try
                {
                    using CancellationTokenSource timeoutSource = new(timeout);
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeoutSource.Token).ConfigureAwait(false);
                }
                finally
                {
                    _sendLock.Release();
                }
            }
        }
        // The connection is going away either way.
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            _socket.Abort();
        }
    }

    /// <summary>Tears the connection down at once. The first recorded reason is kept.</summary>
    public void Abort(WsDisconnectReason reason)
    {
        TrySetReason(reason);
        Volatile.Write(ref _state, (int)WsConnectionState.Closed);
        _socket.Abort();
    }

    /// <summary>Cold path. Records a network failure with its diagnostics, then tears the connection down.</summary>
    private void AbortOnFailure(WebSocketException exception)
    {
        WsDisconnectReason reason = WsNetworkExceptions.Classify(exception, out _, out WebSocketError webSocketError, out SocketError socketError);

        // Only the winner of the reason writes diagnostics, before the abort wakes the receive loop.
        if (TrySetReason(reason))
        {
            WebSocketError = webSocketError;
            SocketError = socketError;
        }

        Volatile.Write(ref _state, (int)WsConnectionState.Closed);
        _socket.Abort();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TrySetReason(WsDisconnectReason reason) =>
        Interlocked.CompareExchange(ref _reason, (int)reason, (int)WsDisconnectReason.None) == (int)WsDisconnectReason.None;

    /// <summary>Releases the socket. Call only after the receive loop has ended and no send is in progress.</summary>
    public void Dispose() => _socket.Dispose();

}
