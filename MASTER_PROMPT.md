# AETHER — Master Prompt (living checklist — update after each task)

> Copy-paste this file to continue work or give to another agent. Tick [ ] → [x] as you finish.
> Live implementation checklist for the current workstream: **PROJECT_TODO.md**. Security picture + manual dashboard steps: **SECURITY.md**. Beta deployment: **BETA.md**.

## PROJECT
Aether — get-aether.de (GitHub Pages, static) + Cloudflare Worker `aether-api` routed at `get-aether.de/api/*` (same-origin API; `https://api.get-aether.de` still answers for legacy/compat).
Sell: Discord bots + Websites. Source-code only. No hosting/DB/domain/backend **for customers**.
Stack: HTML/CSS/JS + public/checkout.js + public/aether-config.js + Cloudflare Worker (worker/src/index.js) + **D1 `aether-db`** + NOWPayments **Payment API** + Resend + Discord webhook
Customer portal: **account.html** (accounts mandatory for purchases, chat and Beta). The dedicated `Customer.get-aether.de` hostname is **not configured yet** — today the portal is served from `get-aether.de/account.html` with the same-origin API, which is why there is no separate origin to secure. DNS + route for that hostname is a MANUAL item.
Beta: separate git branch `beta` + separate deployment target, hostname `betatester.get-aether.de`, path **exactly** `/yesthisistheofficaldomainanditssolongsopeopledontaccidentlyfindthis` (obscurity only — never shorten it, and never treat it as security).
Admin: **admin.html** (orders, accounts, direct messages, **conversations**, **Beta**) — admin is derived from the `ADMIN_EMAILS`/`ADMIN_EMAIL` variable only, never from the database.
Local dev: `node _dev-server.mjs` → http://127.0.0.1:5501 serves static **and** `/api/*` through the real Worker with an in-memory D1 (+ `ALLOW_DEV_ORIGIN=true`, prints a random local admin password). NEVER file://
Pages: index.html, shop.html, discord-bot.html, website.html, contact.html, about.html, **account.html** (customer portal), **admin.html** (control room), **beta.html** (tester area), payment-success.html, payment-cancel.html, forgot-password.html, reset-password.html, verify-email.html, donate.html, error.html, 404.html
Files: public/checkout.js, public/aether-config.js, public/portal.css, public/pages.css, public/ui.css, worker/src/index.js, worker/wrangler.toml

## CRITICAL CONSTRAINTS (never break)
- Emails: `questions@get-aether.de` = orders/questions, `business@get-aether.de` = business only. NEVER hello@/orders@, never create new addresses, NEVER touch MX / Email Routing (route*.mx.cloudflare.net stays).
- `CONTACT_FROM` must stay `Aether <questions@get-aether.de>` (verified sender).
- Secrets ONLY via Worker Secrets — NEVER in HTML/JS/wrangler.toml/GitHub: `RESEND_API_KEY`, `NOWPAYMENTS_API_KEY`, `NOWPAYMENTS_IPN_SECRET`, `NOWPAYMENTS_IPN_SECRET_2`, `DISCORD_WEBHOOK_URL`, `TURNSTILE_SECRET`. `ADMIN_EMAIL` is **config, not a credential** (a plain text variable).
- NOWPayments = **Payment API ONLY** (`POST https://api.nowpayments.io/v1/payment`), NOT Invoice API. Body: `{price_amount, price_currency:"eur", pay_currency:"btc|ltc|eth", order_id:"aether_<ts>_<rand>", order_description, ipn_callback_url:"https://api.get-aether.de/api/ipn"}`.
- Fixed IPN for ALL: `https://api.get-aether.de/api/ipn` — never per-coin/package. XMR removed; only BTC/LTC/ETH.
- D1 is used ONLY for accounts, orders, per-order chat, conversations, sessions, tokens, rate limits, settings, audit log and Beta feedback. Nothing is exposed without auth (admin routes need `ADMIN_EMAILS`/`ADMIN_EMAIL`).
- Frontend `apiBase = ""` → **same origin**: `/api/*` on get-aether.de is routed to the Worker (worker route `get-aether.de/api/*` + `www.get-aether.de/api/*`), so the session cookie is first-party (`__Host-aether_session`, HttpOnly, Secure, SameSite=Lax). `https://api.get-aether.de` still works (legacy/compat, SameSite=None + Bearer).
- CORS is an **explicit allowlist** (`ALLOWED_ORIGIN`); `*` is deliberately ignored by the Worker. Unknown origins get no CORS headers and their writes get 403.
- **Roles are server-side only.** `user` | `tester` live in `users.role`; `admin` is derived at request time from `ADMIN_EMAILS`/`ADMIN_EMAIL`. The API can never grant `admin`, never demote an admin, and never let someone change their own role. The UI buttons are convenience only.
- **Anonymous chat does not exist.** Every `/api/conversations*` route needs a session and every row is filtered by `user_id`; another account's conversation id returns **404** (not 403) so ids cannot be probed.
- **Never claim "100% secure".** Obscure URLs, hidden pages, frontend role checks, hidden buttons, minified/obfuscated JS, Base64 and client-side variables are **not** security mechanisms. Say what is enforced in code, what is enforced at the Cloudflare edge, and what is still manual.
- Security architecture + manual dashboard steps live in **SECURITY.md**. Read it before changing auth, cookies, CORS or headers.
- **Signed-in customers never type an email.** `POST /api/invoice` and `POST /api/order` both resolve the account first: if the body has no usable email (or Discord) and the request carries a valid token, the account's own email/Discord are used. A typed email always wins. Signed out, an email is still required.
- **worker/src/index.js must stay 100% ASCII** (escape every €, —, →, · as `\uXXXX`). A non-ASCII byte is what turned every character in the Discord embed into `?`. `_build_chunks.mjs` aborts if it finds one.
- **PBKDF2 in workerd rejects iterations > 100000**. `PBKDF2_ITERATIONS = 100000` is the ceiling; anything higher made register/login return a bare `1101`.
- **The logo is the black-and-white mark, and it is generated.** Every page and the favicon use `public/aether-logo.png` (128 px monochrome, 6.7 KB) produced by `node _make-logo.mjs` from `Aether Logo trasnparent new.png` — the original 1.2 MB brand asset, which is also still the Discord embed thumbnail because Discord cannot render SVG. The blue gradient survives only as `public/aether-logo-gradient.svg` (light/coloured backgrounds) — do not put it back in the header, and never ship the 1.2 MB PNG to a page.
- **Never write `[^\\s@]` in a JS regex.** `public/checkout.js` once shipped with a doubled backslash in `isEmail`, so every address containing the letter **s** was rejected. `node _check-email-regex.mjs` runs every `isEmail` in the project against addresses that must pass/fail — run it after touching validation.

