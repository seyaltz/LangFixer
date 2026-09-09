/**
 * Demo-focused walkthrough: two intro slides, then the live Notepad demo, then one closing card.
 * Writes langfixer-demo-raw.mp4 + langfixer-demo.captions.json; narrate-demo.js adds the voice.
 *
 *   node record-demo.js            # needs an idle desktop for ~2 minutes
 *
 * The live act runs LangFixer in test mode (--accept-injected, autocorrect on in strict mode, scratch data
 * folder). Any other LangFixer instance is stopped first and the user's instance is restarted afterwards.
 */
'use strict';

const fs = require('fs');
const path = require('path');
const { spawn, spawnSync } = require('child_process');
const L = require('./lf-lib');
const {
    chromium, DIR, LANGFIXER, VIEWPORT, REGION, checks, captions,
    startPhase, check, say, slide, typer, sleep, notepadText, startCapture, stopCapture, probe, normalize,
} = L;

const NAME = 'langfixer-demo';
const TMP = path.join(DIR, '.capture-demo');
fs.rmSync(TMP, { recursive: true, force: true });
fs.mkdirSync(TMP, { recursive: true });

const pieces = [];
let timeline = 0;

async function browserAct(fn) {
    const browser = await chromium.launch({ headless: false, args: ['--window-position=200,80'] });
    const context = await browser.newContext({ viewport: VIEWPORT, recordVideo: { dir: TMP, size: VIEWPORT } });
    const page = await context.newPage();
    await page.goto('about:blank', { waitUntil: 'domcontentloaded' });
    startPhase(timeline);
    const before = new Set(fs.readdirSync(TMP));
    try { await fn(page); }
    finally { await context.close(); await browser.close(); }
    const webm = fs.readdirSync(TMP).find(f => f.endsWith('.webm') && !before.has(f));
    const mp4 = path.join(TMP, `piece${pieces.length}.mp4`);
    const dur = normalize(path.join(TMP, webm), mp4);
    pieces.push(mp4);
    timeline += Math.round(dur * 1000);
}

/** One demo step: narrate, type, wait, read Notepad back, assert, narrate the verdict. */
async function demo(intro, introSay, typed, expectFn, passText, passSay, holdMs) {
    say(intro, introSay);
    await sleep(700);
    if (typed === 'HOTKEY') typer('hotkey'); else typer('type', typed);
    await sleep(holdMs || 2600);
    const t = notepadText();
    const ok = expectFn(t);
    check(intro, ok, t);
    say(ok ? 'PASS  ' + passText : 'FAIL  ' + t, ok ? passSay : 'That did not work as expected.');
    await sleep(1500);
}

