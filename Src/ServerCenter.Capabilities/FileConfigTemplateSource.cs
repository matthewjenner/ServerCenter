using ServerCenter.Core.Capabilities;

namespace ServerCenter.Capabilities;

// Resolves a schemaRef ("cs2/server.cfg") to template text under a templates root directory. Real
// I/O, smoked at Tier 2; the pure render + write logic is tested against fakes.
public sealed class FileConfigTemplateSource(string templatesRoot) : IConfigTemplateSource
{
    public Task<string> GetAsync(string schemaRef, CancellationToken ct) =>
        File.ReadAllTextAsync(Path.Combine(templatesRoot, schemaRef), ct);
}
