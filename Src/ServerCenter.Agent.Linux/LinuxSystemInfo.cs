using System.Globalization;
using System.Runtime.InteropServices;
using ServerCenter.Core.Platform;

namespace ServerCenter.Agent.Linux;

// Real host/guest facts + resource sampling on Linux, read from /proc and the root filesystem. The
// parsers are pure static methods so they can be unit-tested cross-platform (CI runs on Windows too);
// only SampleAsync/GetFactsAsync/RebootPendingAsync touch the actual filesystem.
public sealed class LinuxSystemInfo : ISystemInfo
{
    // Ubuntu (and apt's update-notifier) drops this file when a package upgrade needs a reboot.
    private const string RebootRequiredPath = "/var/run/reboot-required";

    public async Task<SystemFacts> GetFactsAsync(CancellationToken ct)
    {
        long uptime = ParseUptimeSecs(await ReadFileOrEmptyAsync("/proc/uptime", ct));
        string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        return new SystemFacts("linux", RuntimeInformation.OSDescription, arch, ReadKernel(), uptime);
    }

    public async Task<ResourceSample> SampleAsync(CancellationToken ct)
    {
        // CPU needs two /proc/stat reads over an interval; take a short delta so the sample is
        // self-contained (no cross-call state).
        (long idle1, long total1) = ParseCpuTotals(await ReadFileOrEmptyAsync("/proc/stat", ct));
        await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        (long idle2, long total2) = ParseCpuTotals(await ReadFileOrEmptyAsync("/proc/stat", ct));
        double cpuPct = CpuPct(idle1, total1, idle2, total2);

        double memPct = ParseMemUsedPct(await ReadFileOrEmptyAsync("/proc/meminfo", ct));
        double diskPct = RootDiskUsedPct();

        return new ResourceSample(cpuPct, memPct, diskPct);
    }

    public Task<bool> RebootPendingAsync(CancellationToken ct) => Task.FromResult(File.Exists(RebootRequiredPath));

    // --- pure parsers (unit-tested) ---

    // First token of the first "cpu " line is idle=field4; total=sum of all fields.
    public static (long Idle, long Total) ParseCpuTotals(string procStat)
    {
        foreach (string line in procStat.Split('\n'))
        {
            if (!line.StartsWith("cpu ", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            long total = 0;
            long idle = 0;
            for (int i = 1; i < parts.Length; i++)
            {
                if (!long.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                {
                    continue;
                }

                total += value;
                if (i == 4) // idle
                {
                    idle = value;
                }
            }

            return (idle, total);
        }

        return (0, 0);
    }

    public static double CpuPct(long idle1, long total1, long idle2, long total2)
    {
        long totalDelta = total2 - total1;
        long idleDelta = idle2 - idle1;
        if (totalDelta <= 0)
        {
            return 0;
        }

        double busy = 100.0 * (totalDelta - idleDelta) / totalDelta;
        return Math.Clamp(busy, 0, 100);
    }

    // used% from MemTotal and MemAvailable (the modern, accurate "used" measure).
    public static double ParseMemUsedPct(string procMeminfo)
    {
        long total = 0;
        long available = 0;
        foreach (string line in procMeminfo.Split('\n'))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                total = FirstNumber(line);
            }
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                available = FirstNumber(line);
            }
        }

        if (total <= 0)
        {
            return 0;
        }

        double used = 100.0 * (total - available) / total;
        return Math.Clamp(used, 0, 100);
    }

    public static long ParseUptimeSecs(string procUptime)
    {
        string first = procUptime.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out double secs)
            ? (long)secs
            : 0;
    }

    private static long FirstNumber(string line)
    {
        foreach (string token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            {
                return value;
            }
        }

        return 0;
    }

    private static double RootDiskUsedPct()
    {
        try
        {
            DriveInfo root = new DriveInfo("/");
            if (!root.IsReady || root.TotalSize <= 0)
            {
                return 0;
            }

            double used = 100.0 * (root.TotalSize - root.TotalFreeSpace) / root.TotalSize;
            return Math.Clamp(used, 0, 100);
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static string ReadKernel()
    {
        try
        {
            return File.Exists("/proc/sys/kernel/osrelease")
                ? File.ReadAllText("/proc/sys/kernel/osrelease").Trim()
                : Environment.OSVersion.VersionString;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static async Task<string> ReadFileOrEmptyAsync(string path, CancellationToken ct)
    {
        try
        {
            return File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}
