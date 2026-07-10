using AwesomeAssertions;
using ServerCenter.Agent.Linux;
using Xunit;

namespace ServerCenter.Agent.Tests;

// Parsing the build id out of a Steam app manifest (KeyValues .acf), for buildid update-detect.
public sealed class SteamAppManifestTests
{
    private const string Manifest = """
        "AppState"
        {
        	"appid"		"730"
        	"Universe"		"1"
        	"name"		"Counter-Strike 2"
        	"StateFlags"		"4"
        	"buildid"		"18512345"
        	"LastUpdated"		"1720000000"
        }
        """;

    [Fact]
    public void ParseBuildId_reads_the_build_id()
    {
        SteamAppManifest.ParseBuildId(Manifest).Should().Be("18512345");
    }

    [Fact]
    public void ParseBuildId_is_null_when_absent()
    {
        SteamAppManifest.ParseBuildId("\"AppState\"\n{\n\t\"appid\"\t\"730\"\n}").Should().BeNull();
    }
}
