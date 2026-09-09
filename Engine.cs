using System;
using System.Collections.Generic;

namespace LangFixer
{
    /// <summary>
    /// Word-buffer state machine fed by the keyboard hook. All calls happen on the hook (UI) thread.
    /// </summary>
    internal sealed class Engine
    {
        private enum LastKind { None, Fixed, Skipped }

        private sealed class LastAction
        {
            public LastKind Kind;
            public List<KeyRec> Keys;
            public IntPtr TypedHkl;   // layout the keys were originally typed in
            public IntPtr FixedHkl;   // layout we switched to (Fixed only)
            public string FixedText;  // what we typed instead (Fixed only)
            public int SeparatorVk;   // 0 if none
        }

        /// <summary>How long the hook may wait for a dictionary verdict at a word boundary (LL hook budget is 300ms).</summary>
        private const int DecisionTimeoutMs = 200;

        private readonly Layouts _layouts;
        private readonly DecisionService _decisions;
        private readonly Settings _settings;
        public readonly Injector Injector;
        private readonly Action<string> _log;

        private readonly List<KeyRec> _buffer = new List<KeyRec>();
        private bool _tainted;
        private IntPtr _bufferHkl = IntPtr.Zero;
        private IntPtr _lastForeground = IntPtr.Zero;
        private bool _foregroundExcluded;
        private string _foregroundProcess = "";
        private LastAction _last;
        /// <summary>The previous word in this window was kept because it fails both dictionaries (names guard).</summary>
        private bool _prevUnknown;

        public bool Enabled = true;
        public int FixCount;

        public Engine(Layouts layouts, DecisionService decisions, Settings settings, Action<string> log)
        {
            _layouts = layouts;
            _decisions = decisions;
            _settings = settings;
            _log = log ?? delegate { };
            Injector = new Injector(_log);
        }

        public void Reset()
        {
            _buffer.Clear();
            _tainted = false;
            _bufferHkl = IntPtr.Zero;
        }

        /// <summary>Called for pointer clicks and focus changes: forget everything.</summary>
        public void ResetAll()
        {
            Reset();
            _last = null;
            _prevUnknown = false;
        }

        private static bool IsWordKey(uint vk)
        {
            return (vk >= 0x41 && vk <= 0x5A)  // A-Z
                || vk == 0xBA                   // ;  -> Hebrew final pe
                || vk == 0xBC                   // ,  -> tav
                || vk == 0xBE                   // .  -> final tsadi
                || vk == 0xBF                   // /  -> Hebrew period
                || vk == 0xDE;                  // '  -> Hebrew comma
        }

        private static bool IsSeparator(uint vk)
        {
            return vk == Native.VK_SPACE || vk == Native.VK_RETURN || vk == Native.VK_TAB;
        }

        private static bool IsDigit(uint vk)
        {
            return (vk >= 0x30 && vk <= 0x39) || (vk >= 0x60 && vk <= 0x69);
        }

        /// <summary>Modifier and lock keys on their own carry no meaning for the word buffer (and must not wipe the undo state).</summary>
        private static bool IsIgnored(uint vk)
        {
            return vk == Native.VK_SHIFT || vk == Native.VK_CONTROL || vk == Native.VK_MENU || vk == Native.VK_CAPITAL
                || vk == Native.VK_LWIN || vk == Native.VK_RWIN
                || (vk >= 0xA0 && vk <= 0xA5) // L/R SHIFT CONTROL MENU
                || vk == 0x90 || vk == 0x91;  // NUMLOCK SCROLL
        }

        /// <summary>The Ctrl+Alt+H hotkey chord: seen by the hook before WM_HOTKEY arrives, so it must leave all state intact.</summary>
        public const uint HotkeyVk = (uint)'H';

        private static bool IsHotkeyChord(uint vk)
        {
            return vk == HotkeyVk && Native.IsDown(Native.VK_CONTROL) && Native.IsDown(Native.VK_MENU);
        }

        /// <summary>Handle a physical key-down. Returns true when the key must be swallowed.</summary>
        public bool OnKeyDown(uint vk, uint scan)
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != _lastForeground)
            {
                _lastForeground = fg;
                _foregroundProcess = Settings.ProcessNameOf(fg);
                _foregroundExcluded = _settings.IsExcludedApp(_foregroundProcess);
                ResetAll();
            }

            if (IsIgnored(vk) || IsHotkeyChord(vk)) return false;

            if (Native.IsDown(Native.VK_CONTROL) || Native.IsDown(Native.VK_MENU)
                || Native.IsDown(Native.VK_LWIN) || Native.IsDown(Native.VK_RWIN))
            {
                ResetAll();
                return false;
            }

            if (vk == Native.VK_BACK)
            {
                if (_buffer.Count > 0) _buffer.RemoveAt(_buffer.Count - 1);
                else _last = null;
                return false;
            }

            if (IsWordKey(vk))
            {
                IntPtr hkl = Layouts.ForegroundHkl();
                if (Layouts.LangOf(hkl) == Lang.Other) { Reset(); return false; }
                if (_buffer.Count == 0) _bufferHkl = hkl;
                else if (hkl != _bufferHkl) { Reset(); _bufferHkl = hkl; }
                var rec = new KeyRec
                {
                    Vk = vk,
                    Scan = scan,
                    Shift = Native.IsDown(Native.VK_SHIFT),
                    Caps = Native.IsToggled(Native.VK_CAPITAL)
                };
                rec.Typed = Layouts.Render(rec, hkl);
                _buffer.Add(rec);
                _last = null;
                if (Enabled && !_foregroundExcluded && _layouts.Complete && !_tainted)
                    _decisions.Prefetch(Layouts.LangOf(hkl), Layouts.Typed(_buffer), Layouts.Render(_buffer, _layouts.English), Layouts.Render(_buffer, _layouts.Hebrew), _prevUnknown);
                return false;
            }