## PRICING (locked)
- Discord: Basic **€15** (≤10 commands, 2 revisions) · Premium **€30** (≤25 commands, 3 revisions) · Custom **€50+**
- Website: Starter **€15** (1 page, 1 revision) · Basic **€30** (≤3 pages, 2 revisions) · Custom **€50+**
- Custom Discord builder: first **10 commands included**, **+€2 per extra command**; **+€3 per additional feature**; **max 4 revisions** = **2 free, +€7 per extra** (hard cap 4)
- Custom Website builder: first **3 pages included**, **+€5 per extra page**; **+€3 per additional feature**; **max 4 revisions** = **2 free, +€7 per extra**; complexity preset **0 / 5 / 10 / 15**
- Custom price is dynamic via Payment API (€1–5000) — the builder calculates it. Fixed IIDs below are reference/fallback only.

### Add-ons & PROJECT SIZE (added 2026-09-30 — a superset of the rules above, nothing removed)
Features are no longer a flat €3 each: every add-on carries its own price in `data-price`, €3 stays the floor, and heavier work costs what it costs. Free "incl." options stay free. Both builders also gained a **Project Size** selector, and both are saved in the URL (`size=`).
- Website add-ons: Animations/Image Gallery/SEO +3 · Video Background / Dark-Light Theme / Custom Illustrations +5 · Interactive Elements / Multi-Language (i18n) / Blog-News / Booking Flow +10 · Shop UI, Member Area UI, Admin Panel UI (all static front-end) +15 · 3D-WebGL / Data Visualisation +20
- Bot add-ons: Logging / Help Command / Welcome / Embeds / Buttons +3 · Reaction Roles / Giveaway / Scheduled Tasks / Ticket Transcripts +5 · Ticket System / Database Integration (customer supplies the DB) / External API / Economy / Auto-Moderation +10 · AI-LLM +15 · Multi-Server-Sharding +15 · Music-Audio +20 · Web Dashboard +25
- Website **Project Size**: Standard (≤5 pages, incl.) / Large 6–15 pages **+€40** / Platform 16+ pages **+€100**
- Bot **Project Size**: Standard (≤20 commands, incl.) / Large 21–40 commands **+€40** / Platform 40+ commands, dashboard, sharding **+€100**
- Anything past that is an individual quote: both request sections say so explicitly, and the inquiry path is free — **the customer pays only after approving the quote**. Nothing here promises hosting, backends, databases or panels; UI-only options are labelled "(static)" and the DB add-on is "you provide the DB".

## CUSTOM URL / SHARE LINKS
Both builders persist state in the URL (`website.html?pages=…&type=…&style=…&complexity=…&size=…&feats=…&revs=…&promo=…`, `discord-bot.html?cmds=…&lang=…&size=…&feats=…&revs=…&promo=…`) and re-load on refresh; "Copy share link" writes it to the clipboard. A promo in the URL auto-applies on load.

## REFERENCE PAYMENT LINKS (fixed-price fallback — Payment API is dynamic)
- Website Starter €15: LTC 5283564223, BTC 4636081292, ETH 5015006089
- Website Premium €30: LTC 4421223219, ETH 5277333867, BTC 4908575462
- Discord Starter €15: LTC 4746627664, ETH 6214760305, BTC 5024772056
- Discord Premium €30: LTC 5353160401, ETH 5762161630, BTC 6096587586

