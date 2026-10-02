// Guards the tester download (program.html + the /api/program endpoints), where every guarantee is
// invisible in a screenshot and easy to lose in a reasonable-looking refactor. Run it in CI next to
// the other `_check-*.mjs` scripts.
//
//   1. The build's location is a secret, so it is never written into anything the browser receives:
//      not into a config file, not into a page, not into a URL. The single-use ticket travels as a
//      path-narrowed HttpOnly cookie instead, which is why no URL here ever carries one.
//   2. The gate is the server's: the page cannot decide who may download, and it must keep taking
//      the download path from the API response rather than hardcoding the endpoint.
//   3. Spending a ticket needs the session that minted it, and the artifact is hashed against the
//      published checksum before any of it is sent.
//   4. A tester must be able to verify what they received by hand, so the checksum, the copy control
//      and the per-platform commands stay on the page.
//
// Usage: node _check-program.mjs
import { readFileSync, readdirSync } from "node:fs";

const problems = [];
const read = (f) => readFileSync(f, "utf8");
const must = (cond, msg) => { if (!cond) problems.push(msg); };

// ---------- 1. the page ships no location and no secret ----------
const page = read("program.html");
must(/<meta name="referrer" content="no-referrer">/.test(page),
  "program.html: no <meta name=\"referrer\" content=\"no-referrer\"> — the one-time download request would leak through the Referer header of any subresource");
must(!/ticket=|"?\?ticket/.test(page),
  "program.html: a ticket is being put into a URL — the ticket must travel as a cookie set by the server, never in the address bar, in history or in a log");
must(!/(downloadUrl|download_url|buildUrl|build_url|mirrorUrl|fileUrl)\s*:/.test(page),
  "program.html: assigns a download location — the build's location belongs in the Worker's environment, reachable only through /api/program/download");
must(!/\b[0-9a-f]{64}\b/.test(page),
  "program.html: contains a 64-hex literal — a checksum or ticket written into the page is either stale or a leak");
must(!/localStorage|sessionStorage/.test(page),
  "program.html: writes the download state to device storage — the ticket is single use and must not outlive the request");
must(!/program\.(version|size|sha256|sha|url|downloadUrl|build)\b/.test(page),
  "program.html: reads build metadata out of the public config — version, size and checksum come from the API, which is the only place they can be trusted");

// ---------- 2. the gate and the path are the server's ----------
must(/location\.href = ticket\.url/.test(page),
  "program.html: must navigate to the path the API returned (`ticket.url`) instead of a URL of its own");
must(!/href="[^"]*api\/program/.test(page),
  "program.html: links straight to a program API endpoint — the page must ask the API for the path");
must(/<button[^>]*id="dl-button"[^>]*type="button"/.test(page),
  "program.html: the download control must be a plain button — an <a href> invites a copy-pasteable link and a prefetch");
must(!/<a[^>]*id="dl-button"/.test(page),
  "program.html: the download control is an anchor again — it must stay a button that asks the server");

// ---------- 3. a tester can still verify the bytes ----------
must(/id="dl-copy"/.test(page), "program.html: no way to copy the published checksum");
must(/certutil -hashfile/.test(page) && /shasum -a 256/.test(page) && /sha256sum/.test(page),
  "program.html: the per-platform checksum instructions are gone — an unsigned build without a checkable checksum is a leap of faith");

// ---------- 4. the server side of the same guarantees ----------
const worker = read("worker/src/index.js");
must(!/\?ticket=/.test(worker) && !/ticket=" \+ ticket/.test(worker),
  "worker/src/index.js: a ticket is built into a URL again — tickets belong in a path-narrowed HttpOnly cookie");
must(/Path=\/api\/program\/file/.test(worker) && /SameSite=Strict/.test(worker) && /HttpOnly; Secure/.test(worker),
  "worker/src/index.js: the download ticket cookie must stay path-narrowed, HttpOnly, Secure and SameSite=Strict");
must(/COOKIE_REQUIRED/.test(worker),
  "worker/src/index.js: the mint no longer refuses a browser that cannot carry the ticket — it would hand out a ticket that cannot work");
must(/WRONG_SESSION/.test(worker) && /PROGRAM_TICKET_COOKIE/.test(worker),
  "worker/src/index.js: the ticket is no longer bound to the session that minted it — a copied ticket would be usable by whoever copied it");
must(/crypto\.subtle\.digest\("SHA-256", buf\)/.test(worker) && /CHECKSUM_MISMATCH/.test(worker),
  "worker/src/index.js: the artifact is no longer hashed against the published checksum before it is sent");
must(/program\.download\.mismatch/.test(worker),
  "worker/src/index.js: a refused build is not audited any more");
must(/x-content-verified/.test(worker) && /content-digest/.test(worker) && /x-checksum-sha256/.test(worker),
  "worker/src/index.js: the integrity headers (verified state, digest, checksum) are gone — a client can no longer tell a checked file from an unchecked one");
must(/programSourceOk/.test(worker) && /u\.protocol === "https:"/.test(worker),
  "worker/src/index.js: the build source is no longer restricted to https — plaintext can be rewritten in transit and a checksum would only describe the rewritten file");

