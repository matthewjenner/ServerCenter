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

    private sealed class NoopJobClient : IJobClient
    {
        public async IAsyncEnumerable<JobListSnapshot> Watch([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<string> RestartServiceAsync(string agentId, string unit, CancellationToken ct) => Task.FromResult(string.Empty);
    }
}
