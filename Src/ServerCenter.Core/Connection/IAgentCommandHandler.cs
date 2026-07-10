using ServerCenter.Contracts.V1;
using ServerCenter.Core.Transport;

namespace ServerCenter.Core.Connection;

// Where the agent's read loop dispatches controller-pushed commands. The transport is handed in
// so the handler can stream JobProgress / CommandResult back up while the job runs (the pump
// owns the transport; job execution needs to send on it).
public interface IAgentCommandHandler
{
    Task OnCommandAsync(Command command, IAgentTransport transport, CancellationToken ct);

    Task OnCancelAsync(CancelJob cancel, CancellationToken ct);
}
