# Aether — security architecture

This document describes what is enforced **in code**, what is enforced by **Cloudflare
configuration**, and what still has to be done **manually in the Cloudflare dashboard**.
It deliberately does not claim the site is "100% secure" — no web application is.

Repository visibility: **assume this repo is public.** Anything committed here is world
readable forever. Secrets therefore live only in Cloudflare Worker Secrets.

---

## 1. Architecture

| Piece | Where it runs | Notes |
|---|---|---|
| Static site | GitHub Pages, proxied by Cloudflare | HTML/CSS/JS only, no secrets |
| API | Cloudflare Worker `aether-api` (`worker/src/index.js`) | one file, ASCII-only, no dependencies |
| API hostnames | `https://get-aether.de/api/*` (primary, same-origin) and `https://api.get-aether.de/*` (legacy/compat) | both routed to the same Worker |
| Database | Cloudflare D1 `aether-db` (binding `DB`) | `users`, `sessions`, `orders`, `messages`, `auth_tokens`, `rate_limits` |
| Email | Resend (Worker secret) | not configured yet; the API says so instead of pretending |
| Payments | NOWPayments Payment API (Worker secret) | IPN verified with HMAC-SHA512 |
| Deploy relay | Cloudflare KV namespace (chunked upload) | only because `wrangler` is not authenticated on this machine |

The frontend talks to the API **same-origin** by default (`public/aether-config.js`
→ `apiBase: ""`). That is what makes the session cookie first-party (`__Host-`,
`HttpOnly`, `Secure`, `SameSite=Lax`) instead of a third-party cookie.

---

## 2. Enforced in code (Worker)

* **Passwords**: PBKDF2-HMAC-SHA256, per-user random 16-byte salt, 100 000 iterations
  (the hard ceiling of the Workers runtime), constant-time comparison, min 8 chars with a
  letter and a number, max 200 chars.
* **Sessions**: 32-byte random tokens. D1 stores `SHA-256(token)` — a database leak cannot
  be replayed as a login. 14-day expiry, rotated on every login, capped at 10 per user,
  expired rows pruned. Logout/reset/delete invalidate server-side.
* **Cookies**: `__Host-aether_session; Path=/; HttpOnly; Secure` + `SameSite=Lax`
  (same-origin host) or `SameSite=None` only on the legacy `api.` host, where the client
  additionally sends a Bearer token.
* **CSRF**: every `POST/PUT/PATCH/DELETE` must come from an allowlisted `Origin` (or a
  matching `Referer`); otherwise `403`. The IPN endpoint is exempt because it is
  authenticated by HMAC signature instead. Combined with `SameSite=Lax` this closes
  classic cross-site request forgery.
* **CORS**: explicit origin allowlist, never a wildcard, never a reflected unknown origin.
  `ALLOWED_ORIGIN="*"` is intentionally **ignored** in favour of built-in defaults.
* **Rate limiting** (two layers): per-isolate burst counter + D1 fixed-window counters
  shared by every isolate. Login 12/15 min, register 6/h, forgot 6/h, reset 12/h,
  password change 10/h, verify-email 30/h, order 40/h, contact 20/h, invoice 40/h,
  promo 120/h, payment status 900/h, account reads 2000/h, admin 2000/h.
* **Password reset / change / email verification**: single-use tokens (30 min for reset,
  24 h for verification), stored only as hashes, invalidated on use, all other sessions
  dropped after a reset. Forgot-password answers identically for known and unknown
  addresses, so it cannot be used to enumerate accounts.
* **Turnstile**: tokens are verified server-side against `challenges.cloudflare.com`.
  Disabled until `TURNSTILE_SECRET` exists (fail-open on a captcha outage, never on a
  failed check).
* **Input handling**: type/length caps on every field, 64 KB body cap (413), honeypot
  fields, HTML escaping for emails, Markdown escaping for Discord, `slice()` limits on
  every stored/forwarded value.
* **SQL**: every query is parameterised (`prepare().bind()`); no string interpolation.
* **Output hygiene**: JSON-only responses with `nosniff`, `frame-ancestors 'none'` CSP,
  `no-store`, HSTS, `no-referrer`, `permissions-policy`, `cross-origin-resource-policy:
  same-site`, `x-robots-tag: noindex`. Crashes return `{"error":"Internal error"}`
  with details only in Worker logs. `/api/health` returns booleans, never addresses or
  configuration.
