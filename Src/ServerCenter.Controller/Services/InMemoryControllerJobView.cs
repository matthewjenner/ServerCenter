using ServerCenter.Core.Jobs;

namespace ServerCenter.Controller.Services;

// Read-only placeholder job store for Phase 1: there are no jobs yet, so no jobs are ever
// open and reconcile actions are only logged. The SQLite-backed implementation (the precious
// surface) replaces this in a later Phase 1 ship; the resync handshake already exercises this
// path with an empty job set.
public sealed class InMemoryControllerJobView(ILogger<InMemoryControllerJobView> logger) : IControllerJobView
{
    public Task<IReadOnlyList<ControllerOpenJob>> GetOpenJobsAsync(string agentId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ControllerOpenJob>>([]);

    public Task ApplyAsync(ReconcileAction action, CancellationToken ct)
    {
        logger.LogInformation("Reconcile {JobId} -> {Outcome}", action.JobId, action.Outcome);
        return Task.CompletedTask;
    }
}
