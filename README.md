# Whisperkey

Press a key. Talk. Your words appear in the text box.

A small Windows tray app that dictates into any text field, using a speech model
that runs entirely on your machine. Nothing is uploaded, and there is no account.

The whole app is one microphone that appears next to your caret while it listens.
There is no window, no settings screen, and nothing in the taskbar.

## How it works

1. Put your caret in a text box — anywhere. Notepad, a browser input, Slack, a
   search box.
2. Press **Alt+D**. A small microphone appears next to the caret and starts
   listening.
3. Talk.
4. Press **Enter**. The text is typed in where your caret is.

**Esc** cancels and inserts nothing. **Double-Esc** always releases the keyboard
hook, whatever state the app is in.

The hotkey only does anything when your caret is genuinely in an editable text
field. Anywhere else the keystroke passes straight through untouched, so Alt+D
still focuses Explorer's address bar and Ctrl+D still works in your editor.
Password fields are always excluded.

## Install

Download the `.msi` from [Releases](https://github.com/ogden-marrow/whisperkey/releases)
and run it. It installs per-user, so there is no admin prompt.

```
winget install --id ogden-marrow.Whisperkey
```

*(Not on winget yet — download the MSI for now.)*

On first run it downloads the speech model, about 465 MB, into `%LOCALAPPDATA%`.
That happens once, in the background, with progress in the tray.

**These builds are not code-signed**, so Windows will show "Windows protected your
PC" the first time. That is expected. Every release publishes SHA256 checksums if
you want to verify what you downloaded. See
[#25](https://github.com/ogden-marrow/whisperkey/issues/25) for why.

## Measured on real hardware

Everything below was measured on the development machine — i7-11800H (8c/16t),
32 GB RAM, Windows 11 26200 — not estimated.

| | |
|---|---|
| Process start to tray ready | **38 ms** (median of 7; min 34, max 47) |
| Hotkey to microphone live | **109 ms** (median of 8; min 105, max 113) |
| Transcribing a 12.3 s clip | **747 ms** |
| Idle CPU, model warm | **0.36% of one core** (0.109 CPU-seconds over 30 s) |
| Idle memory, model warm | **722 MB** working set |
| On disk | **26.2 MB**, 3 files |

The one number that is not small is memory. Parakeet is held in RAM permanently so
that pressing the hotkey is instant rather than a two-second wait. On a 32 GB
machine that is about 3% of RAM and a fair trade; on an 8 GB machine it is not.
`model` in the config picks a lighter one.

### Why NativeAOT

Measured against the same app published ReadyToRun:

| | NativeAOT | ReadyToRun |
|---|---|---|
| Start to tray ready | **38 ms** | 103 ms |
| On disk | **26.2 MB** | 95.7 MB |
| Files | **3** | 192 |

### Why Parakeet and not Whisper

Whisper large-v3-turbo took **16.7 seconds** to transcribe a 12.3 second clip on
this CPU — slower than real time. It is a GPU model, and putting it on the GPU
would drag a CUDA runtime into a tray app. Parakeet does the same clip in 747 ms.

Full comparison: [ADR 0001](docs/adr/0001-stt-engine.md).

## Configuration

One JSON file at `%APPDATA%\Whisperkey\config.json`, reloaded when you save it.
There is no settings UI on purpose. Open it from the tray menu.

```jsonc
{
  "hotkey": "Alt+D",           // "Ctrl+Shift+D", "F9", ...
  "stopKey": "Enter",
  "cancelKey": "Escape",
  "requireTextField": true,    // false makes the hotkey global
  "model": "parakeet-0.6b-v3", // or "parakeet-110m-en"
  "language": "en",
  "device": "default",         // or a waveIn device index
  "insertionMode": "auto",     // auto | sendinput | clipboard
  "accent": "system"           // or "#0078D4"
}
```

Comments, trailing commas and any capitalisation are accepted, because it is a
file meant to be edited by hand.

`accent` exists because "use the system accent colour" produces a grey microphone
on a machine whose accent is grey.

## Design decisions

- [ADR 0001 — the speech engine](docs/adr/0001-stt-engine.md)
- [ADR 0002 — the UI stack](docs/adr/0002-ui-stack.md)

Both are written up with the numbers that decided them, including the two things
that were measured and turned out to contradict the original plan.

## Building

Requires the .NET 9 SDK and Visual Studio Build Tools (NativeAOT needs the MSVC
linker).

```bash
dotnet publish src/Whisperkey/Whisperkey.csproj -c Release
```

If the publish fails with `'vswhere.exe' is not recognized`, add the VS Installer
directory to `PATH`:

```
%ProgramFiles(x86)%\Microsoft Visual Studio\Installer
```

The MSI:

```bash
dotnet build installer/Whisperkey.wixproj -c Release
```

Set `WHISPERKEY_TRACE` to a file path to get a trace log. It costs nothing when
unset.

## Privacy

Audio never leaves the machine. The only network request the app ever makes is
downloading the speech model on first run, from GitHub.

## Licence

MIT.
