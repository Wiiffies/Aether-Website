# AETHER — Master Prompt (living checklist — update after each task)

> Copy-paste this file to continue work or give to another agent. Tick [ ] → [x] as you finish.

## PROJECT
Aether — get-aether.de (GitHub Pages, static) + api.get-aether.de (Cloudflare Worker)
Sell: Discord bots + Websites. Source-code only. No hosting/DB/domain/backend **for customers**.
Stack: HTML/CSS/JS + public/checkout.js + public/aether-config.js + Cloudflare Worker (worker/src/index.js) + **D1 `aether-db`** + NOWPayments **Payment API** + Resend + Discord webhook
Local dev: `python -m http.server 5501 --bind 127.0.0.1` → http://127.0.0.1:5501 — NEVER file://
Pages: index.html, shop.html, discord-bot.html, website.html, contact.html, about.html, **account.html** (customer portal), **admin.html** (control room), payment-success.html, payment-cancel.html
Files: public/checkout.js, public/aether-config.js, public/portal.css, worker/src/index.js, worker/wrangler.toml

## CRITICAL CONSTRAINTS (never break)
- Emails: `questions@get-aether.de` = orders/questions, `business@get-aether.de` = business only. NEVER hello@/orders@, never create new addresses, NEVER touch MX / Email Routing (route*.mx.cloudflare.net stays).
- `CONTACT_FROM` must stay `Aether <questions@get-aether.de>` (verified sender).
- Secrets ONLY via Worker Secrets — NEVER in HTML/JS/wrangler.toml/GitHub: `RESEND_API_KEY`, `NOWPAYMENTS_API_KEY`, `NOWPAYMENTS_IPN_SECRET`, `NOWPAYMENTS_IPN_SECRET_2`, `DISCORD_WEBHOOK_URL`
- NOWPayments = **Payment API ONLY** (`POST https://api.nowpayments.io/v1/payment`), NOT Invoice API. Body: `{price_amount, price_currency:"eur", pay_currency:"btc|ltc|eth", order_id:"aether_<ts>_<rand>", order_description, ipn_callback_url:"https://api.get-aether.de/api/ipn"}`
- Fixed IPN for ALL: `https://api.get-aether.de/api/ipn` — never per-coin/package. XMR removed; only BTC/LTC/ETH.
- D1 is used ONLY for accounts, orders and per-order chat. Order/chat data is never shared with anyone else and never exposed without auth (admin routes need `ADMIN_EMAILS`).
- Frontend `apiBase = ""` → **same origin**: `/api/*` on get-aether.de is routed to the Worker (worker route `get-aether.de/api/*` + `www.get-aether.de/api/*`), so the session cookie is first-party (`__Host-aether_session`, HttpOnly, Secure, SameSite=Lax). `https://api.get-aether.de` still works (legacy/compat, SameSite=None + Bearer).
- CORS is an **explicit allowlist** (`ALLOWED_ORIGIN`); `*` is deliberately ignored by the Worker. Unknown origins get no CORS headers and their writes get 403.
- Local dev: `node _dev-server.mjs` → http://127.0.0.1:5501 serves the static site AND `/api/*` through the real Worker with an in-memory D1. `python -m http.server` no longer reaches the API (that is intentional).
- Security architecture + manual dashboard steps live in **SECURITY.md**. Read it before changing auth, cookies, CORS or headers.
- **Signed-in customers never type an email.** `POST /api/invoice` and `POST /api/order` both resolve the account first: if the body has no usable email (or Discord) and the request carries a valid token, the account's own email/Discord are used. A typed email always wins. Signed out, an email is still required. The frontend does the same via `AetherCheckout.accountEmail()` / `resolveEmail()` so the UI never shows an empty required field to a member.
- **Never write `[^\\s@]` in a JS regex.** `public/checkout.js` shipped with a doubled backslash in `isEmail`, so the class meant "not backslash, not s, not @" and rejected every address containing the letter **s** (`test@example.com`, `wispz@outlook.de`). Fixed 2026-09-30. `node _check-email-regex.mjs` runs every `isEmail` in the project against addresses that must pass/fail — run it after touching validation.
- **worker/src/index.js must stay 100% ASCII** (escape every €, —, →, · as `\uXXXX`). A non-ASCII byte is what turned every character in the Discord embed into `?`. `_build_chunks.mjs` aborts if it finds one.
- **PBKDF2 in workerd rejects iterations > 100000** ("Pbkdf2 failed: iteration counts above 100000 are not supported"). `PBKDF2_ITERATIONS = 100000` is the ceiling; anything higher made register/login return a bare `1101`.

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

