# LangFixer specification

This document is written so that a person **or a coding agent** can rebuild LangFixer from
scratch, or verify that a build still meets the requirements. The reference implementation in
this repository satisfies every item below; `LangFixer.exe --test` and `tools\LangFixerDriver.exe`
are the executable acceptance tests.

## 1. Problem

Bilingual Windows users (Hebrew + English) often type a whole word before noticing the keyboard
layout was wrong: `akuo` instead of `שלום`, or `יקךךם` instead of `hello`. The tool must notice
this at the word boundary, retype the word in the intended language, and switch the layout so
the rest of the sentence comes out right, with no user action.

## 2. Functional requirements

| # | Requirement | Acceptance check |
|---|---|---|
| F1 | Runs as a single standalone `.exe` on Windows 10/11 with no runtime to install. | Builds with the C# compiler shipped in `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319` (C# 5). |
| F2 | Detects a word typed in the wrong layout in **both** directions at Space, Enter or Tab. | Self-test: `akuo` (English layout) → שלום; `hello` typed in Hebrew layout → `hello`. |
| F3 | Retypes the word in the intended language and switches the focused window's layout. | Driver: text in Notepad equals the expected word and the focused thread's layout has changed. |
| F4 | Converts only when the typed rendering **fails** its own language's dictionary **and** the other rendering **passes** the other language's dictionary. | `gradle`, `npm`, slang: untouched. |
| F5 | Dictionaries are the Windows built-in spell checkers (`ISpellChecker`, en-US and he-IL). Falls back to structural rules when a checker is missing and tells the user. | Startup balloon when a checker is missing. |
| F6 | Trailing punctuation is handled: `akuo,` → `שלום,`; `hello,` typed in Hebrew → `hello,`. Punctuation is trimmed only when the key is punctuation in **both** layouts (the W key is `'` in Hebrew, so `how` = `ים'` must not be judged as `ים`). | Self-test rows "comma key", "how typed in Hebrew". |
| F7 | Hebrew targets shorter than 3 letters are not auto-converted (159 of 676 two-letter combinations are Hebrew words, e.g. `js`, `ts`, `sh`). The one-letter English words `I` and `a` typed in Hebrew (`ן`, `ש`) **are** converted. | Self-test rows "2-letter guard", "final nun alone". |
| F8 | If the result would read identically to what is on screen (Shift/Caps in the Hebrew layout already produces Latin: `IATA`, `LON`), do nothing. Otherwise the layout flips under the user. | Self-test rows "LON", "DEADLOCK". |
| F9 | Hotkey **Ctrl+Alt+H**: converts the word being typed; or converts the last word left alone; or undoes the last conversion. An undo adds the original word to a persistent ignore list. | Driver rows "Ctrl+Alt+H force-converts", "undoes it". |
| F10 | Persistent, user-editable lists in `%LOCALAPPDATA%\LangFixer`: `ignore-words.txt` (never convert; seeded with tech tokens such as `api`, `aws`) and `excluded-apps.txt` (process names where auto-fix is off; terminals and IDEs by default). | Self-test rows "ignore list", "excluded app". |
| F11 | Words with digits, keys pressed with Ctrl/Alt/Win, and typing in excluded apps are never judged. A mouse click or window switch clears the word buffer. | Code review + driver. |
| F12 | Optional **spelling autocorrect**, off by default: for a word that is not a layout mistake, accept the dictionary's suggestion only if it is exactly one edit away (wrong/missing/extra/swapped letter), the word is ≥ 3 letters, not capitalized, not all-caps, not ignored. A correction outranks a layout switch only when the edit has a typing signature (swapped adjacent pair, or a QWERTY-neighbour key), so `akuo` still becomes שלום and not `akua`. Grammar is out of scope. | Self-test rows "autocorrect ON/OFF"; driver row `teh` → `the`. |
| F13 | Tray icon plus a small dashboard: Start/Stop, status, dictionary status, live decision feed, Start with Windows, autocorrect checkbox, edit lists. Closing the dashboard hides to the tray; running the exe again brings it back. Single instance. | Manual. |
| F14 | `--test` self-test with no hook installed; `--debug` decision log; `LANGFIXER_HOME` overrides the data folder; `--accept-injected` (test only) treats injected keystrokes as real typing. | `LangFixer.exe --test` prints `ALL PASSED`. |

