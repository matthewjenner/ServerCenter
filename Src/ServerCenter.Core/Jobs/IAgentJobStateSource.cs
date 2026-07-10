namespace ServerCenter.Core.Jobs;

// The agent's local, transient job state (kept keyed by job id for resync, brief 3.6). On
// reconnect the agent reports this so the controller can reconcile. Losing it costs at most
// a re-query or re-run, never precious data.
public interface IAgentJobStateSource
{
    Task<IReadOnlyList<AgentResyncEntry>> GetLocalJobStateAsync(CancellationToken ct);
}
