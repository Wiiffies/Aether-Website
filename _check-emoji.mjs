// Guards the plain-text rule for everything the product sends or shows. Run it in CI next to the
// other `_check-*.mjs` scripts.
//
// The operator's decision (2026-10-03) is that no notification, status line or page carries colour
// emoji: a glyph that renders as a blank box in one mail client, a differently-coloured shape in the
// next and noise in a screen reader is worse than a word that reads the same everywhere. The
// temptation to decorate the next notification is strong and invisible in review, which is exactly
// why the rule is enforced here instead of by memory.
//
// Three rules, so none can drift alone:
//
//   1. **No emoji anywhere in tracked text files.** The ranges below cover the blocks real emoji live
//      in (pictographs, emoticons, transport, dingbats, symbols, flags, keycaps, variation
//      selectors, zero-width joiners). They deliberately do NOT cover the typography the site uses on
//      purpose: `—` (U+2014), `€`, `×`, `≈`, `≤`, box-drawing, and the plain arrows `←` `→` `↗`
//      (U+2190/2192/2197), which are not in an emoji block and are not scanned. Note the scanner
//      starts at U+2000, so every typographic character below that is out of scope by construction.
//   2. **The functional glyphs stay allowed, and nothing else joins them.** `☰` (menu), `✓` (done),
//      `✕` (close) and `♦` (the ETH coin chip) are UI shapes with no default emoji presentation, and
//      `▶` `►` `▼` (U+25B6/25BA/25BC) are the diagram arrows in the docs and the CSS select marker.
//      The allowlist is written out one glyph at a time so adding an eighth is a deliberate,
//      reviewable act rather than an accident.
//   3. **The allowlist itself is checked.** A glyph listed there that is not actually inside an emoji
//      block is a dead entry that hides what the allowlist is for, so that fails the build too.
//
// Usage: node _check-emoji.mjs
import { readFileSync } from "node:fs";
import { execSync } from "node:child_process";

const problems = [];

// ---------- 1. emoji blocks ----------
// Listed by name so the intent of each range survives the next reader.
const EMOJI_BLOCKS = [
  [0x2300, 0x23ff, "technical (\u231A \u231B \u23E9)"],
  [0x2460, 0x24ff, "enclosed alphanumerics (\u2460 \u2469)"],
  [0x25a0, 0x25ff, "geometric shapes (\u25AA \u25B6 \u25FC) \u2014 the emoji-presentation ones are the point"],
  [0x2600, 0x27bf, "misc symbols + dingbats (\u2600 \u2611 \u2705 \u2702 \u2728 \u2708)"],
  [0x2b00, 0x2bff, "misc symbols and arrows (\u2B1B \u2B50)"],
  [0x1f000, 0x1faff, "emoji (pictographs, emoticons, transport, flags, skin tones)"],
  [0xfe0f, 0xfe0f, "variation selector-16 (turns a text glyph into a colour emoji)"],
  [0x200d, 0x200d, "zero-width joiner (only ever used to build emoji sequences)"],
  [0x20e3, 0x20e3, "combining enclosing keycap"],
];

// The glyphs the pages and the coin chip use on purpose. Anything not on this list that falls in an
// emoji block is a failed build, so the next `🎉` has to be argued for rather than merged.
const ALLOWED = new Map([
  [0x2630, "\u2630 menu button"],
  [0x2713, "\u2713 done / verified"],
  [0x2715, "\u2715 close / remove"],
  [0x2666, "\u2666 Ethereum coin chip"],
  [0x25b6, "\u25B6 diagram arrow in README"],
  [0x25ba, "\u25BA diagram arrow in BETA.md"],
  [0x25bc, "\u25BC CSS select marker"],
]);

const TEXT_FILE = /\.(html|htm|css|js|mjs|json|md|txt|xml|yml|yaml|toml|webmanifest)$/i;
const SKIP = /^(package-lock\.json|\.env\.example)$/;

const files = execSync("git ls-files", { encoding: "utf8" })
  .split(/\r?\n/)
  .filter((f) => f && TEXT_FILE.test(f) && !SKIP.test(f));

const offenders = new Map(); // "U+XXXX char" -> [file]
for (const file of files) {
  let text;
  try { text = readFileSync(file, "utf8"); } catch { continue; }
  for (const ch of text) {
    const cp = ch.codePointAt(0);
    if (cp < 0x2000) continue; // typography (— € × ·) lives below this and is never emoji
    if (ALLOWED.has(cp)) continue;
    const block = EMOJI_BLOCKS.find(([lo, hi]) => cp >= lo && cp <= hi);
    if (!block) continue;
    const key = "U+" + cp.toString(16).toUpperCase().padStart(4, "0") + " " + JSON.stringify(ch);
    if (!offenders.has(key)) offenders.set(key, { block: block[2], files: new Set() });
    offenders.get(key).files.add(file);
  }
}

for (const [key, info] of offenders) {
  problems.push(
    key + " (" + info.block + ") in " + [...info.files].slice(0, 4).join(", ") +
    (info.files.size > 4 ? " (+" + (info.files.size - 4) + " more)" : "") +
    " — notifications and pages use plain-text labels (PAID, FAILED, PENDING); if this glyph is a" +
    " functional UI shape, add it to ALLOWED in _check-emoji.mjs on purpose"
  );
}

