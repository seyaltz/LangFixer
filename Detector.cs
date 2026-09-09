using System;

namespace LangFixer
{
    internal sealed class Decision
    {
        public bool Fix;
        public Lang Target;
        public string Text = "";
        public string Reason = "";
        /// <summary>Kept, and the word fails BOTH dictionaries: the signature of a name or an identifier. Feeds the names guard for the next word.</summary>
        public bool Unknown;

        public static readonly Decision None = new Decision();

        public static Decision Keep(string reason)
        {
            return new Decision { Reason = reason };
        }

        public static Decision KeepUnknown(string reason)
        {
            return new Decision { Reason = reason, Unknown = true };
        }

        public static Decision To(Lang target, string text, string reason)
        {
            return new Decision { Fix = true, Target = target, Text = text, Reason = reason };
        }
    }

    /// <summary>
    /// Pure decision logic: given the word rendered under both layouts and the layout it was typed in,
    /// decide whether it was typed in the wrong layout. Converts only when the typed rendering fails
    /// its own language and the other rendering passes the other language, so identifiers and slang
    /// are left alone. Optionally, when it is not a layout mistake, corrects a single-letter typo from
    /// the dictionary's suggestions.
    /// </summary>
    internal sealed class Detector
    {
        /// <summary>Shortest Hebrew word we auto-convert to. 159 of the 676 two-letter combinations (js, ts, sh, ps...) spell real Hebrew words.</summary>
        public int MinHebrewLength = 3;
        /// <summary>Shortest English word we auto-convert to (besides the one-letter words "I" and "a").</summary>
        public int MinEnglishLength = 2;
        /// <summary>Shortest word we auto-correct for spelling.</summary>
        public int MinAutoCorrectLength = 3;
        /// <summary>A valid other-layout word at least this long beats a spelling suggestion ("eurv" is קורה, not "eruv").</summary>
        public int LayoutOverSpellingLength = 4;

        private readonly Dictionaries _dict;
        private readonly Settings _settings;
        private static readonly char[] TrailingPunct = { ',', '.', ';', '\'', '/', '"' };

        public Detector(Dictionaries dict, Settings settings) { _dict = dict; _settings = settings; }

        public static string TrimTrailing(string s)
        {
            return s.TrimEnd(TrailingPunct);
        }

        private static bool IsPunct(char c)
        {
            return Array.IndexOf(TrailingPunct, c) >= 0;
        }

        /// <summary>
        /// Strip trailing punctuation from <paramref name="s"/> only where the same key is punctuation in the
        /// other layout too. The W key renders as a geresh (') in Hebrew, so "how" typed in Hebrew is "ים'":
        /// that geresh is a letter's worth of typing, not punctuation, and must stay.
        /// </summary>
        public static string TrimTrailingAligned(string s, string other)
        {
            if (s.Length != other.Length) return TrimTrailing(s);
            int end = s.Length;
            while (end > 0 && IsPunct(s[end - 1]) && IsPunct(other[end - 1])) end--;
            return s.Substring(0, end);
        }

        /// <summary>
        /// Text for a forced (hotkey) conversion, no dictionary involved. Going to Hebrew, a trailing key that is
        /// punctuation in English but a letter in Hebrew (the , key is ת) stays punctuation: "akuo," becomes "שלום,".
        /// </summary>
        public static string ForcedText(Lang target, string english, string hebrew)
        {
            if (target != Lang.Hebrew) return english;
            string en = TrimTrailing(english);
            string trail = english.Substring(en.Length);
            if (trail.Length == 0 || english.Length != hebrew.Length) return hebrew;
            string heTail = hebrew.Substring(hebrew.Length - trail.Length);
            for (int i = 0; i < heTail.Length; i++)
                if (IsPunct(heTail[i])) return hebrew; // the Hebrew side is punctuation too (e.g. / gives a Hebrew period)
            return hebrew.Substring(0, hebrew.Length - trail.Length) + trail;
        }

