# Aether — live implementation checklist

Single source of truth for the customer-portal / Beta work. Status legend:

- `[x]` completed and verified
- `[ ]` pending
- `[!]` blocked / failed (reason next to it)
- `[ ] MANUAL` requires an external action (Cloudflare dashboard, DNS, secret, DNS/GitHub)

> Do not create a second TODO list. Update this file as work lands.

## 0. Architecture analysis (existing system)

- [x] Static frontend (GitHub Pages) + Cloudflare Worker `aether-api` (same-origin `/api/*`)
- [x] D1 `aether-db`: users, sessions, orders, messages, auth_tokens, rate_limits
- [x] Sessions: `__Host-aether_session` cookie, sha256(token) at rest, 14-day TTL, rotation on login
- [x] Password reset / change, email verification endpoints (shipped in the hardening phase)
- [x] Per-order chat (orders + messages) and admin panel (orders/users/messages)
- [x] No git repository existed in the working copy -> initialized a real repo (see §11)
- [x] No separate Beta deployment existed -> created (see §11)

## 1. Data model (D1)

- [x] `users.role` (`user` | `tester`; `admin` is derived from `ADMIN_EMAILS`/`ADMIN_EMAIL` only)
- [x] `orders.purchase_id` + unique index
- [x] `conversations` (conversation_id, user_id, order_id, purchase_id, subject, status, assigned_admin, created_at, updated_at)
- [x] `messages.conversation_id`
- [x] `auth_tokens.payload` (pending Beta-domain change; never a password)
- [x] `settings` (server-side Beta host/path + feature flags)
- [x] `audit_log` (administrative actions)
- [x] `beta_feedback` (Beta-only data, isolated from production tables)
- [x] Migration applied to the live `aether-db`

## 2. Purchase IDs

- [x] Server-generated `AETH-2026-XXXXXXXX` (CSPRNG, ambiguity-free alphabet)
- [x] Unique (checked + unique index), never accepted from the browser
- [x] Lazy backfill for orders that predate the column
- [x] Owner-only lookup `GET /api/purchases/:purchaseId` (identifier, not a credential)
- [x] Shown in the portal, order detail, admin panel, and notification emails
- [x] Chats can reference a Purchase ID (validated against the authenticated account)

## 3. Authentication / registration / email verification

- [x] Registration (email + password + confirmation in the UI), rate limited, unique email
- [x] Password policy, PBKDF2 hashing, no plaintext anywhere
- [x] Verification email on register (24h, single-use, hashed token at rest)
- [x] `POST /api/auth/resend-verification` — session-bound, per-account rate limited
- [x] Unverified accounts are blocked from purchases, chat, and Beta (`403 EMAIL_UNVERIFIED`)
- [x] Enforcement is server-side and flips on as soon as an email provider is configured
- [x] Password reset already existed (generic answers, single-use, kills sessions)

## 4. Chat system (no anonymous chat)

- [x] Every conversation is owned by the session account (never a client-supplied id)
- [x] IDOR-proof: `/api/conversations/<other id>` -> 404, posting -> 404
- [x] New conversation, list, open, message history, timestamps, status, Purchase ID link
- [x] Message sanitising (control chars stripped, 4000-char cap) + rate limits
- [x] Legacy per-order chats are surfaced in the same history (idempotent bridge)
- [x] Admin support replies land in the same thread (and email the customer)

## 4b. Checkout / orders — account-only (2026-10-01)

- [x] `POST /api/invoice` requires a session: anonymous or junk-token requests get `401 {"code":"ACCOUNT_REQUIRED"}` and **no order is written**
- [x] The order always belongs to the session account (`user_id` comes from the session, never from the request body)
- [x] The account email wins over any posted email; a posted email is only a fallback for an account row with no usable address
- [x] The verified-email gate still runs before payment for signed-in accounts
- [x] Frontend: the pay modal hides the email field, states that an account is required and routes "Sign in to buy" / "Switch account" to `account.html?next=<page>`
- [x] `account.html` honours `?next=` (same-site relative paths only, never itself) and returns a signed-in buyer to the page they came from
- [x] Public copy cleaned up (`account.html`, `admin.html`, README, MASTER_PROMPT) — no "guest checkout still works" left anywhere
- [x] `/api/order` order requests (`type: discord_bot|website`) are **account-only too**: anonymous → `401 ACCOUNT_REQUIRED`, the account email is the only reply address, and the request is filed into the customer's portal chat (`conversationId` in the response). The general `type: contact` form stays open and still asks for an email.
- [x] The request forms have **no email box left** (`discord-bot.html`, `website.html`): they show the signed-in account instead, gate submission on a session and use the same CTA as checkout; `/api/order` never writes an order row
- [x] Legacy rows written by the old guest checkout are still claimed when that email registers (tested)

