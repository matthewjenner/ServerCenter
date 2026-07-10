using ServerCenter.Core.Capabilities;

namespace ServerCenter.Capabilities;

// A template source backed by templates shipped inline with a job (the controller resolves the
// templates and sends their text in the job params, so the agent renders locally - work stays
// decentralized, the controller owns the template data). An unknown schemaRef fails like a missing
// template file.
public sealed class InlineConfigTemplateSource(IReadOnlyDictionary<string, string> templates) : IConfigTemplateSource
{
    public Task<string> GetAsync(string schemaRef, CancellationToken ct) =>
        templates.TryGetValue(schemaRef, out string? template)
            ? Task.FromResult(template)
            : throw new FileNotFoundException($"no template shipped for schemaRef '{schemaRef}'");
}
