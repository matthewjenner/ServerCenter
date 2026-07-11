using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ServerCenter.Contracts.V1;
using ServerCenter.Ui.Services;

namespace ServerCenter.Ui.ViewModels;

// The Jobs tab: the live recent-jobs list. Job TRIGGERS moved onto the tab that owns their target
// (node actions -> Fleet tab, server actions -> Servers tab); this is now read-only. Apply() is pure
// and UI-thread-agnostic (testable); RunAsync() marshals onto the UI thread. The client (used only to
// Watch) is swapped on reconnect.
public sealed partial class JobsViewModel : ObservableObject
{
    private IJobClient _client;

    public JobsViewModel(IJobClient client) => _client = client;

    public void UseClient(IJobClient client) => _client = client;

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

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await foreach (JobListSnapshot snapshot in _client.Watch(ct))
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
