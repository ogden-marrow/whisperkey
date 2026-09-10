using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Whisperkey;

/// Tracks whether the caret is sitting in an editable text field.
///
/// Everything expensive happens here, on a focus-change event. The keyboard hook
/// only ever reads <see cref="IsEditable"/>, because a UIA round trip was measured
/// at up to 51 ms and that would be a visible stall on a keystroke.
sealed unsafe class FocusWatcher : IDisposable {
    const int EVENT_OBJECT_FOCUS = 0x8005;
    const int EVENT_SYSTEM_FOREGROUND = 0x0003;
    const int WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;

    delegate void WinEventProc(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] static extern nint SetWinEventHook(uint min, uint max, nint mod, WinEventProc cb, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern int UnhookWinEvent(nint h);
    [DllImport("user32.dll")] static extern int GetMessageW(out N.MSG m, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern nint DispatchMessageW(ref N.MSG m);
    [DllImport("user32.dll")] static extern int PostThreadMessageW(uint tid, uint msg, nint w, nint l);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    volatile bool _editable;
    volatile int _controlType;
    uint _threadId;
    nint _hook;
    Thread _thread;
    WinEventProc _cb;               // must outlive the hook
    IUIAutomation _uia;

    /// Read by the keyboard hook. Must stay a plain field read.
    public bool IsEditable => _editable;
    public int ControlType => _controlType;

    public void Start() {
        _thread = new Thread(Run) { IsBackground = true, Name = "Whisperkey.Focus" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    void Run() {
        // MTA, not STA. See ADR 0002.
        Uia.CoInitializeEx(0, Uia.COINIT_MULTITHREADED);
        var iid = typeof(IUIAutomation).GUID;
        if (Uia.CoCreateInstance(in Uia.CLSID_CUIAutomation, 0, 1, in iid, out nint ppv) < 0) return;
        _uia = ComInterfaceMarshaller<IUIAutomation>.ConvertToManaged((void*)ppv);

        _threadId = GetCurrentThreadId();
        _cb = OnEvent;
        // Out-of-context hooks are delivered to this thread, so this thread pumps.
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_OBJECT_FOCUS, 0, _cb, 0, 0,
                                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        Evaluate();   // seed from whatever is focused right now

        while (GetMessageW(out var msg, 0, 0, 0) > 0) DispatchMessageW(ref msg);

        if (_hook != 0) UnhookWinEvent(_hook);
    }

    void OnEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time) {
        const int OBJID_CARET = -8;
        if (idObject == OBJID_CARET) return;   // caret moves do not change what is focused
        Evaluate();
    }

    void Evaluate() {
        try {
            if (_uia.GetFocusedElement(out var el) < 0 || el is null) { Set(false, 0); return; }

            int ct = Uia.Int(el, UiaProp.ControlType);

            // ControlType is the gate. TextPattern alone is not sufficient: an
            // Explorer file list item reports TextPattern with ValueIsReadOnly=false
            // and would otherwise be treated as a text box, swallowing the hotkey
            // in Explorer. Measured; see ADR 0002.
            bool typeOk = ct is UiaCtrl.Edit or UiaCtrl.Document or UiaCtrl.ComboBox;
            if (!typeOk) { Set(false, ct); return; }

            if (Uia.Flag(el, UiaProp.IsPassword)) { Set(false, ct); return; }   // never dictate a password
            if (!Uia.Flag(el, UiaProp.IsKeyboardFocusable)) { Set(false, ct); return; }

            // A ComboBox only counts when it genuinely takes text.
            if (ct == UiaCtrl.ComboBox && !Uia.Flag(el, UiaProp.IsValuePatternAvailable)) { Set(false, ct); return; }

            bool readOnly = Uia.Flag(el, UiaProp.IsValuePatternAvailable)
                         && Uia.Flag(el, UiaProp.ValueIsReadOnly);
            Set(!readOnly, ct);
        } catch {
            // A dying window mid-query is routine. Fail closed: pass the key through.
            Set(false, 0);
        }
    }

    void Set(bool editable, int ct) {
        if (Log.On && (editable != _editable || ct != _controlType))
            Log.Write($"focus: editable={editable} controlType={ct}");
        _editable = editable;
        _controlType = ct;
    }

    public void Dispose() {
        if (_threadId != 0) PostThreadMessageW(_threadId, 0x0012 /* WM_QUIT */, 0, 0);
    }
}
