using System.Runtime.CompilerServices;
using AwesomeAssertions;
using ServerCenter.Contracts.V1;
using ServerCenter.Ui.Services;
using ServerCenter.Ui.ViewModels;
using Xunit;

namespace ServerCenter.Ui.Tests;

// The Fleet tab's snapshot reconciliation (add / update / remove cards) and the shared policy load.
// Per-card actions are covered in NodeRowViewModelTests. Apply() is UI-thread-agnostic (testable).
public sealed class DashboardViewModelTests
{
    [Fact]
    public void Apply_adds_a_card_per_node_with_dual_truth_text()
    {
        DashboardViewModel vm = new DashboardViewModel();

        vm.Apply(Snapshot(
            Node("n1", "host-1", "host", AgentLiveness.Online),
            Node("n2", "guest-1", "guest", AgentLiveness.Offline)));

        vm.Nodes.Should().HaveCount(2);
        NodeRowViewModel host = vm.Nodes.Single(n => n.NodeId == "n1");
        host.DisplayName.Should().Be("host-1");
        host.Kind.Should().Be("host");
        host.AgentLivenessText.Should().Be("Online");
        host.VmStateText.Should().Be("Unknown");   // dual-truth: no libvirt yet
        host.IsGuest.Should().BeFalse();            // host: no VM controls
    }

    [Fact]
    public void Apply_updates_existing_cards_and_removes_ones_no_longer_present()
    {
        DashboardViewModel vm = new DashboardViewModel();
        vm.Apply(Snapshot(
            Node("n1", "a", "guest", AgentLiveness.Online),
            Node("n2", "b", "guest", AgentLiveness.Online)));

        vm.Apply(Snapshot(
            Node("n1", "a-renamed", "guest", AgentLiveness.Stale),
            Node("n3", "c", "guest", AgentLiveness.Online)));

        vm.Nodes.Select(n => n.NodeId).Should().BeEquivalentTo(["n1", "n3"]);
        NodeRowViewModel n1 = vm.Nodes.Single(n => n.NodeId == "n1");
        n1.DisplayName.Should().Be("a-renamed");
        n1.AgentLivenessText.Should().Be("Stale");
    }

    [Fact]
    public void UseClients_loads_the_shared_policy_list()
    {
        NoopAdminClient admin = new NoopAdminClient { Policies = ["apt", "plex"] };
        DashboardViewModel vm = new DashboardViewModel();

        vm.UseClients(new NoopJobClient(), admin);   // fires the (synchronous, faked) policy load

        vm.Policies.Should().BeEquivalentTo(["apt", "plex"]);
    }

    private static FleetSnapshot Snapshot(params NodeState[] nodes)
    {
        FleetSnapshot snapshot = new FleetSnapshot { GeneratedUnixMs = 1000 };
        snapshot.Nodes.AddRange(nodes);
        return snapshot;
    }

    private static NodeState Node(string id, string name, string kind, AgentLiveness liveness) => new()
    {
        NodeId = id,
        DisplayName = name,
        Kind = kind,
        AgentLiveness = liveness,
        VmState = VmState.Unknown
    };

    private sealed class NoopJobClient : IJobClient
    {
        public async IAsyncEnumerable<JobListSnapshot> Watch([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<string> RestartServiceAsync(string agentId, string unit, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<UpdateTriggerResult> TriggerUpdateAsync(string agentId, string policyId, string? serviceUnit, CancellationToken ct) =>
            Task.FromResult(new UpdateTriggerResult("Dispatched", string.Empty, string.Empty));

        public Task<UpdateTriggerResult> TriggerVmActionAsync(string nodeId, string action, CancellationToken ct) =>
            Task.FromResult(new UpdateTriggerResult("Dispatched", string.Empty, string.Empty));
    }

    private sealed class NoopAdminClient : IAdminClient
    {
        public IReadOnlyList<string> Policies { get; set; } = [];

        public Task<string> LinkDomainAsync(string nodeId, string domain, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<string> StoreAsync(string surface, string bodyJson, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<string> ServerJobAsync(string kind, string agentId, string instanceId, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<IReadOnlyList<ServerInstanceRow>> ListServerInstancesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ServerInstanceRow>>([]);

        public Task<IReadOnlyList<string>> ListServicesAsync(string nodeId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> ListLibvirtDomainsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> ListPolicyIdsAsync(CancellationToken ct) => Task.FromResult(Policies);
    }
}
