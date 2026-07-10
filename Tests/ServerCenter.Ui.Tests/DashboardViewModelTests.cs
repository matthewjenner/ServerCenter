using AwesomeAssertions;
using ServerCenter.Contracts.V1;
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
}
