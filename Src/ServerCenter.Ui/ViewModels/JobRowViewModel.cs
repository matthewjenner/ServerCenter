using CommunityToolkit.Mvvm.ComponentModel;
using ServerCenter.Contracts.V1;

namespace ServerCenter.Ui.ViewModels;

// One row in the jobs panel.
public sealed partial class JobRowViewModel : ObservableObject
{
    public string JobId { get; }

    [ObservableProperty] private string _shortId = string.Empty;
    [ObservableProperty] private string _nodeId = string.Empty;
    [ObservableProperty] private string _type = string.Empty;
    [ObservableProperty] private string _stateText = string.Empty;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private string _detail = string.Empty;

    public JobRowViewModel(JobInfo job)
    {
        JobId = job.JobId;
        Update(job);
    }

    public void Update(JobInfo job)
    {
        ShortId = job.JobId.Length > 8 ? job.JobId[..8] : job.JobId;
        NodeId = job.NodeId;
        Type = job.Type;
        StateText = FormatState(job.State);
        ProgressText = job.ProgressPct >= 0 ? $"{job.ProgressPct}%" : string.Empty;
        Detail = !string.IsNullOrEmpty(job.FailReason) ? job.FailReason : job.ProgressNote;
    }

    private static string FormatState(JobState state) => state switch
    {
        JobState.Queued => "Queued",
        JobState.Running => "Running",
        JobState.Succeeded => "Succeeded",
        JobState.Failed => "Failed",
        JobState.Timedout => "Timed out",
        JobState.Cancelled => "Cancelled",
        _ => "?"
    };
}