        /// <param name="typedIn">Layout that was active while typing.</param>
        /// <param name="typed">What actually appeared on screen.</param>
        /// <param name="english">The keys rendered under the English layout.</param>
        /// <param name="hebrew">The keys rendered under the Hebrew layout.</param>
        /// <param name="prevUnknown">The previous word was kept because it fails both dictionaries (names guard input).</param>
        public Decision Decide(Lang typedIn, string typed, string english, string hebrew, bool prevUnknown = false)
        {
            Decision d;
            if (typedIn == Lang.English) d = EnglishLayout(english, hebrew, prevUnknown);
            else if (typedIn == Lang.Hebrew) d = HebrewLayout(english, hebrew, prevUnknown);
            else return Decision.None;
            // Shift/Caps in the Hebrew layout already produce Latin letters (acronyms inside Hebrew text):
            // rewriting them to the identical text would only flip the layout under the user's hands.
            if (d.Fix && d.Text == typed) return Decision.Keep("already reads as the target text");
            return d;
        }

        // Typed while the English layout was active: did they mean Hebrew?
        private Decision EnglishLayout(string english, string hebrew, bool prevUnknown)
        {
            // If they meant English, trailing , . ; ' are punctuation: plain trim for the English check.
            string en = TrimTrailing(english);
            if (_settings.IsIgnoredWord(en)) return Decision.Keep("ignored word");
            if (_dict.IsValidEnglish(en)) return Decision.Keep("valid English" + Err());
            if (_dict.LastError.Length > 0) return Decision.Keep("dictionary fault, keeping" + Err()); // never convert on a checker failure
            string trail = english.Substring(en.Length);

            // If they meant Hebrew, only keys that are punctuation in both layouts (' and / give Hebrew comma
            // and period) are punctuation; the W key's geresh stays part of the word.
            string he = TrimTrailingAligned(hebrew, english);
            bool heValid = he.Length >= MinHebrewLength && _dict.IsValidHebrew(he);
            string heVerdict = he.Length < MinHebrewLength ? "Hebrew too short" : heValid ? "" : "not Hebrew" + Err();

            // Names guard: a lowercase word that fails both dictionaries, right after another such word, is almost
            // always a name ("tal ayash") or an identifier, not a layout mistake or a typo. Leave it alone.
            bool lowercaseUnknown = Dictionaries.IsStructurallyEnglish(en) && en == en.ToLowerInvariant();
            if (_settings.NamesGuard && prevUnknown && lowercaseUnknown)
                return Decision.KeepUnknown("names guard: unknown word after an unknown word");

            bool strongTypo;
            string corrected = TryAutoCorrect(Lang.English, en, out strongTypo);

            // Priority, learned from a day of real typing:
            //  1. a valid Hebrew word of 4+ letters beats any spelling suggestion ("eurv" is קורה, not "eruv");
            //  2. a typo with a typing signature (swapped adjacent letters, neighbouring key) beats a short Hebrew
            //     word: the Hebrew checker is permissive ("teh" renders as אקי, which it accepts);
            //  3. a 3-letter valid Hebrew word;
            //  4. a weak spelling suggestion (only in aggressive mode) when nothing else applies.
            if (heValid && he.Length >= LayoutOverSpellingLength)
                return Decision.To(Lang.Hebrew, hebrew, "'" + en + "' is not English, '" + he + "' is Hebrew");
            if (corrected != null && strongTypo)
                return Decision.To(Lang.English, corrected + trail, "spelling (typo signature): '" + en + "' -> '" + corrected + "'");
            if (heValid)
                return Decision.To(Lang.Hebrew, hebrew, "'" + en + "' is not English, '" + he + "' is Hebrew");

            // They may have pressed the real , or . key after the Hebrew word: strip that trailing
            // English punctuation (one key = one char in both layouts here) and try again.
            if (trail.Length > 0 && trail.Length < hebrew.Length)
            {
                string hePrefix = TrimTrailing(hebrew.Substring(0, hebrew.Length - trail.Length));
                if (hePrefix.Length >= MinHebrewLength && _dict.IsValidHebrew(hePrefix))
                    return Decision.To(Lang.Hebrew, hePrefix + trail, "'" + en + "' is not English, '" + hePrefix + "' is Hebrew");
            }

            if (corrected != null)
                return Decision.To(Lang.English, corrected + trail, "spelling: '" + en + "' -> '" + corrected + "'");
            // Fails both dictionaries (and is not merely too short): remember it for the names guard.
            return lowercaseUnknown && he.Length >= MinHebrewLength ? Decision.KeepUnknown(heVerdict) : Decision.Keep(heVerdict);
        }