## WORKER API (all live)
- `GET  /api/health` → `{ok,service,time,email,payments,ipnSignature,discord,turnstile,db}` (booleans only — no addresses/config)
- `POST /api/auth/forgot` → single-use reset link (30 min); answers identically for unknown emails; honest 503 while `RESEND_API_KEY` is missing
- `POST /api/auth/reset` → `{token,password}` → replaces the password, kills every other session, signs the user back in
- `POST /api/auth/password` → signed-in password change (needs `currentPassword`)
- `POST /api/auth/verify-email` → consumes a 24 h verification token
- `POST /api/order` → validates (Discord optional, **email optional when signed in**), questions@ vs business@ routing, auto-reply
- `POST /api/invoice` → **email optional when signed in** (falls back to the account email), `pay_currency` + promo validation
- `POST /api/invoice` → validates pay_currency + promo, creates NOWPayments Payment, stores the D1 order (links `user_id` when signed in **or when the email already owns an account**), sends pending email+Discord, returns `{orderId,paymentId,promoApplied,discount,…}`
- `POST /api/ipn` → verifies `x-nowpayments-sig` HMAC-SHA512 against **both** IPN secrets; handles waiting/confirming/confirmed/finished/sending/failed/expired/partially_paid; updates `orders.status`; email+Discord
- `GET  /api/payment/:paymentId` (or `?payment_id=`, `/api/status`) → verifies via `api.nowpayments.io/v1/payment/:id` → `{status,isPaid,isPending,isFailed}`
- `GET|POST /api/promo?code=&amount=` → `{valid,code,type,value,discount,finalAmount}`; unknown codes return `{valid:false}` (never 500)
- `POST /api/auth/register` · `POST /api/auth/login` · `POST /api/auth/logout` → `{ok,token,user,emailVerified,isAdmin}` + `__Host-aether_session` cookie (HttpOnly, Secure, SameSite=Lax same-origin / None on the api. host). Sessions are stored as **SHA-256(token)** in D1, rotate on login, expire after 14 days, capped at 10 per user.
- `GET  /api/me` (`isAdmin`) · `DELETE /api/me` → a user deletes their own account (sessions removed, orders/messages detached, not deleted)
- `GET  /api/orders` · `GET /api/orders/:id` · `POST /api/orders/:id/message` → the customer's orders and per-order chat
- `GET  /api/admin` · `GET /api/admin/orders[?status=&limit=]` · `GET /api/admin/orders/:id` · `POST /api/admin/orders/:id/message` · `GET /api/admin/users` · `DELETE /api/admin/users/:id` · `GET /api/admin/messages` → everything, admin only (`ADMIN_EMAILS`), 403 otherwise
- Auth accepts **either** `Authorization: Bearer <token>` **or** the cookie, so accounts survive blocked third-party cookies.

## WORKER VARIABLES
- `CONTACT_TO`, `CONTACT_FROM`, `SUCCESS_URL`, `CANCEL_URL` — unchanged
- `ALLOWED_ORIGIN` — comma-separated origin allowlist. Currently `*` in the vars, which the code **ignores** and falls back to `https://get-aether.de,https://www.get-aether.de,https://api.get-aether.de`. Set it explicitly in the dashboard so the config matches reality.
- `SITE_URL` (default `https://get-aether.de`) — used in password-reset / verification links
- `TURNSTILE_SECRET` (secret, optional) + `TURNSTILE_LOGIN` (optional, `true` = captcha on login too). Until the secret exists no captcha is required and `public/aether-config.js → turnstileSiteKey` should stay empty.
- `ALLOW_DEV_ORIGIN` — local development only, must stay unset/false in production.
- `PROMO_CODES` — JSON map, e.g. `{"WELCOME10":{"type":"percent","value":10},"SAVE5":{"type":"fixed","value":5}}`. Shorthand: a number = percent, `"10%"` = percent. **Currently empty → `/api/promo` returns `{valid:false}`.**
- `ADMIN_EMAILS` — comma-separated admin emails, e.g. `you@example.com,ops@example.com`. **Currently `Wispz@outlook.de` (guessed — change it to the email you actually sign in with).** Empty = nobody is admin (all `/api/admin/*` → 403).
- D1 binding `DB` → `aether-db` (`201052a6-bed3-4c1c-a3ee-e16394aa36e4`, WEUR)

