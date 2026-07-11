using AwesomeAssertions;
using ServerCenter.Ui.Services;
using ServerCenter.Ui.ViewModels;
using Xunit;

namespace ServerCenter.Ui.Tests;

public sealed class ServersViewModelTests
{
    [Fact]
    public void Apply_formats_bindings_as_id_at_version_or_dash()
    {
        ServersViewModel vm = new ServersViewModel(new FakeAdminClient());

        vm.Apply(
        [
            new ServerInstanceRow("srv-cs2", "cs2-node", "cs2-dedicated", 3, null, null, "cs2-nightly", 1),
            new ServerInstanceRow("srv-plex", "plex-node", null, null, "plex-recipe", 2, null, null)
        ]);

        vm.Rows.Should().HaveCount(2);
        ServerRowViewModel cs2 = vm.Rows[0];
        cs2.Descriptor.Should().Be("cs2-dedicated@3");
        cs2.Recipe.Should().Be("-");
        cs2.Policy.Should().Be("cs2-nightly@1");

        vm.Rows[1].Recipe.Should().Be("plex-recipe@2");
        vm.Status.Should().Be("2 server(s) defined");
    }

    [Fact]
    public async Task Refresh_loads_from_the_client()
    {
        FakeAdminClient client = new FakeAdminClient
        {
            Instances = [new ServerInstanceRow("srv-1", "n1", "d", 1, null, null, null, null)]
        };
        ServersViewModel vm = new ServersViewModel(client);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Rows.Should().ContainSingle().Which.Id.Should().Be("srv-1");
    }

    [Fact]
    public async Task Server_job_uses_the_selected_server_as_the_target()
    {
        FakeAdminClient client = new FakeAdminClient { Response = "{\"jobId\":\"abc\"}" };
        ServersViewModel vm = new ServersViewModel(client)
        {
            SelectedServer = new ServerRowViewModel(new ServerInstanceRow("srv-cs2", "cs2-node", "cs2", 1, null, null, null, null))
        };

        await vm.ServerJobCommand.ExecuteAsync("server-install");

        client.LastServerJob.Should().Be(("server-install", "cs2-node", "srv-cs2"));   // agent + instance from the row
        vm.ActionStatus.Should().Contain("server-install");
    }

    [Fact]
    public async Task Server_job_requires_a_selection()
    {
        FakeAdminClient client = new FakeAdminClient();
        ServersViewModel vm = new ServersViewModel(client);   // nothing selected

        await vm.ServerJobCommand.ExecuteAsync("server-install");

        client.LastServerJob.Should().BeNull();
        vm.ActionStatus.Should().Contain("select a server");
    }

    [Fact]
    public async Task Store_posts_the_body_to_the_chosen_surface()
    {
        FakeAdminClient client = new FakeAdminClient { Response = "{\"id\":\"cs2\"}" };
        ServersViewModel vm = new ServersViewModel(client) { DefinitionJson = "{\"id\":\"cs2\",\"version\":1}" };

        await vm.StoreCommand.ExecuteAsync("game-descriptors");

        client.LastStore.Should().Be(("game-descriptors", "{\"id\":\"cs2\",\"version\":1}"));
        vm.DefinitionStatus.Should().Contain("stored game-descriptors");
    }

    [Fact]
    public async Task Store_requires_a_body()
    {
        FakeAdminClient client = new FakeAdminClient();
        ServersViewModel vm = new ServersViewModel(client) { DefinitionJson = "" };

        await vm.StoreCommand.ExecuteAsync("build-recipes");

        client.LastStore.Should().BeNull();
        vm.DefinitionStatus.Should().Contain("paste a definition");
    }

    [Fact]
    public async Task Refresh_surfaces_an_error()
    {
        ServersViewModel vm = new ServersViewModel(new FakeAdminClient { Throw = true });

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Status.Should().StartWith("error:");
        vm.Rows.Should().BeEmpty();
    }

    private sealed class FakeAdminClient : IAdminClient
    {
        public IReadOnlyList<ServerInstanceRow> Instances { get; set; } = [];
        public (string Surface, string Body)? LastStore { get; private set; }
        public (string Kind, string Agent, string Instance)? LastServerJob { get; private set; }
        public string Response { get; set; } = string.Empty;
        public bool Throw { get; set; }

        public Task<string> LinkDomainAsync(string nodeId, string domain, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<string> StoreAsync(string surface, string bodyJson, CancellationToken ct)
        {
            LastStore = (surface, bodyJson);
            return Task.FromResult(Response);
        }

        public Task<string> ServerJobAsync(string kind, string agentId, string instanceId, CancellationToken ct)
        {
            LastServerJob = (kind, agentId, instanceId);
            return Task.FromResult(Response);
        }

        public Task<IReadOnlyList<ServerInstanceRow>> ListServerInstancesAsync(CancellationToken ct)
        {
            if (Throw)
            {
                throw new HttpRequestException("boom");
            }

            return Task.FromResult(Instances);
        }
    }
}
