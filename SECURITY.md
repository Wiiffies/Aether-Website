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
  `ALLOWED_ORIGIN` is now set explicitly to `https://get-aether.de,https://www.get-aether.de,https://api.get-aether.de`
  (a literal `*` is still intentionally **ignored** in favour of built-in defaults, so a bad
  dashboard edit can never turn the API into a wildcard).
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
* **Roles (User / Tester / Admin)**: stored in `users.role` and resolved on every request.
  `admin` can be derived **only** from the server-side `ADMIN_EMAILS` / `ADMIN_EMAIL`
  configuration — `POST /api/admin/users/:id/role` accepts `user` or `tester` and rejects
  `admin`, so neither a forged request nor a database row can escalate an account. No hidden
  or hardcoded admin account exists, and admin accounts cannot be demoted or deleted through
  the API.
* **Email verification gate**: protected customer data (purchases, conversations, Beta) is
  refused with `403 EMAIL_UNVERIFIED` until the address is confirmed. The gate is computed
  server-side and switches on automatically as soon as email delivery is configured (forced
  with `REQUIRE_EMAIL_VERIFICATION`); when no provider exists it stands down rather than
  locking every account out of its own data, and `/api/me` reports that honestly.
* **Account-only requests**: `POST /api/order` with `type: discord_bot|website` is the same rule —
  a signed-out visitor gets `401 ACCOUNT_REQUIRED`, the account email is the only reply address (a
  typed one is ignored), and the request is filed into that account's portal chat. The two builder
  pages have no email input at all, so there is nothing to type. Only the general `type: contact`
  form accepts an email from anyone, and it writes no order.
* **Account-only checkout**: `POST /api/invoice` requires a session — a signed-out buyer gets
  `401 {"code":"ACCOUNT_REQUIRED"}` and **no order row is written**. The order is owned by the
  session account (`user_id` comes from the session, never from the body) and the account's own
  email is the one invoices and updates go to; a posted `email` is only a fallback for an account
  row with no usable address. `/api/order` (the free inquiry form) never creates an order, so there
  is no email-only path into the order table. Legacy rows written by the old guest checkout are
  claimed when that email registers — they are not deletable from the frontend.
* **Purchase IDs** (`AETH-YYYY-XXXXXXXX`): generated server-side from a CSPRNG, unique
  (unique index + collision retry), ambiguity-free alphabet, lazily backfilled for older
  orders. A Purchase ID is an identifier, not a credential: `GET /api/purchases/:id` still
  filters by the session account, so knowing (or guessing) someone else's ID reveals nothing.
* **Conversations**: the owner is always taken from the session, never from the request.
  Reading, posting, closing or attaching a Purchase ID to another account's thread returns
  `404`; customers cannot reach each other's threads even with a correct conversation id, and
  the admin/support path is a separate `/api/admin/conversations/*` route behind
  `requireAdmin`.
* **Chat input**: control characters stripped, 4000-character hard cap (server-side), rate
  limited per route, stored as plain text and escaped at render time — no HTML is executed.
* **Legacy order chat**: pre-existing per-order threads (messages keyed only by `order_id`)
  are surfaced inside the account's own conversation history; the ownership filter is applied
  in the same query, so the bridge cannot leak another account's messages.
* **Beta access**: `/api/beta/*` requires a session + verified email + Tester/Admin role on
  every call. `/api/beta/access` answers `allowed: false` and returns **no URL** to everyone
  else, and the obscure Beta path is treated as obscurity only. Cross-host sessions use a
  single-use, 120-second, hashed ticket.
* **Beta configuration changes**: admin session + verified email + password re-entry, then a
  64-hex single-use token (30 min, stored hashed, payload in `auth_tokens.payload`) emailed to
  the admin. The confirmation re-checks that the account is still an admin before the setting
  is written, and both steps are audit-logged. A browser request alone cannot change it.
* **Audit log**: `tester.grant`, `tester.revoke`, `conversation.*`, `beta.domain.request`,
  `beta.domain.confirm`, `beta.flags`, `beta.ticket`, `beta.redeem` are recorded with actor,
  target, detail and timestamp; readable only through `/api/admin/audit`.

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
3. **SSL/TLS mode is `Full`, and it must stay `Full` while GitHub Pages is the origin — tested
   2026-10-01, it is NOT a safe change.** Cloudflare `Full (strict)` was enabled, the whole site
   instantly answered **HTTP 526 (origin SSL handshake failed)** and the setting was reverted
   within minutes. Cause, measured with `openssl s_client -connect 185.199.108.153:443 -servername
   get-aether.de`: GitHub Pages serves `subject=CN=*.github.io` for this hostname — no certificate
   for `get-aether.de` has been provisioned, so there is nothing for strict validation to accept.
   `Full` still encrypts the Cloudflare→origin leg, it just does not validate that certificate.
   Two legitimate ways to reach `Full (strict)`: (a) serve the static site from Cloudflare Pages or a
   Worker with static assets, which get a Cloudflare-managed certificate, or (b) let GitHub Pages
   provision a certificate for `get-aether.de` first — which needs the Cloudflare proxy temporarily
   switched to DNS-only so GitHub's validation can reach it, then the proxy re-enabled and the
   certificate checked with `openssl`. Do not flip this setting “to be safer” without doing one of
   those first; when in doubt, check the live site within 30 seconds of any SSL/TLS change.
