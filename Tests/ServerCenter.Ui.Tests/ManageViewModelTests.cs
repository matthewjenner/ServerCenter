using AwesomeAssertions;
using ServerCenter.Ui.Services;
using ServerCenter.Ui.ViewModels;
using Xunit;

namespace ServerCenter.Ui.Tests;

public sealed class ManageViewModelTests
{
    [Fact]
    public async Task Link_domain_dispatches_trimmed_inputs()
    {
        RecordingAdminClient client = new RecordingAdminClient();
        ManageViewModel vm = new ManageViewModel(client) { LinkNodeId = " n1 ", LinkDomain = " plex-vm " };

        await vm.LinkDomainCommand.ExecuteAsync(null);

        client.LastLink.Should().Be(("n1", "plex-vm"));
        vm.LinkStatus.Should().Contain("linked");
    }

    [Fact]
    public async Task Link_domain_requires_both_fields()
    {
        RecordingAdminClient client = new RecordingAdminClient();
        ManageViewModel vm = new ManageViewModel(client) { LinkNodeId = "n1", LinkDomain = "" };

        await vm.LinkDomainCommand.ExecuteAsync(null);

        client.LastLink.Should().BeNull();
        vm.LinkStatus.Should().Contain("node id and a domain");
    }

    [Fact]
    public async Task Store_posts_the_body_to_the_chosen_surface()
    {
        RecordingAdminClient client = new RecordingAdminClient { Response = "{\"id\":\"cs2\"}" };
        ManageViewModel vm = new ManageViewModel(client) { DefinitionJson = "{\"id\":\"cs2\",\"version\":1}" };

        await vm.StoreCommand.ExecuteAsync("game-descriptors");

        client.LastStore.Should().Be(("game-descriptors", "{\"id\":\"cs2\",\"version\":1}"));
        vm.DefinitionStatus.Should().Contain("stored game-descriptors");
    }

    [Fact]
    public async Task Store_requires_a_body()
    {
        RecordingAdminClient client = new RecordingAdminClient();
        ManageViewModel vm = new ManageViewModel(client) { DefinitionJson = "" };

        await vm.StoreCommand.ExecuteAsync("build-recipes");

        client.LastStore.Should().BeNull();
        vm.DefinitionStatus.Should().Contain("paste a definition");
    }

    [Fact]
    public async Task Server_job_dispatches_with_the_kind_agent_and_instance()
    {
        RecordingAdminClient client = new RecordingAdminClient { Response = "{\"jobId\":\"abc\"}" };
        ManageViewModel vm = new ManageViewModel(client) { ServerAgentId = " a1 ", ServerInstanceId = " srv-cs2 " };

        await vm.ServerJobCommand.ExecuteAsync("server-install");

        client.LastServerJob.Should().Be(("server-install", "a1", "srv-cs2"));
        vm.ServerStatus.Should().Contain("server-install");
    }

    [Fact]
    public async Task Server_job_requires_agent_and_instance()
    {
        RecordingAdminClient client = new RecordingAdminClient();
        ManageViewModel vm = new ManageViewModel(client) { ServerAgentId = "a1", ServerInstanceId = "" };

        await vm.ServerJobCommand.ExecuteAsync("recipe-apply");

        client.LastServerJob.Should().BeNull();
        vm.ServerStatus.Should().Contain("agent id and an instance id");
    }

    [Fact]
    public async Task An_error_from_the_controller_is_surfaced()
    {
        RecordingAdminClient client = new RecordingAdminClient { Throw = true };
        ManageViewModel vm = new ManageViewModel(client) { LinkNodeId = "n1", LinkDomain = "vm" };

        await vm.LinkDomainCommand.ExecuteAsync(null);

        vm.LinkStatus.Should().StartWith("error:");
    }

    private sealed class RecordingAdminClient : IAdminClient
    {
        public (string Node, string Domain)? LastLink { get; private set; }
        public (string Surface, string Body)? LastStore { get; private set; }
        public (string Kind, string Agent, string Instance)? LastServerJob { get; private set; }
        public string Response { get; set; } = string.Empty;
        public bool Throw { get; set; }

        public Task<string> LinkDomainAsync(string nodeId, string domain, CancellationToken ct)
        {
            if (Throw)
            {
                throw new HttpRequestException("boom");
            }

            LastLink = (nodeId, domain);
            return Task.FromResult(Response);
        }

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

        public Task<IReadOnlyList<ServerInstanceRow>> ListServerInstancesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ServerInstanceRow>>([]);
    }
}
