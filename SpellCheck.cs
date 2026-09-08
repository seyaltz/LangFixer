using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace LangFixer
{
    // Windows Spell Checking API (spellcheck.h), Windows 8+. In-proc COM; vtable order matters.

    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISpellCheckerFactory
    {
        IEnumString SupportedLanguages { [return: MarshalAs(UnmanagedType.Interface)] get; }
        [return: MarshalAs(UnmanagedType.Bool)]
        bool IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
        [return: MarshalAs(UnmanagedType.Interface)]
        ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
    }

    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISpellChecker
    {
        string LanguageTag { [return: MarshalAs(UnmanagedType.LPWStr)] get; }
        [return: MarshalAs(UnmanagedType.Interface)]
        IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string text);
        [return: MarshalAs(UnmanagedType.Interface)]
        IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);
        // Remaining vtable slots are not used by this program.
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumSpellingError
    {
        /// <summary>S_OK with a raw ISpellingError pointer, or S_FALSE when there are no more errors.
        /// The error object is never inspected, so it is kept as IntPtr and released by hand.</summary>
        [PreserveSig]
        int Next(out IntPtr value);
    }

    /// <summary>
    /// Word validity for English and Hebrew. Uses the Windows spell checker when the language's
    /// proofing tools are installed, otherwise falls back to structural heuristics.
    /// </summary>
    internal sealed class Dictionaries
    {
        private static readonly Guid FactoryClsid = new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC");

        private ISpellChecker _en;
        private ISpellChecker _he;

        public bool EnglishAvailable { get { return _en != null; } }
        public bool HebrewAvailable { get { return _he != null; } }
        public string InitError = "";

        public static Dictionaries Create()
        {
            var d = new Dictionaries();
            try
            {
                Type t = Type.GetTypeFromCLSID(FactoryClsid);
                var factory = (ISpellCheckerFactory)Activator.CreateInstance(t);
                d._en = TryCreate(factory, new[] { "en-US", "en-GB", "en" });
                d._he = TryCreate(factory, new[] { "he-IL", "he" });
            }
            catch (Exception ex)
            {
                d.InitError = ex.Message;
            }
            return d;
        }

        private static ISpellChecker TryCreate(ISpellCheckerFactory f, string[] tags)
        {
            foreach (var tag in tags)
            {
                try
                {
                    if (f.IsSupported(tag)) return f.CreateSpellChecker(tag);
                }
                catch { }
            }
            return null;
        }

        /// <summary>Last exception raised by a spell-check call, for the debug log. Empty when the last call succeeded.</summary>
        public string LastError = "";

        private bool NoErrors(ISpellChecker c, string word)
        {
            try
            {
                IEnumSpellingError e = c.Check(word);
                IntPtr err;
                int hr = e.Next(out err);
                if (err != IntPtr.Zero) Marshal.Release(err);
                LastError = "";
                return hr != 0; // S_FALSE (1) = enumeration empty = no spelling errors
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>Dictionary suggestions for a misspelled word, best first. Empty when the language has no checker.</summary>
        public System.Collections.Generic.List<string> Suggest(Lang lang, string word)
        {
            var result = new System.Collections.Generic.List<string>();
            ISpellChecker c = lang == Lang.Hebrew ? _he : _en;
            if (c == null) return result;
            try
            {
                IEnumString e = c.Suggest(word);
                var one = new string[1];
                while (result.Count < 10 && e.Next(1, one, IntPtr.Zero) == 0)
                    result.Add(one[0]);
                LastError = "";
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
            }
            return result;
        }

        // ---- English ----

        public static bool IsStructurallyEnglish(string w)
        {
            if (w.Length < 2) return false;
            for (int i = 0; i < w.Length; i++)
            {
                char c = w[i];
                bool letter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                bool innerApostrophe = c == '\'' && i > 0 && i < w.Length - 1;
                if (!letter && !innerApostrophe) return false;
            }
            return true;
        }

        public bool IsValidEnglish(string w)
        {
            if (!IsStructurallyEnglish(w)) return false;
            if (_en != null) return NoErrors(_en, w);
            // Fallback: needs a vowel. Weak, but the Hebrew side gate does most of the work.
            return w.IndexOfAny(new[] { 'a', 'e', 'i', 'o', 'u', 'y', 'A', 'E', 'I', 'O', 'U', 'Y' }) >= 0;
        }

        // ---- Hebrew ----

        private static bool IsHebrewLetter(char c) { return c >= 'א' && c <= 'ת'; }
        private static bool IsFinalForm(char c)
        {
            return c == 'ך' || c == 'ם' || c == 'ן' || c == 'ף' || c == 'ץ'; // ך ם ן ף ץ
        }
        private static bool HasFinalFormButIsNot(char c)
        {
            // כ מ נ צ written in non-final form; פ is excluded because loanwords end in it.
            return c == 'כ' || c == 'מ' || c == 'נ' || c == 'צ';
        }

        public static bool IsStructurallyHebrew(string w)
        {
            if (w.Length < 2) return false;
            for (int i = 0; i < w.Length; i++)
            {
                char c = w[i];
                bool last = i == w.Length - 1;
                bool innerMark = (c == '\'' || c == '"' || c == '׳' || c == '״') && i > 0 && !last;
                if (!IsHebrewLetter(c) && !innerMark) return false;
                if (IsFinalForm(c) && !last) return false;
                if (last && HasFinalFormButIsNot(c)) return false;
            }
            return true;
        }

        public bool IsValidHebrew(string w)
        {
            if (!IsStructurallyHebrew(w)) return false;
            if (_he != null) return NoErrors(_he, w);
            return true; // structural check only
        }
    }
}
