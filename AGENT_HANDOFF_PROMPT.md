# AETHER — NEW AGENT HANDOFF PROMPT

> Copy everything between the two rulers into a brand-new agent session (new account, no chat history).
> The workspace is `C:/Users/wiiff/Documents/Aether CS2 Website` (a real git repo since 2026-10-01).
> Nothing here contains a secret. Secrets live only in Cloudflare Worker Secrets.

---

You are taking over **Aether** (get-aether.de) — a live, revenue-generating site. Do not scaffold a new project, do not "rewrite it properly", and do not assume a framework: it is plain HTML/CSS/JS on GitHub Pages plus one Cloudflare Worker and one D1 database. Inspect before you change anything, keep every existing convention, and stay inside this repo.

## 0. Do these four things first (do not skip)
1. `git status --short; git log --oneline -5; git branch -a; git remote -v` — expect branch `main`, a `beta` branch, `origin` = `https://github.com/Wiiffies/Aether-Website` (public, Pages publishes `main`) and a clean working tree.
2. Read, in this order: **`MASTER_PROMPT.md`** (authoritative living checklist — project map, pricing, full API list, schema, deploy recipe, update log), **`SECURITY.md`** (security model + the manual dashboard list), **`PROJECT_TODO.md`** (this workstream's status), **`BETA.md`** (Beta deployment plan), **`.env.example`**.
3. `node _test-worker.mjs` → must print `183 passed, 0 failed`. If it doesn't, stop and report before touching anything.
4. `curl https://get-aether.de/api/health` → must return `{"ok":true,"service":"aether-api",...,"db":true}` with `payments:true`, `ipnSignature:true`, `discord:true`, `email:false`, `turnstile:false`.

## 1. What the product is
- Sells **Discord bots** and **static websites**, source-code only. No hosting/backend/domain for customers.
- Static frontend on GitHub Pages at `get-aether.de`; the API is a Cloudflare Worker (`worker/src/index.js`, script names `aether-api` + `aether-payments`) routed at `get-aether.de/api/*` and `www.get-aether.de/api/*`, so the frontend uses `apiBase = ""` (same origin, first-party `__Host-aether_session` cookie). `https://api.get-aether.de` still answers for legacy/compat.
- Data: Cloudflare **D1 `aether-db`**, KV namespace used only for deploy staging + a version marker.
- Payments: NOWPayments **Payment API only** (never Invoice API), IPN at `https://api.get-aether.de/api/ipn`.
- Mail: Resend (**key still missing**) + Discord webhook.
- Admin: `admin.html`. Admin identity comes from the `ADMIN_EMAILS`/`ADMIN_EMAIL` variable — never from the database and never from the frontend.

## 2. Hard constraints (breaking any of these is a production incident)
- **Emails:** `questions@get-aether.de` = orders/questions, `business@get-aether.de` = business only. Never introduce hello@/orders@. **Never touch MX or Email Routing** (`route*.mx.cloudflare.net` must stay). `CONTACT_FROM` stays `Aether <questions@get-aether.de>`.
- **Secrets never leave Worker Secrets:** `RESEND_API_KEY`, `NOWPAYMENTS_API_KEY`, `NOWPAYMENTS_IPN_SECRET(_2)`, `DISCORD_WEBHOOK_URL`, `TURNSTILE_SECRET`. Never in HTML/JS/`wrangler.toml`/git/GitHub. `ADMIN_EMAIL` is config, not a credential.
- **The repo is public. Treat everything committed as world-readable forever.** No keys, tokens, credentials, private keys, database details, or customer data — ever.
- **`worker/src/index.js` must stay 100% ASCII** (write €, —, →, · as `\uXXXX`). `_build_chunks.mjs` aborts on any byte > 127, and a non-ASCII byte once turned every character in the Discord embed into `?`.
- **PBKDF2 in workerd rejects > 100000 iterations.** `PBKDF2_ITERATIONS = 100000` is the ceiling.
- D1 is only for: users, sessions, auth tokens, rate limits, orders, messages, conversations, settings, audit log, beta feedback. Nothing is readable without auth; admin routes need the admin email variable.
- **Never claim "100% secure".** Obscure URLs, hidden pages, frontend role checks, hidden buttons, minified/obfuscated JS, Base64 and client-side variables are **not** security. Say what is enforced in code, what is enforced at the Cloudflare edge, and what is still a manual step.
- **The Beta path is obscurity, never access control.** Never shorten `betatester.get-aether.de/yesthisistheofficaldomainanditssolongsopeopledontaccidentlyfindthis`. Everyone who knows the URL still needs a session, a verified email and the Tester role.
- Roles are server-side only: `users.role` is `user`|`tester`; `admin` is derived from config at request time. The API can never grant `admin`, never demote an admin, never let anyone change their own role. Frontend buttons are convenience only.
- **There is no anonymous chat.** Every `/api/conversations*` route requires a session and filters by `user_id`; another account's id returns **404** (not 403) so ids cannot be probed.
- **There is no guest checkout either.** `POST /api/invoice` requires a session — anyone signed out gets `401 {"code":"ACCOUNT_REQUIRED"}` and **no order row is written** — and the order always belongs to the session account (`user_id` from the session, account email wins over any posted email). Never reintroduce an email-only purchase path; `/api/order` is the free inquiry/quote form and never creates an order.
- **Purchase IDs (`AETH-2026-XXXXXXXX`) are identifiers, not credentials.** Never accept one from the browser as proof of anything; lookups only inside the authenticated account.
- Don't run destructive/irreversible commands (no `git push`, no D1 drops, no zone-wide settings changes) without explicit approval. Never run `git commit`/`push` unless asked.

## 3. Live state (verified 2026-10-01)
- Worker deployed from `worker/src/index.js`: **sha256 `5e139dde5031dc1d75ad0f5f0baf8cd655632aa1060f7f8948762be61cce48af`, 135 412 bytes, 0 non-ASCII, 22 gzip chunks** (only chunks 16–21 changed from the previous build `1a1c22b7…`, 135 027 bytes), uploaded to both `aether-api` and `aether-payments`. The KV staging chunks (`aether-gz-0..21`) are byte-identical to that build — verify by re-downloading and comparing the sha256 before any upload — so a future deploy only needs to re-upload the chunks whose checksum changed.
- Live D1 has all tables: `users`, `sessions`, `auth_tokens`, `rate_limits`, `orders`, `messages`, `conversations`, `settings`, `audit_log`, `beta_feedback` (+ unique index on `orders.purchase_id`). Counts: **users 1** (a genuine signup, `alex.real.apple@gmail.com`, unverified), everything else 0.
- Live Worker vars (updated 2026-10-01): plain `CONTACT_TO`, `CONTACT_FROM`, `SUCCESS_URL`, `CANCEL_URL`, **`ADMIN_EMAIL=alex.real.apple@gmail.com`**, `ADMIN_EMAILS=` (emptied — the guessed `Wispz@outlook.de` is gone), `ALLOWED_ORIGIN=https://get-aether.de,https://www.get-aether.de,https://api.get-aether.de`, `PROMO_CODES=` (empty). Secrets present: NOWPayments key + both IPN secrets + Discord webhook. Missing: `RESEND_API_KEY`, `TURNSTILE_SECRET`. D1 binding `DB` present. Compatibility `2024-12-01`, flag `nodejs_compat`. **How to change these safely:** re-send the whole binding list as multipart metadata `bindings` *plus* `keep_bindings:["secret_text"]` (a bare `PATCH /settings` is rejected — it demands multipart; and it is the mechanism that preserved all four secrets when tested on a throwaway probe Worker first).
- Edge routes: `get-aether.de/api/*` and `www.get-aether.de/api/*` → `aether-api`. WAF managed ruleset `77454fe2d30c4220b5701f6fdfb893ba` (entrypoint `d0c31ddfc1a94d17b965ec5749aeb18a`). One edge rate-limit ruleset `c1f44df272df4b5aa9139338a895ab15` (display name literally `aether-probe`): 10 req / 10 s per IP+colo on `/api/auth/*` + order/contact/invoice, block 10 s — the free plan allows exactly one rule, so chat/Beta/admin are protected by application limits only.
- Cloudflare IDs you will need: account `e83c68e5e26e3e9096542df702331096`, KV namespace `747e914c4ebf40c5b64d876090f1b467`, D1 `201052a6-bed3-4c1c-a3ee-e16394aa36e4` (WEUR, name `aether-db`), zone `74675dbf0eec6c0cba4b9674a9cd7431` (`get-aether.de`, **Free Website** plan), workers subdomain `wispz`.
- Git: remote `origin` = `https://github.com/Wiiffies/Aether-Website` (public); Pages builds the site from `main` (branch build, artifact trimmed by `_config.yml`). `main` carries the customer-portal work; `beta` adds `BETA_BRANCH.md` + `public/aether-config.js → betaDeployment: true`. The site's own history includes the pre-existing placeholder commits from 2026-09-22, which the portal work merged into rather than overwrote. Workflows: `deploy-production.yml` (main only), `deploy-beta.yml` (beta only, refuses other refs, uses the `beta` environment + `vars.BETA_TARGET`/`secrets.BETA_DEPLOY_TOKEN`), `deploy-worker.yml` (inert until `CLOUDFLARE_API_TOKEN` is a repo secret).

## 4. Architecture in one screen
- **Auth:** register/login/logout/me; PBKDF2-SHA256 100 000 iters; sessions stored as SHA-256(token), rotated on login, 14-day TTL, max 10/user; cookie `__Host-aether_session` or `Authorization: Bearer`; writes require a same-origin Origin/Referer header (CSRF guard); login/register/reset rate limited per IP and per account.
- **Email verification:** 24 h single-use hashed token on register; `POST /api/auth/resend-verification` is session-bound and capped at 4/h; unverified accounts get `403 {"code":"EMAIL_UNVERIFIED"}` on purchases, conversations and Beta. The gate is enforced server-side and **stands down honestly while no mail provider exists** (`email:false`), because a gate nobody can pass is a lockout. `REQUIRE_EMAIL_VERIFICATION=true` forces it on.
- **Purchase IDs:** `AETH-2026-XXXXXXXX` from `crypto.getRandomValues` with alphabet `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`, uniqueness-checked + unique index, never taken from the client, lazily backfilled, carried in emails/Discord/portal/admin.
- **Chat:** conversations `conv_<18 hex>` owned by `user_id`, statuses `open|answered|closed`, 4000-char cap with control chars stripped, optional link to one of *your own* Purchase IDs, legacy per-order chat rows bridged deterministically so no history is lost, admin replies land in the same thread and email the customer.
- **Tester/Beta:** `POST /api/admin/users/:id/role` grants/revokes `tester` (audited); `/api/beta/*` requires session + verified + tester; 120 s single-use ticket → redeem for a session; feedback in `beta_feedback`; flags `betaNewChat|betaDashboard|betaTools` are server-provided.
- **Beta hostname change:** `/api/admin/beta/domain/request` needs admin + verified + **password re-auth** → 64-hex 30-min single-use token (payload in `auth_tokens`), `/domain/confirm` re-checks still-admin, writes `settings`, logs to `audit_log`.
- **Frontend:** `account.html` (portal: purchases → chat → Beta → security), `beta.html` (server-gated tester area), `admin.html` (orders, accounts, direct messages, **conversations**, **Beta** incl. config/confirm/flags/audit), `public/checkout.js` exposes `portal.*`, `auth.*`, `admin.*`; assets carry `?v=6` (`beta.html` uses `?v=7`).

## 5. Local tooling you must use
- `node _dev-server.mjs` → http://127.0.0.1:5501 serves the static site **and** `/api/*` through the real Worker with an in-memory D1 (`node:sqlite`) and `ALLOW_DEV_ORIGIN=true`; prints a random local admin password at start. Never use `file://`, and plain `python -m http.server` no longer reaches the API (intentional).
  Kill it with: `netstat -ano | grep "127.0.0.1:5501" | grep -oE "[0-9]+$" | head -1` then `taskkill //PID <pid> //F`. The `LISTENING` column is localised (`ABHÖREN`) here — do not filter on it.
- `node _test-worker.mjs` → **183 checks** (IDOR, privilege escalation, Beta gating, Purchase IDs, verified-email gate, hardening suite). Run before every deploy.
- `node _check-inline-js.mjs *.html` → parses every inline `<script>` block.
- `node _check-email-regex.mjs` → actually runs every `isEmail` against addresses that must pass/fail (this is what caught an `isEmail` that rejected any address containing the letter `s`).
- `node _build_chunks.mjs` → ASCII guard + gzip/base64 chunk generator with per-chunk checksums (writes the gitignored `_gz2_NN.txt`).
- **Keep the schema in sync in three places:** live D1, `_test-worker.mjs`, `_dev-server.mjs`.

## 6. Deploy recipe (wrangler is NOT authenticated — use the Cloudflare MCP `execute` tool)
1. `node --check worker/src/index.js` → `node _test-worker.mjs` (183/0) → `node _build_chunks.mjs` (prints `srcBytes`, `nonAscii` = 0, `sha256`, `gzBytes`, `chunkCount`, and per-chunk `len/sum/head/tail`).
2. **Only re-upload the chunks that changed:** one `execute` call can `GET` all `aether-gz-0..21` values, compute `len` + Σ charCodeAt inside the sandbox and return just those numbers — compare with the fresh manifest (gzip keeps a byte-identical prefix, so the last deploy needed only 5 of 22). Otherwise upload chunk files one per call with `cloudflare.request({method:"PUT", path:`/accounts/${accountId}/storage/kv/namespaces/747e914c4ebf40c5b64d876090f1b467/values/aether-gz-${i}`, body:"<2000-char chunk>", contentType:"text/plain", rawBody:true})`.
   - **The `execute` `code` argument caps around ~3 KB.** One 2000-char chunk per call is reliable; packing four chunks into one call returns a misleading `10000: Authentication error`.
3. One final `execute` call: `GET` all chunks → `atob` → gunzip via `new Response(new Response(bytes).body.pipeThrough(new DecompressionStream("gzip")))` → **verify sha256 and abort unless it matches** → multipart `PUT` to BOTH `/accounts/${accountId}/workers/scripts/aether-api` and `.../aether-payments`.
   - Metadata must be `{main_module:"index.js", compatibility_date:"2024-12-01", compatibility_flags:["nodejs_compat"], keep_bindings:["plain_text","secret_text","d1","kv_namespace"]}` — `keep_bindings` is what preserves secrets + the D1 binding across a code-only upload.
   - The module part must be `Content-Disposition: form-data; name="index.js"; filename="index.js"` + `Content-Type: application/javascript+module`, or Cloudflare answers `10021: No such module: index.js`.
4. Wait ~1 minute, then verify: `/api/health` and an anonymous `GET /api/conversations` (must be 401).
5. D1 changes go through the same tool: `POST /accounts/${accountId}/d1/database/201052a6-bed3-4c1c-a3ee-e16394aa36e4/query` with `{sql, params}`. Additive `ALTER TABLE … ADD COLUMN` / `CREATE TABLE IF NOT EXISTS` is safe to repeat; update the two test/dev stubs in the same commit.

## 7. What is left to do (priority order)
1. ~~Replace the guessed admin email~~ **Done 2026-10-01:** `ADMIN_EMAIL=alex.real.apple@gmail.com`, `ADMIN_EMAILS` emptied, secrets verified intact. Reminder: the admin address is documented in this public repo, so never make an address you have not registered the only admin identity — register it first (the live D1 already has that account).
2. **`RESEND_API_KEY`** (+ Resend domain verification; never touch MX/Email Routing) → turns on real verification/reset mail and the verification gate.
3. **Repository — done and live.** `main` and `beta` are pushed to `Wiiffies/Aether-Website`, Pages publishes `main` through its **branch** build (the custom domain `get-aether.de` is kept by the committed `CNAME`) and the real site replaced the old "under construction" placeholder (verified live 2026-10-01). Remaining only: set the `beta` environment's `BETA_TARGET` + `BETA_DEPLOY_TOKEN` and wire the transfer step in `deploy-beta.yml`. Do not push unless asked.
4. **Beta infrastructure:** DNS record + route for `betatester.get-aether.de`, a Beta Worker with its **own** D1/KV/secrets publishing the long path, and set `BETA_HOST`/`BETA_PATH` if they should not rely on code defaults.
5. **`Customer.get-aether.de`**: DNS + route if the portal should live on its own hostname (today it is same-origin on get-aether.de).
6. (**`ALLOWED_ORIGIN` explicit** is done as of 2026-10-01. **Never set SSL/TLS to `Full (strict)` while GitHub Pages is the origin** — it was tried and instantly took the whole site down with HTTP 526, because GitHub Pages answers with `CN=*.github.io` for `get-aether.de`; the zone is `Full` again. See SECURITY.md §4.3.) Then: Turnstile widget + secret — this must be a two-step change, because `turnstileGuard` starts enforcing the moment `TURNSTILE_SECRET` exists, so the widget/site key has to be live in `public/aether-config.js` **first** or registration breaks for everyone; then `PROMO_CODES`; then consider a broader edge rate-limit rule (the free plan allows only one, and it must not throttle `/api/ipn`).
7. Optional hardening worth doing: strict CSP (pages still use inline scripts, so hashes/nonces first), 2FA or Cloudflare Access in front of `/api/admin/*`, auditing of read-only admin views.

## 8. How to verify you have not broken anything
```
node _test-worker.mjs                      # 183 passed, 0 failed
node _check-inline-js.mjs *.html           # all inline scripts parse
node _check-email-regex.mjs                # validators still behave
curl https://get-aether.de/api/health      # ok:true, db:true, payments:true
curl -i https://get-aether.de/api/conversations   # 401 anonymous (no anonymous chat)
curl -i https://get-aether.de/api/beta/access     # 401 anonymous (obscurity is not access)
git diff --stat                            # know exactly what you changed
```
Browser: `http://127.0.0.1:5501/account.html` (register → verify → buy → chat), `/beta.html` (tester only), `/admin.html` (orders, accounts, conversations, Beta config, audit).
Also re-run the secret scan before any push: `git ls-files -z | xargs -0 grep -nIE "(re_[A-Za-z0-9]{20,}|sk-[A-Za-z0-9]{20,}|-----BEGIN [A-Z ]*PRIVATE KEY)"` — `.env.example` placeholders are the only acceptable matches.

## 9. Reporting rules for this project
- Report in numbered sections and tag every control **[code]**, **[edge]** or **[MANUAL]**.
- Never write "100% secure", "impossible to bypass" or "unhackable". State what is enforced, what is only obscurity, and what depends on a manual step.
- Never commit, push, or deploy anything destructive without being asked. Say what you changed, where it is verified, and what remains.

---