## 5. Tester / Beta

- [x] Server-side roles; `admin` cannot be granted through the API (configuration only)
- [x] Admin can grant/revoke Tester; every change is audited
- [x] Beta API (`/api/beta/*`) enforces session + verified email + Tester role
- [x] Beta access state is server-provided; ordinary users get `allowed: false` and no URL
- [x] Server-side feature flags (`betaNewChat`, `betaDashboard`, `betaTools`)
- [x] Single-use 120s beta ticket -> redeem for a session on another Beta hostname
- [x] Beta feedback stored in `beta_feedback` (never production tables)
- [x] Obscure Beta path treated as obscurity only, never as security

## 6. Beta domain configuration (admin panel)

- [x] Stored server-side (`settings`), env fallback (`BETA_HOST`/`BETA_PATH`), never hardcoded in the frontend
- [x] Change requires admin auth + verified email + password re-auth + email confirmation
- [x] Confirmation token: 64-hex, expiring (30 min), single-use, hashed at rest
- [x] Still-admin re-check at confirmation time; audit logged
- [ ] MANUAL: deploy the Beta app on `betatester.get-aether.de` (DNS + route + separate deployment)

## 7. Admin / Alpha panel

- [x] Account list shows role + email verification status
- [x] Grant Tester / Remove Tester buttons (server enforced)
- [x] Customer conversation support inbox with replies and status
- [x] Beta configuration section (current URL, pending change, confirm field, feature flags, audit trail)
- [x] Sensitive actions require a verified admin email when verification is enforced

## 8. Cloudflare edge

- [x] Worker routes `get-aether.de/api/*` + `www.get-aether.de/api/*` (same-origin API)
- [x] TLS: always-use-HTTPS, min TLS 1.2, TLS 1.3, HSTS 1y includeSubDomains + nosniff
- [x] Managed WAF ruleset deployed; security level medium; browser check on
- [x] Edge rate limit on `/api/auth/*` + order/contact/invoice endpoints (free plan: 1 rule)
- [x] Application rate limits per endpoint (D1 + isolate counters)
- [x] DDoS protection via Cloudflare proxy (always-on, automatic)
- [ ] MANUAL: add edge rate-limit rules for `/api/conversations*` + `/api/beta/*` (free plan allows 1 rule)
- [x] Turnstile widget + secret live 2026-10-01: managed widget for `get-aether.de` (`www`, `api`), site key in `public/aether-config.js`, secret on the Worker **and** as the `TURNSTILE_SECRET` GitHub secret; the register form gained its `[data-turnstile]` mount first (shipping the secret without it would have broken signups)
- [!] SSL/TLS mode: **Full (strict) is impossible as long as GitHub Pages is the origin** — enabling it on 2026-10-01 produced an instant site-wide **HTTP 526**; reverted to `Full` within minutes and the site came back (verified: `/`, `/shop.html`, `/account.html`, `/public/checkout.js`, `/public/aether.css` all 200). GitHub Pages answers with `CN=*.github.io` for `get-aether.de`, so strict validation can never succeed. Prerequisite before retrying: move the static site behind a Cloudflare-managed certificate (Cloudflare Pages / Worker with static assets) or let GitHub provision a cert for the domain with the proxy temporarily DNS-only. See SECURITY.md §4.3.
- [x] `ADMIN_EMAIL=alex.real.apple@gmail.com` set and the guessed `ADMIN_EMAILS=Wispz@outlook.de` emptied (bindings re-sent with `keep_bindings:["secret_text"]`; all four secrets + D1 survived, proven by `/api/health`)
- [x] `ALLOWED_ORIGIN` set explicitly to `https://get-aether.de,https://www.get-aether.de,https://api.get-aether.de` (wildcards are still ignored by code)
- [ ] MANUAL: set `RESEND_API_KEY` so verification/reset emails really send

