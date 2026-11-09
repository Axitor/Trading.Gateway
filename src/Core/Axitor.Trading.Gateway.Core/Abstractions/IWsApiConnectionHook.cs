using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

namespace Axitor.Trading.Gateway.Core.Abstractions;

/// <summary>Prepares every new WebSocket API connection before it serves, e.g. subscribes user data on it. Cold path: implement as a class.</summary>
internal interface IWsApiConnectionHook
{
    /// <summary>Runs once the connection is read, before it becomes current. False drops it. Any exception but cancellation or WsApiFatalException is fatal.</summary>
    ValueTask<bool> OnConnectedAsync(WsApiConnectionTicket ticket, CancellationToken cancellationToken);
}
