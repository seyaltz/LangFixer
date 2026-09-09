/**
 * Helpers for the LangFixer walkthrough recording. Adapted from the qa-feature-video
 * skill's lib.js: captions carry a combined-timeline timestamp (`at`) because the
 * final video is stitched from a browser capture, a desktop capture and a browser
 * capture again.
 */
'use strict';

const path = require('path');
const fs = require('fs');
const { execFileSync, spawnSync } = require('child_process');
const { chromium } = require(
    'C:/Users/eyal/.claude/skills/playwright-skill/node_modules/playwright');

const DIR = __dirname;
const REPO = path.resolve(DIR, '..');
const TYPER = path.join(DIR, 'DemoTyper.exe');
const LANGFIXER = path.join(REPO, 'LangFixer.exe');
const VIEWPORT = { width: 1280, height: 720 };
/** Desktop region captured during the live act; Notepad and the dashboard are placed inside it. */
const REGION = { x: 200, y: 150, w: 1280, h: 720 };

const checks = [];
const captions = [];        // {at, text, tone, say}  at = ms on the COMBINED timeline
let phaseStart = 0;         // Date.now() when the current capture started
let base = 0;               // ms of video before the current capture

const now = () => base + (Date.now() - phaseStart);
const startPhase = (baseMs) => { base = baseMs; phaseStart = Date.now(); };

function check(name, ok, detail) {
    checks.push({ name, ok: !!ok, detail: detail == null ? '' : String(detail) });
    console.log(`  [${ok ? 'PASS' : 'FAIL'}] ${name}${detail ? '  -- ' + detail : ''}`);
    return !!ok;
}

/** Narration line without any on-screen drawing (used during the desktop capture). */
function say(text, say) {
    captions.push({ at: now(), text, tone: 'info', say: say || text });
}

/** Caption bar in the browser; also a narration line. textContent only. */
async function caption(page, text, ms = 3600, tone = 'info', sayText = null) {
    captions.push({ at: now(), text, tone, say: sayText || text });
    await page.evaluate(({ text, tone }) => {
        let el = document.getElementById('__qa_caption');
        if (!el) { el = document.createElement('div'); el.id = '__qa_caption'; document.documentElement.appendChild(el); }
        const colour = tone === 'fail' ? '#b91c1c' : tone === 'pass' ? '#065f46' : '#0f172a';
        el.setAttribute('style', ['position:fixed', 'left:0', 'right:0', 'bottom:0', 'z-index:2147483647',
            `background:${colour}`, 'color:#fff', 'font:600 24px/1.45 Segoe UI,Arial,sans-serif',
            'padding:16px 28px', 'box-shadow:0 -6px 24px rgba(0,0,0,.28)', 'direction:ltr',
            'text-align:left', 'pointer-events:none'].join(';'));
        el.textContent = text;
    }, { text, tone });
    await page.waitForTimeout(ms);
}

