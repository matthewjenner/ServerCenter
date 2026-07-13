using AwesomeAssertions;
using ServerCenter.Agent.Linux;
using ServerCenter.Core.Platform;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Agent.Tests;

// The steamcmd "what" provider: locate + self-update in place, but only if steamcmd is actually on the
// node (never installs it where it doesn't belong).
public sealed class SteamCmdUpdateProviderTests
{
    [Fact]
    public async Task Apply_self_updates_the_located_steamcmd()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeProcessRunner runner = new FakeProcessRunner
        {
            Respond = (file, _) => file == "sh"
                ? new ProcessResult(0, "/usr/games/steamcmd\n", string.Empty)   // locate resolves the path
                : new ProcessResult(0, string.Empty, string.Empty)              // steamcmd +quit succeeds
        };

        UpdateOutcome outcome = await new SteamCmdUpdateProvider(runner)
            .ApplyAsync(new UpdatePlan([], AllowReboot: false), new RecordingJobSink(), ct);

        outcome.Success.Should().BeTrue();
        runner.Invocations.Should().Contain(i => i.File == "/usr/games/steamcmd" && i.Args.Contains("+quit"));
    }

    [Fact]
    public async Task Apply_skips_when_steamcmd_is_not_found()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeProcessRunner runner = new FakeProcessRunner
        {
            Respond = (_, _) => new ProcessResult(1, string.Empty, string.Empty)   // locate fails (not present)
        };

        UpdateOutcome outcome = await new SteamCmdUpdateProvider(runner)
            .ApplyAsync(new UpdatePlan([], AllowReboot: false), new RecordingJobSink(), ct);

        outcome.Success.Should().BeTrue();                  // no-op success
        runner.Invocations.Should().ContainSingle();        // only the locate; steamcmd never run
        runner.Invocations[0].File.Should().Be("sh");
    }
}