            if (IsDigit(vk))
            {
                if (_buffer.Count > 0) _tainted = true;
                return false;
            }

            if (IsSeparator(vk))
            {
                if (_buffer.Count == 0) return false;
                var keys = new List<KeyRec>(_buffer);
                IntPtr typedHkl = _bufferHkl;
                bool tainted = _tainted;
                Reset();
                if (tainted || !Enabled || !_layouts.Complete)
                {
                    _last = null;
                    return false;
                }
                Lang typedIn = Layouts.LangOf(typedHkl);
                string en = Layouts.Render(keys, _layouts.English);
                string he = Layouts.Render(keys, _layouts.Hebrew);
                Decision d = _foregroundExcluded ? Decision.None : _decisions.Decide(typedIn, Layouts.Typed(keys), en, he, _prevUnknown, DecisionTimeoutMs);
                if (!d.Fix)
                {
                    _log("keep: typed='" + Layouts.Typed(keys) + "' en='" + en + "' he='" + he + "' typedIn=" + typedIn + " -> " + (_foregroundExcluded ? "excluded app " + _foregroundProcess : d.Reason) + " [" + _foregroundProcess + "]");
                    _last = new LastAction { Kind = LastKind.Skipped, Keys = keys, TypedHkl = typedHkl, SeparatorVk = (int)vk };
                    _prevUnknown = d.Unknown;
                    return false;
                }
                _prevUnknown = false;
                _log("auto-fix: " + d.Reason + " [" + _foregroundProcess + "]");
                DoFix(keys, typedHkl, _layouts.HklFor(d.Target), d.Text, (int)vk, Layouts.Typed(keys).Length);
                return true; // we re-send the separator ourselves
            }

            // Navigation, escape, function keys, other punctuation: the word is over, nothing to judge.
            ResetAll();
            return false;
        }

        private void DoFix(List<KeyRec> keys, IntPtr typedHkl, IntPtr targetHkl, string text, int separatorVk, int backspaces)
        {
            Injector.Enqueue(backspaces, targetHkl, text, keys, separatorVk);
            FixCount++;
            _last = new LastAction
            {
                Kind = LastKind.Fixed, Keys = keys, TypedHkl = typedHkl, FixedHkl = targetHkl,
                FixedText = text, SeparatorVk = separatorVk
            };
        }

        /// <summary>
        /// Hotkey: convert the word being typed; or convert the last word that was left alone;
        /// or undo the last conversion.
        /// </summary>
        public void ForceToggle()
        {
            // The system swallowed H for RegisterHotKey, so the foreground app sees Alt pressed and released with
            // nothing in between: an "Alt tap", which activates its menu bar and then eats the replayed keys.
            // Inject a harmless Ctrl press now, while Alt is still held, so the tap does not look lone (AutoHotkey's
            // mask key). Done here, after WM_HOTKEY, not in the hook: input sent from inside a hook callback lands
            // ahead of the hooked key and would make Windows see Ctrl as released when H arrives.
            Fixer.Send(new List<Native.INPUT>
            {
                Fixer.Vk(Native.VK_CONTROL, false, Native.InjectMarker),
                Fixer.Vk(Native.VK_CONTROL, true, Native.InjectMarker)
            });
            if (!_layouts.Complete) return;

            if (_buffer.Count > 0)
            {
                var keys = new List<KeyRec>(_buffer);
                IntPtr typedHkl = _bufferHkl;
                Reset();
                Lang other = Layouts.LangOf(typedHkl) == Lang.Hebrew ? Lang.English : Lang.Hebrew;
                string text = Detector.ForcedText(other, Layouts.Render(keys, _layouts.English), Layouts.Render(keys, _layouts.Hebrew));
                _log("force (current word) -> " + text);
                DoFix(keys, typedHkl, _layouts.HklFor(other), text, 0, Layouts.Typed(keys).Length);
                return;
            }

            if (_last == null) return;

            if (_last.Kind == LastKind.Skipped)
            {
                var la = _last;
                Lang other = Layouts.LangOf(la.TypedHkl) == Lang.Hebrew ? Lang.English : Lang.Hebrew;
                string text = Detector.ForcedText(other, Layouts.Render(la.Keys, _layouts.English), Layouts.Render(la.Keys, _layouts.Hebrew));
                int bs = Layouts.Typed(la.Keys).Length + (la.SeparatorVk != 0 ? 1 : 0);
                _log("force (last word) -> " + text);
                DoFix(la.Keys, la.TypedHkl, _layouts.HklFor(other), text, la.SeparatorVk, bs);
                return;
            }

            if (_last.Kind == LastKind.Fixed)
            {
                var la = _last;
                string original = Layouts.Typed(la.Keys);
                int bs = la.FixedText.Length + (la.SeparatorVk != 0 ? 1 : 0);
                _log("undo -> " + original);
                Injector.Enqueue(bs, la.TypedHkl, original, la.Keys, la.SeparatorVk);
                // The user meant it: never auto-convert this word again.
                _settings.AddIgnoredWord(Detector.TrimTrailing(original));
                _decisions.ClearCache();
                _last = new LastAction { Kind = LastKind.Skipped, Keys = la.Keys, TypedHkl = la.TypedHkl, SeparatorVk = la.SeparatorVk };
            }
        }
    }
}
