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
- [x] `/api/order` (the free inquiry/quote form) never writes an order — it stays an email/Discord notification path only
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
- [ ] MANUAL: deploy Turnstile widget + secrets (`TURNSTILE_SECRET`, `turnstileSiteKey`)
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

## 10. Testing / audit

- [x] 183 automated checks (`node _test-worker.mjs`) incl. IDOR, privilege escalation, Beta gating
- [x] Secret scan (repo + `public/` contain no keys, tokens, or credentials — only `.env.example` placeholders)
- [x] Worker stays ASCII-only; `_build_chunks.mjs` ASCII guard passes (0 non-ASCII bytes)
- [x] Worker rebuilt + redeployed live 2026-10-01: sha256 `1a1c22b74b282b96efb3739c9be233237ee4601b417c9e33e35f10e56a14fee7`, 135 027 bytes, 22 chunks, uploaded to `aether-api` **and** `aether-payments` (200/ok)
- [x] Account-only checkout redeployed 2026-10-01: sha256 `5e139dde5031dc1d75ad0f5f0baf8cd655632aa1060f7f8948762be61cce48af`, 135 412 bytes, **6 of 22 chunks** changed (16–21), verified by re-downloading the KV chunks and comparing the sha256 before upload; `aether-meta` KV updated
- [x] Live proof of the gate: anonymous `POST /api/invoice` → `401 {"error":"Checkout requires an account…","code":"ACCOUNT_REQUIRED"}`, a disallowed origin still → 403, `/api/conversations` → 401, `/api/health` all-true after both deploys and the binding change
- [x] Live verification after deployment: `/api/health` → `{ok:true,email:false,payments:true,ipnSignature:true,discord:true,db:true}`; anonymous `/api/conversations`, `/api/purchases/…`, `/api/beta/access` and `/api/me` all 401
- [x] Browser-verified locally: portal dashboard (purchase + conversation history), tester `beta.html`, admin tabs (Conversations, Beta config + audit) incl. the whole Beta-domain request/confirm flow

## 11. Git + independent Beta deployment

- [x] Real git repository initialized in the working copy (`main`)
- [x] `beta` branch created and independently deployable (separate workflow + separate Beta config)
- [x] Production checks run only on `main`/PRs; the Beta workflow only on `beta`, and it never falls back to the production Pages site
- [x] Remote added and both branches pushed: `https://github.com/Wiiffies/Aether-Website` (public)
- [x] GitHub Pages publishes `main` through its branch build, custom domain `get-aether.de` kept by the committed `CNAME`, artifact trimmed by `_config.yml`
- [ ] MANUAL: set the `beta` environment's `BETA_TARGET` + `BETA_DEPLOY_TOKEN`, then wire the transfer step in `deploy-beta.yml`

## 12. Remaining / blocked

- [ ] MANUAL: DNS record + worker route for the Beta hostname (`betatester.get-aether.de` + the long path)
- [ ] MANUAL: separate Beta Worker with its own D1/KV/secrets (data isolation) before real Beta testing — today isolation is configuration, not a hard boundary
- [ ] MANUAL: DNS record + route if the portal should live on its own `Customer.get-aether.de` hostname (today it is same-origin on get-aether.de)
- [ ] MANUAL: `PROMO_CODES`, `TURNSTILE_*`, `RESEND_API_KEY`, `ADMIN_EMAIL` dashboard values
- [x] MANUAL: explicit `ALLOWED_ORIGIN` set (2026-10-01); SSL mode cannot move to `Full (strict)` until the origin stops being GitHub Pages (see §8)
- [ ] MANUAL: second edge rate-limit rule for chat/Beta/admin (free plan allows one — the existing rule must not throttle `/api/ipn`)
