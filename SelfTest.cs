using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace LangFixer
{
    /// <summary>
    /// `LangFixer.exe --test`: exercises layout rendering, the spell checkers and the decision logic
    /// on this machine without installing any hook. Output goes to the parent console and test-output.txt.
    /// </summary>
    internal static class SelfTest
    {
        private static readonly StringBuilder Out = new StringBuilder();
        private static int _failures;

        private static void Line(string s)
        {
            Out.AppendLine(s);
            Console.WriteLine(s);
        }

        private static void Check(bool ok, string what)
        {
            if (!ok) _failures++;
            Line((ok ? "PASS " : "FAIL ") + what);
        }

        /// <summary>Build key records from an ASCII string as if typed on the physical US keyboard.</summary>
        private static List<KeyRec> Keys(string ascii, IntPtr typedHkl)
        {
            var list = new List<KeyRec>();
            foreach (char c in ascii)
            {
                var k = new KeyRec();
                char lower = char.ToLowerInvariant(c);
                if (lower >= 'a' && lower <= 'z') { k.Vk = (uint)(lower - 'a' + 0x41); k.Shift = char.IsUpper(c); }
                else if (c == ';') k.Vk = 0xBA;
                else if (c == ',') k.Vk = 0xBC;
                else if (c == '.') k.Vk = 0xBE;
                else if (c == '/') k.Vk = 0xBF;
                else if (c == '\'') k.Vk = 0xDE;
                else throw new ArgumentException("unsupported test char " + c);
                k.Scan = Native.MapVirtualKey(k.Vk, 0);
                k.Typed = Layouts.Render(k, typedHkl);
                list.Add(k);
            }
            return list;
        }

        public static void Run()
        {
            Native.AttachConsole(-1);
            Console.WriteLine();

            var layouts = Layouts.Discover();
            Line("English HKL: " + layouts.English.ToString("X") + "   Hebrew HKL: " + layouts.Hebrew.ToString("X"));
            Check(layouts.Complete, "both layouts installed");
            if (!layouts.Complete) { Finish(); return; }

            var dict = Dictionaries.Create();
            Line("Spell checker: English=" + dict.EnglishAvailable + " Hebrew=" + dict.HebrewAvailable
                + (dict.InitError.Length > 0 ? " error=" + dict.InitError : ""));

            // Rendering
            string shalom = Layouts.Render(Keys("akuo", layouts.English), layouts.Hebrew);
            Check(shalom == "שלום", "render 'akuo' under Hebrew layout = shalom, got '" + shalom + "'");
            string hello = Layouts.Render(Keys("hello", layouts.Hebrew), layouts.English);
            Check(hello == "hello", "render h,e,l,l,o under English layout, got '" + hello + "'");
            string helloHe = Layouts.Render(Keys("hello", layouts.Hebrew), layouts.Hebrew);
            Line("     h,e,l,l,o under Hebrew layout = '" + helloHe + "'");
            string shiftHe = Layouts.Render(Keys("Hello", layouts.Hebrew), layouts.Hebrew);
            Line("     H(shift),e,l,l,o under Hebrew layout = '" + shiftHe + "' (len " + shiftHe.Length + ")");

            // Dictionaries
            Check(dict.IsValidEnglish("hello"), "en: hello valid");
            Check(!dict.IsValidEnglish("akuo"), "en: akuo invalid");
            Line("     en: cd valid? " + dict.IsValidEnglish("cd") + "   he: bet-gimel valid? " + dict.IsValidHebrew("בג"));
            Check(dict.IsValidHebrew("שלום"), "he: shalom valid");
            Check(!dict.IsValidHebrew("יקךךם"), "he: 'hello' garbage invalid");
            Check(dict.IsValidHebrew("את"), "he: et valid");
            Check(dict.IsValidHebrew("מחשב"), "he: machshev valid");

            // Decisions (in-memory settings so the test never touches the real lists)
            var settings = new Settings(new[] { "api", "aws" }, new[] { "Code.exe" });
            var det = new Detector(dict, settings);
            Decide(det, layouts, "api", Lang.English, false, "api -> keep (ignore list)");
            Decide(det, layouts, "aws", Lang.English, false, "aws -> keep (ignore list)");
            Decide(det, layouts, "aui", Lang.English, true, "aui (non-word that spells a Hebrew word) -> converts when not listed");
            settings.AddIgnoredWord("aui");
            Decide(det, layouts, "aui", Lang.English, false, "aui -> keep after learning");
            Check(settings.IsExcludedApp("code.exe"), "excluded app match is case-insensitive");
            Decide(det, layouts, "akuo", Lang.English, true, "shalom typed in English layout -> fix");
            Decide(det, layouts, "hello", Lang.English, false, "hello typed in English -> keep");
            Decide(det, layouts, "hello", Lang.Hebrew, true, "hello typed in Hebrew layout -> fix");
            Decide(det, layouts, "akuo", Lang.Hebrew, false, "shalom typed in Hebrew -> keep");
            Decide(det, layouts, "tbh", Lang.English, true, "ani typed in English -> fix");
            Decide(det, layouts, "njac", Lang.English, true, "machshev typed in English -> fix");
            Decide(det, layouts, "gradle", Lang.English, false, "gradle (not a word, garbage Hebrew) -> keep");
            Decide(det, layouts, "cd", Lang.English, false, "cd -> keep");
            Decide(det, layouts, "js", Lang.English, false, "js -> keep (2-letter guard)");
            Decide(det, layouts, "Hello", Lang.Hebrew, true, "Hello with shift typed in Hebrew -> fix");
            Decide(det, layouts, "akuo'", Lang.English, true, "shalom + Hebrew comma key -> fix");
            Decide(det, layouts, "akuo,", Lang.English, true, "shalom + English comma key -> fix");
            Decide(det, layouts, "akuo.", Lang.English, true, "shalom + English period key -> fix");
            Decide(det, layouts, "hello,", Lang.Hebrew, true, "hello, typed in Hebrew -> fix");
            Decide(det, layouts, "hello,", Lang.English, false, "hello, typed in English -> keep");
            Decide(det, layouts, "the", Lang.Hebrew, true, "the typed in Hebrew -> fix");
            Decide(det, layouts, "a", Lang.English, false, "single letter 'a' typed in English -> keep");
            Decide(det, layouts, "i", Lang.Hebrew, true, "'i' typed in Hebrew layout (final nun alone) -> fix to i");
            Decide(det, layouts, "a", Lang.Hebrew, true, "'a' typed in Hebrew layout (shin alone) -> fix to a");
            Decide(det, layouts, "c", Lang.Hebrew, false, "'c' typed in Hebrew layout (bet alone) -> keep");
            Decide(det, layouts, "vhv", Lang.English, true, "haya typed in English -> fix");
            Decide(det, layouts, "npm", Lang.English, false, "npm -> keep");
            Decide(det, layouts, "it's", Lang.Hebrew, true, "it's typed in Hebrew -> fix");
            Decide(det, layouts, "how", Lang.Hebrew, true, "how typed in Hebrew (W key = geresh) -> fix");
            Decide(det, layouts, "we", Lang.Hebrew, true, "we typed in Hebrew -> fix");
            Decide(det, layouts, "what", Lang.Hebrew, true, "what typed in Hebrew -> fix");
            Decide(det, layouts, "LON", Lang.Hebrew, false, "LON with Shift in Hebrew layout already reads LON -> keep, layout stays Hebrew");
            Decide(det, layouts, "DEADLOCK", Lang.Hebrew, false, "DEADLOCK with Shift in Hebrew layout -> keep");
            Decide(det, layouts, "please", Lang.Hebrew, true, "please typed in Hebrew -> fix");
            Decide(det, layouts, "vhv/", Lang.English, true, "haya + Hebrew period key -> fix");

            // Spelling autocorrect (dictionary suggestions, one edit away)
            Check(Detector.IsOneEditAway("teh", "the"), "one edit: transposition");
            Check(Detector.IsOneEditAway("helo", "hello"), "one edit: missing letter");
            Check(Detector.IsOneEditAway("helllo", "hello"), "one edit: extra letter");
            Check(Detector.IsOneEditAway("hallo", "hello"), "one edit: wrong letter");
            Check(!Detector.IsOneEditAway("hxllx", "hello"), "two edits rejected");
            Check(!Detector.IsOneEditAway("hello", "hello"), "identical rejected");
            var sugg = dict.Suggest(Lang.English, "teh");
            Line("     suggestions for 'teh': " + string.Join(", ", sugg.ToArray()));
            Check(sugg.Contains("the"), "en: suggestions for teh include the");
            var suggHe = dict.Suggest(Lang.Hebrew, "שלוום");
            Line("     he: shalom+extra vav valid? " + dict.IsValidHebrew("שלוום") + "; suggestions: " + string.Join(", ", suggHe.ToArray())
                + "  (the Windows Hebrew checker is permissive, Hebrew autocorrect rarely triggers)");
            var acSettings = new Settings(new[] { "gradle" }, new string[0]);
            var acDet = new Detector(dict, acSettings);
            Decide(acDet, layouts, "teh", Lang.English, true, "autocorrect OFF: teh -> layout fix to aleph-kuf-yod, which the Hebrew checker accepts (known permissiveness)");
            acSettings.AutoCorrect = true;
            DecideText(acDet, layouts, "teh", Lang.English, "the", "autocorrect ON: teh -> the (spelling outranks the layout switch)");
            DecideText(acDet, layouts, "teh,", Lang.English, "the,", "autocorrect ON: teh, -> the, (punctuation kept)");
            // Strict mode (default): only typo-signature corrections. Cases from a day of real typing.
            Decide(acDet, layouts, "helo", Lang.English, false, "strict: helo kept (hello is a weak, one-insertion suggestion)");
            Decide(acDet, layouts, "postgres", Lang.English, false, "strict: postgres kept (was 'postures')");
            Decide(acDet, layouts, "poull", Lang.English, false, "strict: poull kept (was 'poll', user meant pull)");
            Decide(acDet, layouts, "deplink", Lang.English, false, "strict: deplink kept (was 'delink')");
            Decide(acDet, layouts, "etc", Lang.English, false, "etc kept (suggestion 'etc.' only adds punctuation)");
            Check(!Detector.AcceptableSuggestion("etc", "etc."), "AcceptableSuggestion rejects punctuation-only 'etc.'");
            DecideText(acDet, layouts, "eurv", Lang.English, "קורה", "layout beats spelling: eurv -> kore, not the transposition 'eruv'");
            acSettings.AutoCorrectAggressive = true;
            DecideText(acDet, layouts, "helo", Lang.English, "hello", "aggressive: helo -> hello");
            acSettings.AutoCorrectAggressive = false;
            // Names guard: 'tal' alone still converts (indistinguishable from thl -> eich); after an unknown word it is left alone.
            DecideText(acDet, layouts, "tal", Lang.English, "אשך", "names guard off-context: tal alone -> ashach (documented limitation, undo teaches it)");
            var gk = Keys("tal", layouts.English);
            var gd = acDet.Decide(Lang.English, Layouts.Typed(gk), Layouts.Render(gk, layouts.English), Layouts.Render(gk, layouts.Hebrew), true);
            Check(!gd.Fix, "names guard: tal after an unknown word -> keep  [" + gd.Reason + "]");
            var ek = Keys("eurv", layouts.English);
            var ed = acDet.Decide(Lang.English, Layouts.Typed(ek), Layouts.Render(ek, layouts.English), Layouts.Render(ek, layouts.Hebrew), true);
            Check(ed.Fix && ed.Text == "קורה", "names guard exempts a 4-letter valid Hebrew word: eurv after 'postgres' -> kore  [" + ed.Reason + "]");
            var pk = Keys("webguru", layouts.English);
            var pd = acDet.Decide(Lang.English, Layouts.Typed(pk), Layouts.Render(pk, layouts.English), Layouts.Render(pk, layouts.Hebrew), false);
            Check(!pd.Fix && pd.Unknown, "webguru alone -> keep and flagged unknown (feeds the guard)  [" + pd.Reason + "]");
            var ak = Keys("ayash", layouts.English);
            var ad = acDet.Decide(Lang.English, Layouts.Typed(ak), Layouts.Render(ak, layouts.English), Layouts.Render(ak, layouts.Hebrew), false);
            Line("     documented limitation: a name with a typo signature is still corrected when it stands alone: ayash -> " + (ad.Fix ? "'" + ad.Text + "'" : "keep"));
            var ad2 = acDet.Decide(Lang.English, Layouts.Typed(ak), Layouts.Render(ak, layouts.English), Layouts.Render(ak, layouts.Hebrew), true);
            Check(!ad2.Fix, "names guard: ayash after an unknown word -> keep  [" + ad2.Reason + "]");
            // Hebrew autocorrect is off by default
            var hk = Keys("csh,v", layouts.Hebrew); // בדיכה (typo of בדיחה)
            var hd = acDet.Decide(Lang.Hebrew, Layouts.Typed(hk), Layouts.Render(hk, layouts.English), Layouts.Render(hk, layouts.Hebrew));
            Check(!hd.Fix, "Hebrew autocorrect off by default: bedicha kept  [" + hd.Reason + "]");
            acSettings.AutoCorrectHebrew = true;
            var hd2 = acDet.Decide(Lang.Hebrew, Layouts.Typed(hk), Layouts.Render(hk, layouts.English), Layouts.Render(hk, layouts.Hebrew));
            Line("     Hebrew autocorrect on: bedicha -> " + (hd2.Fix ? "'" + hd2.Text + "'" : "keep") + "  [" + hd2.Reason + "]");
            acSettings.AutoCorrectHebrew = false;
            Decide(acDet, layouts, "gradle", Lang.English, false, "autocorrect ON: ignored word untouched");
            // Rare short English words do not win over Hebrew (from Eyal's log: יטולת became "hyuk,")
            Decide(acDet, layouts, "hyuk,", Lang.Hebrew, false, "hyuk, typed in Hebrew (a slip of yecholet) -> keep, 'hyuk' is not an everyday word");
            Decide(acDet, layouts, "hyuk", Lang.Hebrew, false, "hyuk -> keep");
            Decide(det, layouts, "did", Lang.Hebrew, true, "did typed in Hebrew still fixes (everyday word)");
            Decide(det, layouts, "text", Lang.Hebrew, true, "text typed in Hebrew still fixes");
            Decide(det, layouts, "workflow", Lang.Hebrew, true, "workflow typed in Hebrew still fixes (6+ letters)");
            Check(!CommonWords.English.Contains("hyuk") && CommonWords.English.Contains("Hello"), "common English list: hyuk absent, Hello present (case-insensitive)");
            // Cross-layout typo repair (from Eyal's log: 'chsev' for בדיקה, h and s swapped)
            DecideText(acDet, layouts, "cshev", Lang.English, "בדיקה", "bdika typed correctly in English layout -> plain layout fix");
            DecideText(acDet, layouts, "chsev", Lang.English, "בדיקה", "cross-layout typo: chsev (swapped pair) -> bdika");
            DecideText(acDet, layouts, "hlelo", Lang.Hebrew, "hello", "cross-layout typo the other way: hlelo on Hebrew layout -> hello");
            acSettings.AutoCorrect = false;
            Decide(acDet, layouts, "chsev", Lang.English, false, "cross-layout repair needs the autocorrect switch: off -> keep");
            acSettings.AutoCorrect = true;
            Decide(acDet, layouts, "Teh", Lang.English, false, "autocorrect ON: capitalized word untouched");
            DecideText(acDet, layouts, "akuo", Lang.English, "שלום", "autocorrect ON: layout fix still applies (akuo -> shalom, not the suggestion 'akua')");
            DecideText(acDet, layouts, "thl", Lang.English, "איך", "autocorrect ON: thl -> eich (suggestion 'the' is not a neighbour-key typo)");
            DecideText(acDet, layouts, "hwllo", Lang.English, "hello", "autocorrect ON: hwllo -> hello (w is next to e: typo signature)");
            Check(Detector.AreNeighbourKeys('w', 'e') && Detector.AreNeighbourKeys('s', 'w') && !Detector.AreNeighbourKeys('o', 'a'), "QWERTY neighbour map");
            Check(Detector.OneEdit("teh", "the") == Detector.EditKind.Transposition, "teh/the is a transposition");
            Check(!Detector.AcceptableSuggestion("heald", "Heald") && !Detector.AcceptableSuggestion("hello", "Hello")
                && Detector.AcceptableSuggestion("teh", "the") && Detector.AcceptableSuggestion("Teh", "The"),
                "suggestions: capitalized/case-only rejected for lowercase words (live bug: heald -> Heald)");
            var dh = acDet.Decide(Lang.English, "heald", "heald", Layouts.Render(Keys("heald", layouts.English), layouts.Hebrew));
            Check(!dh.Fix || dh.Text != "Heald", "autocorrect ON: heald never becomes Heald -> " + (dh.Fix ? "'" + dh.Text + "'" : "keep"));
            DecideText(acDet, layouts, "hello", Lang.Hebrew, "hello", "autocorrect ON: hello typed in Hebrew still becomes hello");
            Decide(acDet, layouts, "hello", Lang.English, false, "autocorrect ON: valid word untouched");

            // Forced (hotkey) conversion text
            Forced(layouts, "akuo,", Lang.Hebrew, "שלום,", "forced: akuo, -> shalom + comma");
            Forced(layouts, "akuo/", Lang.Hebrew, "שלום.", "forced: akuo/ -> shalom + Hebrew period");
            Forced(layouts, "akuo", Lang.Hebrew, "שלום", "forced: akuo -> shalom");
            Forced(layouts, "hello,", Lang.English, "hello,", "forced: to English keeps English rendering");

            // The path the hook actually uses: worker thread + cache.
            using (var svc = new DecisionService(new Settings(new string[0], new string[0]), null))
            {
                var d1 = svc.Decide(Lang.English, "akuo", "akuo", "שלום", false, 3000);
                Check(d1.Fix && d1.Text == "שלום", "DecisionService: akuo -> shalom via worker thread" + (d1.Reason.Length > 0 ? "  [" + d1.Reason + "]" : ""));
                svc.Prefetch(Lang.Hebrew, "יקךךם", "hello", "יקךךם", false);
                Thread.Sleep(200);
                var d2 = svc.Decide(Lang.Hebrew, "יקךךם", "hello", "יקךךם", false, 0);
                Check(d2.Fix && d2.Text == "hello", "DecisionService: prefetched verdict served from cache with zero wait");
            }

            // Risk sweep: which 2- and 3-letter English-layout strings would convert?
            Sweep(det, layouts, 2);
            Sweep(det, layouts, 3, 400);
            // Information only: what a 2-letter Hebrew minimum would convert (currently disabled by MinHebrewLength = 3).
            var det2 = new Detector(dict, settings) { MinHebrewLength = 2 };
            Line("     --- if MinHebrewLength were 2 ---");
            Sweep(det2, layouts, 2);

            Finish();
        }

        private static void Sweep(Detector det, Layouts layouts, int len, int limit)
        {
            var hits = new List<string>();
            int total = 0;
            var buf = new char[len];
            SweepRec(det, layouts, buf, 0, hits, ref total);
            Line("     sweep " + len + "-letter: " + hits.Count + " of " + total + " would convert" + (hits.Count > limit ? " (first " + limit + ")" : ""));
            var sb = new StringBuilder("     ");
            for (int i = 0; i < hits.Count && i < limit; i++) { sb.Append(hits[i]).Append(' '); if (sb.Length > 110) { Line(sb.ToString()); sb.Clear(); sb.Append("     "); } }
            if (sb.Length > 5) Line(sb.ToString());
        }

        private static void Sweep(Detector det, Layouts layouts, int len) { Sweep(det, layouts, len, 1000); }

        private static void SweepRec(Detector det, Layouts layouts, char[] buf, int pos, List<string> hits, ref int total)
        {
            if (pos == buf.Length)
            {
                total++;
                string s = new string(buf);
                var keys = Keys(s, layouts.English);
                var d = det.Decide(Lang.English, Layouts.Typed(keys), Layouts.Render(keys, layouts.English), Layouts.Render(keys, layouts.Hebrew));
                if (d.Fix) hits.Add(s + ">" + d.Text);
                return;
            }
            for (char c = 'a'; c <= 'z'; c++) { buf[pos] = c; SweepRec(det, layouts, buf, pos + 1, hits, ref total); }
        }

        private static void Decide(Detector det, Layouts layouts, string ascii, Lang typedIn, bool expectFix, string what)
        {
            var keys = Keys(ascii, layouts.HklFor(typedIn));
            string en = Layouts.Render(keys, layouts.English);
            string he = Layouts.Render(keys, layouts.Hebrew);
            var d = det.Decide(typedIn, Layouts.Typed(keys), en, he);
            Check(d.Fix == expectFix, what + (d.Fix ? "  => '" + d.Text + "'" : "") + (d.Reason.Length > 0 ? "  [" + d.Reason + "]" : ""));
        }

        private static void DecideText(Detector det, Layouts layouts, string ascii, Lang typedIn, string expectedText, string what)
        {
            var keys = Keys(ascii, layouts.HklFor(typedIn));
            var d = det.Decide(typedIn, Layouts.Typed(keys), Layouts.Render(keys, layouts.English), Layouts.Render(keys, layouts.Hebrew));
            Check(d.Fix && d.Text == expectedText, what + " -> got " + (d.Fix ? "'" + d.Text + "'" : "keep") + "  [" + d.Reason + "]");
        }

        private static void Forced(Layouts layouts, string ascii, Lang target, string expected, string what)
        {
            var keys = Keys(ascii, layouts.English);
            string got = Detector.ForcedText(target, Layouts.Render(keys, layouts.English), Layouts.Render(keys, layouts.Hebrew));
            Check(got == expected, what + " -> got '" + got + "'");
        }

        private static void Finish()
        {
            Line(_failures == 0 ? "ALL PASSED" : _failures + " FAILED");
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-output.txt");
                File.WriteAllText(path, Out.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }
}
