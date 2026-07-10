using ServerCenter.Core.Games;

namespace ServerCenter.Core.Recipes;

// A build recipe (brief 3.12, phase-0-contracts.md 6): the versioned data that stands a node up from
// nothing. Almost every field composes a primitive that already exists for another reason (apt/"what"
// provider, SteamCMD, config templating, service control, the script runner), so the engine lands
// late and cheap. Every step is idempotent / convergent (ensure-*, an "already done?" check), so
// build = repair = rebuild is one operation. Stored as versioned JSON keyed by (id, version) - a
// running server_instance pins the exact recipe version it was built from, so history reconstructs.
public sealed record BuildRecipe
{
    public required string Id { get; init; }

    public required int Version { get; init; }

    // Packages to ensure present before anything else (reuses the "what" provider). Optional: a pure
    // config recipe may declare none.
    public BaseRequirements? BaseRequirements { get; init; }

    // The anonymous SteamCMD app to ensure installed (reuses SteamAppSpec). Null for non-Steam servers.
    public SteamAppSpec? SteamApp { get; init; }

    // Config files to render + write (reuses ConfigFileSpec + the templating primitive).
    public IReadOnlyList<ConfigFileSpec> ConfigFiles { get; init; } = [];

    // Ordered idempotent scripts (the one genuinely new primitive, the script runner).
    public IReadOnlyList<RecipeScript> Scripts { get; init; } = [];

    // The systemd unit to ensure exists + enabled (reuses service control).
    public ServiceDefinition? ServiceDefinition { get; init; }

    // The game descriptor this recipe produces a server for (ties build to the capability layer).
    public DescriptorRef? DescriptorRef { get; init; }
}

public sealed record BaseRequirements(string Provider, IReadOnlyList<string> Packages);

// An ordered recipe step. Run the command UNLESS its alreadyDone check passes (convergence: skip
// work already done); on success, run the marker command (e.g. touch a sentinel) so the next apply
// skips it. alreadyDone/onSuccess are optional - a step with no alreadyDone always runs.
public sealed record RecipeScript(string Id, string Run, string? AlreadyDone = null, string? OnSuccess = null);

public sealed record ServiceDefinition(string Unit, string ExecStart, string? User = null, string Restart = "on-failure");

public sealed record DescriptorRef(string Id, int Version);
