using AwesomeAssertions;
using ServerCenter.Agent.Linux;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Platform;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Agent.Tests;

// apt "what" provider: command construction (non-interactive), upgradable parsing, and the reboot
// flag probe, all against a fake process runner. Real apt smokes on a Linux node (Tier 2).
public sealed class AptUpdateProviderTests
{
    private const string UpgradableOutput =
        "Listing...\n" +
        "zlib1g/jammy-updates 1:1.2.11.dfsg-2ubuntu9.2 amd64 [upgradable from: 1:1.2.11.dfsg-2ubuntu9]\n" +
        "libssl3/jammy-security 3.0.2-0ubuntu1.15 amd64 [upgradable from: 3.0.2-0ubuntu1.10]\n";

    [Fact]
    public async Task Check_refreshes_lists_then_parses_upgradable_packages()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeProcessRunner runner = new FakeProcessRunner
        {
            Respond = (file, args) => file == "apt" && args.Contains("list")
                ? new ProcessResult(0, UpgradableOutput, string.Empty)
                : new ProcessResult(0, string.Empty, string.Empty)
        };

        IReadOnlyList<AvailableUpdate> updates = await new AptUpdateProvider(runner).CheckAsync(ct);

        runner.Invocations[0].Should().Match<FakeProcessRunner.Invocation>(i => i.File == "apt-get" && i.Args[0] == "update");
        updates.Should().BeEquivalentTo(new[]
        {
            new AvailableUpdate("zlib1g", "1:1.2.11.dfsg-2ubuntu9", "1:1.2.11.dfsg-2ubuntu9.2"),
            new AvailableUpdate("libssl3", "3.0.2-0ubuntu1.10", "3.0.2-0ubuntu1.15")
        });
    }

    [Fact]
    public async Task Apply_named_packages_runs_a_targeted_only_upgrade_non_interactively()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeProcessRunner runner = new FakeProcessRunner();

        UpdateOutcome outcome = await new AptUpdateProvider(runner)
            .ApplyAsync(new UpdatePlan(["plexmediaserver"], AllowReboot: false), new RecordingJobSink(), ct);

        outcome.Success.Should().BeTrue();
        FakeProcessRunner.Invocation install = runner.Invocations.Single(i => i.Args.Contains("install"));
        install.File.Should().Be("apt-get");
        install.Args.Should().Equal("install", "-y", "--only-upgrade", "plexmediaserver");
        install.Env.Should().Contain(new KeyValuePair<string, string>("DEBIAN_FRONTEND", "noninteractive"));
    }

    [Fact]
    public async Task Apply_with_no_named_packages_upgrades_everything()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeProcessRunner runner = new FakeProcessRunner();

        await new AptUpdateProvider(runner)
            .ApplyAsync(new UpdatePlan([], AllowReboot: false), new RecordingJobSink(), ct);

        runner.Invocations.Should().ContainSingle(i => i.Args.Count == 2 && i.Args[0] == "upgrade" && i.Args[1] == "-y");
    }

    [Fact]
    public async Task Apply_reports_reboot_required_from_the_flag_probe()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        // `test -f /var/run/reboot-required` -> exit 0 means the flag is present. Everything here
        // succeeds, so the probe returns 0 and reboot-required is reported.
        FakeProcessRunner runner = new FakeProcessRunner { Respond = (_, _) => new ProcessResult(0, string.Empty, string.Empty) };

        UpdateOutcome outcome = await new AptUpdateProvider(runner)
            .ApplyAsync(new UpdatePlan([], AllowReboot: true), new RecordingJobSink(), ct);

        outcome.RebootRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Apply_continues_past_a_failed_refresh_and_succeeds_with_a_warning()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        // apt-get update fails on one broken repo (exit 100) but the upgrade still succeeds. A single
        // bad third-party source must not fail the whole node's patch - we press on and warn.
        FakeProcessRunner runner = new FakeProcessRunner
        {
            Respond = (file, args) => file == "apt-get" && args[0] == "update"
                ? new ProcessResult(100, string.Empty, "E: The repository 'mirror+file:/x any-version Release' does not have a Release file.")
                : new ProcessResult(0, string.Empty, string.Empty)
        };
        RecordingJobSink sink = new RecordingJobSink();

        UpdateOutcome outcome = await new AptUpdateProvider(runner)
            .ApplyAsync(new UpdatePlan([], AllowReboot: false), sink, ct);

        outcome.Success.Should().BeTrue();
        // The upgrade actually ran, and the refresh failure surfaced as a warning, not a hard failure.
        runner.Invocations.Should().Contain(i => i.Args.Count == 2 && i.Args[0] == "upgrade");
        sink.Logs.Should().Contain(l => l.Stream == LogStream.Stderr && l.Line.Contains("does not have a Release file"));
        sink.Progresses.Should().Contain(p => p.Note != null && p.Note.Contains("some repos failed to refresh"));
    }

    [Fact]
    public async Task Apply_fails_when_the_upgrade_itself_fails()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeProcessRunner runner = new FakeProcessRunner
        {
            Respond = (file, args) => file == "apt-get" && args[0] == "upgrade"
                ? new ProcessResult(100, string.Empty, "E: Unable to fetch some archives")
                : new ProcessResult(0, string.Empty, string.Empty)
        };

        UpdateOutcome outcome = await new AptUpdateProvider(runner)
            .ApplyAsync(new UpdatePlan([], AllowReboot: false), new RecordingJobSink(), ct);

        outcome.Success.Should().BeFalse();
        outcome.FailReason.Should().Contain("apt-get failed");
    }

    [Fact]
    public async Task RebootRequired_is_false_when_the_flag_is_absent()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeProcessRunner runner = new FakeProcessRunner { Respond = (_, _) => new ProcessResult(1, string.Empty, string.Empty) };

        (await new AptUpdateProvider(runner).RebootRequiredAsync(ct)).Should().BeFalse();
    }
}
