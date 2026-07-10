using ServerCenter.Contracts.V1;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Jobs;

namespace ServerCenter.Agent;

// Minimal Phase 1 (read-only) implementations of the agent seams. Real behavior lands later:
// status from ISystemInfo (Phase 1.5+), local job state for resync (Phase 3), command
// execution (Phase 3).

public sealed class BasicAgentStatusSource : IAgentStatusSource
{
    public Task<NodeStatus> GetStatusAsync(CancellationToken ct) => Task.FromResult(new NodeStatus
    {
        AgentHealth = ServiceHealth.Active,
        RebootPending = false,
        Resources = new ResourceSample()
    });
}

public sealed class EmptyAgentJobStateSource : IAgentJobStateSource
{
    public Task<IReadOnlyList<AgentResyncEntry>> GetLocalJobStateAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AgentResyncEntry>>([]);
}

public sealed class NoopCommandHandler : IAgentCommandHandler
{
    public Task OnCommandAsync(Command command, CancellationToken ct) => Task.CompletedTask;

    public Task OnCancelAsync(CancelJob cancel, CancellationToken ct) => Task.CompletedTask;
}
