using Whisperkey;
using Xunit;

namespace Whisperkey.Tests;

public class ConfigTests {
    [Fact]
    public void WritesCamelCaseAndLeavesPlusSignsAlone() {
        // A file a human edits should say Alt+D, not an escaped form.
        var json = Config.Serialize(new Settings());
        Assert.Contains("\"hotkey\"", json);
        Assert.Contains("Alt+D", json);
        var escaped = ((char)92) + "u002B";
        Assert.DoesNotContain(escaped, json);
    }

    [Fact]
    public void ReadsWhatItWrites() {
        var original = new Settings { Hotkey = "Ctrl+Shift+D", RequireTextField = false, Model = "parakeet-110m-en" };
        Assert.True(Config.TryParse(Config.Serialize(original), out var read, out _));
        Assert.Equal(original.Hotkey, read.Hotkey);
        Assert.Equal(original.RequireTextField, read.RequireTextField);
        Assert.Equal(original.Model, read.Model);
    }

    [Fact]
    public void AcceptsCommentsAndTrailingCommas() {
        var json = """
        {
          // the hotkey I actually use
          "hotkey": "Ctrl+Shift+D",
          "requireTextField": false,
        }
        """;
        Assert.True(Config.TryParse(json, out var s, out var error));
        Assert.Null(error);
        Assert.Equal("Ctrl+Shift+D", s.Hotkey);
        Assert.False(s.RequireTextField);
    }

    [Fact]
    public void AcceptsAnyCasing() {
        Assert.True(Config.TryParse("""{ "HOTKEY": "F9", "Model": "parakeet-110m-en" }""", out var s, out _));
        Assert.Equal("F9", s.Hotkey);
        Assert.Equal("parakeet-110m-en", s.Model);
    }

    [Fact]
    public void OmittedKeysKeepTheirDefaults() {
        Assert.True(Config.TryParse("""{ "hotkey": "F9" }""", out var s, out _));
        Assert.Equal("F9", s.Hotkey);
        Assert.Equal("Enter", s.StopKey);
        Assert.True(s.RequireTextField);
        Assert.Equal("system", s.Accent);
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"just a string\"")]
    public void RejectsRubbishWithAnExplanation(string json) {
        Assert.False(Config.TryParse(json, out var s, out var error));
        Assert.Null(s);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void EmptyDocumentIsRejectedRatherThanTreatedAsDefaults() {
        Assert.False(Config.TryParse("null", out var s, out var error));
        Assert.Null(s);
        Assert.Contains("empty", error);
    }
}
