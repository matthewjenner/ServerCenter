using ServerCenter.Core.Capabilities;
using ServerCenter.Core.Games;
using ServerCenter.Core.Jobs;
using ServerCenter.Primitives.ConfigTemplating;

namespace ServerCenter.Capabilities;

// Config-gen backed by the config-templating primitive: for each declared file, fetch the template
// by schemaRef, render it against the instance params (a missing token is a hard error - a
// half-rendered config is worse than a clear failure), and write it to its path. Format-specific
// writers (INI/JSON/XML) layer on later; today the renderer produces text and the writer persists it.
public sealed class ConfigGenCapability(
    ConfigGenSpec spec, IConfigTemplateSource templates, IConfigWriter writer) : IConfigGenCapability
{
    public CapabilityKind Kind => CapabilityKind.ConfigGen;

    public async Task ApplyAsync(ConfigContext ctx, IJobSink sink, CancellationToken ct)
    {
        foreach (ConfigFileSpec file in spec.Files)
        {
            string template = await templates.GetAsync(file.SchemaRef, ct);
            string rendered = ConfigTemplateRenderer.Render(template, ctx.InstanceParams);
            sink.Log(LogStream.Note, $"writing {file.Path} ({file.Format.ToString().ToLowerInvariant()})");
            await writer.WriteAsync(file.Path, rendered, ct);
        }
    }
}