## D1 SCHEMA (live)
`users(id, email UNIQUE, password_hash, discord, created_at, email_verified)` ·
`sessions(token PK = **sha256 of the session token**, user_id, created_at, expires_at)` ·
`auth_tokens(token_hash PK, user_id, purpose 'reset'|'verify', expires_at, created_at)` ·
`rate_limits(rl_key PK, count, window_start)` ·
`orders(id, user_id, order_id UNIQUE, payment_id, amount, currency, type, package, description, status DEFAULT 'pending', promo_code, discount, meta, extra, created_at, email)` ·
`messages(id, order_id, user_id, sender DEFAULT 'customer'/*'admin'*/, body, created_at)` ·
indexes `idx_sessions_user, idx_orders_user, idx_orders_orderid, idx_orders_email, idx_messages_order`

## DEPLOY (wrangler is NOT authenticated — no CLOUDFLARE_API_TOKEN)
1. `node --check worker/src/index.js`
2. `node _build_chunks.mjs` → writes `_gz2_00.txt`…`_gz2_10.txt` (gzip + base64, 2000 chars each) and prints `sha256`, per-chunk `len/sum`. **Aborts if the source is not pure ASCII.**
3. For each chunk `i`, call the Cloudflare MCP `execute` tool with `cloudflare.request({method:"PUT", path:`/accounts/${accountId}/storage/kv/namespaces/747e914c4ebf40c5b64d876090f1b467/values/aether-gz-${i}`, body:<that 2000-char chunk>, contentType:"text/plain", rawBody:true})` and GET it back to compare `len`/`sum`. **One chunk per call — the sandbox rejects a code string longer than ~2.2 KB ("Unexpected token ';'").**
4. One more `execute` call: GET `aether-gz-0..10` + `aether-meta`, `atob` → gunzip via `new Response(new Response(bytes).body.pipeThrough(new DecompressionStream("gzip")))`, verify `sha256 == expected`, then multipart-PUT to BOTH `aether-api` and `aether-payments`. Metadata carries the bindings; secret bindings survive re-upload. Reference snippet: `aether-deploy.mjs`.
5. The multipart module part **must** be `Content-Disposition: form-data; name="index.js"; filename="index.js"` + `Content-Type: application/javascript+module` — without `filename` Cloudflare answers `10021: No such module: index.js`.
6. The multipart metadata must include `main_module:"index.js"`, `compatibility_date`, `compatibility_flags:["nodejs_compat"]` and `keep_bindings:["plain_text","secret_text","d1","kv_namespace"]` — that last field is what preserves the Worker secrets and the D1 binding across a code-only deploy. Verified live 2026-10-01 (`db:true`, `payments:true`, `ipnSignature:true`).
7. Chunk values can hold 2000 chars; putting 3 per `execute` call works (the real limit is higher than the old ~2.2 KB note). Always compare `len`/`sum` on upload and the final `sha256` before the PUT.
8. After a deploy, allow ~1 minute before believing a failure — the previous version can still answer briefly.