        // Typed while the Hebrew layout was active: did they mean English?
        private Decision HebrewLayout(string english, string hebrew, bool prevUnknown)
        {
            // Judge the Hebrew with aligned trimming so "how" (ים') is not mistaken for the word ים.
            string he = TrimTrailingAligned(hebrew, english);
            if (_settings.IsIgnoredWord(he)) return Decision.Keep("ignored word");
            if (he.Length >= 1 && _dict.IsValidHebrew(he)) return Decision.Keep("valid Hebrew" + Err());
            if (_dict.LastError.Length > 0) return Decision.Keep("dictionary fault, keeping" + Err());

            // If they meant English, a trailing , . ; ' is punctuation ("hello," typed in Hebrew).
            string en = TrimTrailing(english);
            bool enValid = en.Length >= MinEnglishLength && _dict.IsValidEnglish(en);

            string heTrail = hebrew.Substring(he.Length);
            bool strongTypo = false;
            string corrected = null;
            // Hebrew slang after Hebrew slang: the names guard only withholds autocorrect here; English typed in the
            // Hebrew layout is strong evidence on its own and is still fixed.
            if (!(_settings.NamesGuard && prevUnknown)) corrected = TryAutoCorrect(Lang.Hebrew, he, out strongTypo);
            if (corrected == null) strongTypo = false;

            if (enValid && en.Length >= LayoutOverSpellingLength)
                return Decision.To(Lang.English, english, "'" + he + "' is not Hebrew, '" + en + "' is English");
            if (corrected != null && strongTypo)
                return Decision.To(Lang.Hebrew, corrected + heTrail, "spelling (typo signature): '" + he + "' -> '" + corrected + "'");
            // "I" and "a" are the only one-letter English words; their Hebrew-layout renderings (ן, ש) are never words on their own.
            if (en == "i" || en == "I" || en == "a" || en == "A")
                return Decision.To(Lang.English, english, "'" + he + "' is not Hebrew, '" + en + "' is a one-letter English word");
            if (enValid)
                return Decision.To(Lang.English, english, "'" + he + "' is not Hebrew, '" + en + "' is English");
            if (corrected != null)
                return Decision.To(Lang.Hebrew, corrected + heTrail, "spelling: '" + he + "' -> '" + corrected + "'");
            string enVerdict = en.Length < MinEnglishLength ? "English too short" : "not English" + Err();
            return en.Length >= MinEnglishLength && he.Length >= 2 ? Decision.KeepUnknown(enVerdict) : Decision.Keep(enVerdict);
        }

        /// <summary>
        /// Dictionary-based autocorrect, deliberately narrow: the word must be letters only, at least
        /// <see cref="MinAutoCorrectLength"/> long, not capitalized (names) or all-caps (acronyms), and the
        /// dictionary's suggestion must be exactly one edit away (one wrong, missing, extra or swapped letter).
        /// Grammar is out of reach for a dictionary; only spelling is attempted.
        /// </summary>
        /// <param name="strongTypo">True when the edit has a typing signature: swapped adjacent letters, or a
        /// substitution by a physically neighbouring key. Such a correction may outrank a layout switch.</param>
        public string TryAutoCorrect(Lang lang, string word, out bool strongTypo)
        {
            strongTypo = false;
            if (!_settings.AutoCorrect) return null;
            if (lang == Lang.Hebrew && !_settings.AutoCorrectHebrew) return null; // the Windows Hebrew checker "fixed" correct words
            if (word.Length < MinAutoCorrectLength) return null;
            if (lang == Lang.English)
            {
                if (!Dictionaries.IsStructurallyEnglish(word) || word.IndexOf('\'') >= 0) return null;
                if (char.IsUpper(word[0])) return null; // names, sentence-initial words with typos are left alone too
            }
            else if (!Dictionaries.IsStructurallyHebrew(word)) return null;

            // Only the dictionary's FIRST acceptable one-edit suggestion counts. Walking further would turn
            // "helo" into "help" (a neighbour-key substitution) when "hello" was the obvious first choice.
            foreach (var s in _dict.Suggest(lang, word))
            {
                if (!AcceptableSuggestion(word, s)) continue;
                EditKind kind = OneEdit(word, s);
                if (kind == EditKind.None) continue;
                strongTypo = kind == EditKind.Transposition
                    || (kind == EditKind.Substitution && lang == Lang.English && AreNeighbourKeys(word, s));
                // A weak suggestion (any other single edit) produced "postures" for postgres and "poll" for pull
                // in real use: it applies only when the user opted into aggressive mode.
                if (!strongTypo && !_settings.AutoCorrectAggressive) return null;
                return s;
            }
            return null;
        }

