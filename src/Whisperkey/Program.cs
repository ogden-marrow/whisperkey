using System.Runtime.InteropServices;

namespace Whisperkey;

static unsafe class Program {
    static Tray _tray;
    static FocusWatcher _focus;
    static Keyboard _keys;
    static Audio _audio;
    static Worker _worker;
    static N.WndProc _proc;   // must outlive the window; the GC does not know Win32 holds it

    [DllImport("ole32.dll")] static extern int CoInitializeEx(nint p, int f);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern nint ShellExecuteW(nint h, string op, string file, string p, string dir, int show);

    [STAThread]
    static int Main() {
        // One instance only. A second launch simply exits.
        N.CreateMutexW(0, 1, @"Local\Whisperkey.SingleInstance");
        if (N.GetLastError() == N.ERROR_ALREADY_EXISTS) return 0;

        // The UI thread is STA for shell APIs. UI Automation lives on its own MTA
        // thread - see ADR 0002, an STA there deadlocks every cross-process call.
        CoInitializeEx(0, 2);

        Theme.Refresh();
        Config.Load();

        var inst = N.GetModuleHandleW(null);
        _proc = WndProc;
        var cls = Marshal.StringToHGlobalUni("WhisperkeyHost");
        var wc = new N.WNDCLASSEX {
            cbSize = (uint)Marshal.SizeOf<N.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = inst,
            lpszClassName = cls,
        };
        N.RegisterClassExW(ref wc);

        // WS_EX_TOOLWINDOW: no taskbar entry, no Alt+Tab entry.
        var hwnd = N.CreateWindowExW(0x00000080, "WhisperkeyHost", "Whisperkey", 0, 0, 0, 0, 0, 0, 0, inst, 0);
        if (hwnd == 0) return 1;

        _tray = new Tray(hwnd) { IsStartupEnabled = () => Startup.Enabled };

        // A bad config file must say so rather than silently reverting to defaults.
        Config.Invalid += m => _tray.Notify("Whisperkey config", m);
        Config.Changed += () => _tray.Notify("Whisperkey", $"Settings reloaded. Hotkey: {Config.Current.Hotkey}");

        _focus = new FocusWatcher();
        _focus.Start();

        _worker = new Worker();

        _audio = new Audio();
        _audio.Lost += m => _tray.Notify("Whisperkey", m);
        var dev = Config.Current.Device;
        _worker.Post(() => _audio.Prewarm(dev));   // never block startup

        _keys = new Keyboard(_focus);
        _keys.Failed  += m => _tray.Notify("Whisperkey", m);
        _keys.Killed  += () => _tray.Notify("Whisperkey",
            "Keyboard hook released by double-Esc. Use the tray menu to start dictation again.");
        // The hook only enqueues. Doing ~110ms of device work inside the hook proc
        // would risk Windows dropping the hook and killing the hotkey system-wide.
        _keys.StartRequested  += () => { _pressed = System.Diagnostics.Stopwatch.GetTimestamp(); _worker.Post(BeginDictation); };
        _keys.StopRequested   += () => _worker.Post(EndDictation);
        _keys.CancelRequested += () => _worker.Post(CancelDictation);
        _keys.Start();

        while (N.GetMessageW(out var msg, 0, 0, 0) > 0) {
            N.TranslateMessage(ref msg);
            N.DispatchMessageW(ref msg);
        }

        _worker.Dispose();
        _keys.Dispose();
        _audio.Dispose();
        _focus.Dispose();
        _tray.Dispose();
        return 0;
    }

    static nint WndProc(nint hwnd, uint msg, nint w, nint l) {
        switch (msg) {
            case N.WM_TRAY:
                // With NOTIFYICON_VERSION_4 the event is in the low word of lParam.
                int ev = (int)(l & 0xFFFF);
                if (ev == N.WM_RBUTTONUP || ev == N.WM_CONTEXTMENU) OnCommand(_tray.ShowMenu());
                else if (ev == N.WM_LBUTTONUP) OnCommand(Tray.CmdDictate);
                return 0;

            case N.WM_COMMAND:
                OnCommand((int)(w & 0xFFFF));
                return 0;

            // Theme, accent and accessibility settings change by notification, never by polling.
            case N.WM_SETTINGCHANGE:
            case N.WM_DWMCOLORIZATIONCOLORCHANGED:
                Theme.Refresh();
        Config.Load();
                return 0;

            case N.WM_DESTROY:
                N.PostQuitMessage(0);
                return 0;
        }
        return N.DefWindowProcW(hwnd, msg, w, l);
    }

    static long _pressed;

    static void BeginDictation() {
        // Measured from the keypress itself, including queue time, because that is
        // what the user actually waits for.
        long t0 = _pressed != 0 ? _pressed : System.Diagnostics.Stopwatch.GetTimestamp();
        if (!_audio.Start(Config.Current.Device)) return;
        SetRecording(true);
        Log.Write($"start -> capturing in {System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds:F1} ms");
    }

    static void EndDictation() {
        var samples = _audio.Stop();
        SetRecording(false);
        Log.Write($"stop: {samples.Length} samples ({samples.Length / (double)Audio.SampleRate:F2}s)");
        // Transcription and insertion land in #9 and #10.
    }

    static void CancelDictation() {
        _audio.Stop();
        SetRecording(false);
        Log.Write("cancel: discarded");
    }

    static void SetRecording(bool on) {
        _keys.SetRecording(on);
        _tray.SetRecording(on);
    }

    static void OnCommand(int cmd) {
        switch (cmd) {
            case Tray.CmdDictate:
                _keys.Rearm();                       // also the way back from the kill switch
                _worker.Post(() => { if (_tray.Recording) EndDictation(); else BeginDictation(); });
                break;
            case Tray.CmdPause:
                _tray.Paused = !_tray.Paused;
                _keys.Paused = _tray.Paused;
                SetRecording(false);
                break;
            case Tray.CmdStartup:
                Startup.Toggle();
                break;
            case Tray.CmdConfig:
                ShellExecuteW(0, "open", Config.EnsureFile(), null, null, 1);
                break;
            case Tray.CmdAbout:
                _tray.Notify("Whisperkey", "Local dictation into any text field. Nothing leaves this machine.");
                break;
            case Tray.CmdExit:
                N.PostQuitMessage(0);
                break;
        }
    }
}
