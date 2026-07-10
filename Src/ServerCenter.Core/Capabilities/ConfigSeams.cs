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
