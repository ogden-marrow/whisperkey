# End-to-end verification

Run on the development machine: i7-11800H (8c/16t), 32 GB RAM, RTX 3050 Ti,
Windows 11 26200. Every row below is from a trace log of the real app, not a
unit test and not a description of intended behaviour.

## The focus gate

This is the rule the whole app depends on: the hotkey is captured only when the
caret is genuinely in an editable text field, and passes straight through
otherwise.

| Target | ControlType | Editable | Right answer? |
|---|---|---|---|
| Notepad document | 50030 Document | **true** | yes |
| Start menu search box | 50004 Edit | **true** | yes |
| WinForms text box | 50004 Edit | **true** | yes |
| Claude desktop chat input (Electron/Chromium) | 50004 Edit | **true** | yes |
| **WinForms password box** | 50004 Edit | **false** | **yes — excluded** |
| Browser page body | 50030 Document | false | yes, it is read-only |
| **Explorer file list** | 50007 ListItem | **false** | **yes — see below** |
| Explorer / desktop panes | 50032, 50033 | false | yes |
| Buttons | 50000 Button | false | yes |

Two rows matter more than the rest.

**The password box and the normal text box are both ControlType 50004.** Nothing
about the control type distinguishes them; only `IsPassword` does. Pressing the
hotkey in the password box produced no start event and left the field empty.

**The Explorer file list is the case that broke the original design.** It
advertises TextPattern and reports `ValueIsReadOnly=false`, so the rule as first
specified — "TextPattern available, or ValuePattern not read-only" — classified a
file list as a text box. Gating on ControlType fixes it. This is written up in
[ADR 0002](adr/0002-ui-stack.md).

## Pass-through

The best evidence is accidental. With focus in an Explorer window, pressing Alt+D
produced no start event, and focus moved to ControlType 50004 — because **Alt+D is
Explorer's own shortcut for focusing the address bar**, and it fired normally. The
keystroke reached Explorer completely untouched.

## The full dictation path

Traced through the real app, from a clean start with no model on disk:

| Step | Result |
|---|---|
| Model download and extract | 482 MB, automatic, in the background |
| Model load | 2468 ms, off the UI thread |
| Hotkey to microphone live | 113.9 ms |
| Captured from the microphone | 13.32 s |
| Transcribed locally | 697 ms |
| Inserted into the focused field | verbatim, matching the transcript exactly |

Also verified: **Enter** stops and inserts without typing a newline into the
target, **Esc** cancels and inserts nothing, and neither key reaches the
application underneath while recording.

## Text insertion

Exact string comparison against a controlled text box:

| Sent | Result |
|---|---|
| `abc def ghi` | exact match |
| `The quick bread box just in the` | exact match |
| `Hello, world - it's 42% done.` | exact match |

Punctuation, apostrophes and `%` all survive.

## Installer

| Step | Result |
|---|---|
| Install | exit 0, per-user, no elevation prompt |
| Add/Remove Programs | Whisperkey 0.1.0, publisher, 26.2 MB, install location, help link |
| Installed app | runs, loads the model |
| Uninstall | exit 0 |
| After uninstall | app folder, Start Menu entry and Run key all gone |
| User data after uninstall | config and the model correctly left in place |

Known gap: the Add/Remove Programs entry has no icon
([#34](https://github.com/ogden-marrow/whisperkey/issues/34)). Cosmetic only.

## What was not verified

Being explicit about this rather than implying wider coverage than exists.

- **Transcription accuracy.** The rig played speech through the laptop speakers
  into its own microphone, which is a poor signal — the pipeline was proven, but
  the words were not. Engine accuracy is measured properly in
  [ADR 0001](adr/0001-stt-engine.md) against a known transcript.
- **A second monitor.** The DPI and work-area clamping code paths are written and
  reasoned about, but this machine has one display, so the flip-and-clamp
  behaviour has not been seen on a real multi-monitor setup.
- **Screen reader announcements.** The UIA notification path is implemented and
  gated on `UiaClientsAreListening`, but it has not been listened to with Narrator
  actually running.
- **High contrast themes.** The code path exists and is exercised by the same
  render function, but no high contrast theme was switched on.
