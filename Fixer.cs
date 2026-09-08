using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LangFixer
{
    /// <summary>Building blocks for synthesized input and for switching the focused window's layout.</summary>
    internal static class Fixer
    {
        public static void SwitchLayout(IntPtr hkl)
        {
            IntPtr hwnd = Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;
            uint pid;
            uint tid = Native.GetWindowThreadProcessId(hwnd, out pid);
            var gti = new Native.GUITHREADINFO();
            gti.cbSize = (uint)Marshal.SizeOf(typeof(Native.GUITHREADINFO));
            IntPtr target = hwnd;
            if (Native.GetGUIThreadInfo(tid, ref gti) && gti.hwndFocus != IntPtr.Zero) target = gti.hwndFocus;
            Native.PostMessage(target, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
            if (target != hwnd) Native.PostMessage(hwnd, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
        }

        public static Native.INPUT Vk(int vk, bool up, IntPtr marker)
        {
            return Vk(vk, (ushort)Native.MapVirtualKey((uint)vk, 0), up, marker);
        }

        public static Native.INPUT Vk(int vk, ushort scan, bool up, IntPtr marker)
        {
            var inp = new Native.INPUT { type = Native.INPUT_KEYBOARD };
            inp.U.ki.wVk = (ushort)vk;
            inp.U.ki.wScan = scan;
            inp.U.ki.dwFlags = up ? Native.KEYEVENTF_KEYUP : 0;
            inp.U.ki.dwExtraInfo = marker;
            return inp;
        }

        public static Native.INPUT Unicode(char c, bool up)
        {
            var inp = new Native.INPUT { type = Native.INPUT_KEYBOARD };
            inp.U.ki.wScan = c;
            inp.U.ki.dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0);
            inp.U.ki.dwExtraInfo = Native.InjectMarker;
            return inp;
        }

        public static void Send(List<Native.INPUT> list)
        {
            if (list.Count == 0) return;
            var arr = list.ToArray();
            Native.SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(Native.INPUT)));
        }
    }
}
