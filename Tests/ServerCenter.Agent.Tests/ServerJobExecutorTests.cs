using AwesomeAssertions;
using ServerCenter.Agent.Jobs;
using ServerCenter.Core.Capabilities;
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
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeSteamCmd steam = new FakeSteamCmd();

        JobOutcome outcome = await new ServerInstallExecutor(steam).ExecuteAsync(
            Context("server.install", new ServerInstallParams(730, "/opt/cs2", null, Validate: true)),
            new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeTrue();
        steam.Requests.Should().ContainSingle()
            .Which.Should().Be(new SteamAppRequest(730, "/opt/cs2", null, Validate: true));
    }

    [Fact]
    public async Task Install_fails_when_steamcmd_fails()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeSteamCmd steam = new FakeSteamCmd { Result = new SteamAppResult(false, null, "disk full") };

        JobOutcome outcome = await new ServerInstallExecutor(steam).ExecuteAsync(
            Context("server.install", new ServerInstallParams(730, "/opt/cs2", null, true)), new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeFalse();
        outcome.FailReason.Should().Contain("disk full");
    }

    [Fact]
    public async Task ConfigApply_renders_the_shipped_template_and_writes_it()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        RecordingConfigWriter writer = new RecordingConfigWriter();
        ServerConfigApplyParams payload = new ServerConfigApplyParams(
            [new ConfigFileSpec("cs2/server.cfg", "/opt/cs2/cfg/server.cfg", ConfigFormat.Kv)],
            new Dictionary<string, string> { ["cs2/server.cfg"] = "hostname={{name}}\nport={{ports.game}}" },
            new Dictionary<string, string> { ["name"] = "ffa", ["ports.game"] = "27015" });

        JobOutcome outcome = await new ServerConfigApplyExecutor(writer).ExecuteAsync(
            Context("server.config-apply", payload), new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeTrue();
        writer.Writes.Should().ContainSingle();
        writer.Writes[0].Path.Should().Be("/opt/cs2/cfg/server.cfg");
        writer.Writes[0].Content.Should().Be("hostname=ffa\nport=27015");
    }

    [Fact]
    public async Task ConfigApply_fails_on_a_missing_token()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ServerConfigApplyParams payload = new ServerConfigApplyParams(
            [new ConfigFileSpec("cs2/server.cfg", "/opt/cs2/cfg/server.cfg", ConfigFormat.Kv)],
            new Dictionary<string, string> { ["cs2/server.cfg"] = "rcon={{rcon.password}}" },
            new Dictionary<string, string>());

        JobOutcome outcome = await new ServerConfigApplyExecutor(new RecordingConfigWriter()).ExecuteAsync(
            Context("server.config-apply", payload), new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Remove_stops_disables_and_deletes_unit_install_dir_and_configs()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeServiceController services = new FakeServiceController();
        RecordingPathCleaner cleaner = new RecordingPathCleaner();
        ServerRemoveParams payload = new ServerRemoveParams(
            "sc-cs2-arena1.service",
            "/opt/servercenter/cs2/arena1",
            ["/opt/servercenter/cs2/arena1/cfg/server.cfg"]);

        JobOutcome outcome = await new ServerRemoveExecutor(services, cleaner).ExecuteAsync(
            Context("server.remove", payload), new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeTrue();
        services.Calls.Should().Contain(("stop", "sc-cs2-arena1.service"));
        services.Calls.Should().Contain(("disable", "sc-cs2-arena1.service"));
        services.Calls.Should().Contain(("daemon-reload", string.Empty));
        cleaner.Deleted.Should().Contain("/etc/systemd/system/sc-cs2-arena1.service");
        cleaner.Deleted.Should().Contain("/opt/servercenter/cs2/arena1");
        cleaner.Deleted.Should().Contain("/opt/servercenter/cs2/arena1/cfg/server.cfg");
    }

    [Fact]
    public async Task Remove_with_no_unit_only_deletes_paths()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeServiceController services = new FakeServiceController();
        RecordingPathCleaner cleaner = new RecordingPathCleaner();

        JobOutcome outcome = await new ServerRemoveExecutor(services, cleaner).ExecuteAsync(
            Context("server.remove", new ServerRemoveParams(string.Empty, "/opt/x/y", [])), new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeTrue();
        services.Calls.Should().BeEmpty();   // no unit -> no systemctl calls
        cleaner.Deleted.Should().Equal("/opt/x/y");
    }

    [Fact]
    public async Task ConfigRead_emits_the_file_contents_on_stdout()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        RecordingJobSink sink = new RecordingJobSink();

        JobOutcome outcome = await new ServerConfigReadExecutor(new StubConfigReader("hostname=ffa\nport=27015"))
            .ExecuteAsync(Context("server.config-read", new ServerConfigReadParams("/opt/cs2/arena1/server.cfg")), sink, ct);

        outcome.Succeeded.Should().BeTrue();
        sink.Logs.Should().Contain(l => l.Stream == LogStream.Stdout && l.Line == "hostname=ffa\nport=27015");
    }

    [Fact]
    public async Task ConfigRead_of_a_missing_file_emits_empty()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        RecordingJobSink sink = new RecordingJobSink();

        JobOutcome outcome = await new ServerConfigReadExecutor(new StubConfigReader(null))
            .ExecuteAsync(Context("server.config-read", new ServerConfigReadParams("/nope")), sink, ct);

        outcome.Succeeded.Should().BeTrue();
        sink.Logs.Should().Contain(l => l.Stream == LogStream.Stdout && l.Line == string.Empty);
    }

    [Fact]
    public async Task ConfigWrite_persists_raw_content()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        RecordingConfigWriter writer = new RecordingConfigWriter();

        JobOutcome outcome = await new ServerConfigWriteExecutor(writer).ExecuteAsync(
            Context("server.config-write", new ServerConfigWriteParams("/opt/cs2/arena1/server.cfg", "edited")),
            new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeTrue();
        writer.Writes.Should().ContainSingle();
        writer.Writes[0].Path.Should().Be("/opt/cs2/arena1/server.cfg");
        writer.Writes[0].Content.Should().Be("edited");
    }

    private sealed class RecordingPathCleaner : IPathCleaner
    {
        public List<string> Deleted { get; } = [];

        public Task DeletePathAsync(string path, CancellationToken ct)
        {
            Deleted.Add(path);
            return Task.CompletedTask;
        }
    }

    private sealed class StubConfigReader(string? contents) : IConfigReader
    {
        public Task<string?> ReadAsync(string path, CancellationToken ct) => Task.FromResult(contents);
    }
}
