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

  // ---------------------------------------------------------------------------
  // BETA BRANCH ONLY (git branch `beta`).
  // This build is published by .github/workflows/deploy-beta.yml to the Beta
  // environment. apiBase stays "" because the Beta host routes /api/* to the Beta
  // Worker, so the session cookie is still first-party. betaDeployment only drives
  // UI labelling - it NEVER grants access: the Worker checks the Tester role.
  // ---------------------------------------------------------------------------
  betaDeployment: true,


  // Cloudflare Turnstile — PUBLIC site key only (the secret lives in the Worker as
  // TURNSTILE_SECRET and is never exposed here). Leave empty and no widget is rendered
  // and no captcha is required. To enable: create a Turnstile widget for get-aether.de,
  // put its site key here and add the secret to the Worker.
  turnstileSiteKey: "",
  turnstileLogin: false,

  // Donation destination. Set this to your own donation page / profile link
  // (for example a Ko-fi, GitHub Sponsors or a NOWPayments donation link you created).
  // While it is empty, /donate.html shows a clear "not configured yet" state instead of
  // inventing an address or a payment endpoint.
  donateUrl: "",

  // successUrl / cancelUrl default to /payment-success.html and /payment-cancel.html
  // on the site's own origin.
};
