/* Aether checkout helper — shared by shop, discord-bot, website pages */
(function(){
  const CFG = window.AETHER_CONFIG || {};
  // --- where the API lives -------------------------------------------------------------
  // Production: same origin (get-aether.de/api/* → Aether Worker via a Cloudflare route),
  // so the session cookie is first-party. Local development: fall back to the public
  // API host so the pages still work when opened from a local static server.
  const RAW_API = String(CFG.apiBase == null ? "" : CFG.apiBase).replace(/\/+$/, "");
  const IS_LOCAL = /^(localhost|127\.0\.0\.1|\[::1\])$/i.test(location.hostname) || location.protocol === "file:";
  const API = RAW_API || (IS_LOCAL ? String(CFG.devApiBase || "").replace(/\/+$/, "") : "");
  const CROSS_ORIGIN = !!API && API.indexOf(location.origin) !== 0;
  const CURRENCY = (CFG.currency || "eur").toLowerCase();
  const SUCCESS_URL = CFG.successUrl || (location.origin + "/payment-success.html");
  const CANCEL_URL  = CFG.cancelUrl  || (location.origin + "/payment-cancel.html");
  function apiUrl(path){ if(API) return API + path; return location.protocol === "file:" ? null : path; }
  // --- auth ---------------------------------------------------------------------------
  // Primary mechanism: an HttpOnly, Secure session cookie set by the Worker. This helper
  // keeps a short-lived copy in sessionStorage (per tab, cleared when the tab closes) purely
  // as a fallback so a signed-in customer is not logged out if cookies are blocked. Nothing
  // sensitive is persisted on the device beyond the current browsing session.
  const TOKEN_KEY = "aether_token";
  function getToken(){
    try{
      const live = sessionStorage.getItem(TOKEN_KEY);
      if(live) return live;
      // One-time migration: older builds kept the token in localStorage for 30 days.
      const legacy = localStorage.getItem(TOKEN_KEY);
      if(legacy){ localStorage.removeItem(TOKEN_KEY); sessionStorage.setItem(TOKEN_KEY, legacy); return legacy; }
      return "";
    }catch{ return ""; }
  }
  function setToken(t){
    try{
      if(t){ sessionStorage.setItem(TOKEN_KEY, t); localStorage.removeItem(TOKEN_KEY); }
      else { sessionStorage.removeItem(TOKEN_KEY); localStorage.removeItem(TOKEN_KEY); }
    }catch{}
  }
  function authHeaders(extra){
    const h = Object.assign({}, extra || {});
    const t = getToken();
    if(t) h.authorization = "Bearer " + t;
    return h;
  }
  // --- signed-in account cache -------------------------------------------------
  // The account already knows the customer email + Discord, so checkout never asks
  // for them again. Cached in localStorage so a slow or offline API cannot make a
  // signed-in customer retype what we already have.
  const ACCOUNT_KEY = "aether_account";
  let accountCache = null;
  let accountInflight = null;
  function account(){
    if(accountCache) return accountCache;
    try{
      const raw = sessionStorage.getItem(ACCOUNT_KEY) || localStorage.getItem(ACCOUNT_KEY);
      localStorage.removeItem(ACCOUNT_KEY);
      if(raw) accountCache = JSON.parse(raw);
    }catch{ accountCache = null; }
    return accountCache || null;
  }
  function setAccount(a){
    accountCache = a || null;
    try{ a ? sessionStorage.setItem(ACCOUNT_KEY, JSON.stringify(a)) : sessionStorage.removeItem(ACCOUNT_KEY); localStorage.removeItem(ACCOUNT_KEY); }catch{}
  }
  function accountEmail(){ const a = account(); return a && isEmail(a.email) ? String(a.email).trim() : ""; }
  // Accounts are mandatory: there is no guest checkout. Send anyone who is not signed in to the
  // portal, and bring them back to the page they were on (account.html honours ?next=).
  function accountUrl(){
    const here = location.pathname + location.search + location.hash;
    const back = /account\.html$/.test(location.pathname) ? "" : ("?next=" + encodeURIComponent(here));
    return "account.html" + back;
  }
  function accountDiscord(){ const a = account(); return a && a.discord ? String(a.discord).trim() : ""; }
  // Request forms (website / discord bot) are account-only like checkout itself: the account
  // supplies the reply address and the request is filed in the portal chat, so pages ask for a
  // session before they submit anything. Renders the same CTA the pay modal uses.
  function accountRequiredNotice(el, text){
    if(accountEmail()) return true;
    if(el){
      el.innerHTML = esc(text || "This needs an account \u2014 there is no guest checkout.") +
        ' <a href="' + accountUrl() + '" style="display:inline-block;margin-left:8px;padding:6px 12px;border-radius:6px;background:#fff;color:#000;font-weight:700;text-decoration:none">Create an account / sign in</a>';
      el.className = "form-msg err";
    }
    return false;
  }
  function loadAccount(force){
    if(!getToken() && CROSS_ORIGIN){ setAccount(null); return Promise.resolve(null); }
    if(!force && account()) return Promise.resolve(account());
    if(accountInflight) return accountInflight;
    accountInflight = (async()=>{
      try{
        const r = await apiFetch("/api/me");
        if(r && r.user){ setAccount({ id:r.user.id, email:r.user.email, discord:r.user.discord||"", isAdmin:!!r.isAdmin, emailVerified:r.emailVerified !== false, verificationRequired:!!r.emailVerificationRequired }); return accountCache; }
        return account();
      }catch(e){
        const m = String((e && e.message) || "");
        if(/401/.test(m) || /not authenticated/i.test(m)){ setToken(""); setAccount(null); return null; }
        // Network hiccup or 5xx: keep showing the cached account instead of dropping it.
        return account();
      }
    })().finally(()=>{ accountInflight = null; });
    return accountInflight;
  }
  // Warm the cache once per page so the pay modal is instant when it is opened.
  try{ if(getToken()) setTimeout(()=>{ loadAccount(true).catch(()=>{}); }, 0); }catch{}
  // An email we can actually use: what the form typed, else the signed-in account.
  function resolveEmail(raw){
    const v = String(raw||"").trim();
    if(isEmail(v)) return v;
    return accountEmail();
  }
  // Prefill request forms from the signed-in account and mark the fields as optional.
  // Anything tagged [data-account-email] / [data-account-discord] is filled in, and the
  // matching [data-account-email-note] line explains where the address came from.
  function bindAccountFields(root, onReady){
    const doc = root || document;
    const apply = (acct)=>{
      const em = (acct && isEmail(acct.email)) ? String(acct.email).trim() : "";
      if(em){
        doc.querySelectorAll("[data-account-email]").forEach(inp=>{
          inp.value = em;
          inp.title = "Your account email (" + em + ") — edit it if you want a different one";
          if(inp.required){ inp.required = false; inp.setAttribute("data-was-required","1"); }
        });
      }
      if(acct && acct.discord){
        doc.querySelectorAll("[data-account-discord]").forEach(inp=>{ if(!String(inp.value||"").trim()) inp.value = String(acct.discord).trim(); });
      }
      if(!em) return;
      doc.querySelectorAll("[data-account-email-note]").forEach(n=>{
        n.textContent = "Using your account email (" + em + ") — orders and chat land in your account automatically.";
        n.style.color = "#8ee0a0";
        n.style.display = "";
      });
      doc.querySelectorAll("[data-account-optional]").forEach(n=>{ n.style.display = "none"; });
    };
    apply(account());
    // Always ask the server (it answers from the cache when there is one). The session cookie is
    // HttpOnly, so a visitor with cookies but an empty sessionStorage -- a new tab, or a browser
    // reopened after the tab closed -- IS signed in, and asking them to sign in again is a lie.
    loadAccount(false).then(a=>{ apply(a); if(onReady) onReady(a); }).catch(()=>{ if(onReady) onReady(account()); });
    return account();
  }
  // Turns an HTTP failure into one human sentence. Rate limits get their own wording so a
  // visitor understands they simply need to wait, not that the site is broken.
  function friendlyError(j, status){
    const raw = (j && (j.error || j.message)) || "";
    if(status === 429) return raw || "Too many attempts — please wait a few minutes and try again.";
    if(status === 403 && /origin/i.test(raw)) return "This request was blocked for security reasons. Reload the page and try again.";
    // 500/502 are crashes: never surface the server's wording there. 503 keeps its message
    // because the Worker uses it for honest "this feature is not configured yet" answers.
    if(status === 500 || status === 502) return "Aether had a temporary problem. Please try again in a moment.";
    if(raw) return raw;
    if(status >= 500) return "Aether had a temporary problem. Please try again in a moment.";
    return "Request failed (" + status + ")";
  }
  async function readJson(res){
    const text = await res.text();
    let j; try{ j = JSON.parse(text); }catch{ j = { raw:text }; }
    if(!res.ok){
      const err = new Error(friendlyError(j, res.status));
      err.status = res.status;
      // A machine-readable reason travels with the error so a page can tell "signed out" from "email
      // not confirmed" from "not a Tester" and answer with the one instruction that fits.
      err.code = (j && j.code) || "";
      throw err;
    }
    return j;
  }
  async function apiFetch(path, opts){
    const url = apiUrl(path);
    if(!url) throw new Error("The Aether API is not reachable from this page — open the site over https://get-aether.de");
    const o = Object.assign({ method:"GET" }, opts || {});
    const headers = authHeaders(Object.assign({ "accept":"application/json" }, (opts && opts.headers) || {}));
    if(o.body && typeof o.body !== "string"){ o.body = JSON.stringify(turnstile.decorate(o.body)); headers["content-type"] = "application/json"; }
    const res = await fetch(url, Object.assign(o, { headers, credentials: "include" }));
    return readJson(res);
  }
  function isConfigured(){ return !!API || location.protocol !== "file:"; }
  async function postJson(url, data){
    if(!url) throw new Error("The Aether API is not reachable from this page — open the site over https://get-aether.de");
    const res = await fetch(url, {
      method: "POST",
      headers: authHeaders({ "content-type": "application/json" }),
      credentials: "include",
      body: JSON.stringify(turnstile.decorate(data)),
    });
    return readJson(res);
  }
  function esc(s){ return String(s).replace(/[&<>"']/g, c=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c])); }
  // Prices can be fractional (the EUR 0.02 test item), so show cents only when they exist.
  function eur(n){ const v=Number(n)||0; return (Math.abs(v % 1) < 0.005) ? ("€"+v.toFixed(0)) : ("€"+v.toFixed(2)); }
  // One place that asks the Worker about a promo code. The server is authoritative: whatever it
  // answers (discount, final amount, test:true) is what the UI shows, and the invoice recomputes
  // it again from PROMO_CODES, so a tampered page can never grant itself a discount.
  async function promoLookup(code, amount){
    const url = apiUrl("/api/promo?code=" + encodeURIComponent(code) + "&amount=" + encodeURIComponent(String(amount||0)));
    if(!url) throw new Error("The Aether API is not reachable from this page — open the site over https://get-aether.de");
    const res = await fetch(url, { headers: { "accept":"application/json" } });
    const text = await res.text(); let j; try{ j=JSON.parse(text);}catch{ j={raw:text};}
    if(!res.ok) throw new Error(j.error || j.message || "Promo check failed ("+res.status+")");
    return j;
  }

  // --- optional Cloudflare Turnstile ---------------------------------------------------
  // Rendered only when a PUBLIC site key is configured. The token is verified server-side by
  // the Worker against challenges.cloudflare.com — a frontend-only "success" is never trusted.
  const turnstile = (() => {
    const siteKey = String(CFG.turnstileSiteKey || "");
    let widgetId = null, token = "", loading = null;
    function load(){
      if(loading) return loading;
      loading = new Promise((resolve, reject) => {
        const s = document.createElement("script");
        s.src = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit";
        s.async = true; s.defer = true;
        s.onload = () => resolve(true);
        s.onerror = () => reject(new Error("Could not load the captcha widget"));
        document.head.appendChild(s);
      });
      return loading;
    }
    return {
      enabled(){ return !!siteKey; },
      token(){ return token; },
      async mount(el){
        if(!siteKey || !el) return null;
        try{
          await load();
          if(!window.turnstile) return null;
          el.hidden = false;
          widgetId = window.turnstile.render(el, {
            sitekey: siteKey,
            theme: "dark",
            callback: (t) => { token = t; },
            "error-callback": () => { token = ""; },
            "expired-callback": () => { token = ""; },
          });
          return widgetId;
        }catch{ return null; }
      },
      reset(){ try{ if(window.turnstile && widgetId !== null) window.turnstile.reset(widgetId); }catch{} token = ""; },
      decorate(payload){
        if(!token || !payload || typeof payload !== "object") return payload;
        const out = Object.assign({}, payload);
        if(!out.turnstileToken) out.turnstileToken = token;
        return out;
      },
      // Any element with [data-turnstile] becomes a widget container when the key is set.
      autoMount(root){
        if(!siteKey) return;
        (root || document).querySelectorAll("[data-turnstile]").forEach(el => { this.mount(el); });
      },
    };
  })();
  // NOTE: keep the character classes as [^\s@] (a single backslash). A doubled
  // backslash turns them into "not backslash, not s, not @", which silently
  // rejects every address containing the letter s.
  function isEmail(s){ return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(String(s||"").trim()); }

  function showPayModal(opts){
    // opts: { title, amount, breakdownHtml, defaultEmail, defaultDiscord, onConfirm({email,discord,payCurrency,setMsg}) }
    let overlay = document.getElementById("aether-pay-modal");
    if(overlay) overlay.remove();
    overlay = document.createElement("div");
    overlay.id = "aether-pay-modal";
    overlay.setAttribute("role","dialog");
    overlay.setAttribute("aria-modal","true");
    overlay.style.cssText = "position:fixed;inset:0;z-index:9999;display:flex;align-items:center;justify-content:center;padding:18px;background:rgba(0,0,0,.7);backdrop-filter:blur(6px);overflow-y:auto";
    const defEmail = esc(opts.defaultEmail || accountEmail() || "");
    const defDiscord = esc(opts.defaultDiscord || accountDiscord() || "");
    overlay.innerHTML = `
      <div style="width:min(560px,100%);background:#0b0b0c;border:1px solid #24262b;border-radius:16px;overflow:hidden;box-shadow:0 24px 70px rgba(0,0,0,.65);margin:auto">
        <div style="padding:22px 24px 18px;border-bottom:1px solid #1d1f23;display:flex;align-items:flex-start;justify-content:space-between;gap:14px">
          <div style="display:flex;gap:12px;align-items:flex-start;min-width:0">
            <span style="flex:0 0 auto;width:34px;height:34px;border-radius:10px;border:1px solid #26282d;background:#141519;display:grid;place-items:center;font-size:15px">&#9889;</span>
            <div style="min-width:0">
              <div style="font:10px 'DM Mono',monospace;letter-spacing:.14em;text-transform:uppercase;color:#85878b">Secure checkout</div>
              <div style="font-size:17px;font-weight:600;letter-spacing:-.03em;margin-top:5px;line-height:1.3">${esc(opts.title||"Confirm order")}</div>
            </div>
          </div>
          <button type="button" data-close aria-label="Close" style="flex:0 0 auto;width:32px;height:32px;border-radius:9px;border:1px solid #26282d;background:#141519;color:#c8c9cc;font-size:13px;cursor:pointer;display:grid;place-items:center">&#10005;</button>
        </div>
        <div style="padding:20px 24px 24px">
          <div style="border:1px solid #1d1f23;border-radius:12px;padding:16px;background:#0f1013">
            <div style="font:10px 'DM Mono',monospace;letter-spacing:.12em;text-transform:uppercase;color:#85878b;margin-bottom:10px">Order summary</div>
            <div style="font-size:12px;line-height:1.75;color:#c8c9cc">${opts.breakdownHtml || ""}</div>
            <div style="margin-top:14px;padding-top:14px;border-top:1px solid #1d1f23;display:flex;align-items:baseline;justify-content:space-between;gap:12px">
              <span style="font:10px 'DM Mono',monospace;letter-spacing:.12em;text-transform:uppercase;color:#85878b">Total due</span>
              <span data-total style="font-size:24px;font-weight:600;letter-spacing:-.045em;color:#fff">${eur(opts.amount)}</span>
            </div>
            <div style="margin-top:8px;font-size:10px;line-height:1.6;color:#6f7277">Crypto via NOWPayments — BTC / LTC / ETH. Hosting, domain and database are not included.</div>
          </div>

          <div data-pay-fields style="margin-top:16px;display:grid;gap:12px">
            <label style="display:flex;flex-direction:column;gap:6px">
              <span style="font:10px 'DM Mono',monospace;letter-spacing:.1em;text-transform:uppercase;color:#85878b">Pay with <span style="color:#ff6b6b">*</span></span>
              <div style="display:flex;gap:8px">
                <button type="button" data-coin="btc" style="flex:1;padding:12px 0;border-radius:9px;border:1px solid #2a2e33;background:#111214;color:#fff;font-size:13px;font-weight:600;cursor:pointer">₿ BTC</button>
                <button type="button" data-coin="ltc" style="flex:1;padding:12px 0;border-radius:9px;border:1px solid #2a2e33;background:#111214;color:#fff;font-size:13px;font-weight:600;cursor:pointer">Ł LTC</button>
                <button type="button" data-coin="eth" style="flex:1;padding:12px 0;border-radius:9px;border:1px solid #2a2e33;background:#111214;color:#fff;font-size:13px;font-weight:600;cursor:pointer">♦ ETH</button>
              </div>
              <span data-coin-hint style="font:10px 'DM Mono',monospace;color:#7a7d82;margin-top:2px">Select BTC, LTC or ETH</span>
            </label>
            <label style="display:flex;flex-direction:column;gap:6px">
              <span style="font:10px 'DM Mono',monospace;letter-spacing:.1em;text-transform:uppercase;color:#85878b">Email <span style="color:#ff6b6b">*</span></span>
              <input data-field="email" type="email" value="${defEmail}" placeholder="you@example.com" autocomplete="email" style="width:100%;background:#111214;border:1px solid #2a2e33;color:#fff;border-radius:4px;padding:11px 12px;font-size:12px;font-family:Manrope,Arial,sans-serif;outline:none">
            </label>
            <label style="display:flex;flex-direction:column;gap:6px">
              <span style="font:10px 'DM Mono',monospace;letter-spacing:.1em;text-transform:uppercase;color:#85878b">Discord username <span style="color:#7a7d82;font-weight:400;text-transform:none;letter-spacing:0">optional — leave blank if you don’t have Discord</span></span>
              <input data-field="discord" value="${defDiscord}" placeholder="yourname" autocomplete="username" style="width:100%;background:#111214;border:1px solid #2a2e33;color:#fff;border-radius:8px;padding:11px 12px;font-size:12px;font-family:Manrope,Arial,sans-serif;outline:none">
            </label>
            ${opts.promo ? `
            <label style="display:flex;flex-direction:column;gap:6px">
              <span style="font:10px 'DM Mono',monospace;letter-spacing:.1em;text-transform:uppercase;color:#85878b">Promo code <span style="color:#7a7d82;font-weight:400">— optional</span></span>
              <div style="display:flex;gap:8px">
                <input data-promo-input placeholder="WELCOME10" autocomplete="off" spellcheck="false" style="flex:1;background:#111214;border:1px solid #2a2e33;color:#fff;border-radius:4px;padding:10px 12px;font:12px 'DM Mono',monospace;outline:none;text-transform:uppercase">
                <button type="button" data-promo-apply style="padding:10px 14px;border-radius:6px;border:1px solid #333;background:#1a1d20;color:#fff;font-size:11px;font-weight:600;cursor:pointer;white-space:nowrap">Apply</button>
              </div>
              <span data-promo-msg style="font:10px 'DM Mono',monospace;color:#7a7d82;min-height:14px"></span>
            </label>` : ``}
          </div>

          <div data-pay-actions style="margin-top:18px;display:flex;gap:10px">
            <button type="button" data-confirm style="flex:1;padding:13px 16px;border-radius:10px;border:1px solid #fff;background:#fff;color:#08080a;font-family:Manrope,Arial,sans-serif;font-size:13px;font-weight:700;cursor:pointer">Pay with Crypto →</button>
            <button type="button" data-close style="padding:13px 16px;border-radius:10px;border:1px solid #2a2e33;background:transparent;color:#c8c9cc;font-family:Manrope,Arial,sans-serif;font-size:13px;cursor:pointer">Cancel</button>
          </div>
          <div data-msg style="margin-top:10px;font-size:11px;line-height:1.6;color:#9aa0a6;white-space:pre-wrap"></div>
          ${!isConfigured() ? `<div style="margin-top:10px;padding:10px 12px;border:1px solid #442;border-radius:6px;background:#1a1510;color:#c9a87a;font-size:11px;line-height:1.6">Worker not configured yet — <code style="color:#fff">public/aether-config.js → apiBase</code> is empty. Checkout needs the API to create a payment.</div>` : ``}
        </div>
      </div>
    `;
    document.body.appendChild(overlay);
    document.body.style.overflow = "hidden";
    const emailInput = overlay.querySelector('[data-field="email"]');
    const discordInput = overlay.querySelector('[data-field="discord"]');
    const coinHint = overlay.querySelector('[data-coin-hint]');
    const coinBtns = overlay.querySelectorAll('[data-coin]');
    // --- account-aware email: never ask a signed-in customer for their email ---------
    const emailWrap = emailInput.closest("label") || emailInput.parentNode;
    // The email field never appears any more: the account supplies it, or the CTA asks for an account.
    emailWrap.style.display = "none";
    const confirmBtn = overlay.querySelector("[data-confirm]");
    let accountMode = false;
    let resolvedAccountEmail = "";
    const acctRow = document.createElement("div");
    acctRow.style.cssText = "display:none;flex-direction:column;gap:6px";
    acctRow.innerHTML = `<span style="font:10px 'DM Mono',monospace;letter-spacing:.11em;text-transform:uppercase;color:#85878b">Signed in</span>
      <div style="display:flex;align-items:center;justify-content:space-between;gap:10px;background:#0f1418;border:1px solid #24402c;border-radius:10px;padding:11px 12px">
        <span style="font-size:12px;color:#8ee0a0;word-break:break-all;min-width:0">&#10003; <b data-acct-chip style="color:#fff"></b></span>
        <button type="button" data-acct-change style="padding:5px 10px;border-radius:6px;border:1px solid #2a2e33;background:#191c21;color:#c8c9d8;font:10px 'DM Mono',monospace;cursor:pointer;white-space:nowrap">Switch</button>
      </div>
      <span style="font:10px 'DM Mono',monospace;line-height:1.7;color:#7a7d82">Your invoice, order updates and chat stay tied to this account.</span>`;
    const acctChip = acctRow.querySelector("[data-acct-chip]");
    emailWrap.parentNode.insertBefore(acctRow, emailWrap);
    // --- not signed in: one clean card, and the single button that fixes it --------------------
    const accountNotice = document.createElement("div");
    accountNotice.style.cssText = "border:1px solid #26282d;border-radius:12px;background:#141419;padding:16px;font-family:Manrope,Arial,sans-serif";
    accountNotice.innerHTML = `<div style="display:flex;gap:12px;align-items:flex-start">
        <span style="flex:0 0 auto;width:30px;height:30px;border-radius:9px;background:#1d1f24;display:grid;place-items:center;font-size:14px">&#128274;</span>
        <div style="min-width:0">
          <div style="font-size:13px;font-weight:700;color:#fff">Sign in to buy</div>
          <div style="margin-top:5px;font-size:12px;line-height:1.7;color:#9aa0a6">Checkout is account-only &mdash; your orders, invoices and chat history live in your account, so there is no guest checkout.</div>
        </div>
      </div>
      <button type="button" data-goto-account style="width:100%;margin-top:14px;padding:12px 14px;border-radius:9px;border:1px solid #fff;background:#fff;color:#08080a;font-family:Manrope,Arial,sans-serif;font-size:13px;font-weight:700;cursor:pointer">Create an account / sign in &rarr;</button>`;
    accountNotice.querySelector("[data-goto-account]").addEventListener("click", ()=>{ location.href = accountUrl(); });
    emailWrap.parentNode.insertBefore(accountNotice, emailWrap.nextSibling);
    // --- signed in, but the address is still unconfirmed: the Worker refuses that checkout --------
    const verifyNotice = document.createElement("div");
    verifyNotice.style.cssText = "display:none;border:1px solid #4a3a12;border-radius:12px;background:rgba(255,204,0,.05);padding:15px;font-family:Manrope,Arial,sans-serif";
    verifyNotice.innerHTML = `<div style="font-size:13px;font-weight:700;color:#fff">Confirm your email to finish checkout</div>
      <div style="margin-top:6px;font-size:12px;line-height:1.7;color:#c9b577">This account has not confirmed its address yet, and checkout only opens for a confirmed account.</div>
      <button type="button" data-resend style="margin-top:12px;padding:10px 14px;border-radius:8px;border:1px solid #3a3a1e;background:#191a10;color:#e8d48a;font-family:Manrope,Arial,sans-serif;font-size:12px;font-weight:700;cursor:pointer">Resend confirmation email</button>`;
    verifyNotice.querySelector("[data-resend]").addEventListener("click", (ev)=> resendVerification(ev.currentTarget));
    accountNotice.parentNode.insertBefore(verifyNotice, accountNotice.nextSibling);
    let needsVerify = false;
    function useAccount(acct){
      if(!overlay.isConnected) return false;
      const em = (acct && isEmail(acct.email)) ? String(acct.email).trim() : "";
      if(!em) return false;
      accountMode = true;
      resolvedAccountEmail = em;
      acctChip.textContent = em;
      emailInput.value = em;
      emailWrap.style.display = "none";
      accountNotice.style.display = "none";
      acctRow.style.display = "flex";
      // A normal unconfirmed account is refused by the Worker (403 EMAIL_UNVERIFIED). An admin
      // account is exempt there, so it is never nagged about a confirmation mail it does not need.
      needsVerify = !acct.isAdmin && acct.emailVerified === false && acct.verificationRequired;
      verifyNotice.style.display = needsVerify ? "" : "none";
      confirmBtn.disabled = false;
      confirmBtn.textContent = needsVerify ? "Confirm your email first" : "Pay with Crypto \u2192";
      confirmBtn.style.opacity = needsVerify ? ".6" : "";
      confirmBtn.style.cursor = "pointer";
      if(!discordInput.value.trim() && acct.discord) discordInput.value = String(acct.discord).trim();
      return true;
    }
    function useAccountRequired(){
      accountMode = false;
      needsVerify = false;
      resolvedAccountEmail = "";
      acctRow.style.display = "none";
      verifyNotice.style.display = "none";
      accountNotice.style.display = "";
      emailWrap.style.display = "none";
      // Kept clickable on purpose: clicking "Sign in to buy" sends the visitor to the portal.
      confirmBtn.disabled = false;
      confirmBtn.textContent = "Sign in to buy \u2192";
      confirmBtn.style.opacity = ".6";
      confirmBtn.style.cursor = "pointer";
    }
    acctRow.querySelector("[data-acct-change]").addEventListener("click", ()=>{ location.href = accountUrl(); });
    if(!useAccount(account())) useAccountRequired();
    // The session cookie is HttpOnly, so a signed-in visitor can have no token in this tab (a new
    // tab, or a browser reopened after the tab closed). Only the server can tell -- and skipping the
    // question is exactly how a signed-in customer was told to sign in to a site they were on.
    loadAccount(false).then(a=>{ if(a && !accountMode) useAccount(a); }).catch(()=>{});
    let payCurrency = (opts.defaultCoin||"").toLowerCase().trim();
    function setCoin(c){
      payCurrency=c;
      coinBtns.forEach(b=>{
        const on=b.dataset.coin===c;
        b.style.background=on?"#fff":"#111214";
        b.style.color=on?"#000":"#fff";
        b.style.borderColor=on?"#fff":"#2a2e33";
      });
      coinHint.textContent=c?("Selected: "+c.toUpperCase()):"Select BTC, LTC or ETH";
      coinHint.style.color=c?"#8ee0a0":"#7a7d82";
    }
    coinBtns.forEach(b=> b.addEventListener("click", ()=> setCoin(b.dataset.coin)));
    if(payCurrency) setCoin(payCurrency);
    else setCoin("");      setTimeout(()=> { if(accountMode) discordInput.focus(); else if(!defEmail) emailInput.focus(); else if(!defDiscord) discordInput.focus(); else emailInput.focus(); }, 50);


    const close = () => { overlay.remove(); document.body.style.overflow=""; document.removeEventListener("keydown", onKey); };
    const onKey = (e)=>{ if(e.key==="Escape") close(); };
    document.addEventListener("keydown", onKey);
    overlay.addEventListener("click", (e)=>{ if(e.target===overlay) close(); });
    overlay.querySelectorAll("[data-close]").forEach(b=> b.addEventListener("click", close));
    const confirm = overlay.querySelector("[data-confirm]");
    const msg = overlay.querySelector("[data-msg]");
    const fieldsEl = overlay.querySelector("[data-pay-fields]");
    const actionsEl = overlay.querySelector("[data-pay-actions]");
    // Once an order exists the form is over: leaving the coin buttons and the promo box around a
    // finished order is what made the result state read like a debug panel. The panel replaces it,
    // and `white-space:normal` stops the template literal's indentation from rendering as gaps.
    function showResult(html){
      if(fieldsEl) fieldsEl.style.display = "none";
      // The result panels carry their own Close / Check status buttons, so the form's action row
      // would only be a second, redundant Cancel next to them.
      if(actionsEl) actionsEl.style.display = "none";
      msg.textContent = "";
      msg.style.whiteSpace = "normal";
      msg.innerHTML = html;
      if(overlay.scrollTo) try{ overlay.scrollTo({ top: overlay.scrollHeight, behavior: "smooth" }); }catch{}
      return msg;
    }
    // --- an unconfirmed address is a fixable state, not a sentence to read and give up on -------
    async function resendVerification(btn){
      if(!btn) return;
      const label = btn.textContent;
      btn.disabled = true; btn.textContent = "Sending…";
      try{
        const r = await apiFetch("/api/auth/resend-verification", { method:"POST", body:{} });
        btn.textContent = (r && r.message) ? r.message : "Sent ✓";
        btn.style.borderColor = "#2a7a3a"; btn.style.color = "#8ee0a0";
      }catch(e){
        btn.disabled = false; btn.textContent = label;
        msg.textContent = e.message || "Could not send the confirmation email right now.";
        msg.style.color = "#ff8a8a";
      }
    }
    function showVerifyMsg(){
      msg.style.color = "#e8eaed";
      // Not showResult(): the fields stay put, because confirming the address is what unlocks them.
      msg.style.whiteSpace = "normal";
      msg.innerHTML = `<div style="padding:14px;border:1px solid #4a3a12;border-radius:10px;background:rgba(255,204,0,.05)">`
        + `<div style="font-size:13px;font-weight:700;color:#fff">Confirm your email before you pay</div>`
        + `<div style="margin-top:6px;font-size:12px;line-height:1.7;color:#c9b577">A confirmation link goes to <b style="color:#fff">${esc(resolvedAccountEmail)}</b>. Open it and come back to this tab — checkout unlocks by itself.</div>`
        + `<button type="button" data-resend style="margin-top:12px;padding:10px 14px;border-radius:8px;border:1px solid #3a3a1e;background:#191a10;color:#e8d48a;font-family:Manrope,Arial,sans-serif;font-size:12px;font-weight:700;cursor:pointer">Resend confirmation email</button></div>`;
      const b = msg.querySelector("[data-resend]");
      if(b) b.addEventListener("click", ()=> resendVerification(b));
    }

    // --- optional promo code ------------------------------------------------------------------
    // The Worker decides the discount; this field only asks. The total shown here is the server's
    // finalAmount, and the invoice recomputes everything from PROMO_CODES again.
    let appliedPromo = null; // {code, discount, finalAmount, test}
    let currentTotal = Number(opts.amount) || 0; // 0 once a test code covers the whole amount
    const totalEl = overlay.querySelector("[data-total]");
    const promoInput = overlay.querySelector("[data-promo-input]");
    const promoMsg = overlay.querySelector("[data-promo-msg]");
    function setTotal(v){
      currentTotal = Math.max(0, Number(v) || 0);
      if(totalEl) totalEl.textContent = eur(currentTotal);
      if(confirm && confirm.style.display !== "none"){
        confirm.textContent = (currentTotal <= 0) ? "Place free order →" : "Pay with Crypto →";
      }
      // A fully discounted order creates no NOWPayments payment, so no coin has to be picked for it.
      if(coinHint && !payCurrency){
        coinHint.textContent = currentTotal <= 0 ? "Not needed — a free order creates no payment" : "Select BTC, LTC or ETH";
        coinHint.style.color = "#7a7d82";
      }
    }
    async function applyPromo(){
      const code = (promoInput.value||"").trim().toUpperCase();
      if(!promoMsg) return;
      if(!code){ appliedPromo = null; promoMsg.textContent = ""; setTotal(opts.amount); return; }
      promoMsg.textContent = "Checking…"; promoMsg.style.color = "#9aa0a6";
      try{
        const r = await promoLookup(code, opts.amount);
        if(r && r.valid){
          appliedPromo = { code: r.code || code, discount: r.discount, finalAmount: r.finalAmount, test: !!r.test };
          promoMsg.textContent = "✓ " + appliedPromo.code + " — " + (r.type === "percent" ? r.value + "% off" : "−" + eur(r.value)) + (appliedPromo.test ? " (test code)" : "") + (appliedPromo.discount ? " · −" + eur(appliedPromo.discount) : "");
          promoMsg.style.color = "#8ee0a0";
          setTotal(r.finalAmount);
        } else {
          appliedPromo = null; setTotal(opts.amount);
          promoMsg.textContent = "✕ " + ((r && r.error) || "Invalid code"); promoMsg.style.color = "#ff8a8a";
        }
      }catch(e){
        appliedPromo = null; setTotal(opts.amount);
        promoMsg.textContent = "✕ " + (e.message || "Promo check failed"); promoMsg.style.color = "#ff8a8a";
      }
    }
    if(promoInput){
      overlay.querySelector("[data-promo-apply]").addEventListener("click", applyPromo);
      promoInput.addEventListener("keydown", e=>{ if(e.key === "Enter"){ e.preventDefault(); applyPromo(); } });
    }

    confirm.addEventListener("click", async ()=>{
      // Accounts are mandatory \u2014 there is no guest checkout. The worker rejects anonymous
      // invoices with 401 ACCOUNT_REQUIRED, so anyone signed out goes to the portal instead.
      const fresh = account();
      if(fresh) useAccount(fresh);
      if(!accountMode || !isEmail(resolvedAccountEmail)){ location.href = accountUrl(); return; }
      if(needsVerify){ showVerifyMsg(); return; }
      const email = resolvedAccountEmail;
      const discord = (discordInput.value||"").trim();
      if(!payCurrency && currentTotal > 0){ msg.textContent="Please select BTC, LTC or ETH."; msg.style.color="#ff8a8a"; return; }
      confirm.disabled = true;
      confirm.textContent = "Creating payment…";
      msg.textContent = "";
      msg.style.color = "#9aa0a6";
      emailInput.style.borderColor = "#2a2e33";
      discordInput.style.borderColor = "#2a2e33";
      try{
        const result = await opts.onConfirm({
          email, discord, payCurrency,
          promoCode: appliedPromo ? appliedPromo.code : undefined,
          setMsg:(t,k)=>{ msg.textContent=t; msg.style.color = k==="err" ? "#ff8a8a" : k==="ok" ? "#8ee0a0" : "#9aa0a6"; }
        });
        // A test code can cover the whole amount. The Worker then creates NO payment and answers
        // {free:true}; there is nothing to poll, so say exactly that and stop.
        if(result && result.free){
          const orderId = result.orderId || "";
          const purchaseId = result.purchaseId || "";
          const promoCode = result.promoApplied || (appliedPromo && appliedPromo.code) || "";
          msg.style.color = "#8ee0a0";
          showResult(`
            <div style="padding:12px;border:1px solid #2a7a3a;border-radius:8px;background:rgba(46,160,67,.08)">
              <div style="font:10px 'DM Mono',monospace;letter-spacing:.1em;text-transform:uppercase;color:#8ee0a0;margin-bottom:8px">✓ Order placed — no payment required</div>
              <div style="font-size:12px;line-height:1.6;color:#e8eaed">A 100% promo covered the whole amount, so no crypto payment was created and nothing is charged.</div>
              <div style="margin-top:8px;display:grid;gap:4px;font-size:11px;color:#9aa0a6">
                <div><span style="color:#6f7277">Order ID:</span> <code style="color:#c8d0d8;font-size:10px;word-break:break-all">${esc(orderId)}</code></div>
                ${purchaseId?`<div><span style="color:#6f7277">Purchase ID:</span> <code style="color:#c8d0d8;font-size:10px">${esc(purchaseId)}</code></div>`:``}
                ${promoCode?`<div><span style="color:#6f7277">Promo:</span> <b style="color:#fff">${esc(promoCode)}</b></div>`:``}
              </div>
              <div style="margin-top:10px;font-size:10px;line-height:1.6;color:#7a7d82">It is recorded in your account — the portal shows it like any other order.</div>
              <div style="margin-top:10px"><button type="button" data-close-payment style="padding:10px 14px;border-radius:6px;border:1px solid #333;background:transparent;color:#fff;font-size:11px;cursor:pointer">Close</button></div>
            </div>`);
          confirm.textContent = "Order placed ✓";
          confirm.style.display = "none";
          const closeFree = msg.querySelector("[data-close-payment]");
          if(closeFree) closeFree.addEventListener("click", close);
          return;
        }
        // Payment API returns payment data (payAddress, payAmount) — show address + poll for verified payment before redirect
        if(result && (result.payAddress || result.pay_address)){
          const addr=result.payAddress||result.pay_address;
          const amt=result.payAmount||result.pay_amount||"";
          const cur=(result.payCurrency||result.pay_currency||payCurrency||"").toUpperCase();
          const orderId=result.orderId||result.order_id||"";
          const paymentId=result.paymentId||result.payment_id||result.invoiceId||"";
          msg.style.color="#c8d0d8";
          showResult(`
            <div style="padding:12px;border:1px solid #2a7a3a;border-radius:8px;background:rgba(46,160,67,.08)">
              <div style="font:10px 'DM Mono',monospace;letter-spacing:.1em;text-transform:uppercase;color:#8ee0a0;margin-bottom:8px">✓ Payment created — send exact amount</div>
              <div style="font-size:13px;font-weight:700;letter-spacing:-.02em;color:#fff">Send <span style="color:#8ee0a0">${esc(String(amt))} ${esc(cur)}</span> to:</div>
              <div style="margin-top:8px;display:flex;gap:8px;align-items:stretch">
                <code data-pay-addr style="flex:1;display:block;padding:10px 12px;background:#111214;border:1px solid #2a2e33;border-radius:6px;color:#fff;font-size:11px;word-break:break-all;user-select:all">${esc(addr)}</code>
                <button type="button" data-copy-addr style="padding:0 14px;border-radius:6px;border:1px solid #2a2e33;background:#1a1d20;color:#fff;font-size:11px;font-weight:600;cursor:pointer;white-space:nowrap">Copy</button>
              </div>
              <div style="margin-top:10px;display:grid;gap:6px;font-size:11px;line-height:1.5;color:#9aa0a6">
                <div><span style="color:#6f7277">Order ID:</span> <code style="color:#c8d0d8;font-size:10px;word-break:break-all">${esc(orderId)}</code> <button type="button" data-copy-order style="margin-left:6px;padding:2px 6px;border-radius:4px;border:1px solid #2a2e33;background:#111214;color:#7a7d82;font-size:10px;cursor:pointer">copy</button></div>
                ${paymentId?`<div><span style="color:#6f7277">Payment ID:</span> <code style="color:#c8d0d8;font-size:10px">${esc(String(paymentId))}</code></div>`:``}
                <div><span style="color:#6f7277">Amount:</span> <b style="color:#fff">${esc(String(amt))} ${esc(cur)}</b> <span style="color:#6f7277">· Price: ${esc(eur(result.priceAmount ?? opts.amount))}${result.discount?` (${esc(String(result.promoApplied||"promo"))} −${esc(eur(result.discount))})`:``}</span></div>
              </div>
              <div style="margin-top:10px;font-size:10px;line-height:1.6;color:#7a7d82">Send <b style="color:#c8d0d8">exactly</b> that amount — network fee on top. Keep this tab open — you'll be redirected automatically once NOWPayments confirms the payment (IPN: <code>api.get-aether.de/api/ipn</code>). Success page is only reachable after verified payment.</div>
              <div data-pay-status style="margin-top:12px;padding:10px 12px;border:1px solid #2a2e33;border-radius:6px;background:#111214;color:#9aa0a6;font-size:11px;line-height:1.6">⏳ Waiting for payment — checking every 8s…<br><span style="color:#7a7d82">Status: <b data-status-text style="color:#c8d0d8">waiting</b> · Checks: <span data-checks>0</span></span></div>
              <div style="margin-top:10px;display:flex;gap:8px;flex-wrap:wrap">
                <button type="button" data-check-now style="flex:1;min-width:140px;padding:10px 14px;border-radius:6px;background:#fff;color:#000;font-size:11px;font-weight:700;cursor:pointer">Check status now</button>
                <button type="button" data-close-payment style="padding:10px 14px;border-radius:6px;border:1px solid #333;background:transparent;color:#fff;font-size:11px;cursor:pointer">Close</button>
              </div>
              <div style="margin-top:8px;font:10px 'DM Mono',monospace;color:#60636a">Payment ID ${esc(String(paymentId))} — this page will auto-redirect only after payment is <b style="color:#8ee0a0">finished/confirmed</b>.</div>
            </div>`);
          confirm.textContent="Payment created ✓ — waiting for confirmation";
          confirm.disabled=true;
          confirm.style.display="none";
          const copyAddrBtn = msg.querySelector('[data-copy-addr]');
          const copyOrderBtn = msg.querySelector('[data-copy-order]');
          const addrEl = msg.querySelector('[data-pay-addr]');
          function flash(btn, t){ const orig=btn.textContent; btn.textContent=t; setTimeout(()=> btn.textContent=orig, 1200); }
          if(copyAddrBtn && addrEl){
            copyAddrBtn.addEventListener('click', async ()=>{ try{ await navigator.clipboard.writeText(addr); flash(copyAddrBtn,'Copied ✓'); }catch{ const r=document.createRange(); r.selectNodeContents(addrEl); const s=window.getSelection(); s.removeAllRanges(); s.addRange(r); flash(copyAddrBtn,'Selected — Ctrl+C'); }});
          }
          if(copyOrderBtn){
            copyOrderBtn.addEventListener('click', async ()=>{ try{ await navigator.clipboard.writeText(orderId); flash(copyOrderBtn,'Copied ✓'); }catch{ flash(copyOrderBtn,'Copied'); }});
          }
          if(navigator.clipboard) navigator.clipboard.writeText(addr).catch(()=>{});
          // polling — only redirect after verified payment
          let checks=0; let pollTimer=null; let stopped=false;
          const statusEl = msg.querySelector('[data-pay-status]');
          const statusText = msg.querySelector('[data-status-text]');
          const checksEl = msg.querySelector('[data-checks]');
          const checkBtn = msg.querySelector('[data-check-now]');
          async function checkOnce(){
            if(!paymentId){ statusEl.innerHTML='No payment_id — cannot verify. Save Order ID: <code>'+esc(orderId)+'</code>'; return; }
            checkBtn.disabled=true; checkBtn.textContent='Checking…';
            try{
              const u = apiUrl('/api/payment/' + encodeURIComponent(paymentId));
              if(!u) throw new Error('API not configured');
              const res = await fetch(u, { headers:{'accept':'application/json'}});
              const t = await res.text(); let j; try{ j=JSON.parse(t);}catch{ j={raw:t}; }
              if(!res.ok) throw new Error(j.error || `Status ${res.status}`);
              checks++; if(checksEl) checksEl.textContent=String(checks);
              const s=String(j.status||'').toLowerCase();
              if(statusText) statusText.textContent=s||'unknown';
              if(j.isPaid){
                statusEl.style.borderColor='#2a7a3a'; statusEl.style.background='rgba(46,160,67,.08)';
                statusEl.innerHTML=`✅ Payment <b style="color:#8ee0a0">${esc(s)}</b> — verified! Redirecting to success page…<br><span style="color:#7a7d82">Order ${esc(orderId)} · ${esc(String(j.pay_amount||amt))} ${esc(cur)}</span>`;
                clearInterval(pollTimer); stopped=true; checkBtn.disabled=true; checkBtn.textContent='Verified ✓';
                setTimeout(()=>{ const url=SUCCESS_URL+`?order_id=${encodeURIComponent(orderId)}&payment_id=${encodeURIComponent(paymentId)}&pay=${encodeURIComponent(cur)}&amount=${encodeURIComponent(String(j.pay_amount||amt))}&status=${encodeURIComponent(s)}`; location.href=url; }, 1200);
                return;
              } else if(j.isFailed){
                statusEl.style.borderColor='#7a2a2a'; statusEl.style.background='rgba(122,42,42,.12)';
                statusEl.innerHTML=`❌ Payment <b style="color:#ff8a8a">${esc(s)}</b> — failed/expired.<br><span style="color:#7a7d82">Order ${esc(orderId)} — you were not charged for failed payments. Try again or contact questions@get-aether.de</span><br><a href="${esc(CANCEL_URL+`?order_id=${encodeURIComponent(orderId)}&payment_id=${encodeURIComponent(paymentId)}`)}" style="display:inline-block;margin-top:8px;padding:6px 10px;border:1px solid #333;border-radius:4px;color:#fff;text-decoration:none;font-size:10px">Go to cancel page</a>`;
                clearInterval(pollTimer); stopped=true; checkBtn.disabled=false; checkBtn.textContent='Check status now';
                return;
              } else {
                statusEl.style.borderColor='#2a2e33'; statusEl.style.background='#111214';
                statusEl.innerHTML=`⏳ Status: <b style="color:#ffcc00">${esc(s||'waiting')}</b> — still waiting for confirmations…<br><span style="color:#7a7d82">Checks: ${checks} · Send exactly ${esc(String(amt))} ${esc(cur)} to the address above. Auto-checks every 8s.</span>`;
                checkBtn.disabled=false; checkBtn.textContent='Check status now';
              }
            }catch(e){
              statusEl.innerHTML=`⚠️ Check failed: ${esc(e.message)}<br><span style="color:#7a7d82">Will retry automatically. Payment ID: ${esc(String(paymentId))}</span>`;
              checkBtn.disabled=false; checkBtn.textContent='Check status now';
            }
          }
          checkBtn.addEventListener('click', checkOnce);
          pollTimer=setInterval(()=>{ if(!stopped) checkOnce(); }, 8000);
          setTimeout(checkOnce, 4000);
          const closeBtn = msg.querySelector('[data-close-payment]');
          if(closeBtn) closeBtn.addEventListener('click', ()=>{ clearInterval(pollTimer); stopped=true; close(); });
          overlay.addEventListener('click', (e)=>{ if(e.target===overlay){ clearInterval(pollTimer); }});
          document.addEventListener('keydown', function onEsc(e){ if(e.key==='Escape'){ clearInterval(pollTimer); document.removeEventListener('keydown', onEsc);} });
          return;
        }
        if(result && result.invoiceUrl){
          msg.textContent = "Redirecting…";
          msg.style.color = "#8ee0a0";
          location.href = result.invoiceUrl;
          return;
        }
        if(result && result.ok){
          close();
        }
      } catch(e){
        confirm.disabled = false;
        confirm.textContent = needsVerify ? "Confirm your email first" : "Pay with Crypto →";
        // The Worker names this refusal; turn it into the button that fixes it.
        if(e && e.code === "EMAIL_UNVERIFIED"){ needsVerify = true; showVerifyMsg(); return; }
        msg.textContent = e.message || "Something went wrong";
        msg.style.color = "#ff8a8a";
      }
    });
    return { close, overlay };
  }

  // --- tiny UI helpers shared by every form on the site --------------------------------
  const ui = {
    // Puts a button into a busy state without losing its label.
    loading(btn, on, label){
      if(!btn) return;
      if(on){
        if(!btn.dataset.label) btn.dataset.label = btn.textContent;
        btn.disabled = true; btn.classList.add("is-loading");
        if(label) btn.textContent = label;
      } else {
        btn.disabled = false; btn.classList.remove("is-loading");
        if(btn.dataset.label) btn.textContent = btn.dataset.label;
      }
    },
    // Sets an inline message: kind = "err" | "ok" | "info" | "" (neutral)
    setMsg(el, text, kind){
      if(!el) return;
      el.textContent = text || "";
      el.classList.remove("err", "ok", "info");
      if(kind) el.classList.add(kind);
    },
    fieldError(input, message){
      if(!input) return;
      if(message){ input.setAttribute("aria-invalid", "true"); input.classList.add("has-error"); }
      else { input.removeAttribute("aria-invalid"); input.classList.remove("has-error"); }
    },
    toast(message, kind){
      let host = document.getElementById("aether-toast-host");
      if(!host){
        host = document.createElement("div");
        host.id = "aether-toast-host";
        host.className = "toast-host";
        document.body.appendChild(host);
      }
      const t = document.createElement("div");
      t.className = "toast" + (kind ? " " + kind : "");
      t.setAttribute("role", kind === "err" ? "alert" : "status");
      t.textContent = message;
      host.appendChild(t);
      requestAnimationFrame(()=> t.classList.add("is-in"));
      setTimeout(()=>{ t.classList.remove("is-in"); setTimeout(()=> t.remove(), 320); }, 4200);
    },
  };

  window.AetherCheckout = {
    API, CURRENCY, SUCCESS_URL, CANCEL_URL, CROSS_ORIGIN, IS_LOCAL,
    apiUrl, postJson, apiFetch, esc, isEmail, isConfigured, getToken, setToken, turnstile, ui,
    account, setAccount, accountEmail, accountDiscord, accountUrl, accountRequiredNotice, loadAccount, resolveEmail, bindAccountFields,
    showPayModal,
    // Account portal helpers — used by account.html and admin.html
    auth: {
      getToken, setToken,
      async register(email, password, discord){
        const url = apiUrl("/api/auth/register");
        if(!url) throw new Error("Worker not configured");
        const r = await postJson(url, { email, password, discord });
        if(r && r.token) setToken(r.token);
        if(r && r.user) setAccount({ id:r.user.id, email:r.user.email, discord:r.user.discord||"", isAdmin:!!r.isAdmin, role:r.role||r.user.role||"user", emailVerified:!!r.emailVerified });
        return r;
      },
      async login(email, password){
        const url = apiUrl("/api/auth/login");
        if(!url) throw new Error("Worker not configured");
        const r = await postJson(url, { email, password });
        if(r && r.token) setToken(r.token);
        if(r && r.user) setAccount({ id:r.user.id, email:r.user.email, discord:r.user.discord||"", isAdmin:!!r.isAdmin, role:r.role||r.user.role||"user", emailVerified:!!r.emailVerified });
        return r;
      },
      async logout(){
        try{ await postJson(apiUrl("/api/auth/logout"), {}); }catch{}
        setToken("");
        setAccount(null);
      },
      // Password reset — the Worker always answers the same way, so this cannot be used to
      // discover which email addresses have an account.
      forgotPassword(email){ return postJson(apiUrl("/api/auth/forgot"), { email }); },
      async resetPassword(token, password){
        const r = await postJson(apiUrl("/api/auth/reset"), { token, password });
        if(r && r.token) setToken(r.token);
        if(r && r.user) setAccount({ id:r.user.id, email:r.user.email, discord:r.user.discord||"", isAdmin:!!r.isAdmin, role:r.role||r.user.role||"user", emailVerified:!!r.emailVerified });
        return r;
      },
      changePassword(currentPassword, newPassword){
        return apiFetch("/api/auth/password", { method:"POST", body:{ currentPassword, newPassword } });
      },
      verifyEmail(token){ return postJson(apiUrl("/api/auth/verify-email"), { token }); },
      // Session-bound: the server emails the confirmation link to the signed-in account only.
      resendVerification(){ return apiFetch("/api/auth/resend-verification", { method:"POST", body:{} }); },
      me(){ return apiFetch("/api/me"); },
      orders(){ return apiFetch("/api/orders"); },
      order(orderId){ return apiFetch("/api/orders/" + encodeURIComponent(orderId)); },
      sendMessage(orderId, body){ return apiFetch("/api/orders/" + encodeURIComponent(orderId) + "/message", { method:"POST", body:{ body } }); },
      deleteAccount(){ return apiFetch("/api/me", { method:"DELETE" }); },
    },
    // Customer portal: purchases (Purchase IDs are identifiers, never credentials) and the
    // conversation history. Ownership is decided server-side from the session.
    portal: {
      purchases(){ return apiFetch("/api/orders"); },
      purchase(purchaseId){ return apiFetch("/api/purchases/" + encodeURIComponent(purchaseId)); },
      conversations(){ return apiFetch("/api/conversations"); },
      conversation(id){ return apiFetch("/api/conversations/" + encodeURIComponent(id)); },
      newConversation(payload){
        const p = payload || {};
        return apiFetch("/api/conversations", { method:"POST", body:{ subject:p.subject||"", message:p.message||"", purchase_id:p.purchaseId||p.purchase_id||"" } });
      },
      sendMessage(id, body){ return apiFetch("/api/conversations/" + encodeURIComponent(id) + "/messages", { method:"POST", body:{ body } }); },
      setStatus(id, status){ return apiFetch("/api/conversations/" + encodeURIComponent(id) + "/status", { method:"POST", body:{ status } }); },
      // The Beta state below is server-provided; the backend independently re-checks the role.
      betaAccess(){ return apiFetch("/api/beta/access"); },
      betaStatus(){ return apiFetch("/api/beta/status"); },
      betaFeedback(area, message){ return apiFetch("/api/beta/feedback", { method:"POST", body:{ area, message } }); },
      betaTicket(){ return apiFetch("/api/beta/ticket", { method:"POST", body:{} }); },
      betaRedeem(ticket){ return postJson(apiUrl("/api/beta/redeem"), { ticket }); },
      // Program builds. The server holds the file and decides access; these two calls never receive
      // a durable URL, only a single-use ticket that expires in 120 seconds.
      programInfo(){ return apiFetch("/api/program"); },
      programDownload(){ return apiFetch("/api/program/download", { method:"POST", body:{} }); },
    },
    // Admin helpers — only work when the signed-in account is listed in ADMIN_EMAILS
    admin: {
      orders(query){ return apiFetch("/api/admin/orders" + (query || "")); },
      order(orderId){ return apiFetch("/api/admin/orders/" + encodeURIComponent(orderId)); },
      users(){ return apiFetch("/api/admin/users"); },
      messages(){ return apiFetch("/api/admin/messages"); },
      reply(orderId, body){ return apiFetch("/api/admin/orders/" + encodeURIComponent(orderId) + "/message", { method:"POST", body:{ body } }); },
      deleteUser(id){ return apiFetch("/api/admin/users/" + encodeURIComponent(id), { method:"DELETE" }); },
      // Server-side role change. "admin" is never grantable through the API.
      setRole(id, role){ return apiFetch("/api/admin/users/" + encodeURIComponent(id) + "/role", { method:"POST", body:{ role } }); },
      conversations(){ return apiFetch("/api/admin/conversations"); },
      conversation(id){ return apiFetch("/api/admin/conversations/" + encodeURIComponent(id)); },
      conversationReply(id, body){ return apiFetch("/api/admin/conversations/" + encodeURIComponent(id) + "/messages", { method:"POST", body:{ body } }); },
      conversationStatus(id, status){ return apiFetch("/api/admin/conversations/" + encodeURIComponent(id) + "/status", { method:"POST", body:{ status } }); },
      beta(){ return apiFetch("/api/admin/beta"); },
      // Sensitive: needs the admin password now and a confirmed, expiring, single-use token.
      requestBetaDomain(host, path, currentPassword){ return apiFetch("/api/admin/beta/domain/request", { method:"POST", body:{ host, path, currentPassword } }); },
      confirmBetaDomain(token){ return apiFetch("/api/admin/beta/domain/confirm", { method:"POST", body:{ token } }); },
      setBetaFlags(flags){ return apiFetch("/api/admin/beta/flags", { method:"POST", body:{ flags } }); },
      audit(){ return apiFetch("/api/admin/audit"); },
    },
    async createInvoice(payload){
      const url = apiUrl("/api/invoice");
      if(!url) throw new Error("Checkout is offline — set public/aether-config.js → apiBase to your Worker URL (e.g. https://api.get-aether.de). Nothing was charged: email questions@get-aether.de and we will take the order manually.");
      // Keep legacy field name /api/invoice — backend now uses Payment API and expects pay_currency
      return postJson(url, {
        amount: payload.amount,
        currency: payload.currency || CURRENCY,
        pay_currency: payload.pay_currency || payload.payCurrency || payload.coin || "",
        type: payload.type,
        package: payload.package,
        discord: payload.discord,
        email: payload.email,
        description: payload.description,
        meta: payload.meta,
        extra: payload.extra,
        promoCode: payload.promoCode || payload.promo_code || payload.code || undefined,
        promo_code: payload.promo_code || payload.promoCode || payload.code || undefined,
      });
    },
    validatePromo(code, amount){ return promoLookup(code, amount); },
    // Alias used by new code
    async createPayment(payload){ return this.createInvoice(payload); },
    async submitOrder(payload){
      const url = apiUrl("/api/order");
      if(!url) return null;
      return postJson(url, payload);
    },
    // Marks the current page as the active nav item (used by the shared header).
    markActiveNav(){
      try{
        const here = location.pathname.split("/").pop() || "index.html";
        document.querySelectorAll(".nav a").forEach(a => {
          const href = (a.getAttribute("href") || "").split("#")[0];
          if(href && href === here){ a.classList.add("is-active"); a.setAttribute("aria-current", "page"); }
        });
      }catch{}
    },
    mailtoFallback({ subject, body }){
      const url = `mailto:questions@get-aether.de?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;
      window.location.href = url;
      if(navigator.clipboard) navigator.clipboard.writeText(body).catch(()=>{});
    }
  };
})();
