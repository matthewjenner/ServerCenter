using AwesomeAssertions;
using ServerCenter.Capabilities;
using ServerCenter.Core.Capabilities;
using ServerCenter.Core.Games;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Capabilities.Tests;

// Config-gen renders each declared file from its template + instance params and writes it to its
// path. A missing token is a hard failure (no half-written config).
public sealed class ConfigGenCapabilityTests
{
    private static readonly ConfigGenSpec Spec = new("config-template",
        [new ConfigFileSpec("cs2/server.cfg", "/opt/cs2/cfg/server.cfg", ConfigFormat.Kv)]);

    [Fact]
    public async Task Renders_the_template_and_writes_it_to_the_path()
    {
        var ct = TestContext.Current.CancellationToken;
        var templates = new FakeConfigTemplateSource(
            new Dictionary<string, string> { ["cs2/server.cfg"] = "hostname={{name}}\nport={{ports.game}}" });
        var writer = new RecordingConfigWriter();
        var ctx = new ConfigContext(new Dictionary<string, string> { ["name"] = "ffa", ["ports.game"] = "27015" });

        await new ConfigGenCapability(Spec, templates, writer).ApplyAsync(ctx, new RecordingJobSink(), ct);

        writer.Writes.Should().ContainSingle();
        var write = writer.Writes[0];
        write.Path.Should().Be("/opt/cs2/cfg/server.cfg");
        write.Content.Should().Be("hostname=ffa\nport=27015");
    }

    [Fact]
    public async Task Fails_on_a_missing_token_without_writing()
    {
        var ct = TestContext.Current.CancellationToken;
        var templates = new FakeConfigTemplateSource(
            new Dictionary<string, string> { ["cs2/server.cfg"] = "rcon={{rcon.password}}" });
        var writer = new RecordingConfigWriter();
        var ctx = new ConfigContext(new Dictionary<string, string>());

        var act = async () =>
            await new ConfigGenCapability(Spec, templates, writer).ApplyAsync(ctx, new RecordingJobSink(), ct);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        writer.Writes.Should().BeEmpty();
    }
}
