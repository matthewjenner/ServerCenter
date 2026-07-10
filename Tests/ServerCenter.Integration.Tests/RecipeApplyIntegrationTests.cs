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
using ServerCenter.Agent.Linux;
using ServerCenter.Contracts.V1;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Games;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Recipes;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Integration.Tests;

// The provisioning vertical over real gRPC (in-process): a stored recipe + instance are resolved and
// dispatched as recipe.apply, which runs on the agent (fake package/steam/service, real ScriptRunner
// over a fake process) and persists Succeeded. Closes the Phase 7 recipe-engine DoD end to end.
public sealed class RecipeApplyIntegrationTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _dbPath = null!;

    public ValueTask InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sc-recipe-{Guid.NewGuid():N}.db");
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
    public async Task Dispatched_recipe_apply_runs_on_the_agent_and_persists_succeeded()
    {
        var ct = TestContext.Current.CancellationToken;

        var channel = GrpcChannel.ForAddress(
            _factory.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = _factory.Server.CreateHandler() });
        var call = new AgentLink.AgentLinkClient(channel).Connect(cancellationToken: ct);
        await using var transport = new GrpcAgentTransport(channel, call);

        var steam = new FakeSteamCmd();
        var services = new FakeServiceController();
        var executor = new RecipeApplyExecutor(
            new FakePackageInstaller(), steam, new RecordingConfigWriter(),
            new ScriptRunner(new AlwaysOkProcessRunner()), services);
        var handler = new JobExecutingCommandHandler(
            new IJobExecutor[] { executor }, new AgentJobStore(), NullLogger<JobExecutingCommandHandler>.Instance);

        var identity = new AgentIdentity("recipe-agent", "0.1.0", "linux", "x64");
        (await AgentHandshake.PerformAsync(transport, identity, new EmptyAgentJobStateSource(), ct))
            .Established.Should().BeTrue();

        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pump = AgentSessionPump.RunAsync(
            transport, new BasicAgentStatusSource(), handler, TimeProvider.System, TimeSpan.FromSeconds(10), pumpCts.Token);

        var connected = _factory.Services.GetRequiredService<ConnectedAgents>();
        await WaitUntilAsync(() => connected.TryGet("recipe-agent", out _), ct);

        await _factory.Services.GetRequiredService<BuildRecipeRepository>().InsertAsync(new BuildRecipe
        {
            Id = "cs2-server",
            Version = 5,
            SteamApp = new SteamAppSpec(730, "/opt/cs2"),
            ServiceDefinition = new ServiceDefinition("cs2.service", "/opt/cs2/start.sh")
        }, 1000, ct);
        await _factory.Services.GetRequiredService<ServerInstanceRepository>().InsertAsync(new ServerInstance
        {
            Id = "srv-1",
            NodeId = "recipe-agent",
            RecipeId = "cs2-server",
            RecipeVersion = 5,
            InstanceParamsJson = "{}",
            CreatedAtUnixMs = 1000
        }, ct);

        var result = await _factory.Services.GetRequiredService<ServerJobDispatcher>()
            .ApplyRecipeAsync("recipe-agent", "srv-1", ct);
        result.Outcome.Should().Be(ServerDispatchOutcome.Dispatched);

        var jobs = _factory.Services.GetRequiredService<JobRepository>();
        await WaitUntilAsync(
            () => jobs.GetAsync(result.JobId!, ct).GetAwaiter().GetResult() is { State: Core.Jobs.JobState.Succeeded },
            ct);

        steam.Requests.Should().ContainSingle().Which.AppId.Should().Be(730);
        services.Calls.Should().Contain(("start", "cs2.service")); // service brought up

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

    // The recipe has no scripts, but ScriptRunner still needs a process runner; this one always
    // succeeds (unused here since Scripts is empty, but keeps the executor construction honest).
    private sealed class AlwaysOkProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));

        public Task<ProcessResult> RunAsync(
            string fileName, IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string> environment, CancellationToken ct) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
    }
}
