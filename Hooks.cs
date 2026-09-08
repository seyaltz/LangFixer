using System;
using System.Runtime.InteropServices;

namespace LangFixer
{
    /// <summary>Low-level keyboard and mouse hooks. Must be created on a thread that pumps messages.</summary>
    internal sealed class Hooks : IDisposable
    {
        private readonly Engine _engine;
        private readonly bool _acceptInjected;
        private readonly Action<string> _log;
        private readonly Native.HookProc _kbProc;   // kept alive so the delegate is not collected
        private readonly Native.HookProc _mouseProc;
        private IntPtr _kbHook = IntPtr.Zero;
        private IntPtr _mouseHook = IntPtr.Zero;

        /// <param name="acceptInjected">Test mode: treat injected keystrokes from other programs as real typing.
        /// LangFixer's own output is always ignored via <see cref="Native.InjectMarker"/>.</param>
        public Hooks(Engine engine, bool acceptInjected, Action<string> log)
        {
            _engine = engine;
            _acceptInjected = acceptInjected;
            _log = log ?? delegate { };
            _kbProc = KeyboardProc;
            _mouseProc = MouseProc;
        }

        public void Install()
        {
            IntPtr mod = Native.GetModuleHandle(null);
            _kbHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kbProc, mod, 0);
            if (_kbHook == IntPtr.Zero) throw new InvalidOperationException("Keyboard hook failed: " + Marshal.GetLastWin32Error());
            _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, mod, 0);
        }

        private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == 0)
            {
                int msg = wParam.ToInt32();
                var info = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                bool ours = info.dwExtraInfo == Native.InjectMarker;      // LangFixer's own output: never look at it
                bool replay = info.dwExtraInfo == Injector.ReplayMarker;  // user's key held during a rewrite: physical
                bool injected = (info.flags & Native.LLKHF_INJECTED) != 0;
                bool physical = !ours && (replay || !injected || _acceptInjected);
                if (physical)
                {
                    if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
                    {
                        // A rewrite is being typed out right now: park this key, replay it after the rewrite.
                        if (!replay && _engine.Injector.TryHold(info.vkCode, info.scanCode, Native.IsDown(Native.VK_SHIFT)))
                            return (IntPtr)1;
                        bool swallow = false;
                        try { swallow = _engine.OnKeyDown(info.vkCode, info.scanCode); }
                        catch (Exception ex) { _log("hook error: " + ex); _engine.ResetAll(); }
                        if (swallow) return (IntPtr)1;
                    }
                    else if ((msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP) && !replay)
                    {
                        if (_engine.Injector.TryReleaseHeld(info.vkCode)) return (IntPtr)1;
                    }
                }
            }
            return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == 0)
            {
                int msg = wParam.ToInt32();
                if (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN || msg == Native.WM_MBUTTONDOWN)
                    _engine.ResetAll();
            }
            return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_kbHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
            if (_mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        }
    }
}
