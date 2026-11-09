using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using Axitor.Trading.Gateway.Benchmarks.ExchangeStub.Rest;
using Axitor.Trading.Gateway.Benchmarks.ExchangeStub.Streams;
using Axitor.Trading.Gateway.Benchmarks.ExchangeStub.WsApi;
using Microsoft.AspNetCore.Http.Features;

// Local stand-in for the Binance USD-M endpoints: serves recorded mainnet data to every benchmarked client.
// Run: dotnet run -c Release -- [dataDir] [port] [pace]
// pace: 1 replays streams at the recorded speed, 4 four times faster, max as fast as the client reads.

string dataDir = args.Length > 0 ? args[0] : "Data";
int port = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 5100;
string paceText = args.Length > 2 ? args[2] : "1";
double pace = paceText == "max" ? 0 : double.Parse(paceText, CultureInfo.InvariantCulture);

RestResponses rest = RestResponses.Load(dataDir);
WsApiResponder wsApi = WsApiResponder.Load(dataDir);
StreamReplayer replayer = StreamReplayer.Load(Path.Combine(dataDir, "ws"), pace, rest.SnapshotUpdateIds);

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));

WebApplication app = builder.Build();

// Before UseWebSockets: it takes the upgrade feature as a request comes in.
app.Use((context, next) =>
{
    IHttpUpgradeFeature? upgrade = context.Features.Get<IHttpUpgradeFeature>();

    if (upgrade is { IsUpgradableRequest: true })
    {
        context.Features.Set<IHttpUpgradeFeature>(new CoalescingUpgradeFeature(upgrade));
    }

    return next(context);
});

app.UseWebSockets();
app.Run(context => HandleAsync(context, rest, wsApi, replayer));

Console.WriteLine($"Stub on http://127.0.0.1:{port}, stream pace {paceText}.");
await app.RunAsync().ConfigureAwait(false);

static async Task HandleAsync(HttpContext context, RestResponses rest, WsApiResponder wsApi, StreamReplayer replayer)
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        await rest.WriteAsync(context).ConfigureAwait(false);
        return;
    }

    string path = context.Request.Path.Value ?? string.Empty;
    string streams = context.Request.Query["streams"].ToString();

    using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);

    if (path.StartsWith("/ws-fapi", StringComparison.Ordinal))
    {
        await wsApi.RunAsync(socket, context.RequestAborted).ConfigureAwait(false);
        return;
    }

    CoalescingStream? transport = (context.Features.Get<IHttpUpgradeFeature>() as CoalescingUpgradeFeature)?.Transport;
    await replayer.RunAsync(socket, transport, path, streams, context.RequestAborted).ConfigureAwait(false);
}
