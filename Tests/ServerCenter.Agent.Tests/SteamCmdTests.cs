using AwesomeAssertions;
using ServerCenter.Agent.Linux;
using ServerCenter.Core.Primitives;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Agent.Tests;

// SteamCMD command construction + success parsing against a fake runner (real install is Tier 2).
public sealed class SteamCmdTests
{
    private static FakeProcessRunner SuccessRunner() =>
        new() { Respond = (_, _) => new ProcessResult(0, "Success! App '730' fully installed.", string.Empty) };

    [Fact]
    public async Task EnsureApp_builds_the_anonymous_validated_app_update_command()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = SuccessRunner();

        var result = await new SteamCmd(runner).EnsureAppAsync(
            new SteamAppRequest(730, "/opt/cs2"), new RecordingJobSink(), ct);

        result.Success.Should().BeTrue();
        var invocation = runner.Invocations.Single();
        invocation.File.Should().Be("steamcmd");
        invocation.Args.Should().Equal(
            "+force_install_dir", "/opt/cs2", "+login", "anonymous", "+app_update", "730", "validate", "+quit");
    }

    [Fact]
    public async Task EnsureApp_includes_the_beta_branch_before_validate()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = SuccessRunner();

        await new SteamCmd(runner).EnsureAppAsync(
            new SteamAppRequest(730, "/opt/cs2", BetaBranch: "preview"), new RecordingJobSink(), ct);

        runner.Invocations.Single().Args.Should().Equal(
            "+force_install_dir", "/opt/cs2", "+login", "anonymous",
            "+app_update", "730", "-beta", "preview", "validate", "+quit");
    }

    [Fact]
    public async Task EnsureApp_can_skip_validation()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = SuccessRunner();

        await new SteamCmd(runner).EnsureAppAsync(
            new SteamAppRequest(730, "/opt/cs2", Validate: false), new RecordingJobSink(), ct);

        runner.Invocations.Single().Args.Should().NotContain("validate");
    }

    [Fact]
    public async Task EnsureApp_fails_on_a_nonzero_exit()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new FakeProcessRunner
        {
            Respond = (_, _) => new ProcessResult(8, string.Empty, "Error! App '730' state is 0x202")
        };

        var result = await new SteamCmd(runner).EnsureAppAsync(
            new SteamAppRequest(730, "/opt/cs2"), new RecordingJobSink(), ct);

        result.Success.Should().BeFalse();
        result.FailReason.Should().Contain("steamcmd exited 8");
    }

    [Fact]
    public async Task EnsureApp_fails_when_the_success_marker_is_absent()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new FakeProcessRunner
        {
            Respond = (_, _) => new ProcessResult(0, "Update state (0x61) downloading, progress: 12.34", string.Empty)
        };

        var result = await new SteamCmd(runner).EnsureAppAsync(
            new SteamAppRequest(730, "/opt/cs2"), new RecordingJobSink(), ct);

        result.Success.Should().BeFalse();
    }
}
