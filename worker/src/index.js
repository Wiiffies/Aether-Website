/**
 * Aether -- Cloudflare Worker (api.get-aether.de)
 * - POST /api/order   -> validates + emails you + Discord webhook + auto-replies
 * - POST /api/invoice -> creates NOWPayments Payment + pending email + beautiful Discord, returns payAddress
 * - POST /api/ipn     -> NOWPayments IPN callback, verifies HMAC-512, emails + Discord
 * - GET  /api/health  -> {ok,resend,nowpayments,ipnSecret,discord,db}
 * - GET  /api/promo?code=&amount= -> validate promo (PROMO_CODES env JSON)
 * - GET  /api/payment/:paymentId  -> verify payment via NOWPayments (so success can't be faked)
 * - POST /api/auth/register | /api/auth/login | /api/auth/logout
 * - GET  /api/me, GET /api/orders, GET /api/orders/:orderId, POST /api/orders/:orderId/message
 *
 * - POST /api/auth/forgot | /api/auth/reset | /api/auth/password | /api/auth/verify-email
 *
 * Secrets (wrangler secret put): RESEND_API_KEY, NOWPAYMENTS_API_KEY, NOWPAYMENTS_IPN_SECRET(+_2),
 *   DISCORD_WEBHOOK_URL, TURNSTILE_SECRET (optional), TURNSTILE_LOGIN (optional flag)
 * Bindings: DB (D1, required), EMAIL (optional: Cloudflare Email Sending - preferred over RESEND_API_KEY)
 * Vars : CONTACT_TO, CONTACT_FROM, ALLOWED_ORIGIN (explicit origin allowlist, never "*"),
 *   RESET_DISCORD_EMAILS (optional: who may receive a reset link through the Discord webhook)
 *   SUCCESS_URL, CANCEL_URL, SITE_URL, PROMO_CODES (JSON), ADMIN_EMAILS, ALLOW_DEV_ORIGIN (local only)
 * D1 binding: DB (aether-db) - tables: users, sessions, orders, messages, auth_tokens, rate_limits
 * This file must stay ASCII-only: the deploy pipeline (see _build_chunks.mjs) aborts on bytes > 127.
 */

// ---------- helpers ----------
const JSON_HEADERS = { "content-type": "application/json; charset=utf-8" };

// ---------- security headers ----------
// The worker only ever returns JSON, so a locked-down CSP cannot break the site.
const SECURITY_HEADERS = {
  "x-content-type-options": "nosniff",
  "x-frame-options": "DENY",
  "content-security-policy": "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
  "referrer-policy": "no-referrer",
  "permissions-policy": "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()",
  "cross-origin-resource-policy": "same-site",
  "strict-transport-security": "max-age=31536000; includeSubDomains",
  "x-robots-tag": "noindex",
  "cache-control": "no-store",
};

// ---------- CORS ----------
// ALLOWED_ORIGIN is a comma-separated allowlist of site origins. "*" is deliberately
// ignored: a wildcard would let any website call this API with the visitor's session.
// Unset (or "*") falls back to the real Aether origins - never to a wildcard.
const DEFAULT_ORIGINS = ["https://get-aether.de", "https://www.get-aether.de", "https://api.get-aether.de"];
function allowedOrigins(env) {
  const raw = String(env.ALLOWED_ORIGIN || "").split(",")
    .map(s => s.trim().replace(/\/+$/, ""))
    .filter(s => s && s !== "*");
  return raw.length ? raw : DEFAULT_ORIGINS.slice();
}
function isDevOriginAllowed(env, origin) {
  // Local development only: ALLOW_DEV_ORIGIN=true must never be set on the live worker.
  if (String(env.ALLOW_DEV_ORIGIN || "").toLowerCase() !== "true") return false;
  return /^http:\/\/(127\.0\.0\.1|localhost|\[::1\]):\d+$/.test(String(origin || ""));
}
function isAllowedOrigin(env, origin) {
  const o = String(origin || "").replace(/\/+$/, "");
  if (!o) return false;
  if (allowedOrigins(env).includes(o)) return true;
  return isDevOriginAllowed(env, o);
}
// Browsers send Origin on cross-site requests and on every non-GET same-origin request.
// When it is present and untrusted we refuse the write instead of trusting a cookie.
function writeOriginAllowed(env, request) {
  const origin = request.headers.get("origin");
  if (origin) return isAllowedOrigin(env, origin);
  const referer = request.headers.get("referer");
  if (referer) { try { return isAllowedOrigin(env, new URL(referer).origin); } catch { return false; } }
  return true; // no browser metadata (curl / server-to-server webhook) - auth still applies
}
function isIpnPath(path){ return /\/ipn\/?$/.test(String(path||"")); }
const MAX_BODY_BYTES = 64 * 1024;
function bodyTooLarge(request){
  const len = Number(request.headers.get("content-length") || 0);
  return Number.isFinite(len) && len > MAX_BODY_BYTES;
}
const AETHER_LOGO = "https://get-aether.de/Aether%20Logo%20trasnparent%20new.png";

