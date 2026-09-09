using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using LangFixer;

/// <summary>
/// End-to-end test: opens a fresh Notepad tab, types with SendInput as if on the physical keyboard,
/// and asserts what LangFixer (running with --accept-injected) turned it into.
/// Keys are only sent while Notepad is the foreground window, so a user typing elsewhere pauses the test.
/// </summary>
internal static class Driver
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder cls, int max);

    private static int _fails;
    private static Layouts _layouts;
    private static IntPtr _hwnd;
    private static string _expected = "";

    private static void Check(bool ok, string what)
    {
        if (!ok) _fails++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
    }

    private static int Main()
    {
        _layouts = Layouts.Discover();
        if (!_layouts.Complete) { Console.WriteLine("layouts missing"); return 2; }

        // Windows 11 Notepad is single-instance: launching may just add a tab to an existing window.
        var before = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName("Notepad")) before.Add(p.Id);
        Process.Start("notepad.exe");
        var cls = new System.Text.StringBuilder(64);
        for (int i = 0; i < 60 && _hwnd == IntPtr.Zero; i++)
        {
            Thread.Sleep(250);
            IntPtr fg = Native.GetForegroundWindow();
            cls.Length = 0;
            GetClassName(fg, cls, cls.Capacity);
            if (cls.ToString() == "Notepad") _hwnd = fg;
        }
        if (_hwnd == IntPtr.Zero) _hwnd = FindWindow("Notepad", null);
        if (_hwnd == IntPtr.Zero) { Console.WriteLine("Notepad window not found"); return 2; }
        uint npPid;
        Native.GetWindowThreadProcessId(_hwnd, out npPid);
        bool weOwnProcess = !before.Contains((int)npPid);
        ShowWindow(_hwnd, 9);
        if (!WaitForeground()) { Console.WriteLine("Notepad never became foreground (user busy?)"); return 2; }
        Thread.Sleep(500);
        Chord(new[] { Native.VK_CONTROL }, 'N'); // fresh empty tab, independent of whatever else is open
        Thread.Sleep(800);

        try
        {
            Fixer.SwitchLayout(_layouts.English);
            Thread.Sleep(400);
            Check(Lang() == LangFixer.Lang.English, "precondition: Notepad in English layout");

            Step("akuo ", "שלום ", LangFixer.Lang.Hebrew, "shalom typed in English layout");
            Step("nv ", "מה ", LangFixer.Lang.Hebrew, "2-letter Hebrew word typed in (now) Hebrew layout stays");
            Step("hello ", "hello ", LangFixer.Lang.English, "hello typed in Hebrew layout");
            Step("gradle ", "gradle ", LangFixer.Lang.English, "gradle untouched");
            Step("please ", "please ", LangFixer.Lang.English, "please typed in English stays");
            Step("vhv ", "היה ", LangFixer.Lang.Hebrew, "haya typed in English");
            Step("please ", "please ", LangFixer.Lang.English, "please typed in Hebrew layout (6 letters, the reported erase case)");
            Step("cceav ", "בבקשה ", LangFixer.Lang.Hebrew, "bevakasha typed in English");
            Step("how ", "how ", LangFixer.Lang.English, "how typed in Hebrew layout (W key = geresh)");
            Step("thl ", "איך ", LangFixer.Lang.Hebrew, "eich typed in English");
            Step("LON ", "LON ", LangFixer.Lang.Hebrew, "LON with Shift in Hebrew layout stays, layout stays Hebrew");
            Step("tbjbu ", "אנחנו ", LangFixer.Lang.Hebrew, "Hebrew continues after the acronym");

            // Hotkey on the last word that was left alone: force-convert it, then press again to undo.
            Chord(new[] { Native.VK_CONTROL, Native.VK_MENU }, 'H');
            Thread.Sleep(900);
            _expected = _expected.Substring(0, _expected.Length - "אנחנו ".Length) + "tbjbu ";
            Check(Text() == _expected, "Ctrl+Alt+H force-converts the last word -> got '" + Text() + "'");
            Check(Lang() == LangFixer.Lang.English, "   layout switched to English by the forced conversion");
            Chord(new[] { Native.VK_CONTROL, Native.VK_MENU }, 'H');
            Thread.Sleep(900);
            _expected = _expected.Substring(0, _expected.Length - "tbjbu ".Length) + "אנחנו ";
            Check(Text() == _expected, "Ctrl+Alt+H again undoes it -> got '" + Text() + "'");
            Check(Lang() == LangFixer.Lang.Hebrew, "   layout Hebrew again");

            Step("hello\n", "hello|", LangFixer.Lang.English, "Enter as separator");

            // Spelling autocorrect (the test profile has autocorrect=1)
            Step("teh ", "the ", LangFixer.Lang.English, "autocorrect: teh -> the, layout unchanged");
            Step("postgres ", "postgres ", LangFixer.Lang.English, "strict autocorrect: postgres stays (was 'postures')");
            Step("gradle ", "gradle ", LangFixer.Lang.English, "autocorrect leaves ignored words alone");
            Chord(new[] { Native.VK_CONTROL, Native.VK_MENU }, 'H'); // undo nothing? last word was kept; force-converts it instead
            Thread.Sleep(900);
            _expected = _expected.Substring(0, _expected.Length - "gradle ".Length) + "ערשגךק ";
            Check(Text() == _expected, "hotkey after a kept word force-converts it -> got '" + Text() + "'");
            Chord(new[] { Native.VK_CONTROL, Native.VK_MENU }, 'H');
            Thread.Sleep(900);
            _expected = _expected.Substring(0, _expected.Length - "ערשגךק ".Length) + "gradle ";
            Check(Text() == _expected, "and undoes it again -> got '" + Text() + "'");
            Check(Lang() == LangFixer.Lang.English, "   layout English again");

            Step("eurv ", "קורה ", LangFixer.Lang.Hebrew, "layout beats spelling: eurv -> kore (not 'eruv')");
            Fixer.SwitchLayout(_layouts.English);
            Thread.Sleep(500);
            Check(Lang() == LangFixer.Lang.English, "precondition for the last test: English layout");

            Type("akuo,"); // no separator yet
            Chord(new[] { Native.VK_CONTROL, Native.VK_MENU }, 'H'); // force current word
            Thread.Sleep(900);
            _expected += "שלום,";
            Check(Text() == _expected, "hotkey on the word being typed -> got '" + Text() + "'");
            Check(Lang() == LangFixer.Lang.Hebrew, "   layout Hebrew after forced conversion");
        }
        finally
        {
            // Leave no trace: empty the tab, close it, close Notepad if we started it.
            // Only if Notepad really is in front: Ctrl+A / Delete / Ctrl+W into the user's window would be destructive.
            if (WaitForeground() && Native.GetForegroundWindow() == _hwnd)
            {
                Chord(new[] { Native.VK_CONTROL }, 'A'); Thread.Sleep(150);
                Key(0x2E); Thread.Sleep(300); // Delete
                Chord(new[] { Native.VK_CONTROL }, 'W'); Thread.Sleep(500); // empty tab closes without prompt
                if (weOwnProcess)
                {
                    try { Process.GetProcessById((int)npPid).Kill(); } catch { }
                }
            }
            else
            {
                Console.WriteLine("cleanup skipped: Notepad is not the foreground window (a test tab may remain open)");
            }
        }

        Console.WriteLine(_fails == 0 ? "ALL PASSED" : _fails + " FAILED");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>Type <paramref name="ascii"/>, then assert the document equals everything expected so far plus <paramref name="adds"/>.</summary>
    private static void Step(string ascii, string adds, Lang expectLang, string what)
    {
        Type(ascii);
        _expected += adds;
        string got = Text();
        Check(got == _expected, what + " -> expected '" + _expected + "' got '" + got + "'");
        Check(Lang() == expectLang, "   layout is " + expectLang);
    }

    private static Lang Lang()
    {
        return Layouts.LangOf(Layouts.HklOfWindow(_hwnd));
    }

    private static string Text()
    {
        Thread.Sleep(900);
        try
        {
            var root = AutomationElement.FromHandle(_hwnd);
            var doc = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document))
                ?? root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (doc == null) return "<no document element>";
            var tp = (TextPattern)doc.GetCurrentPattern(TextPattern.Pattern);
            return tp.DocumentRange.GetText(-1).Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "|");
        }
        catch (Exception ex) { return "<uia error: " + ex.Message + ">"; }
    }

    /// <summary>Bring Notepad to front; if the user is typing elsewhere, wait until it is really foreground.</summary>
    private static bool WaitForeground()
    {
        for (int i = 0; i < 120; i++)
        {
            if (Native.GetForegroundWindow() == _hwnd) return true;
            SetForegroundWindow(_hwnd);
            Thread.Sleep(500);
        }
        return Native.GetForegroundWindow() == _hwnd;
    }

    // ---- physical-looking input (no LangFixer marker) ----

    private static void Type(string ascii)
    {
        if (!WaitForeground()) throw new InvalidOperationException("Notepad lost foreground");
        foreach (char c in ascii)
        {
            char lower = char.ToLowerInvariant(c);
            int vk;
            if (lower >= 'a' && lower <= 'z') vk = lower - 'a' + 0x41;
            else if (c == ' ') vk = Native.VK_SPACE;
            else if (c == '\n') vk = Native.VK_RETURN;
            else if (c == ',') vk = 0xBC;
            else if (c == '.') vk = 0xBE;
            else if (c == '\'') vk = 0xDE;
            else throw new ArgumentException("char " + c);
            if (char.IsUpper(c)) Chord(new[] { Native.VK_SHIFT }, (char)vk); else Key(vk);
            Thread.Sleep(40);
        }
    }

    /// <summary>Press a chord the way a person does: modifiers down, a beat, the key, a beat, modifiers up.</summary>
    private static void Chord(int[] mods, char vk)
    {
        var down = new List<Native.INPUT>();
        foreach (var m in mods) down.Add(Make(m, false));
        Send(down);
        Thread.Sleep(40);
        Send(new List<Native.INPUT> { Make(vk, false), Make(vk, true) });
        Thread.Sleep(80);
        var up = new List<Native.INPUT>();
        for (int i = mods.Length - 1; i >= 0; i--) up.Add(Make(mods[i], true));
        Send(up);
    }

    private static void Key(int vk)
    {
        Send(new List<Native.INPUT> { Make(vk, false), Make(vk, true) });
    }

    private static Native.INPUT Make(int vk, bool up)
    {
        var inp = new Native.INPUT { type = Native.INPUT_KEYBOARD };
        inp.U.ki.wVk = (ushort)vk;
        inp.U.ki.wScan = (ushort)Native.MapVirtualKey((uint)vk, 0);
        inp.U.ki.dwFlags = up ? Native.KEYEVENTF_KEYUP : 0;
        return inp;
    }

    private static void Send(List<Native.INPUT> list)
    {
        var arr = list.ToArray();
        Native.SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(Native.INPUT)));
    }
}
