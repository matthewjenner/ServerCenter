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
            RecordingJobClient jobClient = new RecordingJobClient();

            DashboardViewModel fleet = new DashboardViewModel();
            fleet.Apply(FleetWith("stale-node"));   // a row left over from a previous controller
            JobsViewModel jobs = new JobsViewModel(new RecordingJobClient());

            MainWindowViewModel vm = new MainWindowViewModel(fleet, jobs, Factory, new ConnectionSettings(settingsPath))
            {
                ControllerAddress = "  http://host:5080 "
            };

            vm.ConnectCommand.Execute(null);

            addresses.Should().Equal("http://host:5080");           // called once, trimmed
            fleet.Nodes.Should().BeEmpty();                         // stale rows cleared
            File.ReadAllText(settingsPath).Should().Contain("http://host:5080");   // persisted

            vm.Dispose();

            (IFleetClient Fleet, IJobClient Jobs) Factory(string address)
            {
                addresses.Add(address);
                return (new NoopFleetClient(), jobClient);
            }
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }

    [Fact]
    public async Task Connect_swaps_the_job_client_so_commands_hit_the_new_controller()
    {
        string settingsPath = Path.Combine(Path.GetTempPath(), $"sc-ui-{Guid.NewGuid():N}.json");
        try
        {
            RecordingJobClient newClient = new RecordingJobClient();

            DashboardViewModel fleet = new DashboardViewModel();
            JobsViewModel jobs = new JobsViewModel(new RecordingJobClient());   // initial client, to be replaced

            MainWindowViewModel vm = new MainWindowViewModel(
                fleet, jobs, _ => (new NoopFleetClient(), newClient), new ConnectionSettings(settingsPath))
            {
                ControllerAddress = "http://host:5080"
            };

            vm.ConnectCommand.Execute(null);

            vm.Jobs.VmNodeId = "n1";
            await vm.Jobs.VmActionCommand.ExecuteAsync("start");

            newClient.LastVmAction.Should().Be(("n1", "start"));   // routed to the swapped-in client
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
        JobsViewModel jobs = new JobsViewModel(new RecordingJobClient());

        MainWindowViewModel vm = new MainWindowViewModel(
            fleet, jobs, Factory, new ConnectionSettings(Path.Combine(Path.GetTempPath(), $"sc-ui-{Guid.NewGuid():N}.json")))
        {
            ControllerAddress = "   "
        };

        vm.ConnectCommand.Execute(null);

        addresses.Should().BeEmpty();
        fleet.ConnectionStatus.Should().Contain("Enter a controller address");
        vm.Dispose();

        (IFleetClient Fleet, IJobClient Jobs) Factory(string address)
        {
            addresses.Add(address);
            return (new NoopFleetClient(), new RecordingJobClient());
        }
    }

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
}
