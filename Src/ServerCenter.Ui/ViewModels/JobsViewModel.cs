using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerCenter.Contracts.V1;
using ServerCenter.Ui.Services;

namespace ServerCenter.Ui.ViewModels;

// The jobs panel: live recent-jobs list plus a trigger for a service restart. Apply() is pure and
// UI-thread-agnostic (testable); RunAsync() marshals onto the UI thread.
public sealed partial class JobsViewModel(IJobClient client) : ObservableObject
{
    [ObservableProperty] private string _restartAgentId = string.Empty;
    [ObservableProperty] private string _restartUnit = string.Empty;
    [ObservableProperty] private string _triggerStatus = string.Empty;

    public ObservableCollection<JobRowViewModel> Jobs { get; } = [];

    public void Apply(JobListSnapshot snapshot)
    {
        var seen = new HashSet<string>(snapshot.Jobs.Count);
        for (var i = 0; i < snapshot.Jobs.Count; i++)
        {
            var job = snapshot.Jobs[i];
            seen.Add(job.JobId);
            var existing = Jobs.FirstOrDefault(j => j.JobId == job.JobId);
            if (existing is null)
            {
                Jobs.Insert(Math.Min(i, Jobs.Count), new JobRowViewModel(job)); // newest-first
            }
            else
            {
                existing.Update(job);
            }
        }

        for (var i = Jobs.Count - 1; i >= 0; i--)
        {
            if (!seen.Contains(Jobs[i].JobId))
            {
                Jobs.RemoveAt(i);
            }
        }
    }

    [RelayCommand]
    private async Task RestartAsync()
    {
        if (string.IsNullOrWhiteSpace(RestartAgentId) || string.IsNullOrWhiteSpace(RestartUnit))
        {
            TriggerStatus = "enter an agent id and a unit";
            return;
        }

        try
        {
            var jobId = await client.RestartServiceAsync(RestartAgentId.Trim(), RestartUnit.Trim(), CancellationToken.None);
            TriggerStatus = $"dispatched {(jobId.Length > 8 ? jobId[..8] : jobId)}";
        }
        catch (Exception ex)
        {
            TriggerStatus = $"error: {ex.Message}";
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await foreach (var snapshot in client.Watch(ct))
                {
                    await Dispatcher.UIThread.InvokeAsync(() => Apply(snapshot));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // controller unreachable; retry
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
