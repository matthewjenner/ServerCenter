using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServerCenter.Agent;
using ServerCenter.Contracts.V1;
using ServerCenter.Controller;
using ServerCenter.Controller.Persistence;
using ServerCenter.Controller.Services;
using ServerCenter.Core.Connection;
using ServerCenter.Core.Transport;
using Xunit;

namespace ServerCenter.Integration.Tests;

// The full mTLS path over a real socket: real Kestrel with a CA-signed server cert and client-
// cert enforcement on, an agent that enrolls over HTTPS and connects presenting its client cert.
// This is the end-to-end proof that closes Phase 1.
public sealed class MtlsIntegrationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private string _dbPath = null!;
    private string _baseAddress = null!;
    private ControllerOwnedTrustProvider _trust = null!;
    private AgentPresenceStore _presence = null!;

    public async ValueTask InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sc-mtls-{Guid.NewGuid():N}.db");

        ServerCenterDatabase database = new ServerCenterDatabase(_dbPath);
        await database.InitializeAsync(CancellationToken.None);
        ControllerOwnedTrustProvider bootstrap = new ControllerOwnedTrustProvider(new TrustRepository(database), TimeProvider.System);
        await bootstrap.EnsureCaAsync(CancellationToken.None);
        X509Certificate2 serverCertificate = await bootstrap.CreateServerCertificateAsync("localhost", CancellationToken.None);

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(serverCertificate, https =>
            {
                https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                https.ClientCertificateValidation = (_, _, _) => true;
            })));
        builder.Services.AddControllerServices(database, requireClientCertificate: true);

        _app = builder.Build();
        _app.MapControllerEndpoints();
        await _app.StartAsync();

        _baseAddress = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _trust = _app.Services.GetRequiredService<ControllerOwnedTrustProvider>();
        _presence = _app.Services.GetRequiredService<AgentPresenceStore>();
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (string? file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    [Fact]
    public async Task Enrolled_agent_connects_over_mtls_and_its_heartbeat_reaches_the_controller()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string token = await _trust.CreateBootstrapTokenAsync("mtls-node", TimeSpan.FromMinutes(10), ct);
        EnrollmentBundle bundle = await EnrollmentClient.EnrollAsync(_baseAddress, "mtls-node", token, ct);

        GrpcTransportConnector connector = new GrpcTransportConnector(_baseAddress, AgentTls.ToTlsMaterial(bundle));
        IAgentTransport agentTransport = await connector.ConnectAsync(ct);
        await using IAsyncDisposable _ = (IAsyncDisposable)agentTransport;

        AgentIdentity identity = new AgentIdentity(bundle.AgentId, "0.1.0", "linux", "x64");
        AgentHandshakeResult handshake = await AgentHandshake.PerformAsync(agentTransport, identity, new EmptyAgentJobStateSource(), ct);
        handshake.Established.Should().BeTrue();

        using CancellationTokenSource pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<AgentSessionOutcome> pump = AgentSessionPump.RunAsync(
            agentTransport, new BasicAgentStatusSource(), new NoopCommandHandler(),
            TimeProvider.System, TimeSpan.FromSeconds(10), pumpCts.Token);

        await WaitUntilAsync(() => _presence.TryGet(bundle.AgentId, out AgentPresence? p) && p!.LastStatus is not null, ct);

        await pumpCts.CancelAsync();
        try
        {
            await pump;
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }

    [Fact]
    public async Task Connection_without_a_client_certificate_is_rejected()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        // TLS to the controller but presenting NO client cert: the handshake completes, then the
        // controller's authorization kicks the session with a Goodbye.
        using SocketsHttpHandler handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, _, _, _) => true }
        };
        GrpcChannel channel = GrpcChannel.ForAddress(_baseAddress, new GrpcChannelOptions { HttpHandler = handler });
        AsyncDuplexStreamingCall<AgentMessage, ControllerMessage> call = new AgentLink.AgentLinkClient(channel).Connect(cancellationToken: ct);
        await using GrpcAgentTransport transport = new GrpcAgentTransport(channel, call);

        AgentHandshakeResult handshake = await AgentHandshake.PerformAsync(
            transport, new AgentIdentity("ghost", "0.1.0", "linux", "x64"), new EmptyAgentJobStateSource(), ct);
        handshake.Established.Should().BeTrue(); // handshake precedes authorization

        AgentSessionOutcome outcome = await AgentSessionPump.RunAsync(
            transport, new BasicAgentStatusSource(), new NoopCommandHandler(),
            TimeProvider.System, TimeSpan.FromSeconds(10), ct);

        outcome.Kind.Should().Be(SessionEndKind.ControllerGoodbye);
        outcome.GoodbyeReason.Should().Be(GoodbyeReason.Revoked);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct, int timeoutMs = 5000)
    {
        Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
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
