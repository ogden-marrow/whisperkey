using System.Diagnostics;

namespace Whisperkey;

/// First-run model fetch. Downloads to a temp file, extracts, then swaps into
/// place, so an interrupted download can never leave a half-model that looks
/// installed.
static class ModelDownloader {
    public static event Action<string> Progress;

    public static async Task<bool> EnsureAsync(ModelSpec model, CancellationToken ct) {
        if (Models.IsInstalled(model)) return true;

        Directory.CreateDirectory(Models.Root);
        var archive = Path.Combine(Models.Root, model.Folder + ".download");
        var staging = Path.Combine(Models.Root, model.Folder + ".staging");

        try {
            Progress?.Invoke($"Downloading the speech model ({model.ApproxBytes / 1_000_000} MB). This happens once.");
            await DownloadAsync(model, archive, ct);

            Progress?.Invoke("Extracting the speech model...");
            CleanDir(staging);
            if (!Extract(archive, staging)) { Progress?.Invoke("Could not extract the speech model."); return false; }

            // The tarball contains its own top-level folder.
            var inner = Directory.GetDirectories(staging).FirstOrDefault() ?? staging;
            var target = Models.DirFor(model);
            CleanDir(target);
            Directory.Move(inner, target);

            Progress?.Invoke("Speech model ready.");
            return Models.IsInstalled(model);
        } catch (OperationCanceledException) {
            return false;
        } catch (Exception e) {
            Progress?.Invoke($"Could not prepare the speech model: {e.Message}");
            return false;
        } finally {
            TryDelete(archive);
            CleanDir(staging, removeOnly: true);
        }
    }

    static async Task DownloadAsync(ModelSpec model, string path, CancellationToken ct) {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var rsp = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        rsp.EnsureSuccessStatusCode();

        long total = rsp.Content.Headers.ContentLength ?? model.ApproxBytes;
        await using var src = await rsp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(path);

        var buf = new byte[1 << 16];
        long done = 0;
        int lastPct = -10;
        int read;
        while ((read = await src.ReadAsync(buf, ct)) > 0) {
            await dst.WriteAsync(buf.AsMemory(0, read), ct);
            done += read;
            int pct = (int)(done * 100 / Math.Max(total, 1));
            if (pct >= lastPct + 10) { lastPct = pct; Progress?.Invoke($"Downloading the speech model: {pct}%"); }
        }
    }

    /// Windows ships bsdtar, which handles .tar.bz2. Shelling out to it beats
    /// taking a bzip2 dependency for something used exactly once per model.
    static bool Extract(string archive, string into) {
        Directory.CreateDirectory(into);
        var psi = new ProcessStartInfo("tar.exe", $"-xf \"{archive}\" -C \"{into}\"") {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return false;
        p.WaitForExit();
        if (p.ExitCode != 0) Log.Write($"tar failed: {p.StandardError.ReadToEnd()}");
        return p.ExitCode == 0;
    }

    static void CleanDir(string dir, bool removeOnly = false) {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        if (!removeOnly) Directory.CreateDirectory(Path.GetDirectoryName(dir)!);
    }

    static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
}
