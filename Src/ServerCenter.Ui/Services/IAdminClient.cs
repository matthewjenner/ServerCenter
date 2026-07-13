namespace ServerCenter.Ui.Services;

// The operator "write" surface against the controller's REST endpoints (distinct from the gRPC job
// client): link a node to a libvirt domain, store a declarative definition, and trigger a server job.
// Behind an interface so the Manage view-model can be tested without a controller. Each call returns
// the controller's response body (a job id or a short message) and throws on a non-success status.
public interface IAdminClient
{
    Task<string> LinkDomainAsync(string nodeId, string domain, CancellationToken ct);

    // surface: "game-descriptors" | "build-recipes" | "server-instances"; body is the raw JSON.
    Task<string> StoreAsync(string surface, string bodyJson, CancellationToken ct);

    // kind: "server-install" | "server-config-apply" | "recipe-apply".
    Task<string> ServerJobAsync(string kind, string agentId, string instanceId, CancellationToken ct);

    // The defined server instances (their bindings; the secret instance params are NOT included).
    Task<IReadOnlyList<ServerInstanceRow>> ListServerInstancesAsync(CancellationToken ct);

    // The systemd services on a node (from its last heartbeat), for the operator's service picker.
    Task<IReadOnlyList<string>> ListServicesAsync(string nodeId, CancellationToken ct);

    // The libvirt domains the controller can see, for the domain picker (manual link override).
    Task<IReadOnlyList<string>> ListLibvirtDomainsAsync(CancellationToken ct);

    // The defined update-policy ids, for the update policy picker.
    Task<IReadOnlyList<string>> ListPolicyIdsAsync(CancellationToken ct);

    // The defined update policies with their full JSON bodies, so the Settings editor can load one to
    // read or clone (a pre-made starting point instead of authoring JSON from scratch).
    Task<IReadOnlyList<PolicyDoc>> ListPoliciesAsync(CancellationToken ct);

    // Operator action: mint a one-time bootstrap token for a new node. Returns the plaintext token
    // (delivered to the node out-of-band); ttlMinutes is clamped server-side.
    Task<EnrollmentTokenResult> MintEnrollmentTokenAsync(string displayName, int ttlMinutes, CancellationToken ct);

    // The seeded/defined games (descriptor id + latest version, paired with the same-id recipe version
    // if one exists), for the "pick a game" dropdown in the add-server form.
    Task<IReadOnlyList<GameOption>> ListGamesAsync(CancellationToken ct);

    // Remove a server instance: dispatches the cleanup job to its node and deletes the row.
    Task<string> RemoveServerInstanceAsync(string instanceId, CancellationToken ct);

    // The rendered config-file paths of an instance (the raw config editor's file list).
    Task<IReadOnlyList<string>> ListConfigFilesAsync(string instanceId, CancellationToken ct);

    // Dispatch a raw config-read job; returns the job id whose stdout log carries the file's contents
    // (read back via GetJobLogsAsync).
    Task<string> DispatchConfigReadAsync(string instanceId, string path, CancellationToken ct);

    // Dispatch a raw config-write job (writes content back to the file verbatim); returns the job id.
    Task<string> DispatchConfigWriteAsync(string instanceId, string path, string content, CancellationToken ct);

    // A job's persisted log lines in order; the config editor reads a read-job's emitted stdout back.
    Task<IReadOnlyList<JobLogEntry>> GetJobLogsAsync(string jobId, CancellationToken ct);
}

// One persisted job log line (stream is "stdout" | "stderr" | "note"), as returned by GET /jobs/{id}/logs.
public sealed record JobLogEntry(long Seq, string Stream, string Line);

// A game the operator can create a server from: the descriptor id + its latest version, plus the
// same-id build recipe's version if one is defined (null = descriptor only, no build recipe).
public sealed record GameOption(string Id, int DescriptorVersion, int? RecipeVersion)
{
    public string Display => RecipeVersion is null ? $"{Id} (descriptor v{DescriptorVersion})" : Id;
}

// The minted bootstrap token + when it expires (unix ms). The token is shown once for the operator
// to copy; the controller only keeps its hash.
public sealed record EnrollmentTokenResult(string Token, string DisplayName, long ExpiresAtUnixMs);

// A defined update policy's id and its full JSON body, for loading into the editor.
public sealed record PolicyDoc(string Id, string Json);

// A server instance as shown in the read view: its node + the descriptor/recipe/policy it is bound to.
// Deliberately omits instanceParamsJson (holds secrets like rcon passwords).
public sealed record ServerInstanceRow(
    string Id,
    string NodeId,
    string? DescriptorId,
    int? DescriptorVersion,
    string? RecipeId,
    int? RecipeVersion,
    string? PolicyId,
    int? PolicyVersion);
