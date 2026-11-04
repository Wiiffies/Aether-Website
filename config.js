window.AETHER_CONFIG = {
  githubRepo: "Wiiffies/Aether-Website",
  fallbackDownloadUrl: "",
  versionFallback: "1.2.0",

  donations: {
    endpoint: "/api/donations",
    // Public Turnstile site key from the Cloudflare dashboard. The secret belongs in
    // the Pages project as TURNSTILE_SECRET_KEY, never in this file.
    turnstileSiteKey: "",
    minAmount: 3,
    maxAmount: 500
  },

  maintenance: {
    enabled: false,
    title: "Aether is getting",
    titleAccent: "a tune-up.",
    message: "The site is being rebuilt and the launcher download is paused for a moment. Nothing is lost, and this page returns on its own when the work is done.",
    endsAt: ""
  }
};
