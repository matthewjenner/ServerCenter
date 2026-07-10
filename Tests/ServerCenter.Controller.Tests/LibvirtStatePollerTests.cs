using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Primitives;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Controller.Tests;

// The poller seeds the domain-state cache from libvirt so the fleet view has VM truth.
public sealed class LibvirtStatePollerTests
{
    [Fact]
    public async Task Seeds_the_cache_from_the_libvirt_domain_list()
    {
        var ct = TestContext.Current.CancellationToken;
        var libvirt = new FakeLibvirtHost();
        libvirt.Seed("cs2-ffa", "uuid-1", DomainState.Running);
        libvirt.Seed("plex-vm", "uuid-2", DomainState.ShutOff);
        var states = new LibvirtDomainStates();

        var poller = new LibvirtStatePoller(libvirt, states, NullLogger<LibvirtStatePoller>.Instance);
        await poller.StartAsync(ct);   // FakeLibvirtHost completes synchronously (seed + empty event stream)
        await poller.StopAsync(ct);

        states.TryGet("cs2-ffa", out var a).Should().BeTrue();
        a.Should().Be(DomainState.Running);
        states.TryGet("plex-vm", out var b).Should().BeTrue();
        b.Should().Be(DomainState.ShutOff);
    }
}
