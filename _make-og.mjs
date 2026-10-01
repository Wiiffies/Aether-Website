// Derives the social preview card (og:image / twitter:image) from the real brand artwork.
//
// Why a separate file: `public/aether-logo.png` is 128x128 — perfect for favicons, too small for a
// link preview (Twitter's `summary_large_image` wants >= 300x157, Discord renders a small thumbnail
// below 256px). This script paints the 1200x630 card every page points at:
//
//   dark `#08080a` canvas (same as the site background) + a 1px frame + the mark centred at ~460px
//
// It reuses the dependency-free PNG decode/resize/encode approach of `_make-logo.mjs`, but
// composites onto an opaque canvas instead of writing a transparent square, so the output is a
// single card readable on both light and dark chat themes.
//
// Usage: node _make-og.mjs
import { readFileSync, writeFileSync } from "node:fs";
import { inflateSync, deflateSync } from "node:zlib";

const SRC = "Aether Logo trasnparent new.png";
const OUT = "public/og.png";
const CARD_W = 1200; // og:image standard
const CARD_H = 630;
const BG = [8, 8, 10];        // #08080a, the site background
const FRAME = [29, 32, 36];   // #1d2024, a hairline that reads on both themes
const FRAME_INSET = 36;
const MARK_MAX = 460;         // the mark fits inside this box, centred on the card

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

function canvas(w, h, rgb) {
  const px = Buffer.alloc(w * h * 4);
  for (let i = 0; i < w * h; i++) {
    px[i * 4] = rgb[0]; px[i * 4 + 1] = rgb[1]; px[i * 4 + 2] = rgb[2]; px[i * 4 + 3] = 255;
  }
  return px;
}

function setPx(px, w, x, y, rgb) {
  if (x < 0 || y < 0 || x >= w) return;
  const o = (y * w + x) * 4;
  if (o < 0 || o + 3 >= px.length) return;
  px[o] = rgb[0]; px[o + 1] = rgb[1]; px[o + 2] = rgb[2]; px[o + 3] = 255;
}

function frame(px, w, h, inset, rgb) {
  for (let x = inset; x < w - inset; x++) { setPx(px, w, x, inset, rgb); setPx(px, w, x, h - 1 - inset, rgb); }
  for (let y = inset; y < h - inset; y++) { setPx(px, w, inset, y, rgb); setPx(px, w, w - 1 - inset, y, rgb); }
}

// Box-filter scale of the mark straight onto the card, centred, with premultiplied alpha so the
// antialiased edges of the original blend cleanly against the background.
function drawMark(px, w, h, img, box, maxSide) {
  const cw = box.x1 - box.x0 + 1;
  const ch = box.y1 - box.y0 + 1;
  const scale = maxSide / Math.max(cw, ch);
  const dw = cw * scale, dh = ch * scale;
  const dx = (w - dw) / 2, dy = (h - dh) / 2;
  const cx = box.x0 + cw / 2, cy = box.y0 + ch / 2;
  for (let oy = 0; oy < Math.round(dh); oy++) {
    for (let ox = 0; ox < Math.round(dw); ox++) {
      const sx0 = cx - dw / 2 / scale + ox / scale;
      const sy0 = cy - dh / 2 / scale + oy / scale;
      const sx1 = sx0 + 1 / scale, sy1 = sy0 + 1 / scale;
      let r = 0, g = 0, b = 0, a = 0, n = 0;
      for (let sy = Math.floor(sy0); sy < Math.ceil(sy1); sy++) {
        if (sy < 0 || sy >= img.height) continue;
        for (let sx = Math.floor(sx0); sx < Math.ceil(sx1); sx++) {
          if (sx < 0 || sx >= img.width) continue;
          const o = (sy * img.width + sx) * 4;
          r += img.rgba[o]; g += img.rgba[o + 1]; b += img.rgba[o + 2]; a += img.rgba[o + 3];
          n++;
        }
      }
      if (!n) continue;
      r /= n; g /= n; b /= n; a /= n;
      const alpha = a / 255;
      if (alpha <= 0.004) continue;
      const x = Math.round(dx) + ox, y = Math.round(dy) + oy;
      if (x < 0 || y < 0 || x >= w) continue;
      const o = (y * w + x) * 4;
      if (o + 3 >= px.length) continue;
      // The artwork is white; keep its luminance but respect coverage.
      const lum = Math.round((r + g + b) / 3);
      px[o] = Math.round(px[o] * (1 - alpha) + lum * alpha);
      px[o + 1] = Math.round(px[o + 1] * (1 - alpha) + lum * alpha);
      px[o + 2] = Math.round(px[o + 2] * (1 - alpha) + lum * alpha);
      px[o + 3] = 255;
    }
  }
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

function pngChunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, "ascii"), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body));
  return Buffer.concat([len, body, crc]);
}

function encodeRgba(rgba, w, h) {
  const stride = w * 4;
  const raw = Buffer.alloc((stride + 1) * h);
  for (let y = 0; y < h; y++) {
    raw[y * (stride + 1)] = 0; // filter: none
    rgba.copy(raw, y * (stride + 1) + 1, y * stride, (y + 1) * stride);
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0);
  ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8;   // bit depth
  ihdr[9] = 6;   // RGBA
  ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    pngChunk("IHDR", ihdr),
    pngChunk("IDAT", deflateSync(raw, { level: 9 })),
    pngChunk("IEND", Buffer.alloc(0)),
  ]);
}

const src = readFileSync(SRC);
const img = decode(src);
const box = contentBox(img);
const card = canvas(CARD_W, CARD_H, BG);
frame(card, CARD_W, CARD_H, FRAME_INSET, FRAME);
drawMark(card, CARD_W, CARD_H, img, box, MARK_MAX);
const png = encodeRgba(card, CARD_W, CARD_H);
writeFileSync(OUT, png);

console.log(JSON.stringify({
  source: { width: img.width, height: img.height, bytes: src.length },
  contentBox: { w: box.x1 - box.x0 + 1, h: box.y1 - box.y0 + 1 },
  output: { file: OUT, width: CARD_W, height: CARD_H, bytes: png.length },
}, null, 1));
