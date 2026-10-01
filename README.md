# Maintenance mode

> **State on 2026-10-01: the SOFT rule is ON.** The shop, the marketing pages and every bookmark
> redirect to the notice, while the account area, the password-recovery pages, payment results, the
> admin panel, `/beta.html`, `/public/*` and `/api/*` all stay open. To close the site to everyone,
> switch to the HARD rule; to reopen, switch both off.

There are **two switches**, and either one is enough. They are independent on purpose: the
Cloudflare rule is the fast one (seconds), the Pages branch is the one that works even if the
Cloudflare zone is ever changed by someone else.

---

## Switch A — Cloudflare rule (fastest, recommended)

The page it serves is `maintenance.html` on `main`, which is live at
<https://get-aether.de/maintenance.html>. That file is the source of truth.

There are **two rules, and exactly one of them should be on** — the ruleset evaluates top to bottom,
so leaving both enabled is the same as leaving the HARD one on:

| Rule (by description) | What a visitor gets | Use it when |
| --- | --- | --- |
| `Maintenance (SOFT)…` | shop + marketing pages closed; **account, chats, password recovery and payment results stay open** | you are shipping and customers only need to be kept out of the shop |
| `Maintenance (HARD)…` | everything public closed, only admin/beta/assets/API reachable | the database or the API itself must not be touched |

- **Turn on:** Cloudflare dashboard → your zone `get-aether.de` → **Rules → Overview** → switch the
  rule you want on and the other one off.
- **Turn off:** switch both off. Nothing is deleted, so re-enabling is instant.

Live identifiers, so you never have to go hunting for the rules:

| Thing | Value |
| --- | --- |
| zone | `74675dbf0eec6c0cba4b9674a9cd7431` |
| ruleset (phase `http_request_dynamic_redirect`) | `81830ab5c40740d5a71a5909da775079` |
| rule | find them by their descriptions — the whole ruleset is replaced on edit, so rule ids are reissued every time |

Both rules match `get-aether.de` and `www.get-aether.de` only, and return a **302**, not a 301, on
purpose: a permanent redirect gets cached hard by browsers and would keep sending people to the
notice after the site is back. Cloudflare can take up to about a minute to pick up a rule change,
so do not judge the switch by the first second.

### What stays reachable while the site is closed

The expressions are deliberately narrow, because a maintenance page that also takes the site's own
assets, support or payments down with it costs more than the outage it announces:

| Path | SOFT | HARD | Why |
| --- | --- | --- | --- |
| `/api/*` | open | open | the Worker, checkout, the payment IPN callback, `/api/beta/*` and the password-reset API keep working |
| `/public/*` | open | open | **CSS, JS and images: without this the admin and beta pages load with no scripts at all** (the browser refuses an HTML page served as a script) |
| `/admin.html` | open | open | the operator needs the panel; it is linked from nowhere public, so it is reached by typing the URL |
| `/beta.html` | open | open | testers keep their area |
| `/maintenance.html` | open | open | without this exclusion the rule redirects to itself forever |
| `/robots.txt` | open | open | crawlers read it first; a redirect here reads as "robots.txt unavailable" |
| `/account.html` | **open** | redirected | the account, orders, invoices and every chat thread |
| `/forgot-password.html`, `/reset-password.html`, `/verify-email.html` | **open** | redirected | a reset link already sitting in somebody's inbox has to keep working |
| `/payment-success.html`, `/payment-cancel.html` | **open** | redirected | someone who already paid must be able to see what happened |
| everything else | `302` → notice | `302` → notice | `/`, `/shop.html`, `/program.html`, `/donate.html`, every bookmark |

The Beta hostname (`betatester.get-aether.de`) is a different host and is not matched by either rule
at all — it is never affected by maintenance mode.

### What the notice page does on its own

`maintenance.html` is self-contained (no fonts, no images, no API calls) and does four things:

1. **A clock that survives a refresh.** "Down for" is stored per browser, so reloading no longer
   starts it at zero again, and it keeps counting while a visitor waits.
2. **A real countdown.** The next check is a point on the clock, not "30 seconds after this page
   happened to load", so refreshing does not reset it to a full interval.
