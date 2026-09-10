using System.Runtime.InteropServices;

namespace Whisperkey;

/// SystemParametersInfo lookups that have no registry equivalent worth trusting.
static class SystemInfo {
    const int SPI_GETHIGHCONTRAST = 0x0042, SPI_GETCLIENTAREAANIMATION = 0x1042;
    const int HCF_HIGHCONTRASTON = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    struct HIGHCONTRAST { public uint cbSize; public uint dwFlags; public nint lpszDefaultScheme; }

    [DllImport("user32.dll")] static extern int SystemParametersInfoW(int action, int param, ref HIGHCONTRAST pv, int win);
    [DllImport("user32.dll")] static extern int SystemParametersInfoW(int action, int param, ref int pv, int win);

    public static bool HighContrast() {
        var hc = new HIGHCONTRAST { cbSize = (uint)Marshal.SizeOf<HIGHCONTRAST>() };
        return SystemParametersInfoW(SPI_GETHIGHCONTRAST, 0, ref hc, 0) != 0
            && (hc.dwFlags & HCF_HIGHCONTRASTON) != 0;
    }

    /// Honours the "Animation effects" accessibility toggle. If this is off we show
    /// the pill instantly rather than fading it in.
    public static bool AnimationsEnabled() {
        int on = 1;
        return SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION, 0, ref on, 0) == 0 || on != 0;
    }
}
