namespace Whisperkey;

/// Off unless WHISPERKEY_TRACE is set. Diagnostics must never cost anything at rest.
static class Log {
    static readonly string Path = Environment.GetEnvironmentVariable("WHISPERKEY_TRACE");
    static readonly object Gate = new();

    public static bool On => Path is not null;

    public static void Write(string message) {
        if (Path is null) return;
        try {
            lock (Gate) File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        } catch { }
    }
}
