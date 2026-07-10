using ServerCenter.Core.Primitives;

namespace ServerCenter.TestFakes;

// An in-memory versioned object store: each Put appends a new version of the key (v1, v2, ...) so
// backup/restore path selection and versioning are testable with zero AWS.
public sealed class FakeObjectStore : IObjectStore
{
    private readonly Dictionary<string, List<(string VersionId, byte[] Data)>> _objects = new();

    public List<string> PutKeys { get; } = [];

    public Task<PutResult> PutAsync(string key, Stream content, CancellationToken ct)
    {
        using MemoryStream buffer = new MemoryStream();
        content.CopyTo(buffer);
        byte[] data = buffer.ToArray();

        if (!_objects.TryGetValue(key, out List<(string VersionId, byte[] Data)>? versions))
        {
            versions = [];
            _objects[key] = versions;
        }

        string versionId = $"v{versions.Count + 1}";
        versions.Add((versionId, data));
        PutKeys.Add(key);
        return Task.FromResult(new PutResult(key, versionId, data.Length));
    }

    public Task<Stream> GetAsync(string key, string? versionId, CancellationToken ct)
    {
        List<(string VersionId, byte[] Data)> versions = _objects[key];
        (string VersionId, byte[] Data) entry = versionId is null ? versions[^1] : versions.First(v => v.VersionId == versionId);
        return Task.FromResult<Stream>(new MemoryStream(entry.Data));
    }

    public Task<IReadOnlyList<ObjectVersion>> ListVersionsAsync(string keyPrefix, CancellationToken ct)
    {
        List<ObjectVersion> results = _objects
            .Where(kv => kv.Key.StartsWith(keyPrefix, StringComparison.Ordinal))
            .SelectMany(kv => kv.Value.Select(v => new ObjectVersion(kv.Key, v.VersionId, v.Data.Length, 0)))
            .ToList();
        return Task.FromResult<IReadOnlyList<ObjectVersion>>(results);
    }
}
