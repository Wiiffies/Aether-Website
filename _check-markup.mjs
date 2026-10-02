// Guards the markup of the pages themselves, because a single missing ">" is invisible in review and
// silent in a browser: the HTML parser recovers by dropping the tag you meant to write and keeping the
// one before it open. That is exactly how the Beta link on account.html and the Admin link on
// admin.html disappeared — the nav rendered "AccountBeta" as one link, a script then looked up an
// element that no longer existed, and every check in this repo still passed because nothing looked at
// the markup. Run it in CI next to the other `_check-*.mjs` scripts.
//
//   1. No end tag is followed straight away by another "<". `</a<a href=...>` is read as the end tag
//      `<a<a ...>`, which matches nothing and is dropped, so the anchor before it never closes.
//   2. Every element whose end tag is not optional is balanced. Inline scripts and styles are emptied
//      first, because their template strings contain markup that is not this page's own.
//   3. No page declares the same id twice (ids are what the page scripts look up).
//
// Usage: node _check-markup.mjs
import { readFileSync, readdirSync } from "node:fs";

const problems = [];
const pages = readdirSync(".").filter((f) => f.endsWith(".html"));
const read = (f) => readFileSync(f, "utf8");

const strip = (html) => html
  .replace(/<!--[\s\S]*?-->/g, "")
  // Keep the script/style tags themselves (they are balanced too), drop what is inside them: their
  // template strings and `/</g`-style regex literals describe markup without being markup.
  .replace(/(<script\b[^>]*>)[\s\S]*?(<\/script>)/gi, "$1$2")
  .replace(/(<style\b[^>]*>)[\s\S]*?(<\/style>)/gi, "$1$2");

// ---------- 1. a missing ">" in an end tag ----------
for (const page of pages) {
  const html = strip(read(page));
  for (const m of html.matchAll(/<\/[a-zA-Z][^<>]{0,60}</g)) {
    problems.push(page + ": malformed end tag starting " + JSON.stringify(m[0].slice(0, 40)) +
      " — a missing \">\" makes the parser drop the tag and leave the previous element open");
  }
}

// ---------- 2. balanced tags ----------
// Only elements whose end tag is required: `p`, `li`, `dt`, `dd`, `tr`, `td`, `th`, `option` and
// friends may legally be left open, so counting them would report the spec as a bug.
const MUST_CLOSE = [
  "html", "head", "body", "title", "header", "footer", "main", "nav", "section", "article", "aside",
  "div", "span", "a", "button", "form", "label", "table", "thead", "tbody", "ul", "ol", "select",
  "textarea", "script", "style", "iframe", "svg", "canvas", "video", "audio", "picture", "template",
];
for (const page of pages) {
  const html = strip(read(page));
  for (const tag of MUST_CLOSE) {
    const open = (html.match(new RegExp("<" + tag + "[\\s>]", "gi")) || []).length;
    const close = (html.match(new RegExp("</" + tag + "\\s*>", "gi")) || []).length;
    if (open !== close) {
      problems.push(page + ": <" + tag + "> is unbalanced — " + open + " opened, " + close +
        " closed; the browser will silently reparent whatever is inside the surplus one");
    }
  }
}

// ---------- 3. duplicate ids ----------
for (const page of pages) {
  // Scripts are emptied first for the same reason as above: `getElementById("x")` is not a declaration.
  const html = strip(read(page));
  const seen = new Map();
  for (const m of html.matchAll(/\sid="([^"]*)"/g)) {
    const id = m[1];
    seen.set(id, (seen.get(id) || 0) + 1);
  }
  for (const [id, count] of seen) {
    if (count > 1) problems.push(page + ": declares id \"" + id + "\" " + count + " times — the scripts here address elements by id, so only the first one is ever reached");
  }
}

if (problems.length) {
  console.error("\n" + problems.join("\n"));
  console.error("\nFAIL: the page markup has regressed.");
  process.exit(1);
}
console.log("markup: " + pages.length + " pages, no malformed end tags, " + MUST_CLOSE.length + " tag kinds balanced, no duplicate ids");
console.log("OK: the pages are well formed.");
