# LangFixer

> **Build it yourself** (recommended, since this is a keyboard-hook program): clone, run `build.cmd`,
> done. No SDK needed, the compiler ships with Windows. Or download `LangFixer-x.y.zip` from the
> Releases page. [SPEC.md](SPEC.md) is written so a coding agent can rebuild or extend it.

Standalone Windows tray utility that notices when you typed a Hebrew word while the
English keyboard layout was active (or an English word while Hebrew was active),
retypes the word in the language you meant, and switches the keyboard layout so the
rest of the sentence comes out right.

Single `LangFixer.exe`, no installer, no runtime to install (uses the .NET Framework 4.8
that ships with Windows 10/11).

## How it works

1. A low-level keyboard hook records the physical keys of the word you are typing.
2. When you press Space, Enter or Tab the word is rendered under **both** the English and
   the Hebrew layout (`akuo` and `שלום` for the same keys).
3. The word is converted only when the rendering in the layout you typed in **fails** its
   language's dictionary **and** the other rendering **passes** the other language's
   dictionary. Identifiers (`gradle`, `npm`), slang and abbreviations are left alone.
   Dictionaries are the Windows built-in spell checkers for English and Hebrew.
4. Fix = backspace the word, post a layout-change request to the focused window, wait until
   the layout really changed, then **replay your original keystrokes** so the application
   renders them itself in the new layout, then re-send the Space/Enter/Tab you pressed.
   Synthetic Unicode input is used only for characters the keys cannot produce (a trailing
   comma kept as punctuation), because Windows 11 Notepad renders bursts of synthetic
   characters as the last one repeated.

Extra guards: words containing digits are ignored, Ctrl/Alt/Win combinations are ignored,
a mouse click or a window switch clears the word buffer, and Hebrew targets shorter than
3 letters are not auto-converted: 159 of the 676 two-letter combinations spell real Hebrew
words, including `js`, `ts`, `sh`, `ps`, `st`, `th`. The one-letter English words `I` and
`a` typed in Hebrew layout (`ן`, `ש`) are converted.

Implementation notes that cost real debugging time:

- The hidden tray form must call `CreateHandle()` in `SetVisibleCore`; a form that is never
  shown never gets a handle, so hooks, hotkey and tray icon were never created.
- A low-level keyboard hook is an input-synchronous callback and COM calls from it fail with
  `RPC_E_CANTCALLOUT_ININPUTSYNCCALL`. The spell checkers live on a worker thread
  (`DecisionService`); the hook waits on an event, and verdicts are pre-fetched while you type.
- Windows 11 Notepad runs its text control on a different thread than its main window, and
  each thread has its own layout. Read the layout of the focused control's thread
  (`Layouts.HklOfWindow`), not the main window's.
- LangFixer's own output is stamped with `dwExtraInfo` so the hook ignores it; keys you type
  during a rewrite are parked and replayed afterwards so nothing interleaves.
- After the layout switch is confirmed, wait ~60 ms before replaying keys: the text control
  resets its input context and drops keys sent inside that window (words came out truncated).
- The hook sees the Ctrl+Alt+H chord before WM_HOTKEY arrives. Modifier presses on their own
  must not reset the word buffer or undo state, and the chord itself is exempt.
- A hotkey-triggered rewrite starts while Ctrl+Alt are still held; Alt+Backspace is Undo in
  Notepad, so the injector waits for all modifiers to be released first.