## CUSTOMER PORTAL (account.html)
Accounts are mandatory for the whole product — **there is no guest checkout at all**: buying, chat and Beta all require a session, and `POST /api/invoice` answers `401 {"code":"ACCOUNT_REQUIRED"}` to anyone signed out. An order always belongs to the session account, and the account's own email is the address invoices and order updates go to.
- **Identity:** PBKDF2-SHA256 100 000 iters, per-user salt, no plaintext anywhere. Sessions are `__Host-aether_session` (HttpOnly, Secure, SameSite=Lax, Path=/) stored as **SHA-256(token)** in `sessions`, rotated on every login, 14-day TTL, capped at 10 per user. `Authorization: Bearer <token>` also works (third-party-cookie safety).
- **Email verification:** registering sends a 24 h single-use token (stored hashed in `auth_tokens`); `verify-email.html` consumes it. `POST /api/auth/resend-verification` is session-bound and limited to 4/h per account. While verification is enforced, unverified accounts get `403 {"error":"Please verify your email address before continuing.","code":"EMAIL_UNVERIFIED"}` on purchases, conversations and Beta. Enforcement **stands down honestly** when no mail provider is configured (`email:false` in health) — that is the current production state until `RESEND_API_KEY` exists; `REQUIRE_EMAIL_VERIFICATION=true` forces the gate on anyway.
- **Purchase IDs:** `AETH-2026-XXXXXXXX`, generated server-side from `crypto.getRandomValues` with an ambiguity-free alphabet `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`, unique (checked + unique index), never accepted from the browser, backfilled lazily for old orders, shown in the portal/order detail/admin/emails. A Purchase ID is an **identifier, not a credential**: `GET /api/purchases/:id` only ever looks inside the authenticated account (`404` for anything else).
- **Chat:** conversations are `conv_<18 hex>`, owned by `user_id`, statuses `open|answered|closed`, messages capped at 4000 chars (control chars stripped), rate limited. A conversation can reference one of *your own* Purchase IDs (validated). Legacy per-order chat rows (no `conversation_id`) are bridged into the same history with a deterministic id, so nothing is lost. Admin replies land in the same thread and email the customer.
- **Portal UI:** dashboard cards in the order purchases → chat → Beta → security; Purchase ID column; verification banner + resend; thread view with 12 s polling and a close button; `#nav-beta` only renders when the server says the account is a tester.
- **Beta area (`beta.html`):** server-gated — the page asks `/api/beta/status` and shows a denied state for anyone else. Shows the environment, server-provided feature flags, a single-use ticket tool and feedback (stored in `beta_feedback`, never in production tables).

## WORKER API (all live)
Public / auth
- `GET  /api/health` → `{ok,service,time,email,payments,ipnSignature,discord,turnstile,db}` (booleans only — no addresses/config)
- `POST /api/auth/register` · `POST /api/auth/login` · `POST /api/auth/logout` · `GET /api/me` · `DELETE /api/me` (self-service account deletion: sessions/tokens/conversations/feedback cleared, orders detached not deleted)
- `POST /api/auth/forgot` → single-use reset link (30 min); identical answer for unknown emails; honest 503 while `RESEND_API_KEY` is missing
- `POST /api/auth/reset` → `{token,password}` → replaces the password, kills every other session, signs back in
- `POST /api/auth/password` → signed-in change (needs `currentPassword`)
- `POST /api/auth/verify-email` → consumes the 24 h token · `POST /api/auth/resend-verification` → new token (session-bound, 4/h)
Orders / payment
- `POST /api/order` → the free inquiry/quote form (never writes an order): Discord optional, **email optional when signed in** (the account email is used), questions@ vs business@ routing, auto-reply
- `POST /api/invoice` → **requires a session** (`401 {"code":"ACCOUNT_REQUIRED"}` while signed out — there is no guest checkout), verified-email gate, `pay_currency` + promo validation, creates the NOWPayments Payment, stores the D1 order **owned by the session account** (`user_id` always set, account email wins over any posted one), generates the **Purchase ID**, returns `{orderId,purchaseId,paymentId,promoApplied,discount,…}`. Rows written by the old guest checkout are still claimed when that email registers.
- `POST /api/ipn` → verifies `x-nowpayments-sig` HMAC-SHA512 against **both** IPN secrets; waiting/confirming/confirmed/finished/sending/failed/expired/partially_paid; updates `orders.status`; email+Discord carry the Purchase ID
- `GET  /api/payment/:paymentId` (or `?payment_id=`, `/api/status`) → verified via `api.nowpayments.io/v1/payment/:id`
- `GET|POST /api/promo?code=&amount=` → `{valid,code,type,value,discount,finalAmount}`; unknown codes never 500
Customer (session + ownership)
- `GET  /api/orders` · `GET /api/orders/:id` · `POST /api/orders/:id/message`
- `GET  /api/purchases/:purchaseId` → `{purchase, conversation}` (400 malformed, 404 not yours)
- `GET|POST /api/conversations` (list / create with optional Purchase ID) · `GET /api/conversations/:id` · `POST /api/conversations/:id/messages` · `POST /api/conversations/:id/status`
Beta (session + verified + tester)
- `GET|POST /api/beta/access` (status) · `POST /api/beta/feedback` · `POST /api/beta/ticket` (120 s, single-use) · `POST /api/beta/redeem`
Admin (`ADMIN_EMAILS`/`ADMIN_EMAIL`, 403 otherwise; all state-changing actions are audit-logged)
- `GET /api/admin` · `GET /api/admin/orders[?status=&limit=]` · `GET /api/admin/orders/:id` · `POST /api/admin/orders/:id/message`
- `GET /api/admin/users` (role + verified + `assignableRoles` + `adminEmailsConfigured`) · `POST /api/admin/users/:id/role` (grant/revoke `tester`) · `DELETE /api/admin/users/:id`
- `GET /api/admin/conversations` · `GET /api/admin/conversations/:id` · `POST /api/admin/conversations/:id/messages` · `POST /api/admin/conversations/:id/status`
- `GET /api/admin/beta` · `POST /api/admin/beta/domain/request` (password re-auth → 64-hex 30-min single-use token) · `POST /api/admin/beta/domain/confirm` · `POST /api/admin/beta/flags`
- `GET /api/admin/audit` · `GET /api/admin/messages`
Rules that hold everywhere: 64 KB body cap, per-field length caps, sanitised errors, security headers on every response, Origin/Referer check on every write (CSRF), app-layer rate limits per route, and `no-store` on API responses.

