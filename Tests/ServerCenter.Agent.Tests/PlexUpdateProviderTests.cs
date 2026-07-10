using AwesomeAssertions;
using ServerCenter.Agent.Linux;
using ServerCenter.Core.Platform;
using ServerCenter.TestFakes;
using Xunit;

namespace ServerCenter.Agent.Tests;

// Plex "what" provider: the non-apt backend. Manifest parse, installed-version compare, arch/distro
// release selection, and the download -> dpkg apply, all against fakes (no network, no dpkg).
public sealed class PlexUpdateProviderTests
{
    private const string Manifest = """
        {
          "computer": {
            "Linux": {
              "version": "1.41.3.9314-a0bfb8370",
              "releases": [
                { "build": "linux-x86_64", "distro": "debian", "url": "https://plex.tv/pms_1.41.3_amd64.deb" },
                { "build": "linux-aarch64", "distro": "debian", "url": "https://plex.tv/pms_1.41.3_arm64.deb" }
              ]
            }
          }
        }
        """;

    private static PlexUpdateProvider Provider(
        FakeHttpFetcher http, FakeProcessRunner runner, string build = "linux-x86_64") =>
        new(http, runner, new PlexUpdateOptions { Build = build, DownloadDirectory = "/tmp" });

    private static FakeProcessRunner RunnerWithInstalled(string installedVersion) => new()
    {
        Respond = (file, _) => file == "dpkg-query"
            ? new ProcessResult(installedVersion.Length == 0 ? 1 : 0, installedVersion, string.Empty)
            : new ProcessResult(0, string.Empty, string.Empty)
    };

    [Fact]
    public async Task Check_reports_an_update_when_the_installed_version_differs()
    {
        var ct = TestContext.Current.CancellationToken;
        var http = new FakeHttpFetcher { Body = Manifest };

        var updates = await Provider(http, RunnerWithInstalled("1.41.0.1000-oldbuild")).CheckAsync(ct);

        updates.Should().ContainSingle();
        updates[0].Package.Should().Be("plexmediaserver");
        updates[0].CurrentVersion.Should().Be("1.41.0.1000-oldbuild");
        updates[0].TargetVersion.Should().Be("1.41.3.9314-a0bfb8370");
    }

    [Fact]
    public async Task Check_reports_nothing_when_already_current()
    {
        var ct = TestContext.Current.CancellationToken;
        var http = new FakeHttpFetcher { Body = Manifest };

        var updates = await Provider(http, RunnerWithInstalled("1.41.3.9314-a0bfb8370")).CheckAsync(ct);

        updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Check_reports_none_installed_as_the_current_version()
    {
        var ct = TestContext.Current.CancellationToken;
        var http = new FakeHttpFetcher { Body = Manifest };

        var updates = await Provider(http, RunnerWithInstalled(string.Empty)).CheckAsync(ct);

        updates[0].CurrentVersion.Should().Be("(none)");
    }

    [Fact]
    public async Task Apply_downloads_the_arch_matched_deb_and_installs_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var http = new FakeHttpFetcher { Body = Manifest };
        var runner = RunnerWithInstalled("1.41.0.1000-oldbuild");

        var outcome = await Provider(http, runner, build: "linux-aarch64")
            .ApplyAsync(new UpdatePlan([], AllowReboot: false), new RecordingJobSink(), ct);

        outcome.Success.Should().BeTrue();
        outcome.RebootRequired.Should().BeFalse(); // an app-channel update never reboots the host
        http.Downloads.Should().ContainSingle()
            .Which.Url.Should().Be("https://plex.tv/pms_1.41.3_arm64.deb");
        var dpkg = runner.Invocations.Single(i => i.File == "dpkg");
        dpkg.Args[0].Should().Be("-i");
        // dpkg installs exactly what was downloaded (path separator is OS-dependent - the tests run
        // on Windows, the provider on Linux - so assert the relationship, not a literal path).
        dpkg.Args[1].Should().Be(http.Downloads[0].Destination);
        Path.GetFileName(dpkg.Args[1]).Should().Be("plexmediaserver-1.41.3.9314-a0bfb8370.deb");
    }

    [Fact]
    public async Task Apply_fails_when_dpkg_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var http = new FakeHttpFetcher { Body = Manifest };
        var runner = new FakeProcessRunner
        {
            Respond = (file, _) => file == "dpkg"
                ? new ProcessResult(1, string.Empty, "dependency problems")
                : new ProcessResult(0, "1.41.0.1000-oldbuild", string.Empty)
        };

        var outcome = await Provider(http, runner)
            .ApplyAsync(new UpdatePlan([], AllowReboot: false), new RecordingJobSink(), ct);

        outcome.Success.Should().BeFalse();
        outcome.FailReason.Should().Contain("dpkg -i failed");
    }
}