## LOCAL TESTS (no credentials needed)
`node _test-worker.mjs` — loads the real worker against an in-memory SQLite D1 stub (`node:sqlite`) and runs **106 checks**: health, promo maths, register/login/duplicate/wrong-password, me, orders, invoice + email-based order claiming, customer chat, admin guards, admin orders/users/messages/reply/delete, guest-order claiming on register, account deletion, account-derived email, plus the hardening suite: security headers, CORS allowlist + wildcard rejection, cross-site write 403, hashed session storage, per-IP login rate limiting, password policy, forgot/reset/change-password/verify-email, single-use + expiry rules, 413 body cap, truncated descriptions, no provider payload echo. Run this **before every deploy** — it has caught several bugs that would have been live outages.
`node _dev-server.mjs` — full local stack (static + real Worker + in-memory D1) on http://127.0.0.1:5501, prints a random local admin password at startup.
`node _check-inline-js.mjs <pages…>` — parses every inline `<script>` block in the HTML pages.
`node _check-email-regex.mjs` — actually *runs* each `isEmail` it finds against addresses that must pass/fail (this is what caught the `[^\\s@]` bug).

## WHAT IS ALREADY DONE [x]
- [x] Worker deployed LIVE (2026-09-30, tag modified 13:50 UTC) — health `{"ok":true,"resend":false,"nowpayments":true,"ipnSecret":true,"discord":true,"db":true,"promos":0}`
- [x] Secrets set: NOWPAYMENTS_API_KEY, IPN ×2, DISCORD_WEBHOOK_URL (canary) — all present and preserved through re-uploads
- [x] Discord username optional; XMR removed (BTC/LTC/ETH only)
- [x] Custom orders carry extra notes + "reply within 3 days"
- [x] CORS `*` + `authorization` header + `DELETE` method + `credentials` + exposed `set-cookie`
- [x] checkout.js: payAddress + orderId + copy, 8s polling, only `isPaid` auto-redirects; **sends `Authorization: Bearer` from localStorage**; `apiFetch` helper
- [x] payment-success.html verifies server-side via `GET /api/payment/:id` (cannot be faked)
- [x] **Discord embeds beautified + title/description/fields/author/footer/thumbnail** (Aether logo), per-status colours, `sendDiscord` retries 3× with backoff
- [x] **Mojibake fixed** — worker is ASCII-only with `\uXXXX` escapes. Verified live: em dash U+2014, € U+20AC, curly quotes, → U+2192, ✓ all round-trip byte-exact.
- [x] **Accounts (D1)** — register/login/logout/me, PBKDF2-SHA256 @100000 iters, 30-day sessions, cookie + Bearer
- [x] **Orders filed to accounts** — signed-in user, or matched by email at checkout; guest orders are claimed when that email registers
- [x] **Per-order chat** — customer page + email + Discord ping; admin replies land in the same thread and email the customer
- [x] **Promo codes** — `PROMO_CODES` JSON, percent/fixed, capped, applied in builders + invoice, discount stored on the order
- [x] **Admin** — `/api/admin/*` for all orders + all accounts + all direct messages, reply-as-admin, delete any account; non-admins get 403
- [x] **Self-service account deletion** — `DELETE /api/me` (typed confirmation in the UI)
- [x] **account.html** + **admin.html** + `public/portal.css`; "Account" link added to every page's nav (bumped `?v=4`, then `?v=5`)
- [x] **Account-aware checkout (2026-09-30)** — `AetherCheckout` caches the signed-in account (`localStorage aether_account`, refreshed from `/api/me` once per page) so the pay modal shows "Email — from your account" instead of an email field, prefills Discord, and offers a Change button; request forms prefill and drop the required `*` via `bindAccountFields()`; the worker derives the same on the server. Worker tests cover it.
- [x] **Bigger-project options (2026-09-30)** — per-add-on pricing in both builders, Project Size selector on both, large-project quote copy in both request sections, stale shop pricing bullets corrected (`+5 cmds +€5` / `+1 revision +€3` were wrong).
- [x] **isEmail bug fixed** — `public/checkout.js` rejected every address containing the letter `s`. Found with `_check-email-regex.mjs`; the worker was unaffected.
- [x] **All pages bumped to `?v=5`** (checkout.js changed)
- [x] Builders: €2/extra command (>10), €3/feature, ≤4 revisions (2 free, +€7), €5/extra page (>3), complexity presets, promo field, shareable URL that restores state
- [x] Live verified end-to-end 2026-09-30: register/login/me/logout, real BTC+LTC invoices (order + payment_id stored, status `waiting`), chat message, admin 403 for non-admins, account deletion, UTF-8 integrity. Frontend verified in a real browser (dashboard, empty state, not-an-admin notice, admin tables).
- [x] Test data removed from D1 — `users`/`orders`/`messages`/`sessions` are all at 0
- [x] **Production hardening (2026-10-01)** — worker rebuilt + deployed live (sha256 `5c9323b41d522d244056625c9cff4710320ed8a247914971ad1978223eef733f`, 82 406 bytes, both `aether-api` and `aether-payments`), D1 migrated (`auth_tokens`, `rate_limits`, `users.email_verified`, legacy plaintext sessions purged), 106 local checks green, new pages + UI layer shipped, 160+ legacy deploy artifacts and the 5.3 MB scraped `page_content (8)` folder deleted, `SECURITY.md` + `.gitignore` + `.env.example` added.

