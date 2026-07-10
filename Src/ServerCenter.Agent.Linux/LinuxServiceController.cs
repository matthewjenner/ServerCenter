using System.Runtime.CompilerServices;
using ServerCenter.Core.Platform;

namespace ServerCenter.Agent.Linux;

// systemd service control via systemctl. State is read with a structured property query
// (`systemctl show --property=ActiveState --value`), not by parsing the free-text status output
// (brief 3.11). WatchAsync polls for now; a DBus PropertiesChanged subscription is the preferred
// push-based upgrade once a DBus dependency is added.
public sealed class LinuxServiceController(IProcessRunner runner) : IServiceController
{
    public async Task<ServiceState> GetStateAsync(string unit, CancellationToken ct)
    {
        ProcessResult result = await runner.RunAsync("systemctl", ["show", unit, "--property=ActiveState", "--value"], ct);
        return MapState(result.StandardOutput);
    }

    public Task StartAsync(string unit, CancellationToken ct) => RunUnitVerbAsync("start", unit, ct);

    public Task StopAsync(string unit, CancellationToken ct) => RunUnitVerbAsync("stop", unit, ct);

    public Task RestartAsync(string unit, CancellationToken ct) => RunUnitVerbAsync("restart", unit, ct);

    public Task EnsureEnabledAsync(string unit, bool enabled, CancellationToken ct) =>
        RunUnitVerbAsync(enabled ? "enable" : "disable", unit, ct);

    public async Task ReloadAsync(CancellationToken ct)
    {
        ProcessResult result = await runner.RunAsync("systemctl", ["daemon-reload"], ct);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"systemctl daemon-reload failed (exit {result.ExitCode}): {result.StandardError}");
        }
    }

    public async IAsyncEnumerable<ServiceState> WatchAsync(string unit, [EnumeratorCancellation] CancellationToken ct)
    {
        ServiceState? last = null;
        while (!ct.IsCancellationRequested)
        {
            ServiceState state = await GetStateAsync(unit, ct);
            if (state != last)
            {
                last = state;
                yield return state;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    private async Task RunUnitVerbAsync(string verb, string unit, CancellationToken ct)
    {
        ProcessResult result = await runner.RunAsync("systemctl", [verb, unit], ct);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"systemctl {verb} {unit} failed (exit {result.ExitCode}): {result.StandardError}");
        }
    }

    private static ServiceState MapState(string activeState) => activeState.Trim() switch
    {
        "active" => ServiceState.Active,
        "inactive" => ServiceState.Inactive,
        "failed" => ServiceState.Failed,
        "activating" => ServiceState.Activating,
        "deactivating" => ServiceState.Deactivating,
        _ => ServiceState.Unknown
    };
}
