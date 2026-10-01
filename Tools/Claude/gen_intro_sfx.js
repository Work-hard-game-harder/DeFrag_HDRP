// Offline synthesis of the LobbyF intro cinematic sound (48 kHz, 16-bit mono WAV).
const fs = require('fs');
const path = require('path');

const SR = 48000;
const OUT = process.argv[2] || path.join(__dirname, 'out');
fs.mkdirSync(OUT, { recursive: true });

let seed = 20261001;
const rnd = () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; };
const noise = () => rnd() * 2 - 1;
const lerp = (a, b, t) => a + (b - a) * t;
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const TAU = Math.PI * 2;

function buffer(sec) { return new Float32Array(Math.ceil(sec * SR)); }

// RBJ biquad
function biquad(type, freq, q, gainDb = 0) {
  const w = TAU * freq / SR, cs = Math.cos(w), sn = Math.sin(w), a = sn / (2 * q);
  let b0, b1, b2, a0, a1, a2;
  if (type === 'lp') { b0 = (1 - cs) / 2; b1 = 1 - cs; b2 = b0; a0 = 1 + a; a1 = -2 * cs; a2 = 1 - a; }
  else if (type === 'hp') { b0 = (1 + cs) / 2; b1 = -(1 + cs); b2 = b0; a0 = 1 + a; a1 = -2 * cs; a2 = 1 - a; }
  else { b0 = a; b1 = 0; b2 = -a; a0 = 1 + a; a1 = -2 * cs; a2 = 1 - a; } // bp (0 dB peak)
  const f = { b0: b0 / a0, b1: b1 / a0, b2: b2 / a0, a1: a1 / a0, a2: a2 / a0, x1: 0, x2: 0, y1: 0, y2: 0 };
  f.run = x => { const y = f.b0 * x + f.b1 * f.x1 + f.b2 * f.x2 - f.a1 * f.y1 - f.a2 * f.y2; f.x2 = f.x1; f.x1 = x; f.y2 = f.y1; f.y1 = y; return y; };
  return f;
}
function filterBuf(buf, type, freq, q = 0.707) { const f = biquad(type, freq, q); for (let i = 0; i < buf.length; i++) buf[i] = f.run(buf[i]); return buf; }
// Time-varying filter: freqAt(t) evaluated every 32 samples.
function sweepFilter(buf, type, freqAt, q = 0.707) {
  let f = biquad(type, freqAt(0), q);
  for (let i = 0; i < buf.length; i++) {
    if (i % 32 === 0) { const n = biquad(type, clamp(freqAt(i / SR), 20, SR * 0.45), q); n.x1 = f.x1; n.x2 = f.x2; n.y1 = f.y1; n.y2 = f.y2; f = n; }
    buf[i] = f.run(buf[i]);
  }
  return buf;
}
function mixInto(dst, src, at = 0, gain = 1) { const o = Math.floor(at * SR); for (let i = 0; i < src.length && o + i < dst.length; i++) if (o + i >= 0) dst[o + i] += src[i] * gain; return dst; }

// Freeverb-style reverb (mono).
function reverb(buf, { size = 1, damp = 0.35, feedback = 0.82, wet = 0.3, predelay = 0.012 } = {}) {
  const combs = [1557, 1617, 1491, 1422, 1277, 1356, 1188, 1116].map(d => ({ d: Math.floor(d * size * SR / 44100), b: null, i: 0, store: 0 }));
  combs.forEach(c => c.b = new Float32Array(c.d));
  const aps = [556, 441, 341, 225].map(d => ({ d: Math.floor(d * SR / 44100), b: null, i: 0 }));
  aps.forEach(a => a.b = new Float32Array(a.d));
  const pd = Math.floor(predelay * SR), pre = new Float32Array(pd + 1); let pi = 0;
  const out = new Float32Array(buf.length);
  for (let n = 0; n < buf.length; n++) {
    pre[pi] = buf[n]; pi = (pi + 1) % pre.length; const x = pre[pi] * 0.015;
    let s = 0;
    for (const c of combs) { const y = c.b[c.i]; c.store = y * (1 - damp) + c.store * damp; c.b[c.i] = x + c.store * feedback; c.i = (c.i + 1) % c.d; s += y; }
    for (const a of aps) { const bo = a.b[a.i]; const y = -s + bo; a.b[a.i] = s + bo * 0.5; a.i = (a.i + 1) % a.d; s = y; }
    out[n] = buf[n] * (1 - wet * 0.5) + s * wet;
  }
  return out;
}

