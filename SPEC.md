# LangFixer — complete build specification

This document is sufficient to build LangFixer from nothing. It is written for a coding agent
(or a person) who has **only this file**, a Windows 10/11 machine with the English and Hebrew
keyboard layouts installed, and no SDK. Everything below was learned by building and testing the
reference implementation in this repository; where a Windows behaviour surprised us, it is
spelled out so you do not have to rediscover it.

Work test-first (section 10): write the offline self-test from the vectors in section 9, watch it
fail, then implement until it prints `ALL PASSED`. Then run the Notepad driver (section 9.4).

---

## 1. Problem

Bilingual Windows users (Hebrew + English) type a whole word before noticing the keyboard layout
was wrong: `akuo` instead of `שלום`, or `יקךךם` instead of `hello`. The tool notices this at the
word boundary, retypes the word in the intended language, and switches the layout so the rest of
the sentence comes out right, with no user action.

## 2. Deliverable and constraints

- One standalone `LangFixer.exe`, C#, targeting .NET Framework 4.8, compiled by the compiler that
  ships with Windows: `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. That compiler is
  **C# 5**: no string interpolation, no `?.`, no `nameof`, no expression-bodied members, no
  `out var`. Compile with `/target:winexe /platform:anycpu /codepage:65001` and reference
  `System.dll System.Drawing.dll System.Windows.Forms.dll`. Put Hebrew literals in source only if
  you pass `/codepage:65001`; otherwise use `\uXXXX` escapes.
- No installer, no runtime download, no network code at all.
- Data folder: `%LOCALAPPDATA%\LangFixer`, overridable by the `LANGFIXER_HOME` environment variable.
- Single instance (named mutex). Running the exe again brings the dashboard to the front.
- Command-line flags: `--test` (offline self-test, no hook), `--debug` (decision log to
  `<data>\log.txt`), `--minimized` (start hidden in the tray), `--accept-injected` (test only:
  treat injected keystrokes from other programs as real typing).

## 3. Functional requirements

| # | Requirement |
|---|---|
| F1 | Detect a word typed in the wrong layout, in both directions, when Space, Enter or Tab is pressed. |
| F2 | Retype the word in the intended language and switch the **focused window's** layout. Re-send the separator the user pressed. |
| F3 | Convert only when the typed rendering **fails** its own language's dictionary **and** the other rendering **passes** the other language's dictionary. Identifiers (`gradle`, `npm`) and slang stay. |
| F4 | Dictionaries: the Windows spell checkers (`ISpellChecker`, `en-US` and `he-IL`). If one is missing, fall back to structural rules and tell the user (balloon tip + dashboard status). |
| F5 | Trailing punctuation: `akuo,` → `שלום,`; `akuo/` → `שלום.`; `hello,` typed in Hebrew → `hello,`. Trim a trailing char only when the key is punctuation in **both** layouts (section 5.3). |
| F6 | Hebrew targets shorter than 3 letters are never auto-converted. The one-letter English words `I`/`a` typed in Hebrew (`ן`/`ש`) are converted. |
| F7 | If the result would read identically to what is already on screen, do nothing (Shift in the Hebrew layout already produces Latin: `IATA`, `LON`). |
| F8 | Hotkey Ctrl+Alt+H: convert the word being typed / convert the last word left alone / undo the last conversion (section 5.6). Undo adds the original word to the ignore list. |
| F9 | Persistent lists (section 6): `ignore-words.txt`, `excluded-apps.txt`, `settings.txt`. |
| F10 | Never judge: words containing digits, keys pressed with Ctrl/Alt/Win held, typing in excluded apps. A mouse click or a foreground-window change clears the word buffer and the undo state. |
| F11 | Optional spelling autocorrect, off by default (section 5.5). Grammar is out of scope. |
| F12 | Tray icon + dashboard (section 7). |
| F13 | Own output must never be re-processed by the hook; keys the user types during a rewrite must not interleave with it (section 5.7). |
| F14 | Privacy: keep only the current word in memory; write keystrokes to disk only in `--debug`. |

## 4. Architecture (one file per box is a good split)

```
Native        P/Invoke declarations and structs (section 8).
Layouts       Find the English/Hebrew HKLs; render raw keys under a layout; layout of the focus thread.
KeyRec        One physical key: vk, scan code, shift, caps, and the text it actually produced.
Hooks         WH_KEYBOARD_LL + WH_MOUSE_LL; marker handling; hands physical key-downs to Engine.
Engine        Word buffer state machine; separator handling; hotkey logic; undo state.
Detector      Pure decision rules (5.2–5.5). No Win32 calls: unit-testable.
SpellCheck    ISpellChecker COM interop + structural rules + Suggest.
DecisionService  Worker thread owning the spell checkers; cache; prefetch (5.8).
Injector      Worker thread performing rewrites (5.7).
Settings      The three files in the data folder; thread-safe reads.
TrayApp       Hidden form owning hooks + hotkey; tray icon; single instance; dashboard wiring.
DashboardForm The small window (section 7).
SelfTest      `--test` (section 9).
tools/Driver  End-to-end Notepad test (section 9.4), a separate console exe.
```

## 5. Behaviour, precisely

### 5.1 Word buffer (Engine)

Virtual-key classes:

- **Word keys**: `A`–`Z` (0x41–0x5A), `;` (0xBA), `,` (0xBC), `.` (0xBE), `/` (0xBF), `'` (0xDE).
  These five punctuation keys are Hebrew letters or Hebrew punctuation in the Hebrew layout
  (`;`→ף, `,`→ת, `.`→ץ, `/`→`.`, `'`→`,`), so they are part of a word.