4. **Cache rules**: `browser_cache_ttl` is 4 h. Consider a "Cache Everything" rule for
   `/public/*`, `*.css`, `*.js`, images only, and confirm that `/api/*` is never cached
   (the Worker already sends `cache-control: no-store`).
5. **WAF custom rules** (optional, paid plans add more): block non-browser access to
   `/api/*` except the IPN path; enable Cloudflare Access (Zero Trust) in front of
   anything admin-shaped; enable managed ruleset *anomaly* actions on Pro+.
6. **Bot Fight Mode / Super Bot Fight Mode**: only enable if you first exclude
   `/api/ipn` and any server-to-server path (not possible on the free plan — hence off).
7. **Alerts**: enable notifications for WAF/rate-limit spikes and Worker error rates.
8. **`ALLOWED_ORIGIN` variable — done (2026-10-01)**: set explicitly to
   `https://get-aether.de,https://www.get-aether.de,https://api.get-aether.de`. A wildcard is
   still ignored by code, so a future dashboard accident cannot open the API up.
9. **Admin email variable — done (2026-10-01)**: `ADMIN_EMAIL=alex.real.apple@gmail.com` is set
   and the guessed `ADMIN_EMAILS=Wispz@outlook.de` was **emptied**, so the only admin identity is
   the real owner. The value must never appear in frontend code. How it was changed without
   losing anything: the bindings were re-sent as a multipart metadata `bindings` list together
   with `keep_bindings:["secret_text"]`, which preserved all four Worker secrets and the D1
   binding — proven by `/api/health` still reporting `payments:true`, `ipnSignature:true`,
   `discord:true`, `db:true` afterwards. (Do **not** try to change these with a bare
   `PATCH /settings` call: that endpoint insists on multipart.)
10. **Beta hostname**: create the DNS record for `betatester.get-aether.de`, route
    `betatester.get-aether.de/api/*` to the Beta Worker, and serve the Beta branch build at
    the configured long path. The admin panel's Beta tab writes `settings.beta_host` /
    `settings.beta_path`, but DNS and routing are dashboard work.
11. **Beta data isolation**: give the Beta its own Worker, KV namespace and D1 database
    (with the same schema), and its own secrets. Never copy payment/email/webhook secrets from
    production into it. Set `AETHER_ENV=beta` there.
12. **Edge rate limit for chat + Beta** (free plan allows a single rule, currently used by the
    auth/forms rule): on a paid plan add a second rule for `/api/conversations*`,
    `/api/beta/*` and `/api/admin/*`. Until then the Worker's own per-route limits apply.
13. **Email verification**: once `RESEND_API_KEY` is set the verification gate turns on by
    itself. Confirm the sending domain in Resend first, then register a test account and walk
    the flow end to end (register → email → confirm → purchases unlock).

## 5. Secret handling

* Production secrets (Resend, NOWPayments, Discord webhook, optional Turnstile) exist only
  as Worker Secrets. Re-uploads keep them (`keep_bindings` is used by the deploy step).
* The frontend contains no keys, no tokens and no privileged logic. `public/aether-config.js`
  is intentionally public configuration only.
* `git grep` style audits are part of the release checklist; a copy of it lives in
  `MASTER_PROMPT.md`.
* Never "hide" a secret with base64, minification or obfuscation — those are not security
  mechanisms and the repo is public.
* The Beta deployment is a **separate** environment: it gets its own Worker/KV/D1 and its own
  secrets. Production credentials are never exposed to it, and Beta-only data lives in
  Beta-only tables (`beta_feedback`) so experimentation cannot corrupt production rows.
* `ADMIN_EMAIL` / `ADMIN_EMAILS` are configuration, not credentials: knowing the admin email
  address grants nothing, because authorization is resolved from the authenticated account's
  role plus that configuration on the server.

## 6. Reporting a problem

Email **questions@get-aether.de** with the details. Please do not open a public issue for
anything exploitable, and do not test against customers' accounts.
