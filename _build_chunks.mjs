import { readFileSync, writeFileSync, rmSync, existsSync } from "node:fs";
import { gzipSync } from "node:zlib";
import { createHash } from "node:crypto";

const CHUNK = 2000;
const src = readFileSync("worker/src/index.js");
// ASCII-only is a hard requirement: a non-ASCII byte in the source is what turned
// every euro sign, arrow and apostrophe in the Discord embed into "?".
const nonAscii = [...src].filter(b => b > 127).length;
if (nonAscii > 0) {
  console.error(`ABORT: worker/src/index.js has ${nonAscii} non-ASCII byte(s). Escape them as \\uXXXX before deploying.`);
  process.exit(1);
}
const sha = createHash("sha256").update(src).digest("hex");
const gz = gzipSync(src, { level: 9 });
const b64 = gz.toString("base64");

// clean previous chunk files
for (let i = 0; i < 40; i++) {
  const f = `_gz2_${String(i).padStart(2, "0")}.txt`;
  if (existsSync(f)) rmSync(f);
}

const parts = [];
for (let i = 0; i < b64.length; i += CHUNK) parts.push(b64.slice(i, i + CHUNK));
parts.forEach((p, i) => writeFileSync(`_gz2_${String(i).padStart(2, "0")}.txt`, p));

console.log(JSON.stringify({
  srcBytes: src.length,
  nonAscii: [...src].filter(b => b > 127).length,
  sha256: sha,
  gzBytes: gz.length,
  b64Len: b64.length,
  chunkCount: parts.length,
  chunks: parts.map((p, i) => {
    let sum = 0;
    for (let k = 0; k < p.length; k++) sum += p.charCodeAt(k);
    return { i, key: `aether-gz-${i}`, len: p.length, sum, head: p.slice(0, 10), tail: p.slice(-10) };
  }),
}, null, 1));
