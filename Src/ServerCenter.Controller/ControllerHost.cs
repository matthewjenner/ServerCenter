using ServerCenter.Capabilities;
using ServerCenter.Controller.Endpoints;
using ServerCenter.Controller.Grpc;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Capabilities;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Identity;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Primitives;
using ServerCenter.Primitives.Libvirt;

namespace ServerCenter.Controller;

// Shared controller service registration + endpoint mapping, so Program and the real-socket
// integration test build the same host (the test adds real Kestrel + TLS around it).
public static class ControllerHost
{
    public static void AddControllerServices(
        this IServiceCollection services,
        ServerCenterDatabase database,
        bool requireClientCertificate,
        string templatesRoot = "templates",
        bool libvirtEnabled = false,
        string? libvirtConnectUri = null)
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
        services.AddSingleton<ConnectedAgents>();
        services.AddSingleton<JobDispatcher>();

        // Update plane (Phase 4): the declarative policy store + policy-resolving dispatcher.
        services.AddSingleton<UpdatePolicyRepository>();
        services.AddSingleton<UpdateJobDispatcher>();

        // Game-server plane (Phase 5): descriptor + instance stores, the descriptor-driven dispatcher,
        // and the template source it ships to config-apply jobs (a templates dir on the controller;
        // a DB-backed template store is a later refinement).
        services.AddSingleton<GameDescriptorRepository>();
        services.AddSingleton<ServerInstanceRepository>();
        services.AddSingleton<ServerJobDispatcher>();
        services.AddSingleton<IConfigTemplateSource>(new FileConfigTemplateSource(templatesRoot));
        // The sink delegates presence to AgentPresenceStore and persists job progress/results.
        services.AddSingleton<IControllerSessionSink, PersistingSessionSink>();

        // Controller-owned identity + connect-time authorization.
        services.AddSingleton<ControllerOwnedTrustProvider>();
        services.AddSingleton<IAgentTrustProvider>(sp => sp.GetRequiredService<ControllerOwnedTrustProvider>());
        services.AddSingleton<AgentAuthorizer>();
        services.AddSingleton(new AgentSecurityOptions(requireClientCertificate));

        // Operator fleet view (dashboard). Liveness thresholds: Stale after 30s, Offline after 90s.
        services.AddSingleton(new LivenessTracker(staleAfterMs: 30_000, offlineAfterMs: 90_000));
        services.AddSingleton<FleetSnapshotBuilder>();

        // VM-lifecycle plane (Phase 6, controller-driven). libvirt is real virsh only where the
        // controller has the socket; elsewhere a null host keeps VM state Unknown and fails lifecycle
        // loudly. The state poller runs only when libvirt is configured.
        services.AddSingleton<LibvirtDomainStates>();
        if (libvirtEnabled)
        {
            services.AddSingleton<ILibvirtHost>(sp =>
                new VirshLibvirtHost(sp.GetRequiredService<TimeProvider>(), "virsh", libvirtConnectUri));
            services.AddHostedService<LibvirtStatePoller>();
        }
        else
        {
            services.AddSingleton<ILibvirtHost, NullLibvirtHost>();
        }
    }

    public static void MapControllerEndpoints(this WebApplication app)
    {
        app.MapGrpcService<AgentLinkService>();
        app.MapGrpcService<FleetService>();
        app.MapGrpcService<JobViewService>();
        app.MapEnrollment();
        app.MapJobs();
        app.MapUpdatePolicies();
        app.MapServerJobs();
        app.MapNodes();
        app.MapGet("/", () => "ServerCenter Controller. AgentLink + FleetView gRPC + enrollment + jobs endpoints are mapped.");
    }
}
