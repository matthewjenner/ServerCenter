using AwesomeAssertions;
using ServerCenter.Agent.Jobs;
using ServerCenter.Core.Jobs;
using ServerCenter.Core.Platform;
using ServerCenter.Core.Updates;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Agent.Tests;

// update.apply composition: provider selection by channel, preflight gating/ordering, the optional
// stop-update-start service bracket, and the reboot decision - all against fakes.
public sealed class UpdateApplyExecutorTests
{
    private static string Params(UpdateJobParams request) => UpdateJobParamsSerializer.Serialize(request);

    private static UpdateApplyExecutor Executor(
        IUpdateProvider provider, IServiceController services, params IPreflightAction[] preflight) =>
        new([provider], preflight.Length == 0 ? [new NotifyPreflight()] : preflight, services);

    private static JobContext Context(UpdateJobParams request) => new("j1", "update.apply", Params(request));

    [Fact]
    public async Task Applies_via_the_channel_provider_and_runs_preflight()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeUpdateProvider provider = new FakeUpdateProvider("apt");
        RecordingJobSink sink = new RecordingJobSink();

        JobOutcome outcome = await Executor(provider, new FakeServiceController()).ExecuteAsync(
            Context(new UpdateJobParams { Channel = "apt", Preflight = [PreflightStep.Notify] }), sink, ct);

        outcome.Succeeded.Should().BeTrue();
        provider.Applied.Should().BeTrue();
        sink.Logs.Select(l => l.Line).Should().Contain("preflight: update starting");
    }

    [Fact]
    public async Task Fails_when_no_provider_serves_the_channel()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeUpdateProvider provider = new FakeUpdateProvider("apt");

        JobOutcome outcome = await Executor(provider, new FakeServiceController()).ExecuteAsync(
            Context(new UpdateJobParams { Channel = "plex" }), new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeFalse();
        outcome.FailReason.Should().Contain("no update provider for channel 'plex'");
        provider.Applied.Should().BeFalse();
    }

    [Fact]
    public async Task Fails_before_applying_when_a_required_preflight_has_no_handler()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeUpdateProvider provider = new FakeUpdateProvider("apt");

        // Only Notify is registered; the policy also asks for a player drain, which no handler serves.
        JobOutcome outcome = await Executor(provider, new FakeServiceController()).ExecuteAsync(
            Context(new UpdateJobParams { Channel = "apt", Preflight = [PreflightStep.DrainPlayersViaRcon] }),
            new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeFalse();
        outcome.FailReason.Should().Contain("not available");
        provider.Applied.Should().BeFalse();
    }

    [Fact]
    public async Task Brackets_the_service_with_stop_then_start()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeServiceController services = new FakeServiceController();
        services.Seed("plex.service", ServiceState.Active);
        FakeUpdateProvider provider = new FakeUpdateProvider("plex");

        JobOutcome outcome = await Executor(provider, services).ExecuteAsync(
            Context(new UpdateJobParams
            {
                Channel = "plex",
                How = UpdateHow.StopUpdateStart,
                ServiceUnit = "plex.service"
            }),
            new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeTrue();
        services.Calls.Should().Equal(("stop", "plex.service"), ("start", "plex.service"));
        provider.Applied.Should().BeTrue();
    }

    [Fact]
    public async Task Restarts_the_service_even_when_the_update_fails()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeServiceController services = new FakeServiceController();
        FakeUpdateProvider provider = new FakeUpdateProvider("plex")
        {
            Outcome = new UpdateOutcome(Success: false, RebootRequired: false, "dpkg blew up")
        };

        JobOutcome outcome = await Executor(provider, services).ExecuteAsync(
            Context(new UpdateJobParams
            {
                Channel = "plex",
                How = UpdateHow.StopUpdateStart,
                ServiceUnit = "plex.service"
            }),
            new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeFalse();
        services.Calls.Should().Equal(("stop", "plex.service"), ("start", "plex.service")); // service came back up
    }

    [Fact]
    public async Task Records_the_reboot_decision_from_the_policy()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeUpdateProvider provider = new FakeUpdateProvider("apt")
        {
            Outcome = new UpdateOutcome(Success: true, RebootRequired: true, null)
        };
        RecordingJobSink sink = new RecordingJobSink();

        await Executor(provider, new FakeServiceController()).ExecuteAsync(
            Context(new UpdateJobParams { Channel = "apt", Reboot = RebootPolicy.IfRequired }), sink, ct);

        sink.Logs.Select(l => l.Line).Should().Contain(l => l.Contains("reboot required by policy"));
    }

    [Fact]
    public async Task Fails_on_unparseable_params()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        JobOutcome outcome = await Executor(new FakeUpdateProvider(), new FakeServiceController())
            .ExecuteAsync(new JobContext("j1", "update.apply", "not json"), new RecordingJobSink(), ct);

        outcome.Succeeded.Should().BeFalse();
        outcome.FailReason.Should().Contain("invalid update.apply params");
    }
}
