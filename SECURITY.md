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
* **Mail transports (2026-10-01)**: outgoing mail is provider-agnostic — the **Cloudflare Email Sending binding** (`env.EMAIL`, native, no secret to hold or rotate) is preferred, **Resend** (`RESEND_API_KEY`) is the fallback, and with neither, mail is mocked into the Worker log and every caller is told (`emailProvider` in `/api/health`). A transport that fails throws, so the password-reset chain can fall through to the next channel instead of reporting success for mail that never left. This removes the old single point of failure where one missing third-party key silently disabled reset and verification. The one thing it does not do is pretend: as long as no transport is enabled, health says `email:false`, the reset page says the link went to the recovery channel, and the verified-email gate stays off (it keys off `RESEND_API_KEY`, or `REQUIRE_EMAIL_VERIFICATION=true`) so nobody is ever locked out by mail that cannot be sent.
* **Password-reset delivery without a mail provider**: email -> the operator's Discord webhook -> one server log line. The Discord hop is an allowlist (`RESET_DISCORD_EMAILS`, default: the admin accounts), because a shared channel is not a mailbox: one customer's single-use link must never appear where the operator (or anyone with the channel) could take over an account. The log hop is operator-only (Cloudflare dashboard) and single-use links stay hashed at rest in `auth_tokens`, so a database leak still yields nothing usable. Every account takes the same code path with the same `200 {ok,delivery,message}` response, and `delivery` describes the deployment rather than the account, so the endpoint cannot be used to discover who has an account.
* **Turnstile (ON since 2026-10-01)**: a managed widget covers registration and the
  password-reset endpoints; tokens are verified server-side against
  `challenges.cloudflare.com`. The public site key lives in `public/aether-config.js`, the
  secret only in Worker Secrets (plus its GitHub Actions copy). It **fails open on a captcha
  outage but never on a failed check**, so an outage cannot lock users out of their own
  accounts. `TURNSTILE_LOGIN` stays unset — sign-in is not captchaed.
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

### 3b. Added 2026-10-01 (evening): page headers, email authentication, download integrity

**Every page and asset now ships a Content-Security-Policy**, applied by a zone rule in the
`http_response_headers_transform` phase (`Aether response security headers`, ruleset phase
entrypoint `903fcfeaee7f47ab8aeb8d0b5f3da955`). It deliberately **excludes `/api/*`**, where the
Worker sets its own stricter JSON-only policy. The six headers are:

| Header | Value | Why it matters here |
| --- | --- | --- |
| `content-security-policy` | `default-src 'self'`, `object-src 'none'`, `base-uri 'self'`, `frame-ancestors 'none'`, `form-action 'self'`, plus allowlisted font/Turnstile origins | blocks injected external JavaScript, exfiltration to an attacker host, `<object>`/`<embed>` payloads, `<base>` hijacking, form hijacking and clickjacking |
| `x-frame-options` | `DENY` | the same for browsers that ignore `frame-ancestors` |
| `x-content-type-options` | `nosniff` | a served file can never be reinterpreted as script |
| `referrer-policy` | `strict-origin-when-cross-origin` | cross-origin requests leak the origin, never the path or query (the three secret-bearing pages set `no-referrer` in their own `<head>`, which wins) |
| `permissions-policy` | camera/mic/geo/USB/payment… all denied | a page has to ask for a device deliberately |
| `cross-origin-opener-policy` | `same-origin` | a page opened from elsewhere cannot reach back through `window.opener` (XS-Leak class) |

The honest limit: the policy allows `'unsafe-inline'` for scripts and styles, because this site is
built from inline `<script>` blocks. It can therefore be tightened further one day (nonces or
hashes), but even today it removes the standard outcomes of a script-injection bug.

* **Source of truth:** [`_security-headers.mjs`](_security-headers.mjs). The Cloudflare rule and
  `_dev-server.mjs` both use these values, so the policy can be exercised locally before it is live.
* **CI guard:** [`_check-headers.mjs`](_check-headers.mjs) fails the build if a page loads a resource
  from an origin the policy does not permit, uses `eval()`/`new Function()` (blocked without
  `'unsafe-eval'`), loads a `data:`/`blob:` script, or if anyone weakens the policy itself. Mutation
  tested: five injected regressions, five caught.
* **Operational notes:** a **cached** response is served without the headers until it expires, so
  purge after changing the rule; and adding a CDN/font/analytics origin now takes two edits
  (`_security-headers.mjs` **and** the Cloudflare rule) — the CI guard fails loudly if you forget.

**Email authentication on `get-aether.de`** (this is what stops someone spoofing
`questions@get-aether.de` to your own customers):

