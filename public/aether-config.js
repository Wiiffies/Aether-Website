// Aether frontend config — everything here is PUBLIC (it ships to the browser).
// Never put API keys, tokens or passwords in this file.
window.AETHER_CONFIG = window.AETHER_CONFIG || {
  // Empty string = same origin. https://get-aether.de/api/* is routed to the Aether Worker
  // by Cloudflare, so the session cookie is a first-party HttpOnly cookie (SameSite=Lax)
  // and the API is never called cross-site from a normal visit.
  apiBase: "",

  // Optional override used ONLY when a page runs on localhost / 127.0.0.1.
  // Leave it empty to talk to the local dev server (node _dev-server.mjs), which serves
  // /api/* same-origin through the real Worker. Set it to a deployed API URL only if you
  // deliberately want local pages to hit that deployment — it then also has to allow the
  // http://127.0.0.1:5501 origin (ALLOW_DEV_ORIGIN on the Worker).
  devApiBase: "",

  currency: "eur",

  // Cloudflare Turnstile — PUBLIC site key only (the secret lives in the Worker as
  // TURNSTILE_SECRET and is never exposed here). Leave empty and no widget is rendered
  // and no captcha is required. The widget "Aether signup + password reset (get-aether.de)"
  // (managed mode, created 2026-10-01) backs registration and the password-reset endpoints.
  // Rotating the widget means updating this key AND the Worker secret — and the site key has to
  // be live here BEFORE the secret exists, or registration fails for everyone.
  turnstileSiteKey: "0x4AAAAAAFLTy5UwWdhRpX5A",
  turnstileLogin: false,

  // Donation destination. Set this to your own donation page / profile link
  // (for example a Ko-fi, GitHub Sponsors or a NOWPayments donation link you created).
  // While it is empty, /donate.html shows a clear "not configured yet" state instead of
  // inventing an address or a payment endpoint.
  donateUrl: "",

  // successUrl / cancelUrl default to /payment-success.html and /payment-cancel.html
  // on the site's own origin.

  // ---- Aether Desktop (program.html) ----
  // The whole program page reads from this one block, so publishing a build is a single edit. Any
  // value left empty degrades to plain "not out yet" copy instead of printing something invented.
  //
  // SECURITY: this file ships to every browser, so a downloadUrl put here is public the moment it is
  // committed. Only use a link that is safe to be public — a host that checks the session itself, or
  // an unguessable short-lived one. program.html only *renders* the link for an allowed session; no
  // amount of client-side gating can make a URL secret.
  program: {
    name: "Aether Desktop",
    tagline: "Not out yet.",
    kicker: "In development",
    status: "in development",
    // description:  one sentence replacing the default lead paragraph
    // statusNote:   one sentence shown next to the status line
    platform: "",      // e.g. "Windows 10/11 (x64)"
    version: "",       // e.g. "0.1.0-beta.1"
    size: "",          // e.g. "48 MB"
    sha256: "",        // publish this whenever downloadUrl is set — testers are told to verify it
    downloadUrl: "",   // empty => a tester sees "no build published yet"
  },
};
