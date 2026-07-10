using System.Diagnostics;
using System.Runtime.CompilerServices;
using ServerCenter.Core.Primitives;

namespace ServerCenter.Primitives.Libvirt;

// The real VM-lifecycle plane: virsh over the local libvirt socket (brief 3.2/3.3 - controller-driven,
// the controller container has the socket mounted). A leaf adapter over System.Diagnostics.Process
// (like TcpRconChannel over sockets) - the testable parsing lives in VirshOutputParser; this is Tier
// 3 only (libvirt is invisible to containers). WatchEvents polls for now; a `virsh event` stream is
// the future push upgrade. connectUri lets a test/dev point at a non-default libvirt (e.g. qemu:///system).
public sealed class VirshLibvirtHost(TimeProvider clock, string virshPath = "virsh", string? connectUri = null)
    : ILibvirtHost
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<DomainInfo>> ListDomainsAsync(CancellationToken ct) =>
        VirshOutputParser.ParseDomainList(await RunAsync(ct, "list", "--all"));

    public async Task<DomainInfo?> GetDomainAsync(string nameOrUuid, CancellationToken ct)
    {
        try
        {
            return VirshOutputParser.ParseDomInfo(await RunAsync(ct, "dominfo", nameOrUuid));
        }
        catch (InvalidOperationException)
        {
            return null; // unknown domain / virsh error
        }
    }

    public Task StartAsync(string nameOrUuid, CancellationToken ct) => RunAsync(ct, "start", nameOrUuid);

    public Task ShutdownAsync(string nameOrUuid, CancellationToken ct) => RunAsync(ct, "shutdown", nameOrUuid);

    public Task RebootAsync(string nameOrUuid, CancellationToken ct) => RunAsync(ct, "reboot", nameOrUuid);

    public async IAsyncEnumerable<DomainEvent> WatchEventsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        Dictionary<string, DomainState> lastState = new Dictionary<string, DomainState>();
        while (!ct.IsCancellationRequested)
        {
            IReadOnlyList<DomainInfo> domains;
            try
            {
                domains = await ListDomainsAsync(ct);
            }
            catch (InvalidOperationException)
            {
                domains = [];
            }

            foreach (DomainInfo domain in domains)
            {
                if (!lastState.TryGetValue(domain.Name, out DomainState previous) || previous != domain.State)
                {
                    lastState[domain.Name] = domain.State;
                    yield return new DomainEvent(domain.Name, domain.State, clock.GetUtcNow().ToUnixTimeMilliseconds());
                }
            }

            try
            {
                await Task.Delay(PollInterval, clock, ct);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    private async Task<string> RunAsync(CancellationToken ct, params string[] args)
    {
        using Process process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = virshPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        if (!string.IsNullOrEmpty(connectUri))
        {
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add(connectUri);
        }

        foreach (string arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"virsh {string.Join(' ', args)} failed (exit {process.ExitCode}): {(await stderr).Trim()}");
        }

        return await stdout;
    }
}
