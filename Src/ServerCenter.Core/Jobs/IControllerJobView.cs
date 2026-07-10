namespace ServerCenter.Core.Jobs;

// The controller's job store, as the handshake needs it. In-memory fake for Tier 1; the
// SQLite-backed implementation lands in a later Phase 1 ship. Async from the start so the
// SQLite impl does not force a refactor.
public interface IControllerJobView
{
    Task<IReadOnlyList<ControllerOpenJob>> GetOpenJobsAsync(string agentId, CancellationToken ct);

    Task ApplyAsync(ReconcileAction action, CancellationToken ct);
}
