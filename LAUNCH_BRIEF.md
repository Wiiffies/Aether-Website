# Launch brief — what only you can do

Everything in this file is an **external action**: a dashboard click, a token, a key, a URL that
lives on your account somewhere. No agent can do these for you, and nothing else in the project is
blocked on them.

The API (`aether-api`) is **live with the current Worker code** as of 2026-10-02 — including the
tester-only download, the account-recovery hardening and the refusal codes. The site is
**in maintenance mode right now, in SOFT mode**: the shop and every marketing page 302
to the notice, while `/account.html` (orders, invoices and **all chat threads**), the password
recovery pages, the payment-result pages, `/admin.html`, `/beta.html`, `/public/*` and `/api/*` all
stay open. Section 7 below is how you switch to HARD, or reopen the site entirely.

Legend: `[ ]` = to do, `[x]` = done and verified.

---

## 0. The Worker is deployed (done 2026-10-02)

- [x] **`CLOUDFLARE_API_TOKEN` exists and `deploy-worker.yml` deployed `aether-api`** — the Worker
      code is **live** (latest: deployment `27774d20-…`, version `10cb9483`, 2026-10-02 19:26 UTC,
      from `worker/src/index.js` at 174 069 bytes /
      `39e3f2965a7b16534215f1106ca3adf950a9e353ddbdb714102f042337f81a2c`; an earlier build the same
      day was `a4665481-…` / `166a1264…`).
      `/api/program` went from 404 to 403 `SESSION_REQUIRED`, and `GET /api/ipn` now answers
      405 + `Allow: POST` instead of a misleading 404 — both are the new build answering.

  **Two traps this step taught us** (both now handled, both worth knowing):

  1. **A token belongs to the account you were signed into when you created it.** A token from
     *another* Cloudflare account authenticates fine and then fails every call with
     `Authentication error [code: 10000]`. Aether lives in **"Wispz@outlook.de's Account"**
     (`e83c68e5e26e3e9096542df702331096`) — the zone, the D1 database and all three Workers.
  2. **The secret name has to match exactly**, and GitHub secrets cannot be renamed or read back.
     Yours landed as `CLOUDFLARE_API_TOKE` (one character short) and `deploy-worker.yml` accepts
     that name as a documented fallback, printing a warning on every run. To tidy it up: create
     `CLOUDFLARE_API_TOKEN` with the same value, delete `CLOUDFLARE_API_TOKE`, then drop the two
     `_TYPO` lines from `.github/workflows/deploy-worker.yml`.

  Permissions, if it ever needs recreating: **Workers Scripts : Edit**, **Workers KV Storage : Edit**,
  **D1 : Edit** — all Account-scoped, Account resources **Include → Wispz@outlook.de's Account**.
  Then: **Actions → "Deploy aether-api worker" → Run workflow → environment `aether-api`**.

  ```bash
  curl -s https://get-aether.de/api/health
  # expect: {"ok":true,...,"email":false,...}
  curl -s -o /dev/null -w '%{http_code}\n' https://get-aether.de/api/program
  # expect: 403  (anonymous is refused by the Worker) - a 404 means CI did not deploy
  ```

  > The deploy is **code only**: `worker/wrangler.toml` holds `keep_vars = true`, declares only the two
  > routes the zone actually has, and never declares an empty variable, so it cannot blank the live
  > `ADMIN_EMAIL` or re-route traffic. `_check-deploy-config.mjs` fails the workflow if any of that
  > changes, and the run after the deploy confirmed the admin address and both routes survived.

## 1. Email that actually sends (needed for verification + reset links)

- [ ] Pick **one** of the two paths (no code change either way):

  - **Cloudflare Email Sending** (native, no key to rotate): dashboard → your zone → **Email →
    Email Routing / Email Sending → Onboard domain `get-aether.de`**, then attach the `EMAIL`
    binding to the Worker (`aether-api` → Settings → Bindings → add a **Send Email** binding named
    `EMAIL`). It was returning `10203` (disabled at account level) the last time it was tried.
  - **Resend**: create an API key, verify `get-aether.de` as a sending domain in Resend, then
    `printf '%s' 're_xxx' | npx wrangler secret put RESEND_API_KEY --name aether-api`.
    Optionally add it as the GitHub secret `RESEND_API_KEY` too, and the workflow keeps both in sync.

  Verify: `curl -s https://get-aether.de/api/health` → `"email":true` and
  `"emailProvider":"cloudflare"` or `"resend"`, and `"resetDelivery":"email"`.

  > While `email:false`, password resets are delivered through the Discord ops channel to
  > admin addresses only (`resetDelivery:"discord"`) — the recovery flow works, it just is not a
  > customer-facing email yet. Nothing is broken; it is simply not finished.

