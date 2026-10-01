import { DatabaseSync } from "node:sqlite";

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
      CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, email TEXT NOT NULL UNIQUE, password_hash TEXT NOT NULL, discord TEXT, created_at TEXT NOT NULL DEFAULT (datetime('now')), email_verified INTEGER DEFAULT 0, role TEXT DEFAULT 'user');
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
  PROMO_CODES: '{"WELCOME10":{"type":"percent","value":10},"SAVE5":{"type":"fixed","value":5}}',
  ADMIN_EMAILS: "admin@example.com",
};

async function call(method, path, opt = {}) {
  const headers = {
    origin: opt.origin === undefined ? "https://get-aether.de" : opt.origin,
    "cf-connecting-ip": opt.ip || ("10.0.0." + Math.floor(Math.random() * 250)),
  };
  if (opt.body !== undefined) headers["content-type"] = "application/json";
  if (opt.token) headers.authorization = "Bearer " + opt.token;
  if (opt.contentLength) headers["content-length"] = String(opt.contentLength);
  const res = await worker.fetch(new Request((opt.host || "https://api.get-aether.de") + path, {
    method, headers, body: opt.body === undefined ? undefined : JSON.stringify(opt.body),
  }), env, {});
  const text = await res.text();
  let json; try { json = JSON.parse(text); } catch { json = { raw: text }; }
  return { status: res.status, json, headers: res.headers, setCookie: res.headers.get("set-cookie") || "" };
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

let limited = false, limitedAt = 0;
for (let i = 1; i <= 15; i++) {
  const rr = await call("POST", "/api/auth/login", { ip: "203.0.113.7", body: { email: "member@example.com", password: "memberpass1" } });
  if (rr.status === 429) { limited = true; limitedAt = i; break; }
}
check("login brute force from one IP is rate limited", limited, { limitedAt });
r = await call("POST", "/api/auth/login", { ip: "203.0.113.8", body: { email: "member@example.com", password: "memberpass1" } });
check("a different IP is unaffected by that limit", r.status === 200, r.json);

r = await call("POST", "/api/auth/register", { body: { email: "weak@example.com", password: "onlyletters" } });
check("password without a digit -> 400", r.status === 400, r.json);

console.log("\n--- hardening: password reset / settings ---");
r = await call("POST", "/api/auth/forgot", { body: { email: "member@example.com" } });
check("forgot without an email provider -> honest 503, never a fake success", r.status === 503, r.json);
r = await call("POST", "/api/auth/forgot", { body: { email: "nobody-here@example.com" } });
check("forgot answer does not reveal whether the account exists", r.status === 503 && !/exists|not found|no account/i.test(JSON.stringify(r.json)), r.json);

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
r = await call("POST", "/api/admin/users/" + memberRow.id + "/role", { token: adminToken, body: { role: "tester" } });
check("admin grants Tester", r.status === 200 && r.json.role === "tester", r.json);
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
r = await call("POST", "/api/auth/resend-verification", {});
check("resend without a session -> 401", r.status === 401, r.json);
let resendLimited = false, resendStatuses = [];
for (let i = 0; i < 7; i++) { const rr = await call("POST", "/api/auth/resend-verification", { token: unverifiedToken }); resendStatuses.push(rr.status); if (rr.status === 429) { resendLimited = true; break; } }
check("verification resend is rate limited per account", resendLimited, resendStatuses);
const unvId = env.DB.db.prepare("SELECT id FROM users WHERE email = ?").get("unverified@example.com").id;
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

console.log("\n--- misc ---");
r = await call("GET", "/api/nope");
check("unknown route -> 404", r.status === 404, r.json);
r = await call("OPTIONS", "/api/invoice");
check("OPTIONS preflight -> 204", r.status === 204, r.status);
r = await call("GET", "/api/payment/123456");
check("payment status for unknown id -> not 200", r.status !== 200, r.json);

console.log("\n================ " + pass + " passed, " + fail + " failed ================\n");
process.exit(fail ? 1 : 0);
