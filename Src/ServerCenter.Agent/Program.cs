using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServerCenter.Agent;

// The agent host. The SAME binary runs on the hypervisor host (node zero) and on every guest;
// it dials the controller and runs the connection loop. Generic Host + UseSystemd gives proper
// systemd lifecycle (Type=notify readiness, graceful SIGTERM, journald logging) on Linux, and is
// a harmless no-op elsewhere.
await Host.CreateDefaultBuilder(args)
    .UseSystemd()
    .ConfigureServices(services =>
    {
        services.AddSingleton(AgentOptions.FromEnvironment());
        services.AddHostedService<AgentWorker>();
    })
    .Build()
    .RunAsync();
