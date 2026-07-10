using System.Text.RegularExpressions;

namespace ServerCenter.Agent.Linux;

// Reads the build id out of a Steam app manifest (appmanifest_<appid>.acf, Valve KeyValues). Pure
// logic, directly unit-testable; the file read that feeds it is thin real I/O smoked at Tier 2.
public static partial class SteamAppManifest
{
    [GeneratedRegex("\"buildid\"\\s+\"(\\d+)\"")]
    private static partial Regex BuildIdRegex();

    public static string? ParseBuildId(string acfContent)
    {
        Match match = BuildIdRegex().Match(acfContent);
        return match.Success ? match.Groups[1].Value : null;
    }
}
