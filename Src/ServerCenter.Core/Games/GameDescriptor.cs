namespace ServerCenter.Core.Games;

// A game capability descriptor (brief 3.10, phase-0-contracts.md 4): descriptor-selected behavior
// for a class of game server, stored as versioned JSON keyed by (id, version). Every per-game
// difference is DATA over the shared primitive library, not new code. Capability specs carry a
// `primitive` name (config-template / file-set / rcon / query-protocol); the plugin escape hatch
// (swap primitive -> plugin) is represented when the first plugin actually lands. `{{tokens}}` in
// values bind to a server_instance's params at run time (class-vs-instance split).
public sealed record GameDescriptor
{
    public required string Id { get; init; }

    public required int Version { get; init; }

    public required SteamAppSpec SteamApp { get; init; }

    public GameCapabilities Capabilities { get; init; } = new();
}

// SteamCMD is anonymous for all our servers (brief 7). betaBranch is null for the default branch.
public sealed record SteamAppSpec(long AppId, string InstallDir, string? BetaBranch = null);

// Each capability is optional; a descriptor only declares the ones the game supports. All map to the
// shared primitives and are consumed by the ICapability implementations (Phase 5d).
public sealed record GameCapabilities
{
    public ConfigGenSpec? ConfigGen { get; init; }

    public SaveBackupSpec? SaveBackup { get; init; }

    public StatsSpec? Stats { get; init; }

    public ShutdownSpec? Shutdown { get; init; }

    public ReadinessSpec? Readiness { get; init; }
}

public sealed record ConfigGenSpec(string Primitive, IReadOnlyList<ConfigFileSpec> Files);

public sealed record ConfigFileSpec(string SchemaRef, string Path, ConfigFormat Format);

public sealed record SaveBackupSpec(
    string Primitive,
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> Exclude,
    QuiesceSpec? Quiesce = null);

// Quiesce the server (e.g. flush/announce) before a save-backup. `via` selects the primitive
// (rcon today); `command` is the engine command to run.
public sealed record QuiesceSpec(string Via, string Command);

public sealed record StatsSpec(string Primitive, IReadOnlyDictionary<string, string> Commands);

public sealed record ShutdownSpec(string Primitive, string DrainCommand, int GraceSeconds);

// Readiness is game-level "accepting players", NOT process-alive (brief trap). `primitive` selects
// how (log-scrape / port-probe / query-protocol); `protocol` refines a query (e.g. a2s); `port` is
// usually a token like "{{ports.game}}".
public sealed record ReadinessSpec(string Primitive, string Port, string? Protocol = null);

// Config file formats the templating primitive can write.
public enum ConfigFormat
{
    Kv,
    Ini,
    Json,
    Xml
}