## INFORMATION STILL NEEDED / MANUAL ACTIONS (user must do)
- [ ] **RESEND_API_KEY still missing** → `resend:false` → no email is delivered (Discord still works). Cloudflare Dashboard → Workers & Pages → aether-api → Settings → Variables → Encrypted → `RESEND_API_KEY`. Then Resend → Domains → verify get-aether.de → add TXT/DKIM to DNS (**do NOT delete MX route*.mx.cloudflare.net**).
- [ ] **Confirm `ADMIN_EMAILS`** — currently the guess `Wispz@outlook.de`. It must equal the email you register/sign in with at /account.html, otherwise /admin.html shows "Not an admin account". Comma-separate several. (Change it in Workers → aether-api → Settings → Variables — applies instantly, no redeploy.)
- [ ] **Set `PROMO_CODES`** — same place. Until then every code returns `{valid:false}`.
- [ ] NOWPayments Dashboard → Settings → IPN → Callback URL = `https://api.get-aether.de/api/ipn` (delete the old `aether-payments.wispz.workers.dev/nowpayments-ipn`), enable BTC/LTC/ETH, both IPN secrets matching the Worker.
- [x] Worker deployed 2026-10-01 — live health: `{"ok":true,"service":"aether-api","email":false,"payments":true,"ipnSignature":true,"discord":true,"turnstile":false,"db":true}`. Bindings + secrets survived the code-only upload (`keep_bindings`).
- [ ] **Push the frontend to GitHub Pages** — every page (`?v=6` bumps), `public/checkout.js`, `public/aether-config.js`, new `public/ui.css`, plus `forgot-password.html`, `reset-password.html`, `verify-email.html`, `donate.html`, `error.html`, `404.html`. The worker is already deployed (sha `5c9323b4…`) and the `/api/*` route exists, so this push is safe — the pages rely on the same-origin API.
- [ ] **Turnstile (optional)**: create a widget for get-aether.de, put the site key in `public/aether-config.js` and the secret in the Worker. Set both or neither.
- [ ] D1 currently holds **1 real account** (`alex.real.apple@gmail.com`, created 2026-09-30 14:35) — that is a genuine signup, not test data. Test rows are at 0.
- [ ] There is no git repo in this working folder — commit from wherever the Pages site is actually maintained.

## HOW TO VERIFY AFTER DONE
```
curl https://api.get-aether.de/api/health
node _test-worker.mjs                     # 74 local checks, no credentials needed
node _check-email-regex.mjs               # every isEmail actually runs against real addresses
node _check-inline-js.mjs *.html          # inline <script> blocks parse
curl "https://api.get-aether.de/api/promo?code=WELCOME10&amount=30"
curl -X POST https://api.get-aether.de/api/auth/register -H "content-type: application/json" \
  -d '{"email":"you@example.com","password":"at-least-8-chars"}'
# Browser: http://127.0.0.1:5501/account.html → create account → orders + chat
#          http://127.0.0.1:5501/admin.html   → all orders / accounts / messages
#          http://127.0.0.1:5501/discord-bot.html#custom-bot-builder → share link restores state
```

