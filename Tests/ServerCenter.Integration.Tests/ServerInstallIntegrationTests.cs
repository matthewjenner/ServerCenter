using System.Diagnostics;
using AwesomeAssertions;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ServerCenter.Agent;
using ServerCenter.Agent.Jobs;
using ServerCenter.Contracts.V1;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Games;
using ServerCenter.Core.Jobs;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Integration.Tests;

// The game-server stand-up vertical over real gRPC (in-process): a stored descriptor + instance are
// resolved and dispatched as a server.install, which runs on the agent against a fake SteamCMD and
// persists Succeeded. Closes the Phase 5 DoD end to end (descriptor-driven anonymous install).
public sealed class ServerInstallIntegrationTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _dbPath = null!;

    public ValueTask InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sc-srv-{Guid.NewGuid():N}.db");
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
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Dispatched_server_install_runs_on_the_agent_and_persists_succeeded()
    {
        var ct = TestContext.Current.CancellationToken;

        var channel = GrpcChannel.ForAddress(
            _factory.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = _factory.Server.CreateHandler() });
        var call = new AgentLink.AgentLinkClient(channel).Connect(cancellationToken: ct);
        await using var transport = new GrpcAgentTransport(channel, call);

        var steam = new FakeSteamCmd();
        var handler = new JobExecutingCommandHandler(
            new IJobExecutor[] { new ServerInstallExecutor(steam) },
            new AgentJobStore(),
            NullLogger<JobExecutingCommandHandler>.Instance);

        var identity = new AgentIdentity("srv-agent", "0.1.0", "linux", "x64");
        (await AgentHandshake.PerformAsync(transport, identity, new EmptyAgentJobStateSource(), ct))
            .Established.Should().BeTrue();

        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pump = AgentSessionPump.RunAsync(
            transport, new BasicAgentStatusSource(), handler, TimeProvider.System, TimeSpan.FromSeconds(10), pumpCts.Token);

        // The agent registers its node on connect; seed the descriptor + instance once it is present.
        var connected = _factory.Services.GetRequiredService<ConnectedAgents>();
        await WaitUntilAsync(() => connected.TryGet("srv-agent", out _), ct);

        await _factory.Services.GetRequiredService<GameDescriptorRepository>().InsertAsync(
            new GameDescriptor { Id = "cs2-dedicated", Version = 3, SteamApp = new SteamAppSpec(730, "/opt/cs2") },
            1000, ct);
        await _factory.Services.GetRequiredService<ServerInstanceRepository>().InsertAsync(
            new ServerInstance
            {
                Id = "srv-1",
                NodeId = "srv-agent",
                DescriptorId = "cs2-dedicated",
                DescriptorVersion = 3,
                InstanceParamsJson = "{}",
                CreatedAtUnixMs = 1000
            }, ct);

        var result = await _factory.Services.GetRequiredService<ServerJobDispatcher>()
            .InstallAsync("srv-agent", "srv-1", ct);
        result.Outcome.Should().Be(ServerDispatchOutcome.Dispatched);

        var jobs = _factory.Services.GetRequiredService<JobRepository>();
        await WaitUntilAsync(
            () => jobs.GetAsync(result.JobId!, ct).GetAwaiter().GetResult() is { State: Core.Jobs.JobState.Succeeded },
            ct);

        steam.Requests.Should().ContainSingle().Which.AppId.Should().Be(730);

        await pumpCts.CancelAsync();
        try { await pump; } catch (OperationCanceledException) { }
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
