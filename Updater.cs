using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;

namespace LangFixer
{
    internal static class Updater
    {
        public const string CurrentVersion = "1.4";

        private const string ReleasesUrl = "https://api.github.com/repos/seyaltz/LangFixer/releases/latest";
        private const long MinExeSize = 10 * 1024; // 10 KB sanity check

        /// <summary>Parse "v1.3" or "1.3" into major and minor. Returns false on garbage.</summary>
        internal static bool ParseVersion(string s, out int major, out int minor)
        {
            major = minor = 0;
            if (s == null) return false;
            s = s.TrimStart('v', 'V');
            int dot = s.IndexOf('.');
            if (dot < 1 || dot == s.Length - 1) return false;
            return int.TryParse(s.Substring(0, dot), out major)
                && int.TryParse(s.Substring(dot + 1), out minor);
        }

        /// <summary>True when remoteTag is strictly newer than localVersion.</summary>
        internal static bool IsNewer(string remoteTag, string localVersion)
        {
            int rMaj, rMin, lMaj, lMin;
            if (!ParseVersion(remoteTag, out rMaj, out rMin)) return false;
            if (!ParseVersion(localVersion, out lMaj, out lMin)) return false;
            return rMaj > lMaj || (rMaj == lMaj && rMin > lMin);
        }

        /// <summary>
        /// Minimal JSON string-field extractor. Finds "key":"value" and returns value.
        /// Handles escaped quotes inside the value. No JSON library needed (C# 5 / csc.exe constraint).
        /// </summary>
        internal static string ExtractJsonString(string json, string key)
        {
            if (json == null || key == null) return null;
            string needle = "\"" + key + "\"";
            int ki = json.IndexOf(needle, StringComparison.Ordinal);
            if (ki < 0) return null;
            int afterKey = ki + needle.Length;
            // skip optional whitespace and the colon
            int colon = json.IndexOf(':', afterKey);
            if (colon < 0) return null;
            int qOpen = json.IndexOf('"', colon + 1);
            if (qOpen < 0) return null;
            // find closing quote, skipping escaped quotes
            int i = qOpen + 1;
            while (i < json.Length)
            {
                if (json[i] == '\\') { i += 2; continue; }
                if (json[i] == '"') break;
                i++;
            }
            if (i >= json.Length) return null;
            return json.Substring(qOpen + 1, i - qOpen - 1);
        }

        /// <summary>
        /// Checks GitHub for the latest release. Returns { tag, downloadUrl } if a newer version exists, null otherwise.
        /// Throws on network errors.
        /// </summary>
        public static string[] CheckForUpdate()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "LangFixer/" + CurrentVersion;
                string json = wc.DownloadString(ReleasesUrl);
                string tag = ExtractJsonString(json, "tag_name");
                if (tag == null || !IsNewer(tag, CurrentVersion)) return null;
                string url = ExtractJsonString(json, "browser_download_url");
                if (url == null) return null;
                return new[] { tag, url };
            }
        }

        /// <summary>
        /// Downloads the new exe to .new, then renames running exe to .old and .new to the original path.
        /// The progress callback receives percentage 0-100 on the calling (background) thread.
        /// </summary>
        public static void DownloadAndReplace(string url, Action<int> progress)
        {
            string exePath = AppDomain.CurrentDomain.BaseDirectory
                           + Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);
            string newPath = exePath + ".new";
            string oldPath = exePath + ".old";

            // Clean up leftovers from a previous failed attempt
            if (File.Exists(newPath)) File.Delete(newPath);
            if (File.Exists(oldPath)) File.Delete(oldPath);

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "LangFixer/" + CurrentVersion;
                var done = new ManualResetEvent(false);
                Exception error = null;
                wc.DownloadProgressChanged += delegate(object s, DownloadProgressChangedEventArgs e)
                {
                    if (progress != null) progress(e.ProgressPercentage);
                };
                wc.DownloadFileCompleted += delegate(object s, System.ComponentModel.AsyncCompletedEventArgs e)
                {
                    error = e.Error;
                    done.Set();
                };
                wc.DownloadFileAsync(new Uri(url), newPath);
                done.WaitOne();
                if (error != null) throw new Exception("Download failed: " + error.Message, error);
            }

            var fi = new FileInfo(newPath);
            if (!fi.Exists || fi.Length < MinExeSize)
                throw new Exception("Downloaded file is too small or missing (" + (fi.Exists ? fi.Length + " bytes" : "missing") + ")");

            // Windows allows renaming a running exe
            File.Move(exePath, oldPath);
            File.Move(newPath, exePath);
        }

        /// <summary>Launch the new exe and terminate this process.</summary>
        public static void Restart(string exePath)
        {
            Process.Start(exePath);
            Environment.Exit(0);
        }

        /// <summary>Delete the .old leftover from a previous update. Called at startup.</summary>
        public static void CleanupOldExe()
        {
            try
            {
                string oldPath = Process.GetCurrentProcess().MainModule.FileName + ".old";
                if (File.Exists(oldPath)) File.Delete(oldPath);
            }
            catch { }
        }
    }
}
