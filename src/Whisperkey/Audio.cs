using System.Runtime.InteropServices;

namespace Whisperkey;

/// Microphone capture at 16 kHz mono, which is what the model wants, so nothing
/// has to be resampled afterwards.
///
/// Uses waveIn rather than WASAPI. WASAPI would mean several hundred lines of
/// hand-written COM for NativeAOT to capture one fixed format from the default
/// device; waveIn is a handful of P/Invokes and Windows implements it on top of
/// WASAPI anyway.
///
/// The device is opened when recording starts, not held open. Holding it open
/// would light the Windows "microphone in use" privacy indicator permanently,
/// which is the opposite of unobtrusive.
sealed class Audio : IDisposable {
    public const int SampleRate = 16000;

    const int WAVE_MAPPER = -1, WAVE_FORMAT_PCM = 1, CALLBACK_FUNCTION = 0x00030000;
    const int WIM_DATA = 0x3C0;
    const int BufferCount = 4, BufferMs = 40;

    delegate void WaveProc(nint hwi, uint msg, nint inst, nint p1, nint p2);

    [StructLayout(LayoutKind.Sequential)]
    struct WAVEFORMATEX { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }

    [StructLayout(LayoutKind.Sequential)]
    struct WAVEHDR { public nint lpData; public uint dwBufferLength, dwBytesRecorded; public nint dwUser; public uint dwFlags, dwLoops; public nint lpNext; public nint reserved; }

    [DllImport("winmm.dll")] static extern int waveInOpen(out nint h, int devId, ref WAVEFORMATEX fmt, WaveProc cb, nint inst, int flags);
    [DllImport("winmm.dll")] static extern int waveInPrepareHeader(nint h, nint hdr, int size);
    [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(nint h, nint hdr, int size);
    [DllImport("winmm.dll")] static extern int waveInAddBuffer(nint h, nint hdr, int size);
    [DllImport("winmm.dll")] static extern int waveInStart(nint h);
    [DllImport("winmm.dll")] static extern int waveInStop(nint h);
    [DllImport("winmm.dll")] static extern int waveInReset(nint h);
    [DllImport("winmm.dll")] static extern int waveInClose(nint h);
    [DllImport("winmm.dll")] static extern int waveInGetNumDevs();

    nint _h;
    nint[] _headers = [];
    WaveProc _cb;                      // must outlive the device
    readonly List<float> _samples = new(SampleRate * 30);
    readonly object _gate = new();
    volatile bool _capturing;
    volatile float _level;

    /// Peak-ish level in 0..1 for the overlay ring.
    public float Level => _level;
    public bool Capturing => _capturing;

    /// Raised if the device disappears mid-recording (unplugged headset, etc).
    public event Action<string> Lost;

    /// Opens and immediately closes the device once at startup.
    ///
    /// The first waveInOpen of a session costs ~276ms of driver initialisation and
    /// the rest cost ~104ms, so paying that once in the background keeps the first
    /// real dictation inside the latency budget. The privacy indicator flashes for
    /// a moment at startup, which is a fair trade against the first dictation
    /// feeling slow.
    public void Prewarm(string device) {
        try {
            if (waveInGetNumDevs() == 0) return;
            var fmt = Format();
            if (waveInOpen(out nint h, ParseDevice(device), ref fmt, null, 0, 0) != 0) return;
            waveInClose(h);
            Log.Write("audio device pre-warmed");
        } catch { }
    }

    static WAVEFORMATEX Format() => new() {
        wFormatTag = WAVE_FORMAT_PCM, nChannels = 1, nSamplesPerSec = SampleRate,
        wBitsPerSample = 16, nBlockAlign = 2, nAvgBytesPerSec = SampleRate * 2, cbSize = 0,
    };

    public bool Start(string device) {
        if (_capturing) return true;
        if (waveInGetNumDevs() == 0) { Lost?.Invoke("No microphone was found."); return false; }

        lock (_gate) _samples.Clear();
        _level = 0;

        var fmt = Format();
        _cb = OnWave;
        int devId = ParseDevice(device);
        int rc = waveInOpen(out _h, devId, ref fmt, _cb, 0, CALLBACK_FUNCTION);
        if (rc != 0) { Lost?.Invoke($"Could not open the microphone (waveIn error {rc})."); return false; }

        int bytes = SampleRate * 2 * BufferMs / 1000;
        _headers = new nint[BufferCount];
        for (int i = 0; i < BufferCount; i++) {
            var hdr = new WAVEHDR { lpData = Marshal.AllocHGlobal(bytes), dwBufferLength = (uint)bytes };
            nint p = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());
            Marshal.StructureToPtr(hdr, p, false);
            _headers[i] = p;
            waveInPrepareHeader(_h, p, Marshal.SizeOf<WAVEHDR>());
            waveInAddBuffer(_h, p, Marshal.SizeOf<WAVEHDR>());
        }

        _capturing = true;
        waveInStart(_h);
        return true;
    }

    static int ParseDevice(string device) =>
        string.IsNullOrWhiteSpace(device) || device.Equals("default", StringComparison.OrdinalIgnoreCase)
            ? WAVE_MAPPER
            : int.TryParse(device, out int i) ? i : WAVE_MAPPER;

    void OnWave(nint hwi, uint msg, nint inst, nint p1, nint p2) {
        if (msg != WIM_DATA || !_capturing) return;
        var hdr = Marshal.PtrToStructure<WAVEHDR>(p1);
        int n = (int)hdr.dwBytesRecorded / 2;
        if (n > 0) {
            var buf = new float[n];
            float peak = 0;
            for (int i = 0; i < n; i++) {
                short s = Marshal.ReadInt16(hdr.lpData, i * 2);
                float f = s / 32768f;
                buf[i] = f;
                float a = MathF.Abs(f);
                if (a > peak) peak = a;
            }
            _level = peak;
            lock (_gate) _samples.AddRange(buf);
        }
        // Recycle immediately so capture is continuous.
        if (_capturing) waveInAddBuffer(_h, p1, Marshal.SizeOf<WAVEHDR>());
    }

    /// Stops capture and hands back everything recorded.
    public float[] Stop() {
        if (!_capturing) return [];
        _capturing = false;
        waveInStop(_h);
        waveInReset(_h);
        Release();
        _level = 0;
        lock (_gate) return _samples.ToArray();
    }

    void Release() {
        foreach (var p in _headers) {
            if (p == 0) continue;
            waveInUnprepareHeader(_h, p, Marshal.SizeOf<WAVEHDR>());
            var hdr = Marshal.PtrToStructure<WAVEHDR>(p);
            if (hdr.lpData != 0) Marshal.FreeHGlobal(hdr.lpData);
            Marshal.FreeHGlobal(p);
        }
        _headers = [];
        if (_h != 0) { waveInClose(_h); _h = 0; }
    }

    public void Dispose() { if (_capturing) Stop(); else Release(); }
}
