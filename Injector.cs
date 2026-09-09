using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace LangFixer
{
    /// <summary>
    /// Performs the rewrite (backspaces, layout switch, retype, separator) on its own thread.
    ///
    /// Retyping strategy: switch the focused window's layout, wait until it really changed, then replay the
    /// user's original physical keys so the application renders them itself in the new layout. Synthetic
    /// Unicode input (VK_PACKET) is used only for characters the keys cannot produce, because apps such as
    /// Windows 11 Notepad render bursts of VK_PACKET as the last character repeated ("םםםם").
    ///
    /// While a rewrite is in flight the hook parks physical keys via <see cref="TryHold"/>; they are replayed
    /// afterwards stamped with <see cref="ReplayMarker"/> so the engine still sees them as the user's typing.
    /// </summary>
    internal sealed class Injector : IDisposable
    {
        /// <summary>Stamped on replayed user keys: the hook must treat these as physical typing.</summary>
        public static readonly IntPtr ReplayMarker = new IntPtr(0x4C465250); // "LFRP"

        private const int KeyDelayMs = 3;
        private const int UnicodeDelayMs = 8;
        private const int LayoutSwitchTimeoutMs = 300;
        /// <summary>After the layout change is visible, text controls (RichEdit/TSF) still reset their input context; keys sent inside that window get dropped.</summary>
        private const int LayoutSettleMs = 120; // 60 was borderline: one run in ten still lost the first key

        private sealed class Job
        {
            public int Backspaces;
            public IntPtr Hkl;
            public string Text;
            public List<KeyRec> Keys;
            public int SeparatorVk;
        }

        private sealed class Held
        {
            public uint Vk;
            public uint Scan;
            public bool Shift;
        }

        private readonly Action<string> _log;
        private readonly BlockingCollection<Job> _jobs = new BlockingCollection<Job>();
        private readonly object _lock = new object();
        private readonly List<Held> _pending = new List<Held>();
        private readonly HashSet<uint> _heldVks = new HashSet<uint>();
        private readonly Thread _thread;
        private volatile bool _busy;

        public Injector(Action<string> log)
        {
            _log = log ?? delegate { };
            _thread = new Thread(Run) { IsBackground = true, Name = "LangFixer.Injector" };
            _thread.Start();
        }

        public bool Busy { get { return _busy; } }

        /// <param name="keys">The physical keys that produced the word; replayed under <paramref name="hkl"/> when they render <paramref name="text"/>.</param>
        public void Enqueue(int backspaces, IntPtr hkl, string text, List<KeyRec> keys, int separatorVk)
        {
            lock (_lock)
            {
                _busy = true;
                _jobs.Add(new Job { Backspaces = backspaces, Hkl = hkl, Text = text, Keys = keys, SeparatorVk = separatorVk });
            }
        }

        private static bool IsModifier(uint vk)
        {
            return vk == Native.VK_SHIFT || vk == Native.VK_CONTROL || vk == Native.VK_MENU || vk == Native.VK_CAPITAL
                || vk == Native.VK_LWIN || vk == Native.VK_RWIN || (vk >= 0xA0 && vk <= 0xA5);
        }

        /// <summary>Hook thread: park a physical key-down while a rewrite is running. True = swallow it.</summary>
        public bool TryHold(uint vk, uint scan, bool shift)
        {
            if (!_busy || IsModifier(vk)) return false;
            lock (_lock)
            {
                if (!_busy) return false;
                _pending.Add(new Held { Vk = vk, Scan = scan, Shift = shift });
                _heldVks.Add(vk);
                return true;
            }
        }

        /// <summary>Hook thread: swallow the key-up of a key whose key-down was parked.</summary>
        public bool TryReleaseHeld(uint vk)
        {
            lock (_lock) return _heldVks.Remove(vk);
        }

        private void Run()
        {
            foreach (var job in _jobs.GetConsumingEnumerable())
            {
                try { Perform(job); }
                catch (Exception ex) { _log("inject error: " + ex.Message); }
                Drain();
            }
        }

        private void Drain()
        {
            while (true)
            {
                List<Held> batch;
                lock (_lock)
                {
                    if (_pending.Count == 0)
                    {
                        if (_jobs.Count == 0) _busy = false;
                        return;
                    }
                    batch = new List<Held>(_pending);
                    _pending.Clear();
                }
                foreach (var h in batch)
                {
                    PressKey((int)h.Vk, (ushort)h.Scan, h.Shift, ReplayMarker);
                    Thread.Sleep(KeyDelayMs);
                }
                // Replayed keys reach the hook asynchronously; a replayed separator may enqueue a new job.
                // Give them time to land before deciding whether we are idle.
                Thread.Sleep(30);
            }
        }

        private static void PressKey(int vk, ushort scan, bool shift, IntPtr marker)
        {
            var list = new List<Native.INPUT>();
            if (shift) list.Add(Fixer.Vk(Native.VK_SHIFT, false, marker));
            list.Add(Fixer.Vk(vk, scan, false, marker));
            list.Add(Fixer.Vk(vk, scan, true, marker));
            if (shift) list.Add(Fixer.Vk(Native.VK_SHIFT, true, marker));
            Fixer.Send(list);
        }

        private static bool WaitForLayout(IntPtr hkl)
        {
            for (int waited = 0; waited < LayoutSwitchTimeoutMs; waited += 5)
            {
                if (Layouts.ForegroundHkl() == hkl) return true;
                Thread.Sleep(5);
            }
            return Layouts.ForegroundHkl() == hkl;
        }

        /// <summary>
        /// A hotkey-triggered job starts while Ctrl+Alt are still physically held; Backspace with those held means
        /// "delete word" or "undo" in editors. Wait (briefly) until every modifier is up.
        /// </summary>
        private static void WaitForModifiersUp()
        {
            for (int waited = 0; waited < 1500; waited += 10)
            {
                if (!Native.IsDown(Native.VK_CONTROL) && !Native.IsDown(Native.VK_MENU) && !Native.IsDown(Native.VK_SHIFT)
                    && !Native.IsDown(Native.VK_LWIN) && !Native.IsDown(Native.VK_RWIN)) return;
                Thread.Sleep(10);
            }
        }

        private void Perform(Job job)
        {
            WaitForModifiersUp();
            var list = new List<Native.INPUT>();
            for (int i = 0; i < job.Backspaces; i++)
            {
                list.Add(Fixer.Vk(Native.VK_BACK, false, Native.InjectMarker));
                list.Add(Fixer.Vk(Native.VK_BACK, true, Native.InjectMarker));
            }
            Fixer.Send(list);
            Thread.Sleep(UnicodeDelayMs);

            bool switched = false;
            if (job.Hkl != IntPtr.Zero)
            {
                if (Layouts.ForegroundHkl() == job.Hkl)
                {
                    switched = true; // already there (spelling fix, or undo after a failed switch): no reset window to wait out
                }
                else
                {
                    Fixer.SwitchLayout(job.Hkl);
                    switched = WaitForLayout(job.Hkl);
                    if (!switched) _log("layout switch not confirmed within " + LayoutSwitchTimeoutMs + "ms; falling back to Unicode input");
                    else Thread.Sleep(LayoutSettleMs);
                }
            }

            // Replay the original keys as long as they render exactly the wanted text in the new layout.
            int pos = 0;
            if (switched && job.Keys != null)
            {
                foreach (var k in job.Keys)
                {
                    string r = Layouts.Render(k, job.Hkl);
                    if (r.Length == 0 || pos + r.Length > job.Text.Length
                        || string.CompareOrdinal(job.Text, pos, r, 0, r.Length) != 0) break;
                    PressKey((int)k.Vk, (ushort)k.Scan, k.Shift, Native.InjectMarker);
                    Thread.Sleep(KeyDelayMs);
                    pos += r.Length;
                }
            }
            // Whatever the original keys cannot produce (a spelling correction, a trailing comma kept as punctuation)
            // is typed as the keys that produce each char in the target layout; Unicode only as a last resort.
            for (; pos < job.Text.Length; pos++)
            {
                char c = job.Text[pos];
                short scan = switched ? Native.VkKeyScanEx(c, job.Hkl) : (short)-1;
                int mods = (scan >> 8) & 0xFF;
                if (scan != -1 && (mods & ~1) == 0)
                {
                    int vk = scan & 0xFF;
                    PressKey(vk, (ushort)Native.MapVirtualKey((uint)vk, 0), (mods & 1) != 0, Native.InjectMarker);
                    Thread.Sleep(KeyDelayMs);
                    continue;
                }
                Fixer.Send(new List<Native.INPUT> { Fixer.Unicode(c, false), Fixer.Unicode(c, true) });
                Thread.Sleep(UnicodeDelayMs);
            }

            if (job.SeparatorVk != 0)
            {
                Thread.Sleep(KeyDelayMs);
                PressKey(job.SeparatorVk, (ushort)Native.MapVirtualKey((uint)job.SeparatorVk, 0), false, Native.InjectMarker);
            }
        }

        public void Dispose()
        {
            _jobs.CompleteAdding();
        }
    }
}
