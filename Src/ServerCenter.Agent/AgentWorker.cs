using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerCenter.Agent.Jobs;
using ServerCenter.Agent.Linux;
using ServerCenter.Agent.Windows;
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
            var (connector, identity) = await AgentBootstrap.PrepareAsync(options, logger, stoppingToken);

            logger.LogInformation(
                "Agent {AgentId} (node kind {NodeKind}) dialing {Address}",
                identity.AgentId, identity.NodeKind, options.ControllerAddress);

            // Same binary, per-OS service control (real impls land in Phase 3b / Phase 8).
            IServiceController services = OperatingSystem.IsWindows()
                ? new WindowsServiceController()
                : new LinuxServiceController();
            var jobStore = new AgentJobStore();
            var commandHandler = new JobExecutingCommandHandler(
                new IJobExecutor[] { new ServiceRestartExecutor(services) },
                jobStore,
                loggerFactory.CreateLogger<JobExecutingCommandHandler>());

            var connectionOptions = new AgentConnectionOptions(
                options.HeartbeatInterval,
                new BackoffPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30)));

            await AgentConnection.RunAsync(
                connector.ConnectAsync,
                identity,
                jobStore,
                new BasicAgentStatusSource(),
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
