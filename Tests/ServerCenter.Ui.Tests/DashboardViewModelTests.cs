using System.Runtime.CompilerServices;
using AwesomeAssertions;
using ServerCenter.Contracts.V1;
using ServerCenter.Ui.Services;
using ServerCenter.Ui.ViewModels;
using Xunit;

namespace ServerCenter.Ui.Tests;

// The dashboard's snapshot reconciliation (add / update / remove). Apply() is UI-thread-agnostic
// so it is directly testable without an Avalonia runtime.
public sealed class DashboardViewModelTests
{
    [Fact]
    public void Apply_adds_a_row_per_node_with_dual_truth_text()
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
        host.VmStateText.Should().Be("Unknown"); // dual-truth: no libvirt yet
    }

    [Fact]
    public void Apply_updates_existing_rows_and_removes_ones_no_longer_present()
    {
        DashboardViewModel vm = new DashboardViewModel();
        vm.Apply(Snapshot(
            Node("n1", "a", "guest", AgentLiveness.Online),
            Node("n2", "b", "guest", AgentLiveness.Online)));

        // n1 renamed + now stale; n2 gone; n3 new.
        vm.Apply(Snapshot(
            Node("n1", "a-renamed", "guest", AgentLiveness.Stale),
            Node("n3", "c", "guest", AgentLiveness.Online)));

        vm.Nodes.Select(n => n.NodeId).Should().BeEquivalentTo(["n1", "n3"]);
        NodeRowViewModel n1 = vm.Nodes.Single(n => n.NodeId == "n1");
        n1.DisplayName.Should().Be("a-renamed");
        n1.AgentLivenessText.Should().Be("Stale");
    }

    [Fact]
    public async Task Restart_service_targets_the_selected_node_with_the_typed_unit()
    {
        RecordingJobClient jobs = new RecordingJobClient();
        DashboardViewModel vm = new DashboardViewModel();
        vm.UseClients(jobs, new NoopAdminClient());
        vm.Apply(Snapshot(Node("plex-server", "plex", "guest", AgentLiveness.Online)));
        vm.SelectedNode = vm.Nodes.Single();
        vm.RestartUnit = " plexmediaserver.service ";

        await vm.RestartServiceCommand.ExecuteAsync(null);

        jobs.LastRestart.Should().Be(("plex-server", "plexmediaserver.service"));   // node from selection, trimmed unit
        vm.ActionStatus.Should().Contain("restart dispatched");
    }

    [Fact]
    public async Task Actions_require_a_selected_node()
    {
        RecordingJobClient jobs = new RecordingJobClient();
        DashboardViewModel vm = new DashboardViewModel();
        vm.UseClients(jobs, new NoopAdminClient());   // nothing selected
        vm.RestartUnit = "x.service";

        await vm.RestartServiceCommand.ExecuteAsync(null);

        jobs.LastRestart.Should().BeNull();
        vm.ActionStatus.Should().Contain("select a node");
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

    private sealed class RecordingJobClient : IJobClient
    {
        public (string Agent, string Unit)? LastRestart { get; private set; }

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

        public Task<UpdateTriggerResult> TriggerUpdateAsync(
            string agentId, string policyId, string? serviceUnit, CancellationToken ct) =>
            Task.FromResult(new UpdateTriggerResult("Dispatched", "u1", string.Empty));

        public Task<UpdateTriggerResult> TriggerVmActionAsync(string nodeId, string action, CancellationToken ct) =>
            Task.FromResult(new UpdateTriggerResult("Dispatched", "vm1", string.Empty));
    }

    [Fact]
    public void Selecting_a_node_loads_its_services()
    {
        NoopAdminClient admin = new NoopAdminClient { Services = ["nginx.service", "plexmediaserver.service"] };
        DashboardViewModel vm = new DashboardViewModel();
        vm.UseClients(new RecordingJobClient(), admin);
        vm.Apply(Snapshot(Node("web-server", "web", "guest", AgentLiveness.Online)));

        vm.SelectedNode = vm.Nodes.Single();   // triggers the (synchronous, faked) service load

        admin.LastServicesNode.Should().Be("web-server");
        vm.Services.Should().BeEquivalentTo(["nginx.service", "plexmediaserver.service"]);
    }

    private sealed class NoopAdminClient : IAdminClient
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
    }
}
