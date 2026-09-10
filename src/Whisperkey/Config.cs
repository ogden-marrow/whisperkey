using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;

namespace Whisperkey;

sealed class Settings {
    public string Hotkey { get; set; } = "Alt+D";
    public string StopKey { get; set; } = "Enter";
    public string CancelKey { get; set; } = "Escape";
    public bool RequireTextField { get; set; } = true;
    public string Model { get; set; } = "parakeet-0.6b-v3";
    public string Language { get; set; } = "en";
    public string Device { get; set; } = "default";
    public string InsertionMode { get; set; } = "auto";   // auto | sendinput | clipboard
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Settings))]
partial class SettingsContext : JsonSerializerContext { }

/// One JSON file, hot-reloaded on save. No GUI, by design.
static class Config {
    // The default encoder escapes '+' as +, which looks broken in a file the
    // user is expected to hand-edit. This is a local file, so relaxed escaping is safe.
    static readonly SettingsContext Ctx = new(new JsonSerializerOptions {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,   // hand-edited files should not fail on case
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    });

    static FileSystemWatcher _watcher;
    static Timer _debounce;

    public static Settings Current { get; private set; } = new();
    public static event Action Changed;

    /// Raised when the file on disk is unusable, so the caller can tray-notify
    /// instead of failing silently.
    public static event Action<string> Invalid;

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Whisperkey");
    public static string FilePath => Path.Combine(Dir, "config.json");

    public static string EnsureFile() {
        Directory.CreateDirectory(Dir);
        if (!File.Exists(FilePath)) Save(new Settings());
        return FilePath;
    }

    public static void Load() {
        EnsureFile();
        Current = Read() ?? new Settings();
        Watch();
    }

    static Settings Read() {
        try {
            var json = ReadShared(FilePath);
            var s = JsonSerializer.Deserialize(json, Ctx.Settings);
            if (s is null) { Invalid?.Invoke("config.json is empty; using defaults."); return null; }
            return s;
        } catch (JsonException e) {
            Invalid?.Invoke($"config.json is not valid JSON ({e.Message.Split('.')[0]}); using the previous settings.");
            return null;
        } catch (Exception e) {
            Invalid?.Invoke($"Could not read config.json: {e.Message}");
            return null;
        }
    }

    /// The editor that just wrote the file may still hold it open.
    static string ReadShared(string path) {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    static void Save(Settings s) {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(s, Ctx.Settings)); }
        catch { }
    }

    static void Watch() {
        if (_watcher != null) return;
        try {
            _watcher = new FileSystemWatcher(Dir, "config.json") {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            // Editors write in bursts (truncate, write, rename). Debounce so we
            // reload once, and only after the writer has let go.
            FileSystemEventHandler onChange = (_, _) => {
                _debounce?.Dispose();
                _debounce = new Timer(_ => Reload(), null, 250, Timeout.Infinite);
            };
            _watcher.Changed += onChange;
            _watcher.Created += onChange;
            _watcher.Renamed += (_, _) => onChange(null, null);
        } catch { /* hot reload is a convenience, never a hard requirement */ }
    }

    static void Reload() {
        var s = Read();
        if (s is null) return;          // keep the last good settings
        Current = s;
        Changed?.Invoke();
    }
}
