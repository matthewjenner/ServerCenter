using System.Text.Json;

namespace ServerCenter.Core.Updates;

// The concrete instructions the controller pushes down for an `update.apply` job. The class fields
// are resolved from an UpdatePolicy (channel/how/preflight/reboot); the instance fields (which
// packages, which service to bracket) come from the dispatch call. The agent executor consumes this
// - it never sees the policy, keeping policy resolution controller-side (brief: controller owns the
// declarative surfaces).
public sealed record UpdateJobParams
{
    public required string Channel { get; init; }

    // Empty means "everything the provider offers" (apt full upgrade; a single-package channel like
    // Plex ignores it).
    public IReadOnlyList<string> Packages { get; init; } = [];

    public UpdateHow How { get; init; } = UpdateHow.InPlace;

    public IReadOnlyList<PreflightStep> Preflight { get; init; } = [];

    public RebootPolicy Reboot { get; init; } = RebootPolicy.IfRequired;

    // The service to stop/start around the update when How brackets the service. Null for whole-OS
    // updates (apt) with no single owning unit.
    public string? ServiceUnit { get; init; }
}

public static class UpdateJobParamsSerializer
{
    public static string Serialize(UpdateJobParams request) => JsonSerializer.Serialize(request, UpdateJson.Options);

    public static UpdateJobParams Deserialize(string json) =>
        JsonSerializer.Deserialize<UpdateJobParams>(json, UpdateJson.Options)
        ?? throw new JsonException("update job params deserialized to null");
}
