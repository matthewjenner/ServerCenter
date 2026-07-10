using AwesomeAssertions;
using ServerCenter.Core.Primitives;
using ServerCenter.Primitives.Rcon;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Core.Tests;

// The RCON sequencing logic against a scripted fake server: auth handshake, command execute, and
// multi-packet response accumulation.
public sealed class SourceRconClientTests
{
    private static readonly RconEndpoint Endpoint = new("127.0.0.1", 27015, "secret");

    [Fact]
    public async Task Authenticates_then_executes_and_returns_the_response()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeRconChannelFactory factory = new FakeRconChannelFactory("secret",
            new Dictionary<string, string[]> { ["status"] = ["players 3/10"] });

        await using IRconSession session = await new SourceRconClient(factory).ConnectAsync(Endpoint, ct);
        string response = await session.ExecuteAsync("status", ct);

        response.Should().Be("players 3/10");
        factory.Last!.Sent.Select(p => p.Type).Should().Equal(
            RconPacketTypes.Auth, RconPacketTypes.ExecCommand, RconPacketTypes.ResponseValue);
    }

    [Fact]
    public async Task A_bad_password_is_rejected()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeRconChannelFactory factory = new FakeRconChannelFactory("secret");

        Func<Task<IRconSession>> act = async () =>
            await new SourceRconClient(factory).ConnectAsync(Endpoint with { Password = "wrong" }, ct);

        await act.Should().ThrowAsync<RconAuthenticationException>();
    }

    [Fact]
    public async Task A_multi_packet_response_is_concatenated_in_order()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeRconChannelFactory factory = new FakeRconChannelFactory("secret",
            new Dictionary<string, string[]> { ["cvarlist"] = ["alpha", "beta", "gamma"] });

        await using IRconSession session = await new SourceRconClient(factory).ConnectAsync(Endpoint, ct);

        (await session.ExecuteAsync("cvarlist", ct)).Should().Be("alphabetagamma");
    }

    [Fact]
    public async Task An_unmapped_command_returns_empty()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeRconChannelFactory factory = new FakeRconChannelFactory("secret");

        await using IRconSession session = await new SourceRconClient(factory).ConnectAsync(Endpoint, ct);

        (await session.ExecuteAsync("noop", ct)).Should().BeEmpty();
    }
}
