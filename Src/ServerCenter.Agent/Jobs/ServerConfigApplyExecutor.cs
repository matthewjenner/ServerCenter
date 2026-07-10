using System.Text.Json;
using ServerCenter.Capabilities;
using ServerCenter.Core.Capabilities;
using ServerCenter.Core.Games;
using ServerCenter.Core.Jobs;

namespace ServerCenter.Agent.Jobs;

// Executes a "server.config-apply" job: renders each config file from its shipped template + the
// instance params and writes it to its path, via the ConfigGen capability. A missing template or an
// unresolved token fails the job (no half-written config). Idempotent -> requeueable.
public sealed class ServerConfigApplyExecutor(IConfigWriter writer) : IJobExecutor
{
    public string JobType => "server.config-apply";

    public async Task<JobOutcome> ExecuteAsync(JobContext context, IJobSink sink, CancellationToken ct)
    {
        ServerConfigApplyParams request;
        try
        {
            request = ServerJobParamsSerializer.Deserialize<ServerConfigApplyParams>(context.ParamsJson);
        }
        catch (JsonException ex)
        {
            return JobOutcome.Failure($"invalid server.config-apply params: {ex.Message}");
        }

        ConfigGenCapability capability = new ConfigGenCapability(
            new ConfigGenSpec("config-template", request.Files),
            new InlineConfigTemplateSource(request.Templates),
            writer);

        try
        {
            await capability.ApplyAsync(new ConfigContext(request.InstanceParams), sink, ct);
            return JobOutcome.Success();
        }
        catch (Exception ex) when (ex is KeyNotFoundException or FileNotFoundException)
        {
            return JobOutcome.Failure(ex.Message);
        }
    }
}
