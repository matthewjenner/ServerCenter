using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using ServerCenter.Contracts.V1;
using ServerCenter.Ui.Services;
using ServerCenter.Ui.ViewModels;
using Xunit;

namespace ServerCenter.Ui.Tests;

// A node card carries its own actions (restart service / VM / update), targeting its own node.
public sealed class NodeRowViewModelTests
{
    [Fact]
    public void Constructing_a_card_loads_the_nodes_services()
    {
        FakeJobClient jobs = new FakeJobClient();
        FakeAdminClient admin = new FakeAdminClient { Services = ["nginx.service", "sshd.service"] };

        NodeRowViewModel card = Card("web-server", "guest", jobs, admin);

        admin.LastServicesNode.Should().Be("web-server");
        card.Services.Should().BeEquivalentTo(["nginx.service", "sshd.service"]);
    }

    [Fact]
    public async Task Restart_service_targets_this_node_with_the_selected_service()
    {
        FakeJobClient jobs = new FakeJobClient();
        NodeRowViewModel card = Card("plex", "guest", jobs, new FakeAdminClient());
        card.SelectedService = "plexmediaserver.service";

        await card.RestartServiceCommand.ExecuteAsync(null);

        jobs.LastRestart.Should().Be(("plex", "plexmediaserver.service"));
        card.ActionStatus.Should().Contain("restart dispatched");
    }

    [Fact]
    public async Task Restart_service_requires_a_selection()
    {
        FakeJobClient jobs = new FakeJobClient();
        NodeRowViewModel card = Card("plex", "guest", jobs, new FakeAdminClient());

        await card.RestartServiceCommand.ExecuteAsync(null);

        jobs.LastRestart.Should().BeNull();
        card.ActionStatus.Should().Contain("pick a service");
    }

    [Fact]
    public async Task Vm_action_targets_this_node()
    {
        FakeJobClient jobs = new FakeJobClient();
        NodeRowViewModel card = Card("cs2-node", "guest", jobs, new FakeAdminClient());

        await card.VmActionCommand.ExecuteAsync("restart");

        jobs.LastVmAction.Should().Be(("cs2-node", "restart"));
    }

    [Fact]
    public async Task Update_uses_the_selected_policy_and_the_service_as_the_optional_unit()
    {
        FakeJobClient jobs = new FakeJobClient();
        NodeRowViewModel card = Card("plex", "guest", jobs, new FakeAdminClient());
        card.SelectedPolicy = "plex";
        card.SelectedService = "plexmediaserver.service";

        await card.RunUpdateCommand.ExecuteAsync(null);

        jobs.LastUpdate.Should().Be(("plex", "plex", "plexmediaserver.service"));
    }

    [Fact]
    public async Task Update_requires_a_policy()
    {
        FakeJobClient jobs = new FakeJobClient();
        NodeRowViewModel card = Card("web", "guest", jobs, new FakeAdminClient());

        await card.RunUpdateCommand.ExecuteAsync(null);

        jobs.LastUpdate.Should().BeNull();
        card.ActionStatus.Should().Contain("pick a policy");
    }

    [Theory]
    [InlineData(VmState.Running, false, true)]   // on: Stop/Restart only
    [InlineData(VmState.Stopped, true, false)]   // off: Start only
    [InlineData(VmState.Unknown, false, false)]  // unknown: neither
    public void Vm_controls_are_state_aware_for_guests(VmState state, bool canStart, bool canStop)
    {
        NodeRowViewModel card = new(
            new NodeState { NodeId = "g", DisplayName = "g", Kind = "guest", VmState = state },
            1000, new FakeJobClient(), new FakeAdminClient(), []);

        card.CanStartVm.Should().Be(canStart);
        card.CanStopVm.Should().Be(canStop);
    }

    [Fact]
    public void The_host_never_shows_vm_controls()
    {
        NodeRowViewModel card = new(
            new NodeState { NodeId = "h", DisplayName = "h", Kind = "host", VmState = VmState.Running },
            1000, new FakeJobClient(), new FakeAdminClient(), []);

        card.CanStartVm.Should().BeFalse();
        card.CanStopVm.Should().BeFalse();
    }

    private static NodeRowViewModel Card(string id, string kind, IJobClient jobs, IAdminClient admin) =>
        new(new NodeState { NodeId = id, DisplayName = id, Kind = kind }, 1000, jobs, admin, []);

    private sealed class FakeJobClient : IJobClient
    {
        public (string Agent, string Unit)? LastRestart { get; private set; }
        public (string Node, string Action)? LastVmAction { get; private set; }
        public (string Agent, string Policy, string? Unit)? LastUpdate { get; private set; }

        public async IAsyncEnumerable<JobListSnapshot> Watch([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<string> RestartServiceAsync(string agentId, string unit, CancellationToken ct)
        {
            LastRestart = (agentId, unit);
            return Task.FromResult("job1234");
        }

        public Task<UpdateTriggerResult> TriggerUpdateAsync(string agentId, string policyId, string? serviceUnit, CancellationToken ct)
        {
            LastUpdate = (agentId, policyId, serviceUnit);
            return Task.FromResult(new UpdateTriggerResult("Dispatched", "u1", string.Empty));
        }

        public Task<UpdateTriggerResult> TriggerVmActionAsync(string nodeId, string action, CancellationToken ct)
        {
            LastVmAction = (nodeId, action);
            return Task.FromResult(new UpdateTriggerResult("Dispatched", "vm1", string.Empty));
        }
    }

    private sealed class FakeAdminClient : IAdminClient
    {
        public IReadOnlyList<string> Services { get; set; } = [];
        public string? LastServicesNode { get; private set; }

        public Task<string> LinkDomainAsync(string nodeId, string domain, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<string> StoreAsync(string surface, string bodyJson, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<string> ServerJobAsync(string kind, string agentId, string instanceId, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<IReadOnlyList<ServerInstanceRow>> ListServerInstancesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ServerInstanceRow>>([]);

        public Task<IReadOnlyList<string>> ListServicesAsync(string nodeId, CancellationToken ct)
        {
            LastServicesNode = nodeId;
            return Task.FromResult(Services);
        }

        public Task<IReadOnlyList<string>> ListLibvirtDomainsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> ListPolicyIdsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<EnrollmentTokenResult> MintEnrollmentTokenAsync(string displayName, int ttlMinutes, CancellationToken ct) =>
            Task.FromResult(new EnrollmentTokenResult("tok", displayName, 0));

        public Task<IReadOnlyList<PolicyDoc>> ListPoliciesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PolicyDoc>>([]);

        public Task<IReadOnlyList<GameOption>> ListGamesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GameOption>>([]);

        public Task<string> RemoveServerInstanceAsync(string instanceId, CancellationToken ct) => Task.FromResult(string.Empty);
    }
}
