# Aether website

The public site for the Aether launcher, served at [get-aether.de](https://get-aether.de).
Static HTML, CSS and JavaScript. No build step, no framework, no runtime dependency.

## Pages

| Path | File | Purpose |
| --- | --- | --- |
| `/` | `index.html` | Hero, toolbox, overlay, settings, script and download sections. |
| `/get-script.html` | `get-script.html` | Status page for the Aether Menu build (coming soon). |
| `/maintenance.html` | `maintenance.html` | Standalone maintenance page. |

## Maintenance

`config.js` holds the switch:

```js
maintenance: { enabled: true }
```

While it is on, every page sends visitors to `maintenance.html`. The page is
deliberately self-contained: all of its CSS and JavaScript are inline, so it still
renders when a Cloudflare redirect rule sends every other path to it.

Deploying is a normal branch push. GitHub Pages serves this branch, and Cloudflare
proxies the hostname with its managed DDoS protection in front.

## The script page

`get-script.html` is a status page while the menu is in development: no loader, no
source and no download. When the first build is ready, the page will show one loader
line, and the build will stay off the page, the sitemap and this README. Replacing
that file will ship a new build without changing the line anyone pasted. A static
host cannot hide a file a client is allowed to download, so the loader will keep the
source out of sight rather than making it cryptographically unavailable.

## Local preview

```bash
python -m http.server 4173 --directory .
```

Then open `http://localhost:4173/index.html?maintenance=0` to bypass the gate.
The plain server does not resolve extensionless routes; use the `.html` paths.

## House rules

- One theme. The palette comes from the launcher: `#0A0C11`, `#2672FA`, Segoe UI.
- Motion is always on. Scroll reveals stay readable: they start around 62% opacity
  and move under 26 px, so text never leaves the screen while you scroll.
- Keep the "not affiliated with Roblox Corporation" line in the footer.
