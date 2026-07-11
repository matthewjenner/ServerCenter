using AwesomeAssertions;
using ServerCenter.Ui.Services;
using Xunit;

namespace ServerCenter.Ui.Tests;

// The settings file is one merged document: saving the address must not drop window metadata, and
// saving window metadata must not drop the address. Window restore must round-trip.
public sealed class ConnectionSettingsTests
{
    [Fact]
    public void Window_state_round_trips()
    {
        string path = TempPath();
        try
        {
            ConnectionSettings settings = new ConnectionSettings(path);
            settings.SaveWindow(new UiWindowState(1200, 800, 100, 50, Maximized: true, SelectedTab: 2));

            UiWindowState loaded = new ConnectionSettings(path).LoadWindow();

            loaded.Should().Be(new UiWindowState(1200, 800, 100, 50, true, 2));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Saving_the_address_preserves_window_state()
    {
        string path = TempPath();
        try
        {
            ConnectionSettings settings = new ConnectionSettings(path);
            settings.SaveWindow(new UiWindowState(1000, 700, 10, 20, Maximized: false, SelectedTab: 1));

            settings.Save("http://host:5080");

            settings.ResolveStartupAddress().Should().Be("http://host:5080");
            settings.LoadWindow().Should().Be(new UiWindowState(1000, 700, 10, 20, false, 1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Saving_window_state_preserves_the_address()
    {
        string path = TempPath();
        try
        {
            ConnectionSettings settings = new ConnectionSettings(path);
            settings.Save("http://host:5080");

            settings.SaveWindow(new UiWindowState(900, 600, 5, 5, Maximized: false, SelectedTab: 0));

            settings.ResolveStartupAddress().Should().Be("http://host:5080");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Defaults_when_no_file_exists()
    {
        string path = TempPath();
        UiWindowState loaded = new ConnectionSettings(path).LoadWindow();
        loaded.Should().Be(new UiWindowState(null, null, null, null, false, 0));
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"sc-settings-{Guid.NewGuid():N}.json");
}
