// Guards the publishing metadata that is easy to drift by hand. Run in CI, alongside the other
// `_check-*.mjs` scripts:
//
//   1. Every `public/*.css|js` reference in every page carries a `?v=` number, and every reference
//      to the *same* asset agrees on it. Assets version independently — only the file that changed
//      needs a bump — but a page left behind on an old version is exactly how a stale `checkout.js`
//      silently disabled the account gates once, so mismatches fail the build.
//   2. `public/og.png` exists and is exactly 1200x630 — the size the `og:image` tags promise, which
//      chat clients use to pick the large card layout.
//   3. Every page has a meta description; every indexable page also has an `og:image` pointing at
//      that card, so a link share never falls back to a bare URL.
//
// Usage: node _check-assets.mjs
import { readFileSync, readdirSync } from "node:fs";

const problems = [];
const pages = readdirSync(".").filter((f) => f.endsWith(".html")).sort();

// ---------- 1. one cache-busting version across every shared asset ----------
const VERSIONED = /(?:src|href)="public\/([a-z-]+\.(?:css|js))(?:\?v=(\d+))?"/g;
const assets = new Map(); // asset file -> Map(version -> [page])
const unversioned = [];
for (const page of pages) {
  const html = readFileSync(page, "utf8");
  for (const m of html.matchAll(VERSIONED)) {
    const asset = m[1];
    if (!m[2]) { unversioned.push(page + ": public/" + asset); continue; }
    if (!assets.has(asset)) assets.set(asset, new Map());
    const byVersion = assets.get(asset);
    if (!byVersion.has(m[2])) byVersion.set(m[2], []);
    byVersion.get(m[2]).push(page);
  }
}
if (!assets.size) problems.push("no shared assets referenced at all — did the pages move?");
if (unversioned.length) problems.push("asset references without ?v=:\n    " + unversioned.join("\n    "));
for (const [asset, byVersion] of assets) {
  if (byVersion.size > 1) {
    problems.push("public/" + asset + " is referenced at different versions:\n" + [...byVersion.entries()]
      .map(([v, refs]) => "    ?v=" + v + " on " + refs.join(", "))
      .join("\n"));
  }
}
const summary = [...assets.entries()]
  .map(([asset, byVersion]) => asset + "?v=" + [...byVersion.keys()].join("/")).join(", ");

// ---------- 2. the OG card ----------
let ogSize = "";
try {
  const png = readFileSync("public/og.png");
  const magic = png.subarray(0, 8).toString("hex");
  if (magic !== "89504e470d0a1a0a") throw new Error("not a PNG");
  const w = png.readUInt32BE(16);
  const h = png.readUInt32BE(20);
  ogSize = w + "x" + h;
  if (w !== 1200 || h !== 630) problems.push("public/og.png is " + ogSize + ", expected 1200x630 (run node _make-og.mjs)");
} catch (e) {
  problems.push("public/og.png is missing or unreadable: " + e.message + " (run node _make-og.mjs)");
}

// ---------- 3. per-page description + og:image ----------
const indexable = [];
for (const page of pages) {
  const html = readFileSync(page, "utf8");
  const noindex = /<meta\s+name="robots"\s+content="[^"]*noindex/.test(html);
  const hasDesc = /<meta\s+name="description"\s+content="[^"]+"/.test(html);
  const hasOgImage = /<meta\s+property="og:image"\s+content="https:\/\/get-aether\.de\/public\/og\.png"/.test(html);
  if (!hasDesc) problems.push(page + ": no meta description");
  if (!noindex) {
    indexable.push(page);
    if (!hasOgImage) problems.push(page + ": indexable but no og:image pointing at public/og.png");
  }
}

// ---------- 4. a build's location must never live in a file that ships to the browser ----------
// Everything under public/ is public the moment it is committed, so a download URL or a checksum
// written there is readable by anyone and the gate around it is decoration. The program build lives
// in the Worker's environment instead (see programBuild() in worker/src/index.js), which is why
// finding one here is a failed build rather than a note in a review.
const PUBLIC_JS = readdirSync("public").filter((f) => f.endsWith(".js"));
const BUILD_KEY = /\b(downloadUrl|download_url|buildUrl|build_url)\s*:/i;
const HEX64_LITERAL = /["'][0-9a-f]{64}["']/i;
for (const asset of PUBLIC_JS) {
  const text = readFileSync("public/" + asset, "utf8");
  if (BUILD_KEY.test(text)) {
    problems.push("public/" + asset + ": assigns a download location in public JavaScript — the build's location belongs in the Worker's environment, reachable only through /api/program/download");
  }
  if (HEX64_LITERAL.test(text)) {
    problems.push("public/" + asset + ": contains a 64-character hex literal, which is what a published build checksum looks like — serve checksums from the API instead");
  }
}

console.log("asset versions across " + pages.length + " pages: " + summary);
console.log("og card: public/og.png " + (ogSize || "missing") + "; indexable pages with og:image: " + indexable.length);
if (problems.length) {
  console.error("\n" + problems.join("\n"));
  console.error("\nFAIL: publishing metadata is out of sync.");
  process.exit(1);
}
console.log("OK: asset versions and OG metadata are consistent.");