## 3. Non-functional requirements

- **Privacy**: keep only the current word in memory; write no keystrokes to disk except in `--debug` mode; no network access at all.
- **Latency**: the decision at the word boundary must not stall typing. Verdicts are pre-computed while the word is typed; the hook waits at most 200 ms.
- **Safety**: never delete more than the word just typed; when in doubt, keep the text.

## 4. Architecture of the reference implementation

```
Hooks.cs          WH_KEYBOARD_LL / WH_MOUSE_LL. Ignores LangFixer's own output (dwExtraInfo marker).
Engine.cs         Word buffer state machine; decides swallow/keep at separators; hotkey logic; undo state.
Layouts.cs        Finds the English/Hebrew HKLs; renders raw keys under a layout with ToUnicodeEx;
                  reads the layout of the *focused control's* thread.
Detector.cs       Pure decision rules (F4, F6, F7, F8, F12). Testable without a hook.
SpellCheck.cs     ISpellChecker COM interop + structural rules (final letters ם ן ץ ף ך only at word end).
DecisionService.cs Worker thread that owns the spell checkers; cache; prefetch.
Injector.cs       Worker thread that performs a rewrite: backspaces, layout switch, wait, replay keys.
Settings.cs       The three files in %LOCALAPPDATA%\LangFixer.
TrayApp.cs        Hidden form (owns hooks + hotkey), tray icon, dashboard wiring.
DashboardForm.cs  The small window.
SelfTest.cs       `--test`.
tools/Driver.cs   End-to-end test against a real Notepad tab via UI Automation.
```

## 5. Windows traps you will hit if you rebuild this

Each of these cost real debugging time; a rebuild that ignores them will fail the driver.

1. **A hidden WinForms form never gets a handle.** If `SetVisibleCore` always passes `false`,
   `OnHandleCreated` never fires and hooks, hotkey and tray icon are never created. Call
   `CreateHandle()` explicitly.
2. **No COM calls inside a low-level keyboard hook.** The hook is an input-synchronous callback;
   `ISpellChecker` calls fail with `RPC_E_CANTCALLOUT_ININPUTSYNCCALL`. Run the dictionaries on
   a worker thread and wait on a kernel event.
3. **Windows 11 Notepad has several threads with different layouts.** Read the keyboard layout of
   the thread that owns the *focused* window (`GetGUIThreadInfo().hwndFocus`), not the main window.
4. **Bursts of `KEYEVENTF_UNICODE` render as the last character repeated** in Notepad
   (`ooooo` for `hello`). Replay the user's original physical keys after the layout switch instead;
   use `VkKeyScanEx` for characters the keys cannot produce; Unicode only as a last resort.
5. **Keys sent right after the layout switch are dropped** while the text control resets its
   input context. Confirm the switch by polling the focus thread's layout, then wait ~120 ms.
6. **The hook sees the hotkey chord before `WM_HOTKEY` arrives.** Modifier presses on their own
   must not reset the word buffer or undo state.
7. **A hotkey-triggered rewrite starts while Ctrl+Alt are still held**, and Alt+Backspace is Undo
   in Notepad. Wait for all modifiers to be released before sending anything.
8. **Alt tapped and released activates the app's menu bar** (Alt-tap). If you must inject a lone Alt,
   send a Ctrl press inside it.
9. **The Windows Hebrew checker is permissive**: it accepts `אקי`, `שלוום`, `בג`. Its suggestions
   are weak, so Hebrew autocorrect rarely triggers. Do not rely on it for two-letter words.
10. **Shift in the Hebrew layout produces Latin uppercase**, so acronyms typed inside Hebrew text look
    like "English typed in Hebrew" (F8).
11. **`ToUnicodeEx` can clear dead-key state**; pass flag `4` (Windows 10 1607+) to leave it alone.

## 6. How to verify a build

```
build.cmd                              # produces LangFixer.exe
LangFixer.exe --test                   # offline: layouts, dictionaries, decision rules, worker path
tools\build-driver.cmd
LangFixer.exe --debug --accept-injected --minimized   # test-mode instance, in the background
tools\LangFixerDriver.exe              # types into a fresh Notepad tab; do not touch the keyboard
```

The driver only sends keys while Notepad is the foreground window and refuses to clean up unless
Notepad is in front, so it cannot type into anything else. Run it on an idle desktop.