## WORKER VARIABLES
- `CONTACT_TO`, `CONTACT_FROM`, `SUCCESS_URL`, `CANCEL_URL`, `SITE_URL` (default `https://get-aether.de`, used in reset/verification links) — unchanged
- `ALLOWED_ORIGIN` — comma-separated origin allowlist. Dashboard currently holds `*`, which the code **ignores** in favour of the built-in `https://get-aether.de,https://www.get-aether.de,https://api.get-aether.de`. Set it explicitly so config == reality.
- `ADMIN_EMAIL=alex.real.apple@gmail.com` — single admin address (preferred). `ADMIN_EMAILS` can hold a comma-separated list; the code reads **both**, unioned. The dashboard still holds the guessed `ADMIN_EMAILS=Wispz@outlook.de` → **MANUAL: replace it** (applies instantly, no redeploy). Empty = nobody is admin.
- `REQUIRE_EMAIL_VERIFICATION` — `true` forces the verified-email gate even without a mail provider; unset = automatic (gate on as soon as `RESEND_API_KEY` exists).
- `BETA_HOST` (default `betatester.get-aether.de`), `BETA_PATH` (default `/yesthisistheofficaldomainanditssolongsopeopledontaccidentlyfindthis`), `BETA_FLAGS` (JSON, default `{"betaNewChat":true,"betaDashboard":true,"betaTools":true}`) — the admin panel can override all three in D1 `settings`; the frontend never hardcodes them.
- `AETHER_ENV` — `production` is reported in `/api/beta/status`.
- `TURNSTILE_SECRET` (secret, optional) + `TURNSTILE_LOGIN`; until the secret exists no captcha is required and `public/aether-config.js → turnstileSiteKey` must stay empty. Set both or neither.
- `ALLOW_DEV_ORIGIN` — local development only, must stay unset/false in production.
- `PROMO_CODES` — JSON map, e.g. `{"WELCOME10":{"type":"percent","value":10}}`. **Currently empty → `/api/promo` returns `{valid:false}`.**
- D1 binding `DB` → `aether-db` (`201052a6-bed3-4c1c-a3ee-e16394aa36e4`, WEUR). KV namespace `747e914c4ebf40c5b64d876090f1b467` holds the deploy staging chunks + `aether-meta`.

## D1 SCHEMA (live)
`users(id, email UNIQUE, password_hash, discord, created_at, email_verified, **role** DEFAULT 'user')` ·
`sessions(token PK = **sha256 of the session token**, user_id, created_at, expires_at)` ·
`auth_tokens(token_hash PK, user_id, purpose 'reset'|'verify', expires_at, created_at, **payload**)` ·
`rate_limits(rl_key PK, count, window_start)` ·
`orders(id, user_id, order_id UNIQUE, payment_id, amount, currency, type, package, description, status DEFAULT 'pending', promo_code, discount, meta, extra, created_at, email, **purchase_id**)` ·
`messages(id, order_id, user_id, sender DEFAULT 'customer'/*'admin'*/, body, created_at, **conversation_id**)` ·
`conversations(conversation_id PK, user_id, order_id, purchase_id, subject, status 'open'|'answered'|'closed', assigned_admin, created_at, updated_at)` ·
`settings(key PK, value, updated_at)` (Beta host/path/flags — server-owned) ·
`audit_log(id, actor_id, actor_email, action, target, detail, created_at)` ·
`beta_feedback(id, user_id, kind, body, created_at)`
indexes `idx_sessions_user, idx_orders_user, idx_orders_orderid, idx_orders_email, idx_messages_order, **idx_orders_purchase_id (unique), idx_conversations_user, idx_messages_conversation, idx_beta_feedback_user**`
All of it is applied to the live database. `_test-worker.mjs` and `_dev-server.mjs` mirror this schema exactly — update all three together.

## CLOUDFLARE EDGE (applied via API)
- Worker routes `get-aether.de/api/*` + `www.get-aether.de/api/*` → `aether-api` (production/Alpha). `aether-payments` stays bound for the legacy coin links.
- TLS: Always Use HTTPS, min TLS 1.2, TLS 1.3, HSTS 1 year + includeSubDomains + nosniff. SSL mode is **`Full`** and must stay `Full` while GitHub Pages is the origin: `Full (strict)` was set on 2026-10-01 and caused an instant site-wide **HTTP 526** (GitHub Pages serves `CN=*.github.io` for `get-aether.de`, so strict validation has nothing to accept). Reverted within minutes; see SECURITY.md §4.3 for the only two ways to make strict viable.
- Cloudflare Managed Free Ruleset (WAF) deployed — ruleset `77454fe2d30c4220b5701f6fdfb893ba`, entrypoint `d0c31ddfc1a94d17b965ec5749aeb18a`.
- Edge rate limiting: ruleset `c1f44df272df4b5aa9139338a895ab15` — one rule (free plan allows exactly one): `10 req / 10 s per IP+colo` on `/api/auth/*` + order/contact/invoice, `block` for 10 s. Because it is used up, **chat, Beta and admin are protected by the application limits only** (`/api/conversations` 400/h, conversation writes 120/10 min, resend 4/h, admin routes per-route limits). MANUAL: either swap that one rule for a broader one or upgrade the plan.
- DDoS: Cloudflare's always-on L3/4 + L7 mitigation via the proxied zone (no proxy = no protection — keep both hostnames orange-clouded).
- Bot Fight Mode intentionally **OFF** so NOWPayments IPNs and the checkout polling are never challenged.
- Turnstile hook exists in code and is off until keys are set.

