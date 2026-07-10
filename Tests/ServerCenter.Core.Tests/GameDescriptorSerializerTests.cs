using AwesomeAssertions;
using ServerCenter.Core.Games;
using Xunit;

namespace ServerCenter.Core.Tests;

// The stored descriptor body must match the hand-authored schema in the brief (lowercase enum
// tokens, camelCase props, template tokens preserved verbatim) and round-trip losslessly.
public sealed class GameDescriptorSerializerTests
{
    private static readonly GameDescriptor Cs2 = new()
    {
        Id = "cs2-dedicated",
        Version = 3,
        SteamApp = new SteamAppSpec(730, "/opt/cs2"),
        Capabilities = new GameCapabilities
        {
            ConfigGen = new ConfigGenSpec("config-template",
                [new ConfigFileSpec("cs2/server.cfg", "/opt/cs2/cfg/server.cfg", ConfigFormat.Kv)]),
            SaveBackup = new SaveBackupSpec("file-set",
                ["/opt/cs2/csgo/cfg"], ["*.tmp"], new QuiesceSpec("rcon", "sv_shutdown_notice")),
            Stats = new StatsSpec("rcon", new Dictionary<string, string> { ["players"] = "status" }),
            Shutdown = new ShutdownSpec("rcon", "say Restarting; sv_shutdown", 60),
            Readiness = new ReadinessSpec("query-protocol", "{{ports.game}}", "a2s")
        }
    };

    [Fact]
    public void Serialize_emits_the_brief_schema_tokens()
    {
        var json = GameDescriptorSerializer.Serialize(Cs2);

        json.Should().Contain("\"appId\":730");
        json.Should().Contain("\"format\":\"kv\"");
        json.Should().Contain("\"protocol\":\"a2s\"");
        json.Should().Contain("\"port\":\"{{ports.game}}\""); // tokens are preserved verbatim
    }

    [Fact]
    public void Round_trips_losslessly()
    {
        var restored = GameDescriptorSerializer.Deserialize(GameDescriptorSerializer.Serialize(Cs2));

        restored.Should().BeEquivalentTo(Cs2);
    }

    [Fact]
    public void Undeclared_capabilities_are_omitted_and_stay_null()
    {
        var minimal = new GameDescriptor
        {
            Id = "valheim",
            Version = 1,
            SteamApp = new SteamAppSpec(896660, "/opt/valheim")
        };

        var json = GameDescriptorSerializer.Serialize(minimal);
        json.Should().NotContain("saveBackup");

        GameDescriptorSerializer.Deserialize(json).Capabilities.SaveBackup.Should().BeNull();
    }
}
