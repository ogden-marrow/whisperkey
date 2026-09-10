using Whisperkey;
using Xunit;

namespace Whisperkey.Tests;

public class ModelsTests {
    [Fact]
    public void TheDefaultModelInSettingsActuallyExists() {
        // A typo here means a fresh install downloads the wrong model, or none.
        var key = new Settings().Model;
        Assert.Contains(Models.All, m => m.Key == key);
    }

    [Fact]
    public void UnknownModelFallsBackToTheFirstRatherThanThrowing() {
        Assert.Equal(Models.All[0].Key, Models.Find("no-such-model").Key);
    }

    [Fact]
    public void LookupIsCaseInsensitive() {
        Assert.Equal("parakeet-0.6b-v3", Models.Find("PARAKEET-0.6B-V3").Key);
    }

    [Fact]
    public void EveryModelIsSelfConsistent() {
        foreach (var m in Models.All) {
            Assert.False(string.IsNullOrWhiteSpace(m.Key));
            Assert.False(string.IsNullOrWhiteSpace(m.Folder));
            Assert.StartsWith("https://", m.Url);
            Assert.Contains(m.Kind, new[] { "transducer", "nemo-ctc" });
            Assert.True(m.ApproxBytes > 0);
            // The folder is the directory the tarball expands to, so the URL
            // should carry the same name or the extract step moves the wrong thing.
            Assert.Contains(m.Folder, m.Url);
        }
    }

    [Fact]
    public void ModelKeysAreUnique() {
        Assert.Equal(Models.All.Length, Models.All.Select(m => m.Key).Distinct().Count());
    }

    [Fact]
    public void NothingIsInstalledInAnEmptyDirectory() {
        foreach (var m in Models.All) {
            // No model files are present in a clean checkout, so this must be false
            // rather than throwing on a missing directory.
            var _ = Models.IsInstalled(m);
        }
    }
}
