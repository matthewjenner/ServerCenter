using System.Text.Json;
using ServerCenter.Core.Capabilities;
using ServerCenter.Core.Games;
using ServerCenter.Core.Jobs;

namespace ServerCenter.Agent.Jobs;

// Executes a "server.config-write" job: writes raw content back to one config file (the operator's edit
// in the raw config editor). Overwrites verbatim - note this is INDEPENDENT of config-apply, which
// re-renders from the descriptor template and would clobber a raw edit. Idempotent; requeueable.
public sealed class ServerConfigWriteExecutor(IConfigWriter writer) : IJobExecutor
{
    public string JobType => "server.config-write";

    public async Task<JobOutcome> ExecuteAsync(JobContext context, IJobSink sink, CancellationToken ct)
    {
        ServerConfigWriteParams request;
        try
        {
            request = ServerJobParamsSerializer.Deserialize<ServerConfigWriteParams>(context.ParamsJson);
        }
        catch (JsonException ex)
        {
            return JobOutcome.Failure($"invalid server.config-write params: {ex.Message}");
        }

        sink.Log(LogStream.Note, $"writing {request.Path}");
        await writer.WriteAsync(request.Path, request.Content, ct);
        sink.Progress(100, "config written");
        return JobOutcome.Success();
    }
}
