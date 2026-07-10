using System.Diagnostics;
using AwesomeAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using ServerCenter.Agent;
using ServerCenter.Contracts.V1;
using ServerCenter.Controller;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Connection;
using Xunit;

namespace ServerCenter.Integration.Tests;

// The provisioning -> managed handoff over real gRPC: a node pre-recorded 'provisioning' (with its
// libvirt domain) flips to 'managed' when its agent first checks in, and keeps its domain (so VM
// truth + lifecycle jobs work). Closes the Phase 7 handoff DoD (the VM boot itself is Tier-3).
public sealed class ProvisioningHandoffIntegrationTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _dbPath = null!;

    public ValueTask InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sc-prov-{Guid.NewGuid():N}.db");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:Path", _dbPath);
            builder.UseSetting("Security:RequireClientCertificate", "false");
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (string? file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task A_provisioning_node_flips_to_managed_when_its_agent_checks_in()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        // Pre-record the node as provisioning (as if its VM was just defined + cloud-init'd).
        AgentNodeRepository nodes = _factory.Services.GetRequiredService<AgentNodeRepository>();
        await nodes.ProvisionNodeAsync("prov-agent", "guest", "cs2-ffa", "linux", 1000, ct);
        (await nodes.GetNodeAsync("prov-agent", ct))!.Lifecycle.Should().Be("provisioning");

        GrpcChannel channel = GrpcChannel.ForAddress(
            _factory.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = _factory.Server.CreateHandler() });
        AsyncDuplexStreamingCall<AgentMessage, ControllerMessage> call = new AgentLink.AgentLinkClient(channel).Connect(cancellationToken: ct);
        await using GrpcAgentTransport transport = new GrpcAgentTransport(channel, call);

        AgentIdentity identity = new AgentIdentity("prov-agent", "0.1.0", "linux", "x64");
        (await AgentHandshake.PerformAsync(transport, identity, new EmptyAgentJobStateSource(), ct))
            .Established.Should().BeTrue();

        using CancellationTokenSource pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<AgentSessionOutcome> pump = AgentSessionPump.RunAsync(
            transport, new BasicAgentStatusSource(), new NoopCommandHandler(),
            TimeProvider.System, TimeSpan.FromSeconds(10), pumpCts.Token);

        // The handoff runs during connect; wait until the node has flipped to managed.
        await WaitUntilAsync(
            () => nodes.GetNodeAsync("prov-agent", ct).GetAwaiter().GetResult() is { Lifecycle: "managed" }, ct);

        NodeRow? node = await nodes.GetNodeAsync("prov-agent", ct);
        node!.AgentId.Should().Be("prov-agent");   // adopted its agent
        node.LibvirtDomain.Should().Be("cs2-ffa"); // domain preserved -> VM truth + lifecycle work

        await pumpCts.CancelAsync();
        try { await pump; } catch (OperationCanceledException) { }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct, int timeoutMs = 5000)
    {
        Stopwatch sw = Stopwatch.StartNew();
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
