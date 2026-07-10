namespace ServerCenter.Core.Jobs;

// Runs one job type on the agent, streaming progress/log through the sink. Registered by type
// (e.g. "service.restart"); the command handler dispatches to the matching executor. Real
// executors compose the primitives (service control, SteamCMD, RCON, ...).
public interface IJobExecutor
{
    string JobType { get; }

    Task<JobOutcome> ExecuteAsync(JobContext context, IJobSink sink, CancellationToken ct);
}

public sealed record JobContext(string JobId, string Type, string ParamsJson);

public sealed record JobOutcome(bool Succeeded, string? FailReason)
{
    public static JobOutcome Success() => new(true, null);

    public static JobOutcome Failure(string reason) => new(false, reason);
}
