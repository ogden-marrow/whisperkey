namespace Whisperkey;

/// A parsed key combination. Modifiers are order-insensitive: "Alt+Shift+D" and
/// "Shift+Alt+D" are the same chord.
readonly record struct Hotkey(int Vk, bool Ctrl, bool Alt, bool Shift, bool Win) {
    public static readonly Hotkey None = new(0, false, false, false, false);
    public bool IsValid => Vk != 0;

    public static bool TryParse(string s, out Hotkey hk) {
        hk = None;
        if (string.IsNullOrWhiteSpace(s)) return false;
        bool ctrl = false, alt = false, shift = false, win = false;
        int vk = 0;

        foreach (var raw in s.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            switch (raw.ToLowerInvariant()) {
                case "ctrl" or "control": ctrl = true; continue;
                case "alt": alt = true; continue;
                case "shift": shift = true; continue;
                case "win" or "windows" or "meta": win = true; continue;
            }
            if (vk != 0) return false;          // two non-modifier keys is not a chord
            vk = KeyToVk(raw);
            if (vk == 0) return false;
        }

        if (vk == 0) return false;
        hk = new Hotkey(vk, ctrl, alt, shift, win);
        return true;
    }

    static int KeyToVk(string k) {
        if (k.Length == 1) {
            char c = char.ToUpperInvariant(k[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }
        return k.ToLowerInvariant() switch {
            "enter" or "return" => 0x0D,
            "escape" or "esc"   => 0x1B,
            "space"             => 0x20,
            "tab"               => 0x09,
            "backspace"         => 0x08,
            "insert"            => 0x2D,
            "delete" or "del"   => 0x2E,
            "home"              => 0x24,
            "end"               => 0x23,
            "pageup"            => 0x21,
            "pagedown"          => 0x22,
            "up"                => 0x26,
            "down"              => 0x28,
            "left"              => 0x25,
            "right"             => 0x27,
            "`" or "backtick" or "grave" => 0xC0,
            var f when f.StartsWith('f') && int.TryParse(f[1..], out int n) && n is >= 1 and <= 24 => 0x6F + n,
            _ => 0,
        };
    }

    public override string ToString() {
        var parts = new List<string>(4);
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(VkName(Vk));
        return string.Join("+", parts);
    }

    static string VkName(int vk) => vk switch {
        0x0D => "Enter", 0x1B => "Escape", 0x20 => "Space", 0x09 => "Tab",
        >= 'A' and <= 'Z' or >= '0' and <= '9' => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
        _ => "0x" + vk.ToString("X2"),
    };
}
