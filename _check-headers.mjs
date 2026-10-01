// Guards the Content-Security-Policy that the edge sends with every page.
//
// A CSP is the one security control that can take the whole site down by accident: the day someone
// adds a font from a new CDN, an analytics snippet, an `eval()` or a `data:` script, the page stops
// working in production — and only in production, because nothing here runs the browser's rules
// locally. This script reads the policy from `_security-headers.mjs` (the same value the Cloudflare
// Response Header Transform rule sets) and proves the pages can still load under it.
//
//   1. The policy cannot be quietly weakened: no `'unsafe-eval'`, and the directives that do the real
//      work (`object-src 'none'`, `base-uri 'self'`, `frame-ancestors 'none'`, `form-action 'self'`,
//      `default-src 'self'`, `upgrade-insecure-requests`) have to be there.
//   2. Every externally loaded resource — `<script src>`, `<link href>`, `<img src>`, `<iframe src>`,
//      `url()` in CSS, `@import`, and every URL literal inside JavaScript — has to be an origin the
//      policy actually permits. This is the check that catches "we added a CDN" before an outage.
//   3. Nothing uses `eval()` or `new Function()`, which the policy does not allow.
//   4. No `data:` or `blob:` script/style/link targets, which the policy does not allow either.
//   5. The local dev server still serves the same headers, so the policy stays testable offline.
//
// Usage: node _check-headers.mjs
import { readFileSync, readdirSync } from "node:fs";
import { PAGE_SECURITY_HEADERS, SELF_ORIGINS, DEV_ONLY_ORIGINS, RESERVED_DOCUMENTATION_ORIGINS } from "./_security-headers.mjs";

const problems = [];
const read = (f) => readFileSync(f, "utf8");
const csp = String(PAGE_SECURITY_HEADERS["content-security-policy"] || "");

// ---------- 1. the policy itself ----------
if (!csp) problems.push("_security-headers.mjs: no content-security-policy is defined");
for (const must of ["default-src 'self'", "object-src 'none'", "base-uri 'self'", "frame-ancestors 'none'", "form-action 'self'", "upgrade-insecure-requests"]) {
  if (!csp.includes(must)) problems.push("CSP: `" + must + "` is missing — that directive is the whole reason the policy is worth sending");
}
if (csp.includes("'unsafe-eval'")) problems.push("CSP: 'unsafe-eval' is not allowed — nothing here needs it, and it re-opens the hole the policy exists to close");
for (const header of ["x-frame-options", "x-content-type-options", "referrer-policy", "permissions-policy", "cross-origin-opener-policy"]) {
  if (!PAGE_SECURITY_HEADERS[header]) problems.push("_security-headers.mjs: `" + header + "` is not set");
}

// ---------- parse the policy the way a browser would ----------
// directive -> list of sources, with the host sources reduced to origins.
const directives = {};
for (const part of csp.split(";")) {
  const bits = part.trim().split(/\s+/).filter(Boolean);
  if (!bits.length) continue;
  directives[bits[0]] = bits.slice(1);
}
const permittedOrigins = new Set(SELF_ORIGINS);
for (const [directive, sources] of Object.entries(directives)) {
  for (const s of sources) {
    if (/^https?:\/\//.test(s) && directive !== "form-action" && directive !== "frame-ancestors") {
      permittedOrigins.add(s.replace(/\/$/, ""));
    }
  }
}
const permitted = (origin) => permittedOrigins.has(origin.replace(/\/$/, ""));

// ---------- 2..4. every page and public asset under the policy ----------
const files = [];
for (const f of readdirSync(".")) if (f.endsWith(".html")) files.push(f);
for (const f of readdirSync("public")) if (/\.(js|css)$/.test(f)) files.push("public/" + f);

const LOADING_ATTRS = [
  /<script[^>]*\ssrc="(https?:\/\/[^"]+)"/gi,
  /<link[^>]*\shref="(https?:\/\/[^"]+)"/gi,
  /<img[^>]*\ssrc="(https?:\/\/[^"]+)"/gi,
  /<iframe[^>]*\ssrc="(https?:\/\/[^"]+)"/gi,
  /<source[^>]*\ssrc="(https?:\/\/[^"]+)"/gi,
  /url\(\s*["']?(https?:\/\/[^)"']+)/gi,
  /@import\s+["'](https?:\/\/[^"']+)/gi,
];

for (const file of files) {
  const body = read(file);

  // A resource the browser will fetch has to come from an origin the policy allows.
  for (const re of LOADING_ATTRS) {
    for (const m of body.matchAll(re)) {
      let origin; try { origin = new URL(m[1]).origin; } catch { continue; }
      if (!permitted(origin)) {
        problems.push(file + ": loads " + origin + " (" + m[0].slice(0, 60) + "…) but the CSP does not allow that origin — the page would break in production. Add it to the right directive in _security-headers.mjs and to the Cloudflare rule.");
      }
    }
  }

  // Any URL literal at all, including the ones JavaScript builds at runtime (`s.src = "https://…"`),
  // has to be something the policy permits. Dev-only addresses are listed rather than allowed.
  for (const m of body.matchAll(/["'`](https?:\/\/[A-Za-z0-9._-]+)/g)) {
    const origin = m[1];
    if (permitted(origin) || DEV_ONLY_ORIGINS.includes(origin) || RESERVED_DOCUMENTATION_ORIGINS.includes(origin)) continue;
    problems.push(file + ": mentions " + origin + " but the CSP does not allow it — if that URL is ever loaded or fetched the page breaks silently");
  }

  if (/[^.\w]eval\s*\(|new\s+Function\s*\(/.test(body)) {
    problems.push(file + ": uses eval()/new Function(), which the CSP blocks (no 'unsafe-eval') — the script would not run in production");
  }
  if (/(src|href)\s*=\s*["'](data|blob):/i.test(body)) {
    problems.push(file + ": loads a data:/blob: resource, which the CSP does not allow for scripts, styles or frames");
  }
}

// ---------- 5. the local server keeps serving the same policy ----------
const dev = read("_dev-server.mjs");
if (!/PAGE_SECURITY_HEADERS/.test(dev)) {
  problems.push("_dev-server.mjs: does not serve the page security headers — the policy could then only be discovered as broken in production");
}

if (problems.length) {
  console.error("\n" + problems.join("\n"));
  console.error("\nFAIL: a page would not load correctly under the deployed CSP.");
  process.exit(1);
}
console.log("CSP: " + Object.keys(directives).length + " directives, " + permittedOrigins.size + " permitted origins, no 'unsafe-eval'");
console.log("scanned " + files.length + " pages/assets: every loaded origin is allowed, no eval, no data:/blob: script");
console.log("OK: the pages stay loadable under the deployed policy.");
