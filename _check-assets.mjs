// Guards the publishing metadata that is easy to drift by hand. Run in CI, alongside the other
// `_check-*.mjs` scripts:
//
//   1. Every `public/*.css|js` reference in every page carries the same `?v=` number and none is
//      left unversioned. This is the lesson from the stale `checkout.js` incident: Cloudflare caches
//      HTML and assets for hours, so a shared file that changes behaviour must change its URL.
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
const VERSIONED = /(?:src|href)="public\/[a-z-]+\.(?:css|js)(\?v=(\d+))?"/g;
const versions = new Map(); // version -> [references]
const unversioned = [];
for (const page of pages) {
  const html = readFileSync(page, "utf8");
  for (const m of html.matchAll(VERSIONED)) {
    const ref = m[0].slice(m[0].indexOf("public/"), -1);
    if (!m[2]) unversioned.push(page + ": " + ref);
    else {
      if (!versions.has(m[2])) versions.set(m[2], []);
      versions.get(m[2]).push(page + ": " + ref);
    }
  }
}
if (unversioned.length) problems.push("asset references without ?v=:\n    " + unversioned.join("\n    "));
if (versions.size > 1) {
  problems.push("asset versions disagree across the site:\n" + [...versions.entries()]
    .map(([v, refs]) => "    ?v=" + v + " (" + refs.length + " refs, e.g. " + refs[0] + ")").join("\n"));
}
const current = versions.size === 1 ? [...versions.keys()][0] : (versions.keys().next().value || "?");
if (!versions.size) problems.push("no shared assets referenced at all — did the pages move?");

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

console.log("asset version: ?v=" + current + " across " + pages.length + " pages");
console.log("og card: public/og.png " + (ogSize || "missing") + "; indexable pages with og:image: " + indexable.length);
if (problems.length) {
  console.error("\n" + problems.join("\n"));
  console.error("\nFAIL: publishing metadata is out of sync.");
  process.exit(1);
}
console.log("OK: asset versions and OG metadata are consistent.");
