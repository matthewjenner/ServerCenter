using System.Text.Json;
using ServerCenter.Controller.Services;

namespace ServerCenter.Controller.Endpoints;

// Minimal operator trigger for jobs (dev). Operator auth is deferred (same posture as
// FleetView); the gRPC operator surface for the UI lands with the Phase 3 UI job view.
public static class JobEndpoint
{
    public static void MapJobs(this WebApplication app)
    {
        app.MapPost("/jobs/service-restart",
            async (ServiceRestartRequest request, JobDispatcher dispatcher, CancellationToken ct) =>
            {
                var paramsJson = JsonSerializer.Serialize(new { unit = request.Unit });
                var jobId = await dispatcher.DispatchAsync(
                    request.AgentId, "service.restart", paramsJson, cancellable: false, requeueable: false, ct);
                return Results.Ok(new { jobId });
            });
    }
}

public sealed record ServiceRestartRequest(string AgentId, string Unit);
