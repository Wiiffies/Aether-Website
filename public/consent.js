// Cookie notice + storage choice.
//
// This site runs no analytics, no advertising and no tracking script, so there is nothing that
// has to wait for consent before it runs. What this script does is (a) tell the visitor that in
// plain language on their first visit, (b) record the choice they make so they are not asked
// again, and (c) give them a way to change or withdraw that choice later from any page.
//
// The choice is stored in localStorage under `aether_consent_v1` (a version number, the choice
// and a timestamp - nothing about the visitor) and mirrored in a cookie-free way: no cookie is
// set by this script, so declining genuinely means nothing extra is stored beyond the record of
// the decline itself.
(function () {
  "use strict";

  var KEY = "aether_consent_v1";
  var VERSION = 1;

  function read() {
    try {
      var raw = localStorage.getItem(KEY);
      if (!raw) return null;
      var parsed = JSON.parse(raw);
      if (!parsed || parsed.v !== VERSION) return null;
      return parsed;
    } catch (e) { return null; }
  }

  function write(choice) {
    try {
      localStorage.setItem(KEY, JSON.stringify({ v: VERSION, choice: choice, at: new Date().toISOString() }));
    } catch (e) { /* storage blocked - the notice will simply show again next time */ }
  }

  function clear() {
    try { localStorage.removeItem(KEY); } catch (e) {}
  }

  function css() {
    if (document.getElementById("aether-consent-css")) return;
    var s = document.createElement("style");
    s.id = "aether-consent-css";
    s.textContent = [
      ".acn-wrap{position:fixed;left:14px;right:14px;bottom:14px;z-index:9998;display:flex;justify-content:center;pointer-events:none}",
      ".acn{pointer-events:auto;width:100%;max-width:660px;box-sizing:border-box;padding:16px 18px;border:1px solid #2a2e33;border-radius:14px;background:#0c0c0f;box-shadow:0 18px 50px rgba(0,0,0,.55);font-family:Manrope,Arial,sans-serif}",
      ".acn h2{margin:0 0 8px;font-size:13px;font-weight:700;letter-spacing:-.01em;color:#fff}",
      ".acn p{margin:0 0 12px;font-size:11.5px;line-height:1.75;color:#a2a6ad}",
      ".acn a{color:#fff;text-decoration:underline;text-underline-offset:2px}",
      ".acn a:hover{color:#8ecbff}",
      ".acn-row{display:flex;gap:8px;flex-wrap:wrap}",
      ".acn button{cursor:pointer;border-radius:9px;font:700 11.5px Manrope,Arial,sans-serif;padding:10px 14px}",
      ".acn .primary{border:1px solid #fff;background:#fff;color:#08080a}",
      ".acn .ghost{border:1px solid #2a2e33;background:transparent;color:#c8c9cc;font-weight:600}",
      ".acn .ghost:hover{border-color:#4a4f57;color:#fff}",
      ".acn .acn-fine{margin:10px 0 0;font-size:10.5px;color:#6f7277;line-height:1.7}",
      "@media (max-width:520px){.acn-wrap{left:10px;right:10px;bottom:10px}.acn{padding:14px}}",
    ].join("");
    document.head.appendChild(s);
  }

  function removeNotice() {
    var el = document.getElementById("aether-consent");
    if (el && el.parentNode) el.parentNode.removeChild(el);
  }

  function show() {
    if (document.getElementById("aether-consent")) return;
    css();
    var wrap = document.createElement("div");
    wrap.id = "aether-consent";
    wrap.className = "acn-wrap";
    wrap.setAttribute("role", "dialog");
    wrap.setAttribute("aria-label", "Cookies and storage");
    // Built as DOM nodes, not as an HTML string: nothing here is user input, but keeping the habit
    // means a future edit cannot accidentally turn this into an injection point.
    var box = document.createElement("div");
    box.className = "acn";

    var h = document.createElement("h2");
    h.textContent = "Cookies and storage";
    box.appendChild(h);

    var p = document.createElement("p");
    p.appendChild(document.createTextNode("This site uses one strictly necessary cookie to keep you signed in, and small browser-storage entries for your chat panel and this choice. There is "));
    var b = document.createElement("b");
    b.textContent = "no analytics, no advertising and no tracking";
    p.appendChild(b);
    p.appendChild(document.createTextNode(" — so there is nothing to switch off. Read the "));
    p.appendChild(link("Cookie Policy", "cookies.html"));
    p.appendChild(document.createTextNode(" and the "));
    p.appendChild(link("Privacy Policy", "privacy.html"));
    p.appendChild(document.createTextNode("."));
    box.appendChild(p);

    var row = document.createElement("div");
    row.className = "acn-row";
    row.appendChild(button("Accept all", "primary", function () { write("all"); removeNotice(); }));
    row.appendChild(button("Essential only", "ghost", function () { write("essential"); removeNotice(); }));
    row.appendChild(linkButton("Cookie settings", function () { location.href = "cookies.html"; }));
    box.appendChild(row);

    var fine = document.createElement("p");
    fine.className = "acn-fine";
    fine.textContent = "Both options behave identically on this site. Your answer is only recorded so you are not asked again, and you can change it at any time.";
    box.appendChild(fine);

    wrap.appendChild(box);
    document.body.appendChild(wrap);
  }

  function link(text, href) {
    var a = document.createElement("a");
    a.textContent = text;
    a.href = href;
    return a;
  }

  function button(text, cls, onClick) {
    var el = document.createElement("button");
    el.type = "button";
    el.className = cls;
    el.textContent = text;
    el.addEventListener("click", onClick);
    return el;
  }

  function linkButton(text, onClick) {
    var el = document.createElement("button");
    el.type = "button";
    el.className = "ghost";
    el.textContent = text;
    el.addEventListener("click", onClick);
    return el;
  }

  // Any element with data-cookie-settings reopens the choice, so it is reachable from the footer
  // and from the policy pages even after the notice was answered.
  document.addEventListener("click", function (ev) {
    var el = ev.target && ev.target.closest ? ev.target.closest("[data-cookie-settings]") : null;
    if (!el) return;
    ev.preventDefault();
    clear();
    show();
  });

  function boot() {
    if (!read()) show();
  }
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
  else boot();
})();
