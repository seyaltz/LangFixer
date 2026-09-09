/**
 * Stage 1 of the LangFixer walkthrough video: slides (browser), live demo (desktop
 * capture of Notepad + dashboard), results (browser). Writes
 *   langfixer-intro-raw.mp4         the stitched picture, 1280x720 30fps, no audio
 *   langfixer-intro.captions.json   narration lines with combined-timeline timestamps
 *
 *   node record.js               # full recording (needs an idle desktop for ~2 minutes)
 *   node record.js --checks-only # live act only, no capture, asserts printed
 *
 * The live act runs a LangFixer instance in test mode (--accept-injected, autocorrect on,
 * scratch data folder) so the synthetic typing counts as typing. Any other LangFixer
 * instance is stopped first and the reference instance is restarted afterwards.
 */
'use strict';

const fs = require('fs');
const path = require('path');
const { spawn, spawnSync } = require('child_process');
const L = require('./lf-lib');
const {
    chromium, DIR, REPO, LANGFIXER, VIEWPORT, REGION, checks, captions,
    startPhase, check, say, caption, slide, clearSlide, typer, sleep, notepadText,
    startCapture, stopCapture, probe, normalize,
} = L;

const NAME = 'langfixer-intro';
const CHECKS_ONLY = process.argv.includes('--checks-only');
const TMP = path.join(DIR, '.capture');
fs.rmSync(TMP, { recursive: true, force: true });
fs.mkdirSync(TMP, { recursive: true });

const pieces = [];   // normalised mp4 files in order
let timeline = 0;    // ms of stitched video so far

async function browserAct(fn) {
    const browser = await chromium.launch({ headless: false, args: ['--window-position=200,80'] });
    const context = await browser.newContext({
        viewport: VIEWPORT,
        recordVideo: CHECKS_ONLY ? undefined : { dir: TMP, size: VIEWPORT },
    });
    const page = await context.newPage();
    await page.goto('about:blank', { waitUntil: 'domcontentloaded' });
    startPhase(timeline);
    const before = new Set(fs.readdirSync(TMP));
    try { await fn(page); }
    finally { await context.close(); await browser.close(); }
    if (CHECKS_ONLY) return;
    const webm = fs.readdirSync(TMP).find(f => f.endsWith('.webm') && !before.has(f));
    const mp4 = path.join(TMP, `piece${pieces.length}.mp4`);
    const dur = normalize(path.join(TMP, webm), mp4);
    pieces.push(mp4);
    timeline += Math.round(dur * 1000);
}

