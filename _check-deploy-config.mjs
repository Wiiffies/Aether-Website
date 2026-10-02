// Guards `worker/wrangler.toml`, the file a `wrangler deploy` treats as the source of truth. Two of
// its failure modes are silent and expensive, and both were real on 2026-10-02:
//
//   1. A variable declared here with an EMPTY value overwrites the live one on the next deploy —
//      `--keep-vars` only stops wrangler from *deleting* the variables it does not know about, it
//      still writes the ones this file declares. `ADMIN_EMAIL = ""` would have blanked the live
//      "alex.real.apple@gmail.com" and left the Worker with no administrator at all: every
//      `/api/admin/*` route answers 403 and nothing says why.
//   2. A route declared here is a route the deploy will create. A pattern that is not meant to change
//      must not be listed, or a code deploy quietly re-routes live traffic.
//
// Usage: node _check-deploy-config.mjs
import { readFileSync } from "node:fs";

const problems = [];
const toml = readFileSync("worker/wrangler.toml", "utf8");
const code = toml.split("\n").filter((line) => !/^\s*#/.test(line)).join("\n");

// ---------- 1. never declare an empty variable ----------
// `NAME = ""` (or `''`) is the dangerous shape; a commented example is not code and is skipped above.
for (const m of code.matchAll(/^\s*([A-Z][A-Z0-9_]*)\s*=\s*(""|'')\s*$/gm)) {
  problems.push("worker/wrangler.toml: `" + m[1] + " = \"\"` — a declared-but-empty variable overwrites the live value on the next deploy. Leave it out of this file and set it in the dashboard (keep_vars preserves it).");
}

// ---------- 2. keep_vars has to be on ----------
if (!/^\s*keep_vars\s*=\s*true\s*$/m.test(code)) {
  problems.push("worker/wrangler.toml: `keep_vars` is not true — a deploy would drop every variable that is only set in the dashboard");
}

// ---------- 3. only the routes that are actually live ----------
const routes = [...code.matchAll(/\{\s*pattern\s*=\s*"([^"]+)"/g)].map((m) => m[1]);
const LIVE_ROUTES = ["get-aether.de/api/*", "www.get-aether.de/api/*"];
for (const r of routes) {
  if (!LIVE_ROUTES.includes(r)) {
    problems.push("worker/wrangler.toml: declares the route `" + r + "` — a deploy creates every route listed here, so a routing change rides along with a code change. Add it deliberately, in its own deploy, with a reason next to it.");
  }
}
for (const r of LIVE_ROUTES) {
  if (!routes.includes(r)) {
    problems.push("worker/wrangler.toml: the live route `" + r + "` is missing — a deploy would replace the routes this file declares and unroute the API");
  }
}

// ---------- 4. the worker and the database this project actually uses ----------
if (!/^\s*name\s*=\s*"aether-api"\s*$/m.test(code)) {
  problems.push("worker/wrangler.toml: `name` is not aether-api");
}
if (!/database_id\s*=\s*"201052a6-bed3-4c1c-a3ee-e16394aa36e4"/.test(code)) {
  problems.push("worker/wrangler.toml: the D1 binding does not point at aether-db (201052a6-…)");
}
if (!/binding\s*=\s*"DB"/.test(code)) {
  problems.push("worker/wrangler.toml: the D1 binding is not named DB, which is the name the Worker reads");
}

if (problems.length) {
  console.error("\n" + problems.join("\n"));
  console.error("\nFAIL: a deploy from this configuration would change more than the code.");
  process.exit(1);
}
console.log("deploy config: " + routes.length + " live routes, no empty variable declarations, keep_vars on, D1 = aether-db");
console.log("OK: a deploy from this file changes code only.");