## SECURITY HARDENING (2026-10-01) — see SECURITY.md for the full picture
**In code (worker + frontend):** CORS allowlist (never `*`, never reflecting unknown origins), Origin/Referer checks on every write (CSRF), security headers on every API response (nosniff, frame-ancestors none, HSTS, no-referrer, permissions-policy, no-store), two-layer rate limiting (in-isolate burst + D1 fixed windows per route), Turnstile verification hook (off until the secret exists), password reset + change + email verification with single-use hashed tokens, hashed session storage + rotation + expiry + per-user cap, honest 503s instead of fake successes, sanitised errors/health, 64 KB body cap and per-field length caps, dead code removed (static payment links, minified legacy bundle).
**Frontend:** same-origin API, token now lives in `sessionStorage` for the tab only (cookie is the primary mechanism, legacy localStorage copy is migrated then deleted), new `/forgot-password.html`, `/reset-password.html`, `/verify-email.html`, `/donate.html`, `/error.html`, `/404.html`, password meter, toasts, focus-visible rings, reduced-motion support, `public/ui.css`, Donate in every nav, favicon switched from the 1.2 MB PNG to the 436-byte SVG.
**Cloudflare (applied via API):** Always Use HTTPS, min TLS 1.2, HSTS 1 year + includeSubDomains, worker routes for `get-aether.de/api/*` and `www.get-aether.de/api/*`, Cloudflare Managed Free Ruleset (WAF) deployed, one edge rate-limit rule (10 req / 10 s per IP+colo on auth + order/contact/invoice, block 10 s). Bot Fight Mode intentionally OFF so NOWPayments IPNs are never challenged.
**Still manual:** Turnstile keys, Resend key, SSL mode → Full (strict), cache rules, `ALLOWED_ORIGIN` value, alerts. Listed in SECURITY.md §4.

## UPDATE LOG
- 2026-09-30 15:55 — **Account-aware checkout + bigger-project options.** Worker: `/api/invoice` and `/api/order` now take the email/Discord from the signed-in account when the body has none (a typed email still wins). checkout.js: account cache + account chip in the pay modal + `resolveEmail`/`bindAccountFields`/`accountEmail`/`loadAccount`, `auth.register|login|logout` keep the cache in sync. Both builders: per-add-on `data-price`, new heavy add-ons, Project Size (+€40/+€100), `size=` in the URL, large-project quote copy; request forms no longer demand an email from members. **Fixed a live bug: `isEmail` in checkout.js rejected any email containing the letter `s`.** New tools `_check-inline-js.mjs` + `_check-email-regex.mjs`; worker suite grown to **74 checks, all passing**. Browser-verified: guest modal, account modal, Change button, empty-email fallback, both builders' maths (€190 / €260), both request forms capturing the account email, and a real LTC invoice created end-to-end with no email typed (test row deleted from D1 afterwards). `?v=4` → `?v=5`.
- 2026-09-24 17:04 — checkout.js 16655 bytes, ?v=2 live, health verified
- 2026-09-27 12:19 — LIVE VERIFIED: invoice BTC €15, payment status pending, CORS *, polling, success-page verification, ?v=2 → ?v=3
- 2026-09-30 13:00 — Worker rewritten ASCII-only; beautified Discord embeds with retries; promo engine; D1 accounts + orders + per-order chat; IPN status colours; checkout.js promo + Bearer auth
- 2026-09-30 13:40 — Admin routes (`/api/admin/*`) + `DELETE /api/me`; guest orders claimed by email; `orders.email` column added; global error handler so failures return JSON instead of a bare 1101; `?v=3` → `?v=4`; account.html + admin.html + portal.css; MASTER_PROMPT rewritten
- 2026-09-30 13:50 — **Fixed register/login outage**: workerd rejects PBKDF2 > 100000 iterations → `PBKDF2_ITERATIONS = 100000`. Deployed, verified live (register/login/me/invoice/chat/delete), UTF-8 proven byte-exact, D1 test rows cleaned.
- 2026-09-30 — Added `_test-worker.mjs` (65 checks) + `_build_chunks.mjs` (ASCII guard + chunk generator) so future deploys are test-first.
