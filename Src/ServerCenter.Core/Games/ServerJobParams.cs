using System.Text.Json;

namespace ServerCenter.Core.Games;

// The concrete params the controller pushes down for descriptor-driven server jobs. The controller
// resolves the descriptor + instance (the agent never sees them) and packs exactly what the executor
// needs: the SteamCMD request for an install, or the config files + their templates + the flattened
// instance params for a config-apply. Serialized in the game dialect (lowercase enum tokens).
public sealed record ServerInstallParams(long AppId, string InstallDir, string? BetaBranch, bool Validate);

public sealed record ServerConfigApplyParams(
    IReadOnlyList<ConfigFileSpec> Files,
    IReadOnlyDictionary<string, string> Templates,     // schemaRef -> template text (shipped with the job)
    IReadOnlyDictionary<string, string> InstanceParams); // flattened dotted tokens

// Teardown for a removed instance: the (per-instance, already-rendered) systemd unit, install dir, and
// config file paths to delete. Any field may be empty (an instance with no recipe has no unit, etc.).
public sealed record ServerRemoveParams(string Unit, string InstallDir, IReadOnlyList<string> ConfigPaths);

// Raw config editing (one already-rendered, per-instance path at a time). Read emits the file's current
// contents on the job's stdout log (null/missing -> empty); write persists raw content back verbatim.
public sealed record ServerConfigReadParams(string Path);

public sealed record ServerConfigWriteParams(string Path, string Content);

public static class ServerJobParamsSerializer
{
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, GameDescriptorSerializer.Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, GameDescriptorSerializer.Options)
        ?? throw new JsonException($"{typeof(T).Name} deserialized to null");
}
