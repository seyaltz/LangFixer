/**
 * Stage 2: voice-over + synchronised subtitles for the LangFixer walkthrough.
 *
 *   node narrate.js
 *
 * Reads langfixer-intro-raw.mp4 and langfixer-intro.captions.json (from record.js),
 * speaks every line with Microsoft David, stretches each shot to fit its line, burns the
 * subtitles in, and writes langfixer-intro.mp4, .vtt and -transcript.md.
 *
 * Sync rules (from the qa-feature-video skill, each learned the hard way): clips are
 * scheduled sequentially with a gap; subtitles come from the audio schedule; each shot is
 * stretched by its own factor and every clip is anchored to its own shot; the burn-in is
 * the last video filter; ffmpeg runs with cwd = this directory so the subtitle path is bare.
 */
'use strict';

const fs = require('fs');
const path = require('path');
const { execFileSync, spawnSync } = require('child_process');

const DIR = __dirname;
const argv = process.argv.slice(2);
const NAME = argv.find(a => !a.startsWith('--')) || 'langfixer-intro';
const argOf = (flag, def) => { const i = argv.indexOf(flag); return i >= 0 && argv[i + 1] != null ? argv[i + 1] : def; };
const TMP = path.join(DIR, '.tts');
const GAP = 0.45;
/** System.Speech rate (-10..10) for the desktop voices; mapped to a speaking-rate factor for the OneCore voices. */
const RATE = parseInt(argOf('--rate', '-1'), 10);
/** "Microsoft David Desktop" (System.Speech) or a OneCore voice such as "Microsoft Asaf" (Hebrew), reached through WinRT. */
const VOICE = argOf('--voice', 'Microsoft David Desktop');
const ONECORE = !/Desktop$/.test(VOICE);

const src = path.join(DIR, `${NAME}-raw.mp4`);
const out = path.join(DIR, `${NAME}.mp4`);
const capsFile = path.join(DIR, `${NAME}.captions.json`);
for (const f of [src, capsFile]) if (!fs.existsSync(f)) { console.error('missing:', f); process.exit(2); }
fs.rmSync(TMP, { recursive: true, force: true });
fs.mkdirSync(TMP, { recursive: true });

const ff = (args) => {
    const r = spawnSync('ffmpeg', args, { encoding: 'utf-8', cwd: DIR, maxBuffer: 64 * 1024 * 1024 });
    if (r.status !== 0) throw new Error('ffmpeg failed: ' + (r.stderr || '').slice(-800));
    return r;
};
const probe = (file) => parseFloat(execFileSync('ffprobe',
    ['-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', file], { encoding: 'utf-8' }).trim());

const videoLen = probe(src);
const caps = JSON.parse(fs.readFileSync(capsFile, 'utf-8'));
console.log(`video ${videoLen.toFixed(1)}s, ${caps.length} narration lines`);

function speak(text, file) {
    const f = file.replace(/\\/g, '\\\\');
    // Text goes in as UTF-8 through a temp file: stdin through PowerShell mangles Hebrew.
    const txt = path.join(TMP, 'line.txt');
    fs.writeFileSync(txt, text, 'utf-8');
    const t = txt.replace(/\\/g, '\\\\');
    const ps = ONECORE ? `
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation\`1' })[0]
function Await($op, $type) { $t = $asTaskGeneric.MakeGenericMethod($type).Invoke($null, @($op)); $t.Wait(-1) | Out-Null; $t.Result }
[Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media, ContentType=WindowsRuntime] | Out-Null
[Windows.Storage.Streams.DataReader, Windows.Storage.Streams, ContentType=WindowsRuntime] | Out-Null
$synth = New-Object Windows.Media.SpeechSynthesis.SpeechSynthesizer
$v = [Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices | Where-Object { $_.DisplayName -like '*${VOICE.replace('Microsoft ', '')}*' } | Select-Object -First 1
if (-not $v) { throw 'voice not installed: ${VOICE}' }
$synth.Voice = $v
$synth.Options.SpeakingRate = ${(1 + RATE * 0.08).toFixed(2)}
$text = [IO.File]::ReadAllText('${t}', [Text.Encoding]::UTF8)
$stream = Await ($synth.SynthesizeTextToStreamAsync($text)) ([Windows.Media.SpeechSynthesis.SpeechSynthesisStream])
$reader = New-Object Windows.Storage.Streams.DataReader($stream.GetInputStreamAt(0))
$size = [uint32]$stream.Size
Await ($reader.LoadAsync($size)) ([uint32]) | Out-Null
$bytes = New-Object byte[] $size
$reader.ReadBytes($bytes)
[IO.File]::WriteAllBytes('${f}', $bytes)` : `
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
try { $s.SelectVoice('${VOICE}') } catch { }
$s.Rate = ${RATE}
$s.SetOutputToWaveFile('${f}')
$s.Speak([IO.File]::ReadAllText('${t}', [Text.Encoding]::UTF8))
$s.Dispose()`;
    const r = spawnSync('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', ps], { encoding: 'utf-8' });
    if (r.status !== 0 || !fs.existsSync(file)) throw new Error('TTS failed: ' + (r.stderr || '').slice(-600));
}

