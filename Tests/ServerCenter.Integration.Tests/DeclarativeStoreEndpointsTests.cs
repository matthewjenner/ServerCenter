using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using ServerCenter.Controller;
using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Games;
using ServerCenter.Core.Recipes;
using Xunit;

namespace ServerCenter.Integration.Tests;

// The operator store/list endpoints for the declarative surfaces (replaces sqlite3 seeding): each
// POST persists a revision and GET lists it back.
public sealed class DeclarativeStoreEndpointsTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _dbPath = null!;

    public ValueTask InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sc-store-{Guid.NewGuid():N}.db");
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
    }

    [Fact]
    public async Task Game_descriptor_round_trips_through_store_and_list()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        HttpClient client = _factory.CreateClient();
        GameDescriptor descriptor = new GameDescriptor
        {
            Id = "valheim",
            Version = 1,
            SteamApp = new SteamAppSpec(896660, "/opt/valheim")
        };

        HttpResponseMessage post = await client.PostAsync("/game-descriptors", Body(GameDescriptorSerializer.Serialize(descriptor)), ct);
        post.StatusCode.Should().Be(HttpStatusCode.OK);

        string listed = await client.GetStringAsync("/game-descriptors", ct);
        listed.Should().Contain("valheim").And.Contain("896660");
    }

    [Fact]
    public async Task Build_recipe_round_trips_through_store_and_list()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        HttpClient client = _factory.CreateClient();
        BuildRecipe recipe = new BuildRecipe
        {
            Id = "config-only",
            Version = 1,
            ConfigFiles = [new ConfigFileSpec("x", "/x", ConfigFormat.Ini)]
        };

        HttpResponseMessage post = await client.PostAsync("/build-recipes", Body(BuildRecipeSerializer.Serialize(recipe)), ct);
        post.StatusCode.Should().Be(HttpStatusCode.OK);

        string listed = await client.GetStringAsync("/build-recipes", ct);
        listed.Should().Contain("config-only");
    }

    [Fact]
    public async Task Server_instance_round_trips_and_stamps_created_at()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        HttpClient client = _factory.CreateClient();

        // An instance references an existing node (FK), so seed one.
        AgentNodeRepository nodes = _factory.Services.GetRequiredService<AgentNodeRepository>();
        await nodes.EnsureAgentAsync("a1", "name-n1", "fpr", 1, ct);
        await nodes.EnsureNodeAsync("n1", "a1", "guest", "managed", 1, ct);

        string instanceJson =
            "{\"id\":\"srv-valheim\",\"nodeId\":\"n1\",\"descriptorId\":\"valheim\",\"descriptorVersion\":1," +
            "\"instanceParamsJson\":\"{\\\"name\\\":\\\"world\\\"}\"}";

        HttpResponseMessage post = await client.PostAsync("/server-instances", Body(instanceJson), ct);
        post.StatusCode.Should().Be(HttpStatusCode.OK);

        string listed = await client.GetStringAsync("/server-instances", ct);
        listed.Should().Contain("srv-valheim").And.Contain("\"nodeId\":\"n1\"");
    }

    [Fact]
    public async Task Update_policy_round_trips_through_store_and_list()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        HttpClient client = _factory.CreateClient();
        string policyJson =
            "{\"id\":\"host-apt\",\"version\":1,\"what\":{\"provider\":\"apt\"},\"how\":\"in-place\"," +
            "\"when\":{\"mode\":\"manual\"},\"reboot\":\"if-required\",\"preflight\":[\"notify\"],\"approval\":\"auto\"}";

        HttpResponseMessage post = await client.PostAsync("/update-policies", Body(policyJson), ct);
        post.StatusCode.Should().Be(HttpStatusCode.OK);

        string listed = await client.GetStringAsync("/update-policies", ct);
        listed.Should().Contain("host-apt");
    }

    [Fact]
    public async Task Server_instance_with_a_bad_body_is_a_bad_request()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage post = await client.PostAsync("/server-instances", Body("{ not json"), ct);

        post.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");
}