- RegisterHotKey swallows the H, so the foreground app sees Alt pressed and released with
  nothing in between: an Alt tap, which opens its menu bar and then eats the replayed keys
  (screenshot showed Notepad's File menu open, and "N" had created a new tab). The hotkey
  handler injects a harmless Ctrl press while Alt is still held (AutoHotkey's "mask key").
  It must be sent from the WM_HOTKEY handler, not from inside the hook: input sent from a hook
  callback lands *ahead* of the hooked key, and Windows then sees Ctrl released when H arrives.
- Feeding the hidden dashboard from the log must only buffer lines until the window has been
  shown; touching its controls creates the window handle as a side effect, on whatever thread
  happens to log.
- Shift/Caps in the Hebrew layout already produce Latin letters. If the "fix" would read
  identically to what is on screen (acronyms like `IATA` inside Hebrew text), do nothing,
  otherwise the layout flips under the user's hands.
- The W key renders as a geresh (') in Hebrew, so `how` typed in Hebrew is `ים'`. Trailing
  punctuation is only trimmed when the key is punctuation in both layouts, else `ים` (sea)
  passes the dictionary and the word is missed.

## Spelling autocorrect (optional, off by default)

The dashboard checkbox **Auto-correct spelling mistakes** turns on dictionary-based autocorrect
for words that are *not* a layout mistake. It is deliberately narrow:

- the word must be letters only, at least 3 long, not capitalized (names) and not all-caps;
- the Windows spell checker's suggestion is taken only if it is exactly one edit away: one
  wrong, missing, extra or swapped letter (`teh` → `the`, `helo` → `hello`, `hwllo` → `hello`);
- a correction outranks a layout switch only when the edit has a typing signature, a swapped
  adjacent pair or a neighbouring key on the QWERTY board. Otherwise the layout fix wins, so
  `akuo` still becomes שלום and not the dictionary's `akua`, and `thl` becomes איך, not `the`;
- Ctrl+Alt+H undoes a correction and adds the original word to the ignore list.

Hebrew autocorrect uses the same rules, but the Windows Hebrew checker is permissive (it accepts
`שלוום`) and its suggestions are weak, so it rarely triggers. **Grammar is not attempted**: a
dictionary cannot judge grammar; that needs a language model.

## Hotkey: Ctrl+Alt+H

- While typing a word: convert it to the other layout right now, no dictionary check.
- Right after a word the auto-fix left alone: convert it anyway.
- Right after an auto-fix: undo it (restores the original text and layout) **and** adds the
  word to the ignore list so it is never auto-converted again.

## Lists (in `%LOCALAPPDATA%\LangFixer`)

- `ignore-words.txt`: words never auto-converted. Seeded with tech tokens the Windows
  dictionary does not know but which spell real Hebrew words (`api` is שפן, `aws` is שוד).
  Every undo appends to it. Edit from the tray menu; the file is reloaded when Notepad closes.
- `excluded-apps.txt`: process names where auto-fix is off (terminals and IDEs by default,
  where most "words" are identifiers). The hotkey still works there.

## Dashboard and tray

Launching `LangFixer.exe` opens a small dashboard: Start/Stop button, dictionary and layout
status, count of words fixed, a live feed of every decision, the Start-with-Windows toggle and
buttons for the ignore/exclusion lists and the log folder. Closing the window hides it to the
tray; the app keeps running. Left-click the tray icon (or launch the exe again) to reopen it.

Tray menu: **Open dashboard**, **Auto-fix enabled**, **Start with Windows** (HKCU Run entry
with `--minimized`), dictionary status, list editors, **Exit**.

`LangFixer.exe --minimized` starts straight into the tray without the dashboard.

If the English or Hebrew spell checker is missing Windows shows a balloon tip at startup.
Install it via Settings > Time & language > Language > (language) > Options > Basic typing.
Without the Hebrew checker the tool falls back to structural rules (final letters ם ן ץ ף ך
only at word end).

## Build

```
build.cmd
```

Uses `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (C# 5). Output: `LangFixer.exe`.

## Self-test

```
LangFixer.exe --test
```

Runs rendering, dictionary and decision checks on this machine without installing any hook
and writes `test-output.txt` next to the exe. It also sweeps every 2- and 3-letter
English-layout string and lists which ones would be converted, so you can see the
false-positive surface for your dictionaries.

## Debug log

```
LangFixer.exe --debug
```

Writes each decision (word, both renderings, verdict, foreground process) to
`%LOCALAPPDATA%\LangFixer\log.txt`. `LANGFIXER_HOME=<dir>` relocates the lists and the log.

## End-to-end test

```
tools\build-driver.cmd
LangFixer.exe --debug --accept-injected --minimized      (in the background)
tools\LangFixerDriver.exe
```

The driver opens a fresh Notepad tab, types the scenarios with SendInput and asserts the
resulting text and layout through UI Automation. `--accept-injected` makes LangFixer treat
injected keystrokes as real typing; never use it in normal operation. Do not touch the
keyboard or mouse while it runs: the keystrokes go to whatever window has focus.

## Known limits

- Apps that auto-correct or auto-complete as you type (Word, some IDEs) can shift the text
  under the caret before the fix lands, so the backspaces may hit the wrong characters.
- Applications running elevated (as Administrator) do not receive input from a
  non-elevated LangFixer. Run LangFixer elevated too if you need it there.
- Only Space, Enter and Tab trigger the check. Other punctuation ends the word silently.
