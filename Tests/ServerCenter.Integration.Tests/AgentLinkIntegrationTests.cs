using System.Diagnostics;
using AwesomeAssertions;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ServerCenter.Agent;
using ServerCenter.Contracts.V1;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Connection;
using Xunit;

namespace ServerCenter.Integration.Tests;

// Drives the real gRPC path in-process: a GrpcChannel over the controller's in-memory
// TestServer, the real AgentLink service, the real transport adapters. Proves the handshake
// and heartbeat cross an actual gRPC bidi stream, not just the in-memory duplex link.
public sealed class AgentLinkIntegrationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Agent_connects_handshakes_and_its_heartbeat_reaches_the_controller()
    {
        var ct = TestContext.Current.CancellationToken;

        var channel = GrpcChannel.ForAddress(
            factory.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });

        var call = new AgentLink.AgentLinkClient(channel).Connect(cancellationToken: ct);
        await using var transport = new GrpcAgentTransport(channel, call);

        var identity = new AgentIdentity("it-agent", "0.1.0", "linux", "x64");
        var handshake = await AgentHandshake.PerformAsync(
            transport, identity, new EmptyAgentJobStateSource(), ct);

        handshake.Established.Should().BeTrue();
        handshake.SessionId.Should().NotBeEmpty();

        // Run the pump briefly so the agent emits its first heartbeat + status.
        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pump = AgentSessionPump.RunAsync(
            transport, new BasicAgentStatusSource(), new NoopCommandHandler(),
            TimeProvider.System, TimeSpan.FromSeconds(10), pumpCts.Token);

        var presence = factory.Services.GetRequiredService<AgentPresenceStore>();
        await WaitUntilAsync(
            () => presence.TryGet("it-agent", out var p) && p!.LastStatus is not null,
            ct);

        presence.TryGet("it-agent", out var recorded).Should().BeTrue();
        recorded!.LastStatus!.AgentHealth.Should().Be(ServiceHealth.Active);

        await pumpCts.CancelAsync();
        try
        {
            await pump;
        }
        catch (OperationCanceledException)
        {
            // expected on cancellation
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException("condition was not met in time");
            }

            await Task.Delay(25, ct);
        }
    }
}
