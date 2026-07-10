using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ServerCenter.Contracts.V1;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using Xunit;

namespace ServerCenter.Controller.Tests;

public sealed class PersistingSessionSinkTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero));
    private TempDatabase _db = null!;
    private JobRepository _jobs = null!;
    private PersistingSessionSink _sink = null!;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        _db = await TempDatabase.CreateAsync(ct);
        _jobs = new JobRepository(_db.Database);
        _sink = new PersistingSessionSink(new AgentPresenceStore(), _jobs, _clock);

        var repo = new AgentNodeRepository(_db.Database);
        await repo.EnsureAgentAsync("a1", "a1", "fpr", 1, ct);
        await repo.EnsureNodeAsync("a1", "a1", "guest", "managed", 1, ct);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Progress_marks_a_queued_job_running_and_persists_pct_and_log()
    {
        var ct = TestContext.Current.CancellationToken;
        await InsertJobAsync("j1", ServerCenter.Core.Jobs.JobState.Queued, ct);

        await _sink.OnJobProgressAsync("a1", new JobProgress
        {
            JobId = "j1",
            Seq = 1,
            Pct = 50,
            Note = "half",
            Log = new LogLine { Stream = LogStream.Stdout, Line = "hello" }
        }, ct);

        var job = await _jobs.GetAsync("j1", ct);
        job!.State.Should().Be(ServerCenter.Core.Jobs.JobState.Running);
        job.ProgressPct.Should().Be(50);
        job.StartedAtUnixMs.Should().NotBeNull();
        job.LastAckedSeq.Should().Be(1);
    }

    [Fact]
    public async Task CommandResult_moves_the_job_to_a_terminal_state()
    {
        var ct = TestContext.Current.CancellationToken;
        await InsertJobAsync("j1", ServerCenter.Core.Jobs.JobState.Running, ct);

        await _sink.OnCommandResultAsync("a1",
            new CommandResult { JobId = "j1", FinalState = JobState.Succeeded }, ct);

        var job = await _jobs.GetAsync("j1", ct);
        job!.State.Should().Be(ServerCenter.Core.Jobs.JobState.Succeeded);
        job.TerminalAtUnixMs.Should().NotBeNull();
    }

    private Task InsertJobAsync(string id, ServerCenter.Core.Jobs.JobState state, CancellationToken ct) =>
        _jobs.InsertAsync(new ServerCenter.Core.Jobs.Job
        {
            Id = id,
            NodeId = "a1",
            Type = "service.restart",
            ParamsJson = "{}",
            State = state,
            Cancellable = false,
            Requeueable = false,
            CreatedAtUnixMs = 1000,
            StartedAtUnixMs = state == ServerCenter.Core.Jobs.JobState.Running ? 1000 : null
        }, ct);
}
