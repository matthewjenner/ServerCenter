namespace ServerCenter.Core.Primitives;

// The S3 seam. Backs the one-backup surface (controller DB snapshots) and the separate
// game-save file-set backups (their own path). Kept behind an interface so backup/restore
// logic is Tier 1 testable against an in-memory fake with zero AWS.
public interface IObjectStore
{
    Task<PutResult> PutAsync(string key, Stream content, CancellationToken ct);

    Task<Stream> GetAsync(string key, string? versionId, CancellationToken ct);

    Task<IReadOnlyList<ObjectVersion>> ListVersionsAsync(string keyPrefix, CancellationToken ct);
}

public sealed record PutResult(string Key, string? VersionId, long Bytes);

public sealed record ObjectVersion(string Key, string VersionId, long Bytes, long LastModifiedUnixMs);
