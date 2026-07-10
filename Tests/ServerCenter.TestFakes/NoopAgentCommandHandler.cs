using System.Collections.Concurrent;
using ServerCenter.Contracts.V1;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Transport;

namespace ServerCenter.TestFakes;

// Records dispatched commands (Phase 1 is read-only; real execution is Phase 3). The record
// lets tests assert the read loop dispatched what the controller pushed.
public sealed class NoopAgentCommandHandler : IAgentCommandHandler
{
    public ConcurrentQueue<Command> Commands { get; } = new();

    public ConcurrentQueue<CancelJob> Cancels { get; } = new();

    public Task OnCommandAsync(Command command, IAgentTransport transport, CancellationToken ct)
    {
        Commands.Enqueue(command);
        return Task.CompletedTask;
    }

    public Task OnCancelAsync(CancelJob cancel, CancellationToken ct)
    {
        Cancels.Enqueue(cancel);
        return Task.CompletedTask;
    }
}