## 2. Publish a build people can download

The download now exists server-side (`/api/program` answers, `/api/program/file` refuses an anonymous
caller with `400 TICKET_REQUIRED`), so the **only** thing missing is the build itself: until the
variables below are set, a Tester sees the honest "No build has been published yet" card.

- [ ] Host the build somewhere **private** (a Cloudflare R2 bucket with no public bucket policy and
      no presigned URL, a private GitHub release asset, or any host whose URL you would not mind
      leaking — but R2/release is the point: the URL is the secret the Worker keeps).

- [ ] Set the Worker variables (Worker → `aether-api` → **Settings → Variables and Secrets**):

  | Name | Value | Notes |
  | --- | --- | --- |
  | `PROGRAM_URL` | `https://…/aether-desktop-0.1.0.exe` | **must be `https://`**; plain `http://` is refused outright |
  | `PROGRAM_VERSION` | `0.1.0-beta.1` | without this the page says "no build published yet" |
  | `PROGRAM_PLATFORM` | `Windows 10/11 (x64)` | shown to the tester |
  | `PROGRAM_SIZE` | `48 MB` | free text, shown as-is |
  | `PROGRAM_SHA256` | 64 hex characters | the Worker verifies the file against it before sending |
  | `PROGRAM_NAME` | `Aether Desktop` | optional |
  | `PROGRAM_NOTES` | one line | optional |
  | `PROGRAM_VERIFY_MAX_BYTES` | e.g. `33554432` (32 MB) | optional; above this the file is served but labelled `unverified` |

  How to get the checksum: `certutil -hashfile aether-desktop-0.1.0.exe SHA256` on Windows, or
  `sha256sum` / `shasum -a 256` elsewhere. It **must** be lowercase hex and exactly 64 characters —
  anything else is treated as "no checksum published" on purpose.

- [ ] Give a tester the role: sign in at `https://get-aether.de/admin.html` (typed, not linked
      anywhere) → **Grant Tester**. The tester then opens `/program.html` and sees the build.

  Verify the whole chain yourself first (anonymous is refused, the file is never linked):

  ```bash
  curl -s -o /dev/null -w '%{http_code}\n' https://get-aether.de/api/program           # 403
  curl -s -o /dev/null -w '%{http_code}\n' https://get-aether.de/api/program/file      # 400, never the file
  ```

## 3. Donations (one line of code, no deploy)

- [ ] Create the NOWPayments donation link, then put it in `public/aether-config.js`:
      `donateUrl: "https://nowpayments.io/donation/…"`. Until then `/donate.html` honestly says
      "Donation destination not configured yet" instead of inventing an address.

## 4. The Beta link (it does not exist yet)

- [ ] `curl https://betatester.get-aether.de/` currently fails to connect: there is **no DNS record,
      no route and no deployment** for it. `/beta.html` on the main site does work (it is exempt from
      maintenance mode), but the separate host is still unbuilt — see `PROJECT_TODO.md` §12 and §11.

## 5. Domain-level hardening (done for you, one step left)

- [x] **SPF + DKIM** were already in place (Cloudflare Email Routing).
- [x] **DMARC now exists**: `_dmarc` → `v=DMARC1; p=none; rua=mailto:questions@get-aether.de; fo=1`.
      `p=none` blocks nothing; it starts the report flow so you can see who is sending as your
      domain. You will get aggregate reports at `questions@get-aether.de` (they are XML — skim them
      or filter them into a folder).