function corsHeaders(env, request) {
  const origin = request.headers.get("origin") || "";
  // Only allowlisted browser origins get CORS headers. Anything else gets none, so the
  // browser blocks the response - we never reflect an arbitrary Origin header.
  if (!origin || !isAllowedOrigin(env, origin)) return { "vary": "Origin" };
  const allowOrigin = origin;
  return {
    "access-control-allow-origin": allowOrigin,
    "access-control-allow-methods": "GET,POST,DELETE,OPTIONS",
    "access-control-allow-headers": "content-type,x-nowpayments-sig,authorization",
    "access-control-allow-credentials": "true",
    // set-cookie is HttpOnly and never exposed to JavaScript
    "access-control-max-age": "86400",
    "vary": "Origin",
  };
}
function json(data, status = 200, env, request, extraHeaders = {}) {
  const headers = { ...SECURITY_HEADERS, ...JSON_HEADERS, ...corsHeaders(env, request), ...extraHeaders };
  return new Response(JSON.stringify(data), { status, headers });
}
function escapeHtml(s) { return String(s).replace(/[&<>"']/g, c => ({ "&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;" }[c])); }
function escMd(s){ return String(s).replace(/[_*`~|>\\]/g, "\\$&"); }
function isEmail(s){ return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(String(s||"").trim()); }
function parseToList(env){
  const raw = env.CONTACT_TO || "questions@get-aether.de,business@get-aether.de";
  return raw.split(",").map(s=>s.trim()).filter(Boolean);
}
function orderRecipients(env, type, body){
  const meta = body && body.meta && typeof body.meta==="object" ? body.meta : {};
  const subj = String(meta.subject || body.subject || "").toLowerCase();
  const txt = String(body.description || "").toLowerCase();
  const isBusiness = type==="business"
    || body.business===true
    || /\b(business|opportunity|opportunities|partner|collab|sponsor)\b/.test(subj)
    || /\bbusiness opportunity\b/.test(txt);
  if(isBusiness) return ["business@get-aether.de"];
  if(type==="contact" && isBusiness) return ["business@get-aether.de"];
  return ["questions@get-aether.de"];
}

// ---------- outgoing mail ----------
// Two transports behind one call site. The Cloudflare Email Sending binding (`env.EMAIL`) wins when
// it is attached - it is native to the platform, needs no secret and has nothing to rotate -
// otherwise Resend is used with RESEND_API_KEY. With neither, mail is mocked into the Worker log
// and the caller is told; mail is never silently dropped. Callers never branch on the provider.
function emailProvider(env){
  if (env.EMAIL && typeof env.EMAIL.send === "function") return "cloudflare";
  if (env.RESEND_API_KEY) return "resend";
  return "none";
}
function emailConfigured(env){ return emailProvider(env) !== "none"; }
// Resend
async function sendEmail(env, { to, subject, html, text, replyTo }) {
  const key = env.RESEND_API_KEY;
  // Cloudflare Email Sending first: native, no secret. A failure falls through to Resend when that
  // is configured, and throws otherwise so the caller (see the reset-link delivery chain) can pick
  // another channel instead of pretending the mail went out.
  if (env.EMAIL && typeof env.EMAIL.send === "function") {
    try {
      const cfPayload = {
        from: env.CONTACT_FROM || "Aether <questions@get-aether.de>",
        to: Array.isArray(to) ? to : [to],
        subject,
        html: html || `<pre>${escapeHtml(text||"")}</pre>`,
        text: text || (html ? String(html).replace(/<[^>]+>/g,"") : ""),
      };
      if (replyTo && isEmail(replyTo)) cfPayload.reply_to = replyTo;
      const sent = await env.EMAIL.send(cfPayload);
      return { provider: "cloudflare", id: (sent && (sent.messageId || sent.id)) || null };
    } catch (e) {
      console.error("Cloudflare Email Sending failed", e && e.message);
      if (!key) throw new Error("Email failed: " + ((e && e.message) || "cloudflare email error"));
    }
  }
  if (!key) { console.log("[email:mock]", { to, subject }); return { mocked: true, provider: "none" }; }
  const from = env.CONTACT_FROM || "Aether <questions@get-aether.de>";
  const payload = {
    from, to: Array.isArray(to) ? to : [to], subject,
    html: html || `<pre>${escapeHtml(text||"")}</pre>`,
    text: text || (html ? (html.replace(/<[^>]+>/g,"") ?? "") : ""),
  };
  if (replyTo && isEmail(replyTo)) payload.reply_to = replyTo;
  const res = await fetch("https://api.resend.com/emails", {
    method: "POST", headers: { "authorization": `Bearer ${key}`, "content-type": "application/json" }, body: JSON.stringify(payload),
  });
  const body = await res.text();
  if (!res.ok) { console.error("Resend error", res.status, body); throw new Error(`Email failed: ${res.status} ${body}`); }
  try { return Object.assign({ provider: "resend" }, JSON.parse(body)); } catch { return { provider: "resend", raw: body }; }
}

// ---------- Discord webhook (beautified + resilient) ----------
const DISCORD_ACCENT = {
  pending: 0xffcc00,
  paid: 0x00c853,
  partially: 0xff8e00,
  failed: 0xff3b30,
  info: 0x4a8bff,
  new: 0xffffff,
};

function discordEmbed({ title, color, fields, description, footer, author }) {
  const e = {
    title: (title||"Aether \u2014 New order").slice(0,256),
    color: color ?? DISCORD_ACCENT.info,
    description: description ? String(description).slice(0,4000) : undefined,
    fields: (fields||[]).slice(0,25).map(([name,value,inline])=>({
      name: String(name).slice(0,256),
      value: String(value).slice(0,1024) || "\u200b",
      inline: inline !== undefined ? !!inline : String(value).length < 55,
    })),
    timestamp: new Date().toISOString(),
    footer: footer ? { text: String(footer).slice(0,2048), icon_url: AETHER_LOGO } : { text: "Aether \u2022 get-aether.de \u2022 " + new Date().toISOString(), icon_url: AETHER_LOGO },
  };
  if (author && author.name) e.author = { name: String(author.name).slice(0,256), icon_url: author.icon_url || AETHER_LOGO, url: author.url || undefined };
  e.thumbnail = { url: AETHER_LOGO };
  return [e];
}

async function sendDiscord(env, { content, embeds }) {
  const url = env.DISCORD_WEBHOOK_URL;
  if (!url) { console.log("[discord:mock]", { content, title: embeds && embeds[0] && embeds[0].title }); return { mocked: true }; }
  // 99999% reliability: retry with backoff, don't throw on single failure when called as fire-and-forget
  const body = JSON.stringify({ content: content ?? undefined, embeds: embeds ?? undefined, allowed_mentions: { parse: [] } });
  let lastErr = null;
  for (let attempt = 0; attempt < 3; attempt++) {
    if (attempt) await new Promise(r=>setTimeout(r, 400 * attempt + Math.random()*300));
    try {
      const res = await fetch(url, { method: "POST", headers: { "content-type": "application/json" }, body });
      const text = await res.text();
      if (!res.ok) { lastErr = new Error(`Discord webhook failed: ${res.status} ${text.slice(0,600)}`); console.error(lastErr.message); continue; }
      try { return JSON.parse(text); } catch { return { raw: text, ok: true }; }
    } catch (e) { lastErr = e; console.error("Discord webhook network error", e && e.message); }
  }
  if (lastErr) throw lastErr;
  throw new Error("Discord webhook failed after retries");
}

// ---------- helpers for promo ----------
function loadPromos(env) {
  const raw = env.PROMO_CODES;
  if (!raw) return {};
  try {
    const j = JSON.parse(raw);
    if (j && typeof j === "object") return j;
  } catch {}
  // also support comma-separated k:v like "WELCOME10=10%,SAVE5=5"
  return {};
}
function resolvePromo(env, code, amount) {
  const promos = loadPromos(env);
  if (!code) return { valid:false, error:"No code" };
  const c = String(code).trim().toUpperCase();
  const entry = promos[c] || promos[c.toLowerCase()];
  if (!entry) return { valid:false, error:"Invalid code" };
  let type, value;
  if (typeof entry === "object" && entry !== null) { type = String(entry.type||"").toLowerCase(); value = Number(entry.value); }
  else if (typeof entry === "number") { type = "percent"; value = entry; }
  else if (typeof entry === "string" && entry.endsWith("%")) { type = "percent"; value = Number(entry.slice(0,-1)); }
  else { type = "fixed"; value = Number(entry); }
  if (!Number.isFinite(value) || value <= 0) return { valid:false, error:"Invalid promo value" };
  if (type !== "percent" && type !== "fixed") type = value < 1 ? "percent" : "fixed";
  const amt = Number(amount)||0;
  let discount = 0;
  if (type === "percent") {
    if (value > 90) value = Math.min(value, 50); // safety cap if misconfigured
    discount = Math.round(amt * (value/100));
  } else {
    discount = Math.min(value, Math.max(0, amt - 1));
  }
  const finalAmount = Math.max(1, amt - discount);
  return { valid:true, code:c, type, value, discount, finalAmount, amount: amt };
}

// ---------- NOWPayments Payment API ----------
const FIXED_IPN_URL = "https://api.get-aether.de/api/ipn";
// (removed) hard-coded static payment links - payments are created per order via the API.
async function createNowPaymentsPayment(env, { amount, currency="eur", payCurrency, orderId, description }) {
  const key = env.NOWPAYMENTS_API_KEY;
  if (!key) throw new Error("NOWPAYMENTS_API_KEY not set");
  const pc = String(payCurrency || "").toLowerCase().trim();
  if (!pc) throw new Error("pay_currency required \u2014 choose BTC, ETH or LTC");
  if (!["btc","ltc","eth"].includes(pc)) throw new Error("Unsupported pay_currency \u2014 use btc, eth or ltc");
  const res = await fetch("https://api.nowpayments.io/v1/payment", {
    method: "POST", headers: { "x-api-key": key, "content-type": "application/json" },
    body: JSON.stringify({
      price_amount: Number(amount),
      price_currency: String(currency).toLowerCase(),
      pay_currency: pc,
      order_id: orderId,
      order_description: (description||`Aether order ${orderId}`).slice(0,200),
      ipn_callback_url: FIXED_IPN_URL,
    }),
  });
  const text = await res.text();
  let data; try{ data=JSON.parse(text);}catch{ data={ raw:text }; }
  if(!res.ok){ console.error("NOWPayments payment error", res.status, text); throw new Error((data && data.message ? data.message : undefined) || `NOWPayments error ${res.status}: ${text.slice(0,500)}`); }
  return data;
}
async function hmacSha512(secret, message){
  const enc=new TextEncoder();
  const key=await crypto.subtle.importKey("raw", enc.encode(secret), { name:"HMAC", hash:"SHA-512" }, false, ["sign"]);
  const sig=await crypto.subtle.sign("HMAC", key, enc.encode(message));
  return [...new Uint8Array(sig)].map(b=>b.toString(16).padStart(2,"0")).join("");
}
function sortedStringify(obj){
  if(obj===null||typeof obj!=="object") return JSON.stringify(obj);
  if(Array.isArray(obj)) return `[${obj.map(sortedStringify).join(",")}]`;
  const keys=Object.keys(obj).sort();
  return `{${keys.map(k=>`${JSON.stringify(k)}:${sortedStringify(obj[k])}`).join(",")}}`;
}
async function verifyIpnSignature(payloadObj, signature, secret){
  if(!secret||!signature) return false;
  const msg=sortedStringify(payloadObj);
  const expected=await hmacSha512(secret, msg);
  return signature.toLowerCase()===expected.toLowerCase();
}

// ---------- email template ----------
function orderHtml({ title, fields, note }){
  const rows=fields.map(([k,v])=>`
    <tr><td style="padding:8px 12px;border:1px solid #222;color:#9aa0a6;font:11px 'DM Mono',monospace;text-transform:uppercase;letter-spacing:.08em;width:160px">${escapeHtml(k)}</td>
        <td style="padding:8px 12px;border:1px solid #222;color:#e8eaed;font-size:13px;line-height:1.5;white-space:pre-wrap;word-break:break-word">${escapeHtml(v)}</td></tr>`).join("");
  return `
  <div style="background:#000;padding:24px;font-family:Manrope,Arial,sans-serif;color:#e8eaed">
    <div style="max-width:640px;margin:0 auto;background:#0b0b0c;border:1px solid #242529;border-radius:8px;overflow:hidden">
      <div style="padding:20px 22px;border-bottom:1px solid #242529"><div style="font:10px 'DM Mono',monospace;letter-spacing:.12em;text-transform:uppercase;color:#85878b">Aether</div>
      <div style="font-size:20px;font-weight:600;letter-spacing:-.04em;margin-top:8px">${escapeHtml(title)}</div></div>
      <table style="width:100%;border-collapse:collapse">${rows}</table>
      ${note? `<div style="padding:14px 22px;color:#9aa0a6;font-size:11px;line-height:1.6;border-top:1px solid #242529">${escapeHtml(note)}</div>` : ""}
      <div style="padding:12px 22px;color:#60636a;font:10px 'DM Mono',monospace">Sent via Aether Worker \u2022 ${new Date().toISOString()}</div>
    </div>
  </div>`;
}

// ---------- rate limiting ----------
// Layer 1: per-isolate 60s burst counter (instant, no I/O).
// Layer 2: D1 fixed-window counter shared by every isolate/colo, so a scripted client
// cannot win its budget back by reconnecting. If D1 is unavailable the check fails open
// (availability) but layer 1 still applies.
const RL = new Map();
function hitRL(ip, max = 12){
  const now=Date.now(); const arr=(RL.get(ip)||[]).filter(t=> now - t < 60_000);
  arr.push(now);
  if (RL.size > 20000) RL.clear(); // bounded memory on a hot isolate
  RL.set(ip, arr);
  return arr.length > max;
}
function clientIp(request){
  return request.headers.get("cf-connecting-ip")
    || (String(request.headers.get("x-forwarded-for")||"").split(",")[0].trim())
    || "unknown";
}
async function hitRateLimit(env, key, limit, windowMs){
  if (hitRL(key, Math.max(limit, 15))) return true;
  if (!env.DB) return false;
  const windowStart = Math.floor(Date.now()/windowMs)*windowMs;
  try {
    const row = await env.DB.prepare("SELECT count, window_start FROM rate_limits WHERE rl_key = ?").bind(key).first();
    let count = 1;
    if (row && Number(row.window_start) === windowStart) {
      count = Number(row.count) + 1;
      await env.DB.prepare("UPDATE rate_limits SET count = ? WHERE rl_key = ?").bind(count, key).run();
    } else {
      await env.DB.prepare("INSERT INTO rate_limits (rl_key, count, window_start) VALUES (?,?,?) ON CONFLICT(rl_key) DO UPDATE SET count = 1, window_start = excluded.window_start").bind(key, 1, windowStart).run();
    }
    if (Math.random() < 0.02) {
      env.DB.prepare("DELETE FROM rate_limits WHERE window_start < ?").bind(Date.now() - 86400000).run().catch(()=>{});
    }
    return count > limit;
  } catch(e){ console.warn("rate-limit store unavailable", e && e.message); return false; }
}
async function rlGuard(request, env, route, limit, windowMs){
  if (await hitRateLimit(env, route + ":" + clientIp(request), limit, windowMs)) {
    return json({ error:"Too many requests \u2014 please slow down and try again later." }, 429, env, request, { "retry-after": String(Math.ceil(windowMs/1000)) });
  }
  return null;
}
// ---------- Turnstile ----------
// Optional: enforced only when the TURNSTILE_SECRET worker secret exists. The token is
// always verified server-side against challenges.cloudflare.com - a frontend "success"
// flag is never trusted.
async function verifyTurnstile(env, request, token){
  const secret = env.TURNSTILE_SECRET;
  if (!secret) return { ok:true, skipped:true };
  if (!token) return { ok:false, error:"Captcha required \u2014 please complete the challenge and retry" };
  try {
    const form = new FormData();
    form.append("secret", secret);
    form.append("response", String(token).slice(0, 4096));
    const ip = clientIp(request);
    if (ip && ip !== "unknown") form.append("remoteip", ip);
    const res = await fetch("https://challenges.cloudflare.com/turnstile/v0/siteverify", { method:"POST", body: form });
    const data = await res.json().catch(()=>({}));
    if (data && data.success) return { ok:true };
    return { ok:false, error:"Captcha check failed \u2014 please try again" };
  } catch(e){
    console.error("turnstile verify failed", e && e.message);
    return { ok:true, skipped:true, error:null }; // fail open: never lock users out on a captcha outage
  }
}
async function turnstileGuard(request, env, body){
  if (!env.TURNSTILE_SECRET) return null;
  const token = (body && (body.turnstileToken || body.turnstile || body["cf-turnstile-response"]))
    || request.headers.get("cf-turnstile-response") || "";
  const r = await verifyTurnstile(env, request, token);
  if (r.ok) return null;
  return json({ error: r.error }, 403, env, request);
}

// ---------- auth helpers (D1) ----------
// Password hashing using PBKDF2 via SubtleCrypto (no external deps)
// workerd rejects PBKDF2 iteration counts above 100000 ("Pbkdf2 failed: iteration counts above 100000 are not supported"),
// so 100000 is the strongest value the Workers runtime will accept.
const PBKDF2_ITERATIONS = 100000;
function b64encode(buf){ return btoa(String.fromCharCode(...new Uint8Array(buf))); }
function b64decode(s){ return Uint8Array.from(atob(s), c=>c.charCodeAt(0)); }
async function hashPassword(password){
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(password), "PBKDF2", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits({ name:"PBKDF2", salt, iterations: PBKDF2_ITERATIONS, hash:"SHA-256" }, key, 32*8);
  return b64encode(salt) + "." + b64encode(bits);
}
async function verifyPassword(password, stored){
  try{
    const [saltB64, hashB64] = String(stored).split(".");
    if (!saltB64 || !hashB64) return false;
    const salt = b64decode(saltB64);
    const expected = b64decode(hashB64);
    const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(password), "PBKDF2", false, ["deriveBits"]);
    const bits = await crypto.subtle.deriveBits({ name:"PBKDF2", salt, iterations: PBKDF2_ITERATIONS, hash:"SHA-256" }, key, 32*8);
    const actual = new Uint8Array(bits);
    if (actual.length !== expected.length) return false;
    let diff=0; for(let i=0;i<actual.length;i++) diff|= actual[i]^expected[i];
    return diff===0;
  }catch{ return false; }
}
// ---------- sessions ----------
// D1 stores SHA-256(token) instead of the token itself, so a database leak cannot be
// replayed as a session. The cookie is HttpOnly + Secure; SameSite=Lax when the API is
// served first-party (get-aether.de/api/*) and SameSite=None only on the legacy
// cross-origin host api.get-aether.de, where the client also sends a Bearer token.
const SESSION_COOKIE = "__Host-aether_session";
const LEGACY_COOKIE = "aether_token";
const SESSION_DAYS = 14;
const SESSION_TTL_SEC = SESSION_DAYS * 24 * 3600;
async function sha256Hex(input){
  const buf = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(String(input)));
  return [...new Uint8Array(buf)].map(b=>b.toString(16).padStart(2,"0")).join("");
}
function newToken(){ return [...crypto.getRandomValues(new Uint8Array(32))].map(b=>b.toString(16).padStart(2,"0")).join(""); }
function requestHost(request){
  const h = String((request && request.headers.get("host")) || "").toLowerCase();
  if (h) return h;
  try { return new URL(request.url).hostname.toLowerCase(); } catch { return ""; }
}
function sessionCookie(token, maxAgeSec, request){
  // Same-origin (get-aether.de/api/*): Lax = first-party cookie + built-in CSRF protection.
  // Legacy cross-origin host (api.get-aether.de) needs None, where the request is signed
  // with a Bearer token instead, so a stray cookie can never authorize a write by itself.
  const sameSite = requestHost(request).startsWith("api.") ? "None" : "Lax";
  return `${SESSION_COOKIE}=${token}; Path=/; Max-Age=${maxAgeSec}; SameSite=${sameSite}; HttpOnly; Secure`;
}
function clearCookie(request){ return sessionCookie("", 0, request); }
// The passwords that top every breach corpus. Matching is an exact test against the lowercased
// password and against a punctuation-stripped form - deliberately never a substring test, so a
// password that merely *contains* a listed word ("wrongpassword", "notpassword1") is still fine.
// This runs when a password is CHOSEN (register / reset / change), never at login, so nobody who
// already holds one of these is locked out - their existing password keeps working until they pick
// a new one, and only then is the list applied.
const COMMON_PASSWORDS = new Set([
  "password", "password1", "password12", "password123", "password1234", "password12345",
  "passwort", "passwort1", "passwort123", "passw0rd", "p4ssword", "p@ssword1",
  "123456", "1234567", "12345678", "123456789", "1234567890", "12345678910",
  "00000000", "11111111", "22222222", "66666666", "88888888", "99999999",
  "qwerty", "qwerty1", "qwerty123", "qwerty1234", "qwertyuiop", "qwertz", "qwertz123",
  "1q2w3e4r", "1qaz2wsx", "qazwsx123", "zaq12wsx", "a1b2c3d4", "asdf1234", "asdfghjkl",
  "zxcvbnm1", "abc12345", "abcd1234", "1234abcd", "12345abc", "aaa12345",
  "admin", "admin1", "admin123", "admin1234", "administrator", "root1234", "default123",
  "changeme", "changeme1", "secret", "secret1", "secret123", "master123", "welcome1",
  "welcome123", "letmein", "letmein1", "trustno1", "whatever1", "test1234", "testpass1",
  "iloveyou", "sunshine1", "princess1", "football1", "baseball1", "starwars1", "superman1",
  "monkey123", "dragon123", "shadow123", "michael1", "jordan23", "hunter2",
  "aether", "aether1", "aether12", "aether123", "aether2025", "aether2026", "getaether",
  "get-aether", "aetherwebsite", "aetherpass1", "aetherpassword",
]);
function isCommonPassword(password){
  const s = String(password == null ? "" : password).toLowerCase();
  return COMMON_PASSWORDS.has(s) || COMMON_PASSWORDS.has(s.replace(/[^a-z0-9]/g, ""));
}
function passwordProblem(password){
  const s = String(password == null ? "" : password);
  if (s.length < 8) return "Password must be at least 8 characters";
  if (s.length > 200) return "Password is too long (200 characters max)";
  if (!/[A-Za-z]/.test(s) || !/[0-9]/.test(s)) return "Use at least one letter and one number";
  if (isCommonPassword(s)) return "That password is too common \u2014 please pick something less guessable";
  return "";
}
const MAX_SESSIONS_PER_USER = 10;
async function createSession(env, userId){
  const token = newToken();
  const hash = await sha256Hex(token);
  const expires = Date.now() + SESSION_TTL_SEC * 1000;
  await env.DB.prepare("INSERT INTO sessions (token, user_id, expires_at) VALUES (?, ?, ?)").bind(hash, userId, expires).run();
  await env.DB.prepare("DELETE FROM sessions WHERE user_id = ? AND token NOT IN (SELECT token FROM sessions WHERE user_id = ? ORDER BY expires_at DESC LIMIT ?)")
    .bind(userId, userId, MAX_SESSIONS_PER_USER).run().catch(()=>{});
  return { token, expires };
}
async function dropExpiredSessions(env){
  if (!env.DB) return;
  env.DB.prepare("DELETE FROM sessions WHERE expires_at < ?").bind(Date.now()).run().catch(()=>{});
  env.DB.prepare("DELETE FROM auth_tokens WHERE expires_at < ?").bind(Date.now()).run().catch(()=>{});
}
// ---------- single-use tokens (password reset + email verification) ----------
async function issueAuthToken(env, userId, purpose, ttlMs, payload){
  const token = newToken();
  const hash = await sha256Hex(token);
  await env.DB.prepare("DELETE FROM auth_tokens WHERE user_id = ? AND purpose = ?").bind(userId, purpose).run().catch(()=>{});
  try {
    // payload carries the pending change (e.g. the requested Beta host) - never a password.
    await env.DB.prepare("INSERT INTO auth_tokens (token_hash, user_id, purpose, expires_at, created_at, payload) VALUES (?,?,?,?,?,?)")
      .bind(hash, userId, purpose, Date.now() + ttlMs, Date.now(), payload == null ? null : String(payload).slice(0,2000)).run();
  } catch {
    await env.DB.prepare("INSERT INTO auth_tokens (token_hash, user_id, purpose, expires_at, created_at) VALUES (?,?,?,?,?)")
      .bind(hash, userId, purpose, Date.now() + ttlMs, Date.now()).run();
  }
  return token;
}
async function consumeAuthToken(env, token, purpose){
  if (!env.DB || !token) return null;
  const hash = await sha256Hex(String(token).trim());
  let row;
  try { row = await env.DB.prepare("SELECT token_hash, user_id, purpose, expires_at, payload FROM auth_tokens WHERE token_hash = ?").bind(hash).first(); }
  catch { row = await env.DB.prepare("SELECT token_hash, user_id, purpose, expires_at FROM auth_tokens WHERE token_hash = ?").bind(hash).first(); }
  if (!row || row.purpose !== purpose) return null;
  await env.DB.prepare("DELETE FROM auth_tokens WHERE token_hash = ?").bind(hash).run().catch(()=>{});
  if (Number(row.expires_at) < Date.now()) return null;
  return row;
}
function siteUrl(env){ return String(env.SITE_URL || "https://get-aether.de").replace(/\/+$/, ""); }
function getBearerOrCookie(request){
  const h = request.headers.get("authorization") || "";
  if (h.toLowerCase().startsWith("bearer ")) return h.slice(7).trim();
  const ck = request.headers.get("cookie") || "";
  for (const part of ck.split(";")) {
    const [k, ...rest] = part.trim().split("=");
    const name = k.trim();
    if (name === SESSION_COOKIE) return rest.join("=").trim();
  }
  for (const part of ck.split(";")) {
    const [k, ...rest] = part.trim().split("=");
    if (k.trim() === LEGACY_COOKIE) return rest.join("=").trim();
  }
  return "";
}
async function requireAuth(request, env){
  const token = getBearerOrCookie(request);
  if (!token) return null;
  if (!env.DB) return null;
  try {
    const now = Date.now();
    const hash = await sha256Hex(token);
    const row = await env.DB.prepare("SELECT token, user_id, expires_at FROM sessions WHERE token = ?").bind(hash).first();
    if (!row) return null;
    if (Number(row.expires_at) < now) { await env.DB.prepare("DELETE FROM sessions WHERE token = ?").bind(hash).run().catch(()=>{}); return null; }
    const user = await env.DB.prepare("SELECT id, email, discord, created_at FROM users WHERE id = ?").bind(row.user_id).first();
    if (!user) return null;
    // email_verified / role are optional (older databases may not have the columns yet)
    try { const v = await env.DB.prepare("SELECT email_verified FROM users WHERE id = ?").bind(row.user_id).first(); if (v) user.email_verified = Number(v.email_verified) || 0; } catch {}
    try { const v = await env.DB.prepare("SELECT role FROM users WHERE id = ?").bind(row.user_id).first(); if (v && v.role) user.role = v.role; } catch {}
    return { token: hash, user, expires_at: row.expires_at };
  } catch(e){ console.error("requireAuth failed", e && e.message); return null; }
}
function hasDb(env){ return !!env.DB; }

// ---------- admin ----------
// ADMIN_EMAILS is a comma-separated list of admin account emails (plain text var).
// ADMIN_EMAIL (singular) is also accepted so the project can name one primary admin.
// When both are empty there are simply no admins - no route leaks anything.
function adminEmails(env){
  return String(env.ADMIN_EMAILS || env.ADMIN_EMAIL || "").split(",").map(s=>s.trim().toLowerCase()).filter(Boolean);
}
function isAdminEmail(env, email){
  const list = adminEmails(env);
  if (!list.length) return false;
  return list.includes(String(email||"").trim().toLowerCase());
}
async function requireAdmin(request, env){
  const auth = await requireAuth(request, env);
  if (!auth) return null;
  if (!isAdminEmail(env, auth.user.email)) return null;
  return auth;
}
// A sensitive admin action additionally needs a confirmed email address. When no email
// provider is configured nothing can ever be confirmed, so the check stands down (it is
// reported to the UI instead of silently weakening anything).
function adminVerifiedGate(auth, env, request){
  if (!verificationRequired(env)) return null;
  if (isVerifiedUser(auth.user)) return null;
  return json({ error:"Confirm your admin email address before making this change.", code:"EMAIL_UNVERIFIED" }, 403, env, request);
}

// ---------- roles: User / Tester / Admin ----------
// The stored role lives in D1 (users.role). "admin" can ONLY be derived from the server-side
// ADMIN_EMAILS / ADMIN_EMAIL configuration - the API never grants or removes admin itself, so
// writing a row into the database can never escalate an account. No hidden admin accounts.
const ROLE_USER = "user", ROLE_TESTER = "tester", ROLE_ADMIN = "admin";
const ASSIGNABLE_ROLES = [ROLE_USER, ROLE_TESTER];
function storedRole(user){
  const r = String((user && user.role) || ROLE_USER).toLowerCase();
  return r === ROLE_TESTER ? ROLE_TESTER : ROLE_USER;
}
function effectiveRole(env, user){
  if (isAdminEmail(env, user && user.email)) return ROLE_ADMIN;
  return storedRole(user);
}
function publicUser(env, user){
  const role = effectiveRole(env, user);
  return {
    id: user.id, email: user.email, discord: user.discord || "", created_at: user.created_at,
    emailVerified: isVerifiedUser(user),
    role,
    isTester: role === ROLE_TESTER || role === ROLE_ADMIN,
    isAdmin: role === ROLE_ADMIN,
  };
}
function isTesterRole(env, user){
  const role = effectiveRole(env, user);
  return role === ROLE_TESTER || role === ROLE_ADMIN;
}

// ---------- email verification gate ----------
// Verification is enforced as soon as an email provider is configured (only then can a user
// actually complete the loop). REQUIRE_EMAIL_VERIFICATION can force it on or off explicitly.
// The state is always computed on the server; the frontend only renders it.
function verificationRequired(env){
  const flag = String(env.REQUIRE_EMAIL_VERIFICATION == null ? "" : env.REQUIRE_EMAIL_VERIFICATION).toLowerCase();
  if (["1","true","on","yes"].includes(flag)) return true;
  if (["0","false","off","no"].includes(flag)) return false;
  return !!env.RESEND_API_KEY;
}
function isVerifiedUser(user){ return !!Number((user && user.email_verified) || 0); }
const EMAIL_UNAVAILABLE_MSG = "Email delivery is not configured yet, so confirmation emails cannot be sent. Contact questions@get-aether.de and we will confirm your account.";
function unverifiedResponse(env, request){
  return json({ error:"Please verify your email address before continuing.", code:"EMAIL_UNVERIFIED", emailVerificationRequired:true }, 403, env, request);
}
function verifiedGate(auth, env, request){
  if (!verificationRequired(env)) return null;
  if (isVerifiedUser(auth.user)) return null;
  return unverifiedResponse(env, request);
}

// ---------- Purchase IDs (AETH-2026-XXXXXXXX) ----------
// Server-generated, never chosen by the browser, unique, and NOT an authentication token:
// every lookup still filters by the authenticated account.
const PURCHASE_ID_RE = /^AETH-\d{4}-[A-Z0-9]{8}$/;
// Alphabet without I/O/0/1 so a Purchase ID can be read out loud over support without confusion.
const PURCHASE_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
function randomPurchaseBody(){
  const bytes = crypto.getRandomValues(new Uint8Array(8));
  let out = "";
  for (let i = 0; i < 8; i++) out += PURCHASE_ALPHABET[bytes[i] % PURCHASE_ALPHABET.length];
  return out;
}
function newPurchaseId(year){ return "AETH-" + (year || new Date().getUTCFullYear()) + "-" + randomPurchaseBody(); }
async function uniquePurchaseId(env){
  const year = new Date().getUTCFullYear();
  for (let i = 0; i < 6; i++){
    const id = newPurchaseId(year);
    try { const hit = await env.DB.prepare("SELECT 1 AS x FROM orders WHERE purchase_id = ?").bind(id).first(); if (!hit) return id; }
    catch { return id; } // column missing (pre-migration): still return a well-formed id
  }
  return newPurchaseId(year);
}
// Orders created before Purchase IDs existed get one lazily, server-side.
async function ensurePurchaseId(env, order){
  if (!order) return "";
  if (order.purchase_id) return String(order.purchase_id);
  const id = await uniquePurchaseId(env);
  try {
    await env.DB.prepare("UPDATE orders SET purchase_id = ? WHERE order_id = ? AND purchase_id IS NULL").bind(id, order.order_id).run();
    order.purchase_id = id;
    return id;
  } catch { return ""; }
}
async function ensurePurchaseIds(env, orders){
  const list = Array.isArray(orders) ? orders : [];
  for (const o of list) { if (o && !o.purchase_id) { try { await ensurePurchaseId(env, o); } catch {} } }
  return list;
}
function normalizePurchaseId(v){
  const s = String(v == null ? "" : v).trim().toUpperCase();
  return PURCHASE_ID_RE.test(s) ? s : "";
}

// ---------- conversations (customer chat) ----------
// A conversation belongs to exactly one account. The owner is always taken from the session,
// never from the request, so /api/conversations/<someone else's id> is a 404 (no IDOR).
const CONVERSATION_STATUSES = ["open","answered","closed"];
function newConversationId(){ return "conv_" + [...crypto.getRandomValues(new Uint8Array(9))].map(b=>b.toString(16).padStart(2,"0")).join(""); }
function safeStatus(s){ const v = String(s || "open").toLowerCase(); return CONVERSATION_STATUSES.includes(v) ? v : "open"; }
// Chat is untrusted input: strip control characters, normalise newlines, hard length cap.
function cleanMessage(s){
  return String(s == null ? "" : s)
    .replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/g, "")
    .replace(/\r\n/g, "\n").slice(0, 4000).trim();
}
function conversationSubject(s){ return String(s == null ? "" : s).replace(/[\u0000-\u001F\u007F]/g, " ").slice(0, 120).trim(); }
async function conversationsReady(env){
  if (!env.DB) return false;
  try { await env.DB.prepare("SELECT 1 AS x FROM conversations LIMIT 1").first(); return true; } catch { return false; }
}
async function touchConversation(env, conversationId){
  try { await env.DB.prepare("UPDATE conversations SET updated_at = ? WHERE conversation_id = ?").bind(new Date().toISOString(), conversationId).run(); } catch {}
}
function publicConversation(c){
  if (!c) return null;
  return {
    conversation_id: c.conversation_id,
    order_id: c.order_id || "",
    purchase_id: c.purchase_id || "",
    subject: c.subject || "",
    status: safeStatus(c.status),
    assigned: c.assigned_admin ? "Aether support" : "",
    created_at: c.created_at || "",
    updated_at: c.updated_at || c.created_at || "",
  };
}
// Legacy order chats (messages keyed only by order_id) are surfaced as conversations with a
// deterministic id, so the portal shows one continuous history without rewriting old rows.
async function conversationForOrder(env, order){
  const cid = "conv_" + String(order.order_id).replace(/[^A-Za-z0-9_-]/g, "");
  try {
    await env.DB.prepare("INSERT INTO conversations (conversation_id, user_id, order_id, purchase_id, subject, status, created_at, updated_at) VALUES (?,?,?,?,?,'open',?,?) ON CONFLICT(conversation_id) DO NOTHING")
      .bind(cid, order.user_id == null ? null : order.user_id, String(order.order_id || ""), order.purchase_id || null, orderSubject(order), new Date().toISOString(), new Date().toISOString()).run();
  } catch {}
  return await env.DB.prepare("SELECT * FROM conversations WHERE conversation_id = ?").bind(cid).first().catch(()=>null);
}
function orderSubject(order){
  const item = String((order && (order.package || order.type)) || "order").replace(/_/g, " ");
  return conversationSubject(item + (order && order.purchase_id ? " - " + order.purchase_id : ""));
}
async function ensureOrderConversations(env, userId){
  const rows = await env.DB.prepare("SELECT o.order_id, o.purchase_id, o.type, o.package, o.user_id, o.created_at FROM orders o WHERE o.user_id = ? AND EXISTS (SELECT 1 AS m FROM messages m WHERE m.order_id = o.order_id) ORDER BY o.id DESC LIMIT 25").bind(userId).all().catch(()=>({ results: [] }));
  for (const o of (rows.results || [])) { try { await conversationForOrder(env, o); } catch {} }
  return rows.results || [];
}
// Reads a conversation thread: new-style rows carry conversation_id, legacy order rows only
// carry order_id. Both are returned for the owning account (never for anyone else).
async function conversationMessages(env, conv){
  let rows;
  try {
    rows = await env.DB.prepare("SELECT id, sender, body, created_at FROM messages WHERE conversation_id = ? OR (order_id IS NOT NULL AND order_id != '' AND order_id = ?) ORDER BY id ASC LIMIT 500")
      .bind(conv.conversation_id, conv.order_id || "").all();
  } catch {
    rows = await env.DB.prepare("SELECT id, sender, body, created_at FROM messages WHERE order_id = ? ORDER BY id ASC LIMIT 500").bind(conv.order_id || "").all();
  }
  return (rows.results || []).map(m => ({ sender: m.sender === "admin" ? "admin" : "customer", body: String(m.body || ""), created_at: m.created_at }));
}
async function insertConversationMessage(env, conv, sender, text, userId){
  const cid = conv && conv.conversation_id ? conv.conversation_id : null;
  try {
    await env.DB.prepare("INSERT INTO messages (conversation_id, order_id, user_id, sender, body) VALUES (?, ?, ?, ?, ?)")
      .bind(cid, conv && conv.order_id ? conv.order_id : "", userId == null ? null : userId, sender, text).run();
  } catch {
    await env.DB.prepare("INSERT INTO messages (order_id, user_id, sender, body) VALUES (?, ?, ?, ?)")
      .bind(conv && conv.order_id ? conv.order_id : "", userId == null ? null : userId, sender, text).run();
  }
  await touchConversation(env, cid);
}

// ---------- settings + Beta configuration ----------
// The Beta hostname/path is server-side configuration (D1 settings table, env fallback) so it
// can be changed from the admin panel without editing source code. The obscure path only
// reduces accidental discovery - it is NOT a security mechanism: Tester role + verified email
// + a valid session are enforced on every Beta API call.
const DEFAULT_BETA_HOST = "betatester.get-aether.de";
const DEFAULT_BETA_PATH = "/yesthisistheofficaldomainanditssolongsopeopledontaccidentlyfindthis";
const BETA_HOST_RE = /^(?=.{4,253}$)(?!-)[a-z0-9-]{1,63}(?:\.[a-z0-9-]{1,63})*\.get-aether\.de$/;
const BETA_PATH_RE = /^\/[A-Za-z0-9/_\-.]{7,200}$/;
function validBetaHost(h){ const s = String(h == null ? "" : h).trim().toLowerCase(); return BETA_HOST_RE.test(s) ? s : ""; }
function validBetaPath(p){ const s = String(p == null ? "" : p).trim(); return BETA_PATH_RE.test(s) ? s : ""; }
const BETA_DOMAIN_TTL_MS = 30 * 60 * 1000;
async function getSetting(env, key){
  if (!env.DB) return "";
  try { const row = await env.DB.prepare("SELECT value FROM settings WHERE key = ?").bind(key).first(); return row && row.value != null ? String(row.value) : ""; }
  catch { return ""; }
}
async function setSetting(env, key, value){
  await env.DB.prepare("INSERT INTO settings (key, value, updated_at) VALUES (?,?,?) ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at")
    .bind(key, String(value), new Date().toISOString()).run();
}
async function betaConfig(env){
  const host = validBetaHost(env.BETA_HOST) || await getSetting(env, "beta_host") || DEFAULT_BETA_HOST;
  const path = validBetaPath(env.BETA_PATH) || await getSetting(env, "beta_path") || DEFAULT_BETA_PATH;
  return { host, path, url: "https://" + host + path };
}
// Feature flags are evaluated server-side; the frontend may render them but the API never
// trusts a client-supplied flag.
function betaFlags(env){
  const defaults = { betaNewChat: true, betaDashboard: true, betaTools: true };
  const raw = env.BETA_FLAGS;
  if (!raw) return defaults;
  try { const j = JSON.parse(String(raw)); if (j && typeof j === "object") return Object.assign({}, defaults, j); } catch {}
  return defaults;
}
// Tester gate: session + (verified email when verification is enforced) + Tester/Admin role.
async function requireTester(request, env){
  const auth = await requireAuth(request, env);
  if (!auth) return null;
  if (verificationRequired(env) && !isVerifiedUser(auth.user)) return null;
  if (!isTesterRole(env, auth.user)) return null;
  return auth;
}

// ---------- audit log ----------
// Administrative and sensitive actions are recorded server-side (who, what, when).
async function audit(env, actor, action, target, detail){
  try {
    await env.DB.prepare("INSERT INTO audit_log (actor_user_id, actor_email, action, target, detail, created_at) VALUES (?,?,?,?,?,?)")
      .bind(actor && actor.id != null ? actor.id : null, actor && actor.email ? String(actor.email) : "", String(action).slice(0,80), String(target || "").slice(0,200), String(detail || "").slice(0,1000), new Date().toISOString()).run();
  } catch {}
}

// ---------- email verification: resend ----------
// Protected by the session (never by an email address alone) plus a per-account window, so the
// endpoint cannot be used to spam a mailbox or to discover which accounts exist.
async function handleResendVerification(request, env){
  if (!hasDb(env)) return json({ error:"Accounts not configured" }, 503, env, request);
  const auth = await requireAuth(request, env);
  if (!auth) return json({ error:"Sign in first and we can resend your confirmation email." }, 401, env, request);
  if (isVerifiedUser(auth.user)) return json({ ok:true, emailVerified:true, message:"Your email is already confirmed." }, 200, env, request);
  if (!emailConfigured(env)) return json({ error: EMAIL_UNAVAILABLE_MSG }, 503, env, request);
  if (await hitRateLimit(env, "resend:" + auth.user.id, 4, 3600000))
    return json({ error:"A confirmation email was sent recently - check your inbox and spam folder first." }, 429, env, request);
  const token = await issueAuthToken(env, auth.user.id, "verify", 24*3600*1000);
  const link = `${siteUrl(env)}/verify-email.html?token=${token}`;
  try {
    await sendEmail(env, { to: auth.user.email, subject:"Confirm your Aether account", html: orderHtml({ title:"Confirm your email", fields:[["Account", auth.user.email],["Link", link]], note:"This link is valid for 24 hours and single use. If you did not create this account you can ignore this email." }), text:`Confirm your Aether account:\n${link}\n\nValid for 24 hours.` });
  } catch(e){
    console.error("resend verification failed", e && e.message);
    return json({ error:"The email could not be sent right now - try again in a few minutes, or contact questions@get-aether.de." }, 502, env, request);
  }
  return json({ ok:true, emailVerified:false, message:"Confirmation email sent to " + auth.user.email + "." }, 200, env, request);
}

// ---------- purchases (safe Purchase ID lookup) ----------
// A Purchase ID is an identifier, not a credential: the lookup still filters by the session's
// account, so knowing (or guessing) someone else's Purchase ID reveals nothing.
function publicPurchase(o){
  if (!o) return null;
  return {
    purchase_id: o.purchase_id || "",
    order_id: o.order_id,
    amount: o.amount, currency: o.currency, type: o.type, package: o.package,
    description: o.description, status: o.status, created_at: o.created_at,
    payment_id: o.payment_id || "",
  };
}
async function handlePurchases(request, env, url){
  const auth = await requireAuth(request, env);
  if (!auth) return json({ error:"Not authenticated" }, 401, env, request);
  if (!hasDb(env)) return json({ error:"Accounts not configured" }, 503, env, request);
  const gate = verifiedGate(auth, env, request);
  if (gate) return gate;
  const m = url.pathname.match(/^\/api\/purchases\/([^\/]+)\/?$/);
  const raw = m ? decodeURIComponent(m[1]) : String(url.searchParams.get("purchase_id") || url.searchParams.get("purchaseId") || "");
  const pid = normalizePurchaseId(raw);
  if (!pid) return json({ error:"Invalid Purchase ID - the format is AETH-YYYY-XXXXXXXX" }, 400, env, request);
  const ord = await env.DB.prepare("SELECT * FROM orders WHERE purchase_id = ? AND user_id = ?").bind(pid, auth.user.id).first();
  if (!ord) return json({ error:"Purchase not found" }, 404, env, request);
  const conv = await conversationForOrder(env, ord).catch(()=>null);
  return json({ ok:true, purchase: publicPurchase(ord), conversation: publicConversation(conv) }, 200, env, request);
}

// ---------- conversations (customer chat history) ----------
const MSG_MATCH = "(m.conversation_id = c.conversation_id OR (c.order_id IS NOT NULL AND c.order_id != '' AND m.order_id = c.order_id))";
function conversationListRow(c){
  return {
    conversation_id: c.conversation_id,
    subject: c.subject || "",
    status: safeStatus(c.status),
    purchase_id: c.purchase_id || "",
    order_id: c.order_id || "",
    created_at: c.created_at,
    updated_at: c.updated_at || c.created_at,
    message_count: Number(c.message_count || 0),
    last_body: c.last_body ? String(c.last_body).slice(0,160) : "",
    last_sender: c.last_sender || "",
    last_at: c.last_at || "",
  };
}
// Support notification for a new customer message (best effort - never fails the request).
async function notifySupportChat(env, auth, conv, text){
  const at = new Date().toLocaleString("en-GB", { timeZone:"Europe/Berlin", dateStyle:"long", timeStyle:"short" });
  const label = conv.purchase_id || conv.order_id || conv.conversation_id;
  try {
    await Promise.all([
      sendEmail(env, {
        to: "questions@get-aether.de",
        subject:`[CHAT] ${label} - ${auth.user.email}`,
        html: orderHtml({ title:`New customer message - ${label}`, fields:[["Conversation", conv.conversation_id],["Purchase", conv.purchase_id || "-"],["From", auth.user.email],["Discord", auth.user.discord || "-"],["Message", text],["At", at]] }),
        text:`New message for ${label} from ${auth.user.email}:\n\n${text}`,
        replyTo: auth.user.email,
      }).catch(()=>null),
      sendDiscord(env, { embeds: discordEmbed({ title:`\uD83D\uDCAC Customer chat - ${label}`, color: DISCORD_ACCENT.info, fields:[["Purchase", conv.purchase_id || "-", true],["From", auth.user.email, true],["Message", text.slice(0,900), false]], description:`**${escMd(auth.user.email)}** \u2192 ${escMd(text.slice(0,400))}`, footer:`Chat \u2022 ${at}`, author:{ name:"Aether Chat" } }) }).catch(()=>null),
    ]);
  } catch {}
}
async function handleConversations(request, env, url){
  const auth = await requireAuth(request, env);
  if (!auth) return json({ error:"Not authenticated" }, 401, env, request);
  if (!hasDb(env)) return json({ error:"Accounts not configured" }, 503, env, request);
  if (!(await conversationsReady(env))) return json({ error:"Chat storage is being upgraded - please try again in a moment." }, 503, env, request);
  const gate = verifiedGate(auth, env, request);
  if (gate) return gate;
  const single = url.pathname.match(/^\/api\/conversations\/([^\/]+)\/?$/);
  if (single && request.method === "GET") {
    const conv = await env.DB.prepare("SELECT * FROM conversations WHERE conversation_id = ? AND user_id = ?").bind(decodeURIComponent(single[1]), auth.user.id).first();
    if (!conv) return json({ error:"Conversation not found" }, 404, env, request);
    return json({ ok:true, conversation: publicConversation(conv), messages: await conversationMessages(env, conv) }, 200, env, request);
  }
  if (request.method === "POST" && !single) {
    let body; try{ body = await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
    const text = cleanMessage(body.message || body.body || body.text || "");
    if (text.length < 2) return json({ error:"Write a slightly longer first message." }, 400, env, request);
    // Optional purchase association: the Purchase ID must belong to this account.
    const rawPurchase = String(body.purchase_id || body.purchaseId || "").trim();
    const purchaseId = normalizePurchaseId(rawPurchase);
    if (rawPurchase && !purchaseId) return json({ error:"That Purchase ID looks wrong - use the format AETH-YYYY-XXXXXXXX" }, 400, env, request);
    let orderId = "";
    let orderRow = null;
    if (purchaseId) {
      orderRow = await env.DB.prepare("SELECT * FROM orders WHERE purchase_id = ? AND user_id = ?").bind(purchaseId, auth.user.id).first();
      if (!orderRow) return json({ error:"No purchase with that ID belongs to your account." }, 404, env, request);
      orderId = String(orderRow.order_id || "");
    }
    const subject = conversationSubject(body.subject || body.topic || "") || (orderRow ? orderSubject(orderRow) : "Support request");
    const convId = newConversationId();
    const now = new Date().toISOString();
    await env.DB.prepare("INSERT INTO conversations (conversation_id, user_id, order_id, purchase_id, subject, status, created_at, updated_at) VALUES (?,?,?,?,?,'open',?,?)")
      .bind(convId, auth.user.id, orderId, purchaseId || null, subject, now, now).run();
    const conv = { conversation_id: convId, order_id: orderId, purchase_id: purchaseId, subject, status: "open", created_at: now, updated_at: now };
    await insertConversationMessage(env, conv, "customer", text, auth.user.id);
    await audit(env, auth.user, "conversation.create", convId, orderId ? "order " + orderId : (purchaseId || "general"));
    await notifySupportChat(env, auth, conv, text);
    return json({ ok:true, conversation: publicConversation(conv) }, 200, env, request);
  }
  // list: legacy order chats are surfaced as conversations (idempotent), then everything the
  // account owns is returned newest-first.
  await ensureOrderConversations(env, auth.user.id);
  const rows = await env.DB.prepare(`SELECT c.*,
      (SELECT COUNT(*) FROM messages m WHERE ${MSG_MATCH}) AS message_count,
      (SELECT m.body FROM messages m WHERE ${MSG_MATCH} ORDER BY m.id DESC LIMIT 1) AS last_body,
      (SELECT m.sender FROM messages m WHERE ${MSG_MATCH} ORDER BY m.id DESC LIMIT 1) AS last_sender,
      (SELECT m.created_at FROM messages m WHERE ${MSG_MATCH} ORDER BY m.id DESC LIMIT 1) AS last_at
    FROM conversations c WHERE c.user_id = ? ORDER BY COALESCE(c.updated_at, c.created_at) DESC LIMIT 100`).bind(auth.user.id).all();
  return json({ ok:true, conversations: (rows.results || []).map(conversationListRow) }, 200, env, request);
}
async function handleConversationAction(request, env, url){
  const auth = await requireAuth(request, env);
  if (!auth) return json({ error:"Not authenticated" }, 401, env, request);
  if (!hasDb(env)) return json({ error:"Accounts not configured" }, 503, env, request);
  if (!(await conversationsReady(env))) return json({ error:"Chat storage is being upgraded - please try again in a moment." }, 503, env, request);
  const gate = verifiedGate(auth, env, request);
  if (gate) return gate;
  const msgMatch = url.pathname.match(/^\/api\/conversations\/([^\/]+)\/messages\/?$/);
  const statusMatch = url.pathname.match(/^\/api\/conversations\/([^\/]+)\/status\/?$/);
  if (!msgMatch && !statusMatch) return json({ error:"Not found" }, 404, env, request);
  if (request.method !== "POST") return json({ error:"Method not allowed" }, 405, env, request);
  const cid = decodeURIComponent((msgMatch || statusMatch)[1]);
  // Ownership is checked first; another account's id is indistinguishable from a missing one.
  const conv = await env.DB.prepare("SELECT * FROM conversations WHERE conversation_id = ? AND user_id = ?").bind(cid, auth.user.id).first();
  if (!conv) return json({ error:"Conversation not found" }, 404, env, request);
  let body; try{ body = await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
  if (statusMatch) {
    const next = String(body.status || "").toLowerCase();
    if (!CONVERSATION_STATUSES.includes(next)) return json({ error:"Unknown status" }, 400, env, request);
    await env.DB.prepare("UPDATE conversations SET status = ?, updated_at = ? WHERE conversation_id = ? AND user_id = ?").bind(next, new Date().toISOString(), cid, auth.user.id).run();
    await audit(env, auth.user, "conversation.status", cid, next);
    return json({ ok:true, status: next }, 200, env, request);
  }
  const text = cleanMessage(body.body || body.message || body.text || "");
  if (text.length < 2) return json({ error:"Message too short" }, 400, env, request);
  if (safeStatus(conv.status) === "closed") return json({ error:"This conversation is closed - start a new one if you need more help." }, 400, env, request);
  await insertConversationMessage(env, conv, "customer", text, auth.user.id);
  await env.DB.prepare("UPDATE conversations SET status = 'open' WHERE conversation_id = ?").bind(cid).run().catch(()=>{});
  await notifySupportChat(env, auth, conv, text);
  return json({ ok:true, messages: await conversationMessages(env, conv) }, 200, env, request);
}

// ---------- Beta (Tester-only) ----------
// Access needs: a session, a verified email (when verification is enforced) and the Tester or
// Admin role. The obscure Beta path only reduces accidental discovery - it is not security.
async function handleBetaAccess(request, env){
  const auth = await requireAuth(request, env);
  if (!auth) return json({ ok:false, authenticated:false, allowed:false }, 401, env, request);
  const allowed = isTesterRole(env, auth.user) && (!verificationRequired(env) || isVerifiedUser(auth.user));
  let beta = null;
  if (allowed) { const cfg = await betaConfig(env); beta = { url: cfg.url, host: cfg.host }; }
  return json({ ok:true, authenticated:true, allowed, role: effectiveRole(env, auth.user), emailVerified: isVerifiedUser(auth.user), emailVerificationRequired: verificationRequired(env), beta }, 200, env, request);
}
async function handleBetaStatus(request, env){
  const auth = await requireTester(request, env);
  if (!auth) return json({ error:"Beta access requires a Tester account with a verified email." }, 403, env, request);
  const cfg = await betaConfig(env);
  const flags = await betaFlagsFor(env);
  let feedback = 0;
  try { const row = await env.DB.prepare("SELECT COUNT(*) AS n FROM beta_feedback").first(); feedback = Number(row && row.n) || 0; } catch {}
  return json({
    ok:true,
    beta:{ host: cfg.host, path: cfg.path, url: cfg.url, env: String(env.AETHER_ENV || "production") },
    flags,
    features: Object.keys(flags).filter(k => !!flags[k]),
    role: effectiveRole(env, auth.user),
    user: { email: auth.user.email },
    feedback_count: feedback,
  }, 200, env, request);
}
async function handleBetaFeedback(request, env){
  const auth = await requireTester(request, env);
  if (!auth) return json({ error:"Beta access requires a Tester account with a verified email." }, 403, env, request);
  if (!hasDb(env)) return json({ error:"Beta storage is not configured" }, 503, env, request);
  let body; try{ body = await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
  const text = cleanMessage(body.message || body.body || "");
  if (text.length < 2) return json({ error:"Write a little more first." }, 400, env, request);
  const area = conversationSubject(body.area || "general") || "general";
  try {
    await env.DB.prepare("INSERT INTO beta_feedback (user_id, email, area, body, created_at) VALUES (?,?,?,?,?)")
      .bind(auth.user.id, auth.user.email, area, text, new Date().toISOString()).run();
  } catch { return json({ error:"Beta feedback storage is not configured" }, 503, env, request); }
  await audit(env, auth.user, "beta.feedback", area, text.slice(0,200));
  return json({ ok:true, message:"Thanks - beta feedback stored." }, 200, env, request);
}
// A Beta deployment on another hostname cannot read the production cookie, so a Tester can mint
// a short-lived single-use ticket and redeem it there for a normal session.
async function handleBetaTicket(request, env){
  const auth = await requireTester(request, env);
  if (!auth) return json({ error:"Beta access requires a Tester account with a verified email." }, 403, env, request);
  const cfg = await betaConfig(env);
  const ticket = await issueAuthToken(env, auth.user.id, "beta", 120*1000, JSON.stringify({ host: cfg.host }));
  await audit(env, auth.user, "beta.ticket", cfg.host, "single-use, 120s");
  return json({ ok:true, ticket, expiresIn:120, beta:{ host: cfg.host, path: cfg.path, url: cfg.url } }, 200, env, request);
}
async function handleBetaRedeem(request, env){
  if (!hasDb(env)) return json({ error:"Accounts not configured" }, 503, env, request);
  let body; try{ body = await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
  const row = await consumeAuthToken(env, String(body.ticket || "").trim(), "beta");
  if (!row) return json({ error:"This beta ticket is invalid, used already, or expired." }, 400, env, request);
  const user = await env.DB.prepare("SELECT id, email, discord, created_at FROM users WHERE id = ?").bind(row.user_id).first();
  if (!user) return json({ error:"This beta ticket is invalid, used already, or expired." }, 400, env, request);
  try { const v = await env.DB.prepare("SELECT email_verified, role FROM users WHERE id = ?").bind(row.user_id).first(); if (v) { user.email_verified = Number(v.email_verified)||0; if (v.role) user.role = v.role; } } catch {}
  const host = String(requestHost(request) || "");
  if (verificationRequired(env) && !isVerifiedUser(user)) return json({ error:"Verify your email before using the Beta." }, 403, env, request);
  const role = effectiveRole(env, user);
  if (role !== ROLE_TESTER && role !== ROLE_ADMIN) return json({ error:"This account is not a Tester." }, 403, env, request);
  const { token } = await createSession(env, user.id);
  await audit(env, user, "beta.redeem", host, "beta session created");
  return json({ ok:true, token, user: publicUser(env, user), betaHost: host }, 200, env, request, { "set-cookie": sessionCookie(token, SESSION_TTL_SEC, request) });
}

// ---------- admin: Tester role management ----------
async function handleAdminRole(request, env, url){
  if (!hasDb(env)) return json({ error:"DB missing" }, 503, env, request);
  const m = url.pathname.match(/^\/api\/admin\/users\/(\d+)\/role\/?$/);
  if (!m) return json({ error:"Not found" }, 404, env, request);
  if (request.method !== "POST") return json({ error:"Method not allowed" }, 405, env, request);
  const auth = await requireAdmin(request, env);
  if (!auth) return json({ error:"Admin only" }, 403, env, request);
  const vg = adminVerifiedGate(auth, env, request); if (vg) return vg;
  let body; try{ body = await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
  const next = String(body.role || "").toLowerCase();
  if (!ASSIGNABLE_ROLES.includes(next))
    return json({ error:"Role must be 'user' or 'tester' - the Admin role comes from the Worker configuration, never from the API." }, 400, env, request);
  const id = Number(m[1]);
  let target;
  try { target = await env.DB.prepare("SELECT id, email, role FROM users WHERE id = ?").bind(id).first(); }
  catch { target = await env.DB.prepare("SELECT id, email FROM users WHERE id = ?").bind(id).first(); }
  if (!target) return json({ error:"User not found" }, 404, env, request);
  if (isAdminEmail(env, target.email)) return json({ error:"That account is an Administrator - Admin access is controlled by the Worker configuration, not by this panel." }, 400, env, request);
  if (Number(id) === Number(auth.user.id)) return json({ error:"You cannot change your own role." }, 400, env, request);
  const before = storedRole(target);
  if (before === next) return json({ ok:true, id, email: target.email, role: next, unchanged:true }, 200, env, request);
  try { await env.DB.prepare("UPDATE users SET role = ? WHERE id = ?").bind(next, id).run(); }
  catch { return json({ error:"Role storage is not configured yet (database migration pending)." }, 503, env, request); }
  await audit(env, auth.user, next === ROLE_TESTER ? "tester.grant" : "tester.revoke", String(target.email), before + " -> " + next);
  return json({ ok:true, id, email: target.email, role: next }, 200, env, request);
}

// ---------- admin: customer conversations (authorized support access) ----------
async function handleAdminConversations(request, env, url){
  if (!hasDb(env)) return json({ error:"DB missing" }, 503, env, request);
  if (!(await conversationsReady(env))) return json({ error:"Chat storage is not configured yet." }, 503, env, request);
  const single = url.pathname.match(/^\/api\/admin\/conversations\/([^\/]+)\/?$/);
  const msgs = url.pathname.match(/^\/api\/admin\/conversations\/([^\/]+)\/messages\/?$/);
  const stat = url.pathname.match(/^\/api\/admin\/conversations\/([^\/]+)\/status\/?$/);
  if (msgs || stat) {
    if (request.method !== "POST") return json({ error:"Method not allowed" }, 405, env, request);
    const cid = decodeURIComponent((msgs || stat)[1]);
    const conv = await env.DB.prepare("SELECT * FROM conversations WHERE conversation_id = ?").bind(cid).first();
    if (!conv) return json({ error:"Conversation not found" }, 404, env, request);
    let body; try{ body = await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
    const auth2 = await requireAdmin(request, env);
    if (stat) {
      const next = String(body.status || "").toLowerCase();
      if (!CONVERSATION_STATUSES.includes(next)) return json({ error:"Unknown status" }, 400, env, request);
      await env.DB.prepare("UPDATE conversations SET status = ?, updated_at = ? WHERE conversation_id = ?").bind(next, new Date().toISOString(), cid).run();
      await audit(env, auth2 && auth2.user, "admin.conversation.status", cid, next);
      return json({ ok:true, status: next }, 200, env, request);
    }
    const text = cleanMessage(body.body || body.message || body.text || "");
    if (text.length < 2) return json({ error:"Message too short" }, 400, env, request);
    await insertConversationMessage(env, conv, "admin", text, conv.user_id);
    await env.DB.prepare("UPDATE conversations SET status = 'answered', assigned_admin = ? WHERE conversation_id = ?").bind(auth2 ? String(auth2.user.email) : null, cid).run().catch(()=>{});
    // tell the customer by email (best effort)
    try {
      const owner = await env.DB.prepare("SELECT email FROM users WHERE id = ?").bind(conv.user_id).first();
      if (owner && isEmail(owner.email)) {
        const label = conv.purchase_id || conv.order_id || conv.conversation_id;
        await sendEmail(env, { to: owner.email, subject:`[Aether] Reply to your conversation ${label}`, html: orderHtml({ title:`Aether replied - ${label}`, fields:[["Purchase", conv.purchase_id || "-"],["Message", text],["At", new Date().toLocaleString("en-GB", { timeZone:"Europe/Berlin", dateStyle:"long", timeStyle:"short" })]] }), text:`${text}\n\n- Aether support (${label})` }).catch(()=>null);
      }
    } catch {}
    await audit(env, auth2 && auth2.user, "admin.conversation.reply", cid, text.slice(0,120));
    return json({ ok:true, messages: await conversationMessages(env, conv) }, 200, env, request);
  }
  if (single && request.method === "GET") {
    const cid = decodeURIComponent(single[1]);
    const conv = await env.DB.prepare("SELECT c.*, COALESCE(u.email, '') AS user_email FROM conversations c LEFT JOIN users u ON u.id = c.user_id WHERE c.conversation_id = ?").bind(cid).first();
    if (!conv) return json({ error:"Conversation not found" }, 404, env, request);
    const detail = Object.assign({}, publicConversation(conv), { user_email: conv.user_email || "" });
    return json({ ok:true, conversation: detail, messages: await conversationMessages(env, conv) }, 200, env, request);
  }
  const rows = await env.DB.prepare(`SELECT c.*, COALESCE(u.email, '') AS user_email,
      (SELECT COUNT(*) FROM messages m WHERE ${MSG_MATCH}) AS message_count,
      (SELECT m.body FROM messages m WHERE ${MSG_MATCH} ORDER BY m.id DESC LIMIT 1) AS last_body,
      (SELECT m.created_at FROM messages m WHERE ${MSG_MATCH} ORDER BY m.id DESC LIMIT 1) AS last_at
    FROM conversations c LEFT JOIN users u ON u.id = c.user_id
    ORDER BY COALESCE(c.updated_at, c.created_at) DESC LIMIT 200`).all();
  return json({ ok:true, conversations: (rows.results || []).map(c => Object.assign(conversationListRow(c), { user_email: c.user_email || "" })) }, 200, env, request);
}

// ---------- admin: Beta configuration (email-confirmed changes) ----------
async function betaFlagsFor(env){
  const flags = betaFlags(env);
  const raw = await getSetting(env, "beta_flags");
  if (!raw) return flags;
  try { const j = JSON.parse(String(raw)); if (j && typeof j === "object") return Object.assign({}, flags, j); } catch {}
  return flags;
}
async function handleAdminBeta(request, env, url){
  const path = url.pathname.replace(/\/+$/, "");
  const auth = await requireAdmin(request, env);
  if (!auth) return json({ error:"Admin only" }, 403, env, request);
  // --- read: current config + pending change + audit trail
  if (path === "/api/admin/beta") {
    const cfg = await betaConfig(env);
    const flags = await betaFlagsFor(env);
    let pending = null;
    try {
      const row = await env.DB.prepare("SELECT t.expires_at, t.payload, COALESCE(u.email,'') AS email FROM auth_tokens t LEFT JOIN users u ON u.id = t.user_id WHERE t.purpose = 'beta_domain' ORDER BY t.created_at DESC LIMIT 1").first();
      if (row && Number(row.expires_at) > Date.now()) {
        let wanted = {}; try { wanted = JSON.parse(String(row.payload || "{}")) || {}; } catch {}
        pending = { requested_by: row.email || "", expires_at: Number(row.expires_at), host: validBetaHost(wanted.host) || "", path: validBetaPath(wanted.path) || "" };
      }
    } catch {}
    const auditRows = await env.DB.prepare("SELECT actor_email, action, target, detail, created_at FROM audit_log ORDER BY id DESC LIMIT 25").all().catch(()=>({ results: [] }));
    return json({
      ok:true,
      beta:{ host: cfg.host, path: cfg.path, url: cfg.url, env: String(env.AETHER_ENV || "production"), flags },
      defaults:{ host: DEFAULT_BETA_HOST, path: DEFAULT_BETA_PATH },
      pending,
      admin_email_verified: isVerifiedUser(auth.user),
      email_configured: emailConfigured(env),
      audit: auditRows.results || [],
    }, 200, env, request);
  }
  const vg = adminVerifiedGate(auth, env, request); if (vg) return vg;
  // --- request a change: password re-auth -> expiring single-use token -> email to the admin
  if (path === "/api/admin/beta/domain/request") {
    if (request.method !== "POST") return json({ error:"Method not allowed" }, 405, env, request);
    if (await hitRateLimit(env, "beta-domain:" + auth.user.id, 6, 3600000))
      return json({ error:"Too many Beta domain change requests - try again later." }, 429, env, request);
    let body; try{ body = await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
    const host = validBetaHost(body.host);
    const bp = validBetaPath(body.path);
    if (!host) return json({ error:"The Beta host must be a subdomain of get-aether.de, for example betatester.get-aether.de" }, 400, env, request);
    if (!bp) return json({ error:"The Beta path must start with / and use plain URL characters (7-200 characters)." }, 400, env, request);
    const password = String(body.currentPassword || "");
    if (!password) return json({ error:"Confirm this sensitive change with your admin password." }, 400, env, request);
    const row = await env.DB.prepare("SELECT password_hash FROM users WHERE id = ?").bind(auth.user.id).first();
    if (!row || !(await verifyPassword(password, row.password_hash)))
      return json({ error:"Password is incorrect - the Beta domain was not changed." }, 401, env, request);
    const token = await issueAuthToken(env, auth.user.id, "beta_domain", BETA_DOMAIN_TTL_MS, JSON.stringify({ host, path: bp }));
    const link = `${siteUrl(env)}/admin.html?betaToken=${token}#beta`;
    let emailed = false;
    if (emailConfigured(env)) {
      try {
        await sendEmail(env, { to: auth.user.email, subject:"Confirm the Aether Beta domain change", html: orderHtml({ title:"Confirm the Beta domain change", fields:[["Admin", auth.user.email],["New Beta URL", "https://" + host + bp],["Confirm link", link],["Valid for", "30 minutes - single use"]], note:"If you did not request this, ignore this email: nothing changes until the link is opened and confirmed." }), text:`Confirm the Aether Beta domain change:\n${link}\n\nValid for 30 minutes and single use.` });
        emailed = true;
      } catch(e){ console.error("beta domain email failed", e && e.message); }
    }
    await audit(env, auth.user, "beta.domain.request", host + bp, emailed ? "confirmation email sent" : "email not configured");
    return json({
      ok:true,
      pending:{ host, path: bp, requested_by: auth.user.email, expires_at: Date.now() + BETA_DOMAIN_TTL_MS },
      emailed,
      confirm_url: emailed ? null : link,
      warning: emailed ? null : "Email delivery is not configured, so the confirmation link is shown here instead of being mailed.",
      note: "The Beta domain changes only after the confirmation link is opened and POSTed back by an authenticated, still-authorized admin.",
    }, 200, env, request);
  }
  // --- confirm: single-use token, still-admin check, then the setting is written
  if (path === "/api/admin/beta/domain/confirm") {
    if (request.method !== "POST") return json({ error:"Method not allowed" }, 405, env, request);
    let body; try{ body = await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
    const token = String(body.token || "").trim().slice(0,200);
    if (!token) return json({ error:"Confirmation token required" }, 400, env, request);
    const row = await consumeAuthToken(env, token, "beta_domain");
    if (!row) return json({ error:"This confirmation link is invalid, already used, or expired - request the change again." }, 400, env, request);
    if (Number(row.user_id) !== Number(auth.user.id)) return json({ error:"That confirmation belongs to a different admin account." }, 403, env, request);
    if (!isAdminEmail(env, auth.user.email)) return json({ error:"This account is no longer an administrator." }, 403, env, request);
    let wanted = {}; try { wanted = JSON.parse(String(row.payload || "{}")) || {}; } catch {}
    const host = validBetaHost(wanted.host);
    const bp = validBetaPath(wanted.path);
    if (!host || !bp) return json({ error:"The requested value is no longer valid - start again." }, 400, env, request);
    await setSetting(env, "beta_host", host);
    await setSetting(env, "beta_path", bp);
    await audit(env, auth.user, "beta.domain.confirm", host + bp, "beta domain updated");
    return json({ ok:true, beta:{ host, path: bp, url: "https://" + host + bp } }, 200, env, request);
  }
  // --- feature flags (server-side evaluation)
  if (path === "/api/admin/beta/flags") {
    if (request.method !== "POST") return json({ error:"Method not allowed" }, 405, env, request);
    let body; try{ body = await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
    const wanted = body.flags && typeof body.flags === "object" ? body.flags : {};
    const merged = Object.assign({}, await betaFlagsFor(env));
    for (const key of ["betaNewChat","betaDashboard","betaTools"]) if (key in wanted) merged[key] = !!wanted[key];
    await setSetting(env, "beta_flags", JSON.stringify(merged));
    await audit(env, auth.user, "beta.flags", "flags", JSON.stringify(merged));
    return json({ ok:true, flags: merged }, 200, env, request);
  }
  return json({ error:"Not found", path }, 404, env, request);
}
// A read-only admin index of recent administrative actions.
async function handleAdminAudit(request, env){
  const rows = await env.DB.prepare("SELECT actor_email, action, target, detail, created_at FROM audit_log ORDER BY id DESC LIMIT 200").all().catch(()=>({ results: [] }));
  return json({ ok:true, audit: rows.results || [] }, 200, env, request);
}

// ---------- promo handler ----------
async function handlePromo(request, env, url){
  const code = (url.searchParams.get("code") || url.searchParams.get("promo") || "").trim();
  const amountStr = url.searchParams.get("amount") || url.searchParams.get("price") || "0";
  const amount = Number(amountStr);
  if (request.method === "POST") {
    let body={}; try{ body=await request.json(); }catch{}
    const c = String(body.code || body.promo || code || "").trim();
    const a = Number(body.amount ?? amount);
    const r = resolvePromo(env, c, a);
    if (!r.valid) return json({ valid:false, error:r.error }, 200, env, request);
    return json({ valid:true, code:r.code, type:r.type, value:r.value, discount:r.discount, finalAmount:r.finalAmount }, 200, env, request);
  }
  const r = resolvePromo(env, code, amount);
  if (!r.valid) return json({ valid:false, error:r.error, valid_codes: Object.keys(loadPromos(env)).length ? undefined : undefined }, 200, env, request);
  return json({ valid:true, code:r.code, type:r.type, value:r.value, discount:r.discount, finalAmount:r.finalAmount }, 200, env, request);
}

// ---------- auth handlers ----------
async function handleRegister(request, env){
  if (!hasDb(env)) return json({ error:"Accounts not configured \u2014 DB missing" }, 503, env, request);
  let body; try{ body=await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
  const email = String(body.email||"").trim().toLowerCase().slice(0,200);
  const password = String(body.password||"");
  const discord = String(body.discord||"").trim().slice(0,120);
  const captcha = await turnstileGuard(request, env, body);
  if (captcha) return captcha;
  if (!isEmail(email)) return json({ error:"Valid email required" }, 400, env, request);
  const pwProblem = passwordProblem(password);
  if (pwProblem) return json({ error: pwProblem }, 400, env, request);
  const exists = await env.DB.prepare("SELECT id FROM users WHERE email = ?").bind(email).first();
  if (exists) return json({ error:"An account with this email already exists \u2014 sign in instead." }, 409, env, request);
  const hash = await hashPassword(password);
  const res = await env.DB.prepare("INSERT INTO users (email, password_hash, discord) VALUES (?, ?, ?)").bind(email, hash, discord||null).run();
  const userId = res.meta && res.meta.last_row_id ? res.meta.last_row_id : (await env.DB.prepare("SELECT id FROM users WHERE email = ?").bind(email).first()).id;
  const { token } = await createSession(env, userId);
  const user = await env.DB.prepare("SELECT id, email, discord, created_at FROM users WHERE id = ?").bind(userId).first();
  if (user) { user.email_verified = 0; user.role = ROLE_USER; }
  dropExpiredSessions(env);
  // Email verification is optional: it only fires when an email transport is configured.
  if (emailConfigured(env)) {
    try {
      const vtoken = await issueAuthToken(env, userId, "verify", 24*3600*1000);
      const link = `${siteUrl(env)}/verify-email.html?token=${vtoken}`;
      await sendEmail(env, { to: email, subject:"Confirm your Aether account", html: orderHtml({ title:"Confirm your email", fields:[["Account", email],["Link", link]], note:"This link is valid for 24 hours. If you did not create this account you can ignore this email." }), text:`Confirm your Aether account:\n${link}\n\nValid for 24 hours.` });
    } catch(e){ console.warn("verification email failed", e && e.message); }
  }
  // Claim guest orders that were placed with this email before the account existed.
  await env.DB.prepare("UPDATE orders SET user_id = ? WHERE user_id IS NULL AND email IS NOT NULL AND lower(email) = lower(?)").bind(userId, email).run().catch(()=>{});
  return json({ ok:true, token, user: user ? publicUser(env, user) : null, emailVerified: false, emailVerificationRequired: verificationRequired(env), emailSent: emailConfigured(env), isAdmin: isAdminEmail(env, email) }, 200, env, request, { "set-cookie": sessionCookie(token, SESSION_TTL_SEC, request) });
}
async function handleLogin(request, env){
  if (!hasDb(env)) return json({ error:"Accounts not configured \u2014 DB missing" }, 503, env, request);
  let body; try{ body=await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
  const email = String(body.email||"").trim().toLowerCase().slice(0,200);
  const password = String(body.password||"");
  // Turnstile on login is opt-in (TURNSTILE_LOGIN=true) so normal sign-in stays frictionless.
  if (String(env.TURNSTILE_LOGIN||"").toLowerCase()==="true") { const captcha = await turnstileGuard(request, env, body); if (captcha) return captcha; }
  if (!isEmail(email) || !password) return json({ error:"Email and password required" }, 400, env, request);
  if (String(password).length > 200) return json({ error:"Invalid email or password" }, 401, env, request);
  const row = await env.DB.prepare("SELECT id, email, password_hash, discord FROM users WHERE email = ?").bind(email).first();
  if (!row) return json({ error:"Invalid email or password" }, 401, env, request);
  const ok = await verifyPassword(password, row.password_hash);
  if (!ok) return json({ error:"Invalid email or password" }, 401, env, request);
  // Rotate any session presented with the login, and spend a fresh random token:
  // this is what stops session fixation and token reuse across logins.
  const presented = getBearerOrCookie(request);
  if (presented) { try { await env.DB.prepare("DELETE FROM sessions WHERE token = ?").bind(await sha256Hex(presented)).run(); } catch {} }
  const { token } = await createSession(env, row.id);
  dropExpiredSessions(env);
  await env.DB.prepare("UPDATE orders SET user_id = ? WHERE user_id IS NULL AND email IS NOT NULL AND lower(email) = lower(?)").bind(row.id, row.email).run().catch(()=>{});
  const user = { id: row.id, email: row.email, discord: row.discord };
  try { const v = await env.DB.prepare("SELECT email_verified FROM users WHERE id = ?").bind(row.id).first(); if (v) user.email_verified = Number(v.email_verified)||0; } catch {}
  try { const v = await env.DB.prepare("SELECT role FROM users WHERE id = ?").bind(row.id).first(); if (v && v.role) user.role = v.role; } catch {}
  const me = publicUser(env, user);
  return json({ ok:true, token, user: me, emailVerified: me.emailVerified, emailVerificationRequired: verificationRequired(env), role: me.role, isTester: me.isTester, isAdmin: me.isAdmin, betaAllowed: me.isTester && (!verificationRequired(env) || me.emailVerified) }, 200, env, request, { "set-cookie": sessionCookie(token, SESSION_TTL_SEC, request) });
}
async function handleLogout(request, env){
  const token = getBearerOrCookie(request);
  if (token && env.DB) {
    try { await env.DB.prepare("DELETE FROM sessions WHERE token = ?").bind(await sha256Hex(token)).run(); } catch {}
  }
  return json({ ok:true }, 200, env, request, { "set-cookie": clearCookie(request) });
}
async function handleMe(request, env){
  const auth = await requireAuth(request, env);
  if (!auth) return json({ ok:false, authenticated:false }, 401, env, request);
  const me = publicUser(env, auth.user);
  return json({ ok:true, authenticated:true, user: auth.user, emailVerified: me.emailVerified, emailVerificationRequired: verificationRequired(env), role: me.role, isTester: me.isTester, isAdmin: me.isAdmin, betaAllowed: me.isTester && (!verificationRequired(env) || me.emailVerified) }, 200, env, request);
}
// DELETE /api/me - a user may remove their own account.
// Orders/messages are kept for accounting but detached (user_id = NULL).
async function handleDeleteMe(request, env){
  const auth = await requireAuth(request, env);
  if (!auth) return json({ error:"Not authenticated" }, 401, env, request);
  if (!hasDb(env)) return json({ error:"Accounts not configured" }, 503, env, request);
  try{
    await env.DB.prepare("DELETE FROM sessions WHERE user_id = ?").bind(auth.user.id).run();
    await env.DB.prepare("UPDATE orders SET user_id = NULL WHERE user_id = ?").bind(auth.user.id).run().catch(()=>{});
    await env.DB.prepare("UPDATE messages SET user_id = NULL WHERE user_id = ?").bind(auth.user.id).run().catch(()=>{});
    await env.DB.prepare("UPDATE conversations SET user_id = NULL WHERE user_id = ?").bind(auth.user.id).run().catch(()=>{});
    await env.DB.prepare("DELETE FROM beta_feedback WHERE user_id = ?").bind(auth.user.id).run().catch(()=>{});
    await env.DB.prepare("DELETE FROM auth_tokens WHERE user_id = ?").bind(auth.user.id).run().catch(()=>{});
    await env.DB.prepare("DELETE FROM users WHERE id = ?").bind(auth.user.id).run();
  }catch(e){ return json({ error: (e && e.message) || "Delete failed" }, 500, env, request); }
  return json({ ok:true, deleted:true }, 200, env, request, { "set-cookie": clearCookie(request) });
}

// ---------- how a password-reset link is delivered ----------
// Email first (Cloudflare Email Sending binding or Resend). A deployment with no transport at all
// cannot email, so the link is handed to the recovery channel instead: the operator's Discord
// webhook, and as the last resort one server log line the operator can read in the Cloudflare dashboard. Both recovery transports
// are deliberately narrow - the webhook only carries links for the accounts in RESET_DISCORD_EMAILS
// (default: the admin accounts), and every account still gets a log line so nothing is lost.
// `delivery` describes the deployment, never the account: every branch answers with the same 200
// body and only the log/webhook side effect differs, so this cannot be used to probe which email
// addresses have an account.
// A per-account ceiling on top of the per-IP one. A single IP is already capped at 6/h, but an
// attacker with a botnet can rotate addresses and keep resetting one victim's password: every
// request replaces their previous link and sends another email. Six per hour is generous for a
// human who is genuinely stuck and worthless as an inbox bomb. The answer stays byte-identical to
// a successful request, so a throttled address cannot be told apart from one that was served.
function forgotAccountLimit(env){
  const n = Number(env.FORGOT_ACCOUNT_LIMIT);
  return Number.isFinite(n) && n > 0 ? Math.floor(n) : 6;
}
// user_id 0 is never a real account, so the stand-in writes below can never touch anybody's row.
const FORGOT_DUMMY_USER = 0;
// The response body of /api/auth/forgot is identical for every address, but the *work* is not: a
// real account gets a lookup, a token write and an outbound email while an unknown one used to get
// nothing at all. That difference in latency is an account oracle no matter how identical the JSON
// is. So the unknown-address branch now performs the same database work (a lookup plus a single-use
// token write that is rolled back in the same request) and every answer is padded past a jittered
// floor. The floor removes the systematic gap - the database work - which is the part a script can
// actually measure; outbound mail latency is far noisier than the gap it would have to reveal.
const FORGOT_FLOOR_MS = 220;
async function dummyForgotWork(env){
  try {
    await env.DB.prepare("SELECT id, email FROM users WHERE email = ?").bind("__aether_no_such_account__").first();
    await env.DB.prepare("DELETE FROM auth_tokens WHERE user_id = ? AND purpose = ?").bind(FORGOT_DUMMY_USER, "reset").run();
    const hash = await sha256Hex(newToken());
    await env.DB.prepare("INSERT INTO auth_tokens (token_hash, user_id, purpose, expires_at, created_at) VALUES (?,?,?,?,?)")
      .bind(hash, FORGOT_DUMMY_USER, "reset", Date.now() + 30*60*1000, Date.now()).run();
    await env.DB.prepare("DELETE FROM auth_tokens WHERE token_hash = ?").bind(hash).run();
  } catch(e){ /* the real branch swallows its failures too, so neither path can be spotted here */ }
}
async function equalizeForgotTiming(startedAt){
  const target = FORGOT_FLOOR_MS + Math.floor(Math.random()*170);
  const spent = Date.now() - startedAt;
  if (spent < target) await new Promise(r => setTimeout(r, target - spent));
}
function resetDeliveryChannel(env){
  if (emailConfigured(env)) return "email";
  if (env.DISCORD_WEBHOOK_URL) return "discord";
  return "logs";
}
function resetChannelAllows(env, email){
  const raw = String(env.RESET_DISCORD_EMAILS == null ? "" : env.RESET_DISCORD_EMAILS).trim().toLowerCase();
  if (!raw) return isAdminEmail(env, email); // default: admin accounts only
  if (raw === "*") return true;             // explicit opt-in for every account (see SECURITY.md)
  return raw.split(",").map(s=>s.trim()).filter(Boolean).includes(String(email||"").trim().toLowerCase());
}
async function deliverResetLink(env, email, link, emailPayload){
  if (emailConfigured(env)) {
    try { await sendEmail(env, emailPayload); return "email"; }
    catch (e) { console.warn("[reset-link] email delivery failed, trying the recovery channel", e && e.message); }
  }
  if (env.DISCORD_WEBHOOK_URL && resetChannelAllows(env, email)) {
    try {
      await sendDiscord(env, { embeds: discordEmbed({
        title:"Password reset link",
        color: DISCORD_ACCENT.pending,
        description:"Requested from the Aether account-recovery form. Single use, valid for 30 minutes. If nobody asked for it, ignore this message: nothing changes until the link is opened.",
        fields:[["Account", email],["Link", link],["Valid for", "30 minutes - single use"]],
      }) });
      return "discord";
    } catch (e) { console.warn("[reset-link] recovery channel failed, falling back to the operator log", e && e.message); }
  }
  // Last resort: the operator's log line (Cloudflare dashboard -> Workers -> aether-api -> Logs).
  // Never printed when a real channel delivered the link.
  console.log("[reset-link] " + email + " " + link);
  return "logs";
}
// Sent after every successful password reset or change. This is the cheapest detection control
// there is: if somebody else took the account over, the real owner learns about it in their inbox
// instead of only noticing later that their session was killed. It carries no password and no link,
// so a misdelivered copy grants nothing, and a mail outage is logged and swallowed - a notification
// must never be able to make a completed password change look like it failed.
async function sendPasswordChangedNotice(env, email, via){
  if (!emailConfigured(env) || !isEmail(email)) return;
  const when = new Date().toISOString().replace("T"," ").slice(0,16) + " UTC";
  try {
    await sendEmail(env, {
      to: email,
      subject: "Your Aether password was changed",
      html: orderHtml({
        title:"Your password was changed",
        fields:[["Account", email],["When", when],["Method", via],["Devices", "every other session was signed out"]],
        note:"If this was you, nothing to do. If it was not, reply to this email immediately and we will secure the account \u2014 the new password is already active, so treat this as urgent.",
      }),
      text:`Your Aether password was changed.\n\nAccount: ${email}\nWhen: ${when}\nMethod: ${via}\nEvery other device was signed out.\n\nIf this was not you, contact questions@get-aether.de immediately.`,
    });
  } catch(e){ console.warn("password-changed notice failed", e && e.message); }
}
// ---------- password reset + account settings ----------
// POST /api/auth/forgot { email } - always answers the same way, so the endpoint cannot be
// used to find out which email addresses have an account.
async function handleForgotPassword(request, env){
  if (!hasDb(env)) return json({ error:"Accounts not configured" }, 503, env, request);
  let body; try{ body=await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
  const captcha = await turnstileGuard(request, env, body);
  if (captcha) return captcha;
  const email = String(body.email||"").trim().toLowerCase().slice(0,200);
  const channel = resetDeliveryChannel(env);
  const message = channel === "email"
    ? "If that email has an Aether account, a reset link is on its way. Check your inbox (and spam)."
    : channel === "discord"
      ? "Email delivery is not configured on this deployment, so the reset link went to the site's recovery channel instead of an inbox (the Discord ops channel, or the server log). It works once and expires after 30 minutes - contact questions@get-aether.de if you cannot reach it."
      : "Email delivery is not configured on this deployment, so the reset link was written to the server log instead of an inbox. It works once and expires after 30 minutes - contact questions@get-aether.de and we will retrieve it with you.";
  const generic = { ok:true, delivery:channel, message };
  const startedAt = Date.now();
  if (isEmail(email)) {
    // The email is hashed before it becomes a rate-limit key, so the throttle table never holds a
    // plaintext address and a compromised D1 snapshot cannot be used as a customer list.
    const bucket = "forgot-acct:" + (await sha256Hex(email)).slice(0, 32);
    const throttled = await hitRateLimit(env, bucket, forgotAccountLimit(env), 3600000);
    if (throttled) {
      // Silent drop: no token, no mail, no recovery-channel post - but the same database work and
      // the same body, so this cannot be used to probe which addresses exist.
      await dummyForgotWork(env);
    } else {
      // No email provider? deliverResetLink() hands the link to the recovery channel instead of refusing.
      try {
        const row = await env.DB.prepare("SELECT id, email FROM users WHERE email = ?").bind(email).first();
        if (row) {
          const token = await issueAuthToken(env, row.id, "reset", 30*60*1000);
          const link = `${siteUrl(env)}/reset-password.html?token=${token}`;
          await deliverResetLink(env, row.email, link, {
            to: row.email,
            subject:"Reset your Aether password",
            html: orderHtml({ title:"Reset your password", fields:[["Account", row.email],["Link", link],["Valid for", "30 minutes \u2014 single use"]], note:"If you did not ask for this, ignore this email: your password stays unchanged. Signing in elsewhere is not affected until the link is used." }),
            text:`Reset your Aether password:\n${link}\n\nValid for 30 minutes and single use. If you did not request it, ignore this email.`,
          });
        } else {
          await dummyForgotWork(env);
        }
      } catch(e){ console.warn("forgot-password failed", e && e.message); }
    }
  }
  await equalizeForgotTiming(startedAt);
  return json(generic, 200, env, request);
}
// POST /api/auth/reset { token, password } - consumes the single-use token, replaces the
// password, kills every existing session and signs the user back in with a fresh one.
async function handleResetPassword(request, env){
  if (!hasDb(env)) return json({ error:"Accounts not configured" }, 503, env, request);
  let body; try{ body=await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
  const captcha = await turnstileGuard(request, env, body);
  if (captcha) return captcha;
  const token = String(body.token||"").trim().slice(0,200);
  const password = String(body.password||"");
  if (!token) return json({ error:"Reset link is incomplete \u2014 request a new email." }, 400, env, request);
  const pwProblem = passwordProblem(password);
  if (pwProblem) return json({ error: pwProblem }, 400, env, request);
  const row = await consumeAuthToken(env, token, "reset");
  if (!row) return json({ error:"This reset link is invalid or has expired \u2014 request a new one." }, 400, env, request);
  const user = await env.DB.prepare("SELECT id, email, discord FROM users WHERE id = ?").bind(row.user_id).first();
  if (!user) return json({ error:"This reset link is invalid or has expired \u2014 request a new one." }, 400, env, request);
  const hash = await hashPassword(password);
  await env.DB.prepare("UPDATE users SET password_hash = ? WHERE id = ?").bind(hash, user.id).run();
  await env.DB.prepare("DELETE FROM sessions WHERE user_id = ?").bind(user.id).run();
  await env.DB.prepare("DELETE FROM auth_tokens WHERE user_id = ?").bind(user.id).run().catch(()=>{});
  const { token: session } = await createSession(env, user.id);
  await sendPasswordChangedNotice(env, user.email, "password reset link");
  try { const v = await env.DB.prepare("SELECT email_verified, role FROM users WHERE id = ?").bind(user.id).first(); if (v) { user.email_verified = Number(v.email_verified)||0; if (v.role) user.role = v.role; } } catch {}
  return json({ ok:true, token: session, user: publicUser(env, user), emailVerified: isVerifiedUser(user), emailVerificationRequired: verificationRequired(env), isAdmin: isAdminEmail(env, user.email) }, 200, env, request, { "set-cookie": sessionCookie(session, SESSION_TTL_SEC, request) });
}
// POST /api/auth/password { currentPassword, newPassword } - signed-in password change.
async function handleChangePassword(request, env){
  if (!hasDb(env)) return json({ error:"Accounts not configured" }, 503, env, request);
  const auth = await requireAuth(request, env);
  if (!auth) return json({ error:"Not authenticated" }, 401, env, request);
  let body; try{ body=await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
  const current = String(body.currentPassword || body.current || "");
  const next = String(body.newPassword || body.password || "");
  if (!current) return json({ error:"Current password required" }, 400, env, request);
  const pwProblem = passwordProblem(next);
  if (pwProblem) return json({ error: pwProblem }, 400, env, request);
  const row = await env.DB.prepare("SELECT password_hash FROM users WHERE id = ?").bind(auth.user.id).first();
  if (!row || !(await verifyPassword(current, row.password_hash))) return json({ error:"Current password is incorrect" }, 401, env, request);
  await env.DB.prepare("UPDATE users SET password_hash = ? WHERE id = ?").bind(await hashPassword(next), auth.user.id).run();
  // Keep this session, drop every other one.
  await env.DB.prepare("DELETE FROM sessions WHERE user_id = ? AND token != ?").bind(auth.user.id, auth.token).run().catch(()=>{});
  await sendPasswordChangedNotice(env, auth.user.email, "signed-in password change");
  return json({ ok:true, message:"Password updated. Other devices have been signed out." }, 200, env, request);
}
// POST /api/auth/verify-email { token }
async function handleVerifyEmail(request, env){
  if (!hasDb(env)) return json({ error:"Accounts not configured" }, 503, env, request);
  let body; try{ body=await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
  const row = await consumeAuthToken(env, String(body.token||"").trim(), "verify");
  if (!row) return json({ error:"This confirmation link is invalid or has expired." }, 400, env, request);
  await env.DB.prepare("UPDATE users SET email_verified = 1 WHERE id = ?").bind(row.user_id).run().catch(()=>{});
  return json({ ok:true, verified:true }, 200, env, request);
}
async function handleOrders(request, env, url){
  const auth = await requireAuth(request, env);
  if (!auth) return json({ error:"Not authenticated" }, 401, env, request);
  // Purchases are protected customer data: an unconfirmed email cannot read them.
  const gate = verifiedGate(auth, env, request);
  if (gate) return gate;
  if (request.method === "GET" && url.pathname.match(/^\/api\/orders\/[^\/]+\/?$/)) {
    const orderId = decodeURIComponent(url.pathname.split("/").pop().replace(/\/$/,""));
    const ord = await env.DB.prepare("SELECT * FROM orders WHERE order_id = ? AND user_id = ?").bind(orderId, auth.user.id).first();
    if (!ord) return json({ error:"Order not found" }, 404, env, request);
    await ensurePurchaseId(env, ord);
    const msgs = await env.DB.prepare("SELECT id, sender, body, created_at FROM messages WHERE order_id = ? ORDER BY id ASC").bind(orderId).all();
    return json({ ok:true, order: ord, purchase: publicPurchase(ord), messages: msgs.results || [] }, 200, env, request);
  }
  // list
  const list = await env.DB.prepare("SELECT * FROM orders WHERE user_id = ? ORDER BY id DESC LIMIT 100").bind(auth.user.id).all();
  const orders = await ensurePurchaseIds(env, list.results || []);
  return json({ ok:true, orders, purchases: orders.map(publicPurchase) }, 200, env, request);
}
async function handleOrderMessage(request, env, url){
  const auth = await requireAuth(request, env);
  if (!auth) return json({ error:"Not authenticated" }, 401, env, request);
  const gate = verifiedGate(auth, env, request);
  if (gate) return gate;
  const m = url.pathname.match(/^\/api\/orders\/([^\/]+)\/message\/?$/);
  if (!m) return json({ error:"Invalid route" }, 404, env, request);
  const orderId = decodeURIComponent(m[1]);
  const ord = await env.DB.prepare("SELECT order_id, user_id FROM orders WHERE order_id = ? AND user_id = ?").bind(orderId, auth.user.id).first();
  if (!ord) return json({ error:"Order not found or not yours" }, 404, env, request);
  let body; try{ body=await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
  const text = String(body.body || body.message || body.text || "").trim().slice(0,4000);
  if (!text || text.length < 2) return json({ error:"Message too short" }, 400, env, request);
  await env.DB.prepare("INSERT INTO messages (order_id, user_id, sender, body) VALUES (?, ?, 'customer', ?)").bind(orderId, auth.user.id, text).run();
  // also email + discord notify (99999% \u2014 best effort, don't fail the request)
  const submitted = new Date().toLocaleString("en-GB",{ timeZone:"Europe/Berlin", dateStyle:"long", timeStyle:"short"});
  const discordFields = [
    ["Order ID", orderId, true],
    ["From", `${auth.user.email}${auth.user.discord ? " \u2022 " + auth.user.discord : ""}`, true],
    ["Message", text.slice(0,900), false],
    ["At", submitted, true],
  ];
  const notify = [
    sendEmail(env, { to: "questions@get-aether.de", subject:`[CHAT] ${orderId} \u2014 ${auth.user.email}`, html: orderHtml({ title:`New customer message \u2014 ${orderId}`, fields: [["Order", orderId],["From", auth.user.email],["Discord", auth.user.discord||"\u2014"],["Message", text],["At", submitted]] }), text:`New message for ${orderId} from ${auth.user.email}:\n\n${text}`, replyTo: auth.user.email }).catch(()=>null),
    sendDiscord(env, { embeds: discordEmbed({ title:`\uD83D\uDCAC New chat \u2014 ${orderId}`, color: DISCORD_ACCENT.info, fields: discordFields, description: `**${escMd(auth.user.email)}** \u2192 ${escMd(text.slice(0,400))}`, footer:`Chat \u2022 ${submitted}`, author:{name:"Aether Chat"} }) }).catch(()=>null),
  ];
  await Promise.all(notify).catch(()=>{});
  const msgs = await env.DB.prepare("SELECT id, sender, body, created_at FROM messages WHERE order_id = ? ORDER BY id ASC").bind(orderId).all();
  return json({ ok:true, messages: msgs.results || [] }, 200, env, request);
}

// ---------- admin handlers ----------
async function handleAdminOrders(request, env, url){
  if (!hasDb(env)) return json({ error:"DB missing" }, 503, env, request);
  const single = url.pathname.match(/^\/api\/admin\/orders\/([^\/]+)\/?$/);
  if (single) {
    const orderId = decodeURIComponent(single[1]);
    const ord = await env.DB.prepare("SELECT o.*, COALESCE(u.email, o.email) AS user_email FROM orders o LEFT JOIN users u ON u.id = o.user_id WHERE o.order_id = ?").bind(orderId).first();
    if (!ord) return json({ error:"Order not found" }, 404, env, request);
    await ensurePurchaseId(env, ord);
    const msgs = await env.DB.prepare("SELECT id, sender, user_id, body, created_at FROM messages WHERE order_id = ? ORDER BY id ASC").bind(orderId).all();
    return json({ ok:true, order: ord, messages: msgs.results || [] }, 200, env, request);
  }
  const status = url.searchParams.get("status");
  const limit = Math.min(Math.max(Number(url.searchParams.get("limit") || 200) || 200, 1), 500);
  const rows = status
    ? await env.DB.prepare("SELECT o.*, COALESCE(u.email, o.email) AS user_email FROM orders o LEFT JOIN users u ON u.id = o.user_id WHERE o.status = ? ORDER BY o.id DESC LIMIT ?").bind(status, limit).all()
    : await env.DB.prepare("SELECT o.*, COALESCE(u.email, o.email) AS user_email FROM orders o LEFT JOIN users u ON u.id = o.user_id ORDER BY o.id DESC LIMIT ?").bind(limit).all();
  const list = await ensurePurchaseIds(env, rows.results || []);
  return json({ ok:true, orders: list }, 200, env, request);
}
async function handleAdminUsers(request, env, url){
  if (!hasDb(env)) return json({ error:"DB missing" }, 503, env, request);
  const del = url.pathname.match(/^\/api\/admin\/users\/(\d+)\/?$/);
  if (del) {
    if (request.method !== "DELETE") return json({ error:"Method not allowed" }, 405, env, request);
    const id = Number(del[1]);
    const target = await env.DB.prepare("SELECT id, email FROM users WHERE id = ?").bind(id).first();
    if (!target) return json({ error:"User not found" }, 404, env, request);
    if (isAdminEmail(env, target.email)) return json({ error:"Cannot delete an admin account" }, 400, env, request);
    await env.DB.prepare("DELETE FROM sessions WHERE user_id = ?").bind(id).run().catch(()=>{});
    await env.DB.prepare("UPDATE orders SET user_id = NULL WHERE user_id = ?").bind(id).run().catch(()=>{});
    await env.DB.prepare("UPDATE messages SET user_id = NULL WHERE user_id = ?").bind(id).run().catch(()=>{});
    await env.DB.prepare("DELETE FROM users WHERE id = ?").bind(id).run();
    return json({ ok:true, deleted:id, email: target.email }, 200, env, request);
  }
  let users;
  try { users = await env.DB.prepare("SELECT id, email, discord, created_at, email_verified, role FROM users ORDER BY id DESC LIMIT 500").all(); }
  catch { users = await env.DB.prepare("SELECT id, email, discord, created_at FROM users ORDER BY id DESC LIMIT 500").all(); }
  const userList = (users.results || []).map(u => Object.assign({}, publicUser(env, u), { email_verified: Number(u.email_verified || 0) ? 1 : 0 }));
  const stats = await env.DB.prepare("SELECT user_id, COUNT(*) AS orders, SUM(CASE WHEN status IN ('finished','confirmed','sending','paid') THEN 1 ELSE 0 END) AS paid FROM orders GROUP BY user_id").all();
  const msgs  = await env.DB.prepare("SELECT user_id, COUNT(*) AS messages FROM messages GROUP BY user_id").all();
  return json({ ok:true, users: userList, stats: stats.results || [], messageStats: msgs.results || [], assignableRoles: ASSIGNABLE_ROLES.slice(), adminEmailsConfigured: adminEmails(env).length }, 200, env, request);
}
async function handleAdminMessages(request, env, url){
  if (!hasDb(env)) return json({ error:"DB missing" }, 503, env, request);
  const limit = Math.min(Math.max(Number(url.searchParams.get("limit") || 300) || 300, 1), 500);
  const rows = await env.DB.prepare("SELECT m.id, m.order_id, m.sender, m.body, m.created_at, u.email AS user_email FROM messages m LEFT JOIN users u ON u.id = m.user_id ORDER BY m.id DESC LIMIT ?").bind(limit).all();
  return json({ ok:true, messages: rows.results || [] }, 200, env, request);
}
async function handleAdminOrderMessage(request, env, url){
  if (!hasDb(env)) return json({ error:"DB missing" }, 503, env, request);
  const m = url.pathname.match(/^\/api\/admin\/orders\/([^\/]+)\/message\/?$/);
  if (!m) return json({ error:"Invalid route" }, 404, env, request);
  const orderId = decodeURIComponent(m[1]);
  const ord = await env.DB.prepare("SELECT o.order_id, o.user_id, u.email AS user_email FROM orders o LEFT JOIN users u ON u.id = o.user_id WHERE o.order_id = ?").bind(orderId).first();
  if (!ord) return json({ error:"Order not found" }, 404, env, request);
  let body; try{ body=await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
  const text = String(body.body || body.message || body.text || "").trim().slice(0,4000);
  if (!text || text.length < 2) return json({ error:"Message too short" }, 400, env, request);
  await env.DB.prepare("INSERT INTO messages (order_id, user_id, sender, body) VALUES (?, ?, 'admin', ?)").bind(orderId, ord.user_id ?? null, text).run();
  const at = new Date().toLocaleString("en-GB",{ timeZone:"Europe/Berlin", dateStyle:"long", timeStyle:"short"});
  const notify = [];
  if (ord.user_email) notify.push(sendEmail(env, { to: ord.user_email, subject:`[Aether] Reply to your order ${orderId}`, html: orderHtml({ title:`Aether replied \u2014 ${orderId}`, fields: [["Order", orderId],["Message", text],["At", at]] }), text:`${text}\n\n\u2014 Aether support (order ${orderId})` }).catch(()=>null));
  notify.push(sendDiscord(env, { embeds: discordEmbed({ title:`\u2709\uFE0F Admin reply \u2014 ${orderId}`, color: DISCORD_ACCENT.paid, fields: [["Order ID", orderId, true],["To", ord.user_email || "guest checkout", true],["Message", text.slice(0,900), false]], description: `**Aether** \u2192 ${escMd(text.slice(0,400))}`, footer:`Admin reply \u2022 ${at}`, author:{name:"Aether Support"} }) }).catch(()=>null));
  await Promise.all(notify).catch(()=>{});
  const msgs = await env.DB.prepare("SELECT id, sender, body, created_at FROM messages WHERE order_id = ? ORDER BY id ASC").bind(orderId).all();
  return json({ ok:true, messages: msgs.results || [] }, 200, env, request);
}

// ---------- handlers (original + beautified) ----------
async function handleOrder(request, env){
  let body; try{ body=await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
  const type=String(body.type||"").trim();
  const amount=body.amount!=null ? Number(body.amount) : null;
  // 99999% -- order requests (website / discord bot) are ACCOUNT-ONLY, exactly like checkout.
  // There is no "leave your email and we will build it" path: the account supplies the reply
  // address and keeps the request in the portal chat. The plain contact form stays open to
  // anyone who writes in, because that is not a purchase.
  const isOrderRequest = type==="discord_bot" || type==="website";
  const auth=await requireAuth(request, env).catch(()=>null);
  if(isOrderRequest && !auth) return json({ error:"Order requests need an account \u2014 create one or sign in, and your request stays in your account chat.", code:"ACCOUNT_REQUIRED" }, 401, env, request);
  if(isOrderRequest){ const orderGate=verifiedGate(auth, env, request); if(orderGate) return orderGate; }
  const accountEmail=(auth && auth.user && isEmail(auth.user.email)) ? String(auth.user.email).trim() : "";
  const discord=String(body.discord||body.discordUsername||(auth && auth.user && auth.user.discord)||"").trim().slice(0,120);
  // The account email is the only reply address for an order request; a posted one cannot override it.
  let email=String(body.email||body.customerEmail||"").trim().slice(0,200);
  if(!isEmail(email) && accountEmail) email=accountEmail;
  if(isOrderRequest && accountEmail) email=accountEmail;
  const description=String(body.description||body.botDescription||body.websiteDescription||"").trim().slice(0,4000);
  const extra=String(body.extra||body.additionalInfo||"").trim().slice(0,4000);
  const meta= body.meta && typeof body.meta==="object" ? body.meta : {};
  const ip = clientIp(request);
  if(hitRL("order:" + ip, 12)) return json({ error:"Too many requests \u2014 slow down." }, 429, env, request);
  if(!isEmail(email)) return json({ error:"A valid email is required so we can reply \u2014 sign in and your account email is used automatically" }, 400, env, request);
  if(!description || description.length < 8) return json({ error:"Description too short" }, 400, env, request);
  if(body.website || body._gotcha) return json({ ok:true, mocked:true }, 200, env, request);
  const submitted=new Date().toLocaleString("en-GB",{ timeZone:"Europe/Berlin", dateStyle:"long", timeStyle:"short"});
  const title = type==="discord_bot" ? "New Discord Bot Order" : type==="website" ? "New Website Order" : "New Contact / Order";
  const fields=[
    ["Type", type||"\u2014"], ["Discord", discord||"\u2014"], ["Email", email],
    ["Package", String(body.package||body.tier||"Custom")],
    ["Estimated Price", amount!=null?`\u20AC${amount}`: String(body.estimatedPrice||"\u2014")],
    ["Submitted", submitted],
  ];
  const push=(k,v)=>{ if(v!=null && String(v).trim()!=="") fields.push([k, String(v).slice(0,2000)]); };
  if(auth) fields.push(["Account", `${auth.user.email} (#${auth.user.id})`]);
  else if(accountEmail) fields.push(["Account", "matched by email"]);
  if(type==="discord_bot"){
    push("Language", meta.language||body.language);
    push("Commands", String(meta.commands ?? body.commands ?? "\u2014"));
    push("Features", Array.isArray(meta.features)? meta.features.join(", "): String(meta.features||body.features||"\u2014"));
    push("Revisions", String(meta.revisions ?? body.revisions ?? "\u2014"));
    push("Required Commands", body.requiredCommands||body.commandsText);
  } else if(type==="website"){
    push("Website Type", meta.websiteType||body.websiteType);
    push("Style", meta.style||body.style);
    push("Pages", String(meta.pages ?? body.pages ?? "\u2014"));
    push("Complexity", meta.complexity||body.complexity);
    push("Features", Array.isArray(meta.features)? meta.features.join(", "): String(meta.features||body.features||"\u2014"));
    push("Revisions", String(meta.revisions ?? body.revisions ?? "\u2014"));
    push("Reference", body.reference||meta.reference);
    push("Features Text", body.featuresText);
  }
  fields.push(["Description", description]);
  if(extra) fields.push(["Additional Info", extra]);
  push("User Agent", request.headers.get("user-agent")||"");
  push("IP", ip);
  const text=fields.map(([k,v])=>`${k}: ${v}`).join("\n");
  const recipients=orderRecipients(env, type, body);
  const toList=recipients.length ? recipients : parseToList(env);
  const subject=`${title} \u2014 \u20AC${amount ?? body.estimatedPrice ?? "?"} \u2014 ${discord} \u2014 ${email}`.slice(0,160);
  const tasks=[];
  const emailTask = sendEmail(env, {
    to: toList, subject,
    html: orderHtml({ title, fields, note:"Reply directly to the customer \u2014 reply-to is set to their email. Hosting / DB / domain are not included." }),
    text: `${title}\n\n${text}\n`, replyTo: email,
  }).catch(e=>({ error:e.message }));
  const discordFields = fields.slice(0,14).map(([k,v])=> [k, String(v).slice(0,600)]);
  const discordColor = DISCORD_ACCENT.new;
  const discordDesc = `**${escMd(discord||"\u2014")}** \u00B7 ${escMd(email)} \u00B7 ${type} \u2014 ${String(body.package||"Custom")} \u00B7 \u20AC${amount ?? body.estimatedPrice ?? "?"}\n\n${escMd(description.slice(0,900))}${description.length>900?"\u2026":""}`;
  const discordTask = sendDiscord(env, {
    content: null,
    embeds: discordEmbed({ title: `\uD83D\uDCE9 ${title} \u2014 \u20AC${amount ?? body.estimatedPrice ?? "?"}`, color: discordColor, fields: discordFields, description: discordDesc, footer: `Aether Orders \u2022 ${submitted}`, author:{name:"Aether \u2014 New order"} })
  }).catch(e=>({ error:e.message }));
  tasks.push(emailTask, discordTask);
  const replyTask = sendEmail(env, {
    to: email, subject:`We got your request \u2014 ${title} \u2014 Aether`,
    html: orderHtml({ title:`Thanks, ${discord||"there"}! We got your request.`, fields:[
      ["What happens next","We\u2019ll review your request and reply within 3 days with a final quote + timeline. No payment is taken until you confirm."],
      ["Your request", description.slice(0,900)],
      ["Estimated Price", amount!=null? `\u20AC${amount} (estimate \u2014 final quote after review)` : "Custom \u2014 we\u2019ll quote after review"],
      ["Payment","Crypto only \u2014 Bitcoin, Litecoin, Ethereum via NOWPayments. Hosting / domain / DB not included."],
      ...(isOrderRequest && auth ? [["Your account","This request is filed in your account chat too \u2014 follow it up at https://get-aether.de/account.html"]] : []),
    ], note:"Need to add something? Just reply to this email (questions@get-aether.de or business@get-aether.de). Please add as many notes/instructions as you can in your original request \u2014 it speeds things up."}),
    text:`Thanks! We got your request.\n\n${text}\n\nWe\u2019ll reply within 3 days.`,
  }).catch(e=>({ error:e.message }));
  tasks.push(replyTask);
  const results = await Promise.all(tasks);
  const emailRes = results[0];
  if(emailRes && emailRes.error) return json({ error: emailRes.error }, 500, env, request);
  // An account-only request also belongs in the customer's own chat history, so they can follow
  // it up in the portal instead of hunting through an old email. This never fails the request:
  // the notification above has already gone out.
  let conversationId = "";
  if(isOrderRequest && auth && auth.user && env.DB){
    try{
      const convId = newConversationId();
      const now = new Date().toISOString();
      const subject = (type==="discord_bot" ? "Discord bot request" : "Website request") + " \u2014 " + String(body.package||body.tier||"Custom").slice(0,60);
      const summary = [
        subject,
        amount!=null ? "Estimate: \u20AC" + amount + " (confirmed before work starts)" : "Estimate: custom \u2014 we will quote after review",
        "",
        description,
        extra ? "\nAdditional info: " + extra : "",
      ].join("\n");
      await env.DB.prepare("INSERT INTO conversations (conversation_id, user_id, order_id, purchase_id, subject, status, created_at, updated_at) VALUES (?,?,?,?,?,'open',?,?)")
        .bind(convId, auth.user.id, "", null, subject, now, now).run();
      const conv = { conversation_id: convId, order_id: "", purchase_id: null, subject, status: "open", created_at: now, updated_at: now };
      await insertConversationMessage(env, conv, "customer", summary, auth.user.id);
      await audit(env, auth.user, "conversation.create", convId, type);
      conversationId = convId;
    }catch(e){ conversationId = ""; }
  }
  return json({ ok:true, emailed: !(emailRes && emailRes.mocked), discord: !(results[1] && results[1].mocked) && !(results[1] && results[1].error), conversationId }, 200, env, request);
}

async function handleInvoice(request, env){
  let body; try{ body=await request.json(); }catch{ return json({ error:"Invalid JSON" }, 400, env, request); }
  let amount=Number(body.amount ?? body.price_amount ?? body.estimatedPrice);
  if(!Number.isFinite(amount)||amount<1) return json({ error:"Valid amount required" }, 400, env, request);
  if(amount>5000) return json({ error:"Amount too large" }, 400, env, request);
  // 99999% -- checkout is ACCOUNT-ONLY: there is no guest purchase. The account supplies the
  // customer email and Discord, the order is filed to it, and portal chat/history follow from it.
  const earlyAuth=await requireAuth(request, env).catch(()=>null);
  if(!earlyAuth) return json({ error:"Checkout requires an account \u2014 create one or sign in, then pay.", code:"ACCOUNT_REQUIRED" }, 401, env, request);
  const invoiceGate=verifiedGate(earlyAuth, env, request);
  if(invoiceGate) return invoiceGate;
  const accountEmail=(earlyAuth.user && isEmail(earlyAuth.user.email)) ? String(earlyAuth.user.email).trim() : "";
  const discord=String(body.discord||body.discordUsername||(earlyAuth.user && earlyAuth.user.discord)||"").trim().slice(0,120);
  // The account email always wins; a posted email is only a fallback for an unusable account row.
  const email=accountEmail || String(body.email||body.customerEmail||"").trim();
  if(!isEmail(email)) return json({ error:"Your account has no usable email address \u2014 contact questions@get-aether.de" }, 400, env, request);
  const currency=String(body.currency||body.price_currency||"eur").toLowerCase();
  const payCurrency=String(body.pay_currency||body.payCurrency||body.coin||"").toLowerCase().trim();
  if(!payCurrency) return json({ error:"Please select BTC, ETH or LTC" }, 400, env, request);
  if(!["btc","eth","ltc"].includes(payCurrency)) return json({ error:"Unsupported coin \u2014 use BTC, ETH or LTC" }, 400, env, request);
  const type=String(body.type||"custom").slice(0,40);
  const pkg=String(body.package||body.tier||"Custom").slice(0,40);
  const description=String(body.description||body.orderDescription||`${type} ${pkg} \u2014 Aether`).slice(0,200);
  const meta=body.meta||{};
  if(body.website||body._gotcha) return json({ ok:true, mocked:true }, 200, env, request);

  // promo integration
  const rawPromo = String(body.promoCode || body.promo_code || body.promo || body.code || "").trim();
  let discount = 0;
  let appliedPromo = null;
  let promoFinalAmount = amount;
  if (rawPromo) {
    const r = resolvePromo(env, rawPromo, amount);
    if (!r.valid) return json({ error: r.error || "Invalid promo code" }, 400, env, request);
    discount = r.discount;
    appliedPromo = r.code;
    promoFinalAmount = r.finalAmount;
    amount = promoFinalAmount;
  }

  const orderId=`aether_${Date.now()}_${Math.random().toString(36).slice(2,7)}`;
  // Purchase ID: server-generated, unique, never chosen or overwritten by the browser.
  // It is an identifier for support - NOT an authentication token (lookups still check the account).
  let purchaseId = "";
  if (hasDb(env)) { try { purchaseId = await uniquePurchaseId(env); } catch { purchaseId = newPurchaseId(); } }
  const submitted=new Date().toLocaleString("en-GB",{ timeZone:"Europe/Berlin", dateStyle:"long", timeStyle:"short"});

  // link to the authenticated user -- already resolved above so the email could be derived from it
  const auth = earlyAuth;
  // Link the order to an account: the signed-in user, or whichever account owns this email.
  // That way "checkout with your account email" files the order to your portal automatically.
  let linkedUserId = auth.user.id; // account-only checkout: the order always belongs to the session account
  if (!linkedUserId && hasDb(env)) {
    try {
      const owner = await env.DB.prepare("SELECT id FROM users WHERE lower(email) = lower(?)").bind(email).first();
      if (owner) linkedUserId = owner.id;
    } catch{}
  }

  const pendingFields=[
    ["Type", `${type} \u2014 ${pkg}`, true], ["Amount", `\u20AC${amount} (${currency.toUpperCase()})${appliedPromo ? ` \u2022 promo ${appliedPromo} \u2212\u20AC${discount}` : ""}`, true],
    ["Crypto", payCurrency.toUpperCase(), true],
    ["Discord", discord||"\u2014 (none)", true], ["Email", email, true], ["Order ID", orderId, false], ["Purchase ID", purchaseId || "\u2014", true], ["Submitted", submitted, true], ["Description", description, false],
  ];
  if(appliedPromo) pendingFields.splice(1,0,["Promo", `${appliedPromo} \u2212\u20AC${discount} \u2192 \u20AC${amount}`, true]);
  if (auth) pendingFields.push(["Account", `${auth.user.email} (#${auth.user.id})`, true]);
  else if (linkedUserId) pendingFields.push(["Account", `matched by email (#${linkedUserId})`, true]);
  if(meta && typeof meta==="object"){
    for(const [k,v] of Object.entries(meta).slice(0,20)){
      if(v==null||String(v).trim()==="") continue;
      pendingFields.push([k, Array.isArray(v)? v.join(", "): String(v).slice(0,1000), false]);
    }
  }
  if(body.extra) pendingFields.push(["Extra", String(body.extra).slice(0,2000), false]);
  // store order in D1 (99999% \u2014 don't fail the payment if D1 is down, just warn)
  if (hasDb(env)) {
    try {
      await env.DB.prepare("INSERT INTO orders (user_id, order_id, amount, currency, type, package, description, status, promo_code, discount, meta, extra, email) VALUES (?, ?, ?, ?, ?, ?, ?, 'pending', ?, ?, ?, ?, ?)").bind(
        linkedUserId, orderId, amount, currency, type, pkg, description, appliedPromo, discount, JSON.stringify(meta||{}).slice(0,4000), String(body.extra||"").slice(0,2000), email
      ).run();
    } catch(e){ console.warn("D1 order insert failed", e && e.message); }
    // Store the Purchase ID separately so a database that predates the column still takes the order.
    if (purchaseId) { try { await env.DB.prepare("UPDATE orders SET purchase_id = ? WHERE order_id = ?").bind(purchaseId, orderId).run(); } catch(e){ console.warn("purchase_id column missing", e && e.message); } }
  }
  if (!hasDb(env)) purchaseId = "";

  let emailOk=true;
  const toList = ["questions@get-aether.de"];
  try{
    await Promise.all([
      sendEmail(env, {
        to: toList,
        subject:`[PENDING] ${type} ${pkg} \u2014 ${payCurrency.toUpperCase()} \u2014 \u20AC${amount} \u2014 ${discord||"\u2014"} \u2014 ${orderId}`.slice(0,180),
        html: orderHtml({ title:`New payment started \u2014 ${payCurrency.toUpperCase()} \u2014 \u20AC${amount} \u2014 ${orderId}`, fields: pendingFields, note:`Customer started a NOWPayments Payment (pay_currency=${payCurrency.toUpperCase()}) \u2014 IPN will be sent to ${FIXED_IPN_URL}. You\u2019ll get a \u2705 PAID email when status becomes confirmed/finished.`}),
        text: pendingFields.map(([k,v])=>`${k}: ${v}`).join("\n"), replyTo: email,
      }),
      sendDiscord(env, {
        embeds: discordEmbed({
          title: `\u23F3 Payment started \u2014 ${payCurrency.toUpperCase()} \u00B7 \u20AC${amount} \u00B7 ${type} ${pkg}`,
          color: DISCORD_ACCENT.pending,
          fields: pendingFields.slice(0,18),
          description: `${escMd(discord||"\u2014")} \u00B7 ${escMd(email)} \u00B7 **${payCurrency.toUpperCase()} \u2192 \u20AC${amount}**${appliedPromo ? ` \u00B7 \uD83C\uDFF7\uFE0F ${escMd(appliedPromo)} \u2212\u20AC${discount}` : ""} \u2014 IPN \u2192 ${FIXED_IPN_URL} \u2014 you\u2019ll get a \u2705 **PAID** ping on **confirmed/finished**.`,
          footer: `Order ${orderId} \u2022 ${submitted} \u2022 ${FIXED_IPN_URL}`,
          author: { name: "Aether Payments \u2014 PENDING" }
        })
      }).catch(()=>null)
    ]);
  }catch(e){ console.warn("pending notify failed", e.message); emailOk=false; }

  try{
    const payment=await createNowPaymentsPayment(env, { amount, currency, payCurrency, orderId, description });
    const payAddr = payment.pay_address || payment.payAddress || null;
    const payAmt = payment.pay_amount || payment.payAmount || null;
    const pid = payment.payment_id || payment.paymentId || payment.id || null;
    if (hasDb(env) && pid) {
      try { await env.DB.prepare("UPDATE orders SET payment_id = ?, status = 'waiting' WHERE order_id = ?").bind(String(pid), orderId).run(); } catch{}
    }
    return json({
      ok:true, orderId, purchaseId,
      invoiceId: pid,
      paymentId: pid,
      invoiceUrl: payment.invoice_url || payment.invoiceUrl || null,
      payAddress: payAddr,
      payAmount: payAmt,
      payCurrency: payCurrency,
      priceAmount: payment.price_amount ?? amount,
      priceCurrency: payment.price_currency || currency,
      promoApplied: appliedPromo, discount,
      emailOk,
    }, 200, env, request);
  }catch(e){
    if(String(e.message).includes("NOWPAYMENTS_API_KEY")){
      return json({ ok:false, error:"NOWPayments not configured yet \u2014 order email + Discord were sent, but crypto checkout is not enabled. Add NOWPAYMENTS_API_KEY.", orderId, purchaseId, emailOk }, 503, env, request);
    }
    if(String(e.message).toLowerCase().includes("pay_currency")){
      return json({ error: e.message }, 400, env, request);
    }
    return json({ error:e.message||"Payment failed" }, 500, env, request);
  }
}

async function handleIpn(request, env){
  const sig=request.headers.get("x-nowpayments-sig") || request.headers.get("x-nowpayments-sig".toLowerCase()) || "";
  const raw=await request.text();
  let payload; try{ payload=JSON.parse(raw); }catch{ payload={ raw }; }
  const secrets=[env.NOWPAYMENTS_IPN_SECRET, env.NOWPAYMENTS_IPN_SECRET_2].filter(Boolean);
  if(secrets.length){
    let ok=false;
    for(const s of secrets){ if(await verifyIpnSignature(payload, sig, s)){ ok=true; break; } }
    if(!ok){ console.warn("IPN bad signature", { sig:(sig ? sig.slice(0,20) : ""), payload:raw.slice(0,500)}); return json({ error:"Bad signature" }, 401, env, request); }
  } else { console.warn("IPN without NOWPAYMENTS_IPN_SECRET \u2014 accepting but you should set it"); }

  const status=String(payload.payment_status||payload.status||"").toLowerCase();
  const orderId=String(payload.order_id||payload.orderId||"\u2014");
  const priceAmount=payload.price_amount ?? payload.priceAmount ?? "\u2014";
  const payCurrency=payload.pay_currency||payload.payCurrency||"\u2014";
  const payAmount=payload.pay_amount||payload.payAmount||"\u2014";
  const paymentId=String(payload.payment_id||payload.paymentId||"");
  const isPaid=["finished","confirmed","sending"].includes(status);
  const isPartiallyPaid = status === "partially_paid";
  const isFailed=["failed","expired","refunded"].includes(status);
  const isPending=["waiting","confirming","sending"].includes(status) && !isPaid;

  // Attach the customer-facing Purchase ID to every notification (support can search by it).
  let ipnPurchaseId = "";
  if (hasDb(env) && orderId && orderId !== "\u2014") {
    try { const row = await env.DB.prepare("SELECT purchase_id FROM orders WHERE order_id = ?").bind(orderId).first(); if (row && row.purchase_id) ipnPurchaseId = String(row.purchase_id); } catch {}
  }

  // update D1 order status
  if (hasDb(env) && paymentId) {
    try {
      let dbStatus = status;
      if (isPaid) dbStatus = "paid"; else if (isFailed) dbStatus = "failed"; else if (isPartiallyPaid) dbStatus = "partially_paid";
      await env.DB.prepare("UPDATE orders SET status = ? WHERE payment_id = ? OR order_id = ?").bind(dbStatus, paymentId, orderId).run().catch(()=>{});
    } catch {}
  } else if (hasDb(env) && orderId) {
    try { await env.DB.prepare("UPDATE orders SET status = ? WHERE order_id = ?").bind(status, orderId).run().catch(()=>{}); } catch{}
  }

  const fields=[
    ["Status", status||"unknown", true], ["Order ID", orderId, false], ["Purchase ID", ipnPurchaseId || "\u2014", true],
    ["Price", `${priceAmount} ${payload.price_currency||""}`.trim(), true],
    ["Pay", `${payAmount} ${String(payCurrency).toUpperCase()}`.trim(), true],
    ["Payment ID", String(payload.payment_id||payload.paymentId||"\u2014"), true],
    ["Description", String(payload.order_description||payload.orderDescription||"\u2014").slice(0,800), false],
    ["Customer Email", String(payload.customer_email||payload.customerEmail||"\u2014"), true],
  ];
  for(const [k,v] of Object.entries(payload).slice(0,30)){
    if(["payment_status","order_id","price_amount","pay_currency","pay_amount","payment_id","order_description"].includes(k)) continue;
    fields.push([k, typeof v==="object"? JSON.stringify(v).slice(0,1000): String(v).slice(0,500), true]);
  }
  let subjectPrefix = "\u2139\uFE0F IPN";
  if(isPaid) subjectPrefix = "\u2705 PAID";
  else if(isPartiallyPaid) subjectPrefix = "\u26A0\uFE0F PARTIALLY PAID";
  else if(isFailed) subjectPrefix = "\u274C FAILED";
  else if(isPending || status === "waiting" || status === "confirming") subjectPrefix = "\u23F3 PENDING \u2014 " + status.toUpperCase();
  const title=`${subjectPrefix} \u2014 ${status.toUpperCase()} \u2014 ${orderId}`;
  const toList=["questions@get-aether.de"];
  const emoji = isPaid ? "\uD83C\uDF89" : isPartiallyPaid ? "\u26A0\uFE0F" : isFailed ? "\u274C" : "\u23F3";

  try{
    await Promise.all([
      sendEmail(env, {
        to: toList,
        subject:`${title} \u2014 \u20AC${priceAmount} \u2014 ${payAmount} ${String(payCurrency).toUpperCase()}`.slice(0,180),
        html: orderHtml({ title: `${emoji} ${title}`, fields, note: raw.slice(0,6000) }),
        text: fields.map(([k,v])=>`${k}: ${v}`).join("\n")+`\n\nRaw:\n${raw.slice(0,4000)}`,
      }).catch(e=>{ console.error("IPN email failed", e.message); }),
      sendDiscord(env, {
        embeds: discordEmbed({
          title, color: isPaid? DISCORD_ACCENT.paid : isPartiallyPaid? DISCORD_ACCENT.partially : isFailed? DISCORD_ACCENT.failed : DISCORD_ACCENT.pending,
          fields: fields.slice(0,18),
          description: isPaid ? `${emoji} Payment **${escMd(status)}** \u2014 ${escMd(String(priceAmount))} ${escMd(String(payload.price_currency||"EUR"))} \u2192 ${escMd(String(payAmount))} ${escMd(String(payCurrency).toUpperCase())} \u2014 **99999% reliable \u2705**` : isPartiallyPaid ? `\u26A0\uFE0F Partially paid \u2014 ${escMd(status)} \u2014 ${escMd(orderId)} \u2014 check amount` : `Status: **${escMd(status)}** \u2014 ${escMd(orderId)}`,
          footer: `NOWPayments IPN \u2022 ${new Date().toLocaleString("en-GB",{timeZone:"Europe/Berlin"})} \u2022 ${FIXED_IPN_URL}`,
          author: { name: isPaid ? "Aether Payments \u2014 PAID \u2705" : isFailed ? "Aether Payments \u2014 FAILED" : "Aether Payments \u2014 IPN" }
        })
      }).catch(e=>{ console.error("IPN discord failed", e.message); })
    ]);
    const customerEmail=payload.customer_email||payload.customerEmail;
    if(isPaid && customerEmail && isEmail(customerEmail)){
      await sendEmail(env, {
        to: customerEmail,
        subject:`Payment received \u2014 ${orderId} \u2014 Aether \u2705`,
        html: orderHtml({ title:`Thanks! Your payment is ${status} \uD83C\uDF89`, fields:[
          ["Order", orderId], ["Purchase ID", ipnPurchaseId || "\u2014"], ["Status", status], ["Amount", `${priceAmount} ${payload.price_currency||"EUR"}`],
          ["What happens next","We\u2019ll start your order and reply from questions@get-aether.de or business@get-aether.de within 24h. Keep this email for your records."],
        ], note:"Questions? Reply to questions@get-aether.de"}),
        text:`Payment ${status} for ${orderId} \u2014 thanks!\nWe\u2019ll reply shortly.`,
      }).catch(()=>{});
    }
  }catch(e){ console.error("IPN notify failed", e.message); }
  return json({ ok:true, status }, 200, env, request);
}

async function handlePaymentStatus(request, env, url){
  let paymentId = "";
  const path = url.pathname;
  const m = path.match(/^\/api\/payment\/([^\/\?]+)$/);
  if(m) paymentId = decodeURIComponent(m[1]);
  if(!paymentId || paymentId.toLowerCase()==="status") paymentId = url.searchParams.get("payment_id") || url.searchParams.get("paymentId") || url.searchParams.get("id") || "";
  paymentId = String(paymentId||"").trim();
  if(!paymentId || paymentId.toLowerCase()==="status") return json({ error:"payment_id required \u2014 use /api/payment/:payment_id or ?payment_id=..." }, 400, env, request);
  if(!/^\d+$/.test(paymentId)) return json({ error:"Invalid payment_id" }, 400, env, request);
  const key = env.NOWPAYMENTS_API_KEY;
  if(!key) return json({ error:"Payment verification not configured" }, 503, env, request);
  try{
    const res = await fetch(`https://api.nowpayments.io/v1/payment/${encodeURIComponent(paymentId)}`, { headers: { "x-api-key": key } });
    const text = await res.text();
    let data; try{ data=JSON.parse(text);}catch{ data={ raw:text }; }
    if(!res.ok) return json({ error: "Payment could not be verified with the provider" }, 502, env, request);
    const status = String(data.payment_status || data.status || "").toLowerCase();
    const isPaid = ["finished","confirmed","sending"].includes(status);
    const isPending = ["waiting","confirming"].includes(status);
    const isFailed = ["failed","expired","refunded"].includes(status);
    // Minimal disclosure: status + amounts only. No provider payload, no customer PII.
    return json({ ok:true, paymentId, status, isPaid, isPending, isFailed, price_amount: data.price_amount, price_currency: data.price_currency, pay_amount: data.pay_amount, pay_currency: data.pay_currency, order_id: data.order_id }, 200, env, request);
  }catch(e){
    return json({ error: e.message || "Verification failed" }, 500, env, request);
  }
}

export default {
  async fetch(request, env, ctx){
    try {
      return await handleRequest(request, env, ctx);
    } catch (e) {
      // Never leak a bare 1101 - always answer with JSON we can actually debug.
      // Log the details server-side, return nothing an attacker can mine.
      console.error("aether worker error", (e && e.stack) || e);
      return json({ error:"Internal error \u2014 please try again." }, 500, env, request);
    }
  }
};
async function handleRequest(request, env, ctx){
    const url=new URL(request.url); const path=url.pathname;
    if(request.method==="OPTIONS") return new Response(null, { status:204, headers:{ ...SECURITY_HEADERS, ...corsHeaders(env, request) } });
    // Reject oversized payloads before any JSON parsing (cheap DoS guard).
    if(bodyTooLarge(request)) return json({ error:"Payload too large" }, 413, env, request);
    // CSRF / cross-site write guard: untrusted browser origins cannot POST here.
    // NOWPayments IPNs are exempt - they carry an HMAC signature instead of an Origin.
    if(["POST","PUT","PATCH","DELETE"].includes(request.method) && !isIpnPath(path) && !writeOriginAllowed(env, request)){
      return json({ error:"Request blocked \u2014 untrusted origin" }, 403, env, request);
    }

    // admin (must be matched before /api/orders)
    if(path.startsWith("/api/admin") || path.startsWith("/admin/")){
      const admin = await requireAdmin(request, env);
      if(!admin) return json({ error:"Admin only" }, 403, env, request);
      const rl = await rlGuard(request, env, "admin", 2000, 3600000); if(rl) return rl;
      const apath = path.startsWith("/api/admin") ? path : "/api/admin" + path.slice(6);
      const aurl = new URL(request.url); aurl.pathname = apath;
      // Beta configuration + Tester role management + support conversations + audit trail.
      if(apath.startsWith("/api/admin/beta")) return handleAdminBeta(request, env, aurl);
      if(apath==="/api/admin/audit"||apath==="/api/admin/audit/") return handleAdminAudit(request, env);
      if(/^\/api\/admin\/users\/\d+\/role\/?$/.test(apath)) return handleAdminRole(request, env, aurl);
      if(apath==="/api/admin/conversations"||apath==="/api/admin/conversations/"||/^\/api\/admin\/conversations\/[^\/]+(\/(messages|status))?\/?$/.test(apath)) return handleAdminConversations(request, env, aurl);
      if(apath==="/api/admin/orders"||apath==="/api/admin/orders/") return handleAdminOrders(request, env, aurl);
      if(/^\/api\/admin\/orders\/[^\/]+\/message\/?$/.test(apath)) return request.method==="POST" ? handleAdminOrderMessage(request, env, aurl) : json({ error:"Method not allowed" }, 405, env, request);
      if(/^\/api\/admin\/orders\/[^\/]+\/?$/.test(apath)) return handleAdminOrders(request, env, aurl);
      if(apath==="/api/admin/users"||apath==="/api/admin/users/") return handleAdminUsers(request, env, aurl);
      if(/^\/api\/admin\/users\/\d+\/?$/.test(apath)) return handleAdminUsers(request, env, aurl);
      if(apath==="/api/admin/messages"||apath==="/api/admin/messages/") return handleAdminMessages(request, env, aurl);
      if(apath==="/api/admin"||apath==="/api/admin/") return json({ ok:true, admin: admin.user.email, routes:["GET /api/admin/orders","GET /api/admin/orders/:id","POST /api/admin/orders/:id/message","GET /api/admin/users","DELETE /api/admin/users/:id","POST /api/admin/users/:id/role","GET /api/admin/messages","GET /api/admin/conversations","POST /api/admin/conversations/:id/messages","GET /api/admin/beta","POST /api/admin/beta/domain/request","POST /api/admin/beta/domain/confirm","POST /api/admin/beta/flags","GET /api/admin/audit"] }, 200, env, request);
      return json({ error:"Not found", path }, 404, env, request);
    }

    // payment verify
    if(request.method==="GET" && (path.startsWith("/api/payment") || path.startsWith("/payment") || path==="/api/status" || path==="/status")){
      // generous: the success page polls every 8s while a payment confirms
      const rl = await rlGuard(request, env, "payment", 900, 3600000); if(rl) return rl;
      return handlePaymentStatus(request, env, url);
    }
    // promo
    if(path==="/api/promo" || path==="/promo" || path.startsWith("/api/promo/")){
      if(request.method==="GET" || request.method==="POST"){
        const rl = await rlGuard(request, env, "promo", 120, 3600000); if(rl) return rl;
        return handlePromo(request, env, url);
      }
    }
    // auth - the strictest rate limits in the worker live on this block
    if(path==="/api/auth/register" || path==="/auth/register"){
      if(request.method!=="POST") return json({error:"Method not allowed"},405,env,request);
      const rl = await rlGuard(request, env, "register", 6, 3600000); if(rl) return rl;
      return handleRegister(request, env);
    }
    if(path==="/api/auth/login" || path==="/auth/login"){
      if(request.method!=="POST") return json({error:"Method not allowed"},405,env,request);
      const rl = await rlGuard(request, env, "login", 12, 900000); if(rl) return rl;
      return handleLogin(request, env);
    }
    if(path==="/api/auth/logout" || path==="/auth/logout") return request.method==="POST" ? handleLogout(request, env) : json({error:"Method not allowed"},405,env,request);
    if(path==="/api/auth/forgot" || path==="/api/auth/forgot-password"){
      if(request.method!=="POST") return json({error:"Method not allowed"},405,env,request);
      const rl = await rlGuard(request, env, "forgot", 6, 3600000); if(rl) return rl;
      return handleForgotPassword(request, env);
    }
    if(path==="/api/auth/reset" || path==="/api/auth/reset-password"){
      if(request.method!=="POST") return json({error:"Method not allowed"},405,env,request);
      const rl = await rlGuard(request, env, "reset", 12, 3600000); if(rl) return rl;
      return handleResetPassword(request, env);
    }
    if(path==="/api/auth/password" || path==="/api/auth/change-password"){
      if(request.method!=="POST") return json({error:"Method not allowed"},405,env,request);
      const rl = await rlGuard(request, env, "password", 10, 3600000); if(rl) return rl;
      return handleChangePassword(request, env);
    }
    if(path==="/api/auth/verify-email" || path==="/api/auth/verify"){
      if(request.method!=="POST") return json({error:"Method not allowed"},405,env,request);
      const rl = await rlGuard(request, env, "verify-email", 30, 3600000); if(rl) return rl;
      return handleVerifyEmail(request, env);
    }
    if(path==="/api/auth/resend-verification" || path==="/api/auth/resend"){
      if(request.method!=="POST") return json({error:"Method not allowed"},405,env,request);
      const rl = await rlGuard(request, env, "resend-verification", 10, 3600000); if(rl) return rl;
      return handleResendVerification(request, env);
    }
    // conversations + purchases (protected customer data, ownership always taken from the session)
    if(/^\/api\/conversations\/[^\/]+\/(messages|status)\/?$/.test(path)){
      const rl = await rlGuard(request, env, "chat-send", 120, 600000); if(rl) return rl;
      return handleConversationAction(request, env, url);
    }
    if(path==="/api/conversations" || path==="/api/conversations/" || /^\/api\/conversations\/[^\/]+\/?$/.test(path)){
      const rl = await rlGuard(request, env, "chat", 400, 3600000); if(rl) return rl;
      return handleConversations(request, env, url);
    }
    if(path==="/api/purchases" || path.startsWith("/api/purchases/")){
      const rl = await rlGuard(request, env, "purchases", 400, 3600000); if(rl) return rl;
      return handlePurchases(request, env, url);
    }
    // Beta (Tester-only). Every endpoint re-checks session + verified email + role server-side.
    if(path==="/api/beta/access"||path==="/api/beta/access/"){
      const rl = await rlGuard(request, env, "beta", 600, 3600000); if(rl) return rl;
      return handleBetaAccess(request, env);
    }
    if(path==="/api/beta/status"||path==="/api/beta/status/"){
      if(request.method!=="GET") return json({error:"Method not allowed"},405,env,request);
      const rl = await rlGuard(request, env, "beta", 600, 3600000); if(rl) return rl;
      return handleBetaStatus(request, env);
    }
    if(path==="/api/beta/feedback"||path==="/api/beta/feedback/"){
      if(request.method!=="POST") return json({error:"Method not allowed"},405,env,request);
      const rl = await rlGuard(request, env, "beta-feedback", 60, 3600000); if(rl) return rl;
      return handleBetaFeedback(request, env);
    }
    if(path==="/api/beta/ticket"||path==="/api/beta/ticket/"){
      if(request.method!=="POST") return json({error:"Method not allowed"},405,env,request);
      const rl = await rlGuard(request, env, "beta-ticket", 60, 3600000); if(rl) return rl;
      return handleBetaTicket(request, env);
    }
    if(path==="/api/beta/redeem"||path==="/api/beta/redeem/"){
      if(request.method!=="POST") return json({error:"Method not allowed"},405,env,request);
      const rl = await rlGuard(request, env, "beta-redeem", 120, 3600000); if(rl) return rl;
      return handleBetaRedeem(request, env);
    }
    if(path==="/api/me" || path==="/me") return request.method==="DELETE" ? handleDeleteMe(request, env) : handleMe(request, env);
    // orders + chat (more specific first)
    if(path.match(/^\/api\/orders\/[^\/]+\/message\/?$/)) return handleOrderMessage(request, env, url);
    if(path.startsWith("/api/orders") || path.startsWith("/orders")){
      const p = path.replace("/api","") || "/orders";
      if(p.startsWith("/orders")){
        const rl = await rlGuard(request, env, "orders", 2000, 3600000); if(rl) return rl;
        return handleOrders(request, env, url);
      }
    }

    // Health
    if(path==="/api/health"||path==="/health"){
      const hasEmail=emailConfigured(env);
      const emailName=emailProvider(env);
      const hasNow=!!env.NOWPAYMENTS_API_KEY;
      const hasIpn=!!env.NOWPAYMENTS_IPN_SECRET;
      const hasDiscord=!!env.DISCORD_WEBHOOK_URL;
      const hasDb=!!env.DB;
      let dbOk = hasDb;
      if (hasDb) { try { await env.DB.prepare("SELECT 1").first(); } catch{ dbOk = false; } }
      // Public, but deliberately uninformative: booleans only - no addresses, no config echo.
      return json({ ok:true, service:"aether-api", time:new Date().toISOString(), email:hasEmail, emailProvider:emailName, payments:hasNow, ipnSignature:hasIpn, discord:hasDiscord, resetDelivery: resetDeliveryChannel(env), turnstile: !!env.TURNSTILE_SECRET, db: hasDb ? (dbOk ? true : "error") : false }, 200, env, request);
    }
    if(path==="/api/order" && request.method==="POST"){
      const rl = await rlGuard(request, env, "order", 40, 3600000); if(rl) return rl;
      return handleOrder(request, env);
    }
    if((path==="/api/invoice"||path==="/api/create-invoice") && request.method==="POST"){
      const rl = await rlGuard(request, env, "invoice", 40, 3600000); if(rl) return rl;
      return handleInvoice(request, env);
    }
    if((path==="/api/ipn"||path==="/api/nowpayments/ipn"||path==="/api/nowpayments-ipn") && request.method==="POST") return handleIpn(request, env);
    if(path==="/order" && request.method==="POST") return handleOrder(request, env);
    if((path==="/invoice"||path==="/create-invoice") && request.method==="POST") return handleInvoice(request, env);
    if((path==="/ipn"||path==="/nowpayments/ipn"||path==="/nowpayments-ipn") && request.method==="POST") return handleIpn(request, env);
    if(path==="/api/contact" && request.method==="POST") {
      const rl = await rlGuard(request, env, "contact", 20, 3600000); if(rl) return rl;
      const clone = request.clone();
      let b; try{ b=await clone.json(); }catch{ b={}; }
      const patched = new Request(request.url.replace("/api/contact","/api/order"), { method:"POST", headers: request.headers, body: JSON.stringify({ type:"contact", ...b }) });
      return handleOrder(patched, env);
    }
    if(path==="/"||path==="") return json({ ok:true, service:"aether-api", docs:"https://get-aether.de" }, 200, env, request);
    return json({ error:"Not found", path }, 404, env, request);
}
