using AwesomeAssertions;
using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Jobs;
using Xunit;

namespace ServerCenter.Controller.Tests;

public sealed class JobRepositoryTests : IAsyncLifetime
{
    private TempDatabase _db = null!;
    private JobRepository _jobs = null!;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        _db = await TempDatabase.CreateAsync(ct);
        _jobs = new JobRepository(_db.Database);

        var agents = new AgentNodeRepository(_db.Database);
        await agents.EnsureAgentAsync("agent-1", "agent-1", "fpr", 1, ct);
        await agents.EnsureNodeAsync("node-1", "agent-1", "guest", "managed", 1, ct);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Insert_and_get_round_trips_all_fields()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = NewJob("j1", requeueable: true);
        await _jobs.InsertAsync(job, ct);

        var got = await _jobs.GetAsync("j1", ct);

        got.Should().NotBeNull();
        got!.NodeId.Should().Be("node-1");
        got.Type.Should().Be("service.restart");
        got.State.Should().Be(JobState.Running);
        got.Requeueable.Should().BeTrue();
        got.CreatedAtUnixMs.Should().Be(1000);
    }

    [Fact]
    public async Task GetOpenJobsForAgent_excludes_terminal_jobs()
    {
        var ct = TestContext.Current.CancellationToken;
        await _jobs.InsertAsync(NewJob("open"), ct);
        await _jobs.InsertAsync(NewJob("done", state: JobState.Succeeded, terminalAt: 2000), ct);

        var open = await _jobs.GetOpenJobsForAgentAsync("agent-1", ct);

        open.Should().ContainSingle().Which.Id.Should().Be("open");
    }

    [Fact]
    public async Task UpdateState_to_terminal_sets_state_and_terminal_at()
    {
        var ct = TestContext.Current.CancellationToken;
        await _jobs.InsertAsync(NewJob("j1"), ct);

        await _jobs.UpdateStateAsync("j1", JobState.Succeeded, null, 5000, ct);

        var got = await _jobs.GetAsync("j1", ct);
        got!.State.Should().Be(JobState.Succeeded);
        got.TerminalAtUnixMs.Should().Be(5000);
    }

    [Fact]
    public async Task Requeue_resets_state_and_clears_terminal_and_start()
    {
        var ct = TestContext.Current.CancellationToken;
        await _jobs.InsertAsync(NewJob("j1", state: JobState.Failed, terminalAt: 5000), ct);

        await _jobs.UpdateStateAsync("j1", JobState.Queued, null, null, ct);

        var got = await _jobs.GetAsync("j1", ct);
        got!.State.Should().Be(JobState.Queued);
        got.TerminalAtUnixMs.Should().BeNull();
        got.StartedAtUnixMs.Should().BeNull();
    }

    [Fact]
    public async Task ListRecentJobs_returns_newest_first()
    {
        var ct = TestContext.Current.CancellationToken;
        await _jobs.InsertAsync(NewJob("old") with { CreatedAtUnixMs = 1000 }, ct);
        await _jobs.InsertAsync(NewJob("new") with { CreatedAtUnixMs = 2000 }, ct);

        var recent = await _jobs.ListRecentJobsAsync(10, ct);

        recent.Select(j => j.Id).Should().Equal("new", "old");
    }

    [Fact]
    public async Task AckLog_moves_the_watermark_forward_only()
    {
        var ct = TestContext.Current.CancellationToken;
        await _jobs.InsertAsync(NewJob("j1"), ct);
        await _jobs.AppendLogAsync("j1", 1, LogStream.Stdout, "line 1", 100, ct);
        await _jobs.AppendLogAsync("j1", 2, LogStream.Stdout, "line 2", 200, ct);

        await _jobs.AckLogAsync("j1", 2, ct);
        (await _jobs.GetAsync("j1", ct))!.LastAckedSeq.Should().Be(2);

        await _jobs.AckLogAsync("j1", 1, ct); // stale ack must not move it back
        (await _jobs.GetAsync("j1", ct))!.LastAckedSeq.Should().Be(2);
    }

    private static Job NewJob(
        string id, JobState state = JobState.Running, bool requeueable = false, long? terminalAt = null) => new()
    {
        Id = id,
        NodeId = "node-1",
        Type = "service.restart",
        ParamsJson = "{}",
        State = state,
        Cancellable = false,
        Requeueable = requeueable,
        CreatedAtUnixMs = 1000,
        StartedAtUnixMs = 1000,
        TerminalAtUnixMs = terminalAt
    };
}
