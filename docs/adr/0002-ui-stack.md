# ADR 0002 — UI stack

Status: accepted · Issue #13

## Decision

**Raw Win32 via P/Invoke, published with NativeAOT.** Not WinUI 3, not WPF.

## Measured cold start

Both candidates were built as a minimal tray-ready app that registers a tray icon and
timestamps `Process.StartTime` to that moment. 12 runs each, first two discarded as
file-cache warm-up, self-contained publish.

| Stack | Cold start to tray ready | Publish size | Files |
|---|---|---|---|
| Win32 + NativeAOT | **16 ms** (min 15, max 22) | **9.2 MB** | 2 |
| WPF + ReadyToRun, self-contained | 481 ms (min 452, max 907) | 134.5 MB | 394 |

That is a 30x difference in startup and a 15x difference in size, for an app whose
entire visible UI is one rounded pill.

WinUI 3 was not measured because the Windows App SDK workload is not even installed
on this machine. That is the point: it would add a workload dependency, a larger
runtime, and a heavier deployment story, to lose to WPF on startup — which already
loses to Win32 by 30x. There is no path where it wins here.

WPF's 907 ms worst case is also worse than its median by nearly half a second. For a
tray app that starts with Windows, a startup that occasionally takes almost a second
is exactly the kind of thing that makes a machine feel slow at login.

## The risk that had to be cleared first

Choosing raw Win32 is only sane if UI Automation still works, because the focus gate
in #4 depends on it, and NativeAOT does not support classic COM interop. It works,
using source-generated COM (`[GeneratedComInterface]`):

- `CoCreateInstance(CLSID_CUIAutomation)` under NativeAOT: **6-7 ms**.
- `GetFocusedElement` plus four property reads: **2-8 ms** typical, **51 ms** worst
  case observed.

Two things cost real time to discover and are worth writing down:

**The UIA thread must be MTA.** Initializing with `COINIT_APARTMENTTHREADED` makes
every cross-process UIA call block forever waiting on a message pump that a
background thread does not have. It does not fail, it hangs. Use
`COINIT_MULTITHREADED`.

**Hand-written COM vtables have to be exactly right.** Declaring the wrong number of
placeholder slots before the method you want silently calls a different function
pointer and hangs. `IUIAutomation` has 2 methods before `GetRootElement`;
`IUIAutomationElement` has 7 before `GetCurrentPropertyValue`.

## Consequences

- NativeAOT publish requires the MSVC linker. It fails with a confusing `vswhere.exe
  is not recognized` error unless the VS Installer directory is on `PATH`. This needs
  to be handled in the build docs and any CI.
- Fluent look is achieved through the DWM APIs directly rather than a framework:
  `DWMWA_SYSTEMBACKDROP_TYPE`, `DWMWA_WINDOW_CORNER_PREFERENCE`, Segoe Fluent Icons,
  and the accent colour from the theme. Confirmed: the overlay is a transient popup,
  so `DWMSBT_TRANSIENTWINDOW` (Acrylic) is correct, not Mica, which is documented for
  long-lived main windows.
- The tray context menu will be a real Win32 `TrackPopupMenu`, which is the actual
  native control rather than an imitation of one.
- Custom drawing for the pill is on us. That is a small, contained cost for one
  widget, and it is the price of the 16 ms.

## The finding that changes another issue

While validating the focus gate, the detection rule written into #4 turned out to be
**wrong**. Measured across three apps:

| Focused element | ControlType | TextPattern | ValueReadOnly | Naive rule | Correct? |
|---|---|---|---|---|---|
| Explorer file list | 50007 ListItem | true | false | **editable** | **no** |
| Notepad body | 50030 Document | true | false | editable | yes |
| Start menu search | 50004 Edit | true | false | editable | yes |

An Explorer file list item advertises TextPattern and reports `ValueIsReadOnly=false`,
so "TextPattern available, or ValuePattern not read-only" classifies your file list as
a text box. The hotkey would have been swallowed in Explorer — precisely the
pass-through bug the focus gate exists to prevent.

The rule must gate on **ControlType** as well: Edit (50004), Document (50030), or an
editable ComboBox (50003), and not a password field, and keyboard-focusable. #4 has
been updated.

Note also the 51 ms worst-case property read. That confirms the requirement to cache
the answer on focus-change events: doing this work inside the keyboard hook would put
a visible stall on a keystroke.
