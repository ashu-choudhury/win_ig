using System.IO;
using System.Text.Json;
using WinInstagram.Models;

namespace WinInstagram.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/>. Writes are atomic (temp file then move) and any
/// failure is logged and swallowed, because losing a preference must never break the app.
/// </summary>
public class AppSettingsService
{
    private static AppSettingsService? _instance;
    public static AppSettingsService Instance => _instance ??= new AppSettingsService();

    private readonly string _path;
    private readonly object _lock = new();
    private AppSettings _settings;

    public AppSettings Settings => _settings;

    /// <summary>Raised after settings change, so live state (volume, verbosity) can follow.</summary>
    public event Action<AppSettings>? Changed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private AppSettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WinInstagram");
        _path = Path.Combine(dir, "settings.json");
        _settings = Load();
    }

    public string SettingsPath => _path;

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        loaded.Shortcuts ??= new Dictionary<string, string>();
                        AppLogger.Info("SETTINGS", $"Loaded preferences from {_path}");
                        return loaded;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("SETTINGS", $"Could not read settings, using defaults: {ex.Message}");
        }

        return new AppSettings();
    }

    /// <summary>Applies a change, persists it, and notifies listeners.</summary>
    public void Update(Action<AppSettings> change)
    {
        if (change == null) return;

        lock (_lock)
        {
            change(_settings);
            Save();
        }

        Changed?.Invoke(_settings);
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Write to a temp file first so an interrupted write cannot corrupt preferences.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_settings, JsonOptions));

            if (File.Exists(_path)) File.Delete(_path);
            File.Move(temp, _path);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("SETTINGS", $"Could not save settings: {ex.Message}");
        }
    }
}