(async () => {
    // ---------------------------------------------------------------- two intro slides
    await browserAct(async (page) => {
        await slide(page, 'LangFixer', [
            '@Hebrew ⇄ English wrong-layout auto-fixer for Windows', '',
            'You typed a whole word before noticing the keyboard layout was wrong.',
            'LangFixer notices at the space, retypes the word in the language you meant,',
            'and switches the layout so the rest of the sentence comes out right.', '',
            '#Free and open source: github.com/seyaltz/LangFixer',
        ], 8000,
        'LangFixer is a small Windows tray utility. When you type a word in the wrong keyboard layout, '
        + 'it notices at the space, retypes the word in the language you meant, and switches the layout for you. '
        + 'It works in every application and needs nothing installed.');

        await slide(page, 'Get it, run it', [
            '> git clone https://github.com/seyaltz/LangFixer   then   build.cmd',
            '> or download LangFixer-1.0.zip from the Releases page',
            '> or hand SPEC.md to your coding agent and let it build its own', '',
            '+ Run LangFixer.exe, tick "Start with Windows", close the window: it lives in the tray.',
            '+ Needs the English and Hebrew layouts and their "Basic typing" feature (the spell checkers).',
            '+ Ctrl+Alt+H converts the current or last word, or undoes the last fix and remembers the word.', '',
            '#Nothing leaves your machine: no network code, only the current word in memory.',
        ], 12000,
        'To get it: clone the repository and run build dot cmd, no SDK needed. Or download the zip from the '
        + 'releases page. Or hand the spec to your coding agent and let it build its own. Run the exe, tick start '
        + 'with Windows, and close the window: it lives in the tray. It needs the English and Hebrew layouts with '
        + 'their basic typing feature, which provides the spell checkers. Control alt H converts the current or '
        + 'last word, or undoes the last fix. Nothing leaves your machine. Now the live demo.');
    });

    // ---------------------------------------------------------------- live desktop act
    const scratch = path.join(TMP, 'home');
    fs.mkdirSync(scratch, { recursive: true });
    fs.writeFileSync(path.join(scratch, 'settings.txt'), 'autocorrect=1\nautocorrect_aggressive=0\nautocorrect_hebrew=0\nnames_guard=1\n');
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
        typer('zoom', '5');
        typer('layout', 'en');
        await sleep(500);
        capture = startCapture(path.join(TMP, 'live.mp4'));
        await sleep(1200);
        startPhase(timeline);

        await demo('Notepad, English layout. Typing "akuo" as if writing שלום in Hebrew.',
            'This is Notepad with the English layout active. I type a k u o and a space, as if I were writing shalom in Hebrew.',
            'akuo ', t => t === 'שלום ', 'שלום, and the layout switched to Hebrew',
            'At the space the word became shalom, and the layout switched to Hebrew.');

        await demo('The next words are typed on the Hebrew layout and come out right: "nv akunl".',
            'The next two words are typed on the Hebrew layout, so they come out right without any help.',
            'nv akunl ', t => t === 'שלום מה שלומך ', 'שלום מה שלומך',
            'Shalom, ma shlomcha.');

        await demo('Now "hello" while the layout is still Hebrew.',
            'Now I type hello while the layout is still Hebrew.',
            'hello ', t => t.endsWith(' hello '), 'hello, and the layout is English again',
            'Pass. Hello, and the layout is English again.');

        await demo('Ctrl+Alt+H undoes the last fix.',
            'Control alt H undoes the last fix and restores exactly what I typed.',
            'HOTKEY', t => t.endsWith(' יקךךם '), 'undone',
            'Undone.');

        await demo('Ctrl+Alt+H again converts it back.',
            'Pressing it again converts the word back.',
            'HOTKEY', t => t.endsWith(' hello '), 'hello again',
            'Hello again.');

        await demo('An identifier stays: "gradle" is not English and not Hebrew.',
            'An identifier like gradle fails both dictionaries and is left alone.',
            'gradle ', t => t.endsWith(' gradle '), 'gradle untouched',
            'Gradle is untouched.');

        await demo('Autocorrect (optional): a swapped-letter typo, "teh".',
            'Spelling autocorrect is on for this run. It only fixes typos with a typing signature. I type t e h.',
            'teh ', t => t.endsWith(' the '), 'the',
            'It became the.');

        await demo('But "postgres" stays: no typing signature, no correction.',
            'Postgres is not in the dictionary either, but it has no typing signature, so it stays. The earlier '
            + 'aggressive version turned it into postures; that is now an opt-in.',
            'postgres ', t => t.endsWith(' postgres '), 'postgres untouched',
            'Postgres is untouched.');

        await demo('Layout beats spelling: "eurv" is קורה, not the English "eruv".',
            'And when a word is a real Hebrew word of four letters or more, the layout fix wins over any spelling '
            + 'suggestion. e u r v is kore in Hebrew.',
            'eurv ', t => t.endsWith(' קורה '), 'קורה, layout Hebrew',
            'Kore, and the layout is Hebrew.');

        say('The dashboard shows every decision as it happens, and holds the options.',
            'The dashboard shows every decision as it happens, with both renderings and the reason. '
            + 'The checkboxes at the bottom are the options: autocorrect, aggressive mode, Hebrew autocorrect, the names guard, and start with Windows.');
        spawnSync(LANGFIXER, [], { env: { ...process.env, LANGFIXER_HOME: scratch } });
        await sleep(800);
        typer('place', 'title:LangFixer', String(REGION.x + 360), String(REGION.y + 70), '560', '590');
        await sleep(9000);
        check('dashboard visible', typer('foreground').out.length > 0, typer('foreground').out);
    } finally {
        if (capture) { await sleep(600); await stopCapture(capture); }
        typer('place', 'class:Notepad', String(REGION.x), String(REGION.y), String(REGION.w), String(REGION.h));
        await sleep(400);
        const closed = typer('close-tab');
        console.log('notepad cleanup:', closed.code === 0 ? 'ok' : closed.out);
        spawnSync('powershell', ['-NoProfile', '-Command', 'Stop-Process -Name LangFixer -Force -ErrorAction SilentlyContinue']);
        await sleep(600);
        spawn(LANGFIXER, ['--minimized'], { detached: true, stdio: 'ignore' }).unref();
    }
    {
        const mp4 = path.join(TMP, `piece${pieces.length}.mp4`);
        const dur = normalize(path.join(TMP, 'live.mp4'), mp4);
        pieces.push(mp4);
        timeline += Math.round(dur * 1000);
    }

    // ---------------------------------------------------------------- closing card
    await browserAct(async (page) => {
        const passed = checks.filter(c => c.ok).length;
        await slide(page, 'github.com/seyaltz/LangFixer', [
            `+ ${passed} of ${checks.length} demo steps verified by reading Notepad back through UI Automation`, '',
            '> git clone https://github.com/seyaltz/LangFixer   then   build.cmd',
            '> or download LangFixer-1.0.zip from Releases',
            '> or give SPEC.md to your coding agent', '',
            '#MIT licensed. Ctrl+Alt+H undoes any fix you did not want.',
        ], 9000,
        `${passed} of ${checks.length} demo steps were verified by reading Notepad back, not assumed. `
        + 'That is LangFixer. Clone it, download it, or let your agent build it. Control alt H undoes any fix you did not want.');
    });

    // ---------------------------------------------------------------- stitch
    const list = path.join(TMP, 'list.txt');
    fs.writeFileSync(list, pieces.map(p => `file '${p.replace(/\\/g, '/')}'`).join('\n'));
    const out = path.join(DIR, `${NAME}-raw.mp4`);
    const r = spawnSync('ffmpeg', ['-y', '-f', 'concat', '-safe', '0', '-i', list, '-c', 'copy', out], { encoding: 'utf-8' });
    if (r.status !== 0) throw new Error('concat failed: ' + (r.stderr || '').slice(-500));
    fs.writeFileSync(path.join(DIR, `${NAME}.captions.json`), JSON.stringify(captions, null, 2), 'utf-8');
    console.log(`raw video: ${out}  (${probe(out).toFixed(1)}s, ${captions.length} narration lines)`);
    const failed = checks.filter(c => !c.ok);
    console.log(`\n${checks.length - failed.length}/${checks.length} checks passed`);
    failed.forEach(c => console.log(`  FAIL  ${c.name}  ${c.detail}`));
    process.exit(failed.length ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
