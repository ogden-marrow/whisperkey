using System.Runtime.InteropServices;

namespace Whisperkey;

/// Puts the transcript into whatever has focus, as if it had been typed.
///
/// A note on UI Automation: the spec called for TextPattern with a SendInput
/// fallback, but TextPattern is an inspection pattern - it can read and select
/// text, not insert it. The only UIA write path is ValuePattern.SetValue, which
/// replaces the entire contents of the field. For dictation into a half-written
/// message that is destructive, so it is deliberately not used.
static unsafe class Insert {
    const int INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;
    const int VK_CONTROL = 0x11, VK_V = 0x56;
    const uint CF_UNICODETEXT = 13;

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public int type; public INPUTUNION u; }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nint dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern int OpenClipboard(nint hwnd);
    [DllImport("user32.dll")] static extern int CloseClipboard();
    [DllImport("user32.dll")] static extern int EmptyClipboard();
    [DllImport("user32.dll")] static extern nint GetClipboardData(uint fmt);
    [DllImport("user32.dll")] static extern nint SetClipboardData(uint fmt, nint h);
    [DllImport("user32.dll")] static extern int IsClipboardFormatAvailable(uint fmt);
    [DllImport("user32.dll")] static extern int CountClipboardFormats();
    [DllImport("kernel32.dll")] static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] static extern nint GlobalLock(nint h);
    [DllImport("kernel32.dll")] static extern int GlobalUnlock(nint h);
    [DllImport("kernel32.dll")] static extern nuint GlobalSize(nint h);

    /// Above this, typing character by character becomes visibly slow and we
    /// prefer a paste - but only when the clipboard holds nothing we would lose.
    const int PasteThreshold = 120;

    public static void Text(string text, string mode) {
        if (string.IsNullOrEmpty(text)) return;

        bool wantPaste = mode.Equals("clipboard", StringComparison.OrdinalIgnoreCase)
            || (mode.Equals("auto", StringComparison.OrdinalIgnoreCase) && text.Length > PasteThreshold);

        if (wantPaste && TryPaste(text)) return;
        Type(text);
    }

    /// Unicode SendInput. Works in essentially every text surface, respects the
    /// existing caret and selection, and does not touch the clipboard.
    static void Type(string text) {
        var inputs = new INPUT[text.Length * 2];
        int n = 0;
        foreach (char c in text) {
            // '\n' as a keystroke would submit chat boxes and search fields.
            ushort ch = c == '\n' ? '\r' : c;
            inputs[n++] = Key(ch, KEYEVENTF_UNICODE);
            inputs[n++] = Key(ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
        }
        var sent = SendInput((uint)n, inputs, Marshal.SizeOf<INPUT>());
        if (sent != n) Log.Write($"SendInput sent {sent}/{n} (error {Marshal.GetLastWin32Error()})");
    }

    static INPUT Key(ushort scan, uint flags) =>
        new() { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags } } };

    /// Paste, restoring whatever text was on the clipboard afterwards.
    ///
    /// If the clipboard holds anything that is not plain text - an image, files,
    /// rich content - we refuse and let the caller type instead. Faithfully
    /// preserving arbitrary formats is not something we can promise, and quietly
    /// destroying someone's copied image to save a few milliseconds is not a
    /// trade worth making.
    static bool TryPaste(string text) {
        bool textOnly = IsClipboardFormatAvailable(CF_UNICODETEXT) != 0 && CountClipboardFormatsSafe() <= 3;
        bool empty = CountClipboardFormatsSafe() == 0;
        if (!textOnly && !empty) { Log.Write("clipboard holds non-text data; typing instead"); return false; }

        string saved = null;
        try {
            if (OpenClipboard(0) == 0) return false;
            if (IsClipboardFormatAvailable(CF_UNICODETEXT) != 0) saved = ReadUnicode();
            if (!WriteUnicode(text)) { CloseClipboard(); return false; }
            CloseClipboard();

            SendCtrlV();
            Thread.Sleep(60);   // let the target consume it before we put the old text back

            if (saved is not null && OpenClipboard(0) != 0) {
                WriteUnicode(saved);
                CloseClipboard();
            }
            return true;
        } catch (Exception e) {
            Log.Write($"paste failed: {e.Message}");
            try { CloseClipboard(); } catch { }
            return false;
        }
    }

    static int CountClipboardFormatsSafe() { try { return CountClipboardFormats(); } catch { return -1; } }

    static string ReadUnicode() {
        nint h = GetClipboardData(CF_UNICODETEXT);
        if (h == 0) return null;
        nint p = GlobalLock(h);
        if (p == 0) return null;
        try { return Marshal.PtrToStringUni(p); } finally { GlobalUnlock(h); }
    }

    static bool WriteUnicode(string s) {
        EmptyClipboard();
        nuint bytes = (nuint)((s.Length + 1) * 2);
        nint h = GlobalAlloc(0x0042 /* GMEM_MOVEABLE|GMEM_ZEROINIT */, bytes);
        if (h == 0) return false;
        nint p = GlobalLock(h);
        if (p == 0) return false;
        try {
            fixed (char* src = s) Buffer.MemoryCopy(src, (void*)p, (long)bytes, s.Length * 2L);
        } finally { GlobalUnlock(h); }
        return SetClipboardData(CF_UNICODETEXT, h) != 0;
    }

    static void SendCtrlV() {
        var inputs = new INPUT[4];
        inputs[0] = Vk(VK_CONTROL, 0);
        inputs[1] = Vk(VK_V, 0);
        inputs[2] = Vk(VK_V, KEYEVENTF_KEYUP);
        inputs[3] = Vk(VK_CONTROL, KEYEVENTF_KEYUP);
        SendInput(4, inputs, Marshal.SizeOf<INPUT>());
    }

    static INPUT Vk(int vk, uint flags) =>
        new() { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = flags } } };
}
