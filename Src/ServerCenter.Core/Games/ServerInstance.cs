namespace ServerCenter.Core.Games;

// A concrete running server: the INSTANCE side of the class-vs-instance split. It binds a node to
// the exact descriptor/recipe/policy versions that govern it (so history reconstructs precisely) and
// carries its instance params (hostname, ports, rcon password, slots) as opaque JSON. The params are
// flattened to dotted tokens at run time to render the descriptor's templates (InstanceParamsResolver).
// Instance params are precious controller state and hold secrets - never persisted on the agent of
// record (brief 3.9 / 8.4).
public sealed record ServerInstance
{
    public required string Id { get; init; }

    public required string NodeId { get; init; }

    public string? DescriptorId { get; init; }

    public int? DescriptorVersion { get; init; }

    public string? RecipeId { get; init; }

    public int? RecipeVersion { get; init; }

    public string? PolicyId { get; init; }

    public int? PolicyVersion { get; init; }

    public required string InstanceParamsJson { get; init; }

    public long CreatedAtUnixMs { get; init; }
}