## 9. Frontend / UI

- [x] Customer portal: registration, login, dashboard, purchases (with Purchase IDs), chat history, settings
- [x] Verification state banner + resend button
- [x] Beta entry shown only to testers (server-provided), `beta.html` area with indicators
- [x] Admin UI: roles, verification status, conversations, Beta config
- [x] Shared styling for chat lists, badges, status pills, empty/loading/error states
- [x] Every page and the favicon use the black-and-white logo `public/aether-logo.png` (128 px, 6.7 KB, generated by `_make-logo.mjs` from the original 1.2 MB asset, which now serves only the Discord embed thumbnail); the colour variant stays as `public/aether-logo-gradient.svg`
- [x] `README.md` written for the public repository (architecture, layout, local dev, deployment, security posture, docs index)
- [x] Publishing metadata (2026-10-01): meta descriptions on all 17 pages, `og:*`/`twitter:*` on the 8 indexable pages backed by a purpose-built 1200x630 `public/og.png` (`_make-og.mjs`), `noindex` on the payment-result + admin pages, `color-scheme`/`theme-color`, `role=status`/`aria-live` on the contact/request form messages, corrected `sitemap.xml` comment + `lastmod`
- [x] One site-wide asset version `?v=7` (was: styles/config `?v=6`, `checkout.js` `?v=7`) so a shared-file change can never be half-cached
- [x] Package-card `Email instead` links were mailto **order** links (`subject=Order - …`) — i.e. a guest order path through an email client. They now say `Ask a question` with a `Question about …` subject; the only purchase/request route is an account

## 10. Testing / audit

- [x] 191 automated checks (`node _test-worker.mjs`) incl. IDOR, privilege escalation, Beta gating
- [x] `_check-assets.mjs` added and wired into both workflows (one `?v=` across every page, 1200x630 OG card, description + `og:image` coverage)
- [x] Rate-limit tests deflaked 2026-10-01: the one red Beta CI run on `294caf7` was the 1 h D1 window rolling over mid-loop at 17:00:00 UTC — both limiter checks now seed the window at the limit instead of looping (still 191 checks, 0 failed)
- [x] `.acc-note` base style moved from `portal.css` to `ui.css` — fixes the unstyled account notes on `discord-bot.html`/`website.html`
- [x] Secret scan (repo + `public/` contain no keys, tokens, or credentials — only `.env.example` placeholders)
- [x] Worker stays ASCII-only; `_build_chunks.mjs` ASCII guard passes (0 non-ASCII bytes)
- [x] Worker rebuilt + redeployed live 2026-10-01: sha256 `1a1c22b74b282b96efb3739c9be233237ee4601b417c9e33e35f10e56a14fee7`, 135 027 bytes, 22 chunks, uploaded to `aether-api` **and** `aether-payments` (200/ok)
- [x] Account-only checkout redeployed 2026-10-01: sha256 `5e139dde5031dc1d75ad0f5f0baf8cd655632aa1060f7f8948762be61cce48af`, 135 412 bytes, **6 of 22 chunks** changed (16–21), verified by re-downloading the KV chunks and comparing the sha256 before upload; `aether-meta` KV updated
- [x] Live proof of the gate: anonymous `POST /api/invoice` → `401 {"error":"Checkout requires an account…","code":"ACCOUNT_REQUIRED"}`, a disallowed origin still → 403, `/api/conversations` → 401, `/api/health` all-true after both deploys and the binding change
- [x] Account-only **request forms** deployed 2026-10-01: sha256 `934adbfd14d17d771260d4a2f904be8edfd57aaae7bbf0b2f1d64ff970a00268`, 137 805 bytes, 23 chunks, both scripts. Live proof: anonymous `POST /api/order` with `type: discord_bot` **and** `type: website` → `401 ACCOUNT_REQUIRED` (no email typed helps), while `type: contact` with an email still returns `ok:true` — so general questions stay open but nothing that buys or requests work can be placed from an accountless browser
- [x] Live verification after deployment: `/api/health` → `{ok:true,email:false,payments:true,ipnSignature:true,discord:true,db:true}`; anonymous `/api/conversations`, `/api/purchases/…`, `/api/beta/access` and `/api/me` all 401
- [x] Browser-verified locally: portal dashboard (purchase + conversation history), tester `beta.html`, admin tabs (Conversations, Beta config + audit) incl. the whole Beta-domain request/confirm flow

