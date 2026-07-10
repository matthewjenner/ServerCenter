using System.Runtime.CompilerServices;
using AwesomeAssertions;
using ServerCenter.Contracts.V1;
using ServerCenter.Ui.Services;
using ServerCenter.Ui.ViewModels;
using Xunit;

namespace ServerCenter.Ui.Tests;

public sealed class JobsViewModelTests
{
    [Fact]
    public void Apply_adds_updates_and_removes_jobs()
    {
        var vm = new JobsViewModel(new NoopJobClient());

        vm.Apply(Snapshot(
            Job("j1", "n1", JobState.Running, 50),
            Job("j2", "n2", JobState.Queued, -1)));
        vm.Jobs.Should().HaveCount(2);

        // j2 dropped from the recent window; j1 finished.
        vm.Apply(Snapshot(Job("j1", "n1", JobState.Succeeded, 100)));

        vm.Jobs.Select(j => j.JobId).Should().BeEquivalentTo(["j1"]);
        var j1 = vm.Jobs.Single();
        j1.StateText.Should().Be("Succeeded");
        j1.ProgressText.Should().Be("100%");
    }

    private static JobListSnapshot Snapshot(params JobInfo[] jobs)
    {
        var snapshot = new JobListSnapshot { GeneratedUnixMs = 1 };
        snapshot.Jobs.AddRange(jobs);
        return snapshot;
    }

    private static JobInfo Job(string id, string node, JobState state, int pct) => new()
    {
        JobId = id,
        NodeId = node,
        Type = "service.restart",
        State = state,
        ProgressPct = pct
    };

    [Fact]
    public async Task Trigger_update_dispatches_with_trimmed_inputs_and_reports_the_job()
    {
        var client = new RecordingJobClient { Result = new UpdateTriggerResult("Dispatched", "abcdef1234", string.Empty) };
        var vm = new JobsViewModel(client)
        {
            UpdateAgentId = "  plex-node ",
            UpdatePolicyId = " plex-nightly ",
            UpdateServiceUnit = " plex.service "
        };

        await vm.TriggerUpdateCommand.ExecuteAsync(null);

        client.LastUpdate.Should().Be(("plex-node", "plex-nightly", "plex.service"));
        vm.UpdateStatus.Should().Be("dispatched abcdef12");
    }

    [Fact]
    public async Task Trigger_update_requires_an_agent_and_a_policy()
    {
        var client = new RecordingJobClient();
        var vm = new JobsViewModel(client) { UpdateAgentId = "n1", UpdatePolicyId = "" };

        await vm.TriggerUpdateCommand.ExecuteAsync(null);

        vm.UpdateStatus.Should().Contain("agent id and a policy id");
        client.LastUpdate.Should().BeNull();
    }

    [Fact]
    public async Task Trigger_update_surfaces_a_non_dispatched_outcome()
    {
        var client = new RecordingJobClient
        {
            Result = new UpdateTriggerResult("NeedsConfirmation", null, "policy requires operator confirmation")
        };
        var vm = new JobsViewModel(client) { UpdateAgentId = "n1", UpdatePolicyId = "p1" };

        await vm.TriggerUpdateCommand.ExecuteAsync(null);

        vm.UpdateStatus.Should().Be("NeedsConfirmation: policy requires operator confirmation");
    }

    private sealed class NoopJobClient : IJobClient
    {
        public async IAsyncEnumerable<JobListSnapshot> Watch([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<string> RestartServiceAsync(string agentId, string unit, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<UpdateTriggerResult> TriggerUpdateAsync(
            string agentId, string policyId, string? serviceUnit, CancellationToken ct) =>
            Task.FromResult(new UpdateTriggerResult("Dispatched", string.Empty, string.Empty));
    }

    private sealed class RecordingJobClient : IJobClient
    {
        public (string Agent, string Policy, string? Unit)? LastUpdate { get; private set; }

        public UpdateTriggerResult Result { get; set; } = new("Dispatched", string.Empty, string.Empty);

        public async IAsyncEnumerable<JobListSnapshot> Watch([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<string> RestartServiceAsync(string agentId, string unit, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<UpdateTriggerResult> TriggerUpdateAsync(
            string agentId, string policyId, string? serviceUnit, CancellationToken ct)
        {
            LastUpdate = (agentId, policyId, serviceUnit);
            return Task.FromResult(Result);
        }
    }
}