| Record | Value | State |
| --- | --- | --- |
| SPF | `v=spf1 include:_spf.mx.cloudflare.net ~all` | existed |
| DKIM | `cf2024-1._domainkey` (Cloudflare Email Routing) | existed |
| DMARC | `_dmarc` → `v=DMARC1; p=none; rua=mailto:questions@get-aether.de; fo=1` | **added 2026-10-01** |

`p=none` is monitoring only — it cannot break mail, and it starts the report flow so the account can
see who is sending as this domain. **Order of operations before tightening:** add the sending
provider's SPF include **and** its DKIM record first, then move to `p=quarantine`, then `p=reject`.
Without that, the project's own verification and reset mail would fail DMARC alignment once the
policy is enforced.

**DNSSEC is enabled on the Cloudflare side and is `pending`** until the DS record is published at
the registrar. Nothing breaks while it is pending (validating resolvers simply cannot prove the
chain), and nothing is gained either — the registrar step is what completes it:

```
get-aether.de. 3600 IN DS 2371 13 2 031AAD25DC73AEB6B8889DD733B7F4807785B40C9DBD04C188408438C5CFB6ED
```

Add it exactly as shown (key tag `2371`, algorithm `13`, digest type `2`), then verify with
`dig +dnssec get-aether.de` or <https://dnssec-analyzer.verisignlabs.com/get-aether.de>. A **wrong**
DS record makes the domain unresolvable for every validating resolver — if the site disappears after
adding it, delete the DS record first and investigate second.

**The tester download** (see §2 for the endpoints) is now: a single-use ticket that travels as a
path-narrowed `HttpOnly` cookie and never in a URL, bound to the session that minted it, over an
`https`-only build source, with the artifact hashed against the published checksum **before** a byte
is sent (`409 CHECKSUM_MISMATCH`, nothing delivered, audited), plus `x-content-verified`,
`x-checksum-sha256`, `content-digest`, `no-store` and `noindex`. `PROGRAM_DISABLED=true` is the kill
switch that stops every download instantly without touching `PROGRAM_URL`, including one already
authorised a second earlier.

**Maintenance modes** now come in two flavours — `Maintenance (SOFT)` keeps the account area, chats,
password recovery and payment results open while the shop is closed; `Maintenance (HARD)` closes
everything public. The notice page itself finishes the job without help: it keeps its "down for"
clock across refreshes (browser storage, not a per-load timer), runs a real countdown to the next
clock-aligned check instead of restarting at a full interval, probes the origin every 30 s and
navigates the visitor back the moment the site answers, reloads itself every 5 min so an edited
notice is seen, and only offers the "your account is still open" link after asking the server — so
it cannot promise a link that the HARD rule would redirect. `maintenance/README.md` documents both
modes, including the trap that caught this project once already: `/public/*` must stay open, or the
admin and beta pages load **with no scripts at all** while *looking* fine.

### 3c. Added 2026-10-01 (late): the zone stopped rewriting what it serves

Three zone settings were silently working against the repo, and all three are now fixed:

| Setting | Was | Now | Why it changed |
| --- | --- | --- | --- |
| Scrape Shield → **Email Obfuscation** | on | **off** | it replaced `questions@get-aether.de` with a `/cdn-cgi/l/email-protection` link that renders only if Cloudflare's injected `email-decode.min.js` runs, and injected that script into every page. On a page whose whole purpose is "email us if it is urgent", a support address that disappears without JavaScript is a defect — and it meant the artifact a visitor received no longer matched the file in the repo, so no integrity check of a served page could be trusted. Verified after the change: `maintenance.html` is served **byte-for-byte identical** to the committed blob |
| **Cache level** | aggressive | **basic** (Standard) | aggressive cached HTML and not just assets, so the edge could outrank the origin |
| **Browser Cache TTL** | 14400 (4 h) | **0** = respect origin (GitHub Pages sends `max-age=600`) | a 4-hour browser TTL is why an edit "did not show" for the rest of the day; changes now land within ~10 minutes |
| **Development Mode** | on (by hand, as a workaround) | **off** | it bypassed the edge cache to work around the two settings above, and expires by itself |

Redirect rules are evaluated **before** the cache, so switching maintenance modes never depended on
these; what they fix is seeing your own edits.

### 3d. Added 2026-10-02: markup the browser was quietly recovering from, and Tester refusals that say which door is shut

**Every page shipped a malformed end tag.** Fourteen pages wrote `</a</nav>` and two wrote the worse
`</a<a href=…>`. HTML has no syntax error to report here: the parser reads `</a</nav>` as one end tag
named `a</nav`, finds nothing to close, **drops it**, and leaves the link and the `<nav>` open. Nothing
looked broken in a review or a desktop screenshot, but the consequences were real:

