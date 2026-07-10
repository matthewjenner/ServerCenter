using System.Collections.Concurrent;
using ServerCenter.Core.Jobs;

namespace ServerCenter.TestFakes;

// In-memory IControllerJobView. Seed the open jobs the controller believes are running for an
// agent, then inspect the reconcile actions applied during the resync handshake.
public sealed class FakeControllerJobView : IControllerJobView
{
    private readonly ConcurrentDictionary<string, List<ControllerOpenJob>> _open = new();

    public List<ReconcileAction> Applied { get; } = [];

    public void SeedOpenJobs(string agentId, params ControllerOpenJob[] jobs) =>
        _open[agentId] = [.. jobs];

    public Task<IReadOnlyList<ControllerOpenJob>> GetOpenJobsAsync(string agentId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ControllerOpenJob>>(
            _open.TryGetValue(agentId, out var jobs) ? jobs : []);

    public Task ApplyAsync(ReconcileAction action, CancellationToken ct)
    {
        Applied.Add(action);
        return Task.CompletedTask;
    }
}
