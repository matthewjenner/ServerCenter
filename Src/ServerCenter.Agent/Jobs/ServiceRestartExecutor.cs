using System.Text.Json;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Platform;

namespace ServerCenter.Agent.Jobs;

// Executes a "service.restart" job via the platform IServiceController. Params: { "unit": "..." }.
// Success is defined by game-level truth here as the service being Active after restart (a
// stricter readiness check belongs to the readiness primitive for game servers).
public sealed class ServiceRestartExecutor(IServiceController services) : IJobExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string JobType => "service.restart";

    public async Task<JobOutcome> ExecuteAsync(JobContext context, IJobSink sink, CancellationToken ct)
    {
        var unit = ParseUnit(context.ParamsJson);
        if (string.IsNullOrWhiteSpace(unit))
        {
            return JobOutcome.Failure("missing 'unit' param");
        }

        sink.Log(LogStream.Note, $"restarting {unit}");
        await services.RestartAsync(unit, ct);

        var state = await services.GetStateAsync(unit, ct);
        sink.Progress(100, $"state: {state}");

        return state == ServiceState.Active
            ? JobOutcome.Success()
            : JobOutcome.Failure($"service not active after restart: {state}");
    }

    private static string? ParseUnit(string paramsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Params>(paramsJson, JsonOptions)?.Unit;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Params(string Unit);
}
