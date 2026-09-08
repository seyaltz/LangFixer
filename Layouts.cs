using System;
using System.Text;

namespace LangFixer
{
    internal enum Lang { Other, English, Hebrew }

    /// <summary>One physical key press as the user made it.</summary>
    internal sealed class KeyRec
    {
        public uint Vk;
        public uint Scan;
        public bool Shift;
        public bool Caps;
        /// <summary>What the key actually produced in the layout that was active when it was pressed.</summary>
        public string Typed = "";
    }

    /// <summary>Finds the English and Hebrew HKLs and renders raw key presses under either of them.</summary>
    internal sealed class Layouts
    {
        public IntPtr English = IntPtr.Zero;
        public IntPtr Hebrew = IntPtr.Zero;

        public bool Complete { get { return English != IntPtr.Zero && Hebrew != IntPtr.Zero; } }

        public static Layouts Discover()
        {
            var result = new Layouts();
            int n = Native.GetKeyboardLayoutList(0, null);
            if (n <= 0) return result;
            var list = new IntPtr[n];
            Native.GetKeyboardLayoutList(n, list);
            foreach (var hkl in list)
            {
                Lang l = LangOf(hkl);
                if (l == Lang.English && result.English == IntPtr.Zero) result.English = hkl;
                if (l == Lang.Hebrew && result.Hebrew == IntPtr.Zero) result.Hebrew = hkl;
            }
            return result;
        }

        public static Lang LangOf(IntPtr hkl)
        {
            if (hkl == IntPtr.Zero) return Lang.Other;
            int langId = (int)(hkl.ToInt64() & 0xFFFF);
            int primary = langId & 0x3FF;
            if (primary == 0x09) return Lang.English;
            if (primary == 0x0D) return Lang.Hebrew;
            return Lang.Other;
        }

        public IntPtr HklFor(Lang lang)
        {
            return lang == Lang.Hebrew ? Hebrew : English;
        }

        /// <summary>Keyboard layout of the thread that actually receives typing in the foreground window, or Zero if unknown.</summary>
        public static IntPtr ForegroundHkl()
        {
            return HklOfWindow(Native.GetForegroundWindow());
        }

        /// <summary>
        /// Layout of the focused control's thread. Apps such as Windows 11 Notepad run their text control on a
        /// different thread than the top-level window, and each thread has its own active layout.
        /// </summary>
        public static IntPtr HklOfWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return IntPtr.Zero;
            uint pid;
            uint tid = Native.GetWindowThreadProcessId(hwnd, out pid);
            var gti = new Native.GUITHREADINFO();
            gti.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.GUITHREADINFO));
            if (Native.GetGUIThreadInfo(tid, ref gti) && gti.hwndFocus != IntPtr.Zero && gti.hwndFocus != hwnd)
            {
                uint focusTid = Native.GetWindowThreadProcessId(gti.hwndFocus, out pid);
                if (focusTid != 0) tid = focusTid;
            }
            return Native.GetKeyboardLayout(tid);
        }

        /// <summary>Render one key press as it would appear under the given layout.</summary>
        public static string Render(KeyRec k, IntPtr hkl)
        {
            var state = new byte[256];
            if (k.Shift) state[Native.VK_SHIFT] = 0x80;
            if (k.Caps) state[Native.VK_CAPITAL] = 0x01;
            var sb = new StringBuilder(16);
            // wFlags bit 2 (0x4): do not change keyboard state (Win10 1607+), so we never disturb dead keys.
            int r = Native.ToUnicodeEx(k.Vk, k.Scan, state, sb, sb.Capacity, 4, hkl);
            if (r <= 0) return "";
            return sb.ToString(0, Math.Min(r, sb.Length));
        }

        public static string Render(System.Collections.Generic.IList<KeyRec> keys, IntPtr hkl)
        {
            var sb = new StringBuilder();
            foreach (var k in keys) sb.Append(Render(k, hkl));
            return sb.ToString();
        }

        /// <summary>Concatenation of what each key actually produced when typed.</summary>
        public static string Typed(System.Collections.Generic.IList<KeyRec> keys)
        {
            var sb = new StringBuilder();
            foreach (var k in keys) sb.Append(k.Typed);
            return sb.ToString();
        }
    }
}