## 11. Git + independent Beta deployment

- [x] Real git repository initialized in the working copy (`main`)
- [x] `beta` branch created and independently deployable (separate workflow + separate Beta config)
- [x] Production checks run only on `main`/PRs; the Beta workflow only on `beta`, and it never falls back to the production Pages site
- [x] Remote added and both branches pushed: `https://github.com/Wiiffies/Aether-Website` (public)
- [x] GitHub Pages publishes `main` through its branch build, custom domain `get-aether.de` kept by the committed `CNAME`, artifact trimmed by `_config.yml`
- [x] `deploy-beta.yml` publish job wired 2026-10-01: builds a site-only artifact (`_beta_dist`, `betaDeployment` marker checked) and deploys it to the `BETA_TARGET` Cloudflare Pages project with the shared `CLOUDFLARE_API_TOKEN` — no separate Beta token
- [ ] MANUAL: set `BETA_TARGET` (Cloudflare Pages project name) and create that Pages project

## 12. Remaining / blocked

- [ ] MANUAL: DNS record + worker route for the Beta hostname (`betatester.get-aether.de` + the long path)
- [ ] MANUAL: separate Beta Worker with its own D1/KV/secrets (data isolation) before real Beta testing — today isolation is configuration, not a hard boundary
- [ ] MANUAL: DNS record + route if the portal should live on its own `Customer.get-aether.de` hostname (today it is same-origin on get-aether.de)
- [ ] MANUAL: `PROMO_CODES` (decide the codes) and `RESEND_API_KEY` — `ADMIN_EMAIL` and `TURNSTILE_*` are done as of 2026-10-01
- [x] MANUAL: explicit `ALLOWED_ORIGIN` set (2026-10-01); SSL mode cannot move to `Full (strict)` until the origin stops being GitHub Pages (see §8)
- [ ] MANUAL: second edge rate-limit rule for chat/Beta/admin (free plan allows one — the existing rule must not throttle `/api/ipn`)

## 13. GitHub Actions secrets + variables (added 2026-10-01)

Managed through the installed `gh` CLI; secret values go in via stdin (`printf '%s' "$VALUE" | gh secret set NAME --repo Wiiffies/Aether-Website`), never on a command line or in a file.

- [x] variable `CLOUDFLARE_ACCOUNT_ID` = `e83c68e5e26e3e9096542df702331096` (deploy-worker.yml reads it instead of a secret copy)
- [x] variable `BETA_HOST` = `betatester.get-aether.de` (the beta workflow's documented host)
- [x] secret `TURNSTILE_SECRET` (set 2026-10-01, mirrors the live Worker secret; Turnstile verified live the same day: health `turnstile:true`, 403 without a token on register/forgot, a real browser token accepted, full signup `200`, smoke account deleted)
- [x] `deploy-worker.yml` gained an optional **secret sync**: `RESEND_API_KEY`, `TURNSTILE_SECRET`, `NOWPAYMENTS_API_KEY`, `NOWPAYMENTS_IPN_SECRET(_2)`, `DISCORD_WEBHOOK_URL` — only the names present in GitHub are pushed, so a blank copy can never blank a live value
- [ ] MANUAL: secret `CLOUDFLARE_API_TOKEN` — Cloudflare refuses to mint tokens for our automation (`9109 Unauthorized`), so it has to be created in the dashboard with **Workers Scripts:Edit + Workers KV Storage:Edit + D1:Edit**. Until then `deploy-worker.yml` runs the checks and skips the deploy
- [ ] MANUAL (optional): add `RESEND_API_KEY` to GitHub once Resend exists — the sync step then keeps the Worker and the repo in agreement
- [ ] MANUAL: set `BETA_TARGET` to a Cloudflare Pages project — the publish job is wired and refuses to fall back to the production Pages site
