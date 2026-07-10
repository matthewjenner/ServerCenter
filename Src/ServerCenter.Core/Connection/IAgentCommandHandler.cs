using ServerCenter.Contracts.V1;

namespace ServerCenter.Core.Connection;

// Where the agent's read loop dispatches controller-pushed commands. Phase 1 is read-only, so
// the default handler is a no-op; the real job execution wiring lands in Phase 3. The seam
// exists now so the pump does not need changing later.
public interface IAgentCommandHandler
{
    Task OnCommandAsync(Command command, CancellationToken ct);

    Task OnCancelAsync(CancelJob cancel, CancellationToken ct);
}
