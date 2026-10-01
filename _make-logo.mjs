// Derives the site's black-and-white logo from the original brand asset.
//
// `Aether Logo trasnparent new.png` is the real monochrome mark (white on transparent, 1536x1024)
// and it is used as-is by the Discord embed thumbnail. Shipping 1.2 MB on every page load is not
// acceptable, and the vector in public/aether-logo.svg is a *different* drawing of the mark, so
// this script crops the original to its content, pads it into a square and downsamples it to a
// small RGBA PNG — exact brand artwork, ~15 KB, no image libraries involved.
//
// Usage: node _make-logo.mjs
import { readFileSync, writeFileSync } from "node:fs";
import { inflateSync, deflateSync } from "node:zlib";

const SRC = "Aether Logo trasnparent new.png";
const OUT = "public/aether-logo.png";
const SIZE = 128; // output width/height in px
const PAD = 0.12; // square padding around the mark (keeps the circular CSS mask off the artwork)

function chunks(buf) {
  const out = [];
  let off = 8;
  while (off + 8 <= buf.length) {
    const len = buf.readUInt32BE(off);
    const type = buf.toString("ascii", off + 4, off + 8);
    out.push({ type, data: buf.subarray(off + 8, off + 8 + len) });
    off += 12 + len;
  }
  return out;
}

function unfilter(raw, width, height, bpp) {
  const stride = width * bpp;
  const out = Buffer.alloc(stride * height);
  let p = 0;
  for (let y = 0; y < height; y++) {
    const ft = raw[p++];
    const line = raw.subarray(p, p + stride);
    p += stride;
    const cur = out.subarray(y * stride, (y + 1) * stride);
    const prev = y > 0 ? out.subarray((y - 1) * stride, y * stride) : null;
    for (let x = 0; x < stride; x++) {
      const a = x >= bpp ? cur[x - bpp] : 0;
      const b = prev ? prev[x] : 0;
      const c = prev && x >= bpp ? prev[x - bpp] : 0;
      let v = line[x];
      if (ft === 1) v += a;
      else if (ft === 2) v += b;
      else if (ft === 3) v += (a + b) >> 1;
      else if (ft === 4) {
        const pp = a + b - c;
        const pa = Math.abs(pp - a), pb = Math.abs(pp - b), pc = Math.abs(pp - c);
        v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
      }
      cur[x] = v & 255;
    }
  }
  return out;
}

function decode(buf) {
  const list = chunks(buf);
  const ihdr = list.find((c) => c.type === "IHDR").data;
  const width = ihdr.readUInt32BE(0);
  const height = ihdr.readUInt32BE(4);
  const depth = ihdr[8];
  const colorType = ihdr[9];
  const interlace = ihdr[12];
  if (depth !== 8) throw new Error("expected 8-bit depth, got " + depth);
  if (interlace !== 0) throw new Error("interlaced PNG is not supported");
  const channels = { 0: 1, 2: 3, 3: 1, 4: 2, 6: 4 }[colorType];
  if (!channels) throw new Error("unsupported colour type " + colorType);
  const raw = inflateSync(Buffer.concat(list.filter((c) => c.type === "IDAT").map((c) => c.data)));
  const px = unfilter(raw, width, height, channels);
  let palette = null;
  if (colorType === 3) {
    const plte = list.find((c) => c.type === "PLTE");
    if (!plte) throw new Error("palette image without PLTE");
    const trns = list.find((c) => c.type === "tRNS");
    palette = [];
    for (let i = 0; i < plte.data.length; i += 3) {
      palette.push([plte.data[i], plte.data[i + 1], plte.data[i + 2], trns && i / 3 < trns.data.length ? trns.data[i / 3] : 255]);
    }
  }
  const rgba = Buffer.alloc(width * height * 4);
  for (let i = 0, o = 0; i < width * height; i++, o += channels) {
    let r, g, b, a;
    if (colorType === 6) [r, g, b, a] = [px[o], px[o + 1], px[o + 2], px[o + 3]];
    else if (colorType === 2) [r, g, b, a] = [px[o], px[o + 1], px[o + 2], 255];
    else if (colorType === 4) [r, g, b, a] = [px[o], px[o], px[o], px[o + 1]];
    else if (colorType === 0) [r, g, b, a] = [px[o], px[o], px[o], 255];
    else [r, g, b, a] = palette[px[o]];
    rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = a;
  }
  return { width, height, rgba };
}