// ---------- 2. escaped emoji, which a plain text scan cannot see ----------
// The worker must stay 100% ASCII (a non-ASCII byte once turned every Discord embed character into
// `?`), so its emoji were always written as `\u2705` escapes — five ASCII characters that carry a
// colour emoji to the client and are invisible to a scan of the decoded text. That is exactly how a
// glyph would sneak back in: not as a literal, but as an escape. So every `\uXXXX` escape in a file
// that ships is decoded and run through the same block check.
//
// Scoped to shipped code on purpose: `*.md` is not published (`_config.yml` excludes it) and the
// update log has to be able to *name* the glyphs that were removed, which means writing their
// escapes. A doc describing `\u2705` is history; a page or a Worker sending one is the bug.
//
// Surrogate pairs are the subtle half. Everything above U+FFFF is written as TWO escapes
// (`\uD83D\uDCAC`), and neither half is an emoji on its own — the first lands in the surrogate range
// and the second is a low surrogate, so a per-escape check sees nothing. A JavaScript engine joins
// them into one code point at parse time, which is precisely how the worker used to encode every
// pictograph. So a high surrogate followed immediately by a low surrogate is decoded as a pair first,
// and only then tested.
const SHIPPED = /\.(html|htm|css|js|mjs|json|xml|yml|yaml|toml|webmanifest)$/i;
const ESCAPE_SEQ = /\\u\{?([0-9a-fA-F]{4,6})\}?/g;
const PAIR = /\\u(D[89AB][0-9A-Fa-f]{2})\\u(D[C-F][0-9A-Fa-f]{2})/g;
const escapeOffenders = new Map();
function checkEscape(cp, label, file) {
  if (ALLOWED.has(cp)) return;
  const block = EMOJI_BLOCKS.find(([lo, hi]) => cp >= lo && cp <= hi);
  if (!block) return;
  if (!escapeOffenders.has(label)) escapeOffenders.set(label, { block: block[2], files: new Set() });
  escapeOffenders.get(label).files.add(file);
}
for (const file of files.filter((f) => SHIPPED.test(f))) {
  let text;
  try { text = readFileSync(file, "utf8"); } catch { continue; }
  // 1. pairs first, so `\uD83D\uDCAC` is judged as U+1F4AC and not as two harmless halves.
  //    The span of each pair is recorded so the pass below can skip BOTH of its escapes: skipping
  //    only the first would report the low surrogate as a "lone surrogate", which is a false alarm
  //    on every well-formed pair.
  const pairSpans = [];
  for (const m of text.matchAll(PAIR)) {
    const hi = parseInt(m[1], 16), lo = parseInt(m[2], 16);
    const cp = 0x10000 + ((hi - 0xd800) << 10) + (lo - 0xdc00);
    pairSpans.push([m.index, m.index + m[0].length]);
    checkEscape(cp, "\\u" + m[1].toUpperCase() + "\\u" + m[2].toUpperCase(), file);
  }
  const insidePair = (i) => pairSpans.some(([s, e]) => i >= s && i < e);
  // 2. every escape that is not part of a pair, on its own.
  for (const m of text.matchAll(ESCAPE_SEQ)) {
    if (insidePair(m.index)) continue;
    const cp = parseInt(m[1], 16);
    // A lone surrogate is a broken pair, not a character; report it as such rather than ignoring it.
    if (cp >= 0xd800 && cp <= 0xdfff) {
      const key = "lone surrogate \\u" + m[1].toUpperCase();
      if (!escapeOffenders.has(key)) escapeOffenders.set(key, { block: "half of a surrogate pair", files: new Set() });
      escapeOffenders.get(key).files.add(file);
      continue;
    }
    checkEscape(cp, "\\u" + m[1].toUpperCase().padStart(4, "0"), file);
  }
}
for (const [key, info] of escapeOffenders) {
  problems.push(
    "escaped " + key + " (" + info.block + ") in " + [...info.files].slice(0, 4).join(", ") +
    " — an escape is still an emoji at runtime, and it is how one would sneak past a text scan;" +
    " write the plain-text label instead (PAID, FAILED, PENDING)"
  );
}

// ---------- 3. the allowlist itself stays honest ----------
for (const [cp, why] of ALLOWED) {
  if (!EMOJI_BLOCKS.some(([lo, hi]) => cp >= lo && cp <= hi)) {
    problems.push(
      "ALLOWED lists U+" + cp.toString(16).toUpperCase() + " (" + why + "), which is outside every " +
      "emoji block \u2014 the entry is dead weight and hides what the allowlist is actually for"
    );
  }
}

console.log("scanned " + files.length + " tracked text files for emoji");
console.log("allowed functional glyphs: " + [...ALLOWED].map(([cp]) => String.fromCodePoint(cp)).join(" "));
if (problems.length) {
  console.error("\n" + problems.join("\n"));
  console.error("\nFAIL: colour emoji found in the product.");
  process.exit(1);
}
console.log("OK: no emoji in any tracked text file — notifications stay plain text.");
