using System.Diagnostics;
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
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeLibvirtHost libvirt = new();
        libvirt.Seed("cs2-ffa", "uuid-1", DomainState.Running);
        libvirt.Seed("plex-vm", "uuid-2", DomainState.ShutOff);
        LibvirtDomainStates states = new();

        LibvirtStatePoller poller = new(libvirt, states, NullLogger<LibvirtStatePoller>.Instance);
        await poller.StartAsync(ct);

        // BackgroundService.StartAsync does NOT run ExecuteAsync to completion - it runs in the
        // background - so wait for the initial ListDomains seed to land rather than assuming it did.
        await WaitUntilAsync(() => states.TryGet("cs2-ffa", out _), ct);
        await poller.StopAsync(ct);

        states.TryGet("cs2-ffa", out DomainState running).Should().BeTrue();
        running.Should().Be(DomainState.Running);
        states.TryGet("plex-vm", out DomainState shutOff).Should().BeTrue();
        shutOff.Should().Be(DomainState.ShutOff);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct, int timeoutMs = 2000)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException("poller did not seed the cache in time");
            }

            await Task.Delay(10, ct);
        }
    }
}
