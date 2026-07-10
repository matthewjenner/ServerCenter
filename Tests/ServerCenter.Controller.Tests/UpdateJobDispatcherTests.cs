using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Updates;
using Xunit;

namespace ServerCenter.Controller.Tests;

// The controller-side policy resolution -> dispatch path (real temp SQLite). The agent is offline in
// these tests, so an eligible dispatch persists a Queued update.apply job; we assert the resolved
// params and the non-eligible / not-found / needs-confirmation outcomes.
public sealed class UpdateJobDispatcherTests : IAsyncLifetime
{
    // 04:30 UTC sits inside a 120-minute window opening at the 04:00 cron occurrence.
    private static readonly DateTimeOffset InWindow = new(2026, 7, 10, 4, 30, 0, TimeSpan.Zero);

    private TempDatabase _db = null!;
    private UpdatePolicyRepository _policies = null!;
    private JobRepository _jobs = null!;
    private UpdateJobDispatcher _dispatcher = null!;

    public async ValueTask InitializeAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _db = await TempDatabase.CreateAsync(ct);
        _policies = new UpdatePolicyRepository(_db.Database);
        _jobs = new JobRepository(_db.Database);

        AgentNodeRepository nodes = new AgentNodeRepository(_db.Database);
        await nodes.EnsureAgentAsync("agent-1", "agent-1", "fpr", 1, ct);
        await nodes.EnsureNodeAsync("agent-1", "agent-1", "guest", "managed", 1, ct);

        FakeTimeProvider clock = new FakeTimeProvider(InWindow);
        JobDispatcher jobDispatcher = new JobDispatcher(_jobs, new ConnectedAgents(), clock);
        _dispatcher = new UpdateJobDispatcher(_policies, jobDispatcher, clock);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private async Task StoreAsync(UpdatePolicy policy) =>
        await _policies.InsertAsync(policy, 1000, TestContext.Current.CancellationToken);

    private static UpdatePolicy PlexPolicy(
        ScheduleMode mode = ScheduleMode.Manual, ApprovalMode approval = ApprovalMode.Auto) => new()
    {
        Id = "plex-nightly",
        Version = 1,
        What = new UpdateWhat("plex"),
        How = UpdateHow.StopUpdateStart,
        When = mode == ScheduleMode.Window
            ? new UpdateSchedule { Mode = ScheduleMode.Window, Cron = "0 4 * * *", WindowMinutes = 120 }
            : UpdateSchedule.Manual,
        Reboot = RebootPolicy.IfRequired,
        Preflight = [PreflightStep.Notify],
        Approval = approval
    };

    [Fact]
    public async Task Dispatches_a_resolved_update_apply_job_from_the_policy()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await StoreAsync(PlexPolicy());

        UpdateDispatchResult result = await _dispatcher.DispatchAsync(
            "agent-1", "plex-nightly", null, UpdatePolicyResolver.Trigger.Manual, [], "plex.service", ct);

        result.Outcome.Should().Be(UpdateDispatchOutcome.Dispatched);
        Job? job = await _jobs.GetAsync(result.JobId!, ct);
        job!.Type.Should().Be("update.apply");
        job.State.Should().Be(Core.Jobs.JobState.Queued);

        UpdateJobParams request = UpdateJobParamsSerializer.Deserialize(job.ParamsJson);
        request.Channel.Should().Be("plex");
        request.How.Should().Be(UpdateHow.StopUpdateStart);
        request.ServiceUnit.Should().Be("plex.service");
        request.Preflight.Should().Equal(PreflightStep.Notify);
    }

    [Fact]
    public async Task Reports_policy_not_found()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        UpdateDispatchResult result = await _dispatcher.DispatchAsync(
            "agent-1", "does-not-exist", null, UpdatePolicyResolver.Trigger.Manual, [], null, ct);

        result.Outcome.Should().Be(UpdateDispatchOutcome.PolicyNotFound);
        result.JobId.Should().BeNull();
    }

    [Fact]
    public async Task A_scheduled_tick_on_a_manual_policy_is_not_eligible()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await StoreAsync(PlexPolicy());

        UpdateDispatchResult result = await _dispatcher.DispatchAsync(
            "agent-1", "plex-nightly", null, UpdatePolicyResolver.Trigger.Scheduled, [], null, ct);

        result.Outcome.Should().Be(UpdateDispatchOutcome.NotEligible);
    }

    [Fact]
    public async Task A_scheduled_run_of_a_confirm_required_policy_needs_confirmation()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await StoreAsync(PlexPolicy(ScheduleMode.Window, ApprovalMode.RequireConfirmation));

        UpdateDispatchResult result = await _dispatcher.DispatchAsync(
            "agent-1", "plex-nightly", null, UpdatePolicyResolver.Trigger.Scheduled, [], null, ct);

        result.Outcome.Should().Be(UpdateDispatchOutcome.NeedsConfirmation);
    }
}
