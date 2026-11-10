using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Assistant.Core.Tests.Telemetry;

/// <summary>
/// A local OTLP/HTTP endpoint (what the Aspire dashboard is to the app). Keeps the raw protobuf bytes per signal;
/// strings are stored as plain UTF-8 inside them, so a canary search over the bytes sees everything exported.
/// </summary>
internal sealed class FakeOtlpReceiver : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakeOtlpReceiver(WebApplication app) => _app = app;

    public ConcurrentQueue<(string Signal, byte[] Body)> Payloads { get; } = new();

    public Uri Endpoint { get; private set; } = null!;

    public static async Task<FakeOtlpReceiver> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var receiver = new FakeOtlpReceiver(app);

        app.MapPost("/v1/{signal}", async (string signal, HttpRequest request) =>
        {
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer);
            receiver.Payloads.Enqueue((signal, buffer.ToArray()));
            return Results.Bytes([], "application/x-protobuf");
        });

        await app.StartAsync();
        string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        receiver.Endpoint = new Uri(address);
        return receiver;
    }

    public byte[] All(string signal) => [.. Payloads.Where(p => p.Signal == signal).SelectMany(p => p.Body)];

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
