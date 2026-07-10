using AwesomeAssertions;
using ServerCenter.Agent.Linux;
using ServerCenter.Core.Platform;
using Xunit;

namespace ServerCenter.Agent.Tests;

// The systemctl command construction + ActiveState parsing, tested with a fake process runner
// (the real systemctl execution is validated on a Linux node).
public sealed class LinuxServiceControllerTests
{
    [Theory]
    [InlineData("active", ServiceState.Active)]
    [InlineData("inactive", ServiceState.Inactive)]
    [InlineData("failed", ServiceState.Failed)]
    [InlineData("activating", ServiceState.Activating)]
    [InlineData("deactivating", ServiceState.Deactivating)]
    [InlineData("something-else", ServiceState.Unknown)]
    public async Task GetState_uses_a_structured_property_query_and_maps_it(string activeState, ServiceState expected)
    {
        var runner = new FakeProcessRunner { Respond = (_, _) => new ProcessResult(0, activeState, string.Empty) };
        var sut = new LinuxServiceController(runner);

        var state = await sut.GetStateAsync("plex.service", TestContext.Current.CancellationToken);

        state.Should().Be(expected);
        runner.Calls.Should().ContainSingle()
            .Which.Should().Equal("show", "plex.service", "--property=ActiveState", "--value");
    }

    [Fact]
    public async Task Restart_issues_systemctl_restart()
    {
        var runner = new FakeProcessRunner();
        await new LinuxServiceController(runner).RestartAsync("plex.service", TestContext.Current.CancellationToken);

        runner.Calls.Should().ContainSingle().Which.Should().Equal("restart", "plex.service");
    }

    [Fact]
    public async Task Restart_throws_on_a_nonzero_exit()
    {
        var runner = new FakeProcessRunner { Respond = (_, _) => new ProcessResult(1, string.Empty, "Unit not found.") };
        var sut = new LinuxServiceController(runner);

        var act = async () => await sut.RestartAsync("nope.service", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
