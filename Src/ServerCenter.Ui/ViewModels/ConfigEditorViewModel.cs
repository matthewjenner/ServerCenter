using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerCenter.Ui.Services;

namespace ServerCenter.Ui.ViewModels;

// The raw config-file editor for one server instance (game-server section, slice 4b). Lists the
// instance's rendered config paths, reads one file's current on-disk bytes back (dispatch a
// server.config-read job, then poll its stdout log for the emitted contents), lets the operator edit
// it, and writes it back (server.config-write). This edits the raw file directly - a later "Config
// apply" re-renders the descriptor template over the same file, so raw edits and template params are
// two doors to one file; the modal copy says so.
public sealed partial class ConfigEditorViewModel : ObservableObject
{
    // Poll budget for reading a file back: the read job emits its one stdout line near-instantly, but a
    // busy/slow node may lag. 40 * 250ms = 10s before we give up (node likely offline).
    private const int ReadPollAttempts = 40;
    private const int ReadPollDelayMs = 250;

    private readonly IAdminClient _client;
    private readonly Func<int, CancellationToken, Task> _delay;

    public ConfigEditorViewModel(
        IAdminClient client, string instanceId, string node, Func<int, CancellationToken, Task>? delay = null)
    {
        _client = client;
        InstanceId = instanceId;
        Node = node;
        _delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));
    }

    public string InstanceId { get; }

    public string Node { get; }

    public ObservableCollection<string> Files { get; } = [];

    [ObservableProperty] private string? _selectedFile;
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isBusy;

    // True once a file's contents have loaded, so the editor + Save are meaningful (bound to IsEnabled).
    [ObservableProperty] private bool _loaded;

    // Load the instance's config file list (called when the modal opens).
    public async Task LoadFilesAsync()
    {
        try
        {
            IReadOnlyList<string> files = await _client.ListConfigFilesAsync(InstanceId, CancellationToken.None);
            Files.Clear();
            foreach (string file in files)
            {
                Files.Add(file);
            }

            Status = Files.Count == 0 ? "no config files defined for this instance" : $"{Files.Count} config file(s)";
            if (Files.Count == 1)
            {
                SelectedFile = Files[0];   // one file: open it straight away
            }
        }
        catch (Exception ex)
        {
            Status = $"error: {ex.Message}";
        }
    }

    // Picking a file loads its current on-disk contents (fire-and-forget, MVVM-style; a synchronous
    // fake client completes it inline so tests stay deterministic).
    partial void OnSelectedFileChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _ = LoadContentAsync(value);
        }
    }

    // Read one file's contents back: dispatch a read job, then poll its logs for the emitted stdout line.
    public async Task LoadContentAsync(string path)
    {
        IsBusy = true;
        Loaded = false;
        Status = $"reading {path} ...";
        try
        {
            string jobId = await _client.DispatchConfigReadAsync(InstanceId, path, CancellationToken.None);
            string? contents = await ReadBackAsync(jobId, CancellationToken.None);
            if (contents is null)
            {
                Status = "timed out reading the file (is the node online?)";
                return;
            }

            Content = contents;
            Loaded = true;
            Status = $"loaded {path}";
        }
        catch (Exception ex)
        {
            Status = $"error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task Reload() => SelectedFile is null ? Task.CompletedTask : LoadContentAsync(SelectedFile);

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrEmpty(SelectedFile))
        {
            Status = "select a file first";
            return;
        }

        IsBusy = true;
        Status = $"writing {SelectedFile} ...";
        try
        {
            string jobId = await _client.DispatchConfigWriteAsync(InstanceId, SelectedFile, Content, CancellationToken.None);
            Status = $"write dispatched ({Short(jobId)}); the raw file is updated - no 'Config apply' needed";
        }
        catch (Exception ex)
        {
            Status = $"error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Poll the read job's persisted logs for the single stdout line the read executor emits (the file
    // contents). Returns null if it never appears within the budget (node offline / job stuck).
    private async Task<string?> ReadBackAsync(string jobId, CancellationToken ct)
    {
        for (int attempt = 0; attempt < ReadPollAttempts; attempt++)
        {
            IReadOnlyList<JobLogEntry> logs = await _client.GetJobLogsAsync(jobId, ct);
            foreach (JobLogEntry entry in logs)
            {
                if (string.Equals(entry.Stream, "stdout", StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Line;
                }
            }

            await _delay(ReadPollDelayMs, ct);
        }

        return null;
    }

    private static string Short(string value) => value.Length > 12 ? value[..12] : value;
}