- [x] **DNSSEC is enabled on Cloudflare** and is `pending` — the last step is at your registrar.
- [ ] **Add the DS record at your registrar** to finish DNSSEC. Copy it exactly:

  ```
  get-aether.de. 3600 IN DS 2371 13 2 031AAD25DC73AEB6B8889DD733B7F4807785B40C9DBD04C188408438C5CFB6ED
  ```

  Registrar fields, if it asks for them separately: key tag `2371`, algorithm `13`
  (ECDSAP256SHA256), digest type `2` (SHA-256), digest
  `031AAD25DC73AEB6B8889DD733B7F4807785B40C9DBD04C188408438C5CFB6ED`.
  Verify at <https://dnssec-analyzer.verisignlabs.com/get-aether.de>.
  WARNING: A **wrong** DS record makes the domain unresolvable for validating resolvers. If the site stops
  resolving right after you add it, remove the DS record first and look it up again.

- [ ] **Tighten DMARC later, in this order:** add the sending provider's SPF include **and** its DKIM
      record → check the reports → change the policy to `p=quarantine` → then `p=reject`. Doing that
      before the sending records exist would fail DMARC alignment on your own verification and
      password-reset mail.

## 5b. If you ever add a CDN, font or analytics script

The pages now ship a Content-Security-Policy, which means an origin that is not on the allowlist is
**blocked in the browser**. Adding one therefore takes two edits: the policy in
`_security-headers.mjs` **and** the same value in the Cloudflare rule
(`Rules → Overview → Aether response security headers`). `node _check-headers.mjs` fails the build if
the page and the policy disagree, so you will find out before your visitors do.

## 5c. Platform settings already applied (do not undo these by hand)

Every one of these was a real defect, not a preference. If a support article or an assistant tells
you to change one of them back, the "why" is in this table.

| Setting | Now | Why |
| --- | --- | --- |
| Email Obfuscation (zone → Scrape Shield) | **Off** | it rewrote `questions@get-aether.de` into a link that only renders if a Cloudflare script runs, and injected `/cdn-cgi/scripts/…` into every page — so the file served no longer matched the file in the repo |
| Cache level | **Standard** (`basic`) | `Aggressive` cached HTML, not just assets, so an edit could sit behind the old file |
| Browser Cache TTL | **0 = respect origin** (10 min) | `14400` (4 h) is why your own changes "did not show up" for hours |
| Development Mode | **Off** | it auto-expires anyway; caching is now correct without it |
| HSTS | **On**, 1 year, includeSubDomains, nosniff | HTTPS-only for the whole domain. `preload` is deliberately **off** — it is hard to reverse |
| Response headers rule | **On** | CSP, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, `Permissions-Policy`, `COOP` on every page (and matching headers in the Worker for JSON) |
| Edge rate limit | **On** | 10 requests / 10 s per IP on `/api/auth/*` and the form endpoints, blocked for 10 s. This works **today**, independent of the Worker deploy |

## 6. Optional, but it is the next real security step

- [ ] Move the static site off GitHub Pages (Cloudflare Pages or a Worker with static assets). It is
      the prerequisite for **SSL/TLS `Full (strict)`**: with Pages as the origin it is impossible
      (GitHub answers with a `*.github.io` certificate for `get-aether.de`, so strict validation
      fails with a 526 — this is why the zone sits at `Full`).
- [ ] Second edge rate-limit rule for `/api/program*` + `/api/conversations*` (the free plan allows
      one rule, and the existing one must not throttle `/api/ipn`).
- [ ] **Minimum TLS version 1.3** (zone → SSL/TLS → Edge Certificates). Deliberately left at `1.2`:
      a customer on an older Android WebView can still pay today, and dropping them to gain a cipher
      is a bad trade for a shop. Do this only if your own analytics say nobody is on TLS 1.2.
- [ ] **Remove `'unsafe-inline'` from `script-src`.** The honest state: it is there because every
      page carries small inline scripts and GitHub Pages has no build step to add nonces. Closing it
      means generating per-page hashes into `_security-headers.mjs` and emitting one Cloudflare rule
      per page — real work, and a mistake there breaks the page silently. The exposure is small: no
      page renders user-supplied HTML, so there is no injection point for an inline script.

## 7. The two maintenance modes, and reopening the site

Right now the **SOFT** rule is on: the shop and the marketing pages redirect to the notice, while
your **account, orders, chats, password recovery and payment results stay open**, as do the admin
panel and `/beta.html`. The notice itself says so and links to your account — it probes
`/account.html` first, so it never offers a link that does not work.

| Rule (Cloudflare → zone → **Rules → Overview**) | Meaning |
| --- | --- |
| `Maintenance (SOFT)…` | shop closed, customer area open — **currently ON** |
| `Maintenance (HARD)…` | everything public closed, only admin/beta/assets/API reachable |