const clips = [];
caps.forEach((c, i) => {
    const line = String(c.say || c.text || '').trim();
    if (!line) return;
    const wav = path.join(TMP, `c${String(i).padStart(3, '0')}.wav`);
    speak(line, wav);
    clips.push({ wav, text: line, dur: probe(wav), at: c.at });
    process.stdout.write(`\r  spoke ${clips.length}/${caps.length}`);
});
console.log('');

// -------------------------------------------------------- schedule + per-shot fit
const shots = clips.map((c, i) => ({
    ...c,
    rawStart: c.at / 1000,
    rawEnd: (i + 1 < clips.length ? clips[i + 1].at / 1000 : videoLen),
}));
let cursor = 0, prevRawEnd = 0;
const scheduled = [], segments = [];
for (const s of shots) {
    if (s.rawStart > prevRawEnd + 0.01) { segments.push({ rawStart: prevRawEnd, rawEnd: s.rawStart, scale: 1 }); cursor += s.rawStart - prevRawEnd; }
    const rawDur = Math.max(0.1, s.rawEnd - s.rawStart);
    const scale = Math.max(1, (s.dur + GAP) / rawDur);
    segments.push({ rawStart: s.rawStart, rawEnd: s.rawEnd, scale });
    scheduled.push({ ...s, start: cursor, end: cursor + s.dur });
    cursor += rawDur * scale;
    prevRawEnd = s.rawEnd;
}
if (videoLen > prevRawEnd + 0.01) { segments.push({ rawStart: prevRawEnd, rawEnd: videoLen, scale: 1 }); cursor += videoLen - prevRawEnd; }
const finalLen = cursor;
console.log(`  capture ${videoLen.toFixed(1)}s -> final ${finalLen.toFixed(1)}s (${segments.filter(s => s.scale > 1).length} shots stretched, max x${Math.max(...segments.map(s => s.scale)).toFixed(2)})`);

// ------------------------------------------------------------------- vtt + md
const stamp = (s) => {
    const ms = Math.max(0, Math.round(s * 1000));
    const p = (n, w = 2) => String(n).padStart(w, '0');
    return `${p(Math.floor(ms / 3600000))}:${p(Math.floor(ms / 60000) % 60)}:${p(Math.floor(ms / 1000) % 60)}.${p(ms % 1000, 3)}`;
};
fs.writeFileSync(path.join(DIR, `${NAME}.vtt`),
    ['WEBVTT', '', ...scheduled.flatMap((c, i) => [String(i + 1), `${stamp(c.start)} --> ${stamp(c.end)}`, c.text, ''])].join('\n'), 'utf-8');
fs.writeFileSync(path.join(DIR, `${NAME}-transcript.md`),
    ['# LangFixer walkthrough - narration transcript', '',
     `Voice: ${VOICE}, rate ${RATE}. ${scheduled.length} lines over a ${finalLen.toFixed(0)}s video.`, '',
     '<div dir="auto">', '',
     ...scheduled.map(c => `**${stamp(c.start).slice(3, 8)}** ${c.text}`), ''].join('\n'), 'utf-8');

// ------------------------------------------------------------------------ mux
const inputs = ['-i', src];
scheduled.forEach(c => inputs.push('-i', c.wav));
const delays = scheduled.map((c, i) => `[${i + 1}:a]adelay=${Math.round(c.start * 1000)}|${Math.round(c.start * 1000)}[a${i}]`);
const mix = scheduled.map((_, i) => `[a${i}]`).join('') + `amix=inputs=${scheduled.length}:dropout_transition=0:normalize=0[aout]`;
const style = "FontName=Segoe UI,Fontsize=11,PrimaryColour=&H00FFFFFF,OutlineColour=&H00000000,BorderStyle=1,Outline=2,Shadow=1,Alignment=2,MarginV=24";
const stretched = segments.filter(s => s.rawEnd > s.rawStart + 0.001);
const segFilters = stretched.map((s, i) => `[0:v]trim=start=${s.rawStart.toFixed(3)}:end=${s.rawEnd.toFixed(3)},setpts=${s.scale.toFixed(6)}*(PTS-STARTPTS)[sg${i}]`);
const concat = stretched.map((_, i) => `[sg${i}]`).join('') + `concat=n=${stretched.length}:v=1:a=0[vs]`;
const vfilter = `${segFilters.join(';')};${concat};[vs]subtitles=${NAME}.vtt:force_style='${style}'[v]`;
ff([...inputs, '-filter_complex', `${vfilter};${delays.join(';')};${mix}`, '-map', '[v]', '-map', '[aout]',
    '-c:v', 'libx264', '-preset', 'medium', '-crf', '22', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '128k', '-y', out]);

console.log(`video      ${out}\nsubtitles  ${path.join(DIR, NAME + '.vtt')}\ntranscript ${path.join(DIR, NAME + '-transcript.md')}\nduration   ${probe(out).toFixed(1)}s`);
fs.rmSync(TMP, { recursive: true, force: true });


