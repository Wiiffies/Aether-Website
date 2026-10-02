// Local development server — NOT deployed, contains no secrets.
//   node _dev-server.mjs   ->  http://127.0.0.1:5501
//
// It serves the static site exactly like GitHub Pages does and routes /api/* through the
// real worker (worker/src/index.js) against an in-memory SQLite database. That means the
// same-origin cookie flow, CORS rules, rate limits and validation can all be tested locally
// without touching production data.
import { DatabaseSync } from "node:sqlite";
import { createServer } from "node:http";
import { PAGE_SECURITY_HEADERS } from "./_security-headers.mjs";
import { readFile, stat } from "node:fs/promises";
import { extname, join, normalize, resolve } from "node:path";
import { randomBytes } from "node:crypto";

const ROOT = resolve(process.cwd());
// Some shells export an empty PORT var — only accept a sane explicit value.
const PORT = (() => {
  const raw = Number(process.env.DEV_PORT || process.env.PORT);
  return Number.isInteger(raw) && raw > 1023 && raw < 65536 ? raw : 5501;
})();

// ---------- D1-compatible stub (same shape as _test-worker.mjs) ----------
const norm = (a) => a.map(v => (v === undefined ? null : typeof v === "boolean" ? (v ? 1 : 0) : v));
class Stmt {
  constructor(db, sql, args) { this.db = db; this.sql = sql; this.args = args || []; }
  bind(...args) { return new Stmt(this.db, this.sql, args); }
  async first() { const r = this.db.prepare(this.sql).get(...norm(this.args)); return r === undefined ? null : r; }
  async all() { return { results: this.db.prepare(this.sql).all(...norm(this.args)), success: true }; }
  async run() {
    const r = this.db.prepare(this.sql).run(...norm(this.args));
    return { success: true, meta: { last_row_id: Number(r.lastInsertRowid), changes: Number(r.changes) } };
  }
}
class DB {
  constructor() {
    this.db = new DatabaseSync(":memory:");
    this.db.exec(`
      CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, email TEXT NOT NULL UNIQUE, password_hash TEXT NOT NULL, discord TEXT, created_at TEXT NOT NULL DEFAULT (datetime('now')), email_verified INTEGER DEFAULT 0, role TEXT DEFAULT 'user');
      CREATE TABLE sessions (token TEXT PRIMARY KEY, user_id INTEGER NOT NULL, created_at TEXT NOT NULL DEFAULT (datetime('now')), expires_at INTEGER NOT NULL);
      CREATE TABLE auth_tokens (token_hash TEXT PRIMARY KEY, user_id INTEGER NOT NULL, purpose TEXT NOT NULL, expires_at INTEGER NOT NULL, created_at INTEGER NOT NULL, payload TEXT);
      CREATE TABLE rate_limits (rl_key TEXT PRIMARY KEY, count INTEGER NOT NULL, window_start INTEGER NOT NULL);
      CREATE TABLE orders (id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER, order_id TEXT NOT NULL UNIQUE, purchase_id TEXT, payment_id TEXT, amount REAL, currency TEXT DEFAULT 'eur', type TEXT, package TEXT, description TEXT, status TEXT DEFAULT 'pending', promo_code TEXT, discount REAL DEFAULT 0, meta TEXT, extra TEXT, created_at TEXT NOT NULL DEFAULT (datetime('now')), email TEXT);
      CREATE UNIQUE INDEX idx_orders_purchase_id_dev ON orders(purchase_id);
      CREATE TABLE messages (id INTEGER PRIMARY KEY AUTOINCREMENT, order_id TEXT NOT NULL, conversation_id TEXT, user_id INTEGER, sender TEXT NOT NULL DEFAULT 'customer', body TEXT NOT NULL, created_at TEXT NOT NULL DEFAULT (datetime('now')));
      CREATE TABLE conversations (id INTEGER PRIMARY KEY AUTOINCREMENT, conversation_id TEXT NOT NULL UNIQUE, user_id INTEGER, order_id TEXT, purchase_id TEXT, subject TEXT, status TEXT DEFAULT 'open', assigned_admin TEXT, created_at TEXT, updated_at TEXT);
      CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT, updated_at TEXT);
      CREATE TABLE audit_log (id INTEGER PRIMARY KEY AUTOINCREMENT, actor_user_id INTEGER, actor_email TEXT, action TEXT, target TEXT, detail TEXT, created_at TEXT);
      CREATE TABLE beta_feedback (id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER, email TEXT, area TEXT, body TEXT, created_at TEXT);
    `);
  }
  prepare(sql) { return new Stmt(this.db, sql); }
}

