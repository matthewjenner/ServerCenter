using ServerCenter.Controller.Services;

namespace ServerCenter.Controller.Endpoints;

// Serves agent self-update. The controller is the distribution root: agents poll IT for updates and
// pull the bundle from IT (never GitHub), so only trust/distribution is centralized. It advertises
// its own product version (the three tiers ship in lockstep) and streams the matching self-contained
// bundle per RID; the bundles are baked into the controller image at build time, so the advertised
// version and the served payload always agree.
//
// NOTE (2026-07-10): today this serves any caller. Restricting it to APPROVED/managed agents is a
// retrofit once the pending->approve trust model lands (see the trust-onboarding decision).
public static class AgentUpdateEndpoint
{
    private static readonly string[] SupportedRids = ["linux-x64", "linux-arm64"];

    public static void MapAgentUpdates(this WebApplication app)
    {
        string bundlesRoot = app.Configuration["AgentBundles:Root"] ?? "/app/agent-bundles";

        app.MapGet("/agent/version", () => Results.Ok(new AgentVersionResponse(ControllerVersion.Current)));

        app.MapGet("/agent/bundle/{rid}", (string rid) =>
        {
            // Whitelist the RID so the path can never escape the bundles directory.
            if (!SupportedRids.Contains(rid))
            {
                return Results.NotFound();
            }

            string path = Path.Combine(bundlesRoot, $"servercenter-agent-{rid}.tar.gz");
            return File.Exists(path)
                ? Results.File(path, "application/gzip", $"servercenter-agent-{rid}.tar.gz")
                : Results.NotFound();
        });
    }
}

public sealed record AgentVersionResponse(string Version);
