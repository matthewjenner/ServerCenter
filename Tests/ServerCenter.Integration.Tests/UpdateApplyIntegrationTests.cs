using System.Diagnostics;
using AwesomeAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ServerCenter.Agent;
using ServerCenter.Agent.Jobs;
using ServerCenter.Contracts.V1;
using ServerCenter.Controller;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Platform;
using ServerCenter.Core.Updates;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Integration.Tests;

// The update vertical over real gRPC (in-process): a stored UpdatePolicy is resolved and dispatched
// on the controller, pushed to a connected agent, executed there against a fake IUpdateProvider, and
// its result is persisted as a terminal Succeeded job. This closes the Phase 4 DoD end to end.
public sealed class UpdateApplyIntegrationTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _dbPath = null!;

    public ValueTask InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sc-upd-{Guid.NewGuid():N}.db");
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
    public async Task Dispatched_update_apply_runs_on_the_agent_and_persists_succeeded()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        GrpcChannel channel = GrpcChannel.ForAddress(
            _factory.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = _factory.Server.CreateHandler() });
        AsyncDuplexStreamingCall<AgentMessage, ControllerMessage> call = new AgentLink.AgentLinkClient(channel).Connect(cancellationToken: ct);
        await using GrpcAgentTransport transport = new GrpcAgentTransport(channel, call);

        FakeUpdateProvider provider = new FakeUpdateProvider("apt");
        JobExecutingCommandHandler handler = new JobExecutingCommandHandler(
            new IJobExecutor[]
            {
                new UpdateApplyExecutor([provider], [new NotifyPreflight()], new FakeServiceController())
            },
            new AgentJobStore(),
            NullLogger<JobExecutingCommandHandler>.Instance);

        AgentIdentity identity = new AgentIdentity("upd-agent", "0.1.0", "linux", "x64");
        (await AgentHandshake.PerformAsync(transport, identity, new EmptyAgentJobStateSource(), ct))
            .Established.Should().BeTrue();

        using CancellationTokenSource pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<AgentSessionOutcome> pump = AgentSessionPump.RunAsync(
            transport, new BasicAgentStatusSource(), handler, TimeProvider.System, TimeSpan.FromSeconds(10), pumpCts.Token);

        // Store a manual apt policy, wait for the agent to connect, then dispatch from the policy.
        UpdatePolicyRepository policies = _factory.Services.GetRequiredService<UpdatePolicyRepository>();
        await policies.InsertAsync(
            new UpdatePolicy
            {
                Id = "apt-manual",
                Version = 1,
                What = new UpdateWhat("apt"),
                When = UpdateSchedule.Manual,
                Preflight = [PreflightStep.Notify]
            },
            1000, ct);

        ConnectedAgents connected = _factory.Services.GetRequiredService<ConnectedAgents>();
        await WaitUntilAsync(() => connected.TryGet("upd-agent", out _), ct);

        UpdateJobDispatcher dispatcher = _factory.Services.GetRequiredService<UpdateJobDispatcher>();
        UpdateDispatchResult result = await dispatcher.DispatchAsync(
            "upd-agent", "apt-manual", null, UpdatePolicyResolver.Trigger.Manual, [], null, ct);
        result.Outcome.Should().Be(UpdateDispatchOutcome.Dispatched);

        JobRepository jobs = _factory.Services.GetRequiredService<JobRepository>();
        await WaitUntilAsync(
            () => jobs.GetAsync(result.JobId!, ct).GetAwaiter().GetResult() is { State: Core.Jobs.JobState.Succeeded },
            ct);

        provider.Applied.Should().BeTrue();

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
