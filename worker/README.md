# Aether — Cloudflare Worker + NOWPayments

Real email delivery + Discord + crypto checkout. `mailto:` is fallback only.

## Endpoints

| Route | Purpose |
|---|---|
| `POST /api/order` | Custom builders + contact form → email + Discord + customer auto-reply |
| `POST /api/invoice` | Tier / builder Pay with Crypto → pending email + Discord, creates NOWPayments invoice, returns `invoice_url` |
| `POST /api/ipn` | NOWPayments HMAC-verified callback → PAID/FAILED email + Discord |
| `GET /api/health` | Shows which secrets/vars are configured (no values leaked) |

Frontend hits `https://api.get-aether.de/api/*` (via `public/aether-config.js → apiBase`).

## Inboxes — DO NOT change

`questions@get-aether.de` — normal orders + questions
`business@get-aether.de` — business opportunities only

Both are **Cloudflare Email Routing** forwards — the Worker never touches MX.
Outgoing mail (Resend) only sends *from* a verified address — `questions@get-aether.de`.

## 1) Resend — outgoing email

1. Resend → API Keys → create key.
2. Domains → Add `get-aether.de` → add the DNS records Resend shows you in **Cloudflare DNS**:
   - Typically 1–2 `TXT` for SPF/DKIM + optionally an `MX` for Resend inbound (do NOT delete the 3 `route*.mx.cloudflare.net` MX records — Email Routing stays).
3. Domain = Verified → you can send from `Aether <questions@get-aether.de>` (set in `wrangler.toml → CONTACT_FROM`).

If Resend still shows `Domain not verified`, check `TXT` propagation (`dig txt get-aether.de` and `dig txt send.get-aether.de` etc. — exact hostnames are shown in Resend).

## 2) NOWPayments — crypto

1. nowpayments.io → **Settings → API Keys** → Generate key.
2. **Settings → IPN Settings** → generate **IPN Secret**, set callback to `https://api.get-aether.de/api/ipn`.
3. Payment settings → enable BTC, LTC, ETH, XMR (minimum).

## 3) Deploy

```bash
cd worker
npm install
npx wrangler login
npx wrangler secret put RESEND_API_KEY
npx wrangler secret put NOWPAYMENTS_API_KEY
npx wrangler secret put NOWPAYMENTS_IPN_SECRET
npx wrangler secret put DISCORD_WEBHOOK_URL
npx wrangler deploy
curl https://api.get-aether.de/api/health
# {"ok":true,"resend":true,"nowpayments":true,"ipnSecret":true,"discord":true,...}
```

`api.get-aether.de` is already bound as a Worker custom domain (`AAAA 100::` proxied) — `wrangler deploy` attaches to it via `routes` in `wrangler.toml`.

## 4) Vars (wrangler.toml)

```
CONTACT_TO   = "questions@get-aether.de,business@get-aether.de"  # used as defaults; Worker routes per-type
CONTACT_FROM = "Aether <questions@get-aether.de>"
ALLOWED_ORIGIN = "https://get-aether.de,https://www.get-aether.de,https://wiiffies.github.io"
SUCCESS_URL  = "https://get-aether.de/payment-success.html"
CANCEL_URL   = "https://get-aether.de/payment-cancel.html"
# NOWPAYMENTS_IPN_URL = "https://api.get-aether.de/api/ipn"  # optional override
```

## 5) Frontend

`public/aether-config.js`:

```js
window.AETHER_CONFIG = { apiBase: "https://api.get-aether.de", currency: "eur" };
```

Empty `apiBase` → forms fall back to `mailto:questions@get-aether.de`.

## 6) Test locally

```bash
npx wrangler dev  # http://localhost:8787

curl -X POST http://localhost:8787/api/order -H "content-type: application/json" -d '{
  "type":"discord_bot","package":"Custom","amount":55,
  "discord":"test_user","email":"you@example.com",
  "description":"Test bot please","meta":{"language":"Python","commands":12}
}'

curl -X POST http://localhost:8787/api/invoice -H "content-type: application/json" -d '{
  "amount":15,"currency":"eur","type":"discord_bot","package":"BASIC",
  "discord":"test_user","email":"you@example.com","description":"Aether BASIC bot — test"
}'
```

## Troubleshooting

- `503 NOWPayments not configured` → missing `NOWPAYMENTS_API_KEY`.
- Resend `403` → `CONTACT_FROM` domain not verified in Resend.
- `401 Bad signature` → IPN secret mismatch.
- No Discord ping → missing `DISCORD_WEBHOOK_URL`; Worker still returns 200.
