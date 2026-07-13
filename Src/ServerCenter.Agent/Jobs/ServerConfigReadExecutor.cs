using System.Text.Json;
using ServerCenter.Core.Capabilities;
using ServerCenter.Core.Games;
using ServerCenter.Core.Jobs;

namespace ServerCenter.Agent.Jobs;

// Executes a "server.config-read" job: reads one config file's current on-disk contents and emits them
// as a SINGLE stdout log line so the operator's editor gets the exact bytes back (an absent file reads
// as empty - a fresh instance the operator is about to author). Read-only; requeueable.
public sealed class ServerConfigReadExecutor(IConfigReader reader) : IJobExecutor
{
    public string JobType => "server.config-read";

    public async Task<JobOutcome> ExecuteAsync(JobContext context, IJobSink sink, CancellationToken ct)
    {
        ServerConfigReadParams request;
        try
        {
            request = ServerJobParamsSerializer.Deserialize<ServerConfigReadParams>(context.ParamsJson);
        }
        catch (JsonException ex)
        {
            return JobOutcome.Failure($"invalid server.config-read params: {ex.Message}");
        }

        string? contents = await reader.ReadAsync(request.Path, ct);
        sink.Log(LogStream.Note, $"read {request.Path}");
        // The whole file as one stdout line - the editor reads this line back verbatim.
        sink.Log(LogStream.Stdout, contents ?? string.Empty);
        return JobOutcome.Success();
    }
}
