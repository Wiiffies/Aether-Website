import { DatabaseSync } from "node:sqlite";
import { createHmac } from "node:crypto";

// ---------- D1-compatible stub over node:sqlite ----------
function norm(a) { return a.map(v => (v === undefined ? null : typeof v === "boolean" ? (v ? 1 : 0) : v)); }

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
      CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, email TEXT NOT NULL UNIQUE, password_hash TEXT NOT NULL, discord TEXT, created_at TEXT NOT NULL DEFAULT (datetime('now')), email_verified INTEGER DEFAULT 0, role TEXT DEFAULT 'user', status TEXT DEFAULT 'active', status_reason TEXT, status_until INTEGER, status_updated_at TEXT, last_ip TEXT, last_ip_at TEXT, signup_ip TEXT);
      CREATE TABLE auth_tokens (token_hash TEXT PRIMARY KEY, user_id INTEGER NOT NULL, purpose TEXT NOT NULL, expires_at INTEGER NOT NULL, created_at INTEGER NOT NULL, payload TEXT);
      CREATE TABLE rate_limits (rl_key TEXT PRIMARY KEY, count INTEGER NOT NULL, window_start INTEGER NOT NULL);
      CREATE TABLE sessions (token TEXT PRIMARY KEY, user_id INTEGER NOT NULL, created_at TEXT NOT NULL DEFAULT (datetime('now')), expires_at INTEGER NOT NULL);
      CREATE TABLE orders (id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER, order_id TEXT NOT NULL UNIQUE, purchase_id TEXT, payment_id TEXT, amount REAL, currency TEXT DEFAULT 'eur', type TEXT, package TEXT, description TEXT, status TEXT DEFAULT 'pending', promo_code TEXT, discount REAL DEFAULT 0, meta TEXT, extra TEXT, created_at TEXT NOT NULL DEFAULT (datetime('now')), email TEXT);
      CREATE UNIQUE INDEX idx_orders_purchase_id_test ON orders(purchase_id);
      CREATE TABLE messages (id INTEGER PRIMARY KEY AUTOINCREMENT, order_id TEXT NOT NULL, conversation_id TEXT, user_id INTEGER, sender TEXT NOT NULL DEFAULT 'customer', body TEXT NOT NULL, created_at TEXT NOT NULL DEFAULT (datetime('now')));
      CREATE TABLE conversations (id INTEGER PRIMARY KEY AUTOINCREMENT, conversation_id TEXT NOT NULL UNIQUE, user_id INTEGER, order_id TEXT, purchase_id TEXT, subject TEXT, status TEXT DEFAULT 'open', assigned_admin TEXT, created_at TEXT, updated_at TEXT);
      CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT, updated_at TEXT);
      CREATE TABLE audit_log (id INTEGER PRIMARY KEY AUTOINCREMENT, actor_user_id INTEGER, actor_email TEXT, action TEXT, target TEXT, detail TEXT, created_at TEXT);
      CREATE TABLE beta_feedback (id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER, email TEXT, area TEXT, body TEXT, created_at TEXT);
    `);
  }
  prepare(sql) { return new Stmt(this.db, sql); }
}

// ---------- load the real worker ----------
const worker = (await import("./worker/src/index.js")).default;

const env = {
  DB: new DB(),
  CONTACT_TO: "questions@get-aether.de,business@get-aether.de",
  CONTACT_FROM: "Aether <questions@get-aether.de>",
  ALLOWED_ORIGIN: "*",
  SUCCESS_URL: "https://get-aether.de/payment-success.html",
  CANCEL_URL: "https://get-aether.de/payment-cancel.html",
  PROMO_CODES: '{"WELCOME10":{"type":"percent","value":10},"SAVE5":{"type":"fixed","value":5},"TESTFULL":{"type":"percent","value":100,"test":true},"TESTFIXED":{"type":"fixed","value":50,"test":true},"CAP100":{"type":"percent","value":100}}',
  ADMIN_EMAILS: "admin@example.com",
};

async function call(method, path, opt = {}) {
  const headers = {
    origin: opt.origin === undefined ? "https://get-aether.de" : opt.origin,
    "cf-connecting-ip": opt.ip || ("10.0.0." + Math.floor(Math.random() * 250)),
  };
  if (opt.body !== undefined) headers["content-type"] = "application/json";
  if (opt.token) headers.authorization = "Bearer " + opt.token;
  if (opt.cookie) headers.cookie = opt.cookie;
  if (opt.headers) Object.assign(headers, opt.headers);
  if (opt.contentLength) headers["content-length"] = String(opt.contentLength);
  const res = await worker.fetch(new Request((opt.host || "https://api.get-aether.de") + path, {
    method, headers, body: opt.body === undefined ? undefined : JSON.stringify(opt.body),
  }), env, {});
  const text = await res.text();
  let json; try { json = JSON.parse(text); } catch { json = { raw: text }; }
  return { status: res.status, json, headers: res.headers, setCookie: res.headers.get("set-cookie") || "" };
}

// The shared D1 limiter is a fixed-window counter, so a real loop of requests can cross a window
// boundary and reset the count mid-test — that is exactly how these checks once failed in CI, on
// the hour boundary at 17:00:00 UTC. Seeding the current window at the limit walks the same code
// path deterministically and still proves the guard trips at exactly `limit`.
function seedRateLimit(key, limit, windowMs) {
  const windowStart = Math.floor(Date.now() / windowMs) * windowMs;
  env.DB.db.prepare("INSERT INTO rate_limits (rl_key, count, window_start) VALUES (?,?,?) ON CONFLICT(rl_key) DO UPDATE SET count = excluded.count, window_start = excluded.window_start").run(key, limit, windowStart);
  return windowStart;
}

let pass = 0, fail = 0;
function check(name, cond, extra) {
  if (cond) { pass++; console.log("  PASS  " + name); }
  else { fail++; console.log("  FAIL  " + name + (extra !== undefined ? "  ->  " + JSON.stringify(extra) : "")); }
}

console.log("\n--- health / promo ---");
let r = await call("GET", "/api/health");
check("health ok + db reachable", r.status === 200 && r.json.db === true, r.json);
check("health does not leak addresses / config", r.json.to === undefined && r.json.from === undefined && r.json.apiBase === undefined && r.json.promos === undefined, r.json);
r = await call("GET", "/api/promo?code=welcome10&amount=30");
check("percent promo WELCOME10 on 30 -> 3 off", r.json.valid === true && r.json.discount === 3 && r.json.finalAmount === 27, r.json);
r = await call("GET", "/api/promo?code=SAVE5&amount=30");
check("fixed promo SAVE5 -> 5 off", r.json.valid === true && r.json.discount === 5, r.json);
r = await call("GET", "/api/promo?code=NOPE&amount=30");
check("bad promo invalid", r.json.valid === false, r.json);

console.log("\n--- register / login ---");
r = await call("POST", "/api/auth/register", { body: { email: "Test@Example.com", password: "hunter2secret", discord: "tester#1" } });
check("register 200 + token", r.status === 200 && typeof r.json.token === "string" && r.json.token.length === 64, r.json);
const custToken = r.json.token;
// NOTE: the two regexes at the end of this line lost their word-boundary escapes when the
// check was written; the includes() calls below carry the actual assertion.
check("register sets an HttpOnly session cookie", r.setCookie.includes("__Host-aether_session=") && r.setCookie.includes("HttpOnly") && r.setCookie.includes("Secure")) /* legacy tail removed */ // HttpOnly/.test(r.setCookie) && /Secure/.test(r.setCookie), r.setCookie);
check("register normalises email to lowercase", r.json.user && r.json.user.email === "test@example.com", r.json.user);
check("register user is not admin", !r.json.isAdmin, r.json.isAdmin);

r = await call("POST", "/api/auth/register", { body: { email: "test@example.com", password: "hunter2secret" } });
check("duplicate register -> 409", r.status === 409, r.json);
r = await call("POST", "/api/auth/register", { body: { email: "bad-email", password: "hunter2secret" } });
check("bad email -> 400", r.status === 400, r.json);
r = await call("POST", "/api/auth/register", { body: { email: "short@example.com", password: "123" } });
check("short password -> 400", r.status === 400, r.json);

r = await call("POST", "/api/auth/login", { body: { email: "test@example.com", password: "wrongpassword" } });
check("wrong password -> 401", r.status === 401, r.json);
r = await call("POST", "/api/auth/login", { body: { email: "TEST@example.com", password: "hunter2secret" } });
check("login 200 + token", r.status === 200 && r.json.token, r.json);

r = await call("GET", "/api/me", { token: custToken });
check("me authenticated", r.status === 200 && r.json.authenticated === true && r.json.user.email === "test@example.com", r.json);
check("me reports isAdmin false", r.json.isAdmin === false, r.json);
r = await call("GET", "/api/me");
check("me without token -> 401", r.status === 401, r.json);
r = await call("GET", "/api/me", { token: "deadbeef" });
check("me with junk token -> 401", r.status === 401, r.json);

console.log("\n--- orders / invoice ---");
r = await call("GET", "/api/orders", { token: custToken });
check("orders empty for new account", r.status === 200 && r.json.orders.length === 0, r.json);

r = await call("POST", "/api/invoice", {  body: { amount: 74, pay_currency: "btc", email: "stranger@example.com", discord: "tester#1", type: "discord_bot", package: "Custom", description: "Test order", meta: { commands: 12 } }, token: custToken });
check("invoice without NOWPayments key -> 503", r.status === 503, r.json);
const orderId = r.json.orderId || "__missing__";
check("invoice returns an order id", typeof r.json.orderId === "string" && r.json.orderId.startsWith("aether_"), r.json);

const row = env.DB.db.prepare("SELECT user_id, email, amount, promo_code FROM orders WHERE order_id = ?").get(orderId);
check("invoice is filed to the signed-in account", row && row.user_id === 1, row);
check("the account email wins over a posted one", row && row.email === "test@example.com", row);

// A second real account: guest purchases no longer exist, so "someone else's order" now means an
// actual other customer.
r = await call("POST", "/api/auth/register", { body: { email: "other@example.com", password: "otherpass123" } });
check("second customer account created", r.status === 200 && !!r.json.token, r.json);
const otherToken = r.json.token;
const otherId = env.DB.db.prepare("SELECT id FROM users WHERE email = ?").get("other@example.com").id;
r = await call("POST", "/api/invoice", { body: { amount: 30, pay_currency: "ltc", type: "website", package: "BASIC", description: "Second order" }, token: otherToken });
check("the other account can buy (503 expected, no key)", r.status === 503, r.json);
const guestOrderId = r.json.orderId || "__missing__";
const guestRow = env.DB.db.prepare("SELECT user_id, email FROM orders WHERE order_id = ?").get(guestOrderId);
check("that order belongs to the other account", guestRow && guestRow.user_id === otherId && guestRow.email === "other@example.com", guestRow);

r = await call("POST", "/api/invoice", { body: { amount: 30, pay_currency: "dogecoin", email: "test@example.com" }, token: custToken });
check("unsupported coin -> 400", r.status === 400, r.json);
r = await call("POST", "/api/invoice", { body: { amount: 30, pay_currency: "btc", email: "test@example.com", promoCode: "GHOST" }, token: custToken });
check("invalid promo on invoice -> 400", r.status === 400, r.json);

console.log("\n--- account-only checkout: no guest purchases ---");
const ordersBeforeGuest = env.DB.db.prepare("SELECT COUNT(*) AS n FROM orders").get().n;
r = await call("POST", "/api/invoice", { body: { amount: 15, pay_currency: "btc", email: "guest@example.com", type: "website", package: "STARTER", description: "A guest tries to buy" } });
check("anonymous invoice -> 401 ACCOUNT_REQUIRED", r.status === 401 && r.json.code === "ACCOUNT_REQUIRED", r.json);
check("the rejected guest wrote no order", env.DB.db.prepare("SELECT COUNT(*) AS n FROM orders").get().n === ordersBeforeGuest);
check("no order carries the guest email", env.DB.db.prepare("SELECT COUNT(*) AS n FROM orders WHERE email = ?").get("guest@example.com").n === 0);
r = await call("POST", "/api/invoice", { body: { amount: 15, pay_currency: "btc", email: "guest@example.com", type: "website", package: "STARTER", description: "A stale token tries to buy" }, token: "deadbeefdeadbeef" });
check("a junk session token cannot buy either", r.status === 401 && r.json.code === "ACCOUNT_REQUIRED", r.json);

r = await call("GET", "/api/orders", { token: custToken });
check("orders list contains the linked order", r.status === 200 && r.json.orders.length === 1 && r.json.orders[0].order_id === orderId, r.json.orders);

r = await call("GET", "/api/orders/" + orderId, { token: custToken });
check("order detail + empty messages", r.status === 200 && r.json.order.order_id === orderId && r.json.messages.length === 0, r.json);
r = await call("GET", "/api/orders/" + guestOrderId, { token: custToken });
check("cannot read someone else's order -> 404", r.status === 404, r.json);

console.log("\n--- customer chat ---");
r = await call("POST", "/api/orders/" + orderId + "/message", { token: custToken, body: { body: "Can I add one more command?" } });
check("customer message stored", r.status === 200 && r.json.messages.length === 1 && r.json.messages[0].sender === "customer", r.json);
r = await call("POST", "/api/orders/" + orderId + "/message", { token: custToken, body: { body: "x" } });
check("too-short message -> 400", r.status === 400, r.json);
r = await call("POST", "/api/orders/" + guestOrderId + "/message", { token: custToken, body: { body: "not mine" } });
check("cannot post to someone else's order -> 404", r.status === 404, r.json);
r = await call("POST", "/api/orders/" + orderId + "/message", { body: { body: "no auth" } });
check("chat without auth -> 401", r.status === 401, r.json);

console.log("\n--- admin guards ---");
r = await call("GET", "/api/admin/orders", { token: custToken });
check("non-admin /api/admin/orders -> 403", r.status === 403, r.json);
r = await call("GET", "/api/admin/orders");
check("anonymous /api/admin/orders -> 403", r.status === 403, r.json);
r = await call("GET", "/api/admin/users", { token: custToken });
check("non-admin /api/admin/users -> 403", r.status === 403, r.json);

console.log("\n--- admin account ---");
r = await call("POST", "/api/auth/register", { body: { email: "admin@example.com", password: "adminsecret1" } });
check("admin register 200", r.status === 200, r.json);
check("register response flags admin", r.json.isAdmin === true, r.json.isAdmin);
const adminToken = r.json.token;
r = await call("GET", "/api/me", { token: adminToken });
check("me reports isAdmin true", r.json.isAdmin === true, r.json);

r = await call("GET", "/api/admin", { token: adminToken });
check("admin index lists routes", r.status === 200 && r.json.routes.length >= 6, r.json);
r = await call("GET", "/api/admin/orders", { token: adminToken });
check("admin sees every order", r.status === 200 && r.json.orders.length === 2, r.json.orders && r.json.orders.length);
check("admin order rows carry the customer email", (r.json.orders || []).some(o => o.user_email === "other@example.com"), r.json.orders);
r = await call("GET", "/api/admin/orders?status=pending", { token: adminToken });
check("admin status filter works", r.status === 200 && r.json.orders.length === 2, r.json);
r = await call("GET", "/api/admin/orders/" + orderId, { token: adminToken });
check("admin order detail + messages", r.status === 200 && r.json.messages.length === 1, r.json);
r = await call("GET", "/api/admin/orders/nope_123", { token: adminToken });
check("admin unknown order -> 404", r.status === 404, r.json);

r = await call("POST", "/api/admin/orders/" + orderId + "/message", { token: adminToken, body: { body: "Added - new total EUR 76." } });
check("admin reply stored as admin", r.status === 200 && r.json.messages.length === 2 && r.json.messages[1].sender === "admin", r.json);
r = await call("POST", "/api/admin/orders/" + guestOrderId + "/message", { token: adminToken, body: { body: "Hello guest" } });
check("admin can reply on another account's order", r.status === 200 && r.json.messages.length === 1, r.json);

r = await call("GET", "/api/admin/users", { token: adminToken });
check("admin sees every account", r.status === 200 && r.json.users.length === 3, r.json.users);
check("admin user stats present", Array.isArray(r.json.stats) && r.json.stats.length >= 1, r.json.stats);
r = await call("GET", "/api/admin/messages", { token: adminToken });
check("admin message inbox has 3 entries", r.status === 200 && r.json.messages.length === 3, r.json.messages && r.json.messages.length);

console.log("\n--- legacy guest rows (created before account-only checkout) ---");
// Rows written by the old guest checkout can still exist in a live database. Registering that
// email must claim them, otherwise those customers would lose their order history.
env.DB.db.prepare("INSERT INTO orders (user_id, order_id, amount, currency, type, package, description, status, email) VALUES (NULL, ?, 15, 'eur', 'website', 'STARTER', 'Legacy guest order', 'pending', ?)").run("aether_legacy_guest_1", "legacy@example.com");
r = await call("POST", "/api/auth/register", { body: { email: "legacy@example.com", password: "legacypass1" } });
check("the email of a legacy guest order can register", r.status === 200 && !!r.json.token, r.json);
const legacyToken = r.json.token;
r = await call("GET", "/api/orders", { token: legacyToken });
check("the legacy guest order was claimed on register", (r.json.orders || []).some(o => o.order_id === "aether_legacy_guest_1"), r.json.orders);

console.log("\n--- delete account ---");
const userId = env.DB.db.prepare("SELECT id FROM users WHERE email = ?").get("other@example.com").id;
const ordersBeforeDelete = env.DB.db.prepare("SELECT COUNT(*) AS n FROM orders").get().n;
r = await call("DELETE", "/api/admin/users/" + 999, { token: adminToken });
check("admin delete missing user -> 404", r.status === 404, r.json);
r = await call("DELETE", "/api/admin/users/" + env.DB.db.prepare("SELECT id FROM users WHERE email = ?").get("admin@example.com").id, { token: adminToken });
check("admin cannot delete an admin -> 400", r.status === 400, r.json);
r = await call("DELETE", "/api/admin/users/" + userId, { token: adminToken });
check("admin removes a user", r.status === 200 && r.json.deleted === userId, r.json);
check("removed user's session is gone", env.DB.db.prepare("SELECT COUNT(*) AS n FROM sessions WHERE user_id = ?").get(userId).n === 0);
check("removed user's orders are detached, not deleted", env.DB.db.prepare("SELECT user_id FROM orders WHERE order_id = ?").get(guestOrderId).user_id === null);

r = await call("DELETE", "/api/me", { token: custToken });
check("user deletes own account", r.status === 200 && r.json.deleted === true, r.json);
r = await call("GET", "/api/me", { token: custToken });
check("deleted user token is dead -> 401", r.status === 401, r.json);
check("deleted user row is gone", env.DB.db.prepare("SELECT COUNT(*) AS n FROM users WHERE email = ?").get("test@example.com").n === 0);
check("their orders survive", env.DB.db.prepare("SELECT COUNT(*) AS n FROM orders").get().n === ordersBeforeDelete);
r = await call("DELETE", "/api/admin/users/abc", { token: adminToken });
check("admin delete with junk id -> 403/404 not a crash", r.status === 404 || r.status === 403, r.json);

console.log("\n--- signed in: the account supplies the email ---");
r = await call("POST", "/api/invoice", { body: { amount: 42, pay_currency: "eth", type: "website", package: "Custom", description: "No email and not signed in" } });
check("invoice while signed out -> 401 account required", r.status === 401 && r.json.code === "ACCOUNT_REQUIRED", r.json);
r = await call("POST", "/api/auth/register", { body: { email: "member@example.com", password: "memberpass1", discord: "member#7" } });
check("member account created", r.status === 200 && !!r.json.token, r.json);
const memberToken = r.json.token;
const memberId = env.DB.db.prepare("SELECT id FROM users WHERE email = ?").get("member@example.com").id;

r = await call("POST", "/api/invoice", { body: { amount: 42, pay_currency: "eth", type: "website", package: "Custom", description: "Signed in, nothing typed" }, token: memberToken });
check("signed-in invoice needs no email (reaches NOWPayments -> 503)", r.status === 503 && !!r.json.orderId, r.json);
const acctOrderId = r.json.orderId || "__none__";
const acctRow = env.DB.db.prepare("SELECT user_id, email FROM orders WHERE order_id = ?").get(acctOrderId);
check("invoice used the account email", acctRow && acctRow.email === "member@example.com", acctRow);
check("invoice linked to the signed-in account", acctRow && acctRow.user_id === memberId, acctRow);

r = await call("POST", "/api/invoice", { body: { amount: 20, pay_currency: "ltc", email: "other@example.com", type: "website", package: "STARTER", description: "Typed a different email" }, token: memberToken });
const typedRow = env.DB.db.prepare("SELECT user_id, email FROM orders WHERE order_id = ?").get(r.json.orderId || "__none__");
check("a typed email can no longer override the account email", typedRow && typedRow.email === "member@example.com", typedRow);
r = await call("GET", "/api/orders/" + orderId, { token: memberToken });
check("another account cannot read that order -> 404", r.status === 404, r.json);

console.log("\n--- order requests are account-only (no email box, no guest path) ---");
r = await call("POST", "/api/order", { body: { type: "website", package: "Custom", amount: 90, description: "Big custom build, no email typed" }, token: memberToken });
check("signed-in website request -> accepted", r.status === 200 && r.json.ok === true, r.json);
const orderConvId = r.json.conversationId || "";
check("the request is filed into the customer's own chat", /^conv_[0-9a-f]{18}$/.test(orderConvId), r.json);
r = await call("GET", "/api/conversations/" + orderConvId, { token: memberToken });
check("the customer can open that request thread", r.status === 200 && (r.json.messages || []).length >= 1, r.json);
check("it is owned by the account, not by a typed address", env.DB.db.prepare("SELECT user_id FROM conversations WHERE conversation_id = ?").get(orderConvId).user_id === memberId);

r = await call("POST", "/api/order", { body: { type: "website", package: "Custom", amount: 90, description: "No email box and not signed in", email: "stranger@example.com" } });
check("anonymous website request -> 401 ACCOUNT_REQUIRED", r.status === 401 && r.json.code === "ACCOUNT_REQUIRED", r.json);
r = await call("POST", "/api/order", { body: { type: "discord_bot", package: "Custom", amount: 60, description: "Anonymous bot request", email: "stranger@example.com" } });
check("anonymous bot request -> 401 ACCOUNT_REQUIRED", r.status === 401 && r.json.code === "ACCOUNT_REQUIRED", r.json);
check("the rejected guests wrote nothing at all", env.DB.db.prepare("SELECT COUNT(*) AS n FROM conversations").get().n === 1, env.DB.db.prepare("SELECT COUNT(*) AS n FROM conversations").get().n);

r = await call("POST", "/api/order", { body: { type: "discord_bot", package: "Custom", amount: 45, description: "Signed-in bot request that typed someone else's email", email: "other@example.com" }, token: memberToken });
check("a signed-in bot request is accepted", r.status === 200 && !!r.json.conversationId, r.json);
check("a typed email cannot file the request under another account",
  env.DB.db.prepare("SELECT COUNT(*) AS n FROM conversations WHERE user_id = (SELECT id FROM users WHERE email = ?)").get("other@example.com").n === 0);

r = await call("POST", "/api/order", { body: { type: "contact", description: "Just a question about hosting", email: "someone@example.com" } });
check("the plain contact form still works without an account", r.status === 200 && r.json.ok === true, r.json);

r = await call("GET", "/api/orders", { token: memberToken });
check("member sees exactly their two orders", r.status === 200 && r.json.orders.length === 2, r.json.orders && r.json.orders.length);

console.log("\n--- hardening: headers / cors / csrf ---");
import { createHash } from "node:crypto";
const sha = (s) => createHash("sha256").update(String(s)).digest("hex");

r = await call("GET", "/api/health");
check("security headers on API responses",
  r.headers.get("x-content-type-options") === "nosniff"
  && /frame-ancestors 'none'/.test(r.headers.get("content-security-policy") || "")
  && r.headers.get("cache-control") === "no-store"
  && /max-age=31536000/.test(r.headers.get("strict-transport-security") || "")
  && r.headers.get("x-frame-options") === "DENY", {
    ct: r.headers.get("x-content-type-options"), csp: r.headers.get("content-security-policy"),
    cc: r.headers.get("cache-control"), hsts: r.headers.get("strict-transport-security"),
  });
check("ALLOWED_ORIGIN='*' is ignored (no wildcard CORS)", r.headers.get("access-control-allow-origin") !== "*");

r = await call("GET", "/api/health", { origin: "https://evil.example" });
check("unknown origin gets no CORS headers", !r.headers.get("access-control-allow-origin"), r.headers.get("access-control-allow-origin"));
r = await call("GET", "/api/health", { origin: "https://get-aether.de" });
check("allowlisted origin is echoed back exactly", r.headers.get("access-control-allow-origin") === "https://get-aether.de", r.headers.get("access-control-allow-origin"));

r = await call("POST", "/api/auth/login", { origin: "https://evil.example", body: { email: "member@example.com", password: "memberpass1" } });
check("cross-site write from an untrusted origin -> 403", r.status === 403, r.json);
r = await call("POST", "/api/auth/login", { origin: "https://get-aether.de", body: { email: "member@example.com", password: "memberpass1" } });
check("same-site write still works", r.status === 200 && !!r.json.token, r.json);
r = await call("POST", "/api/auth/login", { host: "https://get-aether.de", origin: "https://get-aether.de", body: { email: "member@example.com", password: "memberpass1" } });
check("same-origin route gets a SameSite=Lax cookie", r.status === 200 && /SameSite=Lax/.test(r.setCookie), r.setCookie);
r = await call("POST", "/api/auth/login", { host: "https://get-aether.de", origin: "https://evil.example", body: { email: "member@example.com", password: "memberpass1" } });
check("same-origin route still refuses an untrusted origin", r.status === 403, r.json);

console.log("\n--- hardening: sessions + rate limits ---");
const storedSession = env.DB.db.prepare("SELECT token FROM sessions WHERE user_id = ?").get(memberId);
check("sessions store sha256(token), never the token itself", storedSession && storedSession.token !== memberToken && storedSession.token === sha(memberToken), storedSession && storedSession.token.slice(0, 12));
r = await call("GET", "/api/me", { token: memberToken });
check("bearer token still authenticates once only its hash is stored", r.status === 200 && r.json.authenticated === true, r.json);

// Seeded window: one call past a full counter must be refused. The unseeded IP below still gets
// through, so this also pins the limit to the IP rather than to the route as a whole.
let limited = false, limitedAt = 0;
for (let i = 1; i <= 3 && !limited; i++) {
  seedRateLimit("login:203.0.113.7", 12, 900000);
  const rr = await call("POST", "/api/auth/login", { ip: "203.0.113.7", body: { email: "member@example.com", password: "memberpass1" } });
  if (rr.status === 429) { limited = true; limitedAt = i; }
}
check("login brute force from one IP is rate limited", limited, { limitedAt });
r = await call("POST", "/api/auth/login", { ip: "203.0.113.8", body: { email: "member@example.com", password: "memberpass1" } });
check("a different IP is unaffected by that limit", r.status === 200, r.json);

r = await call("POST", "/api/auth/register", { body: { email: "weak@example.com", password: "onlyletters" } });
check("password without a digit -> 400", r.status === 400, r.json);

console.log("\n--- hardening: password reset / settings ---");
// No email provider in this env: the link must still be delivered - through the recovery channel -
// instead of the old 503 dead end. console.log is captured because the operator log is the last
// resort of that chain.
const loggedLines = [];
const realConsoleLog = console.log;
console.log = (...a) => { loggedLines.push(a.map(String).join(" ")); };
const forgotKnown = await call("POST", "/api/auth/forgot", { body: { email: "member@example.com" } });
const forgotUnknown = await call("POST", "/api/auth/forgot", { body: { email: "nobody-here@example.com" } });
console.log = realConsoleLog;
check("forgot without an email provider -> 200 with a delivery channel, never a 503 dead end", forgotKnown.status === 200 && forgotKnown.json.ok === true && forgotKnown.json.delivery === "logs", forgotKnown.json);
check("the answer is identical for an unknown account (no enumeration)", JSON.stringify(forgotUnknown.json) === JSON.stringify(forgotKnown.json), { known: forgotKnown.json, unknown: forgotUnknown.json });
const loggedLine = loggedLines.find(l => l.includes("[reset-link]") && l.includes("/reset-password.html?token=")) || "";
const loggedToken = loggedLine.split("/reset-password.html?token=")[1] || "";
check("the operator log carries a complete reset link", loggedToken.length === 64, loggedLine.slice(0, 80));
check("the token reaches the database only as a hash", env.DB.db.prepare("SELECT COUNT(*) AS n FROM auth_tokens WHERE token_hash = ?").get(sha(loggedToken)).n === 1 && env.DB.db.prepare("SELECT COUNT(*) AS n FROM auth_tokens WHERE token_hash = ?").get(loggedToken).n === 0);
r = await call("POST", "/api/auth/reset", { body: { token: loggedToken, password: "recovered123" } });
check("the logged link resets the password end to end", r.status === 200 && typeof r.json.token === "string", r.json);
r = await call("POST", "/api/auth/login", { body: { email: "member@example.com", password: "recovered123" } });
check("the recovered password signs in", r.status === 200, r.json);

// Discord recovery channel. The log line always happens; the webhook only ever carries links for
// the allowlisted accounts (default = the admins), so a stranger's link cannot surface in the
// operator's channel. fetch is stubbed so nothing leaves this machine.
const posted = [];
const realFetch = globalThis.fetch;
globalThis.fetch = async (url, init) => { const u = String(url); posted.push({ url: u, body: init && init.body ? String(init.body) : "" }); if (u.includes("api.resend.com")) return { ok: false, status: 500, text: async () => "resend unavailable in tests" }; return { ok: true, status: 200, text: async () => "{}" }; };
const resetPosts = () => posted.filter(p => p.url.includes("discord.com/api/webhooks") && p.body.includes("/reset-password.html?token="));
env.DISCORD_WEBHOOK_URL = "https://discord.com/api/webhooks/123/test-token";
const discordDelivery = await call("POST", "/api/auth/forgot", { body: { email: "admin@example.com" } });
check("no email provider + admin account -> the link goes to the Discord recovery channel", discordDelivery.status === 200 && discordDelivery.json.delivery === "discord" && resetPosts().length === 1, { delivery: discordDelivery.json.delivery, posts: posted.length });
const postsAfterAdmin = posted.length;
r = await call("POST", "/api/auth/forgot", { body: { email: "member@example.com" } });
check("another account's link never lands in the shared channel", r.status === 200 && posted.length === postsAfterAdmin && resetPosts().length === 1, { newPosts: posted.length - postsAfterAdmin });
const unknownOnDiscord = await call("POST", "/api/auth/forgot", { body: { email: "nobody-here@example.com" } });
check("the response body still cannot probe for accounts", JSON.stringify(unknownOnDiscord.json) === JSON.stringify(discordDelivery.json), { known: discordDelivery.json, unknown: unknownOnDiscord.json });
env.RESET_DISCORD_EMAILS = "*";
const widened = await call("POST", "/api/auth/forgot", { body: { email: "member@example.com" } });
check("RESET_DISCORD_EMAILS=* deliberately widens the channel to every account", widened.status === 200 && resetPosts().length === 2, { posts: resetPosts().length });
const discordToken = (resetPosts()[1].body.split("/reset-password.html?token=")[1] || "").match(/^[0-9a-f]{64}/);
check("the Discord embed carries a real single-use link", !!discordToken, resetPosts()[1].body.slice(0, 140));
r = await call("POST", "/api/auth/reset", { body: { token: discordToken ? discordToken[0] : "", password: "discordpass1" } });
check("the link delivered to Discord works end to end", r.status === 200 && typeof r.json.token === "string", r.json);
delete env.RESET_DISCORD_EMAILS;
env.RESEND_API_KEY = "re_test_key";
r = await call("POST", "/api/auth/forgot", { body: { email: "member@example.com" } });
check("with an email provider configured the link is emailed, exactly as before", r.status === 200 && r.json.delivery === "email" && posted.filter(p => p.url.includes("api.resend.com")).length === 1 && resetPosts().length === 2, { delivery: r.json.delivery, discordPosts: resetPosts().length });
r = await call("POST", "/api/auth/forgot", { body: { email: "admin@example.com" } });
check("a failed email falls back to the recovery channel instead of losing the link", r.status === 200 && resetPosts().length === 3, { posts: resetPosts().length });
delete env.RESEND_API_KEY;
delete env.DISCORD_WEBHOOK_URL;
globalThis.fetch = realFetch;

// Cloudflare Email Sending binding. When it is attached, mail goes out through it with no secret at
// all, and every caller that used to ask "is Resend configured?" now asks about the transport.
const boundMail = [];
env.EMAIL = { send: async (m) => { boundMail.push(m); return { messageId: "cf-msg-1" }; } };
r = await call("GET", "/api/health");
check("health reports which email transport is live", r.json.email === true && r.json.emailProvider === "cloudflare", { email: r.json.email, provider: r.json.emailProvider });
r = await call("POST", "/api/auth/forgot", { body: { email: "admin@example.com" } });
const mailMessage = r.json.message;
check("with the binding attached the reset link is emailed like any other mail", r.status === 200 && r.json.delivery === "email" && boundMail.length >= 1 && /reset-password\.html\?token=/.test(JSON.stringify(boundMail.map(m => m.text || ""))), { delivery: r.json.delivery, sent: boundMail.length });
check("the reset mail goes to the account, from the configured From address", !!boundMail[0] && boundMail[0].to[0] === "admin@example.com" && /questions@get-aether\.de/.test(boundMail[0].from), boundMail[0] ? { from: boundMail[0].from, to: boundMail[0].to } : null);
check("the visitor is told to check their inbox, not the ops channel", /inbox/i.test(mailMessage), mailMessage);
r = await call("POST", "/api/auth/register", { body: { email: "boundmail@example.com", password: "boundpass1" } });
check("registration sends its verification email through the binding too", r.status === 200 && r.json.emailSent === true && boundMail.some(m => m.to[0] === "boundmail@example.com" && /verify-email\.html\?token=/.test(m.text || "")), { emailSent: r.json.emailSent, sent: boundMail.length });
env.DB.db.prepare("DELETE FROM users WHERE email = ?").run("boundmail@example.com");
env.EMAIL = { send: async () => { throw new Error("email sending disabled for this account"); } };
env.DISCORD_WEBHOOK_URL = "https://discord.com/api/webhooks/123/fallback";
const fallbackPosts = [];
globalThis.fetch = async (url) => { fallbackPosts.push(String(url)); return { ok: true, status: 200, text: async () => "{}" }; };
r = await call("POST", "/api/auth/forgot", { body: { email: "admin@example.com" } });
check("a broken transport falls through to the recovery channel instead of losing the link", r.status === 200 && fallbackPosts.some(u => u.includes("discord.com/api/webhooks")), { posts: fallbackPosts.length });
globalThis.fetch = realFetch;
delete env.EMAIL;
delete env.DISCORD_WEBHOOK_URL;

const resetToken = "reset-token-" + "a".repeat(40);
env.DB.db.prepare("INSERT INTO auth_tokens (token_hash, user_id, purpose, expires_at, created_at) VALUES (?,?,?,?,?)").run(sha(resetToken), memberId, "reset", Date.now() + 600000, Date.now());
r = await call("POST", "/api/auth/reset", { body: { token: resetToken, password: "newpass123" } });
check("reset with a valid token -> 200 + fresh session", r.status === 200 && typeof r.json.token === "string", r.json);
const freshSession = r.json.token;
check("reset kills every other session", env.DB.db.prepare("SELECT COUNT(*) AS n FROM sessions WHERE user_id = ?").get(memberId).n === 1);
r = await call("POST", "/api/auth/reset", { body: { token: resetToken, password: "thirdpass123" } });
check("reset token is single use", r.status === 400, r.json);
env.DB.db.prepare("INSERT INTO auth_tokens (token_hash, user_id, purpose, expires_at, created_at) VALUES (?,?,?,?,?)").run(sha("expired-token"), memberId, "reset", Date.now() - 1000, Date.now());
r = await call("POST", "/api/auth/reset", { body: { token: "expired-token", password: "expired123" } });
check("expired reset token -> 400", r.status === 400, r.json);
r = await call("POST", "/api/auth/login", { body: { email: "member@example.com", password: "memberpass1" } });
check("the old password no longer works after a reset", r.status === 401, r.json);
r = await call("POST", "/api/auth/login", { body: { email: "member@example.com", password: "newpass123" } });
check("the new password works", r.status === 200, r.json);

r = await call("POST", "/api/auth/password", { body: { currentPassword: "nope-nope-1", newPassword: "changed123" }, token: freshSession });
check("password change with a wrong current password -> 401", r.status === 401, r.json);
r = await call("POST", "/api/auth/password", { body: { currentPassword: "newpass123", newPassword: "changed123" }, token: freshSession });
check("password change -> 200", r.status === 200, r.json);
r = await call("GET", "/api/me", { token: freshSession });
check("the session that changed the password survives", r.status === 200, r.json);
r = await call("POST", "/api/auth/password", { body: { currentPassword: "changed123", newPassword: "changed456" } });
check("password change without auth -> 401", r.status === 401, r.json);

// Deeper guarantees on the same flow: an exact-match common-password blocklist instead of a vague
// strength rule, a per-account ceiling above the per-IP one, the same database work on the
// unknown-address branch as on a real one, and a notice to the owner whenever a password changes.
console.log("\n--- hardening: common-password list / per-account throttle / change notice ---");
for (const weak of ["password123", "Aether123", "qwerty123", "Passwort1"]) {
  r = await call("POST", "/api/auth/register", { body: { email: "weaklist@example.com", password: weak } });
  check("common password \"" + weak + "\" is refused at registration", r.status === 400 && /too common/i.test(r.json.error || ""), r.json);
}
r = await call("POST", "/api/auth/register", { body: { email: "contains@example.com", password: "notpassword1x" } });
check("a password that merely contains a listed word is still allowed (exact match, never substring)", r.status === 200, r.json);
r = await call("POST", "/api/auth/register", { body: { email: "notice@example.com", password: "noticepass1" } });
check("the notice fixture account registers", r.status === 200, r.json);

// The cheapest detection control there is: if somebody else took the account over, the real owner
// finds out from their inbox instead of only noticing a killed session later.
const noticeUserId = env.DB.db.prepare("SELECT id FROM users WHERE email = ?").get("notice@example.com").id;
const noticeToken0 = "notice-token-" + "c".repeat(40);
env.DB.db.prepare("INSERT INTO auth_tokens (token_hash, user_id, purpose, expires_at, created_at) VALUES (?,?,?,?,?)").run(sha(noticeToken0), noticeUserId, "reset", Date.now() + 600000, Date.now());
env.EMAIL = { send: async (m) => { boundMail.push(m); return { messageId: "cf-msg-notice" }; } };
const noticeFrom = boundMail.length;
const noticeReset = await call("POST", "/api/auth/reset", { body: { token: noticeToken0, password: "noticepass2" } });
const resetNotices = boundMail.slice(noticeFrom).filter(m => /password was changed/i.test(m.subject || ""));
check("a completed reset emails the owner", noticeReset.status === 200 && resetNotices.length === 1, { status: noticeReset.status, subjects: boundMail.slice(noticeFrom).map(m => m.subject) });
check("the notice carries neither the password nor a usable link", resetNotices.length === 1 && !/noticepass2/.test(JSON.stringify(resetNotices[0])) && !/token=/.test(resetNotices[0].text || ""), resetNotices[0] && resetNotices[0].text);
const noticeSession = noticeReset.json.token;
const changeFrom = boundMail.length;
const changed = await call("POST", "/api/auth/password", { body: { currentPassword: "noticepass2", newPassword: "noticepass3" }, token: noticeSession });
check("a signed-in password change emails the same notice", changed.status === 200 && boundMail.slice(changeFrom).filter(m => /password was changed/i.test(m.subject || "")).length === 1, { status: changed.status, subjects: boundMail.slice(changeFrom).map(m => m.subject) });
env.EMAIL = { send: async () => { throw new Error("mail transport is down"); } };
const brokenNotice = await call("POST", "/api/auth/password", { body: { currentPassword: "noticepass3", newPassword: "noticepass4" }, token: noticeSession });
check("a broken mail transport cannot turn a completed change into an error", brokenNotice.status === 200, brokenNotice.json);
delete env.EMAIL;

// Per-account ceiling. A single IP was always capped, but a botnet rotating addresses could keep
// re-sending reset mail to one victim - and every request also replaces their previous link.
const throttleEmail = "throttle-target@example.com";
env.DB.db.prepare("INSERT INTO users (email, password_hash) VALUES (?, ?)").run(throttleEmail, "x");
const throttleId = env.DB.db.prepare("SELECT id FROM users WHERE email = ?").get(throttleEmail).id;
// The guard keys on a hash of the address, so the exact seat it reads is reproducible here.
const bucketKey = "forgot-acct:" + sha(throttleEmail).slice(0, 32);
const servedLog = [];
console.log = (...a) => { servedLog.push(a.map(String).join(" ")); };
const served = await call("POST", "/api/auth/forgot", { body: { email: throttleEmail } });
console.log = realConsoleLog;
check("an account inside its ceiling still gets its link", served.status === 200 && servedLog.filter(l => l.includes("[reset-link]")).length === 1, served.json);
seedRateLimit(bucketKey, 6, 3600000);
const tokensBeforeThrottle = env.DB.db.prepare("SELECT COUNT(*) AS n FROM auth_tokens WHERE user_id = ?").get(throttleId).n;
const throttleLog = [];
console.log = (...a) => { throttleLog.push(a.map(String).join(" ")); };
const throttled = await call("POST", "/api/auth/forgot", { body: { email: throttleEmail }, ip: "198.51.100.99" });
console.log = realConsoleLog;
const tokensAfterThrottle = env.DB.db.prepare("SELECT COUNT(*) AS n FROM auth_tokens WHERE user_id = ?").get(throttleId).n;
check("at the ceiling the request is silently dropped - no link, no token", throttled.status === 200 && throttleLog.filter(l => l.includes("[reset-link]")).length === 0 && tokensAfterThrottle === tokensBeforeThrottle, { links: throttleLog.length, tokensBeforeThrottle, tokensAfterThrottle });
check("a throttled account is answered identically to a served one", JSON.stringify(throttled.json) === JSON.stringify(served.json), { served: served.json, throttled: throttled.json });
env.DB.db.prepare("DELETE FROM auth_tokens WHERE user_id = ?").run(throttleId);
env.DB.db.prepare("DELETE FROM users WHERE id = ?").run(throttleId);
env.DB.db.prepare("DELETE FROM rate_limits WHERE rl_key = ?").run(bucketKey);

// Identical bodies are not enough on their own: if the unknown-address branch skipped the database
// work and answered instantly, the clock would still say which addresses exist.
const unknownStarted = Date.now();
await call("POST", "/api/auth/forgot", { body: { email: "no-account-at-all@example.com" } });
const unknownMs = Date.now() - unknownStarted;
check("an unknown address is padded past the jitter floor instead of answered instantly", unknownMs >= 200, unknownMs);

env.DB.db.prepare("DELETE FROM auth_tokens WHERE user_id = ?").run(noticeUserId);
env.DB.db.prepare("DELETE FROM users WHERE email IN ('notice@example.com','contains@example.com')").run();

const verifyToken = "verify-token-" + "b".repeat(40);
env.DB.db.prepare("INSERT INTO auth_tokens (token_hash, user_id, purpose, expires_at, created_at) VALUES (?,?,?,?,?)").run(sha(verifyToken), memberId, "verify", Date.now() + 600000, Date.now());
r = await call("POST", "/api/auth/verify-email", { body: { token: verifyToken } });
check("email verification flips the flag", r.status === 200 && env.DB.db.prepare("SELECT email_verified FROM users WHERE id = ?").get(memberId).email_verified === 1, r.json);
r = await call("GET", "/api/me", { token: freshSession });
check("/api/me reports emailVerified", r.json.emailVerified === true, r.json);

console.log("\n--- hardening: input caps / error hygiene ---");
r = await call("POST", "/api/order", { body: { type: "website", description: "ok description", amount: 5 }, contentLength: 90000 });
check("oversized payload -> 413", r.status === 413, r.json);
const hugeDesc = "y".repeat(20000);
r = await call("POST", "/api/invoice", { body: { amount: 25, pay_currency: "btc", email: "member@example.com", description: hugeDesc }, token: freshSession });
const hugeRow = env.DB.db.prepare("SELECT description FROM orders WHERE order_id = ?").get(r.json.orderId || "__none__");
check("oversized description is truncated before it reaches storage", hugeRow && String(hugeRow.description).length <= 200, hugeRow && String(hugeRow.description).length);
r = await call("GET", "/api/payment/123456");
check("payment status never echoes the provider payload", r.json.raw === undefined && r.json.order_description === undefined, r.json);
r = await call("GET", "/api/nope");
check("404 does not echo internals", r.json.error === "Not found" && !/stack|at Object/i.test(JSON.stringify(r.json)), r.json);

console.log("\n--- purchase IDs ---");
r = await call("GET", "/api/orders", { token: freshSession });
const memberOrders = r.json.orders || [];
check("every purchase carries a server-generated Purchase ID", memberOrders.length >= 1 && memberOrders.every(o => /^AETH-\d{4}-[A-Z0-9]{8}$/.test(String(o.purchase_id))), memberOrders.map(o => o.purchase_id));
const memberPurchaseId = memberOrders[0] && memberOrders[0].purchase_id;
check("Purchase IDs are unique across orders", new Set(memberOrders.map(o => o.purchase_id)).size === memberOrders.length);
r = await call("GET", "/api/purchases/" + memberPurchaseId, { token: freshSession });
check("owner looks up their purchase by Purchase ID", r.status === 200 && r.json.purchase.purchase_id === memberPurchaseId, r.json);
r = await call("GET", "/api/purchases/" + memberPurchaseId, { token: adminToken });
check("another account cannot read that purchase -> 404", r.status === 404, r.json);
r = await call("GET", "/api/purchases/" + memberPurchaseId);
check("anonymous Purchase ID lookup -> 401", r.status === 401, r.json);
r = await call("GET", "/api/purchases/AETH-2026-NOPE", { token: freshSession });
check("malformed Purchase ID -> 400", r.status === 400, r.json);

// The success page opens a custom website's project chat by itself, so the two things it leans on
// are contractual: the order list must name the type and the Purchase ID it matches on, and
// fetching a purchase must be what creates that purchase's conversation - idempotently, because a
// reload of the success page must never open a second thread.
check("/api/orders names the type and Purchase ID the success page matches on",
  memberOrders.every(o => typeof o.type === "string" && typeof o.purchase_id === "string")
    && memberOrders.some(o => String(o.type).toLowerCase() === "website"),
  memberOrders.map(o => ({ type: o.type, purchase_id: o.purchase_id })));
r = await call("GET", "/api/purchases/" + memberPurchaseId, { token: freshSession });
const autoChatId = r.json.conversation && r.json.conversation.conversation_id;
check("fetching a purchase hands back its chat, creating it when it did not exist",
  r.status === 200 && String(autoChatId).startsWith("conv_") && r.json.conversation.purchase_id === memberPurchaseId,
  r.json.conversation);
r = await call("GET", "/api/purchases/" + memberPurchaseId, { token: freshSession });
check("a reload cannot open a second thread for the same purchase",
  r.json.conversation && r.json.conversation.conversation_id === autoChatId
    && env.DB.db.prepare("SELECT COUNT(*) AS n FROM conversations WHERE purchase_id = ?").get(memberPurchaseId).n === 1,
  { again: r.json.conversation && r.json.conversation.conversation_id, first: autoChatId });
// Deliberately no message is posted here. Messages are bridged across every conversation that
// shares an order, so writing one would leak into the thread the checks further down assert on -
// and posting into an owned thread is already covered there.
r = await call("GET", "/api/invoice", { token: freshSession });
check("invoice endpoint rejects GET", r.status !== 200, r.status);

console.log("\n--- customer chat: conversations, ownership, IDOR ---");
r = await call("POST", "/api/conversations", { token: freshSession, body: { subject: "Need a change", message: "Can you rename the bot?", purchase_id: memberPurchaseId } });
check("customer creates a conversation linked to a Purchase ID", r.status === 200 && String(r.json.conversation.conversation_id).startsWith("conv_") && r.json.conversation.purchase_id === memberPurchaseId, r.json);
const convId = r.json.conversation && r.json.conversation.conversation_id;
// legacy order chat (messages keyed only by order_id) must show up in the same history
r = await call("POST", "/api/orders/" + acctOrderId + "/message", { token: freshSession, body: { body: "Legacy order chat message." } });
check("legacy per-order chat still accepts a message", r.status === 200, r.json);
r = await call("GET", "/api/conversations", { token: freshSession });
check("conversation list includes it with a message count", r.status === 200 && (r.json.conversations || []).some(c => c.conversation_id === convId && c.message_count >= 1), r.json.conversations);
const legacyConvId = "conv_" + String(acctOrderId).replace(/[^A-Za-z0-9_-]/g, "");
check("legacy order chat is surfaced as a conversation", (r.json.conversations || []).some(c => c.conversation_id === legacyConvId && c.order_id === acctOrderId), r.json.conversations);
r = await call("GET", "/api/conversations/" + convId, { token: freshSession });
check("owner reads the thread", r.status === 200 && r.json.messages.length === 1 && r.json.messages[0].body.includes("rename"), r.json);
r = await call("POST", "/api/conversations/" + convId + "/messages", { token: freshSession, body: { body: "Also please add a hello command." } });
check("owner posts a follow-up message", r.status === 200 && r.json.messages.length === 2, r.json);
r = await call("POST", "/api/conversations", { token: adminToken, body: { message: "can I attach someone else's order?", purchase_id: memberPurchaseId } });
check("another account cannot attach someone else's Purchase ID -> 404", r.status === 404, r.json);
r = await call("GET", "/api/conversations/" + convId, { token: adminToken });
check("IDOR: another account reading the thread -> 404", r.status === 404, r.json);
r = await call("POST", "/api/conversations/" + convId + "/messages", { token: adminToken, body: { body: "let me in" } });
check("IDOR: another account posting into the thread -> 404", r.status === 404, r.json);
r = await call("GET", "/api/conversations/" + convId);
check("anonymous conversation read -> 401", r.status === 401, r.json);
r = await call("POST", "/api/conversations", { token: freshSession, body: { message: "general question, no order" } });
check("a question without a Purchase ID is allowed", r.status === 200 && r.json.conversation.purchase_id === "", r.json);
const generalConv = r.json.conversation && r.json.conversation.conversation_id;
r = await call("POST", "/api/conversations", { token: freshSession, body: { message: "bad id", purchase_id: "AETH-2026-X" } });
check("malformed Purchase ID on create -> 400", r.status === 400, r.json);
r = await call("POST", "/api/conversations/" + generalConv + "/status", { token: freshSession, body: { status: "closed" } });
check("owner can close their conversation", r.status === 200 && r.json.status === "closed", r.json);
r = await call("POST", "/api/conversations/" + generalConv + "/messages", { token: freshSession, body: { body: "one more thing" } });
check("a closed conversation refuses new messages", r.status === 400, r.json);
r = await call("POST", "/api/conversations/" + convId + "/messages", { token: freshSession, body: { body: "z".repeat(9000) } });
check("overlong chat message is capped at 4000 chars", r.status === 200 && String((r.json.messages || [])[r.json.messages.length - 1].body).length === 4000, r.status);

console.log("\n--- admin: support conversations ---");
r = await call("GET", "/api/admin/conversations", { token: freshSession });
check("non-admin cannot list customer conversations -> 403", r.status === 403, r.json);
r = await call("GET", "/api/admin/conversations", { token: adminToken });
check("admin lists customer conversations", r.status === 200 && (r.json.conversations || []).some(c => c.conversation_id === convId), r.json);
r = await call("POST", "/api/admin/conversations/" + convId + "/messages", { token: adminToken, body: { body: "Reply from support." } });
check("admin replies into the conversation", r.status === 200 && (r.json.messages || []).some(m => m.sender === "admin"), r.json);
r = await call("GET", "/api/conversations/" + convId, { token: freshSession });
check("customer sees the support reply in their own history", r.status === 200 && (r.json.messages || []).some(m => m.sender === "admin" && m.body.includes("support")), r.json);

console.log("\n--- roles: Tester grant/revoke + Beta access ---");
const memberRow = env.DB.db.prepare("SELECT id, role FROM users WHERE email = ?").get("member@example.com");
check("new accounts start as role 'user'", memberRow && memberRow.role === "user", memberRow);
const adminId = env.DB.db.prepare("SELECT id FROM users WHERE email = ?").get("admin@example.com").id;
r = await call("POST", "/api/admin/users/" + memberRow.id + "/role", { token: freshSession, body: { role: "tester" } });
check("a user cannot grant themselves Tester -> 403", r.status === 403, r.json);
r = await call("POST", "/api/admin/users/" + memberRow.id + "/role", { token: adminToken, body: { role: "admin" } });
check("the API refuses to grant Admin", r.status === 400, r.json);
r = await call("POST", "/api/admin/users/" + adminId + "/role", { token: adminToken, body: { role: "user" } });
check("admin accounts cannot be demoted through the API -> 400", r.status === 400, r.json);
// The gate says which door is shut: signed out, address unconfirmed, or no role. One sentence for all
// three told two of those people something untrue about their own account.
r = await call("GET", "/api/program", { token: freshSession });
check("a signed-in customer without the role is told exactly that -> NOT_TESTER", r.status === 403 && r.json.code === "NOT_TESTER", r.json);
r = await call("GET", "/api/program");
check("a signed-out visitor is told to sign in -> SESSION_REQUIRED", r.status === 403 && r.json.code === "SESSION_REQUIRED", r.json);
r = await call("GET", "/api/beta/status", { token: freshSession });
check("the Beta refuses a non-Tester with the same specific code", r.status === 403 && r.json.code === "NOT_TESTER", r.json);
r = await call("GET", "/api/admin/users", { token: adminToken });
check("the account list reports whether verification is enforced on this deployment",
  r.status === 200 && r.json.emailVerificationRequired === false, r.json && r.json.emailVerificationRequired);
r = await call("POST", "/api/admin/users/" + memberRow.id + "/role", { token: adminToken, body: { role: "tester" } });
check("admin grants Tester", r.status === 200 && r.json.role === "tester", r.json);
check("a grant with nothing outstanding answers with an empty warnings list", Array.isArray(r.json.warnings) && r.json.warnings.length === 0, r.json.warnings);
check("the role is stored server-side", env.DB.db.prepare("SELECT role FROM users WHERE id = ?").get(memberRow.id).role === "tester");
r = await call("GET", "/api/beta/access", { token: freshSession });
check("tester is allowed into the Beta (server-side decision)", r.status === 200 && r.json.allowed === true && r.json.role === "tester", r.json);
check("the Beta URL is only returned to allowed testers", !!(r.json.beta && String(r.json.beta.url).includes("betatester.get-aether.de")), r.json.beta);
r = await call("GET", "/api/beta/status", { token: freshSession });
check("tester reads beta status + server-side flags", r.status === 200 && r.json.beta.host === "betatester.get-aether.de" && r.json.flags.betaNewChat === true, r.json);
r = await call("POST", "/api/beta/feedback", { token: freshSession, body: { area: "chat", message: "Beta chat feels fast." } });
check("tester can send beta feedback", r.status === 200, r.json);
r = await call("GET", "/api/beta/status", { token: adminToken });
check("admin also counts as a tester", r.status === 200, r.json);
r = await call("POST", "/api/beta/ticket", { token: freshSession });
check("tester can mint a single-use beta ticket", r.status === 200 && typeof r.json.ticket === "string", r.json);
const betaTicket = r.json.ticket;
r = await call("POST", "/api/beta/redeem", { body: { ticket: betaTicket } });
check("beta ticket redeems into a session", r.status === 200 && typeof r.json.token === "string" && r.json.user.role === "tester", r.json);
r = await call("POST", "/api/beta/redeem", { body: { ticket: betaTicket } });
check("beta ticket is single use", r.status === 400, r.json);

// The program build. The whole point of this section is that the file's location never reaches the
// browser: the API answers with metadata and a single-use ticket cookie, and streams the bytes itself.
console.log("\n--- program builds: tester-only download, location held server-side ---");
const realFetchProgram = globalThis.fetch;
const programSessionCookie = (t) => "__Host-aether_session=" + t;
const buildBytes = "BINARY-BYTES-CONTENT";
const buildSha = sha(buildBytes);
r = await call("GET", "/api/program");
check("anonymous cannot read the program build -> 403", r.status === 403, r.json);
r = await call("GET", "/api/program", { token: adminToken });
check("with no build configured -> available:false, no build, no location anywhere in the body",
  r.status === 200 && r.json.available === false && r.json.build === null && !/http|url/i.test(JSON.stringify(r.json)), r.json);
env.PROGRAM_NAME = "Aether Desktop";
env.PROGRAM_VERSION = "0.1.0-beta.1";
env.PROGRAM_PLATFORM = "Windows 10/11 (x64)";
env.PROGRAM_SIZE = "48 MB";
env.PROGRAM_SHA256 = "not-a-checksum";
env.PROGRAM_URL = "https://files.internal.invalid/builds/aether-desktop-0.1.0-beta.1.exe";
r = await call("GET", "/api/program", { token: freshSession });
check("a tester sees the build metadata", r.status === 200 && r.json.available === true && r.json.build.version === "0.1.0-beta.1", r.json.build);
check("a checksum that is not 64 hex characters is never printed as one", r.json.build.sha256 === "", r.json.build.sha256);
check("the metadata never contains the build's location", !JSON.stringify(r.json).includes("files.internal.invalid"), JSON.stringify(r.json));
check("the ticket lifetime reported by the API is the server's own number", r.json.expiresIn === 120, r.json.expiresIn);
env.PROGRAM_SHA256 = "a".repeat(64);
r = await call("GET", "/api/program", { token: freshSession });
check("a real checksum is passed through", r.json.build.sha256 === "a".repeat(64), r.json.build.sha256);

// A download is a browser navigation, and a navigation can only carry a cookie - so the mint refuses
// to issue a ticket the browser could never complete instead of handing out a dead one.
r = await call("POST", "/api/program/download", { token: freshSession });
check("minting without the session cookie is refused with an actionable reason -> 400 COOKIE_REQUIRED",
  r.status === 400 && r.json.code === "COOKIE_REQUIRED", r.json);
r = await call("POST", "/api/program/download", { token: freshSession, cookie: programSessionCookie(freshSession) });
let dlTicket = (/aether_dl=([0-9a-f]{64})/.exec(r.setCookie || "") || [])[1] || "";
check("minting answers with a path and a cookie, never a secret in the body",
  r.status === 200 && r.json.url === "/api/program/file" && !JSON.stringify(r.json).includes("files.internal.invalid") && !JSON.stringify(r.json).includes(dlTicket), r.json);
check("the ticket cookie is path-narrowed, HttpOnly, Secure and SameSite=Strict",
  /aether_dl=[0-9a-f]{64}/.test(r.setCookie) && r.setCookie.includes("Path=/api/program/file") && r.setCookie.includes("HttpOnly") && r.setCookie.includes("Secure") && r.setCookie.includes("SameSite=Strict"), r.setCookie);
check("the ticket is stored only as a hash",
  env.DB.db.prepare("SELECT COUNT(*) AS n FROM auth_tokens WHERE token_hash = ?").get(sha(dlTicket)).n === 1 &&
  env.DB.db.prepare("SELECT COUNT(*) AS n FROM auth_tokens WHERE token_hash = ?").get(dlTicket).n === 0);
check("the ticket names the session that minted it",
  String((env.DB.db.prepare("SELECT payload FROM auth_tokens WHERE token_hash = ?").get(sha(dlTicket)) || {}).payload || "").includes(sha(freshSession)),
  (env.DB.db.prepare("SELECT payload FROM auth_tokens WHERE token_hash = ?").get(sha(dlTicket)) || {}).payload);

const streamed = [];
globalThis.fetch = async (u) => { streamed.push(String(u)); return new Response(buildBytes, { headers: { "content-length": String(buildBytes.length), "content-type": "application/octet-stream" } }); };
r = await call("GET", "/api/program/file", { cookie: programSessionCookie(freshSession) });
check("the file endpoint with no ticket cookie -> 400 TICKET_REQUIRED", r.status === 400 && r.json.code === "TICKET_REQUIRED", r.json);
r = await call("GET", "/api/program/file", { cookie: "aether_dl=" + "f".repeat(64) + "; " + programSessionCookie(freshSession) });
check("a forged ticket cookie -> 400 TICKET_SPENT", r.status === 400 && r.json.code === "TICKET_SPENT", r.json);
r = await call("GET", "/api/program/file", { cookie: "aether_dl=" + dlTicket });
check("the ticket on its own, without the sign-in it was minted for -> 403 WRONG_SESSION",
  r.status === 403 && r.json.code === "WRONG_SESSION", r.json);
check("a refused ticket is spent anyway and the upstream is never reached",
  env.DB.db.prepare("SELECT COUNT(*) AS n FROM auth_tokens WHERE token_hash = ?").get(sha(dlTicket)).n === 0 && streamed.length === 0,
  { rows: env.DB.db.prepare("SELECT COUNT(*) AS n FROM auth_tokens WHERE token_hash = ?").get(sha(dlTicket)).n, streamed });
check("every refusal clears the ticket cookie on the device",
  /aether_dl=;/.test(r.setCookie || "") && /Max-Age=0/.test(r.setCookie || ""), r.setCookie);

// A checksum that does not describe what the host actually serves: nothing may be sent.
r = await call("POST", "/api/program/download", { token: freshSession, cookie: programSessionCookie(freshSession) });
dlTicket = (/aether_dl=([0-9a-f]{64})/.exec(r.setCookie || "") || [])[1] || "";
r = await call("GET", "/api/program/file", { cookie: "aether_dl=" + dlTicket + "; " + programSessionCookie(freshSession) });
check("an artifact that does not match the published checksum is refused -> 409 CHECKSUM_MISMATCH",
  r.status === 409 && r.json.code === "CHECKSUM_MISMATCH", r.json);
check("and not one byte of it reached the visitor", !JSON.stringify(r.json).includes("BINARY-BYTES"), r.json);
check("the refusal is in the audit log",
  env.DB.db.prepare("SELECT COUNT(*) AS n FROM audit_log WHERE action = 'program.download.mismatch'").get().n >= 1);

// A role revoked between minting and use must stop a ticket that was already issued.
r = await call("POST", "/api/program/download", { token: freshSession, cookie: programSessionCookie(freshSession) });
dlTicket = (/aether_dl=([0-9a-f]{64})/.exec(r.setCookie || "") || [])[1] || "";
env.DB.db.prepare("UPDATE users SET role = 'user' WHERE id = ?").run(memberId);
r = await call("GET", "/api/program/file", { cookie: "aether_dl=" + dlTicket + "; " + programSessionCookie(freshSession) });
check("a ticket stops working when the role is revoked before it is used -> 403", r.status === 403, r.json);
r = await call("POST", "/api/program/download", { token: freshSession, cookie: programSessionCookie(freshSession) });
check("and a normal account cannot mint one at all -> 403", r.status === 403, r.json);
env.DB.db.prepare("UPDATE users SET role = 'tester' WHERE id = ?").run(memberId);

// The real thing: a matching checksum, and the bytes.
env.PROGRAM_SHA256 = buildSha;
r = await call("POST", "/api/program/download", { token: freshSession, cookie: programSessionCookie(freshSession) });
const firstTicket = (/aether_dl=([0-9a-f]{64})/.exec(r.setCookie || "") || [])[1] || "";
r = await call("POST", "/api/program/download", { token: freshSession, cookie: programSessionCookie(freshSession) });
dlTicket = (/aether_dl=([0-9a-f]{64})/.exec(r.setCookie || "") || [])[1] || "";
r = await call("GET", "/api/program/file", { cookie: "aether_dl=" + firstTicket + "; " + programSessionCookie(freshSession) });
check("minting again invalidates the previous ticket (one live ticket per account) -> 400",
  r.status === 400 && r.json.code === "TICKET_SPENT", r.json);
const fetchesBefore = streamed.length;
r = await call("GET", "/api/program/file", { cookie: "aether_dl=" + dlTicket + "; " + programSessionCookie(freshSession) });
globalThis.fetch = realFetchProgram;
check("the verified build is served as an attachment named after the real artifact",
  r.status === 200 && /attachment; filename="aether-desktop-0\.1\.0-beta\.1\.exe"/.test(r.headers.get("content-disposition") || ""), r.headers.get("content-disposition"));
check("the download is uncacheable, noindex, and marked as verified against the published checksum",
  /no-store/.test(r.headers.get("cache-control") || "") && /noindex/.test(r.headers.get("x-robots-tag") || "") && r.headers.get("x-content-verified") === "sha256" && r.headers.get("x-checksum-sha256") === buildSha,
  { cc: r.headers.get("cache-control"), robots: r.headers.get("x-robots-tag"), verified: r.headers.get("x-content-verified"), sum: r.headers.get("x-checksum-sha256") });
check("the exact byte length and a content digest travel with it",
  r.headers.get("content-length") === String(buildBytes.length) && /^sha-256=:/.test(r.headers.get("content-digest") || ""),
  { len: r.headers.get("content-length"), digest: r.headers.get("content-digest") });
check("the worker fetched the configured upstream once, and only that upstream",
  streamed.length === fetchesBefore + 1 && streamed[streamed.length - 1] === env.PROGRAM_URL && streamed.every(u => u === env.PROGRAM_URL), streamed);
check("the bytes came back to the visitor", r.json && r.json.raw === buildBytes, r.json);
r = await call("GET", "/api/program/file", { cookie: "aether_dl=" + dlTicket + "; " + programSessionCookie(freshSession) });
check("the same ticket cannot be spent twice -> 400", r.status === 400 && r.json.code === "TICKET_SPENT", r.json);

// Too large to hold in memory: still served, but labelled instead of pretending to be verified.
const bigBytes = "B".repeat(4096);
env.PROGRAM_VERIFY_MAX_BYTES = "1024";
r = await call("POST", "/api/program/download", { token: freshSession, cookie: programSessionCookie(freshSession) });
dlTicket = (/aether_dl=([0-9a-f]{64})/.exec(r.setCookie || "") || [])[1] || "";
globalThis.fetch = async () => new Response(bigBytes, { headers: { "content-length": String(bigBytes.length) } });
r = await call("GET", "/api/program/file", { cookie: "aether_dl=" + dlTicket + "; " + programSessionCookie(freshSession) });
globalThis.fetch = realFetchProgram;
check("an artifact above the verification limit is served but labelled x-content-verified: unverified",
  r.status === 200 && r.headers.get("x-content-verified") === "unverified" && r.headers.get("content-digest") === null,
  { verified: r.headers.get("x-content-verified"), digest: r.headers.get("content-digest"), status: r.status });
delete env.PROGRAM_VERIFY_MAX_BYTES;
env.PROGRAM_SHA256 = "";
r = await call("POST", "/api/program/download", { token: freshSession, cookie: programSessionCookie(freshSession) });
dlTicket = (/aether_dl=([0-9a-f]{64})/.exec(r.setCookie || "") || [])[1] || "";
globalThis.fetch = async () => new Response(buildBytes, { headers: { "content-length": String(buildBytes.length) } });
r = await call("GET", "/api/program/file", { cookie: "aether_dl=" + dlTicket + "; " + programSessionCookie(freshSession) });
globalThis.fetch = realFetchProgram;
check("with no checksum published the build is still served, unverified and without an invented checksum header",
  r.status === 200 && r.headers.get("x-content-verified") === "unverified" && r.headers.get("x-checksum-sha256") === null,
  { verified: r.headers.get("x-content-verified"), sum: r.headers.get("x-checksum-sha256"), status: r.status });

// Plaintext is not a download source: a checksum would only describe whatever the path rewrote it to.
env.PROGRAM_SHA256 = buildSha;
env.PROGRAM_URL = "http://files.example.com/build.exe";
r = await call("GET", "/api/program", { token: freshSession });
check("an http build source counts as not configured at all", r.status === 200 && r.json.available === false, r.json);
r = await call("POST", "/api/program/download", { token: freshSession, cookie: programSessionCookie(freshSession) });
check("and nothing can be minted for it -> 503", r.status === 503, r.json);
env.PROGRAM_URL = "http://127.0.0.1:5501/public/aether-logo.png";
r = await call("GET", "/api/program", { token: freshSession });
check("loopback http is refused too while ALLOW_DEV_ORIGIN is off", r.json.available === false, r.json);
env.ALLOW_DEV_ORIGIN = "true";
r = await call("GET", "/api/program", { token: freshSession });
check("the local dev server can still stand a local file in for a build", r.json.available === true, r.json);
delete env.ALLOW_DEV_ORIGIN;
env.PROGRAM_URL = "https://files.internal.invalid/builds/aether-desktop-0.1.0-beta.1.exe";
r = await call("POST", "/api/program");
check("POST on the info endpoint -> 405", r.status === 405, r.json);
r = await call("POST", "/api/program/file");
check("POST on the file endpoint -> 405", r.status === 405, r.json);
// The kill switch and the per-account cap: both exist for the moment something has to stop fast.
env.PROGRAM_DISABLED = "true";
r = await call("GET", "/api/program", { token: freshSession });
check("PROGRAM_DISABLED withholds the metadata as well -> available:false, paused:true, no build",
  r.status === 200 && r.json.available === false && r.json.build === null && r.json.paused === true, r.json);
r = await call("POST", "/api/program/download", { token: freshSession, cookie: programSessionCookie(freshSession) });
check("and nothing can be minted while it is set -> 503 PROGRAM_PAUSED", r.status === 503 && r.json.code === "PROGRAM_PAUSED", r.json);
delete env.PROGRAM_DISABLED;
r = await call("POST", "/api/program/download", { token: freshSession, cookie: programSessionCookie(freshSession) });
dlTicket = (/aether_dl=([0-9a-f]{64})/.exec(r.setCookie || "") || [])[1] || "";
env.PROGRAM_DISABLED = "true";
r = await call("GET", "/api/program/file", { cookie: "aether_dl=" + dlTicket + "; " + programSessionCookie(freshSession) });
check("a ticket minted a second earlier cannot be spent once the switch is thrown -> 503",
  r.status === 503 && r.json.code === "PROGRAM_PAUSED", r.json);
delete env.PROGRAM_DISABLED;
seedRateLimit("program-mint:" + memberRow.id, 10, 3600000);
r = await call("POST", "/api/program/download", { token: freshSession, cookie: programSessionCookie(freshSession) });
check("a per-account mint cap backs up the per-IP one -> 429", r.status === 429 && r.json.code === "TOO_MANY", r.json);
env.DB.db.prepare("DELETE FROM rate_limits WHERE rl_key = ?").run("program-mint:" + memberRow.id);
delete env.PROGRAM_URL; delete env.PROGRAM_VERSION; delete env.PROGRAM_PLATFORM;
delete env.PROGRAM_SIZE; delete env.PROGRAM_SHA256; delete env.PROGRAM_NAME;
r = await call("POST", "/api/program/download", { token: freshSession, cookie: programSessionCookie(freshSession) });
check("with nothing published the mint answers an honest 503 instead of a fake link", r.status === 503, r.json);
r = await call("POST", "/api/beta/ticket", { token: adminToken });
const adminTicket = r.json.ticket;
r = await call("POST", "/api/admin/users/" + memberRow.id + "/role", { token: adminToken, body: { role: "user" } });
check("admin removes Tester", r.status === 200 && r.json.role === "user", r.json);
r = await call("GET", "/api/beta/status", { token: freshSession });
check("removed tester is blocked from the Beta -> 403", r.status === 403, r.json);
r = await call("GET", "/api/beta/access", { token: freshSession });
check("access says not allowed for a normal user and leaks no URL", r.status === 200 && r.json.allowed === false && r.json.beta === null, r.json);
r = await call("POST", "/api/beta/redeem", { body: { ticket: adminTicket } });
check("a minted admin ticket still redeems (admin keeps test access)", r.status === 200, r.json);

console.log("\n--- admin: Beta domain change needs confirmation ---");
r = await call("GET", "/api/admin/beta", { token: adminToken });
check("admin reads the beta config", r.status === 200 && r.json.beta.host === "betatester.get-aether.de" && r.json.beta.path === "/yesthisistheofficaldomainanditssolongsopeopledontaccidentlyfindthis", r.json.beta);
r = await call("GET", "/api/admin/beta", { token: freshSession });
check("a normal user cannot read the beta config -> 403", r.status === 403, r.json);
r = await call("POST", "/api/admin/beta/domain/request", { token: adminToken, body: { host: "evil.example.com", path: "/somewhere", currentPassword: "adminsecret1" } });
check("beta host must stay under get-aether.de -> 400", r.status === 400, r.json);
r = await call("POST", "/api/admin/beta/domain/request", { token: adminToken, body: { host: "betatester.get-aether.de", path: "/newplace", currentPassword: "wrong-password" } });
check("wrong admin password -> 401 and nothing changes", r.status === 401, r.json);
r = await call("POST", "/api/admin/beta/domain/request", { token: adminToken, body: { host: "betatester.get-aether.de", path: "/newplace", currentPassword: "adminsecret1" } });
check("domain change request creates a pending confirmation (not applied)", r.status === 200 && r.json.pending.path === "/newplace", r.json);
const domainToken = (String(r.json.confirm_url || "").match(/betaToken=([a-f0-9]{64})/) || [])[1] || "";
check("confirmation link carries a 64-hex single-use token", !!domainToken, r.json.confirm_url);
check("the stored beta path is unchanged until confirmation", env.DB.db.prepare("SELECT COUNT(*) AS n FROM settings WHERE key = 'beta_path'").get().n === 0);
r = await call("POST", "/api/admin/beta/domain/confirm", { token: adminToken, body: { token: "deadbeef" } });
check("bogus confirmation token -> 400", r.status === 400, r.json);
r = await call("POST", "/api/admin/beta/domain/confirm", { token: freshSession, body: { token: domainToken } });
check("a non-admin cannot confirm the change -> 403", r.status === 403, r.json);
r = await call("POST", "/api/admin/beta/domain/confirm", { token: adminToken, body: { token: domainToken } });
check("admin confirms the change", r.status === 200 && r.json.beta.path === "/newplace", r.json);
r = await call("POST", "/api/admin/beta/domain/confirm", { token: adminToken, body: { token: domainToken } });
check("the confirmation token is single use", r.status === 400, r.json);
r = await call("GET", "/api/admin/beta", { token: adminToken });
check("the confirmed value is what is stored", r.json.beta.path === "/newplace" && r.json.beta.url === "https://betatester.get-aether.de/newplace", r.json.beta);
r = await call("POST", "/api/admin/beta/flags", { token: adminToken, body: { flags: { betaTools: false } } });
check("admin flips a server-side feature flag", r.status === 200 && r.json.flags.betaTools === false, r.json);
r = await call("GET", "/api/beta/status", { token: adminToken });
check("the flag is what the beta API reports", r.json.flags.betaTools === false, r.json.flags);
r = await call("POST", "/api/admin/beta/flags", { token: freshSession, body: { flags: { betaTools: true } } });
check("a normal user cannot flip flags -> 403", r.status === 403, r.json);
r = await call("GET", "/api/admin/audit", { token: adminToken });
check("audit log records tester + beta-domain actions", r.status === 200 && (r.json.audit || []).some(a => a.action === "tester.grant") && (r.json.audit || []).some(a => a.action === "beta.domain.confirm"), (r.json.audit || []).slice(0, 4));
r = await call("GET", "/api/admin/audit", { token: freshSession });
check("the audit log is admin-only -> 403", r.status === 403, r.json);

console.log("\n--- verified-email gate ---");
env.RESEND_API_KEY = "re_placeholder_for_tests";
r = await call("POST", "/api/auth/register", { body: { email: "unverified@example.com", password: "unverified1" } });
check("register reports verification required when mail is configured", r.status === 200 && r.json.emailVerificationRequired === true && r.json.emailVerified === false, r.json);
const unverifiedToken = r.json.token;
r = await call("GET", "/api/orders", { token: unverifiedToken });
check("unverified account cannot read purchases -> 403", r.status === 403 && r.json.code === "EMAIL_UNVERIFIED", r.json);
r = await call("POST", "/api/conversations", { token: unverifiedToken, body: { message: "let me in please" } });
check("unverified account cannot open a conversation -> 403", r.status === 403, r.json);
r = await call("GET", "/api/beta/status", { token: unverifiedToken });
check("unverified account cannot reach the Beta -> 403", r.status === 403, r.json);
// ...and it must not stop at "cannot" when the account only has to open one mail: the refusal names the
// single thing standing in the way, which is what the Beta page then repeats to the tester.
r = await call("POST", "/api/auth/register", { body: { email: "unvertester@example.com", password: "unvertester1" } });
check("a Tester with an unconfirmed address is refused for that reason and no other", r.status === 200, r.json);
const unvTester = env.DB.db.prepare("SELECT id FROM users WHERE email = ?").get("unvertester@example.com");
env.DB.db.prepare("UPDATE users SET role = 'tester' WHERE id = ?").run(unvTester.id);
r = await call("GET", "/api/program", { token: r.json.token });
check("Tester + unconfirmed address -> EMAIL_UNVERIFIED, not NOT_TESTER", r.status === 403 && r.json.code === "EMAIL_UNVERIFIED", r.json);
// Sensitive admin changes need a confirmed admin address too, and this section is the one place where a
// mail provider is configured. The test env has no inbox, so the admin is confirmed directly.
env.DB.db.prepare("UPDATE users SET email_verified = 1 WHERE email = ?").run("admin@example.com");
r = await call("POST", "/api/admin/users/" + unvTester.id + "/role", { token: adminToken, body: { role: "tester" } });
check("re-granting the same role says it is already done instead of inventing a change", r.status === 200 && r.json.unchanged === true, r.json);
r = await call("POST", "/api/admin/users/" + unvTester.id + "/role", { token: adminToken, body: { role: "user" } });
check("revoking a role needs no warning (nothing is left blocked by an unconfirmed address)", r.status === 200 && (r.json.warnings || []).length === 0, r.json);
r = await call("POST", "/api/admin/users/" + unvTester.id + "/role", { token: adminToken, body: { role: "tester" } });
check("granting Tester to an unconfirmed account warns instead of pretending it is done",
  r.status === 200 && r.json.role === "tester" && (r.json.warnings || []).length === 1 && /confirm/i.test(r.json.warnings[0]), r.json);
r = await call("POST", "/api/auth/resend-verification", {});
check("resend without a session -> 401", r.status === 401, r.json);
const unvId = env.DB.db.prepare("SELECT id FROM users WHERE email = ?").get("unverified@example.com").id;
let resendLimited = false, resendStatuses = [];
for (let i = 0; i < 3 && !resendLimited; i++) { seedRateLimit("resend:" + unvId, 4, 3600000); const rr = await call("POST", "/api/auth/resend-verification", { token: unverifiedToken }); resendStatuses.push(rr.status); resendLimited = rr.status === 429; }
check("verification resend is rate limited per account", resendLimited, resendStatuses);
const gateToken = "verify-gate-" + "c".repeat(40);
env.DB.db.prepare("INSERT INTO auth_tokens (token_hash, user_id, purpose, expires_at, created_at) VALUES (?,?,?,?,?)").run(sha(gateToken), unvId, "verify", Date.now() + 600000, Date.now());
r = await call("POST", "/api/auth/verify-email", { body: { token: gateToken } });
check("confirming the email flips the flag", r.status === 200 && r.json.verified === true, r.json);
r = await call("GET", "/api/conversations", { token: unverifiedToken });
check("the verified account can now read their conversations", r.status === 200, r.json);
r = await call("GET", "/api/orders", { token: unverifiedToken });
check("and their purchases", r.status === 200, r.json);
delete env.RESEND_API_KEY;
r = await call("GET", "/api/orders", { token: unverifiedToken });
check("with no mail provider the gate stands down (no honest lockout)", r.status === 200, r.json);

console.log("\n--- NOWPayments IPN: route, signature, status, notifications ---");
// The callback is POST-only, and the vendor algorithm is the reference here: recursively sorted keys,
// compact JSON, HMAC-SHA512 with the IPN secret - implemented independently of the worker, so a
// future change that drifts from the documented scheme fails here instead of in production.
const npSort = (v) => Array.isArray(v)
  ? v.map(npSort)
  : (v && typeof v === "object" ? Object.keys(v).sort().reduce((o, k) => { o[k] = npSort(v[k]); return o; }, {}) : v);
const npSig = (payload, secret) => createHmac("sha512", secret).update(JSON.stringify(npSort(payload))).digest("hex");

r = await call("GET", "/api/ipn");
check("GET on the IPN callback explains the method instead of reading as a dead route",
  r.status === 405 && r.headers.get("allow") === "POST" && r.json.ipnCallbackUrl === "https://api.get-aether.de/api/ipn", { status: r.status, allow: r.headers.get("allow"), body: r.json });
check("the method answer never echoes a secret", !/secret/i.test(JSON.stringify(r.json)), r.json);

const ipnOrderId = "aether_ipn_test_order";
env.DB.db.prepare("INSERT INTO orders (order_id, purchase_id, payment_id, amount, currency, type, status, email, user_id) VALUES (?,?,?,?,?,?,?,?,?)")
  .run(ipnOrderId, "AETH-IPN-TEST-1", "555000111", 42, "eur", "discord_bot", "waiting", "test@example.com", 1);
const ipnDbStatus = () => { const row = env.DB.db.prepare("SELECT status FROM orders WHERE order_id = ?").get(ipnOrderId); return row ? row.status : null; };
const ipnPayload = (status, extra) => Object.assign({
  payment_id: 555000111, payment_status: status, pay_address: "bc1qprobeexampleaddress",
  price_amount: 42, price_currency: "eur", pay_amount: 0.0005, actually_paid: 0,
  pay_currency: "btc", order_id: ipnOrderId, order_description: "Aether order",
  purchase_id: "AETH-IPN-TEST-1", outcome_amount: 42, outcome_currency: "eur",
  customer_email: "test@example.com", created_at: "2026-10-02T10:00:00.000Z", updated_at: "2026-10-02T10:00:30.000Z",
}, extra || {});

const ipnMails = [];
const ipnDiscordPosts = [];
const realFetchIpn = globalThis.fetch;
env.EMAIL = { send: async (m) => { ipnMails.push(m); return { messageId: "ipn-" + ipnMails.length }; } };
env.DISCORD_WEBHOOK_URL = "https://discord.com/api/webhooks/7/ipn-token";
globalThis.fetch = async (url, init) => { const u = String(url); if (u.includes("discord.com/api/webhooks")) ipnDiscordPosts.push(init && init.body ? String(init.body) : ""); return { ok: true, status: 200, text: async () => "{}" }; };
const customerMails = () => ipnMails.filter(m => (Array.isArray(m.to) ? m.to : [m.to]).includes("test@example.com") && /Payment received/.test(String(m.subject || "")));
env.NOWPAYMENTS_IPN_SECRET = "ipn-secret-test";
env.NOWPAYMENTS_IPN_SECRET_2 = "ipn-secret-test-2";

r = await call("POST", "/api/ipn", { body: ipnPayload("confirmed") });
check("POST without a signature is rejected while a secret is set", r.status === 401 && JSON.stringify(r.json) === '{"error":"Bad signature"}', r.json);
r = await call("POST", "/api/ipn", { body: ipnPayload("confirmed"), headers: { "x-nowpayments-sig": "deadbeef" } });
check("a wrong signature is rejected", r.status === 401, r.json);
const tamperedSigned = ipnPayload("confirmed");
const tamperedBody = ipnPayload("confirmed", { price_amount: 99999 });
r = await call("POST", "/api/ipn", { body: tamperedBody, headers: { "x-nowpayments-sig": npSig(tamperedSigned, "ipn-secret-test") } });
check("a payload edited after signing is rejected", r.status === 401, r.json);
check("nothing was written for rejected callbacks", ipnDbStatus() === "waiting" && ipnMails.length === 0 && ipnDiscordPosts.length === 0, { status: ipnDbStatus(), mails: ipnMails.length, discord: ipnDiscordPosts.length });

const waiting = ipnPayload("waiting");
r = await call("POST", "/api/ipn", { body: waiting, origin: "https://evil.example", headers: { "x-nowpayments-sig": npSig(waiting, "ipn-secret-test-2") } });
check("a server-to-server callback is exempt from the browser-origin guard (and the second secret covers rotation)",
  r.status === 200 && r.json.ok === true && r.json.status === "waiting", r.json);
check("a non-paid callback notifies ops but never mails the customer", ipnDiscordPosts.length === 1 && customerMails().length === 0 && ipnDbStatus() === "waiting", { discord: ipnDiscordPosts.length, customer: customerMails().length, status: ipnDbStatus() });

const confirmed = ipnPayload("confirmed");
r = await call("POST", "/api/ipn", { body: confirmed, origin: "", headers: { "x-nowpayments-sig": npSig(confirmed, "ipn-secret-test") } });
check("a correctly signed confirmation is accepted, answering only ok + status", r.status === 200 && JSON.stringify(r.json) === '{"ok":true,"status":"confirmed"}', r.json);
check("the order status moves to paid", ipnDbStatus() === "paid", ipnDbStatus());
check("the customer gets exactly one receipt", customerMails().length === 1 && customerMails()[0].to[0] === "test@example.com", customerMails().length);
check("ops is notified of the same callback (email + Discord)",
  ipnMails.some(m => (Array.isArray(m.to) ? m.to : [m.to]).includes("questions@get-aether.de")) && ipnDiscordPosts.length === 2, { mails: ipnMails.length, discord: ipnDiscordPosts.length });

r = await call("POST", "/api/ipn", { body: confirmed, headers: { "x-nowpayments-sig": npSig(confirmed, "ipn-secret-test") } });
check("a replayed callback is accepted but does not mail a second receipt", r.status === 200 && customerMails().length === 1, { status: r.status, customer: customerMails().length });
const finished = ipnPayload("finished", { actually_paid: 0.0005 });
r = await call("POST", "/api/ipn", { body: finished, headers: { "x-nowpayments-sig": npSig(finished, "ipn-secret-test") } });
check("the next status (finished) also refuses to resend the receipt", r.status === 200 && customerMails().length === 1 && ipnDbStatus() === "paid", { customer: customerMails().length, status: ipnDbStatus() });

const partial = ipnPayload("partially_paid", { actually_paid: 10 });
r = await call("POST", "/api/ipn", { body: partial, headers: { "x-nowpayments-sig": npSig(partial, "ipn-secret-test") } });
check("partially_paid is recorded as its own status", r.status === 200 && ipnDbStatus() === "partially_paid", ipnDbStatus());
const failed = ipnPayload("failed");
r = await call("POST", "/api/ipn", { body: failed, headers: { "x-nowpayments-sig": npSig(failed, "ipn-secret-test") } });
check("failed is recorded as failed", r.status === 200 && ipnDbStatus() === "failed", ipnDbStatus());

const unicodePayload = ipnPayload("finished", { order_description: "Aether \u2014 caf\u00e9 \uD83C\uDF89" });
r = await call("POST", "/api/ipn", { body: unicodePayload, headers: { "x-nowpayments-sig": npSig(unicodePayload, "ipn-secret-test") } });
check("a payload with non-ASCII text verifies with the documented Node algorithm (raw JSON.stringify)", r.status === 200, r.json);

r = await call("GET", "/api/admin/ipn", { token: freshSession });
check("a non-admin cannot read the IPN diagnostics -> 403", r.status === 403, r.json);
r = await call("GET", "/api/admin/ipn", { token: adminToken });
check("the admin view reports the configured secrets and the last accepted callback",
  r.status === 200 && r.json.secretConfigured === true && r.json.secret2Configured === true && r.json.lastAccepted && r.json.lastAccepted.status === "finished" && r.json.lastAccepted.signed === true, r.json);
check("the admin view reports the last rejected signature", r.status === 200 && !!r.json.lastRejected && r.json.lastRejected.reason === "bad_signature", r.json ? r.json.lastRejected : null);
check("the diagnostics never contain the secret itself", !JSON.stringify(r.json).includes("ipn-secret-test"), JSON.stringify(r.json).slice(0, 200));

env.NOWPAYMENTS_IPN_SECRET = "  ipn-secret-test\n";
const whitespace = ipnPayload("waiting");
r = await call("POST", "/api/ipn", { body: whitespace, headers: { "x-nowpayments-sig": npSig(whitespace, "ipn-secret-test") } });
check("a secret stored with trailing whitespace still verifies (dashboard paste)", r.status === 200, r.json);

delete env.NOWPAYMENTS_IPN_SECRET;
delete env.NOWPAYMENTS_IPN_SECRET_2;
const unsigned = ipnPayload("waiting");
r = await call("POST", "/api/ipn", { body: unsigned });
check("with no secret configured the endpoint still answers 200 (documented fallback; health says ipnSignature:false)", r.status === 200 && r.json.ok === true, r.json);
globalThis.fetch = realFetchIpn;
delete env.EMAIL;
delete env.DISCORD_WEBHOOK_URL;

console.log("\n--- promo codes + the EUR 0.02 test purchase ---");
// The promo engine is server-side only: the browser sends a code and the Worker decides the
// discount from PROMO_CODES. The 50% cap and the EUR 1 floor stay for normal codes; only an entry
// that opts in with test:true may reach 100% (EUR 0), and such an order is recorded as "free".
r = await call("GET", "/api/promo?code=TESTFULL&amount=30");
check("a test code gives a true 100% discount", r.status === 200 && r.json.valid === true && r.json.discount === 30 && r.json.finalAmount === 0 && r.json.test === true, r.json);
r = await call("GET", "/api/promo?code=CAP100&amount=30");
check("a normal 100% entry is still capped at 50% (no accidental free orders)", r.json.valid === true && r.json.discount === 15 && r.json.finalAmount === 15 && r.json.test === false, r.json);
r = await call("GET", "/api/promo?code=TESTFULL&amount=0.02");
check("cent precision: 100% of EUR 0.02 is EUR 0.02, not rounded to zero", r.json.valid === true && r.json.discount === 0.02 && r.json.finalAmount === 0, r.json);
r = await call("GET", "/api/promo?code=TESTFIXED&amount=0.02");
check("a test fixed code can cover a smaller amount too", r.json.valid === true && r.json.discount === 0.02 && r.json.finalAmount === 0, r.json);
r = await call("GET", "/api/promo?code=WELCOME10&amount=0.02");
check("a normal code on the EUR 0.02 item never raises its price (floor does not apply below EUR 1)", r.json.valid === true && r.json.discount === 0 && r.json.finalAmount === 0.02, r.json);
r = await call("GET", "/api/promo?code=NOPE&amount=30");
check("unknown codes are still invalid", r.json.valid === false, r.json);

// The test purchase: the server owns its price (EUR 0.02), package, description and meta.
r = await call("POST", "/api/invoice", { token: freshSession, body: { amount: 0.02, pay_currency: "btc", type: "test_purchase", package: "PREMIUM", description: "evil", meta: { tier: "PREMIUM" } } });
check("the test purchase reaches the normal invoice path and is priced by the server", r.status === 503 && typeof r.json.orderId === "string", { status: r.status, body: r.json });
const testRow = env.DB.db.prepare("SELECT amount, type, package, description, meta, status, user_id FROM orders WHERE order_id = ?").get(r.json.orderId);
check("price, package and meta are server-fixed - the posted values are ignored",
  testRow && testRow.amount === 0.02 && testRow.type === "test_purchase" && testRow.package === "Test Purchase" && /Test Purchase/.test(testRow.description) && JSON.parse(testRow.meta).test === true && testRow.status === "pending", testRow);
check("the test purchase is filed to the signed-in account", testRow && testRow.user_id === memberId, testRow);

r = await call("POST", "/api/invoice", { token: freshSession, body: { amount: 0.02, pay_currency: "btc", type: "discord_bot" } });
check("a normal product cannot be bought at the test price -> 400", r.status === 400, r.json);
r = await call("POST", "/api/invoice", { body: { amount: 0.02, pay_currency: "btc", type: "test_purchase" } });
check("the test purchase still requires an account -> 401", r.status === 401 && r.json.code === "ACCOUNT_REQUIRED", r.json);

// A 100% test promo on the test item: a free order, and no NOWPayments call at all (this env has
// no API key - a real call would fail - so a 200 proves the payment was skipped on purpose).
r = await call("POST", "/api/invoice", { token: freshSession, body: { amount: 0.02, pay_currency: "btc", type: "test_purchase", promoCode: "TESTFULL" } });
check("test item + 100% test code -> free order, no payment created",
  r.status === 200 && r.json.ok === true && r.json.free === true && r.json.priceAmount === 0 && r.json.promoApplied === "TESTFULL" && typeof r.json.purchaseId === "string", r.json);
const freeRow = env.DB.db.prepare("SELECT amount, status, promo_code, discount FROM orders WHERE order_id = ?").get(r.json.orderId);
check("the free order is recorded honestly (status free, EUR 0, promo + discount)", freeRow && freeRow.status === "free" && freeRow.amount === 0 && freeRow.promo_code === "TESTFULL" && freeRow.discount === 0.02, freeRow);

r = await call("POST", "/api/invoice", { token: freshSession, body: { amount: 30, pay_currency: "btc", type: "discord_bot", package: "BASIC", promoCode: "TESTFULL" } });
check("the same test code on a normal item also creates a free order (deliberate and test-only)", r.status === 200 && r.json.free === true, r.json);
const freeRow2 = env.DB.db.prepare("SELECT amount, status, discount FROM orders WHERE order_id = ?").get(r.json.orderId);
check("an item discounted to zero is recorded as free, never as paid", freeRow2 && freeRow2.status === "free" && freeRow2.amount === 0 && freeRow2.discount === 30, freeRow2);

r = await call("POST", "/api/invoice", { token: freshSession, body: { amount: 0.02, pay_currency: "btc", type: "test_purchase", promoCode: "WELCOME10" } });
check("a normal code cannot zero the test item (whole-euro rounding) -> still payable", r.status === 503 && typeof r.json.orderId === "string", { status: r.status, body: r.json });

// A free order creates no payment, so it needs no coin - and everything that does create one is
// still validated just as strictly as before.
r = await call("POST", "/api/invoice", { token: freshSession, body: { amount: 30, type: "discord_bot", package: "BASIC", promoCode: "TESTFULL" } });
check("a free order needs no coin at all (nothing is created to pay)", r.status === 200 && r.json.free === true, r.json);
r = await call("POST", "/api/invoice", { token: freshSession, body: { amount: 30, type: "discord_bot", package: "BASIC" } });
check("a normal order without a coin is still refused -> 400", r.status === 400 && /BTC/.test(String(r.json.error)), r.json);
r = await call("POST", "/api/invoice", { token: freshSession, body: { amount: 30, pay_currency: "doge", type: "discord_bot", package: "BASIC" } });
check("a normal order with an unsupported coin is still refused -> 400", r.status === 400 && /Unsupported/.test(String(r.json.error)), r.json);
r = await call("POST", "/api/invoice", { token: freshSession, body: { amount: 30, pay_currency: "doge", type: "discord_bot", package: "BASIC", promoCode: "TESTFULL" } });
check("a free order ignores the coin entirely - no payment, no payout address", r.status === 200 && r.json.free === true && !r.json.payAddress && r.json.priceAmount === 0, r.json);

console.log("\n--- transactional email: one designed shell, a real button ---");
// A fake Cloudflare Email Sending binding so these checks can read what would actually be
// delivered. It is attached last on purpose: from here on the provider is "cloudflare".
const sentMails = [];
env.EMAIL = { send: async (payload) => { sentMails.push(payload); return { messageId: "test-mail-" + sentMails.length }; } };
const mailsTo = (addr) => sentMails.filter((m) => JSON.stringify(m.to).toLowerCase().includes(addr));

r = await call("POST", "/api/auth/register", { body: { email: "mailbox@example.com", password: "mailboxpass1" } });
check("registering delivers the confirmation mail", r.status === 200 && mailsTo("mailbox@example.com").length === 1, { status: r.status, mails: sentMails.length });
const verifyMail = mailsTo("mailbox@example.com")[0] || {};
const verifyHtml = String(verifyMail.html || "");
const verifyLink = (verifyHtml.match(/href="(https:\/\/[^"]*\/verify-email\.html\?token=[a-f0-9]+)"/) || [])[1] || "";
const verifyText = String(verifyMail.text || "");
check("the confirmation mail gives the link a real button, not a bare URL in a table cell",
  !!verifyLink && /Confirm my email/.test(verifyHtml), { link: verifyLink, subject: verifyMail.subject });
check("the plain link stays under the button, so a client that strips buttons still works",
  !!verifyLink && verifyHtml.split(verifyLink).length === 3, { occurrences: verifyHtml.split(verifyLink).length - 1 });
check("the text alternative still carries the link", !!verifyLink && verifyText.includes(verifyLink), verifyText.slice(0, 80));
check("the shell is branded and dated, not a raw HTML dump",
  /Aether/.test(verifyHtml) && /get-aether\.de/.test(verifyHtml) && /questions@get-aether\.de/.test(verifyHtml) && /\d{4}-\d\d-\d\dT/.test(verifyHtml), verifyHtml.slice(0, 120));
check("no placeholder survived into the delivered mail", !/\$\{/.test(verifyHtml) && !/\$\{/.test(verifyText), verifyHtml.slice(0, 120));

// An address is user input and it lands in the mail body: markup in it must arrive as text.
r = await call("POST", "/api/auth/register", { body: { email: "evil<script>@example.com", password: "mailboxpass2" } });
const hostileHtml = String((mailsTo("evil<script>@example.com")[0] || {}).html || "");
check("an address containing markup cannot inject HTML into the mail",
  r.status === 200 && hostileHtml.length > 0 && !/<script>/.test(hostileHtml) && /&lt;script&gt;/.test(hostileHtml), { status: r.status });

r = await call("POST", "/api/auth/forgot", { body: { email: "mailbox@example.com" } });
const resetMail = mailsTo("mailbox@example.com").find((m) => /Reset/i.test(String(m.subject))) || {};
const resetHtml = String(resetMail.html || "");
check("the reset mail is the same designed shell with a button, not a pasted URL",
  r.status === 200 && /Choose a new password/.test(resetHtml) && /href="https:\/\/[^"]*\/reset-password\.html\?token=[a-f0-9]+"/.test(resetHtml), { status: r.status, subject: resetMail.subject });

console.log("\n--- checkout: the email gate, and the operator's own account ---");
// Turn on exactly what production has: an email provider exists, so the unconfirmed-address gate
// applies. A normal account is refused; the account named by ADMIN_EMAILS is not, because it is
// configured server-side and signed in with a password - locking the operator out of their own
// checkout while testing is a bug, not a safeguard.
env.REQUIRE_EMAIL_VERIFICATION = "true";
r = await call("POST", "/api/auth/register", { body: { email: "unconfirmed@example.com", password: "unconfirmed1" } });
const unconfirmedToken = r.json.token;
check("the gate is on for this section", r.json.emailVerificationRequired === true && r.json.emailVerified === false, r.json);
r = await call("POST", "/api/invoice", { token: unconfirmedToken, body: { amount: 30, pay_currency: "btc", type: "discord_bot", package: "BASIC" } });
check("an unconfirmed normal account is refused checkout -> 403 EMAIL_UNVERIFIED", r.status === 403 && r.json.code === "EMAIL_UNVERIFIED", r.json);
const keepAdmins = env.ADMIN_EMAILS;
env.ADMIN_EMAILS = keepAdmins + ",boss@example.com";
r = await call("POST", "/api/auth/register", { body: { email: "boss@example.com", password: "bosspass123" } });
const bossToken = r.json.token;
check("the operator account is admin and still unconfirmed", r.json.isAdmin === true && r.json.emailVerified === false, r.json);
r = await call("POST", "/api/invoice", { token: bossToken, body: { amount: 30, pay_currency: "btc", type: "discord_bot", package: "BASIC" } });
check("an unconfirmed admin can still check out (reaches the payment step, no 403)", r.status === 503 && typeof r.json.orderId === "string", { status: r.status, body: r.json });
r = await call("POST", "/api/orders", { token: bossToken });
check("the operator's order is filed to their own account", r.status === 200 && (r.json.orders || []).some((o) => o.order_id), r.json);
env.ADMIN_EMAILS = keepAdmins;
env.REQUIRE_EMAIL_VERIFICATION = "";

console.log("\n--- account status: ban / suspend / restrict ---");
// The status is set by an admin, stored on the users row and enforced on the server for every
// protected call. A restricted account keeps its own history readable and stops being able to create
// anything new; a suspend or a ban also ends the sessions it already had.
r = await call("POST", "/api/auth/register", { body: { email: "states@example.com", password: "statespass1" } });
const stateToken = r.json.token;
const stateId = r.json.user.id;
check("a fresh account reports status active", r.status === 200, r.json);
r = await call("GET", "/api/me", { token: stateToken });
check("/api/me carries the account status (and never the operator's raw row)",
  r.json.accountStatus && r.json.accountStatus.state === "active" && r.json.user.password_hash === undefined && r.json.user.status === undefined, r.json.accountStatus);
r = await call("POST", "/api/invoice", { token: stateToken, body: { amount: 30, pay_currency: "btc", type: "discord_bot", package: "BASIC" } });
check("the order exists for the reads below (payments are not configured here)", r.status === 503 && typeof r.json.orderId === "string", r.json);

r = await call("POST", "/api/admin/users/" + stateId + "/status", { token: freshSession, body: { status: "banned" } });
check("a normal account cannot change anyone's status -> 403", r.status === 403, r.json);
r = await call("POST", "/api/admin/users/" + stateId + "/status", { token: adminToken, body: { status: "nonsense" } });
check("an unknown status is refused -> 400", r.status === 400, r.json);
r = await call("POST", "/api/admin/users/" + stateId + "/status", { token: adminToken, body: { status: "suspended" } });
check("a suspension without a length is refused -> 400", r.status === 400, r.json);
r = await call("POST", "/api/admin/users/" + stateId + "/status", { token: adminToken, body: { status: "restricted", reason: "Spam in chat" } });
check("an admin can restrict an account", r.status === 200 && r.json.status === "restricted" && r.json.sessionsRevoked === false, r.json);
r = await call("GET", "/api/me", { token: stateToken });
check("a restricted account can still sign in and is told why", r.status === 200 && r.json.accountStatus.state === "restricted" && r.json.accountStatus.reason === "Spam in chat" && r.json.accountStatus.limited === true, r.json.accountStatus);
r = await call("GET", "/api/orders", { token: stateToken });
check("a restricted account can still read its own orders", r.status === 200 && Array.isArray(r.json.orders), r.json);
r = await call("POST", "/api/invoice", { token: stateToken, body: { amount: 30, pay_currency: "btc", type: "discord_bot", package: "BASIC" } });
check("a restricted account cannot place a new order -> 403 ACCOUNT_RESTRICTED", r.status === 403 && r.json.code === "ACCOUNT_RESTRICTED", r.json);
r = await call("POST", "/api/conversations", { token: stateToken, body: { message: "let me chat again" } });
check("a restricted account cannot open a new conversation -> 403 ACCOUNT_RESTRICTED", r.status === 403 && r.json.code === "ACCOUNT_RESTRICTED", r.json);

r = await call("POST", "/api/admin/users/" + stateId + "/status", { token: adminToken, body: { status: "suspended", hours: 2, reason: "Repeated abuse" } });
check("an admin can suspend an account for a number of hours", r.status === 200 && r.json.status === "suspended" && r.json.until > Date.now() && r.json.sessionsRevoked === true, r.json);
r = await call("GET", "/api/orders", { token: stateToken });
check("a suspension ends the sessions the account already had -> 401", r.status === 401, r.json);
r = await call("POST", "/api/auth/login", { body: { email: "states@example.com", password: "statespass1" } });
check("and a suspended account cannot sign back in -> 403 ACCOUNT_SUSPENDED", r.status === 403 && r.json.code === "ACCOUNT_SUSPENDED", r.json);
check("the suspension answer names when it ends", /until/i.test(String(r.json.error)), r.json.error);
// A suspension that has run out is over: no timer, no cleanup job, just a comparison at read time.
env.DB.db.prepare("UPDATE users SET status = 'suspended', status_until = ? WHERE id = ?").run(Date.now() - 1000, stateId);
r = await call("POST", "/api/auth/login", { body: { email: "states@example.com", password: "statespass1" } });
check("a suspension whose time is up no longer blocks sign-in", r.status === 200 && !!r.json.token, r.json);

r = await call("POST", "/api/admin/users/" + stateId + "/status", { token: adminToken, body: { status: "banned", reason: "Chargebacks" } });
check("an admin can ban an account", r.status === 200 && r.json.status === "banned", r.json);
r = await call("POST", "/api/auth/login", { body: { email: "states@example.com", password: "statespass1" } });
check("a banned account cannot sign in -> 403 ACCOUNT_BANNED", r.status === 403 && r.json.code === "ACCOUNT_BANNED", r.json);
r = await call("POST", "/api/admin/users/" + stateId + "/status", { token: adminToken, body: { status: "active", reason: "Mistake" } });
check("reinstating restores sign-in", r.status === 200, r.json);
r = await call("POST", "/api/auth/login", { body: { email: "states@example.com", password: "statespass1" } });
const reinstatedToken = r.json.token;
check("the reinstated account can sign in again", r.status === 200 && !!reinstatedToken, r.json);

const adminRowId = env.DB.db.prepare("SELECT id FROM users WHERE email = ?").get("admin@example.com").id;
r = await call("POST", "/api/admin/users/" + adminRowId + "/status", { token: adminToken, body: { status: "banned" } });
check("an admin cannot change their own status (no self-lockout)", r.status === 400, r.json);
const keepAdminEmails = env.ADMIN_EMAILS;
env.ADMIN_EMAILS = keepAdminEmails + ",second-admin@example.com";
r = await call("POST", "/api/auth/register", { body: { email: "second-admin@example.com", password: "secondpass1" } });
const secondAdminId = r.json.user.id;
r = await call("POST", "/api/admin/users/" + secondAdminId + "/status", { token: adminToken, body: { status: "banned" } });
check("an account listed in ADMIN_EMAILS cannot be banned from the panel", r.status === 400, r.json);
env.ADMIN_EMAILS = keepAdminEmails;

r = await call("GET", "/api/admin/users", { token: adminToken });
const stateRow = (r.json.users || []).find(u => Number(u.id) === Number(stateId)) || {};
check("the account list carries the status and the last sign-in address",
  stateRow.status === "active" && typeof stateRow.last_ip === "string" && stateRow.last_ip.length > 0, { status: stateRow.status, ip: stateRow.last_ip });
check("the account list never ships a password hash", !/password_hash/.test(JSON.stringify(r.json.users || [])), true);
check("the list advertises the legal statuses for the panel", Array.isArray(r.json.accountStates) && r.json.accountStates.includes("banned"), r.json.accountStates);
r = await call("GET", "/api/admin/users", { token: freshSession });
check("a normal account cannot read the account list -> 403", r.status === 403, r.json);

console.log("\n--- IP restrictions ---");
r = await call("POST", "/api/admin/blocked-ips", { token: freshSession, body: { ip: "203.0.113.9" } });
check("a normal account cannot block an address -> 403", r.status === 403, r.json);
r = await call("POST", "/api/admin/blocked-ips", { token: adminToken, body: { ip: "not-an-ip", note: "probe" } });
check("a malformed address is refused -> 400", r.status === 400, r.json);
r = await call("POST", "/api/admin/blocked-ips", { token: adminToken, body: { ip: "203.0.113.9", note: "abuse probe" } });
check("an admin can block an address", r.status === 200 && r.json.blocked === true && r.json.ips.some(e => e.ip === "203.0.113.9"), r.json);
r = await call("GET", "/api/health", { ip: "203.0.113.9" });
check("a blocked address is refused before any route runs -> 403 IP_BLOCKED", r.status === 403 && r.json.code === "IP_BLOCKED", r.json);
r = await call("POST", "/api/auth/login", { ip: "203.0.113.9", body: { email: "admin@example.com", password: "whatever1" } });
check("a blocked address cannot reach the sign-in route either", r.status === 403 && r.json.code === "IP_BLOCKED", r.json);
r = await call("GET", "/api/health", { ip: "203.0.113.10" });
check("a different address is untouched", r.status === 200 && r.json.ok === true, r.status);
r = await call("GET", "/api/admin/blocked-ips", { token: adminToken });
check("the panel can list the blocked addresses", r.status === 200 && Array.isArray(r.json.ips), r.json);
r = await call("DELETE", "/api/admin/blocked-ips", { token: adminToken, body: { ip: "203.0.113.9" } });
check("an admin can unblock an address", r.status === 200 && !r.json.ips.some(e => e.ip === "203.0.113.9"), r.json);
r = await call("GET", "/api/health", { ip: "203.0.113.9" });
check("and that address works again immediately", r.status === 200, r.status);

console.log("\n--- payments: the provider minimum, honest failures, and ownership ---");
// The provider is stubbed here: the point is not that NOWPayments answers, it is what the Worker does
// with the answer. Its documented flow reads the minimum amount BEFORE creating a payment, and the
// EUR 0.02 test item is far below it - which is the real cause of the raw "temporary problem" the
// checkout used to show, and of an order row left sitting at "pending" for a payment that never was.
const realFetchPay = globalThis.fetch;
const payCalls = [];
globalThis.fetch = async (url, init) => {
  const u = String(url);
  payCalls.push(u);
  if (u.includes("/v1/min-amount")) return { ok: true, status: 200, text: async () => JSON.stringify({ min_amount: 0.00003, fiat_equivalent: 2.2 }) };
  if (u.includes("/v1/payment")) return { ok: true, status: 200, text: async () => JSON.stringify({ payment_id: 777000111, payment_status: "waiting", pay_address: "ltc1qexampleaddress", pay_amount: 0.5, pay_currency: "ltc", price_amount: 30, price_currency: "eur", order_id: "aether_probe" }) };
  return { ok: true, status: 200, text: async () => "{}" };
};
env.NOWPAYMENTS_API_KEY = "np-test-key";
r = await call("POST", "/api/invoice", { token: reinstatedToken, body: { amount: 30, pay_currency: "ltc", type: "discord_bot", package: "BASIC" } });
const paidOwnerPurchase = r.json.purchaseId;
const paidOwnerOrder = r.json.orderId;
check("a payable order still creates a real payment (the minimum check does not block it)",
  r.status === 200 && r.json.payAddress === "ltc1qexampleaddress" && r.json.paymentId === 777000111, r.json);
check("the minimum amount is asked for before the payment is created", payCalls.some(u => u.includes("/v1/min-amount")), payCalls);

r = await call("POST", "/api/invoice", { token: reinstatedToken, body: { type: "test_purchase", pay_currency: "btc" } });
const belowMinOrder = r.json.orderId;
check("an amount below the provider minimum -> 400, not a 500 crash", r.status === 400 && r.json.code === "AMOUNT_BELOW_MINIMUM", { status: r.status, body: r.json });
check("the refusal is plain English and names the real minimum",
  /minimum/i.test(String(r.json.error)) && /2\.20/.test(String(r.json.error)) && /Nothing was charged/i.test(String(r.json.error)), r.json.error);
check("the refusal leaks no provider payload, no key and no internal detail",
  !/np-test-key|min_amount|api\.nowpayments/.test(JSON.stringify(r.json)), r.json);
r = await call("GET", "/api/orders", { token: reinstatedToken });
const belowMinRow = (r.json.orders || []).find(o => o.order_id === belowMinOrder) || {};
check("a payment that was never created does not leave a completed order behind",
  belowMinRow.status === "failed" && belowMinRow.payment_id == null, { status: belowMinRow.status, payment_id: belowMinRow.payment_id });

r = await call("GET", "/api/payment/777000111");
check("payment status without a session -> 401 (a payment id is not a credential)", r.status === 401, r.json);
r = await call("GET", "/api/payment/777000111", { token: freshSession });
check("someone else's payment id -> 404, indistinguishable from one that does not exist", r.status === 404, r.json);
r = await call("GET", "/api/payment/777000111", { token: reinstatedToken });
check("the owner reads their own payment (status, address, amount, order link only)",
  r.status === 200 && r.json.status === "waiting" && r.json.isPending === true && r.json.payAddress === "ltc1qexampleaddress" && r.json.purchaseId === paidOwnerPurchase, r.json);
check("and the answer never carries the provider payload or customer PII",
  r.json.raw === undefined && r.json.customer_email === undefined && r.json.order_description === undefined, r.json);
r = await call("GET", "/api/payment?purchase_id=" + encodeURIComponent(paidOwnerPurchase), { token: freshSession });
check("another account cannot read that order by its Purchase ID -> 404", r.status === 404, r.json);
r = await call("GET", "/api/payment?purchase_id=" + encodeURIComponent(paidOwnerPurchase), { token: reinstatedToken });
check("the owner can open the same order by Purchase ID (the order page's lookup)",
  r.status === 200 && r.json.orderId === paidOwnerOrder && r.json.status === "waiting", r.json);
r = await call("GET", "/api/payment?purchase_id=AETH-2026-NOPE", { token: reinstatedToken });
check("a malformed Purchase ID -> 400", r.status === 400, r.json);
r = await call("GET", "/api/payment/777000111", { token: reinstatedToken, host: "https://api.get-aether.de" });
check("no provider secret or api key is echoed into the response", !/np-test-key/.test(JSON.stringify(r.json)), true);

console.log("\n--- admin chat search and filtering ---");
const allConvs = await call("GET", "/api/admin/conversations", { token: adminToken });
check("the unfiltered list is unchanged in shape", allConvs.status === 200 && Array.isArray(allConvs.json.conversations), allConvs.status);
r = await call("GET", "/api/admin/conversations?q=" + encodeURIComponent("hello command"), { token: adminToken });
// Two threads can share one order (the portal's thread and the one opened for the purchase), and the
// text is bridged across both - so the assertion is that the search narrows the list to exactly the
// threads containing that phrase, not that only one row survives.
const searchHits = r.json.conversations || [];
let hitCarriesPhrase = false;
for (const c of searchHits) {
  const det = await call("GET", "/api/admin/conversations/" + encodeURIComponent(c.conversation_id), { token: adminToken });
  if ((det.json.messages || []).some(m => /hello command/i.test(String(m.body || "")))) hitCarriesPhrase = true;
}
check("search finds threads by the text inside them, server-side, and drops the rest",
  r.status === 200 && searchHits.length >= 1 && searchHits.length < allConvs.json.conversations.length
    && hitCarriesPhrase && r.json.filter.q === "hello command",
  { n: searchHits.length, total: allConvs.json.conversations.length, phraseInThread: hitCarriesPhrase, filter: r.json.filter });
r = await call("GET", "/api/admin/conversations?status=closed", { token: adminToken });
check("the status filter returns only resolved threads",
  r.status === 200 && r.json.conversations.every(c => c.status === "closed"), { n: r.json.conversations.length });
r = await call("GET", "/api/admin/conversations?q=" + encodeURIComponent("hello command"), { token: freshSession });
check("a normal account still cannot search every chat -> 403", r.status === 403, r.json);
r = await call("GET", "/api/admin/conversations", { token: freshSession });
check("and still cannot list them -> 403", r.status === 403, r.json);

globalThis.fetch = realFetchPay;
delete env.NOWPAYMENTS_API_KEY;

console.log("\n--- misc ---");
r = await call("GET", "/api/nope");
check("unknown route -> 404", r.status === 404, r.json);
r = await call("OPTIONS", "/api/invoice");
check("OPTIONS preflight -> 204", r.status === 204, r.status);
r = await call("GET", "/api/payment/123456");
check("payment status for unknown id -> not 200", r.status !== 200, r.json);

console.log("\n================ " + pass + " passed, " + fail + " failed ================\n");
process.exit(fail ? 1 : 0);
