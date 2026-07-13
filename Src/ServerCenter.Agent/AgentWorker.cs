using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerCenter.Agent.Jobs;
using ServerCenter.Agent.Linux;
using ServerCenter.Agent.Windows;
using ServerCenter.Capabilities;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Platform;

namespace ServerCenter.Agent;

// Hosts the agent's outbound connection loop as a background service so it integrates with
// systemd (Type=notify readiness, graceful SIGTERM stop, journald logging). Same worker for host
// and guests.
public sealed class AgentWorker(
    AgentOptions options,
    ILogger<AgentWorker> logger,
    ILoggerFactory loggerFactory,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            (GrpcTransportConnector? connector, AgentIdentity? identity) = await AgentBootstrap.PrepareAsync(options, logger, stoppingToken);

            logger.LogInformation(
                "Agent {AgentId} (node kind {NodeKind}) dialing {Address}",
                identity.AgentId, identity.NodeKind, options.ControllerAddress);

            // Same binary, per-OS service control (real impls land in Phase 3b / Phase 8).
            IServiceController services = OperatingSystem.IsWindows()
                ? new WindowsServiceController()
                : new LinuxServiceController(new ProcessRunner());

            // The "what" providers are Linux-only for now (apt + Plex); on Windows the update
            // executor is registered with none, so a dispatched update.apply fails cleanly with
            // "no provider for channel" (Windows Update is Phase 9).
            ProcessRunner runner = new ProcessRunner();
            IReadOnlyList<IUpdateProvider> updateProviders = OperatingSystem.IsWindows()
                ? []
                : [new AptUpdateProvider(runner), new PlexUpdateProvider(new HttpFetcher(new HttpClient()), runner, new PlexUpdateOptions())];

            AgentJobStore jobStore = new AgentJobStore();
            JobExecutingCommandHandler commandHandler = new JobExecutingCommandHandler(
                new IJobExecutor[]
                {
                    new ServiceRestartExecutor(services),
                    new UpdateApplyExecutor(updateProviders, [new NotifyPreflight()], services),
                    // Descriptor-driven game-server jobs (Phase 5). Backup wiring waits on a real S3
                    // IObjectStore; install (SteamCMD) + config-apply need no extra infra.
                    new ServerInstallExecutor(new SteamCmd(runner)),
                    new ServerConfigApplyExecutor(new FileConfigWriter()),
                    new ServerRemoveExecutor(services, new FilePathCleaner()),
                    // Recipe-driven provisioning (Phase 7): compose the convergent primitives.
                    new RecipeApplyExecutor(
                        new AptPackageInstaller(runner), new SteamCmd(runner), new FileConfigWriter(),
                        new ScriptRunner(runner), services)
                },
                jobStore,
                loggerFactory.CreateLogger<JobExecutingCommandHandler>());

            AgentConnectionOptions connectionOptions = new AgentConnectionOptions(
                options.HeartbeatInterval,
                new BackoffPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30)));

            // Real telemetry on Linux (CPU/mem/disk/uptime/reboot); the zero-valued fallback elsewhere.
            IAgentStatusSource statusSource = OperatingSystem.IsLinux()
                ? new SystemInfoStatusSource(new ServerCenter.Agent.Linux.LinuxSystemInfo(runner))
                : new BasicAgentStatusSource();

            await AgentConnection.RunAsync(
                connector.ConnectAsync,
                identity,
                jobStore,
                statusSource,
                commandHandler,
                TimeProvider.System,
                connectionOptions,
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // graceful shutdown (SIGTERM)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent stopped: bootstrap or connection failed");
            lifetime.StopApplication();
        }
    }
}
