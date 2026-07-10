using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ServerCenter.Agent.Jobs;
using ServerCenter.Contracts.V1;
using ServerCenter.Core.Platform;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Agent.Tests;

// The agent's job execution: a pushed Command is dispatched to its executor, run, and a terminal
// CommandResult streamed back. Driven over the in-memory duplex link with a fake IServiceController.
public sealed class JobExecutionTests
{
    [Fact]
    public async Task Executes_service_restart_and_reports_success()
    {
        var ct = TestContext.Current.CancellationToken;
        using var link = new InMemoryDuplexLink();
        var services = new FakeServiceController();
        services.Seed("plex.service", ServiceState.Inactive);

        var handler = new JobExecutingCommandHandler(
            new ServerCenter.Core.Jobs.IJobExecutor[] { new ServiceRestartExecutor(services) },
            new AgentJobStore(),
            NullLogger<JobExecutingCommandHandler>.Instance);

        await handler.OnCommandAsync(
            new Command { JobId = "j1", Type = "service.restart", ParamsJson = "{\"unit\":\"plex.service\"}" },
            link.AgentSide, ct);

        var result = await ReadCommandResultAsync(link, ct);
        result.JobId.Should().Be("j1");
        result.FinalState.Should().Be(JobState.Succeeded);
        (await services.GetStateAsync("plex.service", ct)).Should().Be(ServiceState.Active); // restarted -> Active
    }

    [Fact]
    public async Task Unknown_job_type_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        using var link = new InMemoryDuplexLink();

        var handler = new JobExecutingCommandHandler(
            Array.Empty<ServerCenter.Core.Jobs.IJobExecutor>(),
            new AgentJobStore(),
            NullLogger<JobExecutingCommandHandler>.Instance);

        await handler.OnCommandAsync(
            new Command { JobId = "j2", Type = "does.not.exist", ParamsJson = "{}" }, link.AgentSide, ct);

        (await ReadCommandResultAsync(link, ct)).FinalState.Should().Be(JobState.Failed);
    }

    private static async Task<CommandResult> ReadCommandResultAsync(InMemoryDuplexLink link, CancellationToken ct)
    {
        await foreach (var message in link.ControllerSide.Incoming(ct))
        {
            if (message.PayloadCase == AgentMessage.PayloadOneofCase.CommandResult)
            {
                return message.CommandResult;
            }
        }

        throw new InvalidOperationException("no CommandResult received");
    }
}
