using System.Diagnostics;
using AwesomeAssertions;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using ServerCenter.Agent;
using ServerCenter.Contracts.V1;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Connection;
using Xunit;

namespace ServerCenter.Integration.Tests;

// Drives the real gRPC path in-process: a GrpcChannel over the controller's in-memory
// TestServer, the real AgentLink service, real transport adapters, and now the SQLite-backed
// controller. Each run uses an isolated temp database.
public sealed class AgentLinkIntegrationTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _dbPath = null!;

    public ValueTask InitializeAsync()
    {
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sc-it-{Guid.NewGuid():N}.db");
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Database:Path", _dbPath));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    [Fact]
    public async Task Agent_connects_handshakes_and_its_heartbeat_reaches_the_controller()
    {
        var ct = TestContext.Current.CancellationToken;

        var channel = GrpcChannel.ForAddress(
            _factory.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = _factory.Server.CreateHandler() });

        var call = new AgentLink.AgentLinkClient(channel).Connect(cancellationToken: ct);
        await using var transport = new GrpcAgentTransport(channel, call);

        var identity = new AgentIdentity("it-agent", "0.1.0", "linux", "x64");
        var handshake = await AgentHandshake.PerformAsync(
            transport, identity, new EmptyAgentJobStateSource(), ct);

        handshake.Established.Should().BeTrue();
        handshake.SessionId.Should().NotBeEmpty();

        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pump = AgentSessionPump.RunAsync(
            transport, new BasicAgentStatusSource(), new NoopCommandHandler(),
            TimeProvider.System, TimeSpan.FromSeconds(10), pumpCts.Token);

        var presence = _factory.Services.GetRequiredService<AgentPresenceStore>();
        await WaitUntilAsync(
            () => presence.TryGet("it-agent", out var p) && p!.LastStatus is not null,
            ct);

        presence.TryGet("it-agent", out var recorded).Should().BeTrue();
        recorded!.LastStatus!.AgentHealth.Should().Be(ServiceHealth.Active);

        await pumpCts.CancelAsync();
        try
        {
            await pump;
        }
        catch (OperationCanceledException)
        {
            // expected on cancellation
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException("condition was not met in time");
            }

            await Task.Delay(25, ct);
        }
    }
}
