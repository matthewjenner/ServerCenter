using AwesomeAssertions;
using ServerCenter.Agent.Linux;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Agent.Tests;

// Onboarding neuter: the controller owns the update plane, so the node's self-update timers and the
// unattended-upgrades service are masked (idempotent).
public sealed class UnattendedUpgradesNeutralizerTests
{
    [Fact]
    public async Task Masks_the_apt_timers_and_the_unattended_upgrades_service()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeProcessRunner runner = new FakeProcessRunner();

        await new UnattendedUpgradesNeutralizer(runner).EnsureDisabledAsync(new RecordingJobSink(), ct);

        // BeEquivalentTo with strict ordering: Equal() would compare the inner IReadOnlyList
        // elements by reference, not structurally.
        runner.Calls.Should().BeEquivalentTo(
            new[]
            {
                new[] { "mask", "--now", "apt-daily.timer" },
                new[] { "mask", "--now", "apt-daily-upgrade.timer" },
                new[] { "mask", "--now", "unattended-upgrades.service" }
            },
            options => options.WithStrictOrdering());
        runner.Invocations.Should().OnlyContain(i => i.File == "systemctl");
    }

    [Fact]
    public async Task Throws_when_a_mask_fails()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeProcessRunner runner = new FakeProcessRunner { Respond = (_, _) => new ProcessResult(1, string.Empty, "boom") };

        Func<Task> act = async () =>
            await new UnattendedUpgradesNeutralizer(runner).EnsureDisabledAsync(new RecordingJobSink(), ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
