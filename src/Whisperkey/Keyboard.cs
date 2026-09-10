using System.Runtime.InteropServices;

namespace Whisperkey;

/// The global keyboard hook.
///
/// Lives on its own thread with its own message pump so a busy UI thread can never
/// make the whole system feel laggy - Windows drops a hook that is slow to return.
/// The hook proc itself only reads cached state and posts work elsewhere.
sealed class Keyboard : IDisposable {
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, WM_KEYUP = 0x0101, WM_SYSKEYUP = 0x0105;
    const int VK_ESCAPE = 0x1B;

    delegate nint HookProc(int code, nint w, nint l);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public nint dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] static extern nint SetWindowsHookExW(int id, HookProc fn, nint mod, uint tid);
    [DllImport("user32.dll")] static extern int UnhookWindowsHookEx(nint h);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint h, int code, nint w, nint l);
    [DllImport("user32.dll")] static extern int GetMessageW(out N.MSG m, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern nint DispatchMessageW(ref N.MSG m);
    [DllImport("user32.dll")] static extern int PostThreadMessageW(uint tid, uint msg, nint w, nint l);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    const int VK_CONTROL = 0x11, VK_MENU = 0x12, VK_SHIFT = 0x10, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    readonly FocusWatcher _focus;
    nint _hook;
    uint _threadId;
    Thread _thread;
    HookProc _proc;                 // must outlive the hook
    long _lastEscTicks;
    volatile bool _recording;

    public Keyboard(FocusWatcher focus) => _focus = focus;

    public bool Paused { get; set; }
    public bool Installed => _hook != 0;

    /// Raised on the hook thread. Handlers must return immediately.
    public event Action StartRequested, StopRequested, CancelRequested;
    /// Raised when the double-Esc kill switch releases the hook.
    public event Action Killed;
    /// Raised when the hook could not be installed, so nothing fails silently.
    public event Action<string> Failed;

    public void Start() {
        _thread = new Thread(Run) { IsBackground = true, Name = "Whisperkey.Keyboard" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    void Run() {
        _threadId = GetCurrentThreadId();
        if (!Install()) return;
        while (GetMessageW(out var msg, 0, 0, 0) > 0) DispatchMessageW(ref msg);
        Uninstall();
    }

    bool Install() {
        _proc = Hook;
        _hook = SetWindowsHookExW(WH_KEYBOARD_LL, _proc, 0, 0);
        if (_hook == 0) {
            Failed?.Invoke($"Could not install the keyboard hook (error {Marshal.GetLastWin32Error()}). Dictation is unavailable.");
            return false;
        }
        Log.Write("keyboard hook installed");
        return true;
    }

    void Uninstall() {
        if (_hook == 0) return;
        UnhookWindowsHookEx(_hook);
        _hook = 0;
        Log.Write("keyboard hook released");
    }

    /// Re-arm after the kill switch. Kill ends the pump and the thread, so this
    /// starts a fresh one rather than trying to revive the old.
    public void Rearm() {
        if (_hook != 0) return;
        Start();
    }

    public void SetRecording(bool on) => _recording = on;

    nint Hook(int code, nint w, nint l) {
        if (code != 0) return CallNextHookEx(_hook, code, w, l);

        int msg = (int)w;
        bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
        if (!down) return CallNextHookEx(_hook, code, w, l);

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(l);
        int vk = (int)data.vkCode;

        // Kill switch first: it must work even if everything else is wrong.
        if (vk == VK_ESCAPE) {
            long now = Environment.TickCount64;
            if (now - _lastEscTicks <= 350) {
                _lastEscTicks = 0;
                Kill();
                return CallNextHookEx(_hook, code, w, l);   // let the app have its Esc too
            }
            _lastEscTicks = now;
        }

        var cfg = Config.Current;

        if (_recording) {
            if (Matches(vk, cfg.StopKey))   { StopRequested?.Invoke();   return 1; }
            if (Matches(vk, cfg.CancelKey)) { CancelRequested?.Invoke(); return 1; }
            return CallNextHookEx(_hook, code, w, l);
        }

        if (Paused) return CallNextHookEx(_hook, code, w, l);

        if (Hotkey.TryParse(cfg.Hotkey, out var hk) && hk.Vk == vk && ModifiersMatch(hk)) {
            // The gate. If the caret is not in a text field we pass the key
            // through completely untouched, so Alt+D still works in editors.
            if (cfg.RequireTextField && !_focus.IsEditable)
                return CallNextHookEx(_hook, code, w, l);

            StartRequested?.Invoke();
            return 1;
        }

        return CallNextHookEx(_hook, code, w, l);
    }

    static bool Matches(int vk, string key) => Hotkey.TryParse(key, out var k) && k.Vk == vk;

    static bool ModifiersMatch(Hotkey hk) =>
        Held(VK_CONTROL) == hk.Ctrl && Held(VK_MENU) == hk.Alt &&
        Held(VK_SHIFT) == hk.Shift && (Held(VK_LWIN) || Held(VK_RWIN)) == hk.Win;

    static bool Held(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    void Kill() {
        Uninstall();
        _recording = false;
        Killed?.Invoke();
        if (_threadId != 0) PostThreadMessageW(_threadId, 0x0012 /* WM_QUIT */, 0, 0);
    }

    public void Dispose() {
        if (_threadId != 0) PostThreadMessageW(_threadId, 0x0012, 0, 0);
    }
}
