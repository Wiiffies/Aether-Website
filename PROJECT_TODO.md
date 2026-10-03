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
- [x] Reset works without a mail provider (2026-10-01): delivery chain email -> Discord ops channel -> operator log, with the Discord hop allowlisted to `RESET_DISCORD_EMAILS` (default: admins) so no customer link lands in a shared channel; identical `200 {ok,delivery,message}` for known and unknown accounts
- [x] Mail layer is provider-agnostic (2026-10-01): Cloudflare Email Sending binding (`env.EMAIL`) preferred, Resend fallback, log mock last; `emailProvider` reported by `/api/health`; the verified-email gate stays tied to `RESEND_API_KEY`/`REQUIRE_EMAIL_VERIFICATION` so a half-enabled binding can never lock buyers out

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

- [x] **Worker deployed from CI (2026-10-02)** — `CLOUDFLARE_API_TOKEN` exists, `deploy-worker.yml` uploads `worker/src/index.js` (169 580 bytes, `166a1264…`), deployment `a4665481-…` / version `d4d59a60`; `/api/program` answers 403 instead of 404, and `wrangler.toml` is guarded so a deploy cannot blank `ADMIN_EMAIL` or add routes
- [x] Server-side roles; `admin` cannot be granted through the API (configuration only)
- [x] Admin can grant/revoke Tester; every change is audited
- [x] Beta API (`/api/beta/*`) enforces session + verified email + Tester role
- [x] Beta access state is server-provided; ordinary users get `allowed: false` and no URL
- [x] Server-side feature flags (`betaNewChat`, `betaDashboard`, `betaTools`)
- [x] Single-use 120s beta ticket -> redeem for a session on another Beta hostname
- [x] Beta feedback stored in `beta_feedback` (never production tables)
- [x] Obscure Beta path treated as obscurity only, never as security
- [x] Tester gate names its refusal (2026-10-02): `SESSION_REQUIRED` / `EMAIL_UNVERIFIED` / `NOT_TESTER`, role checked before the address so "Tester access is already on this account" is only said to an account that has it
- [x] A role grant answers with `warnings[]` (advisory, never a refusal) when the account still has to confirm its address before the Beta and the build unlock
- [x] Tester pages answer each refusal with the one instruction that fits: `beta.html` and `program.html` both explain the unconfirmed-address case instead of a generic "testers only"
- [x] Tester account surface: Beta link in the nav, the server-reported Beta address written before the card is revealed, a copy control, and a direct link to the build from `account.html` and `beta.html`
- [x] Admin panel: Tester count, an All/Testers-only filter, an accurate `colspan`, and no delete button for an account the Worker would refuse to delete

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

