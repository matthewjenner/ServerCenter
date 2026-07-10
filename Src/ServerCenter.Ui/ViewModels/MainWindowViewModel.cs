namespace ServerCenter.Ui.ViewModels;

// Root view-model: the fleet dashboard and the jobs panel.
public sealed class MainWindowViewModel(DashboardViewModel fleet, JobsViewModel jobs)
{
    public DashboardViewModel Fleet { get; } = fleet;

    public JobsViewModel Jobs { get; } = jobs;
}