(async () => {
    // ================================================================ ACT 1: slides
    await browserAct(async (page) => {
        await slide(page, 'LangFixer', [
            '@Hebrew ⇄ English wrong-layout auto-fixer for Windows', '',
            'You typed a whole word before noticing the keyboard layout was wrong.',
            'LangFixer notices at the space, retypes the word in the language you meant,',
            'and switches the layout so the rest of the sentence comes out right.', '',
            '#github.com/seyaltz/LangFixer',
        ], 9000,
        'LangFixer is a small Windows tray utility. When you type a word in the wrong keyboard layout, '
        + 'Hebrew on an English layout or English on a Hebrew one, it notices at the space, retypes the word '
        + 'in the language you meant, and switches the layout for you. This video shows how to get it, run it, '
        + 'and what it does, with a live demo.');

        await slide(page, 'The problem', [
            '@akuo  →  שלום',
            '@יקךךם  →  hello', '',
            'Same keys, other layout. You only see it after the word is out,',
            'then you delete it, switch the layout, and type it again.', '',
            '#LangFixer does those three steps for you, automatically, in any application.',
        ], 8000,
        'The same physical keys spell shalom on the Hebrew layout and a k u o on the English one. '
        + 'You only notice after the word is out. Then you delete it, switch the layout and type it again. '
        + 'LangFixer does those three steps for you, automatically, in any application.');

        await slide(page, 'Get it', [
            '#Option 1 - clone and build. No SDK needed: the compiler ships with Windows.',
            '> git clone https://github.com/seyaltz/LangFixer',
            '> cd LangFixer',
            '> build.cmd',
            '#Option 2 - download the release',
            '> github.com/seyaltz/LangFixer/releases  →  LangFixer-1.0.zip',
            '#Option 3 - let your coding agent build its own copy',
            '> Give it SPEC.md. It contains everything needed, and it has been proven: an independent agent rebuilt LangFixer from it and passed all 40 live checks.',
        ], 12000,
        'Three ways to get it. One: clone the repository and run build dot cmd. No SDK is needed, the C sharp '
        + 'compiler ships with every Windows machine. Two: download LangFixer one point zero dot zip from the '
        + 'releases page. Three: hand SPEC dot md to your own coding agent. The spec is complete; an independent '
        + 'agent rebuilt the program from it and passed all forty live checks.');

        await slide(page, 'Requirements', [
            '+ Windows 10 or 11',
            '+ English and Hebrew keyboard layouts installed',
            '+ "Basic typing" feature for both languages (this provides the spell checkers)',
            '#Settings → Time & language → Language → [language] → Options', '',
            '- The exe is unsigned: SmartScreen warns once. Choose More info → Run anyway,',
            '- or build it yourself from the source.', '',
            '#Privacy: LangFixer keeps only the current word in memory, writes nothing but its own',
            '#settings, and has no network code at all. The source is short enough to read.',
        ], 12000,
        'Requirements: Windows ten or eleven, with the English and Hebrew layouts installed, and the basic '
        + 'typing feature for both languages, which provides the spell checkers. The exe is unsigned, so '
        + 'SmartScreen warns once; choose more info, run anyway, or build it yourself. On privacy: LangFixer '
        + 'keeps only the current word in memory, writes nothing but its own settings, and has no network code.');

        await slide(page, 'Run it', [
            '> LangFixer.exe',
            'The dashboard opens: status, Start / Stop, dictionaries found, a live feed of decisions.', '',
            '+ Tick "Start with Windows" so it is always there.',
            '+ Closing the window hides it to the tray. The blue א A icon brings it back.',
            '+ "Auto-correct spelling" is optional and off by default.', '',
            '#Everything it learns lives in %LOCALAPPDATA%\\LangFixer: ignore-words.txt, excluded-apps.txt, settings.txt',
        ], 11000,
        'Run LangFixer dot exe. The dashboard opens with the status, a start and stop button, the dictionaries '
        + 'it found, and a live feed of every decision. Tick start with Windows so it is always there. Closing '
        + 'the window hides it to the tray; the blue aleph A icon brings it back. Spelling autocorrect is optional '
        + 'and off by default. Everything it learns lives in three small text files in your local app data folder.');

        await slide(page, 'How it decides', [
            '1  It records the physical keys of the word you are typing, not the letters.',
            '2  At the space it renders those keys under both layouts:  akuo  and  שלום.',
            '3  It asks the Windows spell checkers: is "akuo" English? is "שלום" Hebrew?',
            '4  It converts only when your rendering FAILS its language AND the other one PASSES.',
            '5  It deletes the word, switches the layout, and replays your keys so the app types it itself.', '',
            '#Identifiers like gradle or npm fail both dictionaries, so they are left alone.',
            '#Two-letter words are never converted: js, ts, sh, ps happen to spell real Hebrew words.',
        ], 14000,
        'How it decides. It records the physical keys of the word, not the letters. At the space it renders '
        + 'those keys under both layouts and asks the Windows spell checkers about each rendering. It converts '
        + 'only when your rendering fails its own language and the other one passes. Then it deletes the word, '
        + 'switches the layout, and replays your keys so the application types it itself. Identifiers like gradle '
        + 'fail both dictionaries and are left alone, and two-letter words are never converted, because j s, t s '
        + 'and s h happen to spell real Hebrew words.');

        await slide(page, 'Hotkey and lists', [
            '@Ctrl + Alt + H',
            '  while typing a word      →  convert it now, no questions asked',
            '  after a word it kept      →  convert that word',
            '  after a word it fixed     →  undo, and never touch that word again', '',
            '#ignore-words.txt   words never converted; seeded with api, aws, src... and grown by every undo',
            '#excluded-apps.txt  programs where auto-fix is off: terminals and IDEs by default',
        ], 11000,
        'The hotkey, control alt H, does three things depending on context. While typing a word it converts it '
        + 'immediately. After a word that was kept, it converts that word. After a word that was fixed, it undoes '
        + 'the fix and adds the word to the ignore list, so it is never touched again. Auto-fix is off by default '
        + 'in terminals and IDEs, where most words are identifiers; both lists are plain text files you can edit.');

        await slide(page, 'Live demo', [
            'Real Notepad, real keyboard input, real spell checkers.', '',
            '1  type  akuo nv akunl  with the English layout active',
            '2  type  hello  while the layout is Hebrew',
            '3  Ctrl+Alt+H to undo, and again to redo',
            '4  autocorrect:  teh  →  the',
            '5  an identifier stays:  gradle',
            '6  the dashboard feed',
        ], 7000,
        'Now a live demo, in a real Notepad, with real keyboard input.');
    });

    // ================================================================ ACT 2: live desktop
    const scratch = path.join(TMP, 'home');
    fs.mkdirSync(scratch, { recursive: true });
    fs.writeFileSync(path.join(scratch, 'settings.txt'), 'autocorrect=1\n');
    spawnSync('powershell', ['-NoProfile', '-Command', 'Stop-Process -Name LangFixer -Force -ErrorAction SilentlyContinue']);
    await sleep(800);
    const lf = spawn(LANGFIXER, ['--debug', '--accept-injected', '--minimized'],
        { env: { ...process.env, LANGFIXER_HOME: scratch }, detached: true, stdio: 'ignore' });
    lf.unref();
    await sleep(2500);

    let capture = null;
    try {
        const launched = typer('launch-notepad');
        if (launched.code !== 0) throw new Error('notepad: ' + launched.out);
        typer('place', 'class:Notepad', String(REGION.x), String(REGION.y), String(REGION.w), String(REGION.h));
        typer('zoom', '5');      // larger text: legible at 1280x720
        typer('layout', 'en');
        await sleep(500);

        if (!CHECKS_ONLY) {
            capture = startCapture(path.join(TMP, 'live.mp4'));
            await sleep(1200);       // gdigrab start-up latency
        }
        startPhase(timeline);

        say('Notepad, English layout. Typing "akuo nv akunl" as if writing Hebrew.',
            'Notepad, with the English layout active. I type a k u o, space, as if I were writing shalom in Hebrew.');
        await sleep(600);
        typer('type', 'akuo ');
        await sleep(2600);
        say('The word was fixed at the space and the layout switched to Hebrew. The next words come out right.',
            'At the space the word became shalom and the layout switched to Hebrew, so the next two words come out right.');
        typer('type', 'nv akunl ');
        await sleep(2600);
        let t = notepadText();
        check('first word fixed and layout switched', t === 'שלום מה שלומך ', t);
        say(checks[checks.length - 1].ok ? 'PASS  shalom, ma shlomcha' : 'FAIL  ' + t,
            checks[checks.length - 1].ok ? 'Pass. Shalom, ma shlomcha.' : 'Fail.');
        await sleep(1500);

        say('Now "hello" while the layout is Hebrew.', 'Now I type hello while the layout is still Hebrew.');
        await sleep(500);
        typer('type', 'hello ');
        await sleep(2600);
        t = notepadText();
        check('hello typed in Hebrew becomes hello, layout back to English', t.endsWith(' hello '), t);
        say(checks[checks.length - 1].ok ? 'PASS  hello, and the layout is English again' : 'FAIL  ' + t,
            checks[checks.length - 1].ok ? 'Pass. Hello, and the layout is English again.' : 'Fail.');
        await sleep(1500);

        say('Ctrl+Alt+H undoes the last fix.', 'Control alt H undoes the last fix and restores what I actually typed.');
        await sleep(400);
        typer('hotkey');
        await sleep(2600);
        t = notepadText();
        check('undo restores the original keys', t.endsWith(' יקךךם '), t);
        say(checks[checks.length - 1].ok ? 'PASS  undone' : 'FAIL  ' + t, checks[checks.length - 1].ok ? 'Pass. Undone.' : 'Fail.');
        await sleep(1200);
        say('And again to redo.', 'Pressing it again converts the word back.');
        typer('hotkey');
        await sleep(2600);
        t = notepadText();
        check('hotkey again re-converts', t.endsWith(' hello '), t);
        say(checks[checks.length - 1].ok ? 'PASS  hello again' : 'FAIL  ' + t, checks[checks.length - 1].ok ? 'Pass. Hello again.' : 'Fail.');
        await sleep(1200);

        say('Autocorrect is on in this run: "teh" becomes "the".',
            'Spelling autocorrect is switched on for this run. I type t e h.');
        await sleep(400);
        typer('type', 'teh ');
        await sleep(2600);
        t = notepadText();
        check('autocorrect teh -> the', t.endsWith(' the '), t);
        say(checks[checks.length - 1].ok ? 'PASS  the' : 'FAIL  ' + t,
            checks[checks.length - 1].ok ? 'Pass. It became the. Only one-letter typos with a typing signature are corrected, and never capitalized words or names.' : 'Fail.');
        await sleep(1500);

        say('An identifier stays: "gradle".', 'An identifier like gradle fails both dictionaries and is left alone.');
        await sleep(400);
        typer('type', 'gradle ');
        await sleep(2600);
        t = notepadText();
        check('gradle untouched', t.endsWith(' gradle '), t);
        say(checks[checks.length - 1].ok ? 'PASS  gradle untouched' : 'FAIL  ' + t, checks[checks.length - 1].ok ? 'Pass. Gradle is untouched.' : 'Fail.');
        await sleep(1200);

        say('The dashboard shows every decision as it happens.',
            'The dashboard shows every decision as it happens: the word, both renderings, and why it was fixed or kept.');
        spawnSync(LANGFIXER, [], { env: { ...process.env, LANGFIXER_HOME: scratch } }); // second instance -> brings the dashboard forward
        await sleep(800);
        typer('place', 'title:LangFixer', String(REGION.x + 360), String(REGION.y + 100), '560', '520');
        await sleep(7000);
        check('dashboard visible', typer('foreground').out.length > 0, typer('foreground').out);
    } finally {
        if (capture) { await sleep(600); await stopCapture(capture); }
        // leave no trace: clear the Notepad tab only if Notepad is really in front
        typer('place', 'class:Notepad', String(REGION.x), String(REGION.y), String(REGION.w), String(REGION.h));
        await sleep(400);
        const closed = typer('close-tab');
        console.log('notepad cleanup:', closed.code === 0 ? 'ok' : closed.out);
        spawnSync('powershell', ['-NoProfile', '-Command', 'Stop-Process -Name LangFixer -Force -ErrorAction SilentlyContinue']);
        await sleep(600);
        spawn(LANGFIXER, ['--minimized'], { detached: true, stdio: 'ignore' }).unref();  // the user's instance, back
    }
    if (!CHECKS_ONLY) {
        const mp4 = path.join(TMP, `piece${pieces.length}.mp4`);
        const dur = normalize(path.join(TMP, 'live.mp4'), mp4);
        pieces.push(mp4);
        timeline += Math.round(dur * 1000);
    }

    // ================================================================ ACT 3: results
    await browserAct(async (page) => {
        const passed = checks.filter(c => c.ok).length;
        await slide(page, `${passed} of ${checks.length} live checks passed`, [
            ...checks.map(c => (c.ok ? '+  PASS  ' : '-  FAIL  ') + c.name), '',
            '#Every result above was read back from Notepad through UI Automation, not assumed.',
        ], 9000, `${passed} of ${checks.length} live checks passed, each read back from Notepad rather than assumed.`);

        await slide(page, 'github.com/seyaltz/LangFixer', [
            '> git clone https://github.com/seyaltz/LangFixer  &&  build.cmd',
            '> or download LangFixer-1.0.zip from Releases',
            '> or give SPEC.md to your coding agent', '',
            '+ MIT licensed. Source, spec, tests and this video are in the repository.',
            '#Questions and bugs: open an issue. Ctrl+Alt+H undoes any fix you did not want.',
        ], 9000,
        'That is LangFixer. Clone it and run build dot cmd, download the release, or give the spec to your coding '
        + 'agent. It is MIT licensed, and the source, the spec, the tests and this video are all in the repository. '
        + 'And remember: control alt H undoes any fix you did not want.');
    });

    // ================================================================ stitch
    if (!CHECKS_ONLY) {
        const list = path.join(TMP, 'list.txt');
        fs.writeFileSync(list, pieces.map(p => `file '${p.replace(/\\/g, '/')}'`).join('\n'));
        const out = path.join(DIR, `${NAME}-raw.mp4`);
        const r = spawnSync('ffmpeg', ['-y', '-f', 'concat', '-safe', '0', '-i', list, '-c', 'copy', out], { encoding: 'utf-8' });
        if (r.status !== 0) throw new Error('concat failed: ' + (r.stderr || '').slice(-500));
        fs.writeFileSync(path.join(DIR, `${NAME}.captions.json`), JSON.stringify(captions, null, 2), 'utf-8');
        console.log(`raw video: ${out}  (${probe(out).toFixed(1)}s, ${captions.length} narration lines)`);
    }
    const failed = checks.filter(c => !c.ok);
    console.log(`\n${checks.length - failed.length}/${checks.length} checks passed`);
    failed.forEach(c => console.log(`  FAIL  ${c.name}  ${c.detail}`));
    process.exit(failed.length ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
