using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Updates;

namespace ServerCenter.Controller.Services;

// Turns "run this node's update policy" into a persisted update.apply job. It resolves the policy
// controller-side (the agent never sees the policy): loads the revision, runs the resolver, and only
// on an eligible + confirmed decision builds the job params and dispatches. Class fields come from
// the policy; instance fields (packages, service unit) come from the caller.
public sealed class UpdateJobDispatcher(UpdatePolicyRepository policies, JobDispatcher jobs, TimeProvider clock)
{
    public async Task<UpdateDispatchResult> DispatchAsync(
        string agentId,
        string policyId,
        int? policyVersion,
        UpdatePolicyResolver.Trigger trigger,
        IReadOnlyList<string> packages,
        string? serviceUnit,
        CancellationToken ct)
    {
        UpdatePolicy? policy = policyVersion is int version
            ? await policies.GetAsync(policyId, version, ct)
            : await policies.GetLatestAsync(policyId, ct);
        if (policy is null)
        {
            return UpdateDispatchResult.PolicyNotFound(policyId);
        }

        UpdatePolicyResolver.StartDecision decision = UpdatePolicyResolver.DecideStart(policy, trigger, clock.GetUtcNow());
        if (!decision.Eligible)
        {
            return UpdateDispatchResult.NotEligible(decision.IneligibleReason!);
        }

        if (decision.RequiresConfirmation)
        {
            return UpdateDispatchResult.NeedsConfirmation();
        }

        UpdateJobParams jobParams = new UpdateJobParams
        {
            Channel = policy.What.Provider,
            Packages = packages,
            How = policy.How,
            Preflight = decision.Preflight,
            Reboot = policy.Reboot,
            ServiceUnit = serviceUnit
        };

        // A mid-transaction apt is not cancellable, and an interrupted update is not blindly
        // requeueable (re-check first), so both flags are false (brief 2.1).
        string jobId = await jobs.DispatchAsync(
            agentId, "update.apply", UpdateJobParamsSerializer.Serialize(jobParams),
            cancellable: false, requeueable: false, ct);
        return UpdateDispatchResult.Dispatched(jobId);
    }
}

public enum UpdateDispatchOutcome
{
    Dispatched,
    PolicyNotFound,
    NotEligible,
    NeedsConfirmation
}

public sealed record UpdateDispatchResult(UpdateDispatchOutcome Outcome, string? JobId, string? Reason)
{
    public static UpdateDispatchResult Dispatched(string jobId) => new(UpdateDispatchOutcome.Dispatched, jobId, null);

    public static UpdateDispatchResult PolicyNotFound(string policyId) =>
        new(UpdateDispatchOutcome.PolicyNotFound, null, $"update policy '{policyId}' not found");

    public static UpdateDispatchResult NotEligible(string reason) =>
        new(UpdateDispatchOutcome.NotEligible, null, reason);

    public static UpdateDispatchResult NeedsConfirmation() =>
        new(UpdateDispatchOutcome.NeedsConfirmation, null, "policy requires operator confirmation; trigger manually to confirm");
}