// ---------- 5. nothing public knows where the build lives ----------
const publicFiles = [];
for (const f of readdirSync("public")) {
  if (f.endsWith(".js") || f.endsWith(".css") || f.endsWith(".json") || f.endsWith(".txt")) publicFiles.push("public/" + f);
}
for (const f of readdirSync(".")) if (f.endsWith(".html")) publicFiles.push(f);
// Whole-line comments are allowed to name the variables (that is how the next person finds out where a
// build is published); code and trailing comments are not, because that is where an actual value hides.
const codeOnly = (body) => body.split("\n").filter((line) => !/^\s*(\/\/|\*|\/\*)/.test(line)).join("\n");
for (const f of publicFiles) {
  const body = codeOnly(read(f));
  for (const name of ["PROGRAM_URL", "PROGRAM_SHA256", "PROGRAM_VERSION", "PROGRAM_PLATFORM", "PROGRAM_SIZE"]) {
    if (body.includes(name)) problems.push(f + ": references " + name + " outside a comment — the build's metadata and location are the Worker's environment, never the browser's");
  }
}

// ---------- 6. the public config stays presentation-only ----------
const cfg = read("public/aether-config.js");
const block = cfg.slice(cfg.indexOf("program: {"), cfg.indexOf("};", cfg.indexOf("program: {")));
must(block.length > 0, "public/aether-config.js: the program block is gone (its name/tagline copy feeds the page)");
for (const key of ["version", "size", "sha", "url", "download", "platform"]) {
  must(!new RegExp("\\b" + key + "\\s*:").test(block),
    "public/aether-config.js: the program block declares `" + key + "` — that file ships to every browser, so anything about the actual build belongs in the Worker's environment");
}
must(!/api\/program\/file/.test(read("public/checkout.js")),
  "public/checkout.js: hardcodes the file endpoint — the path is handed out by the API so it can change without a page deploy");

// ---------- 7. the Tester's own pages stay honest ----------
// A Tester account reaches the Beta, the build and their own chats, and the three ways that access can
// be refused (signed out, address unconfirmed, no role) have three different fixes. A page that answers
// all of them with "testers only" tells two of those people something untrue about their account, and a
// page that half-renders (the Beta address stuck on "Loading…" because one element went missing) tells
// a Tester the feature is broken. These are the cheapest possible guards on all of it.
const account = read("account.html");
const denied = read("beta.html");
const admin = read("admin.html");
for (const [file, body] of [["account.html", account], ["admin.html", admin], ["beta.html", denied]]) {
  must(/function show\(el, on\)\{ if \(el\)/.test(body),
    file + ": `show(el, on)` must tolerate a missing element (`if (el)`) — otherwise one renamed id silently aborts the rest of the page's script, which is exactly how the Beta address stayed on \"Loading…\"");
}
must(/id="nav-beta"/.test(account) && /<a[^>]*id="nav-beta"[^>]*href="beta.html"/.test(account) || /<a[^>]*href="beta.html"[^>]*id="nav-beta"/.test(account),
  "account.html: the Beta nav link is gone — a tester has no way back to the Beta area from the nav");
must(/id="acc-beta-card"/.test(account) && /acc-beta-link/.test(account),
  "account.html: the Tester card (or its link) is gone");
must(/href="program.html"[^>]*>Download the build|href="program\.html"/.test(account),
  "account.html: a Tester is not pointed at the build any more");
for (const code of ["SESSION_REQUIRED", "EMAIL_UNVERIFIED", "NOT_TESTER"]) {
  must(denied.includes(code),
    "beta.html: no handling for the " + code + " refusal — a Tester who only has to confirm their address is told something that is not true about their account");
  must(worker.includes(code) && /requireTesterOrExplain/.test(worker),
    "worker/src/index.js: the Tester gate no longer distinguishes " + code + " — every refusal collapses back into one sentence");
}
must(/id="beta-copy"/.test(denied) && /navigator\.clipboard\.writeText/.test(denied),
  "beta.html: no way to copy the Beta address the server reported");
must(/dl-locked-why/.test(page) && /EMAIL_UNVERIFIED/.test(page),
  "program.html: a Tester blocked only by an unconfirmed address is not told which one thing to fix");
must(/data-user-filter/.test(admin) && /Testers only/.test(admin),
  "admin.html: the Tester filter is gone — an admin can no longer see at a glance who has the role");
must(/r\.warnings/.test(admin) && /warnings/.test(worker) && /emailVerificationRequired/.test(worker),
  "admin.html + worker: the grant no longer reports that an unconfirmed address still blocks the Beta and the build");

if (problems.length) {
  console.error("\n" + problems.join("\n"));
  console.error("\nFAIL: the tester download has regressed.");
  process.exit(1);
}
console.log("download page: no location, no ticket in a URL, no build metadata from public config");
console.log("download server: https-only source, ticket bound to the minting session, checksum verified before sending");
console.log("tester pages: three refusal reasons answered separately, build linked, Beta address copyable, role grants honest about unconfirmed addresses");
console.log("OK: the tester download and the tester account pages are intact.");