const adminEmail = process.env.DEV_ADMIN_EMAIL || "admin@local.test";
const adminPassword = randomBytes(9).toString("base64url") + "1a";

const env = {
  DB: new DB(),
  CONTACT_TO: "questions@get-aether.de,business@get-aether.de",
  CONTACT_FROM: "Aether <questions@get-aether.de>",
  // No ALLOWED_ORIGIN here on purpose: the worker then falls back to the real allowlist.
  ALLOW_DEV_ORIGIN: "true",
  SITE_URL: `http://127.0.0.1:${PORT}`,
  // Includes a test:true code so the 100% + free-order path can be exercised locally. The live
  // deployment keeps its own codes in the dashboard (PROMO_CODES), never in this repo.
  PROMO_CODES: '{"WELCOME10":{"type":"percent","value":10},"TESTFULL":{"type":"percent","value":100,"test":true}}',
  ADMIN_EMAILS: adminEmail,
  // Force the verified-email gate without configuring a mail provider, so the "your address is not
  // confirmed" paths can be exercised locally:
  //   REQUIRE_EMAIL_VERIFICATION=true node _dev-server.mjs
  // Empty means "decide it the way production does" (on when a provider is configured).
  REQUIRE_EMAIL_VERIFICATION: process.env.REQUIRE_EMAIL_VERIFICATION || "",
  // Program builds (program.html). Passed through from the shell so the tester-only download can be
  // exercised locally:
  //   PROGRAM_URL=http://127.0.0.1:5501/public/aether-logo.png PROGRAM_VERSION=0.1.0-beta.1 \
  //     node _dev-server.mjs
  // The Worker streams whatever PROGRAM_URL returns, so any local file stands in for a real build.
  // Loopback http is only accepted while ALLOW_DEV_ORIGIN is on (it is, below) - the live worker
  // refuses a plaintext build source outright.
  PROGRAM_NAME: process.env.PROGRAM_NAME || "Aether Desktop",
  PROGRAM_VERSION: process.env.PROGRAM_VERSION || "",
  PROGRAM_PLATFORM: process.env.PROGRAM_PLATFORM || "",
  PROGRAM_SIZE: process.env.PROGRAM_SIZE || "",
  PROGRAM_SHA256: process.env.PROGRAM_SHA256 || "",
  PROGRAM_VERIFY_MAX_BYTES: process.env.PROGRAM_VERIFY_MAX_BYTES || "",
  PROGRAM_URL: process.env.PROGRAM_URL || "",
};

const worker = (await import("./worker/src/index.js")).default;

// A demo admin account so /admin.html can be exercised locally.
{
  const res = await worker.fetch(new Request(`http://127.0.0.1:${PORT}/api/auth/register`, {
    method: "POST",
    headers: { "content-type": "application/json", origin: `http://127.0.0.1:${PORT}`, "cf-connecting-ip": "127.0.0.1" },
    body: JSON.stringify({ email: adminEmail, password: adminPassword, discord: "localdev" }),
  }), env, {});
  if (res.status !== 200) console.error("Could not create the local admin account:", res.status, await res.text());
  // The demo admin has no inbox to confirm from, and the Worker (correctly) requires a confirmed admin
  // address for the sensitive changes it guards - granting Tester, changing the Beta domain. Without
  // this, forcing the verification gate on locally would lock the only privileged account out of the
  // panel that the gate is being tested through.
  env.DB.prepare("UPDATE users SET email_verified = 1 WHERE email = ?").bind(adminEmail).run();
}

// ---------- static files ----------
const MIME = {
  ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8",
  ".mjs": "text/javascript; charset=utf-8", ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8", ".svg": "image/svg+xml", ".png": "image/png",
  ".jpg": "image/jpeg", ".webp": "image/webp", ".ico": "image/x-icon", ".txt": "text/plain; charset=utf-8",
  ".woff2": "font/woff2", ".map": "application/json; charset=utf-8",
};