* **Access control**: `requireAdmin` is checked server-side on every `/api/admin/*` route
  before any query; the hidden admin UI is a convenience, not the control. Order and chat
  routes filter by `user_id`, so one customer cannot read another's order (IDOR-safe).
* **Payment status endpoint**: returns status + amounts only — no provider payload, no
  customer PII, no pay address.

## 3. Enforced by Cloudflare (already applied by API)

* DNS in Cloudflare, public records **proxied** (origin IP hidden); MX untouched.
* **Always Use HTTPS**: on. **Minimum TLS version**: 1.2. **TLS 1.3**: on.
* **HSTS**: enabled for 1 year, `includeSubDomains`, `preload` off, `nosniff` on.
* **Worker routes**: `get-aether.de/api/*` and `www.get-aether.de/api/*` → `aether-api`.
* **WAF**: Cloudflare Managed Free Ruleset deployed on the zone (SQLi, XSS, exploit
  signatures, bad bots handled by Cloudflare's signatures).
* **Edge rate limiting**: one rule (free-plan maximum), 10 requests / 10 seconds per
  `ip.src` + colo, blocked for 10 s, covering `/api/auth/*`, `/api/order`, `/api/contact`,
  `/api/invoice`, `/api/create-invoice`. The period/mitigation values are capped by the
  free plan; the Worker's own limits cover the longer windows.
* Bot Fight Mode is deliberately **off**: it cannot be skipped per path, and challenging
  server-to-server traffic (NOWPayments IPNs) would silently break payments.
* DDoS protection (L3/L4/L7) is always-on at Cloudflare. `security_level` is `medium`,
  `browser_check` is on.

## 4. Still to do manually in the Cloudflare dashboard

1. **Turnstile**: create a widget for `get-aether.de`, put the public site key in
   `public/aether-config.js` → `turnstileSiteKey`, and add the secret to the Worker as
   `TURNSTILE_SECRET` (both, or neither — enabling only one breaks or does nothing).
2. **Resend**: create the API key and set `RESEND_API_KEY` as a Worker secret. Until then
   password reset answers with an honest 503, no verification email is sent, and order
   emails stay in mock mode.
3. **SSL/TLS mode**: currently `Flexible/Full` without strict validation. Switch to
   **Full (strict)** once you have confirmed GitHub Pages serves a valid certificate for
   `get-aether.de` — `Full (strict)` was not enabled automatically because a mismatch there
   would take the whole site down.
4. **Cache rules**: `browser_cache_ttl` is 4 h. Consider a "Cache Everything" rule for
   `/public/*`, `*.css`, `*.js`, images only, and confirm that `/api/*` is never cached
   (the Worker already sends `cache-control: no-store`).
5. **WAF custom rules** (optional, paid plans add more): block non-browser access to
   `/api/*` except the IPN path; enable Cloudflare Access (Zero Trust) in front of
   anything admin-shaped; enable managed ruleset *anomaly* actions on Pro+.
6. **Bot Fight Mode / Super Bot Fight Mode**: only enable if you first exclude
   `/api/ipn` and any server-to-server path (not possible on the free plan — hence off).
7. **Alerts**: enable notifications for WAF/rate-limit spikes and Worker error rates.
8. **`ALLOWED_ORIGIN` variable**: currently `*` in the Worker vars, which the code ignores
   in favour of the built-in allowlist. Set it explicitly to
   `https://get-aether.de,https://www.get-aether.de,https://api.get-aether.de` so the
   dashboard reflects reality.

## 5. Secret handling

* Production secrets (Resend, NOWPayments, Discord webhook, optional Turnstile) exist only
  as Worker Secrets. Re-uploads keep them (`keep_bindings` is used by the deploy step).
* The frontend contains no keys, no tokens and no privileged logic. `public/aether-config.js`
  is intentionally public configuration only.
* `git grep` style audits are part of the release checklist; a copy of it lives in
  `MASTER_PROMPT.md`.
* Never "hide" a secret with base64, minification or obfuscation — those are not security
  mechanisms and the repo is public.

## 6. Reporting a problem

Email **questions@get-aether.de** with the details. Please do not open a public issue for
anything exploitable, and do not test against customers' accounts.
