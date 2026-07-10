using AwesomeAssertions;
using ServerCenter.Core.Jobs;
using Xunit;

namespace ServerCenter.Core.Tests;

public sealed class JobStateMachineTests
{
    [Theory]
    [InlineData(JobState.Queued, JobState.Running)]
    [InlineData(JobState.Queued, JobState.Cancelled)]
    [InlineData(JobState.Running, JobState.Succeeded)]
    [InlineData(JobState.Running, JobState.Failed)]
    [InlineData(JobState.Running, JobState.TimedOut)]
    [InlineData(JobState.Running, JobState.Cancelled)]
    public void CanTransition_allows_legal_edges(JobState from, JobState to) =>
        JobStateMachine.CanTransition(from, to).Should().BeTrue();

    [Theory]
    [InlineData(JobState.Queued, JobState.Succeeded)]   // must go through Running
    [InlineData(JobState.Succeeded, JobState.Running)]  // terminal is terminal
    [InlineData(JobState.Cancelled, JobState.Running)]
    [InlineData(JobState.Running, JobState.Queued)]
    public void CanTransition_rejects_illegal_edges(JobState from, JobState to) =>
        JobStateMachine.CanTransition(from, to).Should().BeFalse();

    [Theory]
    [InlineData(JobState.Succeeded)]
    [InlineData(JobState.Failed)]
    [InlineData(JobState.TimedOut)]
    [InlineData(JobState.Cancelled)]
    public void IsTerminal_true_for_terminal_states(JobState state) =>
        JobStateMachine.IsTerminal(state).Should().BeTrue();

    [Theory]
    [InlineData(JobState.Queued)]
    [InlineData(JobState.Running)]
    public void IsTerminal_false_for_open_states(JobState state) =>
        JobStateMachine.IsTerminal(state).Should().BeFalse();
}
