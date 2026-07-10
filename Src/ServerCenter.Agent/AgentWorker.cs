using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerCenter.Core.Connection;

namespace ServerCenter.Agent;

// Hosts the agent's outbound connection loop as a background service so it integrates with
// systemd (Type=notify readiness, graceful SIGTERM stop, journald logging). Same worker for host
// and guests.
public sealed class AgentWorker(
    AgentOptions options,
    ILogger<AgentWorker> logger,
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

            var connectionOptions = new AgentConnectionOptions(
                options.HeartbeatInterval,
                new BackoffPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30)));

            await AgentConnection.RunAsync(
                connector.ConnectAsync,
                identity,
                new EmptyAgentJobStateSource(),
                new BasicAgentStatusSource(),
                new NoopCommandHandler(),
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
