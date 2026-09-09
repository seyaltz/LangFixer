# LangFixer clean-room rebuild — notes

Built from `SPEC.md` alone in `C:\utilProjects\LangFixer-rebuild`. Nothing under `C:\utilProjects\LangFixer`
was read, listed or copied. Compiler: `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (C# 5).

Result: `build.cmd` -> `LangFixer.exe`, zero errors, zero warnings at `/warn:3`.
`LangFixer.exe --test` -> **`ALL PASSED (81 checks)`** on the first run of the completed self-test; report in
`test-output.txt`. `tools\build-driver.cmd` -> `tools\Driver.exe` compiles (not run, per the rules).
The exe was never started in normal mode (rule 2), so Engine/Hooks/Injector/TrayApp/Dashboard are compiled and
desk-checked against the spec but not exercised; that is the honest verification boundary of this rebuild.

Source: 13 files / 2,647 lines under `src\`, plus `tools\Driver.cs` (352 lines) = 14 files, 2,999 lines.

---

## (a) Where SPEC.md was ambiguous, missing or wrong — and what I decided

Ordered roughly by how much they would matter to a re-implementer.

1. **5.3 "keep Hebrew too short (see F6; still fall to autocorrect below)"** — the pseudo-code is an `if/elif` chain
   that ends with `if corrected -> FIX English`, but the first branch is written as `keep`, which reads as an
   immediate return. The parenthetical contradicts that. **Decided:** the `len(he) < 3` branch does *not*
   return; it skips the Hebrew checks, falls to `if corrected -> FIX English (spelling)`, and only then keeps —
   with reason `"Hebrew too short"` rather than `"not Hebrew"`. Suggest rewriting the block as
   `tooShort = len(he) < 3; if not tooShort: ...; if corrected: FIX; keep(tooShort ? "Hebrew too short" : "not Hebrew")`.

2. **5.1 rule 7, separator with an empty buffer** — "as classified above" is circular: separators are classified
   only as "trigger the decision", and there is nothing to decide. Does a second Space clear the undo state?
   **Decided:** no-op (buffer and undo untouched), so Ctrl+Alt+H still undoes after `word␣␣`. Consequence
   (undocumented either way): the undo's backspace count (`fixedText.Length + 1`) then leaves the extra space.
   The spec should state the intended behaviour.

3. **5.6 case 2 "backspaces = typed.Length + 1 (the separator)"** — a `Skipped` produced by *undo* of a case-1
   (hotkey, no separator) conversion has no separator, so `+1` would eat a user character. **Decided:** `+1`
   only when `sepVk != 0`, and the separator is re-sent only in that case. Same guard in undo (`+1 if a
   separator was sent`), which the spec does say.

4. **5.7 step 3 when the layout is already the target** (autocorrect keeps English; undo returns to the layout
   that is still active after a failed switch) — the spec says post, poll, then "sleep 120 ms more"
   unconditionally. **Decided:** if `FocusThreadLayout() == hkl` before posting, skip the post, the poll and the
   120 ms sleep; no input-context reset happens without a change. Worth a sentence in the spec.

5. **9.4 driver build: where the UI Automation assemblies are.** The spec promises "no SDK" and names
   `ControlType.Document` / `TextPattern`, but `UIAutomationClient.dll`, `UIAutomationTypes.dll` and
   `WindowsBase.dll` are **not** in `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\` (first build failed with
   CS0006). They are in the **`WPF\` subfolder** of that directory (also in `GAC_MSIL`). `build-driver.cmd`
   references `%FW%\WPF\...`. Add this path to section 8 or 9.4.

6. **9.4 "40 checks"** — the composition is not stated. 20 script lines × (text check + focus-layout check) = 40,
   so that is what the driver does. Step 18 (`gradle` forced to `ערשגךק`) has no layout column in the spec;
   **decided** Hebrew, since a forced conversion posts the other layout.

7. **7 / 2 second-instance activation** — "finds the first one's dashboard window and brings it to the front" gives
   no mechanism and no window title. **Decided:** dashboard caption is exactly `LangFixer` (hidden main form is
   `LangFixer (hidden)` so `FindWindow(null, "LangFixer")` is unambiguous); the second instance posts a
   `RegisterWindowMessage("LangFixer.ShowDashboard")` message to that hwnd, falling back to `HWND_BROADCAST`;
   both the dashboard and the hidden form handle it. The spec should fix the title and the message.

8. **5.8 duplicate requests** — the cache is keyed by the 4-tuple, but the spec does not say what `Decide` does when
   a `Prefetch` for the same key is still in flight. **Decided:** keep a `pending` map; `Decide` waits on the
   pending request's event instead of enqueuing a second computation. Cache eviction at the ~2000 cap is
   unspecified; **decided** clear-all.

9. **9.3 "a prefetched verdict is served with 0 ms wait"** — as a measurement this is weak: on this machine the
   *un-prefetched* `akuo` round trip through the worker also measured 0 ms (sub-millisecond). The test therefore
   first asserts `IsCached(...)` after the prefetch and then that `Decide` returns from the cache path
   (`WaitedMs == 0` by construction). Suggest the spec ask for "served from the cache" rather than a timing.

10. **8 spell-check interop: `IEnumString`** — not said whether to declare it or use
    `System.Runtime.InteropServices.ComTypes.IEnumString`. **Decided:** the framework one (`Next(1, string[],
    IntPtr pceltFetched)`, loop until `S_FALSE`). Works. Also unspecified: what to do when `Check()` itself
    returns a failure HRESULT — **decided** treat as "no error" (conservative: never convert on a checker fault).

11. **5.3 `IsValidHebrew` in the Hebrew branch guard `len(he) >= 1`** is dead: the structural rule already requires
    length ≥ 2, so a one-letter `he` can never be valid. Harmless; noting so nobody "fixes" it into a bug.

12. **5.1 Backspace: "pop the last key; if the buffer is empty, clear the undo state"** — empty *before* or *after*
    the pop? Immaterial in practice because every word key already clears the undo state, so a non-empty buffer
    implies no undo state. **Decided:** empty-before → clear undo; pop-to-empty → reset the buffer's layout/taint.

13. **5.7 modifier wait** — "up to 1.5 s" but not what to do if a modifier is still down afterwards.
    **Decided:** proceed anyway (the alternative, dropping the job, loses the user's word).

14. **5.7 `busy` clearing race** — after replaying parked keys the hook processes them asynchronously; a replayed
    separator may enqueue a new job *after* `busy` was cleared. The spec's "re-checking that no new job and no new
    parked keys arrived" cannot close this window by itself. **Decided:** sleep 30 ms after a replay burst before
    the re-check. Untested at runtime; flagging it as the most likely place for a timing bug.

15. **6 `excluded-apps.txt` seed has `"Code - Insiders.exe"` in quotes** — the file is one name per line, so it is
    written unquoted; the loader also strips surrounding quotes in case someone copies the spec literally.

16. **5.1 rule 4 Caps Lock** — "caps = Caps Lock toggled" without saying how to read it from a LL hook.
    **Decided:** `GetKeyState(VK_CAPITAL) & 1` on the hook thread. In other hook-based tools this works for toggle
    keys; it is a known grey area because the hook thread is not the input thread. Not verifiable under rule 2.

17. **6 log format** — `auto-fix: <reason> [process]` carries no words. I appended `'typed' -> 'text'` to the
    auto-fix line so the dashboard feed shows what changed. Still F14-compliant (disk only with `--debug`), but it
    is a deliberate deviation from the given format.

18. **9.4 driver environment** — the spec says "Start LangFixer.exe --debug --accept-injected --minimized with
    LANGFIXER_HOME pointing at a scratch folder" but not who starts it. **Decided:** the driver launches it
    (scratch folder `%TEMP%\LangFixer-driver`, writes `settings.txt`, kills it at the end) unless `--no-launch`.

19. **2 / 8 `AttachConsole(-1)` from a winexe** — works, but `Console.Out` must be re-opened
    (`new StreamWriter(Console.OpenStandardOutput())`) or nothing appears, and PowerShell returns to its prompt
    immediately because the process is a GUI subsystem exe; `Start-Process -Wait -NoNewWindow` then reading
    `test-output.txt` is the reliable way to script it. One line in section 8 would save the next builder a detour.

20. **9.2 `cd` valid** — true here, but only because Windows accepts it; the vector `cd English -> keep` in 9.3 is
    also covered by the ignore list (`cd` is seeded), so the 9.3 row does not actually test the dictionary path.
    The self-test reports the raw checker verdict (`raw=True`) alongside so the two reasons stay distinguishable.

Nothing in the spec was found to be outright *wrong* on this machine; items 1, 3, 5 and 7 are the ones that
would have produced a different program in another builder's hands.

## (b) 9.2 / 9.3 vectors vs. this machine's dictionaries

Every row matched. Machine facts observed (Windows 11 Pro 10.0.26200, en-US + he-IL checkers present):

- English suggestions: `teh` -> `the,ten,tea,tee`; `helo` -> `hello,halo,helot,hele,help,held,...` (the spec's
  vector depends on `hello` coming *before* `help` — `help` is a QWERTY-neighbour substitution and would have
  won as a "strong" correction; it did not arise here, but the row is dictionary-order dependent);
  `hwllo` -> `hello,...`; `akuo` -> `akua,...` (o->a not neighbours, so weak, so the layout switch wins as the
  spec says); `thl` -> `the,...` (l->e not neighbours, weak, `איך` wins).
- Hebrew permissiveness exactly as documented: `בג`, `אקי`, `שלוום` all accepted; `יקךךם` rejected.
- Two-letter sweep: **0 / 676** with the 3-letter minimum and **exactly 159 / 676** with a 2-letter minimum — the
  spec's number reproduced to the digit (with the seeded ignore list and autocorrect off). The 159 strings are
  printed in `test-output.txt`.
- `Teh` (autocorrect on) keeps with reason "not Hebrew" (`Tקי` is structurally invalid), not because the English
  checker rejects it — either way a keep, as the table requires.

## (c) Time per phase

| Phase (spec §10) | Wall clock | Notes |
|---|---|---|
| Read spec, plan | 08:07–08:10 | |
| 1–4: Native, KeyRec, Layouts, SpellCheck, Detector, Settings/Log, DecisionService, SelfTest, build.cmd | 08:10–08:20 | one compile, green on the first `--test` run (81/81) |
| 5: Engine, Hooks, Injector | 08:20–08:23 | compiled, not run (rule 2) |
| 6–7: TrayApp, DashboardForm, Driver + build-driver.cmd | 08:23–08:26 | driver build failed once (UIA dll path, item 5), fixed |
| Notes | 08:26–08:30 | |

Total about 23 minutes of building; the test-first order in §10 was followed and paid off — no decision vector
needed a second iteration.

## (d) Final `--test` summary line

```
ALL PASSED (81 checks)
```

(Exit code 0; full report in `test-output.txt` next to `LangFixer.exe`.)

## Not verified here (needs the user's recheck)

- `LangFixer.exe` in normal mode: hooks, injector timings (5.7), hotkey state machine, tray, dashboard,
  single-instance activation, balloon tips.
- `tools\Driver.exe` (9.4, 40 checks) — compiled only.
- Item 14 (busy/replay race) and item 16 (Caps Lock read) are the two runtime behaviours I would watch first.