- [ ] Switch between them (or turn both off) in that list. Nothing is deleted. Cloudflare can take
      up to a minute to apply a rule change.
- [ ] After **any** switch: **Caching → Configuration → Purge Everything** — a cached page can
      otherwise keep sending people to the notice (and browsers hold pages for up to 4 hours).

  ```bash
  # SOFT healthy state: these six answer 302 (closed) …
  for u in / /shop.html /program.html /donate.html /contact.html /about.html; do
    printf '%-18s ' "$u"; curl -s -o /dev/null -w '%{http_code}\n' "https://get-aether.de${u}?cb=$RANDOM"
  done
  # … and these ten answer 200 (open). The two that matter most are the last two.
  for u in /account.html /forgot-password.html /reset-password.html /verify-email.html \
           /payment-success.html /admin.html /beta.html /maintenance.html /public/checkout.js /api/health; do
    printf '%-26s ' "$u"; curl -s -o /dev/null -w '%{http_code}\n' "https://get-aether.de${u}?cb=$RANDOM"
  done
  ```

  `/public/checkout.js` returning `200` with a JavaScript content type is the one people forget: if
  the assets get redirected, every open page loads **without its scripts** while still looking fine.

  Details, live IDs and the two-switch explanation: `maintenance/README.md`.

---

## 8. The prompt to paste into ChatGPT

Copy everything inside the block. It is written to be pasted cold into a new ChatGPT chat, so it
carries its own context and tells the assistant to ask you for the values it cannot know. It was
refreshed on 2026-10-02, after the Worker went live, so it no longer asks anyone to deploy the
Worker from scratch — it starts from what is actually true today.