function contentBox(img, alphaMin = 24) {
  let x0 = img.width, y0 = img.height, x1 = -1, y1 = -1;
  for (let y = 0; y < img.height; y++) {
    for (let x = 0; x < img.width; x++) {
      if (img.rgba[(y * img.width + x) * 4 + 3] > alphaMin) {
        if (x < x0) x0 = x;
        if (x > x1) x1 = x;
        if (y < y0) y0 = y;
        if (y > y1) y1 = y;
      }
    }
  }
  if (x1 < 0) throw new Error("the source image is fully transparent");
  return { x0, y0, x1, y1 };
}

// Box-filter downsample: correct, dependency-free, and the source is large enough that the
// averaging visibly cleans up the antialiasing of the original.
function resize(img, box, size, pad) {
  const cw = box.x1 - box.x0 + 1;
  const ch = box.y1 - box.y0 + 1;
  const side = Math.max(cw, ch);
  const scale = 1 / (1 + pad * 2);
  const target = side / scale; // virtual square that already contains the padding
  const cx = box.x0 + cw / 2;
  const cy = box.y0 + ch / 2;
  const out = Buffer.alloc(size * size * 4);
  const step = target / size;
  for (let oy = 0; oy < size; oy++) {
    for (let ox = 0; ox < size; ox++) {
      const sx0 = cx - target / 2 + ox * step;
      const sy0 = cy - target / 2 + oy * step;
      let r = 0, g = 0, b = 0, a = 0, n = 0;
      for (let sy = Math.floor(sy0); sy < Math.ceil(sy0 + step); sy++) {
        if (sy < 0 || sy >= img.height) continue;
        for (let sx = Math.floor(sx0); sx < Math.ceil(sx0 + step); sx++) {
          if (sx < 0 || sx >= img.width) continue;
          const o = (sy * img.width + sx) * 4;
          r += img.rgba[o]; g += img.rgba[o + 1]; b += img.rgba[o + 2]; a += img.rgba[o + 3];
          n++;
        }
      }
      const o = (oy * size + ox) * 4;
      if (n) { r /= n; g /= n; b /= n; a /= n; }
      // Premultiplied averaging avoids a dark halo around the antialiased edges.
      const alpha = a / 255;
      out[o] = alpha > 0 ? Math.round(r / alpha) : 0;
      out[o + 1] = alpha > 0 ? Math.round(g / alpha) : 0;
      out[o + 2] = alpha > 0 ? Math.round(b / alpha) : 0;
      out[o + 3] = Math.round(a);
      if (out[o] > 255) out[o] = 255;
      if (out[o + 1] > 255) out[o + 1] = 255;
      if (out[o + 2] > 255) out[o + 2] = 255;
    }
  }
  return out;
}

const CRC = (() => {
  const t = new Int32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c;
  }
  return t;
})();

function crc32(buf) {
  let c = -1;
  for (let i = 0; i < buf.length; i++) c = CRC[(c ^ buf[i]) & 255] ^ (c >>> 8);
  return (c ^ -1) >>> 0;
}

function chunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, "ascii"), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body));
  return Buffer.concat([len, body, crc]);
}

function encode(rgba, size) {
  const stride = size * 4;
  const raw = Buffer.alloc((stride + 1) * size);
  for (let y = 0; y < size; y++) {
    raw[y * (stride + 1)] = 0; // filter: none
    rgba.copy(raw, y * (stride + 1) + 1, y * stride, (y + 1) * stride);
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(size, 0);
  ihdr.writeUInt32BE(size, 4);
  ihdr[8] = 8;   // bit depth
  ihdr[9] = 6;   // RGBA
  ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", ihdr),
    chunk("IDAT", deflateSync(raw, { level: 9 })),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

const src = readFileSync(SRC);
const img = decode(src);
const box = contentBox(img);
const small = resize(img, box, SIZE, PAD);
const png = encode(small, SIZE);
writeFileSync(OUT, png);

let opaque = 0;
for (let i = 3; i < small.length; i += 4) if (small[i] > 40) opaque++;
console.log(JSON.stringify({
  source: { width: img.width, height: img.height, bytes: src.length },
  contentBox: { w: box.x1 - box.x0 + 1, h: box.y1 - box.y0 + 1 },
  output: { file: OUT, size: SIZE, bytes: png.length, opaquePct: +(opaque / (SIZE * SIZE) * 100).toFixed(1) },
}, null, 1));
