using ServerCenter.Contracts.V1;

namespace ServerCenter.Core.Connection;

// Supplies the current node status the agent pushes up on each tick. Backed later by the
// platform interfaces (ISystemInfo, IServiceController). Ships with an in-memory fake.
public interface IAgentStatusSource
{
    Task<NodeStatus> GetStatusAsync(CancellationToken ct);
}
