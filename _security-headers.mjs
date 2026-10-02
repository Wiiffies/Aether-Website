// The security headers every page on get-aether.de is served with.
//
// Two places use this file, and they must never disagree:
//
//   1. The edge — a Cloudflare **Response Header Transform** rule on the zone (phase
//      `http_response_headers_transform`) sets exactly these values on every HTML/asset response for
//      get-aether.de and www, and deliberately skips `/api/*`, where the Worker sets its own
//      (stricter) headers. **A second rule in the same phase sets `Cache-Control: no-cache` on HTML
//      only** (never on the versioned assets): GitHub Pages pins its own `max-age=600` on HTML, which
//      hid a deploy from returning visitors for ten minutes and made a correct fix look broken. It
//      is deliberately separate from this file — this file is the security policy, that rule is
//      about deploy visibility — but the two must not be collapsed into one.
//   2. `_dev-server.mjs` — so the policy can be exercised locally before it is ever live, instead of
//      discovering a broken page after deploying it.
//
// `_check-headers.mjs` proves the pages stay compatible with the policy: a script loaded from a new
// CDN, an `eval()` or a `data:` script fails CI instead of silently breaking the live site.
//
// The policy allows `'unsafe-inline'` for scripts and styles because this site is built from inline
// `<script>` blocks and inline `style=` attributes. That is a real limit, and it is still a large
// win over sending no policy at all: injected external JavaScript, exfiltration to an attacker's
// host, `<object>`/`<embed>` payloads, `<base>` hijacking, form hijacking and clickjacking are all
// blocked, and every one of them is a normal outcome of a script-injection bug.
export const PAGE_SECURITY_HEADERS = {
  "content-security-policy": [
    "default-src 'self'",
    "base-uri 'self'",
    "object-src 'none'",
    "frame-ancestors 'none'",
    "form-action 'self'",
    "script-src 'self' 'unsafe-inline' https://challenges.cloudflare.com",
    "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
    "font-src 'self' https://fonts.gstatic.com",
    "img-src 'self' data:",
    "connect-src 'self' https://api.get-aether.de https://challenges.cloudflare.com",
    "frame-src https://challenges.cloudflare.com",
    "worker-src 'self' https://challenges.cloudflare.com",
    "upgrade-insecure-requests",
  ].join("; "),

  // frame-ancestors already covers modern browsers; this covers the rest.
  "x-frame-options": "DENY",
  // Never let a browser guess a content type: an uploaded or injected file must not become script.
  "x-content-type-options": "nosniff",
  // Cross-origin requests get the origin only, never the path or the query. The three pages that can
  // carry a secret in the query string are stricter still, and set `no-referrer` in their own <head>.
  "referrer-policy": "strict-origin-when-cross-origin",
  // Nothing on this site uses a camera, a microphone, a location or a payment sheet. Deny them, so a
  // page that ever does has to ask for it deliberately.
  "permissions-policy":
    "accelerometer=(), autoplay=(self), camera=(), display-capture=(), encrypted-media=(), " +
    "fullscreen=(self), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), midi=(), " +
    "payment=(), picture-in-picture=(self), publickey-credentials-get=(), screen-wake-lock=(), " +
    "usb=(), xr-spatial-tracking=()",
  // Isolates the browsing context: a page opened from elsewhere cannot reach back through
  // `window.opener` into this one. Nothing here opens popups or uses OAuth, so nothing needs it.
  "cross-origin-opener-policy": "same-origin",
};

// Origins the policy itself permits, plus this site's own two hostnames. `_check-headers.mjs` uses
// this to decide whether an external URL found in a page is something the policy will actually load.
export const SELF_ORIGINS = ["https://get-aether.de", "https://www.get-aether.de"];

// Local development only: `_dev-server.mjs` prints this URL, and the pages may mention it in a
// comment. It is never a live resource, so it is listed here instead of in the policy.
export const DEV_ONLY_ORIGINS = ["http://127.0.0.1", "http://localhost"];

// Domains reserved by RFC 2606 / RFC 6761. They cannot resolve to a host anyone controls, so nothing
// can ever be fetched from them - they appear as example text in form placeholders.
export const RESERVED_DOCUMENTATION_ORIGINS = ["https://example.com", "https://example.org", "https://example.net"];
