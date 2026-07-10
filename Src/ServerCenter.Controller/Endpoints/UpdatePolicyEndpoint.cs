using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Updates;

namespace ServerCenter.Controller.Endpoints;

// Operator/dev surface for the update plane: store a policy revision and trigger an update from it.
// Operator auth is deferred (same posture as FleetView and the other job endpoints). The policy body
// is read as raw text and parsed with UpdatePolicySerializer so it uses the canonical kebab dialect
// rather than the minimal-API default enum binding.
public static class UpdatePolicyEndpoint
{
    public static void MapUpdatePolicies(this WebApplication app)
    {
        app.MapPost("/update-policies",
            async (HttpRequest request, UpdatePolicyRepository policies, TimeProvider clock, CancellationToken ct) =>
            {
                using StreamReader reader = new StreamReader(request.Body);
                string body = await reader.ReadToEndAsync(ct);
                UpdatePolicy policy = UpdatePolicySerializer.Deserialize(body);
                await policies.InsertAsync(policy, clock.GetUtcNow().ToUnixTimeMilliseconds(), ct);
                return Results.Ok(new { policy.Id, policy.Version });
            });

        app.MapPost("/jobs/update-apply",
            async (UpdateApplyRequest request, UpdateJobDispatcher dispatcher, CancellationToken ct) =>
            {
                // A dev/operator POST is an explicit (manual) trigger unless it declares itself a
                // scheduler tick. Manual overrides the window and is its own confirmation.
                UpdatePolicyResolver.Trigger trigger = request.Scheduled
                    ? UpdatePolicyResolver.Trigger.Scheduled
                    : UpdatePolicyResolver.Trigger.Manual;

                UpdateDispatchResult result = await dispatcher.DispatchAsync(
                    request.AgentId, request.PolicyId, request.PolicyVersion, trigger,
                    request.Packages ?? [], request.ServiceUnit, ct);

                return result.Outcome == UpdateDispatchOutcome.Dispatched
                    ? Results.Ok(new { jobId = result.JobId })
                    : Results.BadRequest(new { outcome = result.Outcome.ToString(), reason = result.Reason });
            });
    }
}

public sealed record UpdateApplyRequest(
    string AgentId,
    string PolicyId,
    int? PolicyVersion,
    IReadOnlyList<string>? Packages,
    string? ServiceUnit,
    bool Scheduled);
