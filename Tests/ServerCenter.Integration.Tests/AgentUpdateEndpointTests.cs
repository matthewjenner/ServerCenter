using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using ServerCenter.Controller;
using ServerCenter.Controller.Endpoints;
using Xunit;

namespace ServerCenter.Integration.Tests;

// Exercises the controller-distributed agent self-update endpoints in-process: the advertised version
// and the per-RID bundle download (served from a bundles dir baked into the image in real deployments).
public sealed class AgentUpdateEndpointTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _dbPath = null!;
    private string _bundlesDir = null!;

    public ValueTask InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sc-agentupd-{Guid.NewGuid():N}.db");
        _bundlesDir = Path.Combine(Path.GetTempPath(), $"sc-bundles-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_bundlesDir);
        File.WriteAllText(Path.Combine(_bundlesDir, "servercenter-agent-linux-x64.tar.gz"), "fake-bundle");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Database:Path", _dbPath);
                builder.UseSetting("Security:RequireClientCertificate", "false");
                builder.UseSetting("AgentBundles:Root", _bundlesDir);
            });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (string file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
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

        try
        {
            Directory.Delete(_bundlesDir, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public async Task Version_endpoint_reports_a_semver()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        HttpClient client = _factory.CreateClient();

        AgentVersionResponse? body = await client.GetFromJsonAsync<AgentVersionResponse>("/agent/version", ct);

        body.Should().NotBeNull();
        body!.Version.Should().Contain(".");   // e.g. 0.1.3
    }

    [Fact]
    public async Task Bundle_endpoint_serves_a_present_rid()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/agent/bundle/linux-x64", ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/gzip");
        (await response.Content.ReadAsStringAsync(ct)).Should().Be("fake-bundle");
    }

    [Fact]
    public async Task Bundle_endpoint_404s_for_a_missing_rid_and_an_unsupported_rid()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        HttpClient client = _factory.CreateClient();

        // linux-arm64 is supported but not present in this test's bundles dir.
        (await client.GetAsync("/agent/bundle/linux-arm64", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // windows-x64 is not a whitelisted RID.
        (await client.GetAsync("/agent/bundle/windows-x64", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
