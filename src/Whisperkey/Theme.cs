using Microsoft.Win32;

namespace Whisperkey;

/// System theme and accent, refreshed only when Windows says they changed.
/// Never polled - see issue #8.
static class Theme {
    public static bool DarkTaskbar { get; private set; }
    public static bool HighContrast { get; private set; }
    public static bool AnimationsOn { get; private set; } = true;
    public static bool TransparencyOn { get; private set; } = true;
    public static uint Accent { get; private set; } = 0xFF0078D4;   // Windows default blue

    public static event Action Changed;

    const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static void Refresh() {
        DarkTaskbar    = ReadDword(Personalize, "SystemUsesLightTheme", 1) == 0;
        TransparencyOn = ReadDword(Personalize, "EnableTransparency", 1) != 0;
        HighContrast   = SystemInfo.HighContrast();
        AnimationsOn   = SystemInfo.AnimationsEnabled();
        Accent         = ReadAccent();
        Changed?.Invoke();
    }

    static uint ReadAccent() {
        // DWM's accent is BGR; the theme key is the authoritative source for the
        // colour Windows itself uses on the tray and start menu.
        var v = ReadDword(@"Software\Microsoft\Windows\DWM", "AccentColor", 0);
        if (v == 0) return 0xFF0078D4;
        uint b = (v >> 16) & 0xFF, g = (v >> 8) & 0xFF, r = v & 0xFF;
        return 0xFF000000u | (r << 16) | (g << 8) | b;
    }

    static uint ReadDword(string path, string name, uint fallback) {
        try {
            using var k = Registry.CurrentUser.OpenSubKey(path);
            var o = k?.GetValue(name);
            return o is int i ? unchecked((uint)i) : fallback;
        } catch { return fallback; }
    }
}