function softClip(buf, drive = 1) { for (let i = 0; i < buf.length; i++) buf[i] = Math.tanh(buf[i] * drive) / Math.tanh(drive); return buf; }
function normalize(buf, peakDb = -1) { let p = 0; for (const v of buf) p = Math.max(p, Math.abs(v)); const g = p > 0 ? Math.pow(10, peakDb / 20) / p : 1; for (let i = 0; i < buf.length; i++) buf[i] *= g; return buf; }
function fadeEdges(buf, inSec = 0.002, outSec = 0.05) { const a = inSec * SR, b = outSec * SR; for (let i = 0; i < buf.length; i++) { let g = 1; if (i < a) g *= i / a; if (i > buf.length - b) g *= (buf.length - i) / b; buf[i] *= g; } return buf; }

function writeWav(name, buf) {
  const data = Buffer.alloc(buf.length * 2);
  for (let i = 0; i < buf.length; i++) data.writeInt16LE(Math.round(clamp(buf[i], -1, 1) * 32767), i * 2);
  const h = Buffer.alloc(44);
  h.write('RIFF', 0); h.writeUInt32LE(36 + data.length, 4); h.write('WAVE', 8); h.write('fmt ', 12);
  h.writeUInt32LE(16, 16); h.writeUInt16LE(1, 20); h.writeUInt16LE(1, 22); h.writeUInt32LE(SR, 24);
  h.writeUInt32LE(SR * 2, 28); h.writeUInt16LE(2, 32); h.writeUInt16LE(16, 34); h.write('data', 36); h.writeUInt32LE(data.length, 40);
  fs.writeFileSync(path.join(OUT, name), Buffer.concat([h, data]));
  console.log('wrote', name, (buf.length / SR).toFixed(2) + 's');
}

function thump(sec, fStart, fEnd, rate, gain = 1) {
  const b = buffer(sec); let ph = 0;
  for (let i = 0; i < b.length; i++) { const t = i / SR; const f = fEnd + (fStart - fEnd) * Math.exp(-t * 18); ph += TAU * f / SR; b[i] = gain * Math.sin(ph) * Math.exp(-t * rate) * Math.min(1, t * 900); }
  return b;
}
function noiseBurst(sec, rate, lo, hi, gain = 1) {
  const b = buffer(sec); for (let i = 0; i < b.length; i++) b[i] = noise() * Math.exp(-(i / SR) * rate) * gain;
  if (hi) filterBuf(b, 'lp', hi); if (lo) filterBuf(b, 'hp', lo); return b;
}
function clink(f, dec, gain) {
  const sec = Math.min(1.2, 6 / dec), b = buffer(sec);
  const r = [1, 2.76, 5.4, 8.93]; const ph = r.map(() => rnd() * TAU);
  for (let i = 0; i < b.length; i++) { const t = i / SR; let s = 0; for (let k = 0; k < r.length; k++) s += Math.sin(TAU * f * r[k] * t + ph[k]) * Math.exp(-t * dec * (1 + k * 0.8)) / (k + 1); b[i] = s * gain * Math.min(1, t * 4000); }
  return b;
}

