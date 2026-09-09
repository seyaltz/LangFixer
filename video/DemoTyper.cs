using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using LangFixer;

/// <summary>
/// Desktop helper for the walkthrough recording. One command per invocation:
///   launch-notepad              start Notepad (new tab), print when it is foreground
///   place class:Notepad X Y W H move/size a window by class (class:...) or title (title:...) and bring it to front
///   layout en|he                switch the foreground window's layout
///   type "text"                 SendInput the text as physical keys (\n = Enter); no LangFixer marker
///   hotkey                      Ctrl+Alt+H chord
///   read class:Notepad          print the document text of that window via UI Automation
///   close-tab                   Ctrl+A, Delete, Ctrl+W - only if Notepad is the foreground window
///   foreground                  print class name of the foreground window
/// </summary>
internal static class DemoTyper
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder cls, int max);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

    static int Main(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("usage: see source"); return 2; }
        switch (args[0])
        {
            case "launch-notepad": return LaunchNotepad();
            case "place": return Place(args[1], int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5]));
            case "layout":
                {
                    var l = Layouts.Discover();
                    Fixer.SwitchLayout(args[1] == "he" ? l.Hebrew : l.English);
                    Thread.Sleep(300);
                    Console.WriteLine(Layouts.LangOf(Layouts.ForegroundHkl()));
                    return 0;
                }
            case "type": Type(args[1].Replace("\\n", "\n")); return 0;
            case "hotkey": Chord(new[] { Native.VK_CONTROL, Native.VK_MENU }, 'H'); return 0;
            case "zoom": // Ctrl+= N times (Notepad zoom in)
                for (int i = 0; i < int.Parse(args[1]); i++) { Chord(new[] { Native.VK_CONTROL }, (char)0xBB); Thread.Sleep(120); }
                return 0;
            case "read": Console.WriteLine(ReadText(Find(args[1]))); return 0;
            case "close-tab":
                {
                    if (ClassOf(Native.GetForegroundWindow()) != "Notepad") { Console.WriteLine("skipped: Notepad not foreground"); return 1; }
                    Chord(new[] { Native.VK_CONTROL }, 'A'); Thread.Sleep(150);
                    Key(0x2E); Thread.Sleep(300);
                    Chord(new[] { Native.VK_CONTROL }, 'W'); Thread.Sleep(400);
                    return 0;
                }
            case "foreground": Console.WriteLine(ClassOf(Native.GetForegroundWindow())); return 0;
        }
        Console.WriteLine("unknown command"); return 2;
    }

    static string ClassOf(IntPtr h)
    {
        var sb = new StringBuilder(64);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    static IntPtr Find(string spec)
    {
        if (spec.StartsWith("class:")) return FindWindow(spec.Substring(6), null);
        if (spec.StartsWith("title:")) return FindWindow(null, spec.Substring(6));
        return IntPtr.Zero;
    }

    static int LaunchNotepad()
    {
        Process.Start("notepad.exe");
        IntPtr hwnd = IntPtr.Zero;
        for (int i = 0; i < 60 && hwnd == IntPtr.Zero; i++)
        {
            Thread.Sleep(250);
            IntPtr fg = Native.GetForegroundWindow();
            if (ClassOf(fg) == "Notepad") hwnd = fg;
        }
        if (hwnd == IntPtr.Zero) hwnd = FindWindow("Notepad", null);
        if (hwnd == IntPtr.Zero) { Console.WriteLine("notepad not found"); return 1; }
        ShowWindow(hwnd, 9);
        SetForegroundWindow(hwnd);
        Thread.Sleep(600);
        Chord(new[] { Native.VK_CONTROL }, 'N');
        Thread.Sleep(700);
        Console.WriteLine("ok " + hwnd.ToString("X"));
        return 0;
    }

    static int Place(string spec, int x, int y, int w, int h)
    {
        IntPtr hwnd = IntPtr.Zero;
        for (int i = 0; i < 40 && hwnd == IntPtr.Zero; i++) { hwnd = Find(spec); if (hwnd == IntPtr.Zero) Thread.Sleep(250); }
        if (hwnd == IntPtr.Zero) { Console.WriteLine("window not found: " + spec); return 1; }
        ShowWindow(hwnd, 9);
        // Windows 11 top-level windows have ~7px invisible resize borders left/right/bottom: overshoot so the
        // visible frame fills the requested rectangle exactly and nothing behind it leaks into the capture.
        SetWindowPos(hwnd, IntPtr.Zero, x - 7, y, w + 14, h + 7, 0x0040 /*SHOWWINDOW*/);
        SetForegroundWindow(hwnd);
        Thread.Sleep(400);
        Console.WriteLine("ok");
        return 0;
    }

    static string ReadText(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "<no window>";
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var doc = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document))
                ?? root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (doc == null) return "<no document>";
            var tp = (TextPattern)doc.GetCurrentPattern(TextPattern.Pattern);
            return tp.DocumentRange.GetText(-1).Replace("\r\n", "\n").Replace("\r", "\n");
        }
        catch (Exception ex) { return "<uia error: " + ex.Message + ">"; }
    }

    // ---- input ----

    static void Type(string text)
    {
        foreach (char c in text)
        {
            char lower = char.ToLowerInvariant(c);
            int vk;
            if (lower >= 'a' && lower <= 'z') vk = lower - 'a' + 0x41;
            else if (c == ' ') vk = Native.VK_SPACE;
            else if (c == '\n') vk = Native.VK_RETURN;
            else if (c == ',') vk = 0xBC;
            else if (c == '.') vk = 0xBE;
            else if (c == '\'') vk = 0xDE;
            else if (c == '/') vk = 0xBF;
            else continue;
            if (char.IsUpper(c)) Chord(new[] { Native.VK_SHIFT }, (char)vk); else Key(vk);
            Thread.Sleep(90); // legible on video
        }
    }

    static void Chord(int[] mods, char vk)
    {
        var list = new List<Native.INPUT>();
        foreach (var m in mods) list.Add(Make(m, false));
        list.Add(Make(vk, false));
        list.Add(Make(vk, true));
        for (int i = mods.Length - 1; i >= 0; i--) list.Add(Make(mods[i], true));
        Send(list);
    }

    static void Key(int vk) { Send(new List<Native.INPUT> { Make(vk, false), Make(vk, true) }); }

    static Native.INPUT Make(int vk, bool up)
    {
        var inp = new Native.INPUT { type = Native.INPUT_KEYBOARD };
        inp.U.ki.wVk = (ushort)vk;
        inp.U.ki.wScan = (ushort)Native.MapVirtualKey((uint)vk, 0);
        inp.U.ki.dwFlags = up ? Native.KEYEVENTF_KEYUP : 0;
        return inp;
    }

    static void Send(List<Native.INPUT> list)
    {
        var arr = list.ToArray();
        Native.SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(Native.INPUT)));
    }
}
