# Aether API — Cloudflare Worker

The whole backend of get-aether.de is this one Worker (`aether-api`, source `worker/src/index.js`,
routed at `get-aether.de/api/*` and `www.get-aether.de/api/*`). It serves the customer portal, the
private Beta, the admin panel, the checkout, the NOWPayments IPN and every email/Discord
notification. There is no other service and no other secret store.

Full endpoint list, database schema, variables and the exact deploy recipe live in
[`../MASTER_PROMPT.md`](../MASTER_PROMPT.md). This file is the short version for people working in
this directory.

## Bindings and secrets

| Binding | Type | Notes |
| --- | --- | --- |
| `DB` | D1 `aether-db` | users, sessions, tokens, rate limits, orders, messages, conversations, settings, audit log, beta feedback |
| `RESEND_API_KEY` | secret | outgoing mail via Resend — the fallback transport; when no transport exists every response reports `email:false` and the verification gate stands down honestly |
| `EMAIL` | binding (optional) | Cloudflare Email Sending, preferred over Resend: no secret at all. Attach with `{"type":"send_email","name":"EMAIL"}` in the script metadata once `get-aether.de` is onboarded in Email Service; `/api/health` then reports `emailProvider:"cloudflare"` |
| `NOWPAYMENTS_API_KEY` | secret | Payment API (never the Invoice API) |
| `NOWPAYMENTS_IPN_SECRET`, `NOWPAYMENTS_IPN_SECRET_2` | secret | both are checked against the `x-nowpayments-sig` HMAC (recursively sorted keys, compact JSON, HMAC-SHA512 — the vendor's algorithm; trailing whitespace is trimmed). The dashboard secret must match; `GET /api/admin/ipn` reports the last accepted/rejected callback without ever printing a secret |
| `DISCORD_WEBHOOK_URL` | secret | order/chat notifications |
| `TURNSTILE_SECRET` | secret | captcha on register/forgot/reset; **set 2026-10-01** — the site key in `public/aether-config.js` must be live before this exists |
| `CONTACT_TO`, `CONTACT_FROM`, `SUCCESS_URL`, `CANCEL_URL`, `SITE_URL` | vars | mail routing and link building |
| `ADMIN_EMAIL` / `ADMIN_EMAILS` | vars | the only source of admin rights — `admin` is **never** a database value |
| `ALLOWED_ORIGIN`, `REQUIRE_EMAIL_VERIFICATION`, `BETA_HOST`, `BETA_PATH`, `BETA_FLAGS`, `AETHER_ENV`, `RESET_DISCORD_EMAILS` | vars | see MASTER_PROMPT.md (`RESET_DISCORD_EMAILS` gates who may receive a reset link through the Discord webhook when no transport exists; default = admins) |
| `PROMO_CODES` | var (JSON) | the **only** source of promo codes — never in this repo (it is public). Ordinary codes are capped at 50% and always leave at least EUR 1; only `{"type":"percent","value":100,"test":true}` may reach EUR 0, and such an order is stored `status='free'` with no payment created |

**Never put a secret in `wrangler.toml`.** Secrets exist only as Worker secrets.

**No emoji in notifications.** Order, payment and chat mails and Discord embeds carry plain-text
status labels (`PAID`, `FAILED`, `PARTIALLY PAID`, `PENDING`) instead of colour emoji: they survive
every mail client, every Discord client and every screen reader, and a mail subject that renders as
a blank box in one client is worse than one that reads plainly. A single non-ASCII byte is also what
once turned every character in a Discord embed into `?` - see the ASCII rule below.

## Prices are server-side

`POST /api/invoice` never trusts the browser's `amount`. `FIXED_PRICES` owns the packaged tiers
(`discord_bot` BASIC 15 / PREMIUM 30; `website` STARTER 15 / BASIC 30 / FULLSTACK 149 /
ADVANCED 299); a quoted build must post `estimate` within `QUOTE_MIN`..`QUOTE_MAX` (50..5000) and
it must equal the posted `amount`, else `ESTIMATE_REQUIRED`/`ESTIMATE_MISMATCH`. `SOLD_TYPES` gates
the type list (`UNKNOWN_TYPE` otherwise), and the auth check runs **before** pricing so a signed-out
probe cannot read the catalog. The frontend builders send the pre-discount subtotal as `estimate`.

## Rules that must not be broken

- **ASCII only.** Write every non-ASCII character as `\uXXXX`; `../_build_chunks.mjs` aborts if it
  finds a byte above 127, and a single non-ASCII byte once turned the Discord embeds into `?`.
- **PBKDF2 ≤ 100 000 iterations** — workerd rejects higher counts with a bare `1101`.
- **Deploys must preserve bindings**: any upload includes
  `keep_bindings:["plain_text","secret_text","d1","kv_namespace"]` (or `wrangler deploy --keep-vars`).
- **Inboxes**: `questions@get-aether.de` for orders/questions, `business@get-aether.de` for business
  only. `CONTACT_FROM` stays `Aether <questions@get-aether.de>`. Never touch MX or Email Routing.
- **Authorisation stays server-side**: no route may trust a role, an owner id or a Purchase ID sent
  by the browser.

## Test and run locally

```bash
node ../_test-worker.mjs     # 410 checks against an in-memory D1 stub — run before every deploy
node ../_dev-server.mjs      # static site + this Worker on http://127.0.0.1:5501
#   DEV_FAKE_PAYMENTS=waiting|paid node ../_dev-server.mjs
#   ^ answers api.nowpayments.io locally with a stub payment (real per-coin floors; `paid` also
#     reports actually_paid + payin_hash) so the order page's address/copy/chain UI is testable
```

## Deploy

Two supported paths:

1. **CI (preferred once a token exists):** add `CLOUDFLARE_API_TOKEN` and `CLOUDFLARE_ACCOUNT_ID`
   as repository secrets; `.github/workflows/deploy-worker.yml` then runs the checks and deploys on
   every push that touches `worker/**`.
2. **KV relay (used until then):** the checksum-verified recipe in
   [`../MASTER_PROMPT.md`](../MASTER_PROMPT.md) §DEPLOY — build chunks, verify sha256, upload to
   both `aether-api` and `aether-payments`.

After deploying, always verify:

```bash
curl https://get-aether.de/api/health          # {"ok":true,...,"db":true}
curl -i https://get-aether.de/api/conversations # 401 for anonymous callers
```
