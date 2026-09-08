using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace LangFixer
{
    /// <summary>
    /// Small control panel: start/stop the auto-fix, see status and a live feed of decisions.
    /// Closing the window hides it to the tray; the app keeps running.
    /// </summary>
    internal sealed class DashboardForm : Form
    {
        private readonly Engine _engine;
        private readonly Settings _settings;
        private readonly Dictionaries _dict;
        private readonly Layouts _layouts;
        private readonly Action<bool> _setEnabled;
        private readonly Func<bool> _isStartup;
        private readonly Action<bool> _setStartup;
        private readonly Action<bool> _setAutoCorrect;
        private readonly CheckBox _autoCorrect = new CheckBox();

        private readonly Label _state = new Label();
        private readonly Button _toggle = new Button();
        private readonly Label _fixes = new Label();
        private readonly ListBox _feed = new ListBox();
        private readonly CheckBox _startup = new CheckBox();
        private readonly Timer _refresh = new Timer();

        public DashboardForm(Engine engine, Settings settings, Dictionaries dict, Layouts layouts, Icon icon,
                             Action<bool> setEnabled, Func<bool> isStartup, Action<bool> setStartup, Action<bool> setAutoCorrect)
        {
            _engine = engine;
            _settings = settings;
            _dict = dict;
            _layouts = layouts;
            _setEnabled = setEnabled;
            _isStartup = isStartup;
            _setStartup = setStartup;
            _setAutoCorrect = setAutoCorrect;

            Text = "LangFixer";
            Icon = icon;
            Font = new Font("Segoe UI", 10f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 500);
            BackColor = Color.White;

            Build();
            _refresh.Interval = 1000;
            _refresh.Tick += delegate { if (Visible) UpdateState(); };
        }

        private void Build()
        {
            var title = new Label
            {
                Text = "Hebrew / English layout fixer",
                Font = new Font("Segoe UI Semibold", 15f),
                AutoSize = true,
                Location = new Point(20, 16)
            };
            Controls.Add(title);

            _state.Font = new Font("Segoe UI Semibold", 12f);
            _state.AutoSize = true;
            _state.Location = new Point(22, 58);
            Controls.Add(_state);

            _toggle.Size = new Size(120, 40);
            _toggle.Location = new Point(420, 50);
            _toggle.FlatStyle = FlatStyle.Flat;
            _toggle.Font = new Font("Segoe UI Semibold", 11f);
            _toggle.ForeColor = Color.White;
            _toggle.FlatAppearance.BorderSize = 0;
            _toggle.Click += delegate { _setEnabled(!_engine.Enabled); UpdateState(); };
            Controls.Add(_toggle);

            var info = new Label
            {
                AutoSize = true,
                Location = new Point(22, 92),
                ForeColor = Color.FromArgb(0x50, 0x50, 0x50),
                Text = "Dictionaries: English " + (_dict.EnglishAvailable ? "OK" : "missing")
                     + "   Hebrew " + (_dict.HebrewAvailable ? "OK" : "missing (heuristics)")
                     + "\nLayouts: " + (_layouts.Complete ? "English + Hebrew detected" : "MISSING - install both keyboard layouts")
                     + "\nHotkey: Ctrl+Alt+H converts the current or last word, or undoes the last fix"
            };
            Controls.Add(info);

            _fixes.AutoSize = true;
            _fixes.Location = new Point(22, 152);
            Controls.Add(_fixes);

            var feedLabel = new Label { Text = "Recent decisions", AutoSize = true, Location = new Point(22, 178), Font = new Font("Segoe UI Semibold", 10f) };
            Controls.Add(feedLabel);

            _feed.Location = new Point(22, 200);
            _feed.Size = new Size(518, 170);
            _feed.Font = new Font("Consolas", 9f);
            _feed.BorderStyle = BorderStyle.FixedSingle;
            _feed.RightToLeft = RightToLeft.No;
            Controls.Add(_feed);

            _autoCorrect.Text = "Auto-correct spelling mistakes (dictionary suggestion, one-letter typos only; Ctrl+Alt+H undoes)";
            _autoCorrect.AutoSize = true;
            _autoCorrect.Location = new Point(22, 382);
            _autoCorrect.Checked = _settings.AutoCorrect;
            _autoCorrect.CheckedChanged += delegate { _setAutoCorrect(_autoCorrect.Checked); };
            Controls.Add(_autoCorrect);

            _startup.Text = "Start with Windows (minimized to tray)";
            _startup.AutoSize = true;
            _startup.Location = new Point(22, 410);
            _startup.Checked = _isStartup();
            _startup.CheckedChanged += delegate { _setStartup(_startup.Checked); _startup.Checked = _isStartup(); };
            Controls.Add(_startup);

            int x = 22;
            x = AddButton("Ignored words...", x, delegate { OpenFile(_settings.IgnorePath); });
            x = AddButton("Excluded apps...", x, delegate { OpenFile(_settings.ExcludedPath); });
            x = AddButton("Open log folder", x, delegate { try { Process.Start("explorer.exe", "\"" + _settings.Dir + "\""); } catch { } });
            AddButton("Hide to tray", x, delegate { Hide(); });

            UpdateState();
        }

        private int AddButton(string text, int x, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = true, Location = new Point(x, 448), FlatStyle = FlatStyle.System };
            b.Click += onClick;
            Controls.Add(b);
            return x + b.PreferredSize.Width + 8;
        }

        private void OpenFile(string path)
        {
            try
            {
                var p = Process.Start("notepad.exe", "\"" + path + "\"");
                if (p == null) return;
                p.EnableRaisingEvents = true;
                p.Exited += delegate { try { BeginInvoke(new Action(_settings.Reload)); } catch { } };
            }
            catch { }
        }

        public void UpdateState()
        {
            bool on = _engine.Enabled;
            _state.Text = on ? "Running - watching your typing" : "Stopped - nothing is changed";
            _state.ForeColor = on ? Color.FromArgb(0x1E, 0x8E, 0x3E) : Color.FromArgb(0xB0, 0x30, 0x30);
            _toggle.Text = on ? "Stop" : "Start";
            _toggle.BackColor = on ? Color.FromArgb(0xB0, 0x30, 0x30) : Color.FromArgb(0x1E, 0x8E, 0x3E);
            _fixes.Text = "Words fixed this session: " + _engine.FixCount;
            if (_startup.Checked != _isStartup()) _startup.Checked = _isStartup();
            if (_autoCorrect.Checked != _settings.AutoCorrect) _autoCorrect.Checked = _settings.AutoCorrect;
        }

        private readonly System.Collections.Generic.List<string> _pendingLines = new System.Collections.Generic.List<string>();
        private readonly object _pendingLock = new object();

        /// <summary>
        /// Thread-safe: append one decision line to the live feed. While the window has never been shown the
        /// lines are only buffered: touching the controls would create the window handle as a side effect,
        /// on whatever thread happens to log, in the middle of a hook or hotkey callback.
        /// </summary>
        public void Append(string line)
        {
            if (IsDisposed) return;
            if (!IsHandleCreated)
            {
                lock (_pendingLock)
                {
                    _pendingLines.Add(line);
                    while (_pendingLines.Count > 200) _pendingLines.RemoveAt(0);
                }
                return;
            }
            if (InvokeRequired) { try { BeginInvoke(new Action<string>(Append), line); } catch { } return; }
            AddLine(line);
        }

        private void AddLine(string line)
        {
            _feed.Items.Add(line);
            while (_feed.Items.Count > 200) _feed.Items.RemoveAt(0);
            _feed.TopIndex = _feed.Items.Count - 1;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            string[] lines;
            lock (_pendingLock) { lines = _pendingLines.ToArray(); _pendingLines.Clear(); }
            foreach (var l in lines) AddLine(l);
            _refresh.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; // the app lives in the tray; Exit is in the tray menu
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
