using ServerCenter.Core.Jobs;

namespace ServerCenter.Agent.Linux;

// Onboarding step (brief Phase 4 DoD): the controller owns the update plane, so a managed node must
// not silently update itself out from under it. `systemctl mask --now` stops and masks the two apt
// timers and the unattended-upgrades service in one convergent action - masking is idempotent, so
// re-running is a no-op (ensure-*, not install-once). This is the neuter; enabling controller-driven
// updates is the UpdatePolicy path.
public sealed class UnattendedUpgradesNeutralizer(IProcessRunner runner)
{
    // The units that let Ubuntu update itself autonomously.
    private static readonly string[] Units =
    [
        "apt-daily.timer",
        "apt-daily-upgrade.timer",
        "unattended-upgrades.service"
    ];

    public async Task EnsureDisabledAsync(IJobSink sink, CancellationToken ct)
    {
        foreach (var unit in Units)
        {
            sink.Log(LogStream.Note, $"masking {unit}");
            var result = await runner.RunAsync("systemctl", ["mask", "--now", unit], ct);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"systemctl mask --now {unit} failed (exit {result.ExitCode}): {result.StandardError}");
            }
        }
    }
}
