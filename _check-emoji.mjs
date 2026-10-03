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
//      in (pictographs, emoticons, transport, dingbats, symbols, flags, keycaps, variation selectors,
//      zero-width joiners). They deliberately do NOT cover the typography the site uses on purpose:
//      the em dash, euro sign, multiplication sign, box-drawing, and the plain arrows at U+2190/2192/
//      2197, none of which sit in an emoji block. The scanner also starts at U+2000, so every
//      typographic character below that is out of scope by construction.
//   2. **Escaped emoji count too, including surrogate pairs.** The Worker must stay 100% ASCII, so its
//      emoji were written as five-character escapes that a text scan cannot see, and every glyph
//      above U+FFFF arrives as a *pair* of escapes whose halves are not emoji on their own. A pair is
//      therefore decoded into one code point before it is judged, and a lone surrogate is reported as
//      a broken pair rather than ignored.
//   3. **The functional glyphs stay allowed, and nothing else joins them.** The menu icon, the check
//      and cross marks, the Ethereum chip and the three diagram/CSS arrows are UI shapes with no
//      default emoji presentation. The allowlist names them one at a time so adding another is a
//      deliberate, reviewable act. The allowlist is itself checked: a glyph listed there that is not
//      actually inside an emoji block is a dead entry and fails the build.
//
// Note on this file: it contains no literal glyph and no escape sequence, because it is scanned by
// its own rules like everything else. Blocks and examples are therefore described by code point
// (U+2705) rather than by showing the character — which also keeps the guard honest about the thing
// it is asking other files not to do.
//
// Usage: node _check-emoji.mjs
import { readFileSync } from "node:fs";
import { execSync } from "node:child_process";

const problems = [];

// ---------- 1. emoji blocks ----------
// Named by range so the intent of each block survives the next reader.
const EMOJI_BLOCKS = [
  [0x2300, 0x23ff, "technical symbols (watch, hourglass, fast-forward)"],
  [0x2460, 0x24ff, "enclosed alphanumerics (circled digits and letters)"],
  [0x25a0, 0x25ff, "geometric shapes (the emoji-presentation ones are the point)"],
  [0x2600, 0x27bf, "misc symbols + dingbats (sun, ballot box, check mark, scissors, sparkles, plane)"],
  [0x2b00, 0x2bff, "misc symbols and arrows (large squares, star)"],
  [0x1f000, 0x1faff, "emoji (pictographs, emoticons, transport, flags, skin tones)"],
  [0xfe0f, 0xfe0f, "variation selector-16 (turns a text glyph into a colour emoji)"],
  [0x200d, 0x200d, "zero-width joiner (only ever used to build emoji sequences)"],
  [0x20e3, 0x20e3, "combining enclosing keycap"],
];

// The glyphs the pages use on purpose, by code point. Anything not on this list that falls inside an
// emoji block is a failed build, so the next party popper has to be argued for rather than merged.
const ALLOWED = new Map([
  [0x2630, "menu button"],
  [0x2713, "check mark (done / verified)"],
  [0x2715, "multiplication x (close / remove)"],
  [0x2666, "diamond suit (Ethereum coin chip)"],
  [0x25b6, "right-pointing triangle (diagram arrow in README)"],
  [0x25ba, "right-pointing pointer (diagram arrow in BETA.md)"],
  [0x25bc, "down-pointing triangle (CSS select marker)"],
]);

const TEXT_FILE = /\.(html|htm|css|js|mjs|json|md|txt|xml|yml|yaml|toml|webmanifest)$/i;
const SKIP = /^(package-lock\.json|\.env\.example)$/;

const files = execSync("git ls-files", { encoding: "utf8" })
  .split(/\r?\n/)
  .filter((f) => f && TEXT_FILE.test(f) && !SKIP.test(f));

// ---------- 1a. literal glyphs ----------
const offenders = new Map(); // "U+XXXX" -> { block, files }
for (const file of files) {
  let text;
  try { text = readFileSync(file, "utf8"); } catch { continue; }
  for (const ch of text) {
    const cp = ch.codePointAt(0);
    if (cp < 0x2000) continue; // typography (em dash, euro sign, middle dot) lives below this
    if (ALLOWED.has(cp)) continue;
    const block = EMOJI_BLOCKS.find(([lo, hi]) => cp >= lo && cp <= hi);
    if (!block) continue;
    const key = "U+" + cp.toString(16).toUpperCase().padStart(4, "0");
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

// ---------- 1b. escaped glyphs, which a plain text scan cannot see ----------
// Scoped to files that ship. `*.md` is not published (see `_config.yml`) and the update log has to be
// able to *name* the glyphs that were removed, so the docs are exempt from this half only — a doc
// describing a removed escape is history, a page or a Worker sending one is the bug.
const SHIPPED = /\.(html|htm|css|js|mjs|json|xml|yml|yaml|toml|webmanifest)$/i;
const ESCAPE_SEQ = /\\u\{?([0-9a-fA-F]{4,6})\}?/g;
// A high surrogate followed immediately by a low surrogate: two escapes, one code point.
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
  // Pairs first, so a two-escape sequence is judged as the one character it becomes at parse time.
  // Each pair's span is recorded so the pass below can skip BOTH of its escapes: skipping only the
  // first would report the low surrogate as a lone surrogate, a false alarm on every valid pair.
  const pairSpans = [];
  for (const m of text.matchAll(PAIR)) {
    const hi = parseInt(m[1], 16), lo = parseInt(m[2], 16);
    const cp = 0x10000 + ((hi - 0xd800) << 10) + (lo - 0xdc00);
    pairSpans.push([m.index, m.index + m[0].length]);
    checkEscape(cp, "pair ending in " + m[2].toUpperCase(), file);
  }
  const insidePair = (i) => pairSpans.some(([s, e]) => i >= s && i < e);
  // Then every escape that is not part of a pair, on its own.
  for (const m of text.matchAll(ESCAPE_SEQ)) {
    if (insidePair(m.index)) continue;
    const cp = parseInt(m[1], 16);
    // A lone surrogate is a broken pair, not a character; report it rather than ignoring it.
    if (cp >= 0xd800 && cp <= 0xdfff) {
      const key = "lone surrogate " + m[1].toUpperCase();
      if (!escapeOffenders.has(key)) escapeOffenders.set(key, { block: "half of a surrogate pair", files: new Set() });
      escapeOffenders.get(key).files.add(file);
      continue;
    }
    checkEscape(cp, "U+" + m[1].toUpperCase().padStart(4, "0"), file);
  }
}
for (const [key, info] of escapeOffenders) {
  problems.push(
    "escaped glyph " + key + " (" + info.block + ") in " + [...info.files].slice(0, 4).join(", ") +
    " — an escape is still an emoji at runtime, and it is how one would sneak past a text scan;" +
    " write the plain-text label instead (PAID, FAILED, PENDING)"
  );
}

// ---------- 2. the allowlist itself stays honest ----------
for (const [cp, why] of ALLOWED) {
  if (!EMOJI_BLOCKS.some(([lo, hi]) => cp >= lo && cp <= hi)) {
    problems.push(
      "ALLOWED lists U+" + cp.toString(16).toUpperCase() + " (" + why + "), which is outside every " +
      "emoji block — the entry is dead weight and hides what the allowlist is actually for"
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
