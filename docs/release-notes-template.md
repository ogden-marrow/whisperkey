## Whisperkey __TAG__

Local dictation into any Windows text field. Press the hotkey with your caret in a
text box, talk, press Enter. Nothing leaves your machine.

### Install

**Recommended:** download the `.msi` and run it. It installs per-user, so there is
no admin prompt, adds a Start Menu entry, and uninstalls cleanly from Settings.

Or take the `.zip`, unblock it, and run `Whisperkey.exe` directly. It lives in the
tray. The exe needs the two DLLs beside it, which is why there is a zip.

On first run it downloads the speech model, about 465 MB, into `%LOCALAPPDATA%`.
That happens once, in the background, with progress in the tray.

### About the SmartScreen warning

**These builds are not code-signed**, so Windows will show "Windows protected your
PC" the first time you run it. That is expected rather than a sign anything is
wrong. Signing costs money and has real eligibility hurdles for individuals, so
v0.x ships unsigned rather than pretending otherwise — the reasoning is in
[#25](https://github.com/ogden-marrow/whisperkey/issues/25).

Verify what you downloaded against these checksums if you would like to:

```
__SUMS__
```

### Changes

__CHANGES__
