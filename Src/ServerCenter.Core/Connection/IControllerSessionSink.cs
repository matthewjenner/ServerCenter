using ServerCenter.Contracts.V1;

namespace ServerCenter.Core.Connection;

// Where the controller's per-agent read loop delivers what the agent pushes up. Heartbeat and
// status feed agent-online + the dashboard (Phase 2); job progress / results feed the job
// spine (Phase 3). Ships with an in-memory recording fake.
public interface IControllerSessionSink
{
    Task OnHeartbeatAsync(string agentId, Heartbeat heartbeat, CancellationToken ct);

    Task OnStatusAsync(string agentId, NodeStatus status, CancellationToken ct);

    Task OnJobProgressAsync(string agentId, JobProgress progress, CancellationToken ct);

    Task OnCommandResultAsync(string agentId, CommandResult result, CancellationToken ct);
}