## DEPLOY (wrangler is NOT authenticated — no CLOUDFLARE_API_TOKEN)
1. `node --check worker/src/index.js`
2. `node _test-worker.mjs` (183 checks — run this **before** every deploy; it has caught live-breaking bugs)
3. `node _build_chunks.mjs` → writes `_gz2_00.txt`…`_gz2_21.txt` (gzip level 9 + base64, 2000 chars each) and prints `srcBytes`, `nonAscii` (must be 0), `sha256`, `gzBytes`, `chunkCount` and per-chunk `len/sum/head/tail`. **Aborts if the source is not pure ASCII.**
4. For each *changed* chunk, call the Cloudflare MCP `execute` tool with `cloudflare.request({method:"PUT", path:`/accounts/${accountId}/storage/kv/namespaces/747e914c4ebf40c5b64d876090f1b467/values/aether-gz-${i}`, body:<chunk>, contentType:"text/plain", rawBody:true})`.
   - **The `execute` `code` argument caps around ~3 KB.** One 2000-char chunk per call is the reliable size; packing 4 chunks into one call returns a misleading `10000: Authentication error`. Chunk 21 is shorter (784 chars).
   - Cheap optimisation: one call can `GET` all 22 existing `aether-gz-*` values, compute `len`+`sum` (sum = Σ charCodeAt) inside the sandbox and return just those numbers. Compare with the fresh manifest and re-upload **only** the chunks whose `sum` changed — gzip keeps a byte-identical prefix, so a small edit near the end usually moves just the last few chunks (this deploy needed 5 of 22).
5. One more `execute` call: `GET` `aether-gz-0..21`, `atob` → gunzip (`new Response(new Response(bytes).body.pipeThrough(new DecompressionStream("gzip")))`), **compare `sha256` with the manifest and abort unless it matches**, then multipart-PUT to BOTH `aether-api` and `aether-payments`. Write the new `sha256` into the `aether-meta` KV value.
6. Multipart details that must be right or Cloudflare answers `10021: No such module: index.js`:
   - part `Content-Disposition: form-data; name="index.js"; filename="index.js"` + `Content-Type: application/javascript+module`
   - metadata part: `main_module:"index.js"`, `compatibility_date:"2024-12-01"`, `compatibility_flags:["nodejs_compat"]`, and **`keep_bindings:["plain_text","secret_text","d1","kv_namespace"]`** — that last field is what preserves the Worker secrets and the D1 binding across a code-only deploy (verified live: `db:true`, `payments:true`, `ipnSignature:true`, `discord:true` after every upload).
7. Allow ~1 minute after the PUT before believing a failure — the previous version can still answer briefly. Then verify: `curl https://get-aether.de/api/health` and an anonymous `GET /api/conversations` (must be 401).
8. D1 schema changes are applied through the same `execute` tool: `POST /accounts/${accountId}/d1/database/201052a6-bed3-4c1c-a3ee-e16394aa36e4/query` with `{sql, params}`. Additive `ALTER TABLE … ADD COLUMN` / `CREATE TABLE IF NOT EXISTS` statements are safe to repeat; **update `_test-worker.mjs` and `_dev-server.mjs` in the same commit** so tests keep mirroring production.

## GIT + BETA BRANCH
- Real repo in the working copy (there was none): `main` = production/Alpha, `beta` = Beta.
- Commits on `main`: `1a64287` (customer portal, verified accounts, Purchase IDs, chat history, Tester/Beta), `460976f` (independent Beta deployment plumbing + BETA.md), `7411be6` (publish the real site + repo polish), `70b5a56` (merge the pre-existing GitHub Pages placeholder history), `4f8196b` (robots + sitemap), `3d065b1` (black-and-white logo everywhere).
- `beta` diverges by exactly 2 files (`BETA_BRANCH.md`, `public/aether-config.js → betaDeployment: true`) so the Beta build is identifiable and can never be confused with production. **Never merge a Beta-only flag back into `main` without removing it.**
- Remote: **`https://github.com/Wiiffies/Aether-Website`** (public). `main` is pushed and is the live production source; `beta` is pushed as the Beta branch.
- **Publishing model:** GitHub Pages builds from `main` with its *branch* build (Settings → Pages → Deploy from a branch → `main` / `/`) — that pipeline needs no credentials and cannot be broken by a workflow, so it stays the live publisher. `_config.yml` keeps the published artifact to the site itself (docs, `worker/` and repo config are excluded).
- Workflows: `.github/workflows/deploy-production.yml` = production **checks** on `main` + PRs (inline JS, email regex, worker ASCII/compile, 183-check suite, secret scan, placeholder-copy check) because the branch build publishes in parallel; `.github/workflows/deploy-beta.yml` = `beta` only, refuses any other ref, checks + a publish job that is skipped until `vars.BETA_TARGET`/`secrets.BETA_DEPLOY_TOKEN` are set and **never** falls back to the production Pages site; `.github/workflows/deploy-worker.yml` = checks + `wrangler-action`, inert until `CLOUDFLARE_API_TOKEN` is a repo secret.
- Want checks to *gate* publishing instead of running beside it? Switch Pages to the “GitHub Actions” source (`PUT /repos/{owner}/{repo}/pages {"build_type":"workflow"}`), restore the deploy job documented at the top of `deploy-production.yml`, and give the Beta its own target so it can never publish over production.
- Beta data isolation is by convention + configuration today (separate Worker/KV/D1 and secrets for the Beta hostname). **MANUAL** before real testers: give the Beta Worker its own D1/KV bindings so a Beta bug can never touch production rows.

