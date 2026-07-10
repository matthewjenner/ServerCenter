using ServerCenter.Core.Capabilities;

namespace ServerCenter.TestFakes;

// Canned templates keyed by schemaRef, so config-gen runs with no templates directory. An unknown
// schemaRef throws, mirroring a missing template file.
public sealed class FakeConfigTemplateSource(IReadOnlyDictionary<string, string> templates) : IConfigTemplateSource
{
    public Task<string> GetAsync(string schemaRef, CancellationToken ct) =>
        templates.TryGetValue(schemaRef, out string? template)
            ? Task.FromResult(template)
            : throw new FileNotFoundException($"no template for schemaRef '{schemaRef}'");
}

// Records what config-gen would write, so tests assert path + rendered content with no filesystem.
public sealed class RecordingConfigWriter : IConfigWriter
{
    public List<(string Path, string Content)> Writes { get; } = [];

    public Task WriteAsync(string path, string content, CancellationToken ct)
    {
        Writes.Add((path, content));
        return Task.CompletedTask;
    }
}
