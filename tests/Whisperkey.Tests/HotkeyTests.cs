using Whisperkey;
using Xunit;

namespace Whisperkey.Tests;

public class HotkeyTests {
    [Theory]
    [InlineData("Alt+D", 0x44, false, true, false, false)]
    [InlineData("alt+d", 0x44, false, true, false, false)]
    [InlineData("Ctrl+Shift+D", 0x44, true, false, true, false)]
    [InlineData("Control+D", 0x44, true, false, false, false)]
    [InlineData("Win+H", 0x48, false, false, false, true)]
    [InlineData("F9", 0x78, false, false, false, false)]
    [InlineData("Ctrl+F12", 0x7B, true, false, false, false)]
    [InlineData("Escape", 0x1B, false, false, false, false)]
    [InlineData("Enter", 0x0D, false, false, false, false)]
    [InlineData("Space", 0x20, false, false, false, false)]
    [InlineData("Alt+7", 0x37, false, true, false, false)]
    public void Parses(string input, int vk, bool ctrl, bool alt, bool shift, bool win) {
        Assert.True(Hotkey.TryParse(input, out var hk));
        Assert.Equal(vk, hk.Vk);
        Assert.Equal(ctrl, hk.Ctrl);
        Assert.Equal(alt, hk.Alt);
        Assert.Equal(shift, hk.Shift);
        Assert.Equal(win, hk.Win);
        Assert.True(hk.IsValid);
    }

    [Fact]
    public void ModifierOrderDoesNotMatter() {
        Assert.True(Hotkey.TryParse("Alt+Shift+D", out var a));
        Assert.True(Hotkey.TryParse("Shift+Alt+D", out var b));
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Alt")]              // modifier with no key
    [InlineData("Ctrl+Shift")]       // still no key
    [InlineData("Alt+D+E")]          // two non-modifier keys is not a chord
    [InlineData("Alt+Nonsense")]
    [InlineData("F0")]               // function keys start at F1
    [InlineData("F25")]              // and stop at F24
    public void RejectsGarbage(string input) {
        Assert.False(Hotkey.TryParse(input, out var hk));
        Assert.False(hk.IsValid);
    }

    [Theory]
    [InlineData("Alt+D")]
    [InlineData("Ctrl+Alt+Shift+F5")]
    [InlineData("Win+Space")]
    public void RoundTripsThroughToString(string input) {
        Assert.True(Hotkey.TryParse(input, out var first));
        Assert.True(Hotkey.TryParse(first.ToString(), out var second));
        Assert.Equal(first, second);
    }

    [Fact]
    public void SurroundingWhitespaceIsTolerated() {
        Assert.True(Hotkey.TryParse("  Ctrl + Shift + D  ", out var hk));
        Assert.Equal(0x44, hk.Vk);
        Assert.True(hk.Ctrl);
        Assert.True(hk.Shift);
    }

    [Fact]
    public void TheDefaultHotkeyParses() {
        // If this ever fails, a fresh install has no working hotkey at all.
        Assert.True(Hotkey.TryParse(new Settings().Hotkey, out var hk));
        Assert.True(hk.IsValid);
    }

    [Fact]
    public void TheDefaultStopAndCancelKeysParse() {
        var s = new Settings();
        Assert.True(Hotkey.TryParse(s.StopKey, out var stop));
        Assert.True(Hotkey.TryParse(s.CancelKey, out var cancel));
        Assert.Equal(0x0D, stop.Vk);
        Assert.Equal(0x1B, cancel.Vk);
    }
}
