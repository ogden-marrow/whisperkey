using System.Runtime.InteropServices;

namespace Whisperkey;

/// The floating pill. The only thing this app ever shows.
///
/// A layered window with per-pixel alpha, positioned near the caret. It must never
/// take focus or disturb the foreground window, so it is WS_EX_NOACTIVATE plus
/// WS_EX_TOOLWINDOW and is shown with SW_SHOWNOACTIVATE.
sealed unsafe class Overlay : IDisposable {
    const int WS_EX_LAYERED = 0x00080000, WS_EX_TRANSPARENT = 0x00000020,
              WS_EX_TOOLWINDOW = 0x00000080, WS_EX_TOPMOST = 0x00000008, WS_EX_NOACTIVATE = 0x08000000;
    const int WS_POPUP = unchecked((int)0x80000000);
    const int SW_SHOWNOACTIVATE = 4, SW_HIDE = 0;
    const int ULW_ALPHA = 2, AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;
    const int MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] struct BLENDFUNCTION { public byte Op, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public uint cbSize; public N.RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)] struct GUITHREADINFO { public uint cbSize; public uint flags; public nint hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public N.RECT rcCaret; }

    [DllImport("user32.dll")] static extern int UpdateLayeredWindow(nint h, nint dst, ref N.POINT ppt, ref SIZE size, nint src, ref N.POINT ppos, uint key, ref BLENDFUNCTION bf, int flags);
    [DllImport("user32.dll")] static extern int ShowWindow(nint h, int cmd);
    [DllImport("user32.dll")] static extern nint GetDC(nint h);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint h, nint dc);
    [DllImport("user32.dll")] static extern nint MonitorFromPoint(N.POINT pt, int flags);
    [DllImport("user32.dll")] static extern int GetMonitorInfoW(nint mon, ref MONITORINFO mi);
    [DllImport("user32.dll")] static extern int GetGUIThreadInfo(uint tid, ref GUITHREADINFO gti);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint h, nint pid);
    [DllImport("user32.dll")] static extern int ClientToScreen(nint h, ref N.POINT pt);
    [DllImport("user32.dll")] static extern int GetCursorPos(out N.POINT p);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint h);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] static extern int DeleteDC(nint dc);
    [DllImport("gdi32.dll")] static extern nint CreateDIBSection(nint dc, ref BITMAPINFOHEADER bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] static extern int DeleteObject(nint obj);

    nint _hwnd, _dc, _bmp, _old, _bits;
    int _w, _h;
    float _scale = 1f;
    bool _visible;

    public bool Busy { get; set; }

    public void Create(nint inst) {
        _hwnd = N.CreateWindowExW(
            WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT,
            "WhisperkeyHost", "", WS_POPUP, 0, 0, Pill.W, Pill.H, 0, 0, inst, 0);
    }

    /// Places the pill near the caret, flipped and clamped so it never covers the
    /// text being typed and never lands off screen.
    public void Show() {
        if (_hwnd == 0) return;
        var anchor = CaretOrCursor();

        uint dpi = GetDpiForWindow(_hwnd);
        float scale = dpi == 0 ? 1f : dpi / 96f;
        int w = (int)MathF.Round(Pill.W * scale), h = (int)MathF.Round(Pill.H * scale);
        EnsureSurface(w, h, scale);

        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(MonitorFromPoint(anchor, MONITOR_DEFAULTTONEAREST), ref mi);

        int x = anchor.X - w / 4;
        int y = anchor.Y + (int)(24 * scale);              // below the caret by default
        if (y + h > mi.rcWork.B) y = anchor.Y - h - (int)(8 * scale);   // flip above
        x = Math.Clamp(x, mi.rcWork.L, Math.Max(mi.rcWork.L, mi.rcWork.R - w));
        y = Math.Clamp(y, mi.rcWork.T, Math.Max(mi.rcWork.T, mi.rcWork.B - h));

        // Short ease-out entrance, and none at all when the "Animation effects"
        // accessibility setting is off.
        bool fade = !_visible && Theme.AnimationsOn;
        _opacity = fade ? 0f : 1f;
        Paint(x, y, 0f);
        if (!_visible) { ShowWindow(_hwnd, SW_SHOWNOACTIVATE); _visible = true; }

        if (fade) {
            for (int i = 1; i <= 6; i++) {
                float t = i / 6f;
                _opacity = 1f - (1f - t) * (1f - t);   // ease-out, no bounce
                Paint(int.MinValue, 0, 0f);
                Thread.Sleep(8);
            }
            _opacity = 1f;
        }
    }

    public void Update(float level) { if (_visible) Paint(int.MinValue, 0, level); }

    public void Hide() {
        if (!_visible) return;
        ShowWindow(_hwnd, SW_HIDE);
        _visible = false;
        _opacity = 1f;
        Busy = false;
    }

    void EnsureSurface(int w, int h, float scale) {
        if (_bmp != 0 && w == _w && h == _h) { _scale = scale; return; }
        Release();
        _w = w; _h = h; _scale = scale;
        var bmi = new BITMAPINFOHEADER {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w, biHeight = -h,       // top-down
            biPlanes = 1, biBitCount = 32, biCompression = 0,
        };
        var screen = GetDC(0);
        _dc = CreateCompatibleDC(screen);
        _bmp = CreateDIBSection(screen, ref bmi, 0, out _bits, 0, 0);
        ReleaseDC(0, screen);
        _old = SelectObject(_dc, _bmp);
    }

    int _lastX, _lastY;
    float _opacity = 1f;

    void Paint(int x, int y, float level) {
        if (_bits == 0) return;
        if (x != int.MinValue) { _lastX = x; _lastY = y; }

        var span = new Span<uint>((void*)_bits, _w * _h);
        Pill.Render(span, _w, _h, _scale, Theme.Accent, level, Busy, Theme.HighContrast, 0xFFFFFFFF, 0xFF000000,
                    Theme.TransparencyOn, _opacity);

        var pos = new N.POINT { X = _lastX, Y = _lastY };
        var size = new SIZE { cx = _w, cy = _h };
        var src = new N.POINT { X = 0, Y = 0 };
        var bf = new BLENDFUNCTION { Op = AC_SRC_OVER, Alpha = 255, Format = AC_SRC_ALPHA };
        UpdateLayeredWindow(_hwnd, 0, ref pos, ref size, _dc, ref src, 0, ref bf, ULW_ALPHA);
    }

    /// The caret if the focused app reports one, otherwise the mouse - which is
    /// where the user is looking anyway.
    static N.POINT CaretOrCursor() {
        var fg = GetForegroundWindow();
        uint tid = GetWindowThreadProcessId(fg, 0);
        var gti = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        if (tid != 0 && GetGUIThreadInfo(tid, ref gti) != 0 && gti.hwndCaret != 0) {
            var p = new N.POINT { X = gti.rcCaret.L, Y = gti.rcCaret.B };
            if (ClientToScreen(gti.hwndCaret, ref p) != 0 && (p.X != 0 || p.Y != 0)) return p;
        }
        GetCursorPos(out var c);
        return c;
    }

    void Release() {
        if (_dc != 0 && _old != 0) SelectObject(_dc, _old);
        if (_bmp != 0) DeleteObject(_bmp);
        if (_dc != 0) DeleteDC(_dc);
        _bmp = _dc = _old = _bits = 0;
    }

    public void Dispose() { Release(); if (_hwnd != 0) N.DestroyWindow(_hwnd); }
}