// ---------- intro cinematic cues ----------
// Night exterior: wind gusts, distant city hum, faint wet-street hiss. Loops cleanly (crossfaded tail).
function nightAmbience() {
  const sec = 8, b = buffer(sec);
  const wind = noiseBurst(sec, 0, 120, 900, 0.5);
  sweepFilter(wind, 'bp', t => 280 + 160 * Math.sin(TAU * 0.11 * t) + 90 * Math.sin(TAU * 0.27 * t + 1), 0.8);
  for (let i = 0; i < wind.length; i++) wind[i] *= 0.55 + 0.45 * Math.sin(TAU * 0.13 * i / SR) ** 2;
  mixInto(b, wind, 0, 0.8);
  const hum = buffer(sec); let p = 0; for (let i = 0; i < hum.length; i++) { p += TAU * 48 / SR; hum[i] = 0.12 * Math.sin(p) + 0.05 * Math.sin(p * 2.01); }
  mixInto(b, filterBuf(hum, 'lp', 160));
  mixInto(b, noiseBurst(sec, 0, 3000, 9000, 0.04));
  return normalize(fadeEdges(b, 0.6, 0.6), -10);
}
// A small hard-soled shoe on wet stone: heel tap, sole slap, tiny splash.
function wetStep(variant) {
  const b = buffer(0.7);
  mixInto(b, thump(0.12, 420 + variant * 40, 180, 55, 0.6));
  mixInto(b, noiseBurst(0.08, 70, 900, 5200, 0.55), 0.004);
  mixInto(b, noiseBurst(0.06, 60, 600, 3000, 0.4), 0.075 + variant * 0.01);
  const splash = noiseBurst(0.35, 14, 1800, 9000, 0.22);
  sweepFilter(splash, 'bp', t => 5200 - 2600 * t, 1.4); mixInto(b, splash, 0.07);
  for (let k = 0; k < 4; k++) mixInto(b, clink(2600 + rnd() * 2400, 60, 0.03), 0.09 + rnd() * 0.18);
  return normalize(fadeEdges(reverb(b, { size: 0.9, wet: 0.22 }), 0.001, 0.08), -3);
}
// Electronic lock picking: fine metal ticks, a scraping pin, data chirps.
function lockPick() {
  const sec = 2.4, b = buffer(sec);
  for (let k = 0; k < 16; k++) mixInto(b, clink(3200 + rnd() * 3500, 90 + rnd() * 60, 0.12 + rnd() * 0.1), 0.05 + k * 0.13 + rnd() * 0.05);
  const scrape = noiseBurst(1.6, 0, 2500, 7000, 0.18); sweepFilter(scrape, 'bp', t => 3400 + 1200 * Math.sin(TAU * 3.1 * t), 3);
  for (let i = 0; i < scrape.length; i++) scrape[i] *= Math.sin(Math.PI * i / scrape.length); mixInto(b, scrape, 0.2);
  for (let k = 0; k < 6; k++) { const c = buffer(0.05); for (let i = 0; i < c.length; i++) c[i] = 0.12 * Math.sign(Math.sin(TAU * (1800 + k * 260) * i / SR)) * (1 - i / c.length); mixInto(b, filterBuf(c, 'lp', 5000), 0.3 + k * 0.3); }
  return normalize(fadeEdges(b, 0.002, 0.1), -4);
}
// Access granted: two-tone chime and a solenoid bolt retracting.
function lockGranted() {
  const b = buffer(1.2);
  const tone = (f, at, len) => { const t0 = buffer(len); for (let i = 0; i < t0.length; i++) { const t = i / SR; t0[i] = 0.25 * Math.sin(TAU * f * t) * Math.min(1, t * 400) * Math.exp(-t * 6); } mixInto(b, t0, at); };
  tone(1318.5, 0, 0.35); tone(1975.5, 0.11, 0.5);
  mixInto(b, thump(0.25, 220, 90, 30, 0.7), 0.24);
  mixInto(b, noiseBurst(0.09, 50, 300, 4000, 0.45), 0.24);
  mixInto(b, clink(900, 35, 0.18), 0.26);
  return normalize(fadeEdges(reverb(b, { size: 0.7, wet: 0.2 }), 0.001, 0.15), -3);
}
// Heavy glass door: seal unsticking, low swing, air rushing in from the lobby.
function glassDoorOpen() {
  const sec = 2.2, b = buffer(sec);
  const seal = noiseBurst(0.35, 9, 200, 2200, 0.35); sweepFilter(seal, 'bp', t => 400 + 1800 * t, 2); mixInto(b, seal, 0.02);
  const groan = buffer(1.4); let p = 0; for (let i = 0; i < groan.length; i++) { const t = i / SR; p += TAU * (70 + 25 * t) / SR; groan[i] = 0.2 * Math.sin(p + 0.4 * Math.sin(p * 3)) * Math.sin(Math.PI * t / 1.4); }
  mixInto(b, filterBuf(groan, 'lp', 400), 0.15);
  const air = noiseBurst(sec, 0, 400, 6000, 0.4); sweepFilter(air, 'bp', t => 700 + 2500 * clamp(t / 1.2, 0, 1), 0.7);
  for (let i = 0; i < air.length; i++) { const t = i / SR; air[i] *= clamp((t - 0.2) / 0.5, 0, 1) * Math.exp(-Math.max(0, t - 0.9) * 2.2); }
  mixInto(b, air);
  return normalize(fadeEdges(reverb(b, { size: 1.4, wet: 0.3 }), 0.005, 0.3), -4);
}
// Lobby room tone: soft HVAC hum and a bright, airy hush.
function lobbyTone() {
  const sec = 8, b = buffer(sec); let p = 0;
  for (let i = 0; i < b.length; i++) { p += TAU * 60 / SR; b[i] = 0.06 * Math.sin(p) + 0.025 * Math.sin(p * 3); }
  mixInto(b, noiseBurst(sec, 0, 200, 2400, 0.12));
  const shimmer = buffer(sec); for (let i = 0; i < shimmer.length; i++) { const t = i / SR; shimmer[i] = 0.02 * Math.sin(TAU * 2093 * t) * (0.5 + 0.5 * Math.sin(TAU * 0.2 * t)); }
  mixInto(b, shimmer);
  return normalize(fadeEdges(reverb(b, { size: 1.9, wet: 0.35 }), 0.8, 0.8), -12);
}
// Reverse-swell into the white title card.
function riser() {
  const sec = 2.2, b = noiseBurst(sec, 0, 300, 12000, 0.6);
  sweepFilter(b, 'bp', t => 300 * Math.pow(30, t / sec), 1.2);
  for (let i = 0; i < b.length; i++) b[i] *= Math.pow(i / b.length, 2.4);
  const tone = buffer(sec); let p = 0; for (let i = 0; i < tone.length; i++) { const t = i / SR; p += TAU * (110 * Math.pow(4, t / sec)) / SR; tone[i] = 0.25 * Math.sin(p) * Math.pow(t / sec, 3); }
  mixInto(b, tone);
  return normalize(fadeEdges(b, 0.01, 0.02), -3);
}
// Title hit: sub boom, glassy shimmer, long tail.
function titleHit() {
  const sec = 4.5, b = buffer(sec);
  mixInto(b, thump(2.5, 120, 34, 1.6, 1));
  const sh = buffer(sec); const fs_ = [1046.5, 1568, 2093, 3136];
  for (let i = 0; i < sh.length; i++) { const t = i / SR; let s = 0; fs_.forEach((f, k) => s += Math.sin(TAU * f * t + k) * Math.exp(-t * (1.1 + k * 0.5)) / (k + 1)); sh[i] = 0.18 * s * Math.min(1, t * 200); }
  mixInto(b, sh);
  mixInto(b, noiseBurst(1.2, 4, 2000, 12000, 0.12));
  return normalize(fadeEdges(reverb(b, { size: 2.2, wet: 0.45 }), 0.002, 0.8), -2);
}
// Score bed for the whole sequence: low pulse that tightens, dark pad, sparse bell notes.
function scoreBed() {
  const sec = 24, b = buffer(sec); const ph = [0, 0, 0];
  const f = [55, 82.4, 110.2];
  for (let i = 0; i < b.length; i++) {
    const t = i / SR; let s = 0;
    for (let k = 0; k < f.length; k++) { ph[k] += TAU * f[k] * (1 + 0.004 * Math.sin(TAU * 0.07 * t + k)) / SR; s += Math.sin(ph[k] + 0.6 * Math.sin(ph[k] * 2)) / (k + 1.5); }
    const swell = clamp(t / 4, 0, 1) * (t < 16.5 ? 1 : Math.max(0, 1 - (t - 16.5) / 1.6));
    b[i] = 0.35 * s * swell;
  }
  sweepFilter(b, 'lp', t => 220 + 500 * clamp(t / 16, 0, 1), 0.9);
  // heartbeat-like sub pulse, quickening toward the lobby
  let t = 1.5; while (t < 16) { mixInto(b, thump(0.5, 70, 38, 9, 0.5), t); t += lerp(1.25, 0.8, t / 16); }
  // sparse bell motif (minor), haunting
  const notes = [[2.0, 659.3], [3.6, 587.3], [5.2, 523.3], [8.4, 659.3], [10.0, 784.0], [11.6, 698.5], [14.0, 659.3]];
  for (const [at, fr] of notes) mixInto(b, clink(fr, 2.2, 0.18), at);
  softClip(b, 1.2);
  return normalize(fadeEdges(reverb(b, { size: 2.0, wet: 0.38 }), 1.5, 1.0), -8);
}

writeWav('Intro_NightAmbience.wav', nightAmbience());
writeWav('Intro_Step_1.wav', wetStep(0));
writeWav('Intro_Step_2.wav', wetStep(1));
writeWav('Intro_Step_3.wav', wetStep(2));
writeWav('Intro_LockPick.wav', lockPick());
writeWav('Intro_LockGranted.wav', lockGranted());
writeWav('Intro_GlassDoorOpen.wav', glassDoorOpen());
writeWav('Intro_LobbyTone.wav', lobbyTone());
writeWav('Intro_Riser.wav', riser());
writeWav('Intro_TitleHit.wav', titleHit());
writeWav('Intro_ScoreBed.wav', scoreBed());
