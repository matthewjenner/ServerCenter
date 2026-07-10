using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Jobs;
using Xunit;

namespace ServerCenter.Controller.Tests;

public sealed class SqliteControllerJobViewTests : IAsyncLifetime
{
    private TempDatabase _db = null!;
    private JobRepository _jobs = null!;
    private SqliteControllerJobView _view = null!;
    private readonly FakeTimeProvider _clock = new();

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        _db = await TempDatabase.CreateAsync(ct);
        _jobs = new JobRepository(_db.Database);
        _view = new SqliteControllerJobView(_jobs, _clock);

        var agents = new AgentNodeRepository(_db.Database);
        await agents.EnsureAgentAsync("agent-1", "agent-1", "fpr", 1, ct);
        await agents.EnsureNodeAsync("node-1", "agent-1", "guest", "managed", 1, ct);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task GetOpenJobs_maps_open_jobs_for_the_agent()
    {
        var ct = TestContext.Current.CancellationToken;
        await _jobs.InsertAsync(Job("j1", requeueable: true), ct);

        var open = await _view.GetOpenJobsAsync("agent-1", ct);

        open.Should().ContainSingle()
            .Which.Should().Be(new ControllerOpenJob("j1", Requeueable: true));
    }

    [Fact]
    public async Task Apply_CloseSucceeded_persists_the_terminal_transition()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        await _jobs.InsertAsync(Job("j1"), ct);

        await _view.ApplyAsync(new ReconcileAction("j1", ReconcileOutcome.CloseSucceeded), ct);

        var got = await _jobs.GetAsync("j1", ct);
        got!.State.Should().Be(JobState.Succeeded);
        got.TerminalAtUnixMs.Should().Be(now);
    }

    [Fact]
    public async Task Apply_Requeue_returns_the_job_to_queued()
    {
        var ct = TestContext.Current.CancellationToken;
        await _jobs.InsertAsync(Job("j1"), ct);

        await _view.ApplyAsync(new ReconcileAction("j1", ReconcileOutcome.Requeue), ct);

        var got = await _jobs.GetAsync("j1", ct);
        got!.State.Should().Be(JobState.Queued);
        got.TerminalAtUnixMs.Should().BeNull();
    }

    private static Job Job(string id, bool requeueable = false) => new()
    {
        Id = id,
        NodeId = "node-1",
        Type = "service.restart",
        ParamsJson = "{}",
        State = JobState.Running,
        Cancellable = false,
        Requeueable = requeueable,
        CreatedAtUnixMs = 1000,
        StartedAtUnixMs = 1000
    };
}