- **Separators**: Space (0x20), Enter (0x0D), Tab (0x09). Trigger the decision.
- **Digits** (0x30–0x39, numpad 0x60–0x69): mark the current word *tainted* (never converted).
- **Ignored** (no effect on the buffer): Shift, Ctrl, Alt, Caps Lock, Win, their L/R variants
  (0xA0–0xA5), Num Lock, Scroll Lock. **This matters**: the hook sees the Ctrl and Alt presses of
  the hotkey chord before `WM_HOTKEY` arrives; if they reset the buffer, the hotkey acts on nothing.
- **Backspace**: pop the last key from the buffer; if the buffer is empty, clear the undo state.
- **Anything else** (arrows, Home/End, Delete, Esc, F-keys, other punctuation): clear buffer and undo state.

Rules on every physical key-down:

1. If the foreground window changed since the last key: remember it, look up its process name
   (`OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `QueryFullProcessImageName`), compute
   *excluded* = process name is in `excluded-apps.txt`, clear buffer and undo state.
2. Ignored keys: pass through, no state change.
3. If Ctrl, Alt or Win is physically down: **unless** this is the hotkey chord itself
   (vk == `H` with Ctrl and Alt down), clear buffer and undo state, pass through.
4. Word key: `hkl` = layout of the focus thread (5.9). If it is neither English nor Hebrew, clear
   and pass. If the buffer is empty, remember `hkl` as the word's layout; if it differs from the
   word's layout, clear and start a new word. Record `KeyRec{vk, scan, shift = Shift is down,
   caps = Caps Lock toggled, typed = Render(key, hkl)}`. Clear the undo state. If enabled and not
   excluded and not tainted, **prefetch** a verdict for the buffer so far (5.8). Pass through.
5. Digit: taint. Pass through.
6. Separator with a non-empty buffer: snapshot the keys, clear the buffer. If tainted, disabled,
   excluded, or the layouts are incomplete: remember `LastAction = Skipped{keys, layout, sepVk}`
   and pass through. Otherwise ask the DecisionService (5.8) with `typedIn`, `typed`, `english`,
   `hebrew` renderings and a 200 ms timeout. If *keep*: `LastAction = Skipped`, pass through, log
   the reason. If *fix*: **swallow the separator** (return 1 from the hook), enqueue a rewrite
   (5.7) with `backspaces = typed.Length`, `hkl = target layout`, `text`, `keys`, `sepVk`;
   `LastAction = Fixed{keys, typedLayout, fixedLayout, text, sepVk}`; increment the fix counter.
7. Separator with an empty buffer: **no-op** (buffer and undo state untouched, so Ctrl+Alt+H still
   undoes after `word␣␣`; the undo then leaves the extra space). Other keys: as classified above.

### 5.2 Rendering

- HKL discovery: `GetKeyboardLayoutList`; the language id is the low 16 bits of the HKL, its
  primary language is `langid & 0x3FF`: `0x09` = English, `0x0D` = Hebrew. Take the first of each.
- `Render(key, hkl)`: `ToUnicodeEx(vk, scan, keyState, buffer, 16, flags = 4, hkl)` where
  `keyState[VK_SHIFT] = 0x80` if shift, `keyState[VK_CAPITAL] = 0x01` if caps. Flag `4` (Windows 10
  1607+) means "do not change keyboard state", so dead-key state of the real thread is untouched.
  Return `""` for results ≤ 0.
- `english` = concat of Render under the English HKL; `hebrew` = under the Hebrew HKL;
  `typed` = concat of each key's recorded `typed`.

### 5.3 Decision (Detector) — the heart

Definitions. Punctuation set `P = { , . ; ' / " }`. `TrimTrailing(s)` removes trailing chars in
`P`. `TrimTrailingAligned(s, other)`: when `s` and `other` have equal length, remove trailing
chars only while **both** `s[i]` and `other[i]` are in `P`; otherwise fall back to `TrimTrailing`.
Why: the W key is `'` (geresh) in Hebrew, so `how` typed in Hebrew is `ים'`; plain trimming
would judge `ים` (a real word) and miss the fix.

Structural rules (SpellCheck):

- `IsStructurallyEnglish(w)`: length ≥ 2; only `A–Z a–z`, plus `'` in an inner position.
- `IsStructurallyHebrew(w)`: length ≥ 2; only Hebrew letters `א–ת` (U+05D0–U+05EA), plus `'`, `"`,
  `׳`, `״` in an inner position; final forms `ך ם ן ף ץ` only as the last char; the last char may
  not be `כ מ נ צ` (non-final forms; `פ` is allowed because loanwords end in it).
- `IsValidEnglish(w)` = structurally English AND (spell checker reports no error, or when the
  checker is missing: contains a vowel `aeiouy`).
- `IsValidHebrew(w)` = structurally Hebrew AND (spell checker reports no error, or when missing: true).

`Decide(typedIn, typed, english, hebrew)`:

**Typed while English layout was active** (did they mean Hebrew?):

```
en    = TrimTrailing(english)                 # if they meant English, trailing , . ; ' are punctuation
if en in ignoreList            -> keep "ignored word"
if IsValidEnglish(en)          -> keep "valid English"
if the checker call failed     -> keep "dictionary fault"       # never convert on a checker failure
trail = english[len(en):]
he = TrimTrailingAligned(hebrew, english)
heValid = len(he) >= 3 and IsValidHebrew(he)
lowercaseUnknown = IsStructurallyEnglish(en) and en is all lowercase
if names_guard and prevUnknown and lowercaseUnknown and not (heValid and len(he) >= 4)
                               -> keep "names guard", Unknown = true      # "tal" after a name stays; "eurv" (קורה) still converts
corrected, strong = TryAutoCorrect(English, en)          # 5.5; null when autocorrect is off
# priority, measured on real typing:
if heValid and len(he) >= 4    -> FIX Hebrew, text = hebrew            # "eurv" is קורה, not "eruv"
if corrected and strong        -> FIX English, text = corrected + trail   # "teh" -> "the" beats אקי
if heValid                     -> FIX Hebrew, text = hebrew            # 3-letter Hebrew word
if trail != "" and len(trail) < len(hebrew):
     hePrefix = TrimTrailing(hebrew[:len(hebrew)-len(trail)])   # they pressed the real , or . key after a Hebrew word
     if len(hePrefix) >= 3 and IsValidHebrew(hePrefix) -> FIX Hebrew, text = hePrefix + trail
heRepaired = CrossLayoutRepair(Hebrew, he)                # 5.5b: "chsev" -> בידקה is one swapped pair from בדיקה
if heRepaired                  -> FIX Hebrew, text = heRepaired + hebrew[len(he):]   ("cross-layout typo")
if corrected                   -> FIX English, text = corrected + trail   # weak, aggressive mode only
keep ("Hebrew too short" | "not Hebrew"), Unknown = lowercaseUnknown and len(he) >= 3
```

The Hebrew branch mirrors this with `CrossLayoutRepair(English, en)` after the `enValid` check
("hlelo" typed on the Hebrew layout → `hello`).

### 5.5b Cross-layout typo repair

`CrossLayoutRepair(target, rendering)`: requires `autocorrect = 1` and a rendering of 4+ letters
made only of the target alphabet. **Generate** the typing-signature neighbours of the rendering
yourself (every adjacent transposition; for English also every QWERTY neighbour-key substitution)
and collect the ones the target dictionary accepts. Exactly one → use it. Several (the Hebrew checker
also accepts יבדקה) → keep only candidates on a small built-in list of everyday Hebrew words
(`CommonWords.cs`, a few hundred entries); exactly one left → use it, otherwise leave the word.
Do not rely on the checker's suggestions: the Windows Hebrew checker does not suggest בדיקה for
בידקה, but it confirms בדיקה is a word. Weak
edits never apply across layouts, and the per-language Hebrew autocorrect gate does not apply here:
the layout switch is the main evidence and the edit is strong. Result: the word is converted to the
other layout *and* corrected in one rewrite.

`Unknown` on a kept decision means the word failed both dictionaries; the Engine remembers it as
`prevUnknown` for the next word in the same window (cleared by a fix, a click or a window change).

**Typed while Hebrew layout was active** (did they mean English?):

```
he = TrimTrailingAligned(hebrew, english)
if he in ignoreList            -> keep
if IsValidHebrew(he)           -> keep "valid Hebrew"          # structurally needs len >= 2 anyway
if the checker call failed     -> keep "dictionary fault"
heTrail = hebrew[len(he):]
en = TrimTrailing(english); enValid = len(en) >= 2 and IsValidEnglish(en)
if enValid and len(en) < 6 and en not in CommonEnglish -> keep "rare short English word", Unknown = true
                               # the English checker accepts "hyuk"; the Hebrew slip יטולת became "hyuk,"
corrected, strong = (names_guard and prevUnknown) ? null : TryAutoCorrect(Hebrew, he)   # the guard only withholds autocorrect here
if enValid and len(en) >= 4    -> FIX English, text = english
if corrected and strong        -> FIX Hebrew, text = corrected + heTrail
if en in {"i","I","a","A"}     -> FIX English, text = english          # ן / ש alone are never words
if enValid                     -> FIX English, text = english
enRepaired = CrossLayoutRepair(English, en)              # 5.5b: "hlelo" on the Hebrew layout -> hello
if enRepaired                  -> FIX English, text = enRepaired + english[len(en):]
if corrected                   -> FIX Hebrew, text = corrected + heTrail
keep ("English too short" | "not English"), Unknown = len(en) >= 2 and len(he) >= 2
```

`CommonEnglish` is a built-in list of a few hundred everyday English words including contractions
(`it's`, `don't`), case-insensitive (`CommonWords.cs`).

**After either branch**, before returning a FIX: if `text == typed` → keep "already reads as the
target text" (F7). Shift+letter in the Hebrew layout yields Latin uppercase, so `LON` typed inside
Hebrew text renders as `LON` under both layouts; converting it would only flip the layout.

Every keep/fix carries a human-readable reason; the debug log prints it with the process name.

### 5.4 Forced conversion text (hotkey, no dictionary)

`ForcedText(target, english, hebrew)`: to English → `english`. To Hebrew → if `english` has a
trailing run in `P` (`trail`) and the corresponding tail of `hebrew` is **not** punctuation
(i.e. the `,` key, which is `ת`), return `hebrew[:-len(trail)] + trail`; otherwise `hebrew`.
So `akuo,` → `שלום,` but `akuo/` → `שלום.` (the `/` key is a Hebrew period).

### 5.5 Spelling autocorrect (opt-in)

Four settings (section 6): `autocorrect` (off), `autocorrect_aggressive` (off), `autocorrect_hebrew`
(off), `names_guard` (on). The defaults come from a day of real typing: aggressive mode produced
`postgres` → `postures` and `poull` → `poll`; Hebrew mode "corrected" correct words.

`TryAutoCorrect(lang, word)` returns `(suggestion, strong)` or null:

- Off unless `autocorrect = 1`; Hebrew additionally needs `autocorrect_hebrew = 1`. Word length ≥ 3.
- Only the dictionary's **first** acceptable one-edit suggestion counts (walking further turns
  `helo` into `help`). A weak suggestion (not a typing signature) is returned only when
  `autocorrect_aggressive = 1`.
- Suggestions that only add punctuation (`etc` → `etc.`) are unacceptable, like case-only ones.
- English: structurally English, no apostrophe, first letter not uppercase (names and
  sentence-initial words are left alone). Hebrew: structurally Hebrew.
- Ask the checker for suggestions (`ISpellChecker::Suggest`, up to 10). Take the **first** that is
  exactly one edit away (Damerau-Levenshtein 1: substitution, insertion, deletion, adjacent
  transposition) and is *acceptable*: not the word itself, no space, **not differing only by case,
  and not capitalized when the typed word is lowercase**. The dictionary offers proper nouns
  (`heald` → `Heald` happened live); those are not typos. The result is dictionary-order dependent
  (`helo` lists `hello` before `help`); do not assume a particular suggestion beyond the vectors in 9.3.
- `strong` = transposition, or (English) substitution by a **QWERTY neighbour**: rows
  `qwertyuiop / asdfghjkl / zxcvbnm`, two keys are neighbours when |row difference| ≤ 1 and
  |column difference| ≤ 1.
- Only a strong correction outranks a layout switch (5.3). Rationale, measured: the Windows Hebrew
  checker accepts `אקי`, so `teh` would become Hebrew; with spelling-first everywhere, `akuo` became
  the dictionary's `akua` and `thl` became `the` instead of `איך`. The typo-signature rule
  satisfies all three.
- The Windows Hebrew checker is permissive (accepts `שלוום`, `בג`) and its suggestions are weak,
  so Hebrew autocorrect rarely triggers. Do not promise it.

### 5.6 Hotkey state machine

`RegisterHotKey(hwnd, id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT (0x4000), 'H')`, re-registered on
every handle creation (WinForms can recreate handles; unregister on handle destroyed). On
`WM_HOTKEY` (0x0312):

1. Buffer non-empty → *force current word*: `other` = the layout opposite to the word's layout;
   `text = ForcedText(other, english, hebrew)`; rewrite with `backspaces = typed.Length`, no
   separator; `LastAction = Fixed`.
2. Else `LastAction == Skipped` → *force last word*: same, but `backspaces = typed.Length + 1` and
   re-send the separator **only if a separator was recorded** (a `Skipped` produced by undoing a
   case-1 conversion has none; `+1` there would eat a user character).
3. Else `LastAction == Fixed` → *undo*: rewrite with `backspaces = fixedText.Length (+1 if a
   separator was sent)`, `hkl = original layout`, `text = typed` (the original), `keys`, re-send
   the separator; add `TrimTrailing(typed)` to `ignore-words.txt`; clear the decision cache;
   `LastAction = Skipped` (so a third press re-converts).

### 5.7 Rewrite (Injector) — the step that failed most often

Runs on its own thread, one job at a time, from a queue. A job is
`{backspaces, hkl, text, keys, separatorVk}`.

1. **Wait until Ctrl, Alt, Shift and Win are all physically up** (poll `GetAsyncKeyState`, up to
   1.5 s, then proceed anyway rather than drop the job). A hotkey-triggered job starts while
   Ctrl+Alt are still held, and Alt+Backspace is Undo in Notepad: the first backspace was being eaten.
2. Send `backspaces` × (Backspace down, up) in one `SendInput` batch. **Sleep ~80 ms**: without it
   the text control drops the first key that follows the burst (`teh` came out as `he` once the
   layout-switch settle no longer covered same-layout rewrites).
3. If `hkl` given and the focus thread is **already** on `hkl` (a spelling fix; an undo after a
   failed switch): skip this whole step, no reset window occurs. Otherwise post
   `WM_INPUTLANGCHANGEREQUEST` (0x0050, wParam 0, lParam = hkl) to the focus
   window (`GetGUIThreadInfo(threadOfForeground).hwndFocus`) **and** to the foreground window.
   Then **poll until the focus thread's layout equals `hkl`** (5 ms steps, ≤ 300 ms). If it never
   does, fall back to Unicode input for the whole text. If it does, **sleep 120 ms more**: the text
   control (RichEdit/TSF) resets its input context after the change and drops keys sent inside
   that window. 60 ms was borderline; one run in ten lost the first letter.
4. Type the text: walk `keys`; while `Render(key, hkl)` matches the next chars of `text`, replay
   the **original physical key** (Shift down if recorded, key down, key up, Shift up), ~3 ms apart.
   For the remainder (a spelling correction, a trailing comma kept as punctuation): for each char,
   `VkKeyScanEx(char, hkl)`; if it yields a key with at most the Shift modifier, press that key;
   otherwise send a `KEYEVENTF_UNICODE` down/up pair, ~8 ms apart.
   **Never send the whole word as a Unicode burst**: Windows 11 Notepad renders it as the last
   character repeated (`ooooo` for `hello`, `םםםם` for `שלום`).
5. If `separatorVk` given: press it (down/up).
6. Every event LangFixer sends carries `dwExtraInfo = InjectMarker` (any fixed non-zero value).

**Holding user keys**: while a job is running (`busy` flag), the hook parks any *physical*
non-modifier key-down (`vk, scan, shift`) and swallows it; the matching key-up is swallowed too.
After the job, the injector replays the parked keys in order with `dwExtraInfo = ReplayMarker`
(a second fixed value), **sleeps ~30 ms** (replayed keys reach the hook asynchronously and a
replayed separator may enqueue a new job), and only then clears `busy` if no new job and no new
parked keys arrived, looping otherwise. The hook treats `ReplayMarker` events as physical typing (they go through the
Engine), `InjectMarker` events as invisible, and other injected events (`LLKHF_INJECTED`) as
invisible unless `--accept-injected`.

### 5.8 Decisions off the hook thread (DecisionService)

A low-level keyboard hook is an **input-synchronous** callback: any COM call from inside it fails
with `RPC_E_CANTCALLOUT_ININPUTSYNCCALL (0x8001010D)`. Therefore:

- A dedicated thread (`SetApartmentState(MTA)`) creates the spell checkers and owns the Detector.
- Requests `{typedIn, typed, english, hebrew}` go through a blocking queue; results are cached by
  that 4-tuple (cap ~2000 entries, clear-all when full; clear on ignore-list change). If `Decide`
  arrives while a `Prefetch` for the same key is in flight, wait on that request instead of
  computing twice.
- `Prefetch(...)`: enqueue without waiting, called on every word key so the verdict for the word so
  far is ready. `Decide(..., timeoutMs)`: cache hit → immediate; else enqueue and wait on the
  request's event; on timeout → keep ("dictionary timeout").
- The hook may wait on a kernel event; that is allowed. Budget: the LL-hook timeout is 300 ms.

### 5.9 Which layout is "current"

Windows 11 Notepad (and other XAML/WinUI apps) run the text control on a **different thread** than
the top-level window, and each thread has its own active layout. Reading the main window's thread
made LangFixer believe the text was already Hebrew while the control typed Latin. Always:
`tid = GetWindowThreadProcessId(foreground)`; `GetGUIThreadInfo(tid)`; if `hwndFocus` is set and
differs, `tid = thread of hwndFocus`; `GetKeyboardLayout(tid)`.

## 6. Files in the data folder

- `ignore-words.txt` — one word per line, `#` comments, case-insensitive match against the
  **trimmed rendering in the typed language**. Seed on first run with (at least):
  `api apis aws gcp src cli sql css url urls json yaml yml xml http https git cmd exe dll jvm jdk
  jre sdk ide iam env dev prod uat jira npm tsx jsx kts jar war pom ivy ssh ssl tls dns vpn tcp udp
  rpc grpc oauth jwt uuid guid sha utf ascii regex ctx req res err cfg tmp usr lib bin obj str int
  bool len idx ptr args argv kwargs async mvn gradle kotlin lombok redis kafka nginx tomcat docker
  ubuntu linux wsl hkl svc ref refs repo repos cron sudo chmod grep awk sed ls cd rm mkdir curl
  wget localhost stg qa ok lol btw fyi asap tbd wip lgtm pr prs ci`. Undo appends to it.
- `excluded-apps.txt` — process names, case-insensitive. Seed: `WindowsTerminal.exe cmd.exe
  powershell.exe pwsh.exe conhost.exe mintty.exe bash.exe putty.exe kitty.exe mstsc.exe vmware.exe
  VirtualBoxVM.exe Code.exe "Code - Insiders.exe" devenv.exe idea64.exe rider64.exe pycharm64.exe
  webstorm64.exe datagrip64.exe goland64.exe clion64.exe dbeaver.exe pgAdmin4.exe cursor.exe
  windbg.exe`. Do **not** exclude general text editors (Notepad++, Sublime): people write notes there.
- `settings.txt` — `key=value` lines: `autocorrect`, `autocorrect_aggressive`, `autocorrect_hebrew`
  (all default 0) and `names_guard` (default 1). Every option is a dashboard checkbox.
- `log.txt` — only with `--debug`: `HH:mm:ss.fff auto-fix: <reason> 'typed' -> 'text' [process]` /
  `keep: typed='…' en='…' he='…' typedIn=… -> <reason> [process]`, plus startup lines
  (layouts found, dictionaries available, hooks installed, hotkey registered, hwnd).

All reads from the worker thread and writes from the UI thread: guard the sets with a lock.

## 7. UI

- **Hidden main form** owns the hooks and the hotkey. A form that is never shown never gets a
  window handle, so `OnHandleCreated` never fires: override `SetVisibleCore` to call
  `CreateHandle()` once and always pass `false` to the base. Install hooks, register the hotkey
  and build the tray icon in `OnHandleCreated` (guard against running twice).
- **Tray icon** (drawn at runtime: blue disc with `א` and `A`): left-click opens the dashboard;
  context menu: Open dashboard, Auto-fix enabled (check), Auto-correct spelling (check), Start with
  Windows (check; `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value = exe path +
  ` --minimized`), Edit ignored words…, Edit excluded apps… (open in Notepad, reload on exit),
  Reload lists, Exit.
- **Dashboard** (≈560×500): status line (`Running`/`Stopped — nothing is changed`), Start/Stop
  button, dictionary + layout status, words-fixed counter, live decision feed (last ~200 log
  lines, refreshed by a timer), checkboxes for autocorrect and start with Windows, buttons for the
  two lists and Exit. Closing hides to the tray; Exit quits.
- First launch shows the dashboard; `--minimized` starts in the tray. The dashboard's caption is
  exactly `LangFixer` (give the hidden form a different caption). A second instance finds it with
  `FindWindow(null, "LangFixer")`, restores and foregrounds it (or posts a
  `RegisterWindowMessage`-registered message the first instance handles), then exits.
- Startup balloon tip when a layout or a spell checker is missing, or the hotkey is taken.

## 8. Windows API cheat-sheet

- Hooks: `SetWindowsHookEx(WH_KEYBOARD_LL=13 | WH_MOUSE_LL=14, proc, GetModuleHandle(null), 0)`.
  Keep the delegate in a field (GC). `KBDLLHOOKSTRUCT{vkCode, scanCode, flags, time, dwExtraInfo}`;
  `LLKHF_INJECTED = 0x10`. Messages: `WM_KEYDOWN 0x100, WM_KEYUP 0x101, WM_SYSKEYDOWN 0x104,
  WM_SYSKEYUP 0x105, WM_LBUTTONDOWN 0x201, WM_RBUTTONDOWN 0x204, WM_MBUTTONDOWN 0x207`.
  Return `(IntPtr)1` to swallow, else `CallNextHookEx`.
- `INPUT` for `SendInput` on x64 is 40 bytes: `uint type; union { MOUSEINPUT; KEYBDINPUT{ushort
  wVk, wScan; uint dwFlags, time; IntPtr dwExtraInfo}; HARDWAREINPUT }` — declare the union with
  `LayoutKind.Explicit`, `FieldOffset(0)` for all three, and pass `Marshal.SizeOf(typeof(INPUT))`.
  Flags: `KEYEVENTF_KEYUP 2, KEYEVENTF_UNICODE 4`. `INPUT_KEYBOARD = 1`. Scan codes via
  `MapVirtualKey(vk, 0)`.
- `VkKeyScanEx(char, hkl)` → low byte vk, high byte modifiers (1 Shift, 2 Ctrl, 4 Alt), -1 if none.
- Spell checking (`spellcheck.h`), all `InterfaceIsIUnknown`, vtable order matters:
  - `CLSID_SpellCheckerFactory = 7AB36653-1796-484B-BDFA-E74F1DB7C1DC`
  - `ISpellCheckerFactory = 8E018A9D-2415-4677-BF08-794EA61F94BB`: `get_SupportedLanguages(IEnumString**)`,
    `IsSupported(LPCWSTR, BOOL*)`, `CreateSpellChecker(LPCWSTR, ISpellChecker**)`.
  - `ISpellChecker = B6FD0B71-E2BC-4653-8D05-F197E412770B`: `get_LanguageTag(LPWSTR*)`,
    `Check(LPCWSTR, IEnumSpellingError**)`, `Suggest(LPCWSTR, IEnumString**)`, … (declare at least these three, in this order).
  - `IEnumSpellingError = 803E3BD4-2828-4410-8290-418D1D73C762`: `Next(ISpellingError**)` returns
    `S_OK` with an error or `S_FALSE (1)` when none. **Declare the out parameter as `IntPtr` and
    `Marshal.Release` it**: casting it to an `ISpellingError` interface failed with
    `E_NOINTERFACE` in practice. A word is valid when the first `Next` returns `S_FALSE`.
  - Language tags to try: `en-US, en-GB, en` and `he-IL, he`.
- Layout: `GetKeyboardLayoutList`, `GetKeyboardLayout(tid)`, `GetGUIThreadInfo` (cbSize = 72 on
  x64: `uint cbSize, flags; IntPtr ×6; RECT`), `PostMessage(hwnd, 0x0050, 0, hkl)`.
- Hotkey: `RegisterHotKey/UnregisterHotKey`; `WM_HOTKEY = 0x0312`.
- Process name: `GetWindowThreadProcessId` → `OpenProcess(0x1000)` → `QueryFullProcessImageName`.
- Console output from a `winexe` for `--test`: `AttachConsole(-1)`, then re-open stdout
  (`Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true })`) or
  nothing appears; PowerShell returns immediately because it is a GUI exe, so scripts should use
  `Start-Process -Wait` and read `test-output.txt`, which the test also writes next to the exe.
- `IEnumString`: use `System.Runtime.InteropServices.ComTypes.IEnumString` (`Next(1, string[1],
  IntPtr.Zero)` until it returns `S_FALSE`). If `Check()` itself throws, treat the word as
  **unknown** and keep (section 5.3); a checker fault must never cause a conversion.
- Caps Lock inside the hook: `GetKeyState(VK_CAPITAL) & 1` on the hook thread works in practice.
- The UI Automation assemblies for the driver are **not** in the Framework root but in its `WPF\`
  subfolder: `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\{UIAutomationClient,UIAutomationTypes,WindowsBase}.dll`.

## 9. Acceptance tests

### 9.1 Layout rendering

- `Render(keys("akuo"), Hebrew) == "שלום"`; `Render(keys("hello"), English) == "hello"`.
- Under the Hebrew layout `h,e,l,l,o` renders `יקךךם`, and Shift+H renders `H`.

### 9.2 Dictionaries (on a machine with both checkers)

- English: `hello` valid, `akuo` invalid, `cd` valid (abbreviation, accepted by Windows).
- Hebrew: `שלום`, `את`, `מחשב` valid; `יקךךם` invalid; `בג`, `אקי`, `שלוום` **valid** (permissive).
- Suggestions for `teh` include `the`.

### 9.3 Decision vectors (`typedIn`, ASCII keys on the physical US keyboard → expected)

| keys | typed in | expected | why |
|---|---|---|---|
| `akuo` | English | fix → `שלום` | core case |
| `hello` | English | keep | valid English |
| `hello` | Hebrew | fix → `hello` | `יקךךם` invalid |
| `akuo` | Hebrew | keep | valid Hebrew |
| `tbh` | English | fix → `אני` | |
| `njac` | English | fix → `מחשב` | |
| `gradle` | English | keep | ignored / garbage Hebrew |
| `cd` | English | keep | valid English |
| `js` | English | keep | 2-letter guard (`חד` is a word) |
| `Hello` (Shift) | Hebrew | fix → `Hello` | `Hקךךם` |
| `akuo'` | English | fix → `שלום,` | `'` key is Hebrew comma |
| `akuo,` | English | fix → `שלום,` | trailing English comma kept |
| `akuo.` | English | fix → `שלום.` | |
| `hello,` | Hebrew | fix → `hello,` | |
| `hello,` | English | keep | |
| `the` | Hebrew | fix → `the` | |
| `a` | English | keep | |
| `i` | Hebrew | fix → `i` | `ן` alone |
| `a` | Hebrew | fix → `a` | `ש` alone |
| `c` | Hebrew | keep | |
| `vhv` | English | fix → `היה` | |
| `npm` | English | keep | |
| `it's` | Hebrew | fix → `it's` | |
| `vhv/` | English | fix → `היה.` | |
| `how`, `we`, `what` | Hebrew | fix | W key = geresh |
| `LON`, `DEADLOCK` (Shift) | Hebrew | keep | already reads as target |
| `please` | Hebrew | fix → `please` | |
| `aui` | English | fix → `שון`; after adding `aui` to ignore list → keep | learning |
| autocorrect **off**: `teh` | English | fix → `אקי` | permissive Hebrew checker (documented) |
| autocorrect **on**: `teh` / `teh,` / `helo` / `hwllo` | English | fix → `the` / `the,` / `hello` / `hello` | |
| autocorrect on: `akuo` | English | fix → `שלום` (not `akua`) | layout wins over weak typo |
| autocorrect on: `thl` | English | fix → `איך` (not `the`) | |
| autocorrect on: `gradle`, `Teh`, `hello` | English | keep | ignored / capitalized / valid |
| autocorrect on: `heald` | English | never → `Heald` | capitalized suggestion rejected (may keep or → `heal`) |
| autocorrect on, strict (default): `helo`, `postgres`, `poull`, `deplink`, `etc` | English | keep | weak suggestions need aggressive mode; `etc.` only adds punctuation |
| autocorrect on, aggressive: `helo` | English | fix → `hello` | |
| autocorrect on: `eurv` | English | fix → `קורה` | a 4-letter valid Hebrew word beats the transposition `eruv` |
| `tal` alone / `tal` after an unknown word | English | fix → `אשך` / keep | names guard needs a preceding unknown word |
| `webguru` | English | keep, Unknown = true | feeds the names guard |
| `csh,v` (בדיכה) | Hebrew | keep by default | Hebrew autocorrect is opt-in |
| `hyuk,` / `hyuk` | Hebrew | keep | "hyuk" is in the English dictionary but not an everyday word; `did`, `text`, `workflow` still convert |
| autocorrect on: `chsev` | English | fix → `בדיקה` | cross-layout typo repair (swapped pair) |
| autocorrect on: `hlelo` | Hebrew | fix → `hello` | cross-layout typo repair the other way |
| autocorrect off: `chsev` | English | keep | repair needs the autocorrect switch |
| `AcceptableSuggestion`: (heald, Heald) no; (hello, Hello) no; (teh, the) yes; (Teh, The) yes | | | |
| autocorrect on: `hello` | Hebrew | fix → `hello` | |
| `ForcedText`: `akuo,`→`שלום,`, `akuo/`→`שלום.`, `akuo`→`שלום`, to English `hello,`→`hello,` | | | |
| `OneEdit`: teh/the transposition; helo/hello insertion; helllo/hello deletion; hallo/hello substitution; hxllx/hello none; identical none | | | |
| QWERTY neighbours: (w,e) yes, (s,w) yes, (o,a) no | | | |
| Sweep: with the 3-letter minimum, **0 of 676** two-letter English-layout strings convert; with a 2-letter minimum, 159 do | | | |
| DecisionService: `akuo` via the worker thread → fix; after `Prefetch`, `Decide` is served **from the cache** (assert the cache hit, not a timing: the un-prefetched round trip is sub-millisecond too) | | | |

### 9.4 End-to-end in Notepad (driver)

Start `LangFixer.exe --debug --accept-injected --minimized` with `LANGFIXER_HOME` pointing at a
scratch folder containing `settings.txt` = `autocorrect=1` (the harness script or the driver
itself may do this). **Stop any other LangFixer instance first**: two hooks would both act.
The driver: launch `notepad.exe`
(Windows 11 Notepad is single-instance: find the window by class `Notepad` that becomes
foreground, not by process id), Ctrl+N for a fresh tab, post the English layout, then type with
`SendInput` (no marker, 40 ms per key) and read the document back through UI Automation
(`ControlType.Document`, `TextPattern.DocumentRange.GetText`). **Only send keys while Notepad is
the foreground window**; never run the cleanup (Ctrl+A, Delete, Ctrl+W) unless Notepad is in front.
Expected running text and focus-thread layout after each step:

```
akuo␣    -> "שלום "                                   Hebrew
nv␣      -> + "מה "                                    Hebrew   (2-letter word typed in Hebrew stays)
hello␣   -> + "hello "                                 English
gradle␣  -> + "gradle "                                English
please␣  -> + "please "                                English
vhv␣     -> + "היה "                                   Hebrew
please␣  -> + "please "                                English  (the 6-letter erase case)
cceav␣   -> + "בבקשה "                                 Hebrew
how␣     -> + "how "                                   English
thl␣     -> + "איך "                                   Hebrew
LON␣ (Shift) -> + "LON "                               Hebrew   (unchanged, layout stays)
tbjbu␣   -> + "אנחנו "                                 Hebrew
Ctrl+Alt+H -> last "אנחנו " becomes "tbjbu "           English
Ctrl+Alt+H -> back to "אנחנו "                         Hebrew
hello⏎   -> + "hello\n"                                English
teh␣     -> + "the "                                   English  (autocorrect, typo signature)
postgres␣ -> + "postgres "                             English  (strict mode leaves it)
gradle␣  -> + "gradle "                                English
Ctrl+Alt+H -> "gradle " becomes "ערשגךק "              Hebrew   (forced conversion posts the other layout)
Ctrl+Alt+H -> back to "gradle "                        English
eurv␣    -> + "קורה "                                  Hebrew   (layout beats spelling)
(harness switches the layout back to English)
chsev␣   -> + "בדיקה "                                 Hebrew   (cross-layout typo repair)
(harness switches the layout back to English)
akuo, then Ctrl+Alt+H (no separator) -> + "שלום,"      Hebrew
```

47 checks = 23 steps × (document text + focus-thread layout) + 1 precondition; all must pass on an idle desktop.

## 10. Recommended order of work (TDD)

1. Native + Layouts + `--test` harness that renders `akuo` → `שלום`. RED, then GREEN.
2. SpellCheck interop; the 9.2 dictionary checks.
3. Detector + the 9.3 vectors with an in-memory Settings. This is where most of the logic lives
   and it needs no hook; iterate here until every row passes.
4. DecisionService (worker thread) and its two checks.
5. Engine + Hooks + Injector + TrayApp; run the app with `--debug` and read the log while typing.
6. Driver (9.4). Expect to discover timing issues here; the numbers in 5.7 are what worked.
7. Dashboard, settings persistence, autocorrect toggle.

## 11. Definition of done

- `build.cmd` produces `LangFixer.exe` with no errors using the Framework compiler.
- `LangFixer.exe --test` prints `ALL PASSED` on a machine with both layouts and both checkers.
- The driver prints `ALL PASSED` (45 checks) against a fresh Notepad tab.
- Typing `akuo nv akunl ` in Notepad with the English layout active produces `שלום מה שלומך ` and
  leaves the layout on Hebrew; typing `hello ` then produces `hello ` and switches back.
