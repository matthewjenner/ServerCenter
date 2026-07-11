using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using ServerCenter.Controller;
using ServerCenter.Controller.Endpoints;
using ServerCenter.Controller.Services;
using Xunit;

namespace ServerCenter.Integration.Tests;

// Exercises the token-gated enrollment endpoint in-process (no client cert needed for enrollment).
public sealed class EnrollmentEndpointTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _dbPath = null!;

    public ValueTask InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sc-enroll-{Guid.NewGuid():N}.db");
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Database:Path", _dbPath);
                builder.UseSetting("Security:RequireClientCertificate", "false");
            });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
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
    public async Task Enroll_with_a_valid_token_returns_a_cert_bundle()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ControllerOwnedTrustProvider trust = _factory.Services.GetRequiredService<ControllerOwnedTrustProvider>();
        string token = await trust.CreateBootstrapTokenAsync("node-a", TimeSpan.FromMinutes(10), ct);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync("/enroll", new EnrollRequest("node-a", token), ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        EnrollResponse? body = await response.Content.ReadFromJsonAsync<EnrollResponse>(ct);
        body.Should().NotBeNull();
        body!.AgentId.Should().NotBeEmpty();
        body.CertPem.Should().Contain("BEGIN CERTIFICATE");
        body.PrivateKeyPem.Should().Contain("PRIVATE KEY");
        body.CaCertPem.Should().Contain("BEGIN CERTIFICATE");
    }

    [Fact]
    public async Task Minted_token_enrolls_end_to_end()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        HttpClient client = _factory.CreateClient();

        // Operator mints a bootstrap token over the endpoint (no pre-seeding the trust provider)...
        HttpResponseMessage mint = await client.PostAsJsonAsync("/enroll-token", new EnrollTokenRequest("node-b", 30), ct);
        mint.StatusCode.Should().Be(HttpStatusCode.OK);
        EnrollTokenResponse? minted = await mint.Content.ReadFromJsonAsync<EnrollTokenResponse>(ct);
        minted.Should().NotBeNull();
        minted!.Token.Should().NotBeEmpty();
        minted.TtlMinutes.Should().Be(30);

        // ...and that plaintext token immediately enrolls the node.
        HttpResponseMessage enroll = await client.PostAsJsonAsync("/enroll", new EnrollRequest("node-b", minted.Token), ct);
        enroll.StatusCode.Should().Be(HttpStatusCode.OK);
        EnrollResponse? body = await enroll.Content.ReadFromJsonAsync<EnrollResponse>(ct);
        body!.CertPem.Should().Contain("BEGIN CERTIFICATE");
    }

    [Fact]
    public async Task Mint_requires_a_display_name()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/enroll-token", new EnrollTokenRequest("", null), ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Enroll_with_a_bad_token_is_unauthorized()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/enroll", new EnrollRequest("node-a", "not-a-real-token"), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
