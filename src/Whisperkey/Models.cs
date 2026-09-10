namespace Whisperkey;

/// The models we know how to fetch and load. Keeping this a plain table means
/// adding one is a data change, not a code change.
sealed record ModelSpec(
    string Key,
    string Folder,
    string Url,
    string Kind,               // "transducer" or "nemo-ctc"
    long ApproxBytes,
    string Description);

static class Models {
    const string Releases = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/";

    public static readonly ModelSpec[] All = [
        new("parakeet-0.6b-v3",
            "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8",
            Releases + "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2",
            "transducer", 487_000_000,
            "Best accuracy. 25 European languages. ~970 MB resident."),

        new("parakeet-110m-en",
            "sherpa-onnx-nemo-parakeet_tdt_ctc_110m-en-36000",
            Releases + "sherpa-onnx-nemo-parakeet_tdt_ctc_110m-en-36000.tar.bz2",
            "nemo-ctc", 434_000_000,
            "English only, roughly 3x faster. ~750 MB resident."),
    ];

    public static ModelSpec Find(string key) =>
        All.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Whisperkey", "models");

    public static string DirFor(ModelSpec m) => Path.Combine(Root, m.Folder);

    /// A model is present when the files sherpa actually loads are on disk.
    public static bool IsInstalled(ModelSpec m) {
        var d = DirFor(m);
        if (!Directory.Exists(d) || !File.Exists(Path.Combine(d, "tokens.txt"))) return false;
        return m.Kind == "transducer"
            ? File.Exists(Path.Combine(d, "encoder.int8.onnx")) || File.Exists(Path.Combine(d, "encoder.onnx"))
            : File.Exists(Path.Combine(d, "model.onnx")) || File.Exists(Path.Combine(d, "model.int8.onnx"));
    }
}