## LOCAL TESTS (no credentials needed)
`node _test-worker.mjs` — loads the real worker against an in-memory SQLite D1 stub (`node:sqlite`) and runs **183 checks**: health, promo maths, register/login/duplicate/wrong-password, me, orders, invoice + **account-only checkout** (anonymous invoice → 401 `ACCOUNT_REQUIRED`, no order written, a junk token is refused the same way, a typed email can never override the account email), customer chat, admin guards, admin orders/users/messages/reply/delete, **legacy guest rows claimed on register**, account deletion, ownership (one account cannot read another's order → 404), plus the hardening suite (security headers, CORS allowlist + wildcard rejection, cross-site write 403, hashed session storage, per-IP login rate limiting, password policy, forgot/reset/change/verify-email, single-use + expiry rules, 413 body cap, truncated descriptions, no provider payload echo) and the portal suite (Purchase ID format/uniqueness/owner-only/400/401, conversation create/list/legacy bridge/IDOR 404 on read **and** write/closed refuses/4000-char cap/admin reply visible to the customer, role escalation attempts refused incl. `admin` and self-demotion, Tester grant/revoke stored server-side, Beta access/status/ticket/redeem/single-use/feedback/403-after-revoke, the Beta-domain change flow incl. wrong password 401, non-admin confirm refusal, single-use confirm, flags and admin-only audit, and the verified-email gate flipping the whole portal to 403 while mail is configured and standing down when it is not).
`node _dev-server.mjs` — full local stack (static + real Worker + in-memory D1) on http://127.0.0.1:5501, prints a random local admin password at startup. Kill it by finding the PID with `netstat -ano | grep "127.0.0.1:5501" | grep -oE "[0-9]+$" | head -1` then `taskkill //PID <pid> //F` (the `LISTENING` column is localised as `ABHÖREN` on this machine — do not filter on it).
`node _check-inline-js.mjs <pages…>` — parses every inline `<script>` block in the HTML pages.
`node _check-email-regex.mjs` — actually *runs* each `isEmail` it finds against addresses that must pass/fail (this is what caught the `[^\\s@]` bug).

## WHAT IS ALREADY DONE [x]
- [x] Worker deployed LIVE **2026-10-01** — current build sha256 `5e139dde5031dc1d75ad0f5f0baf8cd655632aa1060f7f8948762be61cce48af`, 135 412 bytes, 0 non-ASCII, gz 32 248 B, 22 chunks (only 16–21 changed), uploaded to **both** `aether-api` and `aether-payments` (200/ok) and recorded in the `aether-meta` KV value. The previous build was `1a1c22b7…` (135 027 bytes). Live health: `{"ok":true,"service":"aether-api","time":…,"email":false,"payments":true,"ipnSignature":true,"discord":true,"turnstile":false,"db":true}`; anonymous `GET /api/conversations`, `/api/purchases/…`, `/api/beta/access`, `/api/me` all 401.
- [x] Secrets set and preserved across code-only deploys: NOWPAYMENTS_API_KEY, IPN ×2, DISCORD_WEBHOOK_URL. **RESEND_API_KEY still missing → `email:false`.**
- [x] Migrations applied live: `users.role`, `orders.purchase_id` (+ unique index), `messages.conversation_id`, `auth_tokens.payload`, tables `conversations`, `settings`, `audit_log`, `beta_feedback`, indexes `idx_conversations_user`, `idx_messages_conversation`, `idx_beta_feedback_user`.
- [x] Accounts + verified-email plumbing (register/verify/resend/reset/change), Purchase IDs, conversations with ownership + history, Tester role + Beta API and area, admin tabs for conversations/Beta/audit, Beta-hostname change flow with re-auth + email confirmation + audit.
- [x] 183 local checks green; inline-JS parse + email-validator checks green; secret scan clean (only `.env.example` placeholders).
- [x] **Site published and verified live** at `get-aether.de` (placeholder gone): Pages branch build from `main`, all four workflows green, live 200s for every page, every referenced asset resolving, `www` → 301 apex, docs/worker/config files 404, and a real browser on the live site with zero console errors. Portal, `admin`, `beta` and `robots`/`sitemap` all covered.
- [x] **Account-only checkout:** `/api/invoice` requires a session (`401 ACCOUNT_REQUIRED`), orders belong to the session account, the account email wins over a posted one, and the pay modal + `?next=` round-trip on `account.html` guide signed-out buyers to create an account. Legacy guest rows are still claimed on register.
- [x] Frontend: portal (`account.html`), tester area (`beta.html`), admin (`admin.html`), `public/ui.css` beta/role/flag styles, `?v=6` asset bumps.
- [x] `MASTER_PROMPT.md`, `PROJECT_TODO.md`, `SECURITY.md`, `BETA.md`, `.env.example`, `.gitignore` all updated; `.github/workflows/*` for production/Beta/worker.
- [x] Earlier hardening still in place: security headers, CORS allowlist, CSRF origin checks, hashed sessions, two-layer rate limiting, Turnstile hook, 413 cap, sanitised errors, Discord embed beautification, promo engine, account-aware checkout.
- [x] D1 live data: `users` = 1 (`alex.real.apple@gmail.com`, genuine signup, unverified), `sessions`/`orders`/`messages` = 0, `settings` empty (code defaults apply).

## INFORMATION STILL NEEDED / MANUAL ACTIONS (user must do)
- [ ] **RESEND_API_KEY** → Workers & Pages → aether-api → Settings → Variables → Encrypted. Then Resend → Domains → verify get-aether.de (add TXT/DKIM; **never delete MX route*.mx.cloudflare.net**). Until then `email:false` and the verification gate stands down honestly.
- [x] **`ADMIN_EMAIL=alex.real.apple@gmail.com` set and the guessed `ADMIN_EMAILS=Wispz@outlook.de` emptied (2026-10-01).** Changed by re-sending the binding list as multipart metadata `bindings` together with `keep_bindings:["secret_text"]` (a bare `PATCH /settings` is rejected — it wants multipart), then proving nothing was lost through `/api/health`. Avoid putting an admin address in the public repo in future: register it first, or keep it out of docs.
- [ ] **Set `PROMO_CODES`** — same place. Until then every code returns `{valid:false}`.
- [ ] **Beta deployment target**: `beta` is pushed, but the Beta publish job stays skipped until the `beta` environment has `BETA_TARGET` + `BETA_DEPLOY_TOKEN` and the transfer step is wired.
- [ ] **Beta infrastructure**: DNS record + Worker route for `betatester.get-aether.de`, a Beta Worker with its **own** KV/D1/secrets, publishing the long Beta path.
- [ ] **`Customer.get-aether.de`**: DNS record + route if the portal should live on its own hostname (today it is same-origin on get-aether.de).
- [ ] Turnstile widget + secret (site key into `public/aether-config.js`); set both or neither.
- [x] `ALLOWED_ORIGIN` set explicitly (`https://get-aether.de,https://www.get-aether.de,https://api.get-aether.de`) on 2026-10-01; wildcards stay ignored by code.
- [!] SSL/TLS mode stays **`Full`** — `Full (strict)` is impossible while GitHub Pages is the origin (tried 2026-10-01 → instant HTTP 526, reverted). Fix the origin first: Cloudflare Pages / Worker assets, or let GitHub provision a cert with the proxy temporarily DNS-only.
- [ ] Consider a second edge rate-limit rule (free plan allows only one) so chat/Beta/admin are covered at the edge too. Trade-off: the existing rule deliberately covers `/api/ipn` with the widest limits, and widening it must not throttle NOWPayments callbacks.
- [ ] NOWPayments Dashboard → Settings → IPN → Callback URL = `https://api.get-aether.de/api/ipn`; BTC/LTC/ETH enabled; both IPN secrets matching the Worker.

## HOW TO VERIFY AFTER DONE
```
curl https://get-aether.de/api/health
curl -i https://get-aether.de/api/conversations                 # 401 anonymous — no anonymous chat
curl -i https://get-aether.de/api/beta/access                    # 401 anonymous — obscurity is not access
node _test-worker.mjs                                           # 183 checks, no credentials needed
node _check-email-regex.mjs && node _check-inline-js.mjs *.html
# Browser: http://127.0.0.1:5501/account.html → register → verify → purchase → chat → (tester) Beta
#          http://127.0.0.1:5501/beta.html    → tester-only area
#          http://127.0.0.1:5501/admin.html   → orders / accounts / conversations / Beta config / audit
```

## SECURITY (2026-10-01) — see SECURITY.md for the full picture
**In code (worker + frontend):** CORS allowlist (never `*`, never reflecting unknown origins), Origin/Referer checks on every write (CSRF), security headers on every API response (nosniff, frame-ancestors none, HSTS, no-referrer, permissions-policy, no-store), two-layer rate limiting (in-isolate burst + D1 fixed windows per route), Turnstile verification hook (off until the secret exists), password reset + change + email verification with single-use hashed tokens, hashed session storage + rotation + expiry + per-user cap, server-side roles with no API path to `admin`, ownership filtering on orders/purchases/conversations (404 not 403), Beta gating by session + verified email + role, single-use expiring re-auth tokens for the Beta hostname change, audit logging, honest 503s instead of fake successes, sanitised errors/health, 64 KB body cap and per-field length caps.
**Cloudflare (applied via API):** Always Use HTTPS, min TLS 1.2, HSTS 1 year + includeSubDomains, worker routes for `get-aether.de/api/*` and `www.get-aether.de/api/*`, Cloudflare Managed Free Ruleset (WAF), one edge rate-limit rule (10 req / 10 s per IP+colo on auth + order/contact/invoice, block 10 s), automatic DDoS mitigation via the proxied zone. Bot Fight Mode intentionally OFF so NOWPayments IPNs are never challenged.
**Explicitly NOT security:** the long Beta URL, hidden pages, frontend role checks/hidden buttons, minified or obfuscated JavaScript, Base64, client-side variables. Everyone who knows the Beta URL still needs a session, a verified email and the Tester role.
**Still manual:** Turnstile widget + keys (order matters — see AGENT_HANDOFF_PROMPT.md §7.6), Resend key, an origin that can satisfy `Full (strict)` (blocked by GitHub Pages today), a second edge rate-limit rule for chat/Beta/admin, Beta DNS/routing/isolated data, alerts. `ALLOWED_ORIGIN` and the admin email are done. Listed in SECURITY.md §4.

## UPDATE LOG
- 2026-10-01 — **Cloudflare configuration done through the API (not manual any more).** `ADMIN_EMAIL=alex.real.apple@gmail.com` set and the guessed `ADMIN_EMAILS=Wispz@outlook.de` emptied → the real owner is admin and a stranger can no longer register that guessable address into admin; `ALLOWED_ORIGIN` set explicitly to the three Aether origins; `aether-meta` KV updated to the new build. **SSL mode: `Full (strict)` was tried and reverted** — it broke the site with HTTP 526 because GitHub Pages serves `CN=*.github.io` for `get-aether.de`; the zone is back on `Full` and the site was re-verified 200 seconds later. Lesson worth keeping: change SSL/TLS settings one at a time and curl the homepage immediately after. All four Worker secrets (NOWPayments key + both IPN secrets + Discord webhook) and the D1 binding survived the binding update — proven by `/api/health` (`payments`, `ipnSignature`, `discord`, `db` all true) *and* by a throwaway probe Worker that tested the mechanism (`bindings` + `keep_bindings:["secret_text"]`) before it was used on production, then deleted. Account-only checkout verified live: anonymous `POST /api/invoice` → `401 ACCOUNT_REQUIRED`. Known leftovers: `aether-keys` (unrelated legacy script, not routed) and the missing `RESEND_API_KEY`.
- 2026-10-01 — **Site published + account-only checkout.** The real site replaced the "under construction" placeholder: GitHub Pages publishes `main` through its branch build at `get-aether.de` (the pre-existing placeholder history was merged in, not overwritten), all four workflows are green, and the live site was verified with live smoke tests, an asset crawl and a real browser (zero console errors). Repo front door rewritten (`README.md`), `robots.txt` + `sitemap.xml` added, `_config.yml` trims the published artifact, `.gitattributes` pins line endings, `_make-logo.mjs` regenerates the black-and-white logo every page and the favicon use. **Checkout is now account-only:** `/api/invoice` returns `401 ACCOUNT_REQUIRED` to anyone signed out, the order always belongs to the session account and its account email, the pay modal hides the email field and sends signed-out buyers to `account.html?next=<page>`, and `account.html` returns them to the page they came from. Tests: **183 checks, 0 failed**.
- 2026-10-01 15:00 — **Customer portal release.** Worker rebuilt + deployed live (sha256 `1a1c22b7…`, 135 027 bytes, 22 chunks, both scripts) and the live D1 migrated (role, purchase_id + unique index, conversations, messages.conversation_id, auth_tokens.payload, settings, audit_log, beta_feedback). New: server-side roles (`user`/`tester`, `admin` derived from config only) with admin grant/revoke; verified-email gate + `resend-verification`; **Purchase IDs** `AETH-2026-XXXXXXXX` (CSPRNG, unique, owner-only lookup, email/Discord/admin/portal); conversations with full history, ownership (IDOR → 404), status + admin support inbox and replies that email the customer; legacy per-order chat bridged in; Tester/Beta API + `beta.html` tester area + feature flags; admin Beta tab with a password-re-auth + email-confirmation flow for changing the Beta hostname (64-hex 30-min single-use token, audit-logged); audit log. Frontend: `portal.*`/`admin.*` API surface, verify card + resend, chat UI with polling, Purchase ID column, beta card, role chip, new `public/ui.css` styles. Added a real git repo (main + beta branch) with branch-scoped workflows, BETA.md, and refreshed MASTER_PROMPT/SECURITY/PROJECT_TODO/.env.example. Tests: **183 checks, 0 failed**; secret scan clean; live health + anonymous 401s verified after the deploy. Deploy trick worth remembering: compare KV chunk checksums first — only 5 of 22 chunks changed.
- 2026-10-01 — **Production hardening.** Worker rebuilt + deployed (sha256 `5c9323b4…`, 82 406 bytes, both scripts), D1 migrated (`auth_tokens`, `rate_limits`, `users.email_verified`, legacy plaintext sessions purged), 106 local checks green, new pages (`forgot-password`, `reset-password`, `verify-email`, `donate`, `error`, `404`) + `public/ui.css`, 160+ legacy deploy artifacts and the 5.3 MB scraped folder deleted, `SECURITY.md` + `.gitignore` + `.env.example` added.
- 2026-09-30 15:55 — **Account-aware checkout + bigger-project options.** Worker: `/api/invoice` and `/api/order` take the email/Discord from the signed-in account when the body has none. checkout.js: account cache + account chip + `resolveEmail`/`bindAccountFields`/`accountEmail`/`loadAccount`. Both builders: per-add-on `data-price`, heavy add-ons, Project Size (+€40/+€100), `size=` in the URL, quote copy. **Fixed a live bug: `isEmail` rejected any email containing the letter `s`.** New tools `_check-inline-js.mjs` + `_check-email-regex.mjs`. `?v=4` → `?v=5`.
- 2026-09-24 17:04 — checkout.js 16655 bytes, ?v=2 live, health verified
- 2026-09-27 12:19 — LIVE VERIFIED: invoice BTC €15, payment status pending, CORS *, polling, success-page verification, ?v=2 → ?v=3
- 2026-09-30 13:00 — Worker rewritten ASCII-only; beautified Discord embeds with retries; promo engine; D1 accounts + orders + per-order chat; IPN status colours; checkout.js promo + Bearer auth
- 2026-09-30 13:40 — Admin routes (`/api/admin/*`) + `DELETE /api/me`; guest orders claimed by email; `orders.email` column added; global error handler so failures return JSON instead of a bare 1101; `?v=3` → `?v=4`; account.html + admin.html + portal.css; MASTER_PROMPT rewritten
- 2026-09-30 13:50 — **Fixed register/login outage**: workerd rejects PBKDF2 > 100000 iterations → `PBKDF2_ITERATIONS = 100000`. Deployed, verified live, UTF-8 proven byte-exact, D1 test rows cleaned.
- 2026-09-30 — Added `_test-worker.mjs` + `_build_chunks.mjs` (ASCII guard + chunk generator) so future deploys are test-first.