- the `<nav>` never closed, so the header CTA and the **hamburger button ended up inside it** — and
  since `.nav { display:none }` below 700 px, the mobile menu button had **no box at all**: the site was
  unnavigable on a phone;
- on `account.html` the malformed tag consumed the whole `#nav-beta` element, so the Beta link rendered as
  the bare text `AccountBeta`, `document.getElementById("nav-beta")` returned `null`, and the swallowed
  `TypeError` left the Tester's Beta card saying *"Loading the current Beta address…"* forever — while
  the link it offered pointed at the relative `beta.html` instead of the configured Beta host;
- `admin.html` lost its Admin nav link the same way.

All sixteen are fixed, and `_check-markup.mjs` now runs in all three workflows to keep them fixed: no
end tag followed by another `<`, every element with a required end tag balanced (after the inline
scripts are emptied, so template strings are not mistaken for markup), and no duplicated `id`. It
caught all sixteen on the pre-fix tree.

Two supporting changes went with it: `show(el, on)` on `account.html`, `admin.html` and `beta.html`
now tolerates a missing element instead of throwing (one renamed id could previously abort the rest of
a page's script), and `account.html` writes the Beta address **before** revealing the card.

**The Tester gate now names its refusal.** `requireTester` answered one sentence — *"Beta access
requires a Tester account with a verified email"* — for three different situations with three
different fixes, so a signed-out visitor, a Tester whose address was unconfirmed and an ordinary
customer were all told the same thing, and two of them were told something untrue about their own
account. `requireTesterOrExplain` now answers with the reason and a code the pages branch on:
`SESSION_REQUIRED`, `NOT_TESTER` or `EMAIL_UNVERIFIED` (statuses unchanged at 403, so nothing that
relied on the old shape moves). The **role is checked before the address** on purpose: the sentence
*"Tester access is already on this account"* may only be said to an account that has the role.

A role grant is still a real grant — `POST /api/admin/users/:id/role` now answers with a `warnings`
array (always present, empty when there is nothing to say) instead of refusing. The one warning it can
carry states that an unconfirmed address still blocks the Beta and the build, which is exactly the
state an admin creates when they grant Tester by hand to a fresh signup.

`public/checkout.js` passes the server's `code` through on an error (`err.code`, `?v=9`), so a page can
distinguish those cases without pattern-matching English.

## 4. Still to do manually in the Cloudflare dashboard

1. **Turnstile — done 2026-10-01.** The widget "Aether signup + password reset (get-aether.de)"
   (managed mode, domains `get-aether.de` / `www` / `api`) is live: the public site key is in
   `public/aether-config.js`, the secret exists as a Worker secret and as the `TURNSTILE_SECRET`
   GitHub secret. To rotate, generate a new secret in the widget and update the Worker secret (or
   the GitHub secret and let the CI sync push it). The **site key changes only if the widget is
   recreated**, and a new site key must be live on the site *before* the matching secret is set —
   `turnstileGuard` enforces the moment `TURNSTILE_SECRET` exists, so the wrong order breaks
   registration for everyone.
2. **Real email — pick one of two paths (no code change needed either way).**
   (a) **Cloudflare Email Sending (recommended, no third-party account and no key at all):**
   dashboard -> Compute -> Email Service -> Email Sending -> Onboard Domain -> `get-aether.de`
   (adds SPF/DKIM/DMARC on `cf-bounce`; **never delete the existing MX records**), then attach the
   `EMAIL` binding to `aether-api` and `aether-payments` (`{"type":"send_email","name":"EMAIL"}`
   in the script metadata together with `keep_bindings`). `/api/health` then reports
   `email:true, emailProvider:"cloudflare"`. Sending to the account's **verified destination
   addresses** is free on every plan; arbitrary recipients need the Workers Paid plan. While the
   service is disabled the binding call returns `10203 email.sending_disabled`, which the delivery
   chain catches and routes to Discord/log rather than losing the mail.
   (b) **Resend:** create the API key and set `RESEND_API_KEY` as a Worker secret (and as the
   GitHub secret, which the worker workflow syncs for you). Then verify `get-aether.de` in Resend
   (TXT/DKIM) — again, never touch the MX records.
   Until one of those is done: `email:false`, mail is mocked to the Worker log, the reset link
   travels the recovery channel, and no verification email can be sent (the gate stands down
   honestly rather than locking people out).
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

* Production secrets (Resend, NOWPayments, Discord webhook, Turnstile) exist only as Worker
  Secrets, with copies as **GitHub Actions secrets** where CI needs them (`CLOUDFLARE_API_TOKEN`)
  or syncs them (the service keys). Re-uploads keep them (`keep_bindings` is used by the deploy
  step); the sync step skips blank GitHub secrets, so a missing copy can never blank a live value.
  GitHub never hands a secret value back — it is write-only.
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
