using System.Collections.Concurrent;
using ServerCenter.Core.Jobs;

namespace ServerCenter.Agent.Jobs;

// The agent's transient local job state (brief 3.6), keyed by job id, used to answer the resync
// handshake on reconnect. Losing it costs at most a re-run, never precious data.
public sealed class AgentJobStore : IAgentJobStateSource
{
    private readonly ConcurrentDictionary<string, Entry> _jobs = new();

    public void MarkRunning(string jobId) => _jobs[jobId] = new Entry(AgentJobLocalState.StillRunning, 0);

    public void MarkFinished(string jobId, bool succeeded, long lastSeq) =>
        _jobs[jobId] = new Entry(
            succeeded ? AgentJobLocalState.FinishedSucceeded : AgentJobLocalState.FinishedFailed, lastSeq);

    public void Forget(string jobId) => _jobs.TryRemove(jobId, out _);

    public Task<IReadOnlyList<AgentResyncEntry>> GetLocalJobStateAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AgentResyncEntry>>(
            _jobs.Select(kv => new AgentResyncEntry(kv.Key, kv.Value.State, kv.Value.LastSeq)).ToList());

    private sealed record Entry(AgentJobLocalState State, long LastSeq);
}
