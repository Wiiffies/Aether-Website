// Guards the account-recovery flow in the pages, where the guarantees are easy to lose in a good
// refactor and impossible to see in a screenshot. Run it in CI next to the other `_check-*.mjs`
// scripts.
//
//   1. Every page that can receive a secret in its query string (`reset-password.html`,
//      `verify-email.html`) and the one that can receive an email address (`forgot-password.html`)
//      is `noindex` and ships `<meta name="referrer" content="no-referrer">`. Without it the token
//      travels in the Referer header of any subresource the page pulls from another origin — the
//      Turnstile widget on the reset page is exactly that.
//   2. Those pages read the parameter exactly once, through a helper that immediately calls
//      `history.replaceState`, so the secret does not stay in the address bar or in the history of
//      everyone who opened the link on a shared machine. A second `location.search` read is how a
//      refactor silently reintroduces the leak.
//   3. The captured value is length-capped before it is sent anywhere.
//   4. A locked-out visitor actually has a way in: the sign-in card offers a button to
//      `forgot-password.html`, and the reset page refuses to submit without a token.
//   5. No page ever renders a token into an `href`, which is how a one-time link ends up in the DOM,
//      in a Referer and in someone's bookmarks.
//
// Usage: node _check-recovery.mjs
import { readFileSync, readdirSync } from "node:fs";

const problems = [];
const read = (f) => readFileSync(f, "utf8");

// ---------- 1. noindex + no-referrer on every secret-bearing page ----------
const SECRET_PAGES = ["forgot-password.html", "reset-password.html", "verify-email.html"];
for (const page of SECRET_PAGES) {
  const html = read(page);
  if (!/<meta name="robots" content="noindex">/.test(html)) {
    problems.push(page + ": must stay out of search indexes (noindex)");
  }
  if (!/<meta name="referrer" content="no-referrer">/.test(html)) {
    problems.push(page + ": no <meta name=\"referrer\" content=\"no-referrer\"> — the token or address would leak in the Referer header of any third-party subresource");
  }
}

// ---------- 2 + 3. read once, scrub, and cap ----------
const SCRUBBED = ["reset-password.html", "verify-email.html"];
for (const page of SCRUBBED) {
  const html = read(page);
  const reads = html.match(/location\.search/g) || [];
  if (reads.length !== 1) {
    problems.push(page + ": expected exactly one `location.search` read (inside the scrubbing helper), found " + reads.length);
  }
  if (!/function readToken\(\)/.test(html)) {
    problems.push(page + ": the token must be read through the readToken() helper that scrubs the URL");
  }
  if (!/history\.replaceState\(/.test(html)) {
    problems.push(page + ": the token is never taken back out of the address bar / history");
  }
  if (!/\.slice\(0, 200\)/.test(html)) {
    problems.push(page + ": the captured token is not length-capped before it is sent");
  }
}

// forgot-password.html takes an email address rather than a token: same scrub, no token cap needed.
const forgot = read("forgot-password.html");
if (!/history\.replaceState\(/.test(forgot)) {
  problems.push("forgot-password.html: the prefilled address is left in the address bar and in history");
}

// The secret is posted in a request body, never appended to a URL, where it would land in access
// logs, in the Referer and in whatever the user shares.
const api = read("public/checkout.js");
if (!/resetPassword\(token, password\)\{\s*const r = await postJson\(/.test(api)) {
  problems.push("public/checkout.js: resetPassword must POST the token in the body, not put it in the URL");
}
if (!/verifyEmail\(token\)\{ return postJson\(/.test(api)) {
  problems.push("public/checkout.js: verifyEmail must POST the token in the body, not put it in the URL");
}

// ---------- 4. a locked-out visitor has a way in ----------
const account = read("account.html");
if (!/id="acc-forgot"/.test(account)) {
  problems.push("account.html: the sign-in card has no \"Forgot password?\" control — a visitor who cannot sign in has no route to the reset flow");
}
if (!/forgot-password\.html/.test(account)) {
  problems.push("account.html: nothing links to forgot-password.html");
}
if (!/show\(\$\("acc-forgot"\), !reg\)/.test(account)) {
  problems.push("account.html: the forgot-password control is not hidden on the register tab, where there is no password to recover");
}
const reset = read("reset-password.html");
if (!/if \(!token\) \{/.test(reset) || !/btn\.disabled = true/.test(reset)) {
  problems.push("reset-password.html: a missing token must disable the submit button instead of offering a form that cannot work");
}

// ---------- 5. no token is ever rendered into a link ----------
for (const page of readdirSync(".").filter((f) => f.endsWith(".html"))) {
  const html = read(page);
  for (const m of html.matchAll(/href="[^"]*\btoken=/g)) {
    problems.push(page + ": renders a token into an href (" + m[0] + ") — one-time links belong in an email body, not in the DOM");
  }
}

if (problems.length) {
  console.error("\n" + problems.join("\n"));
  console.error("\nFAIL: the account-recovery flow has regressed.");
  process.exit(1);
}
console.log("recovery pages: " + SECRET_PAGES.length + " noindex + no-referrer, " + SCRUBBED.length + " with the token scrubbed from the URL");
console.log("sign-in card: offers a reset entry point; tokens travel in POST bodies, never in URLs");
console.log("OK: the account-recovery flow is intact.");
