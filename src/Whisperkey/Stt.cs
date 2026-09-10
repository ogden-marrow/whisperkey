using SherpaOnnx;

namespace Whisperkey;

/// Local speech to text. Parakeet TDT via sherpa-onnx, CPU only - see ADR 0001.
///
/// The recogniser is built once and kept warm, because loading it costs ~1.9s and
/// the hotkey has a 150ms budget.
sealed class Stt : IDisposable {
    OfflineRecognizer _rec;
    ModelSpec _model;

    public bool Ready => _rec is not null;
    public event Action<string> Status;

    /// Builds the recogniser, fetching the model first if this is a first run.
    /// Runs on the worker; never call from the hook or the UI thread.
    public async Task<bool> LoadAsync(string modelKey, CancellationToken ct) {
        var spec = Models.Find(modelKey);
        if (_rec is not null && _model?.Key == spec.Key) return true;

        if (!await ModelDownloader.EnsureAsync(spec, ct)) return false;

        try {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var cfg = BuildConfig(spec);
            var rec = new OfflineRecognizer(cfg);
            _rec?.Dispose();
            _rec = rec;
            _model = spec;
            Log.Write($"model '{spec.Key}' loaded in {sw.ElapsedMilliseconds} ms");
            Status?.Invoke("Whisperkey is ready.");
            return true;
        } catch (Exception e) {
            Log.Write($"model load failed: {e}");
            Status?.Invoke($"Could not load the speech model: {e.Message}");
            return false;
        }
    }

    static OfflineRecognizerConfig BuildConfig(ModelSpec spec) {
        var dir = Models.DirFor(spec);
        string Pick(params string[] names) {
            foreach (var n in names) {
                var p = Path.Combine(dir, n);
                if (File.Exists(p)) return p;
            }
            return Path.Combine(dir, names[0]);
        }

        var cfg = new OfflineRecognizerConfig();
        cfg.ModelConfig.Tokens = Path.Combine(dir, "tokens.txt");
        // 4 threads, not 8: measured 695ms vs 747ms on a 12s clip, and it leaves
        // cores free so dictating never makes the rest of the machine feel slow.
        cfg.ModelConfig.NumThreads = 4;
        cfg.ModelConfig.Debug = 0;
        cfg.DecodingMethod = "greedy_search";

        if (spec.Kind == "transducer") {
            cfg.ModelConfig.Transducer.Encoder = Pick("encoder.int8.onnx", "encoder.onnx");
            cfg.ModelConfig.Transducer.Decoder = Pick("decoder.int8.onnx", "decoder.onnx");
            cfg.ModelConfig.Transducer.Joiner  = Pick("joiner.int8.onnx", "joiner.onnx");
            cfg.ModelConfig.ModelType = "nemo_transducer";
        } else {
            cfg.ModelConfig.NeMoCtc.Model = Pick("model.int8.onnx", "model.onnx");
        }
        return cfg;
    }

    /// Blocking transcription. Called on the worker thread only.
    public string Transcribe(float[] samples) {
        if (_rec is null || samples.Length == 0) return "";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var stream = _rec.CreateStream();
        stream.AcceptWaveform(Audio.SampleRate, samples);
        _rec.Decode(stream);
        var text = stream.Result.Text?.Trim() ?? "";
        Log.Write($"transcribed {samples.Length / (double)Audio.SampleRate:F2}s in {sw.ElapsedMilliseconds} ms -> \"{text}\"");
        return text;
    }

    public void Dispose() { _rec?.Dispose(); _rec = null; }
}
