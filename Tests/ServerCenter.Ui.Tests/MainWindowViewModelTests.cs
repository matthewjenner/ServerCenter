using System.Runtime.CompilerServices;
using AwesomeAssertions;
using ServerCenter.Contracts.V1;
using ServerCenter.Ui.Services;
using ServerCenter.Ui.ViewModels;
using Xunit;

namespace ServerCenter.Ui.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Connect_points_at_the_trimmed_address_clears_stale_rows_and_persists()
    {
        string settingsPath = Path.Combine(Path.GetTempPath(), $"sc-ui-{Guid.NewGuid():N}.json");
        try
        {
            List<string> addresses = new List<string>();

            DashboardViewModel fleet = new DashboardViewModel();
            fleet.Apply(FleetWith("stale-node"));   // a row left over from a previous controller

            MainWindowViewModel vm = new MainWindowViewModel(fleet, Jobs(), Servers(), Settings(), Factory, new ConnectionSettings(settingsPath))
            {
                ControllerAddress = "  http://host:5080 "
            };

            vm.ConnectCommand.Execute(null);

            addresses.Should().Equal("http://host:5080");           // called once, trimmed
            fleet.Nodes.Should().BeEmpty();                         // stale rows cleared
            File.ReadAllText(settingsPath).Should().Contain("http://host:5080");   // persisted

            vm.Dispose();

            (IFleetClient Fleet, IJobClient Jobs, IAdminClient Admin) Factory(string address)
            {
                addresses.Add(address);
                return (new NoopFleetClient(), new RecordingJobClient(), new RecordingAdminClient());
            }
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }

    [Fact]
    public async Task Connect_swaps_the_action_clients_so_fleet_actions_hit_the_new_controller()
    {
        string settingsPath = Path.Combine(Path.GetTempPath(), $"sc-ui-{Guid.NewGuid():N}.json");
        try
        {
            RecordingJobClient newJob = new RecordingJobClient();
            RecordingAdminClient newAdmin = new RecordingAdminClient();

            DashboardViewModel fleet = new DashboardViewModel();
            MainWindowViewModel vm = new MainWindowViewModel(
                fleet, Jobs(), Servers(), Settings(),
                _ => (new NoopFleetClient(), newJob, newAdmin), new ConnectionSettings(settingsPath))
            {
                ControllerAddress = "http://host:5080"
            };

            vm.ConnectCommand.Execute(null);   // wires Fleet.UseClients(newJob, newAdmin), clears Nodes
            fleet.Apply(FleetWith("n1"));       // a card is created with the swapped-in clients

            NodeRowViewModel card = fleet.Nodes.Single();
            await card.VmActionCommand.ExecuteAsync("start");
            newJob.LastVmAction.Should().Be(("n1", "start"));   // job client swap reached the card
            newAdmin.LastServicesNode.Should().Be("n1");        // admin client swap (card loaded its services)

            vm.Dispose();
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }

    [Fact]
    public void Connect_with_a_blank_address_warns_and_does_not_connect()
    {
        List<string> addresses = new List<string>();
        DashboardViewModel fleet = new DashboardViewModel();

        MainWindowViewModel vm = new MainWindowViewModel(
            fleet, Jobs(), Servers(), Settings(), Factory, new ConnectionSettings(Path.Combine(Path.GetTempPath(), $"sc-ui-{Guid.NewGuid():N}.json")))
        {
            ControllerAddress = "   "
        };

        vm.ConnectCommand.Execute(null);

        addresses.Should().BeEmpty();
        fleet.ConnectionStatus.Should().Contain("Enter a controller address");
        vm.Dispose();

        (IFleetClient Fleet, IJobClient Jobs, IAdminClient Admin) Factory(string address)
        {
            addresses.Add(address);
            return (new NoopFleetClient(), new RecordingJobClient(), new RecordingAdminClient());
        }
    }

    private static JobsViewModel Jobs() => new(new RecordingJobClient());

    private static ServersViewModel Servers() => new(new RecordingAdminClient());

    private static SettingsViewModel Settings() => new();

    private static FleetSnapshot FleetWith(string nodeId)
    {
        FleetSnapshot snapshot = new FleetSnapshot { GeneratedUnixMs = 1 };
        snapshot.Nodes.Add(new NodeState { NodeId = nodeId, DisplayName = nodeId });
        return snapshot;
    }

    private sealed class NoopFleetClient : IFleetClient
    {
        public async IAsyncEnumerable<FleetSnapshot> Watch([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class RecordingJobClient : IJobClient
    {
        public (string Node, string Action)? LastVmAction { get; private set; }

        public async IAsyncEnumerable<JobListSnapshot> Watch([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<string> RestartServiceAsync(string agentId, string unit, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<UpdateTriggerResult> TriggerUpdateAsync(
            string agentId, string policyId, string? serviceUnit, CancellationToken ct) =>
            Task.FromResult(new UpdateTriggerResult("Dispatched", string.Empty, string.Empty));

        public Task<UpdateTriggerResult> TriggerVmActionAsync(string nodeId, string action, CancellationToken ct)
        {
            LastVmAction = (nodeId, action);
            return Task.FromResult(new UpdateTriggerResult("Dispatched", "vmjob", string.Empty));
        }
    }

    private sealed class RecordingAdminClient : IAdminClient
    {
        public string? LastServicesNode { get; private set; }

        public Task<string> LinkDomainAsync(string nodeId, string domain, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<string> StoreAsync(string surface, string bodyJson, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<string> ServerJobAsync(string kind, string agentId, string instanceId, CancellationToken ct) => Task.FromResult(string.Empty);

        public Task<IReadOnlyList<ServerInstanceRow>> ListServerInstancesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ServerInstanceRow>>([]);

        public Task<IReadOnlyList<string>> ListServicesAsync(string nodeId, CancellationToken ct)
        {
            LastServicesNode = nodeId;
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public Task<IReadOnlyList<string>> ListLibvirtDomainsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> ListPolicyIdsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<EnrollmentTokenResult> MintEnrollmentTokenAsync(string displayName, int ttlMinutes, CancellationToken ct) =>
            Task.FromResult(new EnrollmentTokenResult("tok", displayName, 0));

        public Task<IReadOnlyList<PolicyDoc>> ListPoliciesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PolicyDoc>>([]);
    }
}