async function serveStatic(pathname, res) {
  let rel = decodeURIComponent(pathname);
  if (rel === "/" || rel === "") rel = "/index.html";
  const target = resolve(join(ROOT, normalize(rel).replace(/^(\.\.[/\\])+/, "")));
  if (!target.startsWith(ROOT)) { res.writeHead(403).end("Forbidden"); return; }
  try {
    const info = await stat(target);
    if (info.isDirectory()) { res.writeHead(404).end("Not found"); return; }
    const body = await readFile(target);
    res.writeHead(200, {
      "content-type": MIME[extname(target).toLowerCase()] || "application/octet-stream",
      "cache-control": "no-store",
      // The same policy the edge applies (a Cloudflare Response Header Transform rule sets these on
      // get-aether.de and www, skipping /api/*). Serving them here means a policy that would break a
      // page breaks it locally first, where it costs nothing to fix.
      ...PAGE_SECURITY_HEADERS,
      // GitHub Pages announces the length of everything it serves, and the worker decides whether it
      // can verify a build from exactly that header - so the local server has to send it too, or the
      // verified path could never be exercised here.
      "content-length": String(body.length),
    });
    res.end(body);
  } catch {
    // GitHub Pages serves 404.html for unknown paths — mirror that here.
    try {
      const body = await readFile(join(ROOT, "404.html"));
      res.writeHead(404, { "content-type": "text/html; charset=utf-8", "cache-control": "no-store" });
      res.end(body);
    } catch { res.writeHead(404).end("Not found"); }
  }
}

// ---------- cookie name shim (local development only) ----------
// The Worker's session cookie is `__Host-aether_session`, which is the right name over https: it is
// pinned to the exact host, cannot carry a Domain and must use Path=/. Browsers refuse a `__Host-`
// cookie that arrives over plain http - including http://127.0.0.1, which is where this dev server
// runs - so a session created here would be dropped by the browser and every signed-in page would
// look signed out. This shim renames the cookie in both directions, so the production cookie name
// stays exactly as deployed and the local flow still exercises the same server code.
const DEV_SESSION_COOKIE = "aether_session";
const PROD_SESSION_COOKIE = "__Host-aether_session";
function devIncomingCookie(raw){
  return String(raw || "").split(";").map(part => {
    const [k, ...rest] = part.trim().split("=");
    if (k.trim() === DEV_SESSION_COOKIE) return PROD_SESSION_COOKIE + "=" + rest.join("=");
    return part.trim();
  }).filter(Boolean).join("; ");
}
function devOutgoingCookie(value){
  return String(value).replace(/^__Host-aether_session=/, DEV_SESSION_COOKIE + "=");
}

// ---------- server ----------
createServer(async (req, res) => {
  const url = new URL(req.url, `http://127.0.0.1:${PORT}`);
  if (url.pathname.startsWith("/api/") || url.pathname === "/api") {
    const chunks = [];
    for await (const chunk of req) chunks.push(chunk);
    const body = chunks.length ? Buffer.concat(chunks) : undefined;
    const headers = { ...req.headers };
    if (headers.cookie) headers.cookie = devIncomingCookie(headers.cookie);
    if (!headers["cf-connecting-ip"]) headers["cf-connecting-ip"] = req.socket.remoteAddress || "127.0.0.1";
    const request = new Request(url.toString(), { method: req.method, headers, body, duplex: "half" });
    try {
      const started = Date.now();
      const out = await worker.fetch(request, env, { waitUntil() {}, passThroughOnException() {} });
      // Bytes, not text: a build download is binary, and decoding it as UTF-8 would replace every
      // byte that is not valid UTF-8 and hand back a file that does not match its own checksum.
      const buf = Buffer.from(await out.arrayBuffer());
      const outHeaders = {};
      out.headers.forEach((v, k) => { outHeaders[k] = v; });
      const cookies = (out.headers.getSetCookie ? out.headers.getSetCookie() : []).map(devOutgoingCookie);
      if (cookies.length) outHeaders["set-cookie"] = cookies;
      delete outHeaders["transfer-encoding"];
      // Keep the worker's own length when it set one (that is what a client would get in production),
      // and describe the body when it deliberately did not.
      if (!outHeaders["content-length"]) outHeaders["content-length"] = String(buf.length);
      res.writeHead(out.status, outHeaders);
      res.end(buf);
      console.log(`${req.method} ${url.pathname}${url.search} -> ${out.status} (${Date.now() - started}ms)`);
    } catch (e) {
      console.error("worker error", e);
      res.writeHead(500, { "content-type": "application/json" }).end(JSON.stringify({ error: "dev worker crashed" }));
    }
    return;
  }
  await serveStatic(url.pathname, res);
}).listen(PORT, "127.0.0.1", () => {
  console.log(`\nAether dev server: http://127.0.0.1:${PORT}`);
  console.log(`  admin account (in-memory only): ${adminEmail} / ${adminPassword}`);
  console.log(`  API is served same-origin at /api/* through the real worker`);
  console.log(`  session cookie: deployed as ${PROD_SESSION_COOKIE}, sent here as ${DEV_SESSION_COOKIE}`);
  console.log(`  (browsers reject the __Host- name over http, so this server translates it both ways)\n`);
});
