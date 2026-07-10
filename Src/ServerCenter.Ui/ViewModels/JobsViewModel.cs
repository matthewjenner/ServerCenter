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

    [ObservableProperty] private string _updateAgentId = string.Empty;
    [ObservableProperty] private string _updatePolicyId = string.Empty;
    [ObservableProperty] private string _updateServiceUnit = string.Empty;
    [ObservableProperty] private string _updateStatus = string.Empty;

    [ObservableProperty] private string _vmNodeId = string.Empty;
    [ObservableProperty] private string _vmStatus = string.Empty;

    public ObservableCollection<JobRowViewModel> Jobs { get; } = [];

    public void Apply(JobListSnapshot snapshot)
    {
        HashSet<string> seen = new HashSet<string>(snapshot.Jobs.Count);
        for (int i = 0; i < snapshot.Jobs.Count; i++)
        {
            JobInfo job = snapshot.Jobs[i];
            seen.Add(job.JobId);
            JobRowViewModel? existing = Jobs.FirstOrDefault(j => j.JobId == job.JobId);
            if (existing is null)
            {
                Jobs.Insert(Math.Min(i, Jobs.Count), new JobRowViewModel(job)); // newest-first
            }
            else
            {
                existing.Update(job);
            }
        }

        for (int i = Jobs.Count - 1; i >= 0; i--)
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
            string jobId = await client.RestartServiceAsync(RestartAgentId.Trim(), RestartUnit.Trim(), CancellationToken.None);
            TriggerStatus = $"dispatched {(jobId.Length > 8 ? jobId[..8] : jobId)}";
        }
        catch (Exception ex)
        {
            TriggerStatus = $"error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task TriggerUpdateAsync()
    {
        if (string.IsNullOrWhiteSpace(UpdateAgentId) || string.IsNullOrWhiteSpace(UpdatePolicyId))
        {
            UpdateStatus = "enter an agent id and a policy id";
            return;
        }

        try
        {
            UpdateTriggerResult result = await client.TriggerUpdateAsync(
                UpdateAgentId.Trim(),
                UpdatePolicyId.Trim(),
                string.IsNullOrWhiteSpace(UpdateServiceUnit) ? null : UpdateServiceUnit.Trim(),
                CancellationToken.None);

            // Only "Dispatched" makes a job; the others (not-eligible / needs-confirmation / not-found)
            // carry a reason the operator should see.
            UpdateStatus = result is { Outcome: "Dispatched", JobId: { } jobId }
                ? $"dispatched {(jobId.Length > 8 ? jobId[..8] : jobId)}"
                : $"{result.Outcome}: {result.Reason}";
        }
        catch (Exception ex)
        {
            UpdateStatus = $"error: {ex.Message}";
        }
    }

    // VM lifecycle is controller-driven and keyed by NODE (the controller resolves the domain).
    // The action ("start"/"stop"/"restart") comes from the button's CommandParameter.
    [RelayCommand]
    private async Task VmActionAsync(string? action)
    {
        if (string.IsNullOrWhiteSpace(VmNodeId) || string.IsNullOrWhiteSpace(action))
        {
            VmStatus = "enter a node id";
            return;
        }

        try
        {
            UpdateTriggerResult result = await client.TriggerVmActionAsync(VmNodeId.Trim(), action, CancellationToken.None);
            VmStatus = result is { Outcome: "Dispatched", JobId: { } jobId }
                ? $"{action} dispatched {(jobId.Length > 8 ? jobId[..8] : jobId)}"
                : $"{result.Outcome}: {result.Reason}";
        }
        catch (Exception ex)
        {
            VmStatus = $"error: {ex.Message}";
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await foreach (JobListSnapshot snapshot in client.Watch(ct))
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