        public string TryAutoCorrect(Lang lang, string word)
        {
            bool strong;
            return TryAutoCorrect(lang, word, out strong);
        }

        /// <summary>
        /// A suggestion may not be the word itself, contain a space, differ only by case, or be capitalized when the
        /// typed word is not: the dictionary offers proper nouns ("heald" -> "Heald") and those are not typos.
        /// </summary>
        public static bool AcceptableSuggestion(string word, string s)
        {
            if (s == null || s.Length == 0 || s == word || s.IndexOf(' ') >= 0) return false;
            if (string.Equals(s, word, StringComparison.OrdinalIgnoreCase)) return false;
            if (char.IsUpper(s[0]) && !char.IsUpper(word[0])) return false;
            // "etc" -> "etc.": a suggestion that only adds or moves punctuation is not a spelling correction.
            if (string.Equals(LettersOnly(s), LettersOnly(word), StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static string LettersOnly(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) if (char.IsLetter(c)) sb.Append(c);
            return sb.ToString();
        }

        public enum EditKind { None, Substitution, Transposition, Insertion, Deletion }

        /// <summary>Damerau-Levenshtein distance == 1: one substitution, insertion, deletion or adjacent transposition.</summary>
        public static bool IsOneEditAway(string a, string b)
        {
            return OneEdit(a, b) != EditKind.None;
        }

        /// <summary>Which single edit turns <paramref name="a"/> into <paramref name="b"/>, or None.</summary>
        public static EditKind OneEdit(string a, string b)
        {
            if (a == b) return EditKind.None;
            int la = a.Length, lb = b.Length;
            if (Math.Abs(la - lb) > 1) return EditKind.None;
            if (la == lb)
            {
                int diff = -1, count = 0;
                for (int i = 0; i < la; i++)
                {
                    if (a[i] != b[i]) { count++; if (diff < 0) diff = i; }
                }
                if (count == 1) return EditKind.Substitution;
                if (count == 2 && diff + 1 < la && a[diff] == b[diff + 1] && a[diff + 1] == b[diff]) return EditKind.Transposition;
                return EditKind.None;
            }
            string s = la < lb ? a : b, l = la < lb ? b : a; // s is one char shorter
            int i2 = 0, j2 = 0; bool skipped = false;
            while (i2 < s.Length && j2 < l.Length)
            {
                if (s[i2] == l[j2]) { i2++; j2++; continue; }
                if (skipped) return EditKind.None;
                skipped = true; j2++;
            }
            return la < lb ? EditKind.Insertion : EditKind.Deletion;
        }

        private static readonly string[] QwertyRows = { "qwertyuiop", "asdfghjkl", "zxcvbnm" };

        /// <summary>For a substitution: is the wrong letter on a key next to the right one on a QWERTY board?</summary>
        public static bool AreNeighbourKeys(string typed, string wanted)
        {
            if (typed.Length != wanted.Length) return false;
            for (int i = 0; i < typed.Length; i++)
            {
                if (typed[i] == wanted[i]) continue;
                return AreNeighbourKeys(char.ToLowerInvariant(typed[i]), char.ToLowerInvariant(wanted[i]));
            }
            return false;
        }

        public static bool AreNeighbourKeys(char x, char y)
        {
            int rx = -1, cx = -1, ry = -1, cy = -1;
            for (int r = 0; r < QwertyRows.Length; r++)
            {
                int ix = QwertyRows[r].IndexOf(x); if (ix >= 0) { rx = r; cx = ix; }
                int iy = QwertyRows[r].IndexOf(y); if (iy >= 0) { ry = r; cy = iy; }
            }
            if (rx < 0 || ry < 0) return false;
            // Rows are staggered by about half a key: a key touches its row neighbours and the two or three keys above/below.
            return Math.Abs(rx - ry) <= 1 && Math.Abs(cx - cy) <= 1;
        }

        private string Err()
        {
            return _dict.LastError.Length > 0 ? " [dict error: " + _dict.LastError + "]" : "";
        }
    }
}
