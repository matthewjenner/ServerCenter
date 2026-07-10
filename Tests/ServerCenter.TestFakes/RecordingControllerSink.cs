using System.Collections.Concurrent;
using ServerCenter.Contracts.V1;
using ServerCenter.Core.Connection;

namespace ServerCenter.TestFakes;

// Records what the controller's session pump ingests from an agent, so tests can assert
// heartbeats / status / progress / results were delivered.
public sealed class RecordingControllerSink : IControllerSessionSink
{
    public ConcurrentQueue<(string AgentId, Heartbeat Heartbeat)> Heartbeats { get; } = new();

    public ConcurrentQueue<(string AgentId, NodeStatus Status)> Statuses { get; } = new();

    public ConcurrentQueue<(string AgentId, JobProgress Progress)> Progress { get; } = new();

    public ConcurrentQueue<(string AgentId, CommandResult Result)> Results { get; } = new();

    public Task OnHeartbeatAsync(string agentId, Heartbeat heartbeat, CancellationToken ct)
    {
        Heartbeats.Enqueue((agentId, heartbeat));
        return Task.CompletedTask;
    }

    public Task OnStatusAsync(string agentId, NodeStatus status, CancellationToken ct)
    {
        Statuses.Enqueue((agentId, status));
        return Task.CompletedTask;
    }

    public Task OnJobProgressAsync(string agentId, JobProgress progress, CancellationToken ct)
    {
        Progress.Enqueue((agentId, progress));
        return Task.CompletedTask;
    }

    public Task OnCommandResultAsync(string agentId, CommandResult result, CancellationToken ct)
    {
        Results.Enqueue((agentId, result));
        return Task.CompletedTask;
    }
}