- [x] 209 automated checks (`node _test-worker.mjs`) incl. IDOR, privilege escalation, Beta gating, the reset delivery chain (admin allowlist, no enumeration, the logged link used end to end) and all three mail transports
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
- [ ] MANUAL: `PROMO_CODES` (paste the JSON in the dashboard — the promo engine is implemented and deployed as of 2026-10-02; until it is set every code returns `{valid:false}`. Keep `TEST100` in the dashboard only, never in the repo: the sample the tests use is `TESTFULL`) and **real email** and **real email** — `ADMIN_EMAIL` and `TURNSTILE_*` are done as of 2026-10-01. Mail has two paths and needs no code change: Cloudflare Email Sending (dashboard -> Email Service -> Email Sending -> Onboard Domain -> `get-aether.de`, then attach the `EMAIL` binding; free for the account's verified destinations) or `RESEND_API_KEY` (Worker secret + GitHub secret; the workflow syncs it)
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

## 14. Tester download + maintenance mode (2026-10-01, evening)

- [x] `program.html` announces the desktop app; every value that has to be *true* (version,
      platform, size, checksum) is served by the Worker, never written into the page or into
      `public/aether-config.js`, which ships to every browser
- [x] Download is server-mediated: the Worker holds the build's location and streams the bytes, so
      the browser never learns where the file lives and nothing can be linked to it
- [x] The single-use ticket travels as a **path-narrowed `aether_dl` cookie**
      (`Path=/api/program/file`, `SameSite=Strict`, `HttpOnly`, `Secure`, 120 s) — never in a URL, so
      there is nothing in the address bar, in history, in a shared link or in a request log
- [x] The ticket is bound to the session that minted it and the file request must arrive with that
      same session (mismatch → `403 WRONG_SESSION`), so a copied cookie is worthless on its own
- [x] The mint refuses with `400 COOKIE_REQUIRED` when the browser cannot carry the ticket — a dead
      ticket would be worse than an honest refusal
- [x] `PROGRAM_URL` must be `https://`; plaintext is treated as "not configured" (the only exception
      is loopback while `ALLOW_DEV_ORIGIN` is on, i.e. the local dev server)
- [x] The Worker hashes the artifact against `PROGRAM_SHA256` **before sending a byte**: a mismatch
      answers `409 CHECKSUM_MISMATCH`, delivers nothing and writes `program.download.mismatch` to the
      audit log. Above `PROGRAM_VERIFY_MAX_BYTES` (default 32 MB) the file is served but labelled
      `x-content-verified: unverified` instead of pretending
- [x] Response carries `x-content-verified`, `x-checksum-sha256`, `content-digest` (RFC 9530) and a
      real `content-length`, plus `no-store`, `noindex` and `attachment`
- [x] 264 checks in `_test-worker.mjs` (40 for the download), `_check-program.mjs` wired into all
      three workflows and mutation-tested (3 injected regressions caught)
- [x] Proven against the local Worker end to end: mint → cookie → download, **byte-identical** to the
      published artifact with the checksum verified, replay `400`, wrong checksum `409` with zero
      bytes delivered, and both outcomes written to the audit log
- [x] `_dev-server.mjs` fixed twice over for this: it decoded API responses as text (mangling any
      binary body) and never sent `content-length`, so the verified path could not be exercised
      locally. It also translates the `__Host-` session cookie name, which browsers refuse over
      plain http even on 127.0.0.1
- [x] Site put behind `maintenance.html` with `/api/*`, `/admin.html`, `/beta.html` and
      `/maintenance.html` exempt (live rule enabled 2026-10-01; see `maintenance/README.md`)
- [ ] MANUAL: `CLOUDFLARE_API_TOKEN` — until it exists **none of the Worker work above is live** and
      `/api/program` still answers 404 in production. Worked through step by step in `LAUNCH_BRIEF.md`
- [ ] MANUAL: host the build privately, set `PROGRAM_*` on the Worker, then grant a Tester role
- [ ] MANUAL: turn the maintenance rule back off and purge the cache when the work is done

## 15. Security hardening pass (2026-10-01, late evening)

- [x] **The pages finally have security headers.** A Cloudflare Response Header Transform rule sets
      a Content-Security-Policy plus `X-Frame-Options: DENY`, `nosniff`,
      `strict-origin-when-cross-origin`, a `Permissions-Policy` and
      `Cross-Origin-Opener-Policy: same-origin` on every page and asset, excluding `/api/*` where the
      Worker keeps its own stricter policy. Source of truth: `_security-headers.mjs`; the dev server
      serves the identical values so the policy can be tested before it is live
- [x] `_check-headers.mjs` (in all three workflows) fails the build if a page loads an origin the
      policy does not allow, uses `eval()`/`new Function()`, loads a `data:`/`blob:` script, or if
      anyone weakens the policy — 5 injected regressions, 5 caught
- [x] DMARC added (`p=none`, reporting to `questions@get-aether.de`); SPF and DKIM already existed.
      The ordering rule for tightening it (sender SPF/DKIM first, then quarantine, then reject) is
      written down in `SECURITY.md` §3b and `LAUNCH_BRIEF.md` §5
- [x] DNSSEC enabled on the Cloudflare side (`pending` the registrar step; the exact DS record is in
      `LAUNCH_BRIEF.md` §5 and `SECURITY.md` §3b)
- [x] Download kill switch (`PROGRAM_DISABLED=true`) that stops every download instantly, including a
      ticket authorised a second earlier, without touching `PROGRAM_URL`
- [x] A per-account cap on ticket minting (10/h) on top of the per-IP cap — one account can no longer
      loop the mint and write token and audit rows
- [x] 268 Worker checks (was 264)
- [x] Maintenance now has **two modes**: `Maintenance (SOFT)` keeps the account area, chats, password
      recovery and payment results open; `Maintenance (HARD)` closes everything public. SOFT is on
- [x] **Fixed a real outage-grade bug found while verifying:** `/public/*` was being redirected too, so
      `/admin.html` and `/beta.html` loaded **without any of their scripts** (the browser refused an
      HTML page served as a script). Assets, `/robots.txt` and the notice are excluded in both modes
- [x] The notice page was reworked: the "down for" clock survives a refresh (it used to reset to zero
      on every load), the countdown runs on a fixed clock grid instead of restarting at 30 s, the page
      reloads itself every 5 minutes and returns to the site on its own the moment it is up, and it
      links to the account area only when the server really serves it
- [x] `maintenance` branch rebuilt as a single root commit with the new notice (`c2f435a`, 6 files,
      `index.html` byte-identical to `maintenance.html`)
- [ ] MANUAL: publish the DNSSEC DS record at the registrar (see `LAUNCH_BRIEF.md` §5)
- [ ] MANUAL: move DMARC to `p=quarantine` once every sender's SPF/DKIM is in place

## 16. Checkout UI, transactional mail, admin checkout exemption (2026-10-02, late)
- [x] **"I can't order with my account" was two separate bugs, and neither was the admin role.**
      (a) `public/checkout.js` only asked `/api/me` when `sessionStorage` held a token. The session
      cookie is HttpOnly and `sessionStorage` is per tab, so signing in and then opening the shop in a
      new tab (or reopening the browser) showed "An account is required to buy" while the server knew
      exactly who was signed in. The modal and `bindAccountFields` now always ask the server, which
      answers from the cache when it has one.
- [x] (b) The admin row in D1 was unverified, so `verifiedGate()` refused checkout. It now exempts an
      `ADMIN_EMAILS` account: that account is configured server-side and signs in with a password, so a
      confirmation mail adds nothing to it, and locking the operator out of his own checkout while
      testing is a bug rather than a safeguard. Sensitive admin actions still use `adminVerifiedGate()`.
- [x] An unconfirmed address is a fixable state now: the modal shows "Confirm your email to finish
      checkout" with a *Resend confirmation email* button and names the address, and the Worker's
      `403 EMAIL_UNVERIFIED` opens that same panel (it used to print a sentence with no next step).
- [x] The pay modal was rebuilt: rounded cards, a brand chip, "Total due", short labels, and a result
      state that **replaces the form** instead of dropping a panel under the coin buttons and the promo
      box. It also sets `white-space:normal` when rendering rich panels — a template literal's
      indentation was rendering as large blank gaps inside the free-order panel.
- [x] Every transactional mail shares one designed shell (brand header, eyebrow, lead line, hairline
      rows, a white CTA button with the plain link kept underneath, a titled footer). Verification,
      password reset and the Beta-domain confirmation pass an optional `cta:{label,url}` to
      `orderHtml()`; the text alternative carries the link as well.
- [x] The desktop app is no longer advertised publicly while the project is private / in testing: the
      `Program` nav entry was removed from all 18 pages and `program.html` is `noindex,nofollow`. It
      stays reachable from `account.html` and `beta.html` for testers, and the tester-download guards
      (`_check-program.mjs`) are untouched and green.
- [x] `checkout.js` `?v=10` → `?v=11` on all 13 pages that load it.
- [x] 334 Worker checks (was 321: 13 new for the mail shell and the checkout gate)
- [ ] Consider re-linking `program.html` in the nav once the project is public and a build exists.
- [x] The pay modal no longer puts the wrong answer on screen: while the session check is in flight
      it shows a neutral "Checking your session…" (button disabled) instead of "Sign in to buy", and a
      single miss is not final — the sign-in card shows immediately and two quiet retries upgrade the
      modal the moment the server recognizes the session. `?v=11` → `?v=12`.
- [x] **Deploys are visible immediately now.** GitHub Pages serves HTML with `Cache-Control:
      max-age=600`, so a browser could keep the pre-deploy page — and its old `checkout.js` — for ten
      minutes, which is what made a correct fix look broken. A Cloudflare **Response Header Transform
      Rule** sets `Cache-Control: no-cache` on `/` and `*.html`, so the HTML always revalidates (304
      off `last-modified`) while the versioned assets keep caching. Same mechanism as the earlier
      `checkout.js?v=6` incident, now closed for good.

## 17. Chat, account status, payments, admin (2026-10-03)

- [x] **Site-wide support chat.** `public/chat.js` (standalone, loaded by every public page) adds a
      bottom-right launcher and a panel that lists *that account's* conversations with an honest
      badge (`Waiting for a reply` / `We replied` / `Resolved`), opens/continues a thread, starts a
      new one, and reports unread threads on the launcher while minimised. Open/closed plus the open
      conversation survive a reload (`localStorage`), the panel's own state survives navigation, and
      the thread polls every 12 s. Anonymous visitors get a sign-in prompt instead of an empty box.
- [x] `POST /api/conversations` and every action on a thread run through `accountGate()` — so a
      suspended or restricted account cannot start or continue a chat, and its refusal names the
      reason. Read access asks for the weaker gate (a restricted account can still read its history).
- [x] **Admin → Customer chats**: server-side search (`?q=`, matches subject, customer email,
      Purchase/Order ID, conversation ID **and message bodies**), status filter, limit (max 500),
      reply, close/resolve — all behind `requireAdmin` + `adminVerifiedGate`, with no cross-user
      exposure in the query (proved by a check that a normal account gets `403` for search *and* list).
- [x] **Account states** (`active` / `restricted` / `suspended` / `banned`) in `users.status`,
      managed from the admin Accounts tab: Reinstate / Restrict / Suspend (1–8760 h) / Ban, each with
      a reason that the customer sees verbatim in their portal banner and in the API refusal. Bans and
      suspensions revoke every session immediately (`revokeSessions`); a login attempt answers `403`
      with the reason and `accountStatus`. Admins cannot act on their own row or on an `ADMIN_EMAILS`
      account, and every change is audited (`account.<status>`).
- [x] **IP restrictions**: `GET|POST|DELETE /api/admin/blocked-ips` (admin-verified) with a single
      IPv4/IPv6 per entry, a note, and a D1-backed list in `settings("blocked_ips")`; the router
      refuses `/api/*` and `/admin*` from a blocked address with `403 IP_BLOCKED` before any handler
      runs, and the admin UI has a **Blocked networks** card. Sign-in IPs are now recorded
      (`users.last_ip`, `last_ip_at`, `signup_ip`) and shown in the Accounts table — no more wondering
      which address an account uses.
- [x] **The red "temporary problem" error explained and fixed at the source.** NOWPayments refuses
      payments below a pair-specific minimum (≈ €2 for the coin pairs on offer; `TEST100`'s €0.02 test
      card was under it). `createNowPaymentsPayment` now reads `/v1/min-amount` **before** creating
      anything (6 h cache), and refuses with a sentence that names the real minimum and says nothing
      was charged; if the vendor still refuses, the order is marked `failed`
      (`UPDATE … WHERE order_id=? AND status IN ('pending','waiting')`), the customer gets a readable
      sentence, and the provider detail only reaches `console.error`. Failure no longer leaves a
      completed-looking order, and the frontend keeps the server's own wording for any coded error
      instead of overwriting it with "temporary problem".
- [x] **An order page with no guessable URL as authorization.** `order.html` resolves
      `?purchase_id=` / `?order=` / `?payment_id=` through `GET /api/purchases/:id` and
      `GET /api/payment/:id`, both owner-scoped server-side (another account gets `404`, a tampered or
      malformed ID gets `400`), and shows item, amount, status, dates, pay-address with copy, and the
      links back to the account. `GET /api/payment*` now requires a session (an anonymous caller can
      no longer poll anybody's payment by ID); the local poller inside checkout was updated to send
      the session.
- [x] **Account dashboard orders section**: Order | Item | Amount | Payment | Date | Open, an honest
      `paymentState()` label (`Paid`, `Free (promo)`, `Payment failed`, `Partially paid`, `Waiting for
      payment`, `Payment open`, `No payment yet`), and a per-row **Open ↗** into `order.html`, so a
      failed or unpaid order can always be reopened and retried.
- [x] **No lightning icon** in the pay modal (the chip was removed, and nothing replaced it).
- [x] **Privacy pass**: `/api/me` returns the sanitised `publicUser()` shape instead of the raw row;
      no provider payload, secret or customer PII is echoed into any response (checked); payment
      status and purchase lookups are session-scoped; the chat keeps its token in `sessionStorage`,
      never a URL.
- [x] English consistency verified: every page is `lang="en"` and no German UI string remains
      (`COMMON_PASSWORDS` entries excepted).
- [x] **The live payment failure, diagnosed from the provider's own answer (2026-10-03).** The
      customer sentence was honest but still hid the cause, so **Workers Logs were enabled for
      `aether-api`** (they had never been on, so nothing was ever stored) and the next attempt
      logged `400 code AMOUNT_MINIMAL_ERROR "Crypto amount 2.7e-7 is less than minimal"` — a valid
      key and a genuinely below-minimum amount. Two assumptions were ours and wrong: the vendor says
      *"less than minimal"* while the classifier matched only *"less than the min"*, and the
      pre-flight asked the pair **backwards** (`eur -> coin`; the vendor pays coin -> fiat), whose
      refusal the fail-open swallowed, so the doomed payment was created anyway. Fixed: the pair
      points the vendor's way, only the fiat figure is trusted (a coin amount compared against a EUR
      price would refuse payable orders), an absurd figure is ignored, every lookup failure is
      logged instead of silently returning `0`, and the classifier reads the vendor's `code` too —
      so a below-minimum refusal now answers `400 AMOUNT_BELOW_MINIMUM` with one sentence naming the
      real figure.
- [x] **401 Worker checks** (was 388: 7 for the live payment diagnosis, one of which reproduces the
      production failure verbatim, plus 6 for the two payable test items), `node --check` clean,
      0 non-ASCII bytes in the Worker, and all 8 guard scripts green (`_check-assets` now sees
      20 pages incl. `chat.js?v=1` and `checkout.js?v=13`).
- [x] Live D1 got the additive migration (`users` status/`status_reason`/`status_until`/
      `status_updated_at`/`last_ip`/`last_ip_at`/`signup_ip`) and it was verified via
      `pragma_table_info`; the columns are backward-compatible, so the old Worker keeps working.
- [x] **Committed and pushed (2026-10-03):** `54ed9f744169062846a2802fee9541bd20c634b0` (`54ed9f7`),
      29 files. `Deploy aether-api worker` run `37115075023` green → code version
      `09cf52bf-b98f-42b9-8038-d8012006ca60`, current deployment
      `1bb010e2-adf3-4099-a9fa-280014730a86` / version `0b713672-5c64-4ec3-b850-91479f053b25`;
      `Production checks` `37115075070` and `pages-build-deployment` `37115074647` green;
      `/api/health` 200 with every flag true; the new build proven live by `/api/payment/1` → 401 and
      the two new admin routes → 403.
- [x] **Maintenance switched off (2026-10-03):** the zone was running the **HARD** rule, so every
      public page (including the new `order.html`) redirected to the notice. Ruleset
      `81830ab5c40740d5a71a5909da775079` is now at version 7 with **both** rules `enabled:false` and
      their expressions byte-identical; `/`, `/shop.html`, `/account.html`, `/order.html`,
      `/payment-success.html`, `/public/chat.js` and `/api/health` all answer 200.
- [x] **The provider minimum is per coin and small — measured live 2026-10-03: €0.04 for LTC.** The
      refusal now names the exact figure for the chosen coin (the pre-flight asks `/v1/min-amount`
      with the coin as `currency_from`). Real orders (€15+) are nowhere near any floor.
- [x] **Two payable test items at €0.04 — one Discord bot, one website (2026-10-03).** Repriced from
      €0.02 (which sat a cent below the LTC floor and could only ever demonstrate the refusal) to
      €0.04, which sits exactly on it, and split so both product families run through the real
      checkout: `test_purchase_bot` / `test_purchase_website`, server-priced and packaged, with the
      old `test_purchase` name kept as an alias so a checkout already open cannot break. An LTC run
      creates a real payment; BTC and ETH are refused with their own higher figure named.
- [ ] MANUAL: run an **LTC** test on either card to prove the payment path end to end (a BTC/ETH run
      demonstrates the refusal with the exact figure), use `TEST100` for a free order, and remove both
      cards + both types + the promo code before launch.
- [x] **The order page took over from the pay modal, and the chain became checkable and copyable
      (2026-10-03, not yet pushed).** A created payment (and a free order) hands off to
      `order.html?purchase_id=…` automatically — verified in a browser on the local stack — so the
      buyer lands on the page built for following an order. There the address is itself a copy
      button (Enter/Space work, Enter/Space and the Copy address / **Copy amount** buttons report
      through a polite status line, and the fallback selects the exact value that failed to copy).
      **Check the blockchain now** disables itself, says "Checking…", asks the provider, then reports
      `Last checked <time>`; the page also shows **Received so far** and the **transaction hash**
      (`actuallyPaid` / `payinHash`, new on the owner-only payment endpoint whose response stays a
      closed list the tests assert key by key). The hash is copy-only — `_check-headers.mjs` forbids
      third-party URL literals, so an explorer link would be the first origin to poke through the
      deployed CSP. Two bugs were found by looking at the rendered page: the facts table kept the
      stale `waiting` status next to a finished payment, and the clipboard fallback always selected
      the address. `checkout.js` `?v=14` on 14 pages. Tests: **403 checks, 0 failed (2 new)**.
- [x] **Frontend + backend is priced out loud as the expensive kind of job (2026-10-03, not yet
      pushed): shopping copy pass on `shop.html`, `website.html` and `discord-bot.html` —
      accounts, payments and stored data take considerably more time and cost considerably more than
      a static site, quoted individually, with hosting and the domain paid separately to the
      provider the customer picks.
- [x] **A local payment-provider stub, so the payment UI is testable without crypto (2026-10-03):**
      `DEV_FAKE_PAYMENTS=waiting|paid node _dev-server.mjs` answers `api.nowpayments.io` locally with
      the real per-coin floors (EUR 0.04 clears LTC; BTC/ETH are refused with their figure), a
      pending payment carrying an address, or a finished one carrying `actually_paid` and a
      `payin_hash`. Opt-in — a normal run is untouched.
- [ ] MANUAL: re-run the **LTC** test card on the live site after this deploy and confirm the order
      page's copy buttons and "Check the blockchain now" behave the same against the real provider.

## 18. Full audit pass — pricing, legal, privacy, UX (2026-10-03, not yet pushed)

- [x] **The browser can no longer name the price.** `handleInvoice` used to trust the posted
      `amount`; the Worker now owns every price. `FIXED_PRICES`: `discord_bot` BASIC 15 /
      PREMIUM 30, `website` STARTER 15 / BASIC 30 / FULLSTACK 149 / ADVANCED 299 — a posted amount
      is ignored for those. A quoted build must post `estimate` inside `QUOTE_MIN`..`QUOTE_MAX`
      (50..5000) **and** `amount` must equal it, else `400 ESTIMATE_REQUIRED` / `400
      ESTIMATE_MISMATCH`; `pkg` is normalised case-insensitively; `SOLD_TYPES =
      ["discord_bot","website","custom","contact"]`. The auth check runs **before** pricing, so a
      signed-out probe learns nothing about the catalog. The three builders send the pre-discount
      subtotal as `estimate`.
- [x] **The temporary test purchases are gone** — `shop.html` cards and the worker branch removed;
      `test_purchase*` answers `400 UNKNOWN_TYPE` with no order row, and the tests assert the
      refusal rather than the pricing.
- [x] **Legal pages exist:** `terms.html`, `privacy.html`, `cookies.html`, `imprint.html` (no
      invented company/DPO data; missing manual details flagged), extensionless aliases
      (`terms/`, `privacy/`, `cookies/`, `imprint/index.html` — canonical + noindex + redirect),
      a site-wide footer legal nav with a Cookie settings link, and `public/consent.js` +
      `public/legal.css` (first-visit notice, accept/reject/customise recorded in `localStorage`;
      no analytics, advertising or tracking exists to switch off, and the policy says so).
- [x] **No-refund policy (operator's decision, 2026-10-03):** Terms §6 is "No refunds" — all sales
      are final, an order cannot be cancelled once confirmed and paid (custom work made to
      specification; the EU withdrawal right does not apply, § 312g(2) No. 1 BGB), and the pay
      modal says so with a link to §6 before payment. The one thing that cannot be excluded stays
      honest: if **we** cancel work we cannot deliver, the payment for the undelivered part is
      returned, and statutory rights for non-delivery or defects are untouched.
- [x] **IPN + notification hygiene:** the customer receipt fires once per purchase (transition into
      `paid`), rejected signatures log only sizes/flags, and `safeIpnExtras()` allowlists what may
      be logged so vendor payloads/customer PII never reach mail or Discord.
- [x] **Sanitisation:** `cleanMessage()` strips control characters, normalises CRLF and caps at 4000
      chars server-side; `chat.js` linkifies only safe schemes and renders through `esc()`.
- [x] **Tests: 410 checks, 0 failed (7 new)** — price tampering at a fixed tier, the estimate rules
      (missing / mismatched / under the floor), the minimum-refusal figure, and the removed types.
      All 8 guards green; `worker/src/index.js` sha256
      `0fc4419728b6fa303836e515de89de06441037c669bfab689b1c8e881677a8ed`, 214 765 bytes (LF), 0
      non-ASCII.
- [ ] MANUAL: fill in the imprint's real operator details (name, address, contact) before launch;
      the page ships with honest placeholders flagged in this repo.
- [ ] Not implemented (audit findings, deliberate scope): images in the support chat (CSP already
      allows `img-src 'self' data:`), and a terms-acceptance checkbox at registration.
