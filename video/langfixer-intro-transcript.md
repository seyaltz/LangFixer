# LangFixer walkthrough - narration transcript

Voice: Microsoft David Desktop, rate -1. 24 lines over a 328s video.

**00:00** LangFixer is a small Windows tray utility. When you type a word in the wrong keyboard layout, Hebrew on an English layout or English on a Hebrew one, it notices at the space, retypes the word in the language you meant, and switches the layout for you. This video shows how to get it, run it, and what it does, with a live demo.
**00:24** The same physical keys spell shalom on the Hebrew layout and a k u o on the English one. You only notice after the word is out. Then you delete it, switch the layout and type it again. LangFixer does those three steps for you, automatically, in any application.
**00:45** Three ways to get it. One: clone the repository and run build dot cmd. No SDK is needed, the C sharp compiler ships with every Windows machine. Two: download LangFixer one point zero dot zip from the releases page. Three: hand SPEC dot md to your own coding agent. The spec is complete; an independent agent rebuilt the program from it and passed all forty live checks.
**01:16** Requirements: Windows ten or eleven, with the English and Hebrew layouts installed, and the basic typing feature for both languages, which provides the spell checkers. The exe is unsigned, so SmartScreen warns once; choose more info, run anyway, or build it yourself. On privacy: LangFixer keeps only the current word in memory, writes nothing but its own settings, and has no network code.
**01:48** Run LangFixer dot exe. The dashboard opens with the status, a start and stop button, the dictionaries it found, and a live feed of every decision. Tick start with Windows so it is always there. Closing the window hides it to the tray; the blue aleph A icon brings it back. Spelling autocorrect is optional and off by default. Everything it learns lives in three small text files in your local app data folder.
**02:20** How it decides. It records the physical keys of the word, not the letters. At the space it renders those keys under both layouts and asks the Windows spell checkers about each rendering. It converts only when your rendering fails its own language and the other one passes. Then it deletes the word, switches the layout, and replays your keys so the application types it itself. Identifiers like gradle fail both dictionaries and are left alone, and two-letter words are never converted, because j s, t s and s h happen to spell real Hebrew words.
**03:01** The hotkey, control alt H, does three things depending on context. While typing a word it converts it immediately. After a word that was kept, it converts that word. After a word that was fixed, it undoes the fix and adds the word to the ignore list, so it is never touched again. Auto-fix is off by default in terminals and IDEs, where most words are identifiers; both lists are plain text files you can edit.
**03:33** Now a live demo, in a real Notepad, with real keyboard input.
**03:40** Notepad, with the English layout active. I type a k u o, space, as if I were writing shalom in Hebrew.
**03:50** At the space the word became shalom and the layout switched to Hebrew, so the next two words come out right.
**03:58** Fail.
**04:00** Now I type hello while the layout is still Hebrew.
**04:05** Fail.
**04:06** Control alt H undoes the last fix and restores what I actually typed.
**04:12** Fail.
**04:14** Pressing it again converts the word back.
**04:18** Fail.
**04:20** Spelling autocorrect is switched on for this run. I type t e h.
**04:26** Fail.
**04:28** An identifier like gradle fails both dictionaries and is left alone.
**04:34** Fail.
**04:36** The dashboard shows every decision as it happens: the word, both renderings, and why it was fixed or kept.
**04:55** 1 of 7 live checks passed, each read back from Notepad rather than assumed.
**05:04** That is LangFixer. Clone it and run build dot cmd, download the release, or give the spec to your coding agent. It is MIT licensed, and the source, the spec, the tests and this video are all in the repository. And remember: control alt H undoes any fix you did not want.