```text
You are helping me finish the production setup for my website. I am not a deep
infrastructure person: give me exact dashboard clicks and exact commands in order,
one step at a time, and tell me what output to expect after each step so I can tell
whether it worked. Ask me for any value you need instead of inventing one. Do not
tell me to change code without telling me which file and why.

THE PROJECT
- Static website (plain HTML/CSS/JS, no framework) hosted on GitHub Pages, custom
  domain get-aether.de. Repo: Wiiffies/Aether-Website, branch main, published
  straight from the branch.
- A Cloudflare Worker named "aether-api" serves the API at get-aether.de/api/* (and
  www.get-aether.de/api/*). It is an ES-module Worker with a D1 database binding
  named DB (database "aether-db", id 201052a6-bed3-4c1c-a3ee-e16394aa36e4).
- Cloudflare account id: e83c68e5e26e3e9096542df702331096
- Cloudflare zone get-aether.de id: 74675dbf0eec6c0cba4b9674a9cd7431
- Payments: NOWPayments. Sign-in: email + password, sessions in a __Host- cookie,
  Turnstile on signup. Email sending: not configured yet.
- The Worker is deployed by GitHub Actions
  (.github/workflows/deploy-worker.yml) and it IS live as of 2026-10-02 12:55 UTC. Proof:
  https://get-aether.de/api/health returns a JSON object with "ok":true, and
  https://get-aether.de/api/program returns 403 for an anonymous request (it used to
  return 404 on the older build). The repository secret is currently misspelled
  CLOUDFLARE_API_TOKE and the workflow carries a temporary fallback for that; item 3 below
  fixes the spelling.
- Watch out for the account switcher: I have a second, unrelated Cloudflare account. My
  first API token was created in it and every call failed with "Authentication error
  [code: 10000]" until I recreated the token in the account id above. If a dashboard screen
  does not match that id, that is almost certainly why.
- The zone is already hardened and the site is in SOFT maintenance mode: HSTS (1 year,
  includeSubDomains, nosniff), a Content-Security-Policy plus X-Frame-Options,
  X-Content-Type-Options, Referrer-Policy, Permissions-Policy and COOP applied to every
  page by a Cloudflare response-header rule, Email Obfuscation OFF on purpose (it was
  rewriting my support email into a JavaScript-only link), cache level Standard and
  browser cache TTL 0 (= the 10 minutes the origin sends), and an edge rate-limit rule
  that blocks 10 requests per 10 seconds per IP on /api/auth/* and the form endpoints.
  Do not flip any of those without telling me why first.
- Maintenance mode is two Cloudflare redirect rules (SOFT = customer area stays open,
  HARD = everything public closed). SOFT is the one that is on.

WHAT I WANT TO FINISH, IN THIS ORDER
1. Make email sending work for account verification and password reset. Two options:
   Cloudflare Email Sending (attach a Send Email binding named EMAIL to the Worker)
   or Resend (secret RESEND_API_KEY). Tell me which one you recommend for a small
   project on Cloudflare's free tier and walk me through it. Success = /api/health
   reports "email":true and "resetDelivery":"email". Right now resets are only
   delivered over Discord, so tell me who can and cannot recover an account until
   this is on, and whether turning it on blocks anyone from signing in.
2. Publish a desktop-app beta build for a private tester group: host the file
   somewhere private (an R2 bucket with no public access is fine), then set these
   Worker variables: PROGRAM_URL (must be https), PROGRAM_VERSION, PROGRAM_PLATFORM,
   PROGRAM_SIZE, PROGRAM_SHA256 (64 lowercase hex characters, the real checksum of
   that exact file), and optionally PROGRAM_NAME and PROGRAM_NOTES. Success = signing
   in as an account with the Tester role at https://get-aether.de/program.html shows
   the build and the download works, and an anonymous request to /api/program still
   gets 403. Tell me how to compute that checksum on Windows.
3. Fix the misspelled GitHub secret: recreate my Cloudflare API token in the correct
   Cloudflare account (the id above) with Workers Scripts:Edit, D1:Edit and
   account-scoped permissions, save it in GitHub as CLOUDFLARE_API_TOKEN, delete the
   old CLOUDFLARE_API_TOKE, and prove the "Deploy aether-api worker" workflow still
   runs green afterwards.
4. Set up a NOWPayments donation link and put the URL into donateUrl in
   public/aether-config.js so the donate page stops saying "not configured yet".
5. Sanity-check that a real purchase completes end to end. The callback URL
   https://api.get-aether.de/api/ipn is already correct: that hostname is a Worker
   custom domain on aether-api, so it reaches the Worker even though wrangler.toml
   only declares the get-aether.de/api/* and www.get-aether.de/api/* routes. POST
   /api/ipn is live and rejects a bad signature with 401; GET now answers 405 with
   allow: POST instead of a misleading 404. The one thing I cannot check for myself:
   the IPN secret in the NOWPayments dashboard must equal the Worker secret
   NOWPAYMENTS_IPN_SECRET (GitHub holds no copy, so the CI sync never overwrites it).
   Walk me through one careful test purchase on the cheapest item, and show me how to
   read GET /api/admin/ipn afterwards to confirm the callback verified
   (lastAccepted.signed = true) or to diagnose a mismatch (lastRejected).
6. Finish DNSSEC. Cloudflare already has it enabled and reports this DS record for my
   domain:
   get-aether.de. 3600 IN DS 2371 13 2 031AAD25DC73AEB6B8889DD733B7F4807785B40C9DBD04C188408438C5CFB6ED
   Walk me through publishing it at my registrar (I will tell you which one), what to
   paste in each field (key tag 2371, algorithm 13, digest type 2, and the digest), how to
   verify it, and what to do immediately if the site stops resolving after I add it.
7. Explain my DMARC setup: _dmarc currently reads
   "v=DMARC1; p=none; rua=mailto:questions@get-aether.de; fo=1". Tell me how to read the
   reports it sends to questions@get-aether.de, and give me the exact order of steps to
   move it to p=quarantine and then p=reject without breaking my own password-reset and
   verification emails.
8. Show me how to switch between the two maintenance rules on Cloudflare (zone -> Rules
   -> Overview -> "Maintenance (SOFT)..." and "Maintenance (HARD)..."), how to turn both
   off, and how to purge the cache afterwards so nobody keeps seeing the notice. Then tell
   me when it is safe to turn maintenance off, given everything above.

HOW I WANT TO WORK
- One step at a time. Wait for me to paste the result before moving on.
- If a step needs a value only I can see (a checksum, a key, a URL), ask for it.
- If something fails, give me the most likely cause first, not a list of ten.
- Never invent an id, a URL or a checksum. If you are unsure, say so and tell me how
  to find the real value.
- Keep answers short. I will paste errors back to you verbatim.
```
