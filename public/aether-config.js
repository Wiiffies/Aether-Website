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
  // PRESENTATION ONLY, and deliberately so. This file ships to every browser, so a download link
  // written here is public the moment it is committed and any gate around it is decoration.
  //
  // The build itself — where it lives, its version, platform, size and checksum — is NOT here. It
  // lives in the Worker's own environment (PROGRAM_URL, PROGRAM_VERSION, PROGRAM_PLATFORM,
  // PROGRAM_SIZE, PROGRAM_SHA256, PROGRAM_NAME, PROGRAM_NOTES). The Worker re-checks the session, the
  // verified email and the Tester role on every request, then streams the file back itself, so the
  // browser never learns the location and a leaked link is useless to anyone else.
  //
  // To publish a build: set those variables on the Worker and commit nothing here.
  program: {
    name: "Aether Desktop",
    tagline: "Not out yet.",
    kicker: "In development",
    status: "in development",
    // description:  one sentence replacing the default lead paragraph
    // statusNote:   one sentence shown next to the status line
  },
};
