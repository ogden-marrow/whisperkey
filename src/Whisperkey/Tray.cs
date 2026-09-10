using System.Reflection;
using System.Runtime.InteropServices;

namespace Whisperkey;

/// Tray icon and its native context menu. TrackPopupMenuEx gives us the real
/// Windows menu rather than an imitation of one.
sealed unsafe class Tray : IDisposable {
    public const int CmdDictate = 1, CmdPause = 2, CmdStartup = 3, CmdConfig = 4, CmdAbout = 5, CmdExit = 6;

    readonly nint _hwnd;
    N.NOTIFYICONDATA _nid;
    nint _iconIdle, _iconRec;
    bool _added;

    public bool Paused { get; set; }
    public bool Recording { get; private set; }
    public Func<bool> IsStartupEnabled = () => false;

    public Tray(nint hwnd) {
        _hwnd = hwnd;
        LoadIcons();
        _nid = new N.NOTIFYICONDATA {
            cbSize = (uint)Marshal.SizeOf<N.NOTIFYICONDATA>(),
            hWnd = hwnd, uID = 1,
            uFlags = N.NIF_MESSAGE | N.NIF_ICON | N.NIF_TIP | N.NIF_SHOWTIP,
            uCallbackMessage = N.WM_TRAY,
            hIcon = _iconIdle,
            szTip = "Whisperkey", szInfo = "", szInfoTitle = "",
        };
        _added = N.Shell_NotifyIconW(N.NIM_ADD, ref _nid) != 0;
        _nid.uVersionOrTimeout = 4; // NOTIFYICON_VERSION_4
        N.Shell_NotifyIconW(N.NIM_SETVERSION, ref _nid);
        Theme.Changed += OnThemeChanged;
    }

    void LoadIcons() {
        // The taskbar theme decides which icon reads correctly: a light taskbar
        // needs the dark glyph and vice versa.
        int cx = N.GetSystemMetrics(N.SM_CXSMICON), cy = N.GetSystemMetrics(N.SM_CYSMICON);
        var idle = Theme.DarkTaskbar ? "mic-light.ico" : "mic-dark.ico";
        var old = (_iconIdle, _iconRec);
        _iconIdle = LoadIco(idle, cx, cy);
        _iconRec = LoadIco("mic-rec.ico", cx, cy);
        if (old.Item1 != 0) N.DestroyIcon(old.Item1);
        if (old.Item2 != 0) N.DestroyIcon(old.Item2);
    }

    static nint LoadIco(string name, int cx, int cy) {
        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        if (res is null) return 0;
        using var s = asm.GetManifestResourceStream(res);
        var buf = new byte[s.Length];
        s.ReadExactly(buf);
        fixed (byte* p = buf) {
            // Walk the ICO directory ourselves and hand CreateIconFromResourceEx the
            // single best-matching image, so Windows never rescales for us.
            int count = BitConverter.ToUInt16(buf, 4);
            int best = -1, bestDelta = int.MaxValue;
            for (int i = 0; i < count; i++) {
                int e = 6 + i * 16;
                int w = buf[e] == 0 ? 256 : buf[e];
                int d = Math.Abs(w - cx);
                if (d < bestDelta) { bestDelta = d; best = e; }
            }
            if (best < 0) return 0;
            uint size = BitConverter.ToUInt32(buf, best + 8);
            uint off = BitConverter.ToUInt32(buf, best + 12);
            return N.CreateIconFromResourceEx(p + off, size, 1, 0x00030000, cx, cy, 0);
        }
    }

    void OnThemeChanged() { LoadIcons(); SetRecording(Recording); }

    public void SetRecording(bool on) {
        Recording = on;
        if (!_added) return;
        _nid.uFlags = N.NIF_ICON | N.NIF_TIP | N.NIF_SHOWTIP;
        _nid.hIcon = on ? _iconRec : _iconIdle;
        _nid.szTip = on ? "Whisperkey - listening" : Paused ? "Whisperkey - paused" : "Whisperkey";
        N.Shell_NotifyIconW(N.NIM_MODIFY, ref _nid);
    }

    /// Tray balloon. Used for anything that would otherwise fail silently,
    /// such as a hotkey that could not be bound.
    public void Notify(string title, string message) {
        if (!_added) return;
        _nid.uFlags = N.NIF_INFO;
        _nid.szInfoTitle = title;
        _nid.szInfo = message;
        _nid.dwInfoFlags = 0x01; // NIIF_INFO
        N.Shell_NotifyIconW(N.NIM_MODIFY, ref _nid);
    }

    public int ShowMenu() {
        var m = N.CreatePopupMenu();
        N.AppendMenuW(m, N.MF_STRING | (Paused ? N.MF_GRAYED : 0), CmdDictate, "&Start Dictation");
        N.AppendMenuW(m, N.MF_STRING | (Paused ? N.MF_CHECKED : 0), CmdPause, "&Pause");
        N.AppendMenuW(m, N.MF_SEPARATOR, 0, null);
        N.AppendMenuW(m, N.MF_STRING | (IsStartupEnabled() ? N.MF_CHECKED : 0), CmdStartup, "Start with &Windows");
        N.AppendMenuW(m, N.MF_STRING, CmdConfig, "&Open Config");
        N.AppendMenuW(m, N.MF_SEPARATOR, 0, null);
        N.AppendMenuW(m, N.MF_STRING, CmdAbout, "&About Whisperkey");
        N.AppendMenuW(m, N.MF_STRING, CmdExit, "E&xit");

        // Documented dance: without this the menu will not dismiss on click-away.
        N.SetForegroundWindow(_hwnd);
        N.GetCursorPos(out var pt);
        int cmd = N.TrackPopupMenuEx(m, N.TPM_RIGHTBUTTON | N.TPM_RETURNCMD, pt.X, pt.Y, _hwnd, 0);
        N.PostMessageW(_hwnd, N.WM_NULL, 0, 0);
        N.DestroyMenu(m);
        return cmd;
    }

    public void Dispose() {
        Theme.Changed -= OnThemeChanged;
        if (_added) N.Shell_NotifyIconW(N.NIM_DELETE, ref _nid);
        if (_iconIdle != 0) N.DestroyIcon(_iconIdle);
        if (_iconRec != 0) N.DestroyIcon(_iconRec);
    }
}
