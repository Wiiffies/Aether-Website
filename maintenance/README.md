# Maintenance mode

There are **two switches**, and either one is enough. They are independent on purpose: the
Cloudflare rule is the fast one (seconds), the Pages branch is the one that works even if the
Cloudflare zone is ever changed by someone else.

---

## Switch A — Cloudflare rule (fastest, recommended)

The page it serves is `maintenance.html` on `main`, which is live at
<https://get-aether.de/maintenance.html>. That file is the source of truth.

- **Turn on:** Cloudflare dashboard → your zone `get-aether.de` → **Rules → Overview** →
  enable the rule named **`Maintenance mode (site-wide)`**.
- **Turn off:** disable the same rule. Nothing is deleted, so re-enabling is instant.

The rule rewrites every request to `/maintenance.html` **except `/api/*`**, so the Worker,
checkout, the payment IPN callback and the password-reset API all keep working while the site
is closed. That exclusion is the whole reason the rule is safe to leave armed.

## Switch B — GitHub Pages branch

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
