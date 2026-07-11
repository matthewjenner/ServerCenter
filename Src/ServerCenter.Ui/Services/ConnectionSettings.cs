using System.Text.Json;

namespace ServerCenter.Ui.Services;

// The UI's persisted settings (one settings.json per user, under %APPDATA%\ServerCenter). Holds the
// controller address plus window metadata (size, location, maximized, selected tab) so the app comes
// back the way you left it. Every write MERGES onto the existing file, so saving one facet (address)
// never drops another (window geometry). Persistence is best-effort: a read/write failure must never
// crash the UI. Startup address precedence: SERVERCENTER_CONTROLLER env var > saved file > default.
public sealed class ConnectionSettings
{
    private const string DefaultAddress = "http://localhost:5080";

    private static readonly JsonSerializerOptions JsonOptions =
        new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private readonly string _path;

    public ConnectionSettings() : this(DefaultPath())
    {
    }

    // Explicit path overload for tests (point at a temp file instead of the real per-user location).
    public ConnectionSettings(string settingsFilePath) => _path = settingsFilePath;

    private static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ServerCenter", "settings.json");

    public string ResolveStartupAddress()
    {
        string? env = Environment.GetEnvironmentVariable("SERVERCENTER_CONTROLLER");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        string saved = ReadFile().ControllerAddress;
        return string.IsNullOrWhiteSpace(saved) ? DefaultAddress : saved.Trim();
    }

    // Persist the controller address, preserving any saved window metadata.
    public void Save(string address)
    {
        SettingsFile file = ReadFile();
        file.ControllerAddress = address;
        WriteFile(file);
    }

    // The saved window metadata (defaults - nulls / tab 0 - when nothing is stored yet).
    public UiWindowState LoadWindow()
    {
        SettingsFile file = ReadFile();
        return new UiWindowState(
            file.WindowWidth, file.WindowHeight, file.WindowX, file.WindowY, file.WindowMaximized, file.SelectedTab);
    }

    // Persist window metadata, preserving the saved controller address.
    public void SaveWindow(UiWindowState state)
    {
        SettingsFile file = ReadFile();
        file.WindowWidth = state.Width;
        file.WindowHeight = state.Height;
        file.WindowX = state.X;
        file.WindowY = state.Y;
        file.WindowMaximized = state.Maximized;
        file.SelectedTab = state.SelectedTab;
        WriteFile(file);
    }

    private SettingsFile ReadFile()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(_path), JsonOptions) ?? new SettingsFile();
            }
        }
        catch
        {
            // A corrupt/locked settings file must not block startup - fall back to defaults.
        }

        return new SettingsFile();
    }

    private void WriteFile(SettingsFile file)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(file, JsonOptions));
        }
        catch
        {
            // Best-effort persistence; failing to save must not take down the UI.
        }
    }

    private sealed class SettingsFile
    {
        public string ControllerAddress { get; set; } = string.Empty;
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
        public double? WindowX { get; set; }
        public double? WindowY { get; set; }
        public bool WindowMaximized { get; set; }
        public int SelectedTab { get; set; }
    }
}

// Window metadata restored on launch and saved on close. Width/Height/X/Y are null until first saved;
// Maximized restores the maximized state (keeping the last normal size to un-maximize into).
public sealed record UiWindowState(
    double? Width, double? Height, double? X, double? Y, bool Maximized, int SelectedTab);
