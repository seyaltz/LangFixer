/**
 * Hebrew walkthrough, in this order: a short explanation of the app, the live Notepad demo, the dashboard
 * with a short explanation, and the installation options at the end.
 * Writes langfixer-demo-he-raw.mp4 + langfixer-demo-he.captions.json; then:
 *   node narrate.js langfixer-demo-he --voice "Microsoft Asaf" --rate 0
 *
 *   node record-demo-he.js           # needs an idle desktop for ~2 minutes
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

const NAME = 'langfixer-demo-he';
const TMP = path.join(DIR, '.capture-he');
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
    const r = typed === 'HOTKEY' ? typer('hotkey') : typer('type', typed);
    if (r.code !== 0) throw new Error('typing refused, Notepad is not in front: ' + r.out);
    await sleep(holdMs || 2600);
    const t = notepadText();
    const ok = expectFn(t);
    check(intro, ok, t);
    say(ok ? 'עבר  ' + passText : 'נכשל  ' + t, ok ? passSay : 'זה לא עבד כמצופה.');
    await sleep(1500);
}

(async () => {
    // ---------------------------------------------------------------- 1. short explanation
    await browserAct(async (page) => {
        await slide(page, 'LangFixer', [
            '@מתקן אוטומטי לטעויות שפת מקלדת: עברית ⇄ אנגלית',
            '',
            'הקלדתם מילה שלמה לפני ששמתם לב שהמקלדת בשפה הלא נכונה.',
            'LangFixer מזהה את זה ברווח, מקליד מחדש את המילה בשפה שהתכוונתם אליה,',
            'ומחליף את שפת המקלדת, כך ששאר המשפט יוצא נכון.',
            '',
            '#עובד בכל תוכנה. משתמש במילונים המובנים של Windows. שום דבר לא יוצא מהמחשב.',
        ], 9000,
        'לנג פיקסר הוא כלי קטן לווינדוס שיושב במגש המערכת. כשמקלידים מילה בשפת מקלדת לא נכונה, עברית במקלדת אנגלית או אנגלית במקלדת עברית, '
        + 'הוא מזהה את זה ברווח, מקליד מחדש את המילה בשפה שהתכוונתם אליה, ומחליף את שפת המקלדת. זה עובד בכל תוכנה, בלי להתקין כלום. '
        + 'עכשיו הדגמה חיה.');
    });

    // ---------------------------------------------------------------- 2. live demo
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
        const placed = typer('place', 'class:Notepad', String(REGION.x), String(REGION.y), String(REGION.w), String(REGION.h));
        if (placed.code !== 0) throw new Error('notepad: ' + placed.out);
        typer('zoom', '5');
        typer('layout', 'en');
        await sleep(500);
        capture = startCapture(path.join(TMP, 'live.mp4'));
        await sleep(1200);
        startPhase(timeline);

        await demo('פנקס רשימות, מקלדת באנגלית. מקלידים akuo כאילו כותבים שלום.',
            'זה פנקס רשימות עם מקלדת באנגלית. אני מקליד א ק או או ורווח, כאילו אני כותב שלום בעברית.',
            'akuo ', t => t === 'שלום ', 'שלום, והמקלדת עברה לעברית',
            'ברווח המילה הפכה לשלום, והמקלדת עברה לעברית.');

        await demo('המילים הבאות מוקלדות כבר בעברית ויוצאות נכון: nv akunl.',
            'שתי המילים הבאות מוקלדות כבר במקלדת עברית, ולכן הן יוצאות נכון בלי שום עזרה.',
            'nv akunl ', t => t === 'שלום מה שלומך ', 'שלום מה שלומך',
            'שלום, מה שלומך.');

        await demo('עכשיו hello כשהמקלדת עדיין בעברית.',
            'עכשיו אני מקליד הלו כשהמקלדת עדיין בעברית.',
            'hello ', t => t.endsWith(' hello '), 'hello, והמקלדת חזרה לאנגלית',
            'הלו, והמקלדת חזרה לאנגלית.');

        await demo('Ctrl+Alt+H מבטל את התיקון האחרון.',
            'קונטרול אלט אייץ מבטל את התיקון האחרון ומחזיר בדיוק את מה שהקלדתי.',
            'HOTKEY', t => t.endsWith(' יקךךם '), 'בוטל',
            'בוטל.');

        await demo('לחיצה נוספת מחזירה את התיקון.',
            'לחיצה נוספת ממירה את המילה בחזרה.',
            'HOTKEY', t => t.endsWith(' hello '), 'שוב hello',
            'שוב הלו.');

        await demo('מזהה תוכנה נשאר כמו שהוא: gradle אינו מילה באנגלית ולא בעברית.',
            'מזהה כמו גריידל לא קיים באף אחד משני המילונים, ולכן הוא נשאר כמו שהוא.',
            'gradle ', t => t.endsWith(' gradle '), 'gradle לא השתנה',
            'גריידל לא השתנה.');

        await demo('תיקון כתיב (אופציונלי): אותיות שהתחלפו, teh.',
            'תיקון כתיב מופעל בהרצה הזו. הוא מתקן רק שגיאות עם חתימה של הקלדה, כמו אותיות שהתחלפו. אני מקליד טי אי אייץ.',
            'teh ', t => t.endsWith(' the '), 'the',
            'זה הפך ל־דה.');

        await demo('אבל postgres נשאר: אין חתימת הקלדה, אין תיקון.',
            'פוסטגרס גם לא נמצא במילון, אבל אין לו חתימת הקלדה, ולכן הוא נשאר. גרסה אגרסיבית יותר הפכה אותו לפוסטורס; היום זה אופציה שצריך להפעיל.',
            'postgres ', t => t.endsWith(' postgres '), 'postgres לא השתנה',
            'פוסטגרס לא השתנה.');

        await demo('השפה מנצחת את הכתיב: eurv זה קורה, לא eruv באנגלית.',
            'וכשמילה היא מילה אמיתית בעברית של ארבע אותיות או יותר, תיקון השפה מנצח כל הצעת כתיב. אי יו אר וי זה קורה בעברית.',
            'eurv ', t => t.endsWith(' קורה '), 'קורה, המקלדת בעברית',
            'קורה, והמקלדת בעברית.');

        // ------------------------------------------------------------ 3. dashboard, short explanation
        say('לוח הבקרה: כל החלטה מופיעה בזמן אמת, וההגדרות למטה.',
            'זה לוח הבקרה. כל החלטה מופיעה בזמן אמת: המילה, שני הפירושים שלה, והסיבה שהיא תוקנה או נשארה. '
            + 'למעלה כפתור עצור והתחל. למטה ההגדרות: תיקון כתיב, מצב אגרסיבי, תיקון כתיב בעברית, הגנת שמות, והפעלה עם ווינדוס. '
            + 'כפתורי הרשימות פותחים את רשימת המילים שלא נוגעים בהן ואת רשימת התוכנות שבהן התיקון כבוי.');
        spawnSync(LANGFIXER, [], { env: { ...process.env, LANGFIXER_HOME: scratch } });
        await sleep(800);
        typer('place', 'title:LangFixer', String(REGION.x + 320), String(REGION.y + 70), '640', '590');
        await sleep(10000);
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

    // ---------------------------------------------------------------- 4. installation options
    await browserAct(async (page) => {
        const passed = checks.filter(c => c.ok).length;
        await slide(page, 'איך מתקינים', [
            '> git clone https://github.com/seyaltz/LangFixer   then   build.cmd',
            '> LangFixer-1.0.zip   from the Releases page',
            '> SPEC.md   for your coding agent',
            '',
            '+ מריצים את LangFixer.exe, מסמנים "Start with Windows", וסוגרים את החלון: הוא יושב במגש המערכת.',
            '+ דרישות: מקלדת אנגלית ועברית, ותכונת "Basic typing" לשתי השפות (היא מספקת את המילונים).',
            '+ Ctrl+Alt+H ממיר את המילה הנוכחית או האחרונה, או מבטל את התיקון האחרון וזוכר את המילה.',
            '',
            `#${passed} מתוך ${checks.length} צעדי ההדגמה אומתו בקריאה חזרה מפנקס הרשימות. רישיון MIT.`,
        ], 12000,
        'איך מתקינים. אפשרות אחת: משכפלים את המאגר מגיטהאב ומריצים בילד דוט סי אם די, בלי להתקין שום ערכת פיתוח. '
        + 'אפשרות שתיים: מורידים את קובץ הזיפ מעמוד ההפצות. אפשרות שלוש: נותנים את קובץ הספסיפיקציה לסוכן הקוד שלכם והוא בונה עותק משלו. '
        + 'מריצים את הקובץ, מסמנים הפעלה עם ווינדוס, וסוגרים את החלון. צריך מקלדת אנגלית ועברית עם תכונת ההקלדה הבסיסית, שמספקת את המילונים. '
        + `${passed} מתוך ${checks.length} צעדי ההדגמה אומתו בקריאה חזרה מפנקס הרשימות. זה לנג פיקסר. קונטרול אלט אייץ מבטל כל תיקון שלא רציתם.`);
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
