// Offline synthesis of the door-breach cutscene sound effects (48 kHz, 16-bit mono WAV).
const fs = require('fs');
const path = require('path');

const SR = 48000;
const OUT = process.argv[2] || path.join(__dirname, 'out');
fs.mkdirSync(OUT, { recursive: true });

let seed = 20260927;
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

// ---------- building blocks ----------
// Large steel panel: inharmonic damped modes excited by a strike.
function metalPanel(sec, f0, { brightness = 1, decay = 1, modes = 18, gain = 1 } = {}) {
  const b = buffer(sec);
  const ratios = [1, 1.59, 2.14, 2.3, 2.65, 2.92, 3.6, 4.15, 4.6, 5.4, 6.2, 7.0, 7.9, 8.8, 10.2, 11.7, 13.1, 15.0, 17.3, 19.9];
  for (let m = 0; m < modes; m++) {
    const f = f0 * ratios[m % ratios.length] * (1 + (rnd() - 0.5) * 0.03) * (m >= ratios.length ? 1.9 : 1);
    if (f > SR * 0.45) continue;
    const amp = gain * Math.pow(0.78, m) * (0.6 + rnd() * 0.8) * (m > 5 ? brightness : 1);
    const dec = (2.2 + rnd()) / decay * (1 + m * 0.55);
    const ph = rnd() * TAU, beat = 1 + (rnd() - 0.5) * 0.004;
    for (let i = 0; i < b.length; i++) { const t = i / SR; b[i] += amp * Math.exp(-t * dec) * (Math.sin(TAU * f * t + ph) + 0.35 * Math.sin(TAU * f * beat * t)); }
  }
  return b;
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
function debrisField(dst, start, end, count, { fLo = 900, fHi = 5200, gLo = 0.05, gHi = 0.3, heavyChance = 0.25, density = 2.2 } = {}) {
  for (let n = 0; n < count; n++) {
    const u = Math.pow(rnd(), density), t = lerp(start, end, u);
    const energy = 1 - u * 0.8;
    if (rnd() < heavyChance) mixInto(dst, thump(0.35, 180 + rnd() * 120, 70 + rnd() * 40, 22 + rnd() * 10, 0.5 * energy), t);
    mixInto(dst, clink(lerp(fLo, fHi, rnd()), 18 + rnd() * 40, lerp(gLo, gHi, rnd()) * energy), t);
    if (rnd() < 0.5) mixInto(dst, noiseBurst(0.05, 90, 1500, 9000, 0.12 * energy), t);
  }
}
function creak(sec, fStart, fEnd, gain = 1, roughness = 1) {
  // stick-slip pulse train through a resonant metal body
  const exc = buffer(sec); let next = 0;
  for (let i = 0; i < exc.length; i++) {
    const t = i / SR, u = t / sec; const rate = lerp(fStart, fEnd, u) * (1 + 0.25 * Math.sin(TAU * 3.1 * t) * roughness);
    if (t >= next) { exc[i] = (0.6 + rnd() * 0.4) * (1 - Math.abs(2 * u - 1) * 0.6); next = t + 1 / rate * (0.85 + rnd() * 0.3 * roughness); }
  }
  const out = buffer(sec);
  for (const [f, q, g] of [[420, 14, 1], [980, 18, 0.8], [1730, 20, 0.6], [2650, 22, 0.45], [3900, 25, 0.3]]) { const c = Float32Array.from(exc); filterBuf(c, 'bp', f * (0.95 + rnd() * 0.1), q); mixInto(out, c, 0, g * 6); }
  for (let i = 0; i < out.length; i++) out[i] *= gain * Math.sin(Math.PI * clamp(i / out.length, 0, 1)) ** 0.5;
  return out;
}

// ---------- effects ----------
function doorImpact(heavy) {
  const sec = heavy ? 4.2 : 3.2, b = buffer(sec);
  mixInto(b, thump(1.6, heavy ? 120 : 110, heavy ? 36 : 44, heavy ? 5.5 : 7.5, 1.0));
  mixInto(b, thump(0.5, 260, 90, 30, 0.35));
  mixInto(b, metalPanel(sec, heavy ? 58 : 66, { brightness: heavy ? 1.1 : 0.8, decay: heavy ? 0.8 : 1.1, gain: 0.55 }));
  mixInto(b, noiseBurst(0.25, 38, 200, 5200, 0.9));                 // body of the hit
  mixInto(b, noiseBurst(0.03, 220, 2500, 16000, 0.5));              // crack transient
  debrisField(b, 0.03, heavy ? 1.3 : 0.9, heavy ? 26 : 14, { gHi: heavy ? 0.25 : 0.18, fLo: 1200, fHi: 6000 }); // frame and bolt rattle
  if (heavy) {
    mixInto(b, metalPanel(sec - 0.13, 52, { brightness: 1.3, decay: 0.7, gain: 0.45 }), 0.13); // buckling second blow
    mixInto(b, thump(1.2, 100, 34, 6, 0.7), 0.13);
    mixInto(b, creak(1.5, 32, 11, 0.22, 1.4), 0.55);                  // panel bending
  }
  // falling dust hiss
  const dust = noiseBurst(sec, 1.3, 3000, 11000, 0.035); for (let i = 0; i < dust.length; i++) dust[i] *= Math.min(1, i / (0.4 * SR)); mixInto(b, dust, 0.15);
  softClip(b, heavy ? 1.8 : 1.4);
  return normalize(fadeEdges(reverb(b, { size: 1.5, wet: 0.34, feedback: 0.84, damp: 0.4 })), heavy ? -0.5 : -2.5);
}

function doorBreach() {
  const sec = 6.5, b = buffer(sec);
  // sub pressure wave
  mixInto(b, thump(3.0, 90, 26, 2.4, 1.3));
  mixInto(b, thump(0.8, 220, 60, 12, 0.6));
  // explosive burst with closing low-pass
  const burst = noiseBurst(2.5, 3.2, 40, 0, 1.2); sweepFilter(burst, 'lp', t => 9000 * Math.exp(-t * 3.5) + 180); mixInto(b, burst);
  mixInto(b, noiseBurst(0.04, 150, 2000, 18000, 0.9));
  // metal tearing screech
  const tear = buffer(1.1); let ph = 0;
  for (let i = 0; i < tear.length; i++) { const t = i / SR; const f = 1400 - 900 * t + 120 * Math.sin(TAU * 13 * t); ph += TAU * f / SR; tear[i] = (Math.sin(ph + 2.4 * Math.sin(ph * 1.51)) * 0.6 + noise() * 0.4) * Math.exp(-t * 2.6) * Math.min(1, t * 60); }
  filterBuf(tear, 'bp', 1100, 1.2); mixInto(b, tear, 0.02, 0.55);
  // the panel itself shattering
  mixInto(b, metalPanel(4, 48, { brightness: 1.5, decay: 0.9, gain: 0.6 }));
  debrisField(b, 0.0, 0.5, 60, { gHi: 0.35, heavyChance: 0.35, density: 1.2 });   // shrapnel
  debrisField(b, 0.35, 4.8, 70, { gHi: 0.22, heavyChance: 0.3, density: 2.6 });   // cascade
  mixInto(b, creak(2.2, 18, 6, 0.12, 2), 1.8);                                    // hanging frame
  const dust = noiseBurst(sec, 0.6, 1800, 9000, 0.05); mixInto(b, dust, 0.2);
  softClip(b, 2.4);
  return normalize(fadeEdges(reverb(b, { size: 1.7, wet: 0.38, feedback: 0.86, damp: 0.45 }), 0.001, 0.4), -0.3);
}

function debrisSettle() {
  const sec = 4.5, b = buffer(sec);
  debrisField(b, 0.0, 3.8, 34, { gHi: 0.3, heavyChance: 0.2, density: 1.8, fLo: 1400 });
  mixInto(b, creak(1.8, 14, 5, 0.25, 2), 0.4);
  const dust = noiseBurst(sec, 0.7, 2500, 9000, 0.06); mixInto(b, dust);
  return normalize(fadeEdges(reverb(b, { size: 1.4, wet: 0.3 }), 0.002, 0.6), -4);
}

function monsterStep() {
  const sec = 1.3, b = buffer(sec);
  mixInto(b, thump(0.9, 95, 42, 9, 1));
  mixInto(b, noiseBurst(0.12, 45, 60, 700, 0.8));
  mixInto(b, metalPanel(0.9, 140, { brightness: 0.4, decay: 3, modes: 8, gain: 0.08 })); // floor grating
  return normalize(fadeEdges(reverb(b, { size: 1.8, wet: 0.45, feedback: 0.85 })), -3);
}

function lightBuzz() {
  const sec = 1.8, b = buffer(sec); let gate = 1, next = 0;
  for (let i = 0; i < b.length; i++) {
    const t = i / SR;
    if (t >= next) { gate = rnd() < 0.55 ? 1 : 0.05 + rnd() * 0.2; next = t + 0.02 + rnd() * 0.12; }
    let s = 0; for (let h = 1; h <= 17; h += 2) s += Math.sin(TAU * 60 * h * t) / h; // buzzy odd harmonics
    b[i] = (s * 0.5 + noise() * 0.05) * gate;
  }
  filterBuf(b, 'lp', 3500); filterBuf(b, 'hp', 90);
  for (let k = 0; k < 7; k++) mixInto(b, noiseBurst(0.02, 250, 3000, 14000, 0.6 + rnd() * 0.3), rnd() * (sec - 0.1)); // arcing ticks
  return normalize(fadeEdges(b, 0.01, 0.15), -6);
}

function cctvSignalLoss() {
  const sec = 1.4, b = buffer(sec); let v = 0, next = 0, f = 1000;
  for (let i = 0; i < b.length; i++) {
    const t = i / SR;
    if (t >= next) { f = 200 + rnd() * 2400; next = t + 0.012 + rnd() * 0.04; }
    const crush = Math.floor(Math.sin(TAU * f * t) * 4) / 4; // bit-crushed data tones
    const statics = noise() * Math.min(1, t / 0.45);
    v = t < 0.5 ? crush * 0.5 + statics * 0.4 : statics * 0.8 * Math.exp(-(t - 0.5) * 2.2);
    b[i] = v;
  }
  filterBuf(b, 'hp', 150);
  mixInto(b, thump(0.3, 160, 60, 20, 0.6), 0.5);
  return normalize(fadeEdges(b, 0.002, 0.2), -5);
}

function tvStaticBurst() {
  const sec = 3.2, b = buffer(sec); let ph = 0;
  for (let i = 0; i < b.length; i++) {
    const t = i / SR;
    const env = Math.min(1, t * 40) * (t < 1.4 ? 1 : Math.exp(-(t - 1.4) * 1.6));
    const am = 0.65 + 0.35 * Math.sign(Math.sin(TAU * 17 * t + 3 * Math.sin(TAU * 1.3 * t)));
    const hum = Math.tanh(4 * Math.sin(TAU * 50 * t)) * 0.3;
    const f = 620 + 380 * Math.sin(TAU * 0.7 * t) + 90 * Math.sin(TAU * 11 * t); ph += TAU * f / SR;
    const scream = Math.sin(ph + 3.2 * Math.sin(ph * 2.02)) * 0.35 * Math.min(1, t * 3);
    b[i] = (noise() * 0.7 * am + hum + scream) * env;
  }
  filterBuf(b, 'hp', 70);
  softClip(b, 2.2);
  return normalize(fadeEdges(b, 0.002, 0.3), -1.5);
}

function heartbeat() {
  const b = buffer(1.0);
  mixInto(b, thump(0.35, 70, 42, 16, 1));
  mixInto(b, thump(0.35, 62, 38, 16, 0.75), 0.26);
  filterBuf(b, 'lp', 180);
  return normalize(fadeEdges(b), -3);
}

function tinnitus() {
  const sec = 5, b = buffer(sec);
  for (let i = 0; i < b.length; i++) { const t = i / SR; const env = Math.min(1, t / 0.08) * Math.exp(-t * 0.55); b[i] = (Math.sin(TAU * 3720 * t) + 0.6 * Math.sin(TAU * 3733 * t) + 0.08 * noise()) * env; }
  filterBuf(b, 'bp', 3720, 3);
  return normalize(fadeEdges(b, 0.01, 0.8), -9);
}

function cameraDrop() {
  const sec = 1.1, b = buffer(sec);
  [[0, 1], [0.21, 0.55], [0.34, 0.3], [0.42, 0.18]].forEach(([t, g]) => {
    mixInto(b, thump(0.25, 420, 160, 30, 0.5 * g), t);
    mixInto(b, clink(900 + rnd() * 600, 35, 0.35 * g), t);
    mixInto(b, noiseBurst(0.04, 120, 800, 7000, 0.4 * g), t);
  });
  const scrape = noiseBurst(0.35, 6, 1200, 5000, 0.12); mixInto(b, scrape, 0.46);
  return normalize(fadeEdges(reverb(b, { size: 1.2, wet: 0.25 })), -4);
}

function doorCreak() {
  const b = creak(1.8, 26, 9, 1, 1.6);
  mixInto(b, metalPanel(1.8, 70, { brightness: 0.5, decay: 1.8, modes: 10, gain: 0.05 }));
  return normalize(fadeEdges(reverb(b, { size: 1.5, wet: 0.35 }), 0.02, 0.3), -5);
}

function tensionBed() {
  // 14 s bed: detuned sub drone that swells, with distant metallic scrapes; cut externally.
  const sec = 14.5, b = buffer(sec); const ph = [0, 0, 0, 0];
  const freqs = [41.2, 41.55, 61.8, 82.1];
  for (let i = 0; i < b.length; i++) {
    const t = i / SR; let s = 0;
    for (let k = 0; k < freqs.length; k++) { ph[k] += TAU * freqs[k] * (1 + 0.003 * Math.sin(TAU * 0.13 * t + k)) / SR; const saw = ((ph[k] / TAU) % 1) * 2 - 1; s += saw * (k < 2 ? 0.5 : 0.22); }
    b[i] = s * (0.35 + 0.65 * clamp(t / 12.5, 0, 1) ** 1.6);
  }
  sweepFilter(b, 'lp', t => 90 + 260 * clamp(t / 13, 0, 1) ** 2, 1.1);
  const air = noiseBurst(sec, 0, 300, 1200, 0.05); for (let i = 0; i < air.length; i++) air[i] *= 0.4 + 0.6 * Math.sin(TAU * 0.09 * i / SR) ** 2; mixInto(b, air);
  mixInto(b, creak(2.2, 9, 5, 0.06, 2.5), 3.5);
  mixInto(b, creak(2.0, 12, 4, 0.05, 2.5), 9.0);
  // high sinister harmonic that enters late
  const hi = buffer(6); for (let i = 0; i < hi.length; i++) { const t = i / SR; hi[i] = Math.sin(TAU * 1244.5 * t + 0.6 * Math.sin(TAU * 5.3 * t)) * 0.035 * clamp(t / 5, 0, 1); } mixInto(b, hi, 8.3);
  return normalize(fadeEdges(reverb(b, { size: 1.8, wet: 0.3 }), 1.2, 0.4), -6);
}

function dreadPad() {
  // post-breach: tritone dissonance, slowly rising, then choked.
  const sec = 7, b = buffer(sec); const ph = [0, 0, 0, 0, 0];
  const f = [36.7, 51.9, 73.4, 103.8, 146.8];
  for (let i = 0; i < b.length; i++) {
    const t = i / SR; let s = 0;
    for (let k = 0; k < f.length; k++) { ph[k] += TAU * f[k] * (1 + 0.012 * (t / sec) * (k % 2 ? 1 : -1)) / SR; s += Math.sin(ph[k] + 0.8 * Math.sin(ph[k] * 2)) / (k + 1); }
    b[i] = s * Math.min(1, t / 1.5) * (0.6 + 0.4 * t / sec);
  }
  const grit = noiseBurst(sec, 0, 200, 2500, 0.06); for (let i = 0; i < grit.length; i++) grit[i] *= Math.min(1, i / (3 * SR)); mixInto(b, grit);
  softClip(b, 1.6);
  return normalize(fadeEdges(reverb(b, { size: 1.8, wet: 0.35 }), 0.5, 0.25), -6);
}

writeWav('Breach_DoorImpact_Light.wav', doorImpact(false));
writeWav('Breach_DoorImpact_Heavy.wav', doorImpact(true));
writeWav('Breach_DoorExplode.wav', doorBreach());
writeWav('Breach_DebrisSettle.wav', debrisSettle());
writeWav('Breach_MonsterStep.wav', monsterStep());
writeWav('Breach_LightBuzz.wav', lightBuzz());
writeWav('Breach_CctvSignalLoss.wav', cctvSignalLoss());
writeWav('Breach_TvStaticBurst.wav', tvStaticBurst());
writeWav('Breach_Heartbeat.wav', heartbeat());
writeWav('Breach_Tinnitus.wav', tinnitus());
writeWav('Breach_CameraDrop.wav', cameraDrop());
writeWav('Breach_DoorCreak.wav', doorCreak());
writeWav('Breach_TensionBed.wav', tensionBed());
writeWav('Breach_DreadPad.wav', dreadPad());
