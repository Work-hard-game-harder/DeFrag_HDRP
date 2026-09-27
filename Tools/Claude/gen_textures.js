// Particle textures for the breach cutscene: soft dust puff sheet (2x2), grit dot, spark.
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const OUT = process.argv[2] || path.join(__dirname, 'tex');
fs.mkdirSync(OUT, { recursive: true });

function crc32(buf) { let c, crc = 0xffffffff; for (let n = 0; n < buf.length; n++) { c = (crc ^ buf[n]) & 0xff; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; crc = (crc >>> 8) ^ c; } return (crc ^ 0xffffffff) >>> 0; }
function chunk(type, data) { const len = Buffer.alloc(4); len.writeUInt32BE(data.length); const td = Buffer.concat([Buffer.from(type), data]); const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td)); return Buffer.concat([len, td, crc]); }
function writePng(name, w, h, rgba) {
  const raw = Buffer.alloc((w * 4 + 1) * h);
  for (let y = 0; y < h; y++) { raw[y * (w * 4 + 1)] = 0; for (let x = 0; x < w * 4; x++) raw[y * (w * 4 + 1) + 1 + x] = rgba[(y * w) * 4 + x]; }
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  const png = Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
  fs.writeFileSync(path.join(OUT, name), png); console.log('wrote', name);
}

// value noise + fbm
let seed = 77;
const rnd = () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; };
const perm = Array.from({ length: 512 }, () => rnd());
const hash = (x, y) => perm[((x & 255) + perm[(y & 255)] * 255 | 0) & 511];
const smooth = t => t * t * (3 - 2 * t);
function vnoise(x, y) { const xi = Math.floor(x), yi = Math.floor(y), xf = x - xi, yf = y - yi; const a = hash(xi, yi), b = hash(xi + 1, yi), c = hash(xi, yi + 1), d = hash(xi + 1, yi + 1); const u = smooth(xf), v = smooth(yf); return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v; }
function fbm(x, y, oct = 5) { let s = 0, a = 0.5, f = 1; for (let i = 0; i < oct; i++) { s += a * vnoise(x * f, y * f); f *= 2.03; a *= 0.5; } return s; }

// Dust puff sheet: 256 px, 2x2 frames of irregular soft clouds.
{
  const S = 256, T = 128, px = new Uint8Array(S * S * 4);
  for (let fy = 0; fy < 2; fy++) for (let fx = 0; fx < 2; fx++) {
    const ox = rnd() * 50, oy = rnd() * 50;
    for (let y = 0; y < T; y++) for (let x = 0; x < T; x++) {
      const u = (x + 0.5) / T * 2 - 1, v = (y + 0.5) / T * 2 - 1;
      const r = Math.sqrt(u * u + v * v);
      const n = fbm(u * 2.6 + ox, v * 2.6 + oy);
      const edge = 1 - Math.min(1, r / (0.62 + 0.38 * n));
      let a = Math.pow(Math.max(0, edge), 1.6) * (0.55 + 0.45 * fbm(u * 5 + oy, v * 5 + ox, 4));
      a = Math.min(1, a * 1.5);
      const shade = 0.78 + 0.22 * fbm(u * 3 + 9, v * 3 + 4);
      const i = ((fy * T + y) * S + fx * T + x) * 4;
      px[i] = px[i + 1] = px[i + 2] = Math.round(255 * shade); px[i + 3] = Math.round(255 * a);
    }
  }
  writePng('Breach_DustPuff.png', S, S, px);
}
// Grit: soft irregular speck.
{
  const S = 64, px = new Uint8Array(S * S * 4);
  for (let y = 0; y < S; y++) for (let x = 0; x < S; x++) {
    const u = (x + 0.5) / S * 2 - 1, v = (y + 0.5) / S * 2 - 1; const r = Math.sqrt(u * u + v * v);
    const a = Math.max(0, 1 - r / (0.55 + 0.25 * fbm(u * 3, v * 3, 3))); const i = (y * S + x) * 4;
    px[i] = px[i + 1] = px[i + 2] = 230; px[i + 3] = Math.round(255 * Math.min(1, Math.pow(a, 0.8) * 1.4));
  }
  writePng('Breach_Grit.png', S, S, px);
}
// Spark: hot core with soft glow (used stretched).
{
  const S = 64, px = new Uint8Array(S * S * 4);
  for (let y = 0; y < S; y++) for (let x = 0; x < S; x++) {
    const u = (x + 0.5) / S * 2 - 1, v = (y + 0.5) / S * 2 - 1; const r = Math.sqrt(u * u + v * v);
    const core = Math.exp(-r * r * 40), glow = Math.exp(-r * r * 6) * 0.45; const a = Math.min(1, core + glow); const i = (y * S + x) * 4;
    px[i] = 255; px[i + 1] = Math.round(255 * Math.min(1, 0.55 + core)); px[i + 2] = Math.round(255 * Math.min(1, 0.2 + core * 0.9)); px[i + 3] = Math.round(255 * a);
  }
  writePng('Breach_Spark.png', S, S, px);
}
