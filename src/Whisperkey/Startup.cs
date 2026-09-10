using Microsoft.Win32;

namespace Whisperkey;

/// "Start with Windows" via the Run key, which is where Windows itself expects a
/// user-level startup app to live (a scheduled task would be the wrong tool).
static class Startup {
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "Whisperkey";

    public static bool Enabled {
        get {
            try { using var k = Registry.CurrentUser.OpenSubKey(Key); return k?.GetValue(Name) != null; }
            catch { return false; }
        }
    }

    public static void Toggle() {
        try {
            using var k = Registry.CurrentUser.OpenSubKey(Key, writable: true);
            if (k is null) return;
            if (Enabled) k.DeleteValue(Name, throwOnMissingValue: false);
            else k.SetValue(Name, $"\"{Environment.ProcessPath}\"");
        } catch { }
    }
}
