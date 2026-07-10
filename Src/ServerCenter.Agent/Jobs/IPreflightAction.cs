using ServerCenter.Core.Jobs;
using ServerCenter.Core.Updates;

namespace ServerCenter.Agent.Jobs;

// One preflight action from a policy's ordered list (brief Phase 4 preflight). Pluggable so new
// actions (RCON player-drain, snapshot-first, quiesce) register as their primitives land. The
// update executor fails a job whose policy requires a step no handler provides, rather than silently
// skipping it - skipping a player drain before an update would be a quiet correctness bug.
public interface IPreflightAction
{
    PreflightStep Step { get; }

    Task RunAsync(IJobSink sink, CancellationToken ct);
}

// The always-available action: announce the update is starting. Real fan-out (warn guests / notify
// the operator) layers on later; the sink log is the first, honest form.
public sealed class NotifyPreflight : IPreflightAction
{
    public PreflightStep Step => PreflightStep.Notify;

    public Task RunAsync(IJobSink sink, CancellationToken ct)
    {
        sink.Log(LogStream.Note, "preflight: update starting");
        return Task.CompletedTask;
    }
}
