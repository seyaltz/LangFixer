using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LangFixer
{
    /// <summary>
    /// Hidden message window that owns the hooks, the hotkey, the tray icon and the dashboard.
    /// </summary>
    internal sealed class TrayApp : Form
    {
        private const int HotkeyId = 0x4C46;
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "LangFixer";
        private const string DashboardTitle = "LangFixer";
        private const string HostTitle = "LangFixer.MessageWindow";
        /// <summary>Posted by a second instance to the hidden host window: "show the dashboard".</summary>
        private static readonly int WmShowDashboard = RegisterWindowMessage("LangFixer.ShowDashboard");

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string name);

        private readonly Layouts _layouts;
        private readonly Dictionaries _dict;
        private readonly Settings _settings;
        private readonly DecisionService _decisions;
        private readonly Engine _engine;
        private readonly Hooks _hooks;
        private readonly StreamWriter _log;
        private readonly bool _startMinimized;
        private NotifyIcon _tray;
        private MenuItem _enabledItem;
        private MenuItem _autoCorrectItem;
        private MenuItem _startupItem;
        private DashboardForm _dashboard;
        private Icon _icon;
        private bool _hotkeyOk;
        private bool _started;

        public TrayApp(bool debug, bool acceptInjected, bool startMinimized)
        {
            _startMinimized = startMinimized;
            _settings = new Settings();
            if (debug)
            {
                Directory.CreateDirectory(_settings.Dir);
                _log = new StreamWriter(Path.Combine(_settings.Dir, "log.txt"), true) { AutoFlush = true };
            }
            _layouts = Layouts.Discover();
            _decisions = new DecisionService(_settings, Log);
            _dict = _decisions.Dictionaries; // waits for the worker thread to create the spell checkers
            _engine = new Engine(_layouts, _decisions, _settings, Log);
            _hooks = new Hooks(_engine, acceptInjected, Log);

            Log("start: en=" + _layouts.English.ToString("X") + " he=" + _layouts.Hebrew.ToString("X")
                + " dictEn=" + _dict.EnglishAvailable + " dictHe=" + _dict.HebrewAvailable + " " + _dict.InitError);

            Text = HostTitle;
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Opacity = 0;
        }

        /// <summary>Debug-file log and live dashboard feed. Called from the hook, dictionary and injector threads.</summary>
        private void Log(string s)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + s;
            if (_log != null) _log.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + s);
            var d = _dashboard;
            if (d != null) d.Append(line);
        }

        protected override void SetVisibleCore(bool value)
        {
            // Never show this window, but the handle must exist: hooks, hotkey and tray icon are
            // created in OnHandleCreated, and a form that is never shown never gets a handle.
            if (!IsHandleCreated) CreateHandle();
            base.SetVisibleCore(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                // WinForms may recreate the handle; the hotkey is bound to it, so (re)register every time.
                _hotkeyOk = Native.RegisterHotKey(Handle, HotkeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, Engine.HotkeyVk);
                Log("hotkey registered: " + _hotkeyOk + " on hwnd " + Handle.ToString("X"));
                if (_started) return;
                _started = true;
                _hooks.Install();
                Log("hooks installed");
                BuildTray();
                Log("tray ready");
                if (!_startMinimized) ShowDashboard();
            }
            catch (Exception ex)
            {
                Log("startup failed: " + ex);
                MessageBox.Show(ex.ToString(), AppName + " failed to start", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
        }

        private void BuildTray()
        {
            _icon = MakeIcon();
            _dashboard = new DashboardForm(_engine, _settings, _dict, _layouts, _icon, SetEnabled, IsStartup, SetStartup, SetOption);
            _dashboard.Text = DashboardTitle;

            var menu = new ContextMenu();
            var open = new MenuItem("Open dashboard", delegate { ShowDashboard(); }) { DefaultItem = true };
            _enabledItem = new MenuItem("Auto-fix enabled", delegate { SetEnabled(!_engine.Enabled); });
            _enabledItem.Checked = true;
            _autoCorrectItem = new MenuItem("Auto-correct spelling", delegate { SetAutoCorrect(!_settings.AutoCorrect); });
            _autoCorrectItem.Checked = _settings.AutoCorrect;
            _startupItem = new MenuItem("Start with Windows", delegate { SetStartup(!_startupItem.Checked); });
            _startupItem.Checked = IsStartup();
            menu.MenuItems.Add(open);
            menu.MenuItems.Add(_enabledItem);
            menu.MenuItems.Add(_autoCorrectItem);
            menu.MenuItems.Add(_startupItem);
            menu.MenuItems.Add("-");
            menu.MenuItems.Add(new MenuItem(StatusText()) { Enabled = false });
            menu.MenuItems.Add(new MenuItem("Hotkey: Ctrl+Alt+H  (fix last word / undo)") { Enabled = false });
            menu.MenuItems.Add("-");
            menu.MenuItems.Add(new MenuItem("Edit ignored words...", delegate { OpenAndReload(_settings.IgnorePath); }));
            menu.MenuItems.Add(new MenuItem("Edit excluded apps...", delegate { OpenAndReload(_settings.ExcludedPath); }));
            menu.MenuItems.Add(new MenuItem("Reload lists", delegate { _settings.Reload(); _decisions.ClearCache(); }));
            menu.MenuItems.Add("-");
            menu.MenuItems.Add(new MenuItem("Exit", delegate { Application.Exit(); }));

            _tray = new NotifyIcon
            {
                Icon = _icon,
                Text = AppName + " - running",
                ContextMenu = menu,
                Visible = true
            };
            _tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowDashboard(); };
            _tray.DoubleClick += delegate { ShowDashboard(); };

            string warn = "";
            if (!_layouts.Complete) warn += "Both English and Hebrew keyboard layouts must be installed. ";
            if (!_dict.EnglishAvailable) warn += "English spell checker missing (Settings > Language > English > Basic typing). ";
            if (!_dict.HebrewAvailable) warn += "Hebrew spell checker missing (Settings > Language > Hebrew > Basic typing); using heuristics. ";
            if (!_hotkeyOk) warn += "Ctrl+Alt+H hotkey is taken by another program. ";
            if (warn.Length > 0)
                _tray.ShowBalloonTip(8000, AppName, warn.Trim(), ToolTipIcon.Warning);
            else if (_startMinimized)
                _tray.ShowBalloonTip(6000, AppName + " is running",
                    "It lives in the tray (look under the ^ arrow). Click the blue אA icon or run LangFixer.exe again to open the dashboard.",
                    ToolTipIcon.Info);
        }

        private void ShowDashboard()
        {
            if (_dashboard == null) return;
            _dashboard.UpdateState();
            _dashboard.Show();
            if (_dashboard.WindowState == FormWindowState.Minimized) _dashboard.WindowState = FormWindowState.Normal;
            _dashboard.Activate();
            if (Native.GetForegroundWindow() == _dashboard.Handle) return;
            // Another app (often a maximized chat window) holds the foreground lock. Tap Alt (with Ctrl inside so no
            // menu opens; stamped as our own input so the hook ignores it), then take the foreground and flash TopMost.
            Fixer.Send(new System.Collections.Generic.List<Native.INPUT>
            {
                Fixer.Vk(Native.VK_MENU, false, Native.InjectMarker), Fixer.Vk(Native.VK_CONTROL, false, Native.InjectMarker),
                Fixer.Vk(Native.VK_CONTROL, true, Native.InjectMarker), Fixer.Vk(Native.VK_MENU, true, Native.InjectMarker)
            });
            SetForegroundWindow(_dashboard.Handle);
            _dashboard.TopMost = true;
            _dashboard.BringToFront();
            _dashboard.Activate();
            _dashboard.TopMost = false;
        }

        private void SetAutoCorrect(bool on) { SetOption(Settings.KeyAutoCorrect, on); }

        private void SetOption(string key, bool on)
        {
            if (_settings.Get(key) == on) return;
            _settings.Set(key, on);
            _decisions.ClearCache(); // cached verdicts were computed under the old settings
            if (_autoCorrectItem != null) _autoCorrectItem.Checked = _settings.AutoCorrect;
            Log("option " + key + (on ? " on" : " off"));
            if (_dashboard != null) _dashboard.UpdateState();
        }

        private void SetEnabled(bool on)
        {
            _engine.Enabled = on;
            if (!on) _engine.ResetAll();
            _enabledItem.Checked = on;
            _tray.Text = AppName + (on ? " - running" : " - stopped");
            Log(on ? "auto-fix started" : "auto-fix stopped");
            if (_dashboard != null) _dashboard.UpdateState();
        }

        private void OpenAndReload(string path)
        {
            try
            {
                var p = System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\"");
                if (p == null) return;
                p.EnableRaisingEvents = true;
                p.Exited += delegate { try { BeginInvoke(new Action(delegate { _settings.Reload(); _decisions.ClearCache(); })); } catch { } };
            }
            catch (Exception ex) { Log("open settings failed: " + ex.Message); }
        }

        private string StatusText()
        {
            return "Dictionaries: English " + (_dict.EnglishAvailable ? "OK" : "missing")
                 + ", Hebrew " + (_dict.HebrewAvailable ? "OK" : "heuristic");
        }

        private static Icon MakeIcon()
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                using (var brush = new SolidBrush(Color.FromArgb(0x1E, 0x66, 0xC8)))
                    g.FillEllipse(brush, 0, 0, 31, 31);
                using (var f = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    g.DrawString("א", f, Brushes.White, 1, 6);   // aleph
                    g.DrawString("A", f, Brushes.White, 15, 6);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        private static bool IsStartup()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                return k != null && k.GetValue(AppName) != null;
        }

        private void SetStartup(bool on)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k == null) return;
                if (on) k.SetValue(AppName, "\"" + Application.ExecutablePath + "\" --minimized");
                else k.DeleteValue(AppName, false);
            }
            if (_startupItem != null) _startupItem.Checked = IsStartup();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (_hotkeyOk) { Native.UnregisterHotKey(Handle, HotkeyId); _hotkeyOk = false; Log("hotkey unregistered (handle destroyed)"); }
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmShowDashboard)
            {
                ShowDashboard();
                return;
            }
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                Log("hotkey pressed");
                try { _engine.ForceToggle(); } catch (Exception ex) { Log("hotkey error: " + ex.Message); }
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _hooks.Dispose();
            _engine.Injector.Dispose();
            _decisions.Dispose();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            if (_dashboard != null) _dashboard.Dispose();
            if (_log != null) _log.Dispose();
            base.OnFormClosed(e);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr h);

        [STAThread]
        private static void Main(string[] args)
        {
            bool test = false, debug = false, acceptInjected = false, minimized = false;
            foreach (var a in args)
            {
                if (a == "--test") test = true;
                if (a == "--debug") debug = true;
                if (a == "--accept-injected") acceptInjected = true; // for automated UI tests only
                if (a == "--minimized") minimized = true;            // start in the tray without the dashboard
            }
            if (test) { SelfTest.Run(); return; }

            bool created;
            using (var mutex = new Mutex(true, "Local\\LangFixer.SingleInstance", out created))
            {
                if (!created)
                {
                    // Already running: ask it to show its dashboard instead of starting a second copy. The
                    // dashboard window may not exist yet (started minimized), so signal the always-present host.
                    IntPtr host = FindWindow(null, HostTitle);
                    if (host != IntPtr.Zero) { Native.PostMessage(host, (uint)WmShowDashboard, IntPtr.Zero, IntPtr.Zero); return; }
                    IntPtr h = FindWindow(null, DashboardTitle);
                    if (h != IntPtr.Zero) { ShowWindow(h, 9); SetForegroundWindow(h); }
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp(debug, acceptInjected, minimized));
            }
        }
    }
}
