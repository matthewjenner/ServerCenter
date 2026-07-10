using System.Text.Json;

namespace ServerCenter.Ui.Services;

// Loads and saves the controller address the operator points the UI at, so it sticks between runs
// (no env var needed once set). Startup precedence: SERVERCENTER_CONTROLLER env var (an override for
// scripted/dev runs) > the saved settings file > a plaintext-bring-up default. Persistence is
// best-effort: a read/write failure must never crash the UI.
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

        string? saved = ReadSaved();
        return string.IsNullOrWhiteSpace(saved) ? DefaultAddress : saved.Trim();
    }

    public void Save(string address)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            SettingsFile file = new SettingsFile { ControllerAddress = address };
            File.WriteAllText(_path, JsonSerializer.Serialize(file, JsonOptions));
        }
        catch
        {
            // Best-effort persistence; failing to save the address must not take down the UI.
        }
    }

    private string? ReadSaved()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            SettingsFile? file = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(_path), JsonOptions);
            return file?.ControllerAddress;
        }
        catch
        {
            return null;
        }
    }

    private sealed class SettingsFile
    {
        public string ControllerAddress { get; set; } = string.Empty;
    }
}
