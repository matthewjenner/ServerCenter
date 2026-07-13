namespace ServerCenter.Core.Capabilities;

// Where config-gen gets its templates and where it writes them. Seams so the ConfigGen capability is
// Tier 1 testable without a real templates directory or filesystem. A schemaRef (e.g.
// "cs2/server.cfg") names a template; the real source resolves it under a templates root, the real
// writer persists rendered content to a path.
public interface IConfigTemplateSource
{
    Task<string> GetAsync(string schemaRef, CancellationToken ct);
}

public interface IConfigWriter
{
    Task WriteAsync(string path, string content, CancellationToken ct);
}

// Deletes a path (file or directory, recursively) if it exists; a no-op when absent. The teardown
// seam for removing a server instance's on-disk footprint (install dir, config files, unit file).
// Kept behind an interface so remove logic is Tier 1 testable without touching a real filesystem.
public interface IPathCleaner
{
    Task DeletePathAsync(string path, CancellationToken ct);
}
