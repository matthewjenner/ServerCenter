using ServerCenter.Controller.Endpoints;
using ServerCenter.Controller.Grpc;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Identity;
using ServerCenter.Core.Jobs;

namespace ServerCenter.Controller;

// Shared controller service registration + endpoint mapping, so Program and the real-socket
// integration test build the same host (the test adds real Kestrel + TLS around it).
public static class ControllerHost
{
    public static void AddControllerServices(
        this IServiceCollection services, ServerCenterDatabase database, bool requireClientCertificate)
    {
        services.AddGrpc();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(database);
        services.AddSingleton<JobRepository>();
        services.AddSingleton<AgentNodeRepository>();
        services.AddSingleton<TrustRepository>();

        // Precious state: SQLite-backed. Live presence: in-memory (transient, not precious).
        services.AddSingleton<IControllerJobView, SqliteControllerJobView>();
        services.AddSingleton<AgentPresenceStore>();
        services.AddSingleton<IControllerSessionSink>(sp => sp.GetRequiredService<AgentPresenceStore>());

        // Controller-owned identity + connect-time authorization.
        services.AddSingleton<ControllerOwnedTrustProvider>();
        services.AddSingleton<IAgentTrustProvider>(sp => sp.GetRequiredService<ControllerOwnedTrustProvider>());
        services.AddSingleton<AgentAuthorizer>();
        services.AddSingleton(new AgentSecurityOptions(requireClientCertificate));

        // Operator fleet view (dashboard). Liveness thresholds: Stale after 30s, Offline after 90s.
        services.AddSingleton(new LivenessTracker(staleAfterMs: 30_000, offlineAfterMs: 90_000));
        services.AddSingleton<FleetSnapshotBuilder>();
    }

    public static void MapControllerEndpoints(this WebApplication app)
    {
        app.MapGrpcService<AgentLinkService>();
        app.MapGrpcService<FleetService>();
        app.MapEnrollment();
        app.MapGet("/", () => "ServerCenter Controller. AgentLink + FleetView gRPC + enrollment endpoints are mapped.");
    }
}
