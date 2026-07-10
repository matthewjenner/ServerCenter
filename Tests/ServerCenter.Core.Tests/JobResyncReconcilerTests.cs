using AwesomeAssertions;
using ServerCenter.Core.Jobs;
using Xunit;

namespace ServerCenter.Core.Tests;

// The top refactor trap (phase-0-contracts.md 2.3). Every reconciliation rule is pinned here.
public sealed class JobResyncReconcilerTests
{
    private static readonly ControllerOpenJob RunningJob = new("job-A", Requeueable: false);
    private static readonly ControllerOpenJob RunningRequeueable = new("job-A", Requeueable: true);

    [Fact]
    public void Empty_on_both_sides_produces_no_actions() =>
        JobResyncReconciler.Reconcile([], []).Should().BeEmpty();

    [Fact]
    public void Running_and_agent_still_running_resumes()
    {
        IReadOnlyList<ReconcileAction> actions = JobResyncReconciler.Reconcile(
            [RunningJob],
            [new AgentResyncEntry("job-A", AgentJobLocalState.StillRunning, 42)]);

        actions.Should().ContainSingle()
            .Which.Should().Be(new ReconcileAction("job-A", ReconcileOutcome.Resume));
    }

    [Fact]
    public void Running_and_agent_finished_succeeded_closes_succeeded()
    {
        IReadOnlyList<ReconcileAction> actions = JobResyncReconciler.Reconcile(
            [RunningJob],
            [new AgentResyncEntry("job-A", AgentJobLocalState.FinishedSucceeded, 99)]);

        actions.Should().ContainSingle()
            .Which.Should().Be(new ReconcileAction("job-A", ReconcileOutcome.CloseSucceeded));
    }

    [Fact]
    public void Running_and_agent_finished_failed_closes_failed()
    {
        IReadOnlyList<ReconcileAction> actions = JobResyncReconciler.Reconcile(
            [RunningJob],
            [new AgentResyncEntry("job-A", AgentJobLocalState.FinishedFailed, 99)]);

        actions.Should().ContainSingle()
            .Which.Should().Be(new ReconcileAction("job-A", ReconcileOutcome.CloseFailed));
    }

    [Fact]
    public void Running_and_agent_unknown_fails_lost_when_not_requeueable()
    {
        IReadOnlyList<ReconcileAction> actions = JobResyncReconciler.Reconcile(
            [RunningJob],
            [new AgentResyncEntry("job-A", AgentJobLocalState.Unknown, 0)]);

        actions.Should().ContainSingle()
            .Which.Should().Be(new ReconcileAction("job-A", ReconcileOutcome.FailLost));
    }

    [Fact]
    public void Running_and_agent_unknown_requeues_when_requeueable()
    {
        IReadOnlyList<ReconcileAction> actions = JobResyncReconciler.Reconcile(
            [RunningRequeueable],
            [new AgentResyncEntry("job-A", AgentJobLocalState.Unknown, 0)]);

        actions.Should().ContainSingle()
            .Which.Should().Be(new ReconcileAction("job-A", ReconcileOutcome.Requeue));
    }

    [Fact]
    public void Running_but_agent_never_mentions_it_is_treated_as_lost()
    {
        // Agent rebuilt and lost all state: reports nothing for a job the controller has open.
        IReadOnlyList<ReconcileAction> actions = JobResyncReconciler.Reconcile([RunningJob], []);

        actions.Should().ContainSingle()
            .Which.Should().Be(new ReconcileAction("job-A", ReconcileOutcome.FailLost));
    }

    [Fact]
    public void Agent_reports_a_job_the_controller_does_not_know_is_dropped()
    {
        IReadOnlyList<ReconcileAction> actions = JobResyncReconciler.Reconcile(
            [],
            [new AgentResyncEntry("ghost", AgentJobLocalState.StillRunning, 5)]);

        actions.Should().ContainSingle()
            .Which.Should().Be(new ReconcileAction("ghost", ReconcileOutcome.DropUnknownToAgent));
    }

    [Fact]
    public void Mixed_set_reconciles_each_job_independently_and_in_order()
    {
        IReadOnlyList<ReconcileAction> actions = JobResyncReconciler.Reconcile(
            [
                new ControllerOpenJob("resume", Requeueable: false),
                new ControllerOpenJob("done", Requeueable: false),
                new ControllerOpenJob("lost", Requeueable: true)
            ],
            [
                new AgentResyncEntry("resume", AgentJobLocalState.StillRunning, 1),
                new AgentResyncEntry("done", AgentJobLocalState.FinishedSucceeded, 2),
                new AgentResyncEntry("ghost", AgentJobLocalState.StillRunning, 3)
            ]);

        actions.Should().Equal(
            new ReconcileAction("resume", ReconcileOutcome.Resume),
            new ReconcileAction("done", ReconcileOutcome.CloseSucceeded),
            new ReconcileAction("lost", ReconcileOutcome.Requeue), // open, requeueable, agent silent
            new ReconcileAction("ghost", ReconcileOutcome.DropUnknownToAgent));
    }
}
