using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LangFixer
{
    /// <summary>
    /// Two plain-text lists in %LOCALAPPDATA%\LangFixer, one entry per line, '#' comments allowed:
    ///   ignore-words.txt   words never auto-converted (learned automatically from every undo)
    ///   excluded-apps.txt  process names (e.g. Code.exe) where auto-fix is off; the hotkey still works
    /// </summary>
    internal sealed class Settings
    {
        public readonly string Dir;
        public readonly string IgnorePath;
        public readonly string ExcludedPath;
        public readonly string SettingsPath;

        /// <summary>Auto-correct typos with a typing signature (swapped adjacent letters, neighbouring key). Off by default.</summary>
        public volatile bool AutoCorrect;
        /// <summary>Also accept any one-edit dictionary suggestion. Off by default: it turned postgres into postures.</summary>
        public volatile bool AutoCorrectAggressive;
        /// <summary>Autocorrect Hebrew too. Off by default: the Windows Hebrew checker "corrected" correct words.</summary>
        public volatile bool AutoCorrectHebrew;
        /// <summary>Leave a lowercase unknown word alone when it directly follows another unknown word (names). On by default.</summary>
        public volatile bool NamesGuard = true;

        public const string KeyAutoCorrect = "autocorrect";
        public const string KeyAggressive = "autocorrect_aggressive";
        public const string KeyHebrew = "autocorrect_hebrew";
        public const string KeyNamesGuard = "names_guard";

        // Read from the dictionary worker thread, written from the UI thread: guard every access.
        private readonly object _lock = new object();
        private readonly HashSet<string> _ignore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] DefaultIgnore =
        {
            "# Words LangFixer must never auto-convert. One per line. Undoing an auto-fix (Ctrl+Alt+H) adds the word here.",
            "api", "apis", "aws", "gcp", "src", "cli", "sql", "css", "url", "urls", "json", "yaml", "yml", "xml", "http", "https",
            "git", "cmd", "exe", "dll", "jvm", "jdk", "jre", "sdk", "ide", "iam", "env", "dev", "prod", "uat", "jira", "npm",
            "tsx", "jsx", "kts", "jar", "war", "pom", "ivy", "ssh", "ssl", "tls", "dns", "vpn", "tcp", "udp", "rpc", "grpc",
            "oauth", "jwt", "uuid", "guid", "sha", "utf", "ascii", "regex", "ctx", "req", "res", "err", "cfg", "tmp", "usr",
            "lib", "bin", "obj", "str", "int", "bool", "len", "idx", "ptr", "args", "argv", "kwargs", "async", "mvn", "gradle",
            "kotlin", "lombok", "redis", "kafka", "nginx", "tomcat", "docker", "ubuntu", "linux", "wsl", "hkl", "svc", "ref",
            "refs", "repo", "repos", "cron", "sudo", "chmod", "grep", "awk", "sed", "ls", "cd", "rm", "mkdir", "curl", "wget",
            "localhost", "stg", "qa", "ok", "lol", "btw", "fyi", "asap", "tbd", "wip", "lgtm", "pr", "prs", "ci", "cd"
        };

        private static readonly string[] DefaultExcluded =
        {
            "# Process names where auto-fix is disabled (Ctrl+Alt+H still works). One per line.",
            "WindowsTerminal.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "conhost.exe", "mintty.exe", "bash.exe",
            "putty.exe", "kitty.exe", "mstsc.exe", "vmware.exe", "VirtualBoxVM.exe",
            "Code.exe", "Code - Insiders.exe", "devenv.exe", "idea64.exe", "rider64.exe", "pycharm64.exe",
            "webstorm64.exe", "datagrip64.exe", "goland64.exe", "clion64.exe", "dbeaver.exe", "pgAdmin4.exe",
            "cursor.exe", "windbg.exe"
        };

        public Settings()
        {
            // LANGFIXER_HOME overrides the folder (used by the automated test so it never touches the real lists).
            string home = Environment.GetEnvironmentVariable("LANGFIXER_HOME");
            Dir = string.IsNullOrEmpty(home)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LangFixer")
                : home;
            IgnorePath = Path.Combine(Dir, "ignore-words.txt");
            ExcludedPath = Path.Combine(Dir, "excluded-apps.txt");
            SettingsPath = Path.Combine(Dir, "settings.txt");
            try
            {
                Directory.CreateDirectory(Dir);
                if (!File.Exists(IgnorePath)) File.WriteAllLines(IgnorePath, DefaultIgnore, Encoding.UTF8);
                if (!File.Exists(ExcludedPath)) File.WriteAllLines(ExcludedPath, DefaultExcluded, Encoding.UTF8);
            }
            catch { }
            Reload();
        }

        /// <summary>In-memory only, for tests.</summary>
        public Settings(IEnumerable<string> ignore, IEnumerable<string> excluded)
        {
            foreach (var w in ignore) _ignore.Add(w);
            foreach (var a in excluded) _excluded.Add(a);
        }

        public void Reload()
        {
            lock (_lock)
            {
                Load(IgnorePath, _ignore);
                Load(ExcludedPath, _excluded);
            }
            LoadSettings();
        }

        private void LoadSettings()
        {
            if (SettingsPath == null) return;
            try
            {
                if (!File.Exists(SettingsPath)) return;
                foreach (var raw in File.ReadAllLines(SettingsPath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0 || line.StartsWith("#")) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    bool on = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (key.Equals(KeyAutoCorrect, StringComparison.OrdinalIgnoreCase)) AutoCorrect = on;
                    else if (key.Equals(KeyAggressive, StringComparison.OrdinalIgnoreCase)) AutoCorrectAggressive = on;
                    else if (key.Equals(KeyHebrew, StringComparison.OrdinalIgnoreCase)) AutoCorrectHebrew = on;
                    else if (key.Equals(KeyNamesGuard, StringComparison.OrdinalIgnoreCase)) NamesGuard = on;
                }
            }
            catch { }
        }

        public bool Get(string key)
        {
            switch (key)
            {
                case KeyAutoCorrect: return AutoCorrect;
                case KeyAggressive: return AutoCorrectAggressive;
                case KeyHebrew: return AutoCorrectHebrew;
                case KeyNamesGuard: return NamesGuard;
            }
            return false;
        }

        /// <summary>Set one option and persist all of them.</summary>
        public void Set(string key, bool on)
        {
            switch (key)
            {
                case KeyAutoCorrect: AutoCorrect = on; break;
                case KeyAggressive: AutoCorrectAggressive = on; break;
                case KeyHebrew: AutoCorrectHebrew = on; break;
                case KeyNamesGuard: NamesGuard = on; break;
                default: return;
            }
            if (SettingsPath == null) return;
            try
            {
                File.WriteAllLines(SettingsPath, new[]
                {
                    "# LangFixer settings (1 = on, 0 = off)",
                    "# autocorrect: fix typos with a typing signature (swapped or neighbouring letters)",
                    KeyAutoCorrect + "=" + (AutoCorrect ? "1" : "0"),
                    "# autocorrect_aggressive: also take any one-letter dictionary suggestion (turned postgres into postures)",
                    KeyAggressive + "=" + (AutoCorrectAggressive ? "1" : "0"),
                    "# autocorrect_hebrew: autocorrect Hebrew too (the Windows Hebrew checker is unreliable)",
                    KeyHebrew + "=" + (AutoCorrectHebrew ? "1" : "0"),
                    "# names_guard: leave a lowercase unknown word alone right after another unknown word",
                    KeyNamesGuard + "=" + (NamesGuard ? "1" : "0")
                }, Encoding.UTF8);
            }
            catch { }
        }

        public void SetAutoCorrect(bool on) { Set(KeyAutoCorrect, on); }

        private static void Load(string path, HashSet<string> into)
        {
            if (path == null) return;
            into.Clear();
            try
            {
                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    into.Add(line);
                }
            }
            catch { }
        }

        public bool IsIgnoredWord(string word)
        {
            if (word.Length == 0) return false;
            lock (_lock) return _ignore.Contains(word);
        }

        public void AddIgnoredWord(string word)
        {
            word = word.Trim();
            if (word.Length == 0) return;
            lock (_lock)
            {
                if (!_ignore.Add(word)) return;
            }
            if (IgnorePath == null) return;
            try { File.AppendAllText(IgnorePath, word + Environment.NewLine, Encoding.UTF8); } catch { }
        }

        public bool IsExcludedApp(string processName)
        {
            if (processName.Length == 0) return false;
            lock (_lock) return _excluded.Contains(processName);
        }

        // ---- process name of a window ----

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder exe, ref uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr h);

        public static string ProcessNameOf(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "";
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return "";
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                if (!QueryFullProcessImageName(h, 0, sb, ref size)) return "";
                return Path.GetFileName(sb.ToString(0, (int)size));
            }
            catch { return ""; }
            finally { CloseHandle(h); }
        }
    }
}
