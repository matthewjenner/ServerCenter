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
using ServerCenter.Core.Platform;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Integration.Tests;

// The whole job vertical over real gRPC (in-process): a job dispatched on the controller is
// pushed to a connected agent, executed there against a fake IServiceController, and its result
// is persisted back as a terminal job state. This is Phase 3's spine end to end.
public sealed class JobDispatchIntegrationTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _dbPath = null!;

    public ValueTask InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sc-jobs-{Guid.NewGuid():N}.db");
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
    public async Task Dispatched_service_restart_runs_on_the_agent_and_persists_succeeded()
    {
        var ct = TestContext.Current.CancellationToken;

        var channel = GrpcChannel.ForAddress(
            _factory.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = _factory.Server.CreateHandler() });
        var call = new AgentLink.AgentLinkClient(channel).Connect(cancellationToken: ct);
        await using var transport = new GrpcAgentTransport(channel, call);

        var services = new FakeServiceController();
        services.Seed("web.service", ServiceState.Inactive);
        var handler = new JobExecutingCommandHandler(
            new ServerCenter.Core.Jobs.IJobExecutor[] { new ServiceRestartExecutor(services) },
            new AgentJobStore(),
            NullLogger<JobExecutingCommandHandler>.Instance);

        var identity = new AgentIdentity("job-agent", "0.1.0", "linux", "x64");
        (await AgentHandshake.PerformAsync(transport, identity, new EmptyAgentJobStateSource(), ct))
            .Established.Should().BeTrue();

        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pump = AgentSessionPump.RunAsync(
            transport, new BasicAgentStatusSource(), handler, TimeProvider.System, TimeSpan.FromSeconds(10), pumpCts.Token);

        // Wait until the controller sees the agent connected, then dispatch a job to it.
        var connected = _factory.Services.GetRequiredService<ConnectedAgents>();
        await WaitUntilAsync(() => connected.TryGet("job-agent", out _), ct);

        var dispatcher = _factory.Services.GetRequiredService<JobDispatcher>();
        var jobId = await dispatcher.DispatchAsync(
            "job-agent", "service.restart", "{\"unit\":\"web.service\"}", false, false, ct);

        // The agent executes it; the controller persists the terminal result.
        var jobs = _factory.Services.GetRequiredService<JobRepository>();
        await WaitUntilAsync(
            () => jobs.GetAsync(jobId, ct).GetAwaiter().GetResult() is { State: ServerCenter.Core.Jobs.JobState.Succeeded },
            ct);

        (await services.GetStateAsync("web.service", ct)).Should().Be(ServiceState.Active);

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