/** Full-screen slide. Leading '#' muted, '+' green, '-' red, '>' code style. */
async function slide(page, title, lines, ms = 4200, sayText = null) {
    if (sayText) captions.push({ at: now(), text: title, tone: 'info', say: sayText });
    await page.evaluate(({ title, lines }) => {
        let el = document.getElementById('__qa_slide');
        if (!el) { el = document.createElement('div'); el.id = '__qa_slide'; document.documentElement.appendChild(el); }
        el.setAttribute('style', ['position:fixed', 'inset:0', 'z-index:2147483646',
            'background:linear-gradient(135deg,#0f172a 0%,#1e3a5f 100%)', 'color:#e2e8f0',
            'font:400 24px/1.6 Segoe UI,Arial,sans-serif', 'padding:52px 72px', 'direction:ltr',
            'text-align:left', 'pointer-events:none', 'overflow:hidden'].join(';'));
        el.replaceChildren();
        const h = document.createElement('div');
        h.setAttribute('style', 'font:700 44px/1.25 Segoe UI,Arial,sans-serif;color:#fff;margin-bottom:26px');
        h.textContent = title;
        el.appendChild(h);
        for (const raw of lines) {
            const t = String(raw);
            const row = document.createElement('div');
            if (t.startsWith('#')) { row.setAttribute('style', 'color:#94a3b8;margin:14px 0 2px;font-size:20px'); row.textContent = t.slice(1).trim(); }
            else if (t.startsWith('+')) { row.setAttribute('style', 'color:#4ade80'); row.textContent = t.slice(1); }
            else if (t.startsWith('-')) { row.setAttribute('style', 'color:#f87171'); row.textContent = t.slice(1); }
            else if (t.startsWith('>')) { row.setAttribute('style', 'font:500 22px/1.7 Consolas,monospace;color:#fde68a;background:rgba(0,0,0,.35);padding:2px 14px;border-radius:6px;display:inline-block;margin:3px 0'); row.textContent = t.slice(1).trim(); el.appendChild(row); el.appendChild(document.createElement('br')); continue; }
            else if (t.startsWith('@')) { row.setAttribute('style', 'font:600 34px/1.5 Segoe UI,Arial,sans-serif;color:#fff;margin:6px 0'); row.textContent = t.slice(1).trim(); }
            else { row.textContent = t || '\u00a0'; }
            el.appendChild(row);
        }
    }, { title, lines });
    await page.waitForTimeout(ms);
}

const clearSlide = (page) => page.evaluate(() => { const e = document.getElementById('__qa_slide'); if (e) e.remove(); });

// ------------------------------------------------------------ desktop helpers

function typer(...args) {
    const r = spawnSync(TYPER, args, { encoding: 'utf-8' });
    // Only strip the final newline: the Notepad text legitimately ends with the space the user typed.
    return { code: r.status, out: (r.stdout || '').replace(/\r?\n$/, ''), err: (r.stderr || '').trim() };
}
const sleep = (ms) => new Promise(r => setTimeout(r, ms));

/** Text of the Notepad document, newline-normalised. */
const notepadText = () => typer('read', 'class:Notepad').out.replace(/\r?\n$/, '');

/** Start ffmpeg capturing REGION to `file`; returns the child (write 'q' to stop). */
function startCapture(file) {
    const { spawn } = require('child_process');
    const child = spawn('ffmpeg', ['-y', '-f', 'gdigrab', '-framerate', '30', '-draw_mouse', '0',
        '-offset_x', String(REGION.x), '-offset_y', String(REGION.y), '-video_size', `${REGION.w}x${REGION.h}`,
        '-i', 'desktop', '-c:v', 'libx264', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p', '-r', '30', file],
        { stdio: ['pipe', 'ignore', 'pipe'] });
    let err = '';
    child.stderr.on('data', d => { err += d; if (err.length > 20000) err = err.slice(-10000); });
    child.errText = () => err;
    return child;
}
function stopCapture(child) {
    return new Promise(resolve => { child.on('close', resolve); try { child.stdin.write('q'); } catch (e) { child.kill(); } });
}

const probe = (file) => parseFloat(execFileSync('ffprobe',
    ['-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', file], { encoding: 'utf-8' }).trim());

/** Normalise any capture to 1280x720 30fps h264 so the pieces can be concatenated. */
function normalize(src, dst) {
    const r = spawnSync('ffmpeg', ['-y', '-i', src, '-vf', `scale=${VIEWPORT.width}:${VIEWPORT.height}:flags=lanczos,fps=30,format=yuv420p`,
        '-an', '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '20', dst], { encoding: 'utf-8' });
    if (r.status !== 0) throw new Error('normalize failed: ' + (r.stderr || '').slice(-500));
    return probe(dst);
}

module.exports = {
    chromium, DIR, REPO, TYPER, LANGFIXER, VIEWPORT, REGION,
    checks, captions, now, startPhase, check, say, caption, slide, clearSlide,
    typer, sleep, notepadText, startCapture, stopCapture, probe, normalize,
};
