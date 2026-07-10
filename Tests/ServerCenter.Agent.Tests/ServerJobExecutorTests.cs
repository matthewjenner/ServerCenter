using AwesomeAssertions;
using ServerCenter.Agent.Jobs;
using ServerCenter.Core.Games;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Primitives;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Agent.Tests;

// The descriptor-driven server executors: install via SteamCMD, config-apply via the ConfigGen
// capability, both against fakes.
public sealed class ServerJobExecutorTests
{
    private static JobContext Context<T>(string type, T payload) =>
        new("j1", type, ServerJobParamsSerializer.Serialize(payload));

    [Fact]
    public async Task Install_runs_steamcmd_for_the_app()
    {
        var ct = TestContext.Current.CancellationToken;
        var steam = new FakeSteamCmd();

        var outcome = await new ServerInstallExecutor(steam).ExecuteAsync(
            Context("server.install", new ServerInstallParams(730, "/opt/cs2", null, Validate: true)),
            new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeTrue();
        steam.Requests.Should().ContainSingle()
            .Which.Should().Be(new SteamAppRequest(730, "/opt/cs2", null, Validate: true));
    }

    [Fact]
    public async Task Install_fails_when_steamcmd_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var steam = new FakeSteamCmd { Result = new SteamAppResult(false, null, "disk full") };

        var outcome = await new ServerInstallExecutor(steam).ExecuteAsync(
            Context("server.install", new ServerInstallParams(730, "/opt/cs2", null, true)), new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeFalse();
        outcome.FailReason.Should().Contain("disk full");
    }

    [Fact]
    public async Task ConfigApply_renders_the_shipped_template_and_writes_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var writer = new RecordingConfigWriter();
        var payload = new ServerConfigApplyParams(
            [new ConfigFileSpec("cs2/server.cfg", "/opt/cs2/cfg/server.cfg", ConfigFormat.Kv)],
            new Dictionary<string, string> { ["cs2/server.cfg"] = "hostname={{name}}\nport={{ports.game}}" },
            new Dictionary<string, string> { ["name"] = "ffa", ["ports.game"] = "27015" });

        var outcome = await new ServerConfigApplyExecutor(writer).ExecuteAsync(
            Context("server.config-apply", payload), new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeTrue();
        writer.Writes.Should().ContainSingle();
        writer.Writes[0].Path.Should().Be("/opt/cs2/cfg/server.cfg");
        writer.Writes[0].Content.Should().Be("hostname=ffa\nport=27015");
    }

    [Fact]
    public async Task ConfigApply_fails_on_a_missing_token()
    {
        var ct = TestContext.Current.CancellationToken;
        var payload = new ServerConfigApplyParams(
            [new ConfigFileSpec("cs2/server.cfg", "/opt/cs2/cfg/server.cfg", ConfigFormat.Kv)],
            new Dictionary<string, string> { ["cs2/server.cfg"] = "rcon={{rcon.password}}" },
            new Dictionary<string, string>());

        var outcome = await new ServerConfigApplyExecutor(new RecordingConfigWriter()).ExecuteAsync(
            Context("server.config-apply", payload), new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeFalse();
    }
}
