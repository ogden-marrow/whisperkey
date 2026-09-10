using System.Runtime.InteropServices;

namespace Whisperkey;

/// Makes classic Win32 menus follow the system dark theme.
///
/// Windows 11's own tray menus are dark, but a menu built with TrackPopupMenuEx
/// renders light regardless, which is exactly the kind of detail that makes an app
/// read as not-quite-native.
///
/// The only way to fix it short of owner-drawing every menu is the set of private
/// uxtheme exports that Explorer itself uses. They have no names - only ordinals -
/// and no contract, so every lookup is defensive: if anything is missing we leave
/// the menu light rather than fail.
static unsafe class DarkMode {
    enum AppMode { Default = 0, AllowDark = 1, ForceDark = 2, ForceLight = 3 }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern nint LoadLibraryW(string name);
    [DllImport("kernel32.dll")] static extern nint GetProcAddress(nint mod, nint ordinal);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(nint hwnd, string app, string id);

    static delegate* unmanaged<int, int> _setPreferredAppMode;
    static delegate* unmanaged<nint, int, int> _allowDarkModeForWindow;
    static delegate* unmanaged<void> _flushMenuThemes;
    static delegate* unmanaged<void> _refreshImmersiveColorPolicyState;
    static bool _tried;

    static void Resolve() {
        if (_tried) return;
        _tried = true;
        try {
            var ux = LoadLibraryW("uxtheme.dll");
            if (ux == 0) return;
            // Ordinals, because these exports have no names.
            _setPreferredAppMode             = (delegate* unmanaged<int, int>)GetProcAddress(ux, 135);
            _allowDarkModeForWindow          = (delegate* unmanaged<nint, int, int>)GetProcAddress(ux, 133);
            _flushMenuThemes                 = (delegate* unmanaged<void>)GetProcAddress(ux, 136);
            _refreshImmersiveColorPolicyState= (delegate* unmanaged<void>)GetProcAddress(ux, 104);
        } catch { /* stay light */ }
    }

    /// Call once at startup, and again whenever the theme changes.
    public static void Apply(nint hwnd) {
        Resolve();
        try {
            // AllowDark rather than ForceDark: follow Windows, do not overrule it.
            if (_setPreferredAppMode != null) _setPreferredAppMode((int)AppMode.AllowDark);
            if (_refreshImmersiveColorPolicyState != null) _refreshImmersiveColorPolicyState();
            if (hwnd != 0 && _allowDarkModeForWindow != null) _allowDarkModeForWindow(hwnd, Theme.DarkTaskbar ? 1 : 0);
            if (hwnd != 0) SetWindowTheme(hwnd, Theme.DarkTaskbar ? "DarkMode_Explorer" : "Explorer", null);
            // Menus cache their theme; without this the change lands only on the next menu after next.
            if (_flushMenuThemes != null) _flushMenuThemes();
        } catch { }
    }
}