3. **It reloads itself and returns on its own.** Every 30 seconds it asks the origin whether the
   site is back and navigates there the moment it is; every 5 minutes it reloads itself so an
   edited notice (or a switch from SOFT to HARD) is actually seen.
4. **It tells the truth about the account area.** It probes `/account.html` once and shows
   "Your account is still open" with a working link only when the server really serves that page —
   under the HARD rule the probe lands back on the notice and the offer is never made.

## Switch B — GitHub Pages branch

> This switch is **incompatible with the exclusions above**: the branch publishes only the notice,
> so `/admin.html` and `/beta.html` would 404 while it is on. Use it when the site should be closed
> to everyone including you, and use Switch A when only visitors should be stopped.

- **Turn on:** repo **Settings → Pages → Build and deployment → Deploy from a branch** →
  branch **`maintenance`**, folder **`/ (root)`** → Save.
- **Turn off:** set it back to branch **`main`**, folder **`/ (root)`**.

The `maintenance` branch deliberately contains **only** this kit, so *every* URL shows the
maintenance page: `/` is served by `index.html`, and anything else (`/shop.html`, `/account.html`,
a stale bookmark) falls through to `404.html`, which sends the visitor to `/`.

`CNAME` **must stay on this branch**. GitHub Pages reads the custom domain from the branch it is
serving, and deleting it drops `get-aether.de` from the Pages configuration.

---

## Caching is the thing that makes a switch look broken

Two layers sit in front of the origin and both are eager:

| Layer | Setting today | Effect |
| --- | --- | --- |
| Cloudflare edge | `cache_level: aggressive` | caches HTML, not just assets |
| Browsers | `browser_cache_ttl: 14400` | a visitor can hold a page for **4 hours** |

So after a switch, always do one of these, or people will keep seeing the old site:

- **Development Mode** (zone → Caching → Development Mode → On) — bypasses the edge cache and
  **expires by itself after about 3 hours**. This is the safe option while testing.
- **Purge Everything** (zone → Caching → Configuration → Purge Everything) — clears the edge,
  but a browser that already has the page may still show it until its own TTL runs out.

Verify with a cache-busting request, which always reaches the origin:

```bash
curl -s -o /dev/null -w '%{http_code}\n' "https://get-aether.de/?cb=$(date +%s)"
```

### Checking the switch itself

```bash
# SOFT (current): closed = 302, open = 200.
for u in / /shop.html /program.html /donate.html \
         /account.html /forgot-password.html /reset-password.html /verify-email.html \
         /payment-success.html /payment-cancel.html \
         /admin.html /beta.html /maintenance.html /robots.txt \
         /public/aether.css /public/checkout.js /api/health; do
  printf '%-26s ' "$u"
  curl -s -o /dev/null -w '%{http_code}\n' "https://get-aether.de${u}?cb=$RANDOM"
done
```

A healthy SOFT run is six `302`s and ten `200`s. Two of those lines matter more than the rest:
`/api/health` returning `200` proves the switch did not take the Worker, checkout or the payment IPN
down with the site, and `/public/checkout.js` returning `200` with a JavaScript content type proves
the pages that stay open can still load their own scripts.

---

## Rebuilding the branch

If `maintenance.html` changes on `main`, the branch has to be rebuilt with it. From a clean
`main` working tree (this writes no files into your checkout):

```bash
IDX="$PWD/.git/maintenance-index"; rm -f "$IDX"
b() { git hash-object -w --stdin; }

GIT_INDEX_FILE="$IDX" git read-tree --empty
GIT_INDEX_FILE="$IDX" git update-index --add --cacheinfo 100644,$(b < maintenance.html),index.html
for f in 404.html robots.txt CNAME .nojekyll README.md; do
  GIT_INDEX_FILE="$IDX" git update-index --add --cacheinfo 100644,$(b < "maintenance/$f"),"$f"
done

TREE=$(GIT_INDEX_FILE="$IDX" git write-tree)
COMMIT=$(git commit-tree "$TREE" -m "Maintenance page for get-aether.de")
git push --force origin "$COMMIT:refs/heads/maintenance"
rm -f "$IDX"
```

The branch is intentionally a single root commit with no history: it is a deployment artifact,
not a place to develop, and it should never be merged.
