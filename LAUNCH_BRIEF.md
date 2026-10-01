# Launch brief — what only you can do

Everything in this file is an **external action**: a dashboard click, a token, a key, a URL that
lives on your account somewhere. No agent can do these for you, and nothing else in the project is
blocked on them.

The site is **in maintenance mode right now** (only `/admin.html`, `/beta.html`, `/maintenance.html`
and `/api/*` answer; everything else 302s to the notice). Section 6 below is how you reopen it.

Legend: `[ ]` = to do, `[x]` = done and verified.

---

## 0. The one that unblocks the most

- [ ] **Create the `CLOUDFLARE_API_TOKEN` secret in GitHub** — this is the only reason the Worker
      code from the last session is not live yet. Until it exists, `deploy-worker.yml` runs every
      check and then **skips the deploy**.

  1. Cloudflare dashboard → **My Profile → API Tokens → Create Token → Create Custom Token**.
  2. Name it `aether-github-actions`.
  3. Permissions — exactly these three, all **Account** scoped, all **Edit**:
     - `Workers Scripts : Edit`
     - `Workers KV Storage : Edit`
     - `D1 : Edit`
     - Account resources: **Include → your account** (`Wispz@outlook.de`).
       (Leave Zone resources empty — these are account-level permissions.)
  4. Create, copy the token **once** (it is never shown again).
  5. GitHub → repo `Wiiffies/Aether-Website` → **Settings → Secrets and variables → Actions →
     New repository secret** → name `CLOUDFLARE_API_TOKEN` → paste → Add.
  6. Then: **Actions → "Deploy aether-api worker" → Run workflow → environment `aether-api`**.

  Verify it worked:

  ```bash
  curl -s https://get-aether.de/api/health
  # expect: {"ok":true,...,"email":false,...}
  curl -s -o /dev/null -w '%{http_code}\n' https://get-aether.de/api/program
  # expect: 403  (anonymous is refused by the Worker)
  # a 404 means the Worker is still the old build
  ```

  > If the token creation page refuses or errors with `9109 Unauthorized`, that is the account's
  > API-token policy, not something in this repo — try creating the token from a browser profile
  > that is signed in as the account owner, or use the alternative: run the deploy from your own
  > machine with `npx wrangler deploy --keep-vars` inside `worker/` after `npx wrangler login`.

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

## 5. Optional, but it is the next real security step

- [ ] Move the static site off GitHub Pages (Cloudflare Pages or a Worker with static assets). It is
      the prerequisite for **SSL/TLS `Full (strict)`**: with Pages as the origin it is impossible
      (GitHub answers with a `*.github.io` certificate for `get-aether.de`, so strict validation
      fails with a 526 — this is why the zone sits at `Full`).
- [ ] Second edge rate-limit rule for `/api/program*` + `/api/conversations*` (the free plan allows
      one rule, and the existing one must not throttle `/api/ipn`).

## 6. Reopening the site when you are done

- [ ] Cloudflare dashboard → zone `get-aether.de` → **Rules → Overview** → switch
      **`Maintenance mode (site-wide, admin + beta exempt)`** off. Nothing is deleted; re-enabling
      is instant. Then purge: **Caching → Configuration → Purge Everything** (browsers can hold a
      page for up to 4 hours — that is `browser_cache_ttl: 14400`).

  ```bash
  # healthy = 4 × 200 and everything else 302
  for u in / /shop.html /program.html /account.html /maintenance.html /admin.html /beta.html /api/health; do
    printf '%-18s ' "$u"; curl -s -o /dev/null -w '%{http_code}\n' "https://get-aether.de${u}?cb=$RANDOM"
  done
  ```

  Details, live IDs and the two-switch explanation: `maintenance/README.md`.

---

## 7. The prompt to paste into ChatGPT

Copy everything inside the block. It is written to be pasted cold into a new ChatGPT chat, so it
carries its own context and tells the assistant to ask you for the values it cannot know.

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
- The Worker is deployed by GitHub Actions (.github/workflows/deploy-worker.yml),
  which needs the repository secret CLOUDFLARE_API_TOKEN. That secret does not exist
  yet, so the deploy step is skipped and the Worker is still running an older build.

WHAT I WANT TO FINISH, IN THIS ORDER
1. Create the CLOUDFLARE_API_TOKEN (Workers Scripts:Edit, Workers KV Storage:Edit,
   D1:Edit, account-scoped) and add it as the GitHub repo secret, then run the
   "Deploy aether-api worker" workflow and prove the new code is live by checking
   https://get-aether.de/api/health (expect a JSON object with "ok":true) and
   https://get-aether.de/api/program (expect HTTP 403 for an anonymous request; a
   404 means the old build is still live).
2. Make email sending work for account verification and password reset. Two options:
   Cloudflare Email Sending (attach a Send Email binding named EMAIL to the Worker)
   or Resend (secret RESEND_API_KEY). Tell me which one you recommend for a small
   project on Cloudflare's free tier and walk me through it. Success = /api/health
   reports "email":true and "resetDelivery":"email".
3. Publish a desktop-app beta build for a private tester group: host the file
   somewhere private (an R2 bucket with no public access is fine), then set these
   Worker variables: PROGRAM_URL (must be https), PROGRAM_VERSION, PROGRAM_PLATFORM,
   PROGRAM_SIZE, PROGRAM_SHA256 (64 lowercase hex characters, the real checksum of
   the file), PROGRAM_NAME. Success = signing in as an account with the Tester role
   at https://get-aether.de/program.html shows the build and the download works, and
   an anonymous request to /api/program still gets 403.
4. Set up a NOWPayments donation link and put the URL into donateUrl in
   public/aether-config.js.
5. Tell me how to turn maintenance mode off again when everything above works
   (Cloudflare zone → Rules → Overview → the rule named "Maintenance mode
   (site-wide, admin + beta exempt)") and how to purge the cache afterwards.

HOW I WANT TO WORK
- One step at a time. Wait for me to paste the result before moving on.
- If a step needs a value only I can see (a checksum, a key, a URL), ask for it.
- If something fails, give me the most likely cause first, not a list of ten.
- Never invent an id, a URL or a checksum. If you are unsure, say so and tell me how
  to find the real value.
- Keep answers short. I will paste errors back to you verbatim.
```
