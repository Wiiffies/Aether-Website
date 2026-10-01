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
- [ ] MANUAL: set SSL/TLS mode to Full (strict)
- [ ] MANUAL: replace the guessed `ADMIN_EMAILS` dashboard var with `ADMIN_EMAIL=alex.real.apple@gmail.com`
- [ ] MANUAL: set `RESEND_API_KEY` so verification/reset emails really send

## 9. Frontend / UI

- [x] Customer portal: registration, login, dashboard, purchases (with Purchase IDs), chat history, settings
- [x] Verification state banner + resend button
- [x] Beta entry shown only to testers (server-provided), `beta.html` area with indicators
- [x] Admin UI: roles, verification status, conversations, Beta config
- [x] Shared styling for chat lists, badges, status pills, empty/loading/error states

## 10. Testing / audit

- [x] 177 automated checks (`node _test-worker.mjs`) incl. IDOR, privilege escalation, Beta gating
- [x] Secret scan (repo + `public/` contain no keys, tokens, or credentials)
- [x] Worker stays ASCII-only; `_build_chunks.mjs` ASCII guard passes
- [x] Live verification after deployment

## 11. Git + independent Beta deployment

- [x] Real git repository initialized in the working copy (`main`)
- [x] `beta` branch created and independently deployable (separate workflow + separate Beta config)
- [x] Production/Alpha deploy workflow only triggers from `main`; Beta workflow only from `beta`
- [ ] MANUAL: add the GitHub remote + push both branches (no remote exists in this checkout)
- [ ] MANUAL: point GitHub Pages at the repository and enable the Beta deployment target

## 12. Remaining / blocked

- [ ] MANUAL: DNS record + worker route for the Beta hostname
- [ ] MANUAL: separate Beta D1/KV (data isolation) before real Beta testing
- [ ] MANUAL: `PROMO_CODES`, `TURNSTILE_*`, `RESEND_API_KEY` dashboard values (still unset)
