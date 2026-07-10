using System.Collections.Concurrent;
using ServerCenter.Contracts.V1;
using ServerCenter.Core.Connection;

namespace ServerCenter.Controller.Services;

// In-memory record of the latest heartbeat and status per agent - the read-only half of the
// dual-truth model (agent-online side). The Phase 2 dashboard reads this; SQLite-backed
// persistence of job history is separate (jobs are the precious surface, live presence is not).
public sealed class AgentPresenceStore : IControllerSessionSink
{
    private readonly ConcurrentDictionary<string, AgentPresence> _byAgent = new();

    public bool TryGet(string agentId, out AgentPresence? presence)
    {
        bool found = _byAgent.TryGetValue(agentId, out AgentPresence? value);
        presence = value;
        return found;
    }

    public IReadOnlyCollection<KeyValuePair<string, AgentPresence>> Snapshot() => _byAgent.ToArray();

    // Connect-time diagnostics from the Hello (version/os/arch): live, keyed by agent, refreshed on
    // every (re)connect - so a version bump shows up after the agent's next reconnect.
    public void RecordConnect(string agentId, string agentVersion, string osFamily, string arch)
    {
        AgentPresence entry = Entry(agentId);
        entry.AgentVersion = agentVersion;
        entry.OsFamily = osFamily;
        entry.Arch = arch;
    }

    public Task OnHeartbeatAsync(string agentId, Heartbeat heartbeat, CancellationToken ct)
    {
        Entry(agentId).LastHeartbeatUnixMs = heartbeat.AgentUnixMs;
        return Task.CompletedTask;
    }

    public Task OnStatusAsync(string agentId, NodeStatus status, CancellationToken ct)
    {
        Entry(agentId).LastStatus = status;
        return Task.CompletedTask;
    }

    // Job progress / results feed the job spine (Phase 3); presence does not record them.
    public Task OnJobProgressAsync(string agentId, JobProgress progress, CancellationToken ct) => Task.CompletedTask;

    public Task OnCommandResultAsync(string agentId, CommandResult result, CancellationToken ct) => Task.CompletedTask;

    private AgentPresence Entry(string agentId) => _byAgent.GetOrAdd(agentId, _ => new AgentPresence());
}

public sealed class AgentPresence
{
    public long LastHeartbeatUnixMs { get; set; }

    public NodeStatus? LastStatus { get; set; }

    // Connect-time diagnostics from the Hello.
    public string AgentVersion { get; set; } = string.Empty;

    public string OsFamily { get; set; } = string.Empty;

    public string Arch { get; set; } = string.Empty;
}
