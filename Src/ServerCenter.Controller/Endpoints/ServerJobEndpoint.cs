using ServerCenter.Controller.Services;

namespace ServerCenter.Controller.Endpoints;

// Operator/dev triggers for descriptor-driven server jobs (operator auth deferred, like the other
// job endpoints). Both resolve the instance's descriptor controller-side and dispatch.
public static class ServerJobEndpoint
{
    public static void MapServerJobs(this WebApplication app)
    {
        app.MapPost("/jobs/server-install",
            async (ServerJobRequest request, ServerJobDispatcher dispatcher, CancellationToken ct) =>
                Respond(await dispatcher.InstallAsync(request.AgentId, request.InstanceId, ct)));

        app.MapPost("/jobs/server-config-apply",
            async (ServerJobRequest request, ServerJobDispatcher dispatcher, CancellationToken ct) =>
                Respond(await dispatcher.ConfigApplyAsync(request.AgentId, request.InstanceId, ct)));

        app.MapPost("/jobs/recipe-apply",
            async (ServerJobRequest request, ServerJobDispatcher dispatcher, CancellationToken ct) =>
                Respond(await dispatcher.ApplyRecipeAsync(request.AgentId, request.InstanceId, ct)));

        // Raw config read/edit: the node is derived from the instance, so these take instanceId (not
        // agentId) + the target path. Read emits contents on the job's stdout log (GET /jobs/{id}/logs).
        app.MapPost("/jobs/server-config-read",
            async (ConfigReadRequest request, ServerJobDispatcher dispatcher, CancellationToken ct) =>
                Respond(await dispatcher.ConfigReadAsync(request.InstanceId, request.Path, ct)));

        app.MapPost("/jobs/server-config-write",
            async (ConfigWriteRequest request, ServerJobDispatcher dispatcher, CancellationToken ct) =>
                Respond(await dispatcher.ConfigWriteAsync(request.InstanceId, request.Path, request.Content, ct)));
    }

    private static IResult Respond(ServerDispatchResult result) =>
        result.Outcome == ServerDispatchOutcome.Dispatched
            ? Results.Ok(new { jobId = result.JobId })
            : Results.BadRequest(new { outcome = result.Outcome.ToString(), reason = result.Reason });
}

public sealed record ServerJobRequest(string AgentId, string InstanceId);

public sealed record ConfigReadRequest(string InstanceId, string Path);

public sealed record ConfigWriteRequest(string InstanceId, string Path, string Content);
