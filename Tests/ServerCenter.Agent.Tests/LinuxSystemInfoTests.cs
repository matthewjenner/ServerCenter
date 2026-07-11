using AwesomeAssertions;
using ServerCenter.Agent.Linux;
using Xunit;

namespace ServerCenter.Agent.Tests;

// The /proc parsers are pure, so they run on any OS (CI includes Windows).
public sealed class LinuxSystemInfoTests
{
    [Fact]
    public void Parses_cpu_totals_from_proc_stat()
    {
        string procStat = "cpu  100 0 100 700 0 0 0 0 0 0\ncpu0 50 0 50 350 0 0 0 0 0 0\n";

        (long idle, long total) = LinuxSystemInfo.ParseCpuTotals(procStat);

        idle.Should().Be(700);
        total.Should().Be(900); // 100 + 0 + 100 + 700
    }

    [Fact]
    public void Cpu_pct_is_the_busy_fraction_of_the_delta()
    {
        // total delta 100, idle delta 50 -> 50% busy.
        LinuxSystemInfo.CpuPct(700, 1000, 750, 1100).Should().Be(50);
    }

    [Fact]
    public void Cpu_pct_is_zero_when_no_time_elapsed()
    {
        LinuxSystemInfo.CpuPct(700, 1000, 700, 1000).Should().Be(0);
    }

    [Fact]
    public void Parses_mem_used_pct_from_meminfo()
    {
        string meminfo = "MemTotal:        1000 kB\nMemFree:          100 kB\nMemAvailable:     250 kB\n";

        // used = (1000 - 250) / 1000 = 75%.
        LinuxSystemInfo.ParseMemUsedPct(meminfo).Should().Be(75);
    }

    [Fact]
    public void Parses_uptime_seconds()
    {
        LinuxSystemInfo.ParseUptimeSecs("12345.67 6789.01").Should().Be(12345);
    }

    [Fact]
    public void Parses_service_unit_names_from_list_units()
    {
        string output =
            "  nginx.service          loaded active running A high performance web server\n" +
            "  plexmediaserver.service loaded active running Plex Media Server\n" +
            "  dbus.socket            loaded active running D-Bus System Message Bus Socket\n" +   // not a .service
            "  nginx.service          loaded active running duplicate line\n";                    // dedup

        IReadOnlyList<string> services = LinuxSystemInfo.ParseServiceUnits(output);

        services.Should().Equal("nginx.service", "plexmediaserver.service");   // sorted, unique, .service only
    }

    [Fact]
    public void Parsers_are_defensive_against_empty_input()
    {
        LinuxSystemInfo.ParseCpuTotals(string.Empty).Should().Be((0L, 0L));
        LinuxSystemInfo.ParseMemUsedPct(string.Empty).Should().Be(0);
        LinuxSystemInfo.ParseUptimeSecs(string.Empty).Should().Be(0);
    }
}
