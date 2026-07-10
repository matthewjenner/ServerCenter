using ServerCenter.Contracts.V1;

namespace ServerCenter.Ui.Services;

// The dashboard's jobs source + trigger. Behind an interface so the jobs view-model can be tested
// with a fake (no controller / gRPC).
public interface IJobClient
{
    IAsyncEnumerable<JobListSnapshot> Watch(CancellationToken ct);

    Task<string> RestartServiceAsync(string agentId, string unit, CancellationToken ct);

    Task<UpdateTriggerResult> TriggerUpdateAsync(string agentId, string policyId, string? serviceUnit, CancellationToken ct);

    Task<UpdateTriggerResult> TriggerVmActionAsync(string nodeId, string action, CancellationToken ct);
}

// The controller's response to an operator trigger: "Dispatched" carries a job id; other outcomes
// (e.g. NotFound / NoDomain / NeedsConfirmation) carry a reason for the operator.
public sealed record UpdateTriggerResult(string Outcome, string? JobId, string Reason);
