using System.Runtime.InteropServices;

namespace Whisperkey;

/// P/Invoke surface. Only what we actually call.
static unsafe class N {
    public const int WM_DESTROY = 0x0002, WM_COMMAND = 0x0111, WM_SETTINGCHANGE = 0x001A,
                     WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320, WM_APP = 0x8000, WM_NULL = 0x0000,
                     WM_RBUTTONUP = 0x0205, WM_LBUTTONUP = 0x0202, WM_CONTEXTMENU = 0x007B;
    public const int WM_TRAY = WM_APP + 1;
    public const int WM_OVERLAY_SHOW = WM_APP + 2, WM_OVERLAY_HIDE = WM_APP + 3, WM_OVERLAY_BUSY = WM_APP + 4;
    public const int WM_TIMER = 0x0113;

    public const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const int NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;

    public const int TPM_RIGHTBUTTON = 0x0002, TPM_RETURNCMD = 0x0100, TPM_NONOTIFY = 0x0080;
    public const int MF_STRING = 0x0000, MF_SEPARATOR = 0x0800, MF_CHECKED = 0x0008, MF_DISABLED = 0x0002, MF_GRAYED = 0x0001;

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public nint hwnd; public uint message; public nint w, l; public uint time; public POINT pt; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEX {
        public uint cbSize, style; public nint lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground, lpszMenuName, lpszClassName, hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA {
        public uint cbSize; public nint hWnd; public uint uID, uFlags; public int uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public nint hBalloonIcon;
    }

    public delegate nint WndProc(nint hwnd, uint msg, nint w, nint l);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint p);
    [DllImport("user32.dll")] public static extern ushort RegisterClassExW(ref WNDCLASSEX c);
    [DllImport("user32.dll")] public static extern nint DefWindowProcW(nint h, uint m, nint w, nint l);
    [DllImport("user32.dll")] public static extern int GetMessageW(out MSG m, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] public static extern int TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] public static extern nint DispatchMessageW(ref MSG m);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] public static extern int DestroyWindow(nint h);
    [DllImport("user32.dll")] public static extern int PostMessageW(nint h, uint m, nint w, nint l);

    [DllImport("user32.dll")] public static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] public static extern int DestroyMenu(nint m);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int AppendMenuW(nint m, int flags, nint id, string item);
    [DllImport("user32.dll")] public static extern int TrackPopupMenuEx(nint m, int flags, int x, int y, nint hwnd, nint p);
    [DllImport("user32.dll")] public static extern int SetForegroundWindow(nint h);
    [DllImport("user32.dll")] public static extern int GetCursorPos(out POINT p);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int Shell_NotifyIconW(int msg, ref NOTIFYICONDATA d);
    [DllImport("user32.dll")] public static extern nint CreateIconFromResourceEx(byte* bits, uint size, int icon, uint ver, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern int DestroyIcon(nint h);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern nint SetTimer(nint h, nint id, uint ms, nint fn);
    [DllImport("user32.dll")] public static extern int KillTimer(nint h, nint id);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandleW(string n);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint CreateMutexW(nint attr, int owner, string name);
    [DllImport("kernel32.dll")] public static extern int GetLastError();

    public const int ERROR_ALREADY_EXISTS = 183;
    public const int SM_CXSMICON = 49, SM_CYSMICON = 50;
}
