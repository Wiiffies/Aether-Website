/* Aether support chat — the launcher in the corner of every page.
   Standalone on purpose: it has to work on the pages that never load checkout.js (home, about,
   contact, maintenance, error pages). It talks to the same account-scoped endpoints the portal uses
   and the server decides everything: which conversations exist, who may read them, and whether a
   reply is allowed. Nothing in this file is a security boundary — hiding or editing it changes
   nothing, because every request is checked against the session.

   State (panel open, which thread you were in) lives in localStorage, so a reload, a page change or
   coming back tomorrow lands you in the same conversation instead of a blank box. */
(function(){
  if (window.__aetherChatLoaded) return;
  window.__aetherChatLoaded = true;
  // The pages load this with `defer`, so the body already exists. The fallback is here anyway: a
  // script tag without defer must wait rather than throw, and re-running itself is the honest way
  // to do that without wrapping the whole file in a function.
  if (!document.body) {
    var SELF = (document.currentScript && document.currentScript.src) || "";
    document.addEventListener("DOMContentLoaded", function(){
      window.__aetherChatLoaded = false;
      if (!SELF) return;
      var s = document.createElement("script"); s.src = SELF; s.defer = true; document.head.appendChild(s);
    }, { once:true });
    return;
  }
  if (document.documentElement.hasAttribute("data-no-chat") || document.body.hasAttribute("data-no-chat")) return;

  var CFG = window.AETHER_CONFIG || {};
  var RAW_API = String(CFG.apiBase == null ? "" : CFG.apiBase).replace(/\/+$/, "");
  var IS_LOCAL = /^(localhost|127\.0\.0\.1|\[::1\])$/i.test(location.hostname) || location.protocol === "file:";
  var API = RAW_API || (IS_LOCAL ? String(CFG.devApiBase || "").replace(/\/+$/, "") : "");
  var CSS_HREF = "public/chat.css?v=1";
  var STATE_KEY = "aether_chat_state";
  var TOKEN_KEY = "aether_token";
  var POLL_MS = 12000;

  function apiUrl(path){ if (API) return API + path; return location.protocol === "file:" ? null : path; }
  function getToken(){ try { return sessionStorage.getItem(TOKEN_KEY) || ""; } catch { return ""; } }
  function esc(s){ return String(s == null ? "" : s).replace(/[&<>"']/g, function(c){ return {"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]; }); }
  function when(ts){
    if (!ts && ts !== 0) return "";
    var d = new Date(typeof ts === "number" ? ts : String(ts));
    if (isNaN(d)) return String(ts);
    return d.toLocaleString("en-GB", { dateStyle:"medium", timeStyle:"short" });
  }
  function api(path, opts){
    var url = apiUrl(path);
    if (!url) return Promise.reject(new Error("The Aether API is not reachable from this page - open the site over https://get-aether.de"));
    var o = Object.assign({ method:"GET", credentials:"include" }, opts || {});
    var headers = Object.assign({ "accept":"application/json" }, (opts && opts.headers) || {});
    var t = getToken();
    if (t) headers.authorization = "Bearer " + t;
    if (o.body && typeof o.body !== "string") { o.body = JSON.stringify(o.body); headers["content-type"] = "application/json"; }
    o.headers = headers;
    return fetch(url, o).then(function(res){
      return res.text().then(function(text){
        var j; try { j = JSON.parse(text); } catch { j = { raw:text }; }
        if (!res.ok) {
          var e = new Error((j && (j.error || j.message)) || ("Request failed (" + res.status + ")"));
          e.status = res.status; e.code = (j && j.code) || "";
          throw e;
        }
        return j;
      });
    });
  }
  function loadState(){ try { return JSON.parse(localStorage.getItem(STATE_KEY) || "{}") || {}; } catch { return {}; } }
  function saveState(patch){ try { var s = Object.assign(loadState(), patch); localStorage.setItem(STATE_KEY, JSON.stringify(s)); } catch {} }

  // ---------- chrome ----------
  function injectCss(){
    if (document.getElementById("aether-chat-css")) return;
    var l = document.createElement("link");
    l.id = "aether-chat-css"; l.rel = "stylesheet"; l.href = CSS_HREF;
    document.head.appendChild(l);
  }
  injectCss();

  var launcher = document.createElement("button");
  launcher.type = "button";
  launcher.className = "ac-launch";
  launcher.id = "aether-chat-launcher";
  launcher.setAttribute("aria-expanded", "false");
  launcher.setAttribute("aria-controls", "aether-chat-panel");
  launcher.innerHTML = '<span class="ac-dot" aria-hidden="true"></span><span>Support</span><span class="ac-count" hidden></span>';

  var panel = document.createElement("section");
  panel.className = "ac-panel";
  panel.id = "aether-chat-panel";
  panel.setAttribute("role", "dialog");
  panel.setAttribute("aria-label", "Aether support chat");
  panel.hidden = true;

  document.body.appendChild(launcher);
  document.body.appendChild(panel);

  // ---------- state ----------
  var state = loadState();
  var convId = String(state.convId || "");
  var signedIn = null;           // null = not asked yet
  var conversations = [];
  var messages = [];
  var current = null;
  var pollTimer = null;

  function setPanelChrome(open){
    panel.hidden = !open;
    launcher.setAttribute("aria-expanded", open ? "true" : "false");
    saveState({ open: !!open });
  }

  function badge(n){
    var el = launcher.querySelector(".ac-count");
    if (!el) return;
    if (n > 0) { el.textContent = String(n > 9 ? "9+" : n); el.hidden = false; }
    else { el.hidden = true; el.textContent = ""; }
  }

  function statusLabel(status){
    var s = String(status || "open").toLowerCase();
    if (s === "closed") return { text:"Resolved", cls:"resolved" };
    if (s === "answered") return { text:"We replied", cls:"answered" };
    return { text:"Waiting for a reply", cls:"waiting" };
  }

  function head(title, sub, opts){
    var o = opts || {};
    return '<div class="ac-head"><div>'
      + '<div class="ac-kicker">Aether support</div>'
      + '<h3>' + esc(title) + '</h3>'
      + (sub ? '<p class="ac-sub">' + esc(sub) + '</p>' : '')
      + '</div><button type="button" class="ac-x" data-chat-close aria-label="Close the chat panel (nothing is lost)">&#10005;</button></div>'
      + (o.back ? '<div class="ac-head" style="border-top:0;padding-top:0;border-bottom:1px solid #1d1f23"><button type="button" class="ac-btn ghost" data-chat-list>&larr; All conversations</button><span></span></div>' : '');
  }

  function renderSignIn(){
    panel.innerHTML = head("Support chat", "Your conversations live in your account.")
      + '<div class="ac-body">'
      + '<div class="ac-note">Sign in and the chat is yours: every thread is tied to your account server-side, so nobody else can open it - not even by changing a link. Orders and invoices sit next to it in the same place.</div>'
      + '<a class="ac-btn" style="display:block;text-align:center;text-decoration:none" href="' + esc(accountUrl()) + '">Sign in / create an account</a>'
      + '<div class="ac-note">Prefer email? <a class="ac-link" href="mailto:questions@get-aether.de">questions@get-aether.de</a>.</div>'
      + '</div>';
  }

  function renderList(){
    var items = conversations.map(function(c){
      var st = statusLabel(c.status);
      var title = c.subject || "Conversation";
      var meta = [];
      if (c.purchase_id) meta.push(c.purchase_id);
      meta.push(st.text);
      return '<button type="button" class="ac-item" data-chat-open="' + esc(c.conversation_id) + '">'
        + '<span class="t">' + esc(title) + ' <span class="ac-badge ' + st.cls + '">' + esc(st.text) + '</span></span>'
        + (c.last_body ? '<span class="m">' + esc(String(c.last_body).slice(0, 120)) + '</span>' : '')
        + '<span class="d">' + esc(meta.join(" · ")) + (c.last_at || c.updated_at ? ' · ' + esc(when(c.last_at || c.updated_at)) : '') + '</span>'
        + '</button>';
    }).join("");
    panel.innerHTML = head("Your conversations", "Pick up where you left off, or start something new.")
      + '<div class="ac-body">'
      + (items ? '<div class="ac-list">' + items + '</div>' : '<div class="ac-empty">No conversations yet. Anything you write here reaches us, and the answer stays in this thread.</div>')
      + '<button type="button" class="ac-btn" data-chat-new>New conversation</button>'
      + '<div class="ac-note">Open, waiting and resolved threads are all kept - closing this panel never loses one, and they are on your account page too.</div>'
      + '</div>';
  }

  function renderThread(){
    var c = current || {};
    var st = statusLabel(c.status);
    var msgs = messages.map(function(m){
      var mine = String(m.sender || "").toLowerCase() !== "admin";
      return '<div class="ac-msg ' + (mine ? "mine" : "theirs") + '">' + esc(m.body || "")
        + '<span class="meta">' + esc(mine ? "You" : "Aether") + (m.created_at ? " · " + esc(when(m.created_at)) : "") + '</span></div>';
    }).join("");
    var closed = String(c.status || "").toLowerCase() === "closed";
    panel.innerHTML = head(c.subject || "Conversation", [st.text, c.purchase_id ? "Purchase " + c.purchase_id : ""].filter(Boolean).join(" · "), { back:true })
      + '<div class="ac-body">'
      + '<div class="ac-thread">' + (msgs || '<div class="ac-empty">No messages yet.</div>') + '</div>'
      + (closed
        ? '<div class="ac-note">This conversation is resolved. Start a new one if you need anything else.</div>'
        : '<div class="ac-field"><label class="ac-label" for="aether-chat-text">Reply</label>'
          + '<textarea class="ac-textarea" id="aether-chat-text" placeholder="Add details or ask for an update..."></textarea></div>'
          + '<div class="ac-row"><button type="button" class="ac-btn" data-chat-send>Send message</button>'
          + '<button type="button" class="ac-btn ghost" data-chat-resolve>Mark as resolved</button></div>')
      + '<div class="ac-note" data-chat-msg role="status" aria-live="polite"></div>'
      + '</div>';
    var ta = panel.querySelector("#aether-chat-text");
    if (ta) ta.focus();
  }

  function renderNew(){
    panel.innerHTML = head("New conversation", "We answer here and by email.")
      + '<div class="ac-body">'
      + '<div class="ac-field"><label class="ac-label" for="aether-chat-subject">Subject</label>'
      + '<input class="ac-input" id="aether-chat-subject" maxlength="120" placeholder="What is this about?"></div>'
      + '<div class="ac-field"><label class="ac-label" for="aether-chat-first">Message</label>'
      + '<textarea class="ac-textarea" id="aether-chat-first" placeholder="Tell us what you need and we reply here (and by email)."></textarea></div>'
      + '<div class="ac-row"><button type="button" class="ac-btn" data-chat-create>Start conversation</button>'
      + '<button type="button" class="ac-btn ghost" data-chat-list>Cancel</button></div>'
      + '<div class="ac-note" data-chat-msg role="status" aria-live="polite"></div>'
      + '</div>';
    var ta = panel.querySelector("#aether-chat-first");
    if (ta) ta.focus();
  }

  function renderNotice(title, text, kind){
    panel.innerHTML = head(title || "Support chat", "Aether support")
      + '<div class="ac-body"><div class="ac-note ' + (kind || "") + '">' + esc(text) + '</div>'
      + (signedIn ? '<button type="button" class="ac-btn ghost" data-chat-list>Back to conversations</button>' : '')
      + '</div>';
  }

  function setMsg(text, kind){
    var el = panel.querySelector("[data-chat-msg]");
    if (!el) return;
    el.className = "ac-note" + (kind ? " " + kind : "");
    el.textContent = text || "";
  }

  function stopPoll(){ if (pollTimer) { clearInterval(pollTimer); pollTimer = null; } }

  // ---------- data ----------
  function loadList(){
    return api("/api/conversations").then(function(r){
      conversations = (r && r.conversations) || [];
      // "Waiting for a reply" is the state we owe the customer, so that is what the little badge
      // counts - and only while the panel is shut, because inside it the list says it already.
      var waiting = conversations.filter(function(c){ return String(c.status || "open").toLowerCase() === "open"; }).length;
      badge(panel.hidden ? waiting : 0);
      return conversations;
    });
  }
  function loadThread(id){
    return api("/api/conversations/" + encodeURIComponent(id)).then(function(r){
      current = (r && r.conversation) || {};
      messages = (r && r.messages) || [];
      return current;
    });
  }
  function showList(){
    convId = ""; saveState({ convId:"" });
    stopPoll();
    setPanelChrome(true);
    panel.innerHTML = head("Your conversations", "Loading...") + '<div class="ac-body"><div class="ac-empty">Loading your conversations...</div></div>';
    if (signedIn === false) { renderSignIn(); return; }
    loadList().then(function(){ renderList(); }).catch(handleError);
  }
  function showThread(id, opts){
    convId = String(id || "");
    saveState({ convId: convId });
    setPanelChrome(true);
    panel.innerHTML = head("Conversation", "Loading...") + '<div class="ac-body"><div class="ac-empty">Loading the thread...</div></div>';
    loadThread(convId).then(function(){ renderThread(); })
      .catch(function(e){
        if (e && e.status === 404) { convId = ""; saveState({ convId:"" }); return showList(); }
        handleError(e);
      });
    if (!(opts && opts.noPoll)) {
      stopPoll();
      pollTimer = setInterval(function(){
        if (panel.hidden || !convId) return;
        loadThread(convId).then(function(){ if (!panel.hidden && convId) renderThread(); }).catch(function(){});
      }, POLL_MS);
    }
  }
  function handleError(e){
    if (e && (e.status === 401 || e.code === "SESSION_REQUIRED")) { signedIn = false; renderSignIn(); return; }
    if (e && e.code === "EMAIL_UNVERIFIED") { renderNotice("Confirm your email", e.message, "err"); return; }
    if (e && /ACCOUNT_BANNED|ACCOUNT_SUSPENDED|ACCOUNT_RESTRICTED/.test(e.code || "")) { renderNotice("Support chat", e.message, "err"); return; }
    renderNotice("Support chat", (e && e.message) || "The chat could not be loaded. Write to questions@get-aether.de and we will help from there.", "err");
  }
  function openPanel(){
    setPanelChrome(true);
    api("/api/me").then(function(me){
      signedIn = !!(me && me.authenticated);
      if (!signedIn) { renderSignIn(); return; }
      if (convId) showThread(convId); else showList();
    }).catch(function(e){
      if (e && e.status === 401) { signedIn = false; renderSignIn(); return; }
      if (e && e.code) { handleError(e); return; }
      renderNotice("Support chat", "The chat is unavailable right now. You can always reach us at questions@get-aether.de" + (location.origin ? " or try again in a moment." : "."), "err");
    });
  }

  function accountUrl(){
    var back = /account\.html$/.test(location.pathname) ? "" : ("?next=" + encodeURIComponent(location.pathname + location.search + location.hash));
    return "account.html" + back;
  }

  // ---------- events ----------
  launcher.addEventListener("click", function(){
    if (!panel.hidden) { setPanelChrome(false); return; }
    openPanel();
  });
  document.addEventListener("keydown", function(e){
    if (e.key === "Escape" && !panel.hidden) setPanelChrome(false);
  });
  panel.addEventListener("click", function(e){
    var t = e.target;
    if (t.closest("[data-chat-close]")) { setPanelChrome(false); return; }
    if (t.closest("[data-chat-list]")) { showList(); return; }
    if (t.closest("[data-chat-new]")) { renderNew(); return; }
    var openBtn = t.closest("[data-chat-open]");
    if (openBtn) { showThread(openBtn.getAttribute("data-chat-open")); return; }
    if (t.closest("[data-chat-send]")) { sendReply(t.closest("[data-chat-send]")); return; }
    if (t.closest("[data-chat-create]")) { createConversation(t.closest("[data-chat-create]")); return; }
    if (t.closest("[data-chat-resolve]")) { resolveConversation(t.closest("[data-chat-resolve]")); return; }
  });

  function sendReply(btn){
    var ta = panel.querySelector("#aether-chat-text");
    var text = ta ? String(ta.value || "").trim() : "";
    if (text.length < 2) { setMsg("Write a slightly longer message.", "err"); return; }
    btn.disabled = true; setMsg("Sending...");
    api("/api/conversations/" + encodeURIComponent(convId) + "/messages", { method:"POST", body:{ body:text } })
      .then(function(r){ messages = (r && r.messages) || messages; if (ta) ta.value = ""; renderThread(); setMsg("Sent - we reply here and by email.", "ok"); })
      .catch(function(e){ setMsg((e && e.message) || "Could not send that message.", "err"); })
      .then(function(){ btn.disabled = false; });
  }
  function createConversation(btn){
    var subj = panel.querySelector("#aether-chat-subject");
    var body = panel.querySelector("#aether-chat-first");
    var text = body ? String(body.value || "").trim() : "";
    if (text.length < 2) { setMsg("Write a slightly longer first message.", "err"); return; }
    btn.disabled = true; setMsg("Starting...");
    api("/api/conversations", { method:"POST", body:{ subject: subj ? String(subj.value || "").trim() : "", message:text } })
      .then(function(r){ var c = (r && r.conversation) || {}; return showThread(c.conversation_id); })
      .catch(function(e){ setMsg((e && e.message) || "Could not start the conversation.", "err"); btn.disabled = false; });
  }
  function resolveConversation(btn){
    if (!confirm("Mark this conversation as resolved? You can always start a new one.")) return;
    btn.disabled = true;
    api("/api/conversations/" + encodeURIComponent(convId) + "/status", { method:"POST", body:{ status:"closed" } })
      .then(function(){ return loadThread(convId); })
      .then(function(){ renderThread(); })
      .catch(function(e){ setMsg((e && e.message) || "Could not update the conversation.", "err"); btn.disabled = false; });
  }

  // ---------- boot ----------
  // The launcher is on every page; the API is only asked once, quietly, so the badge can tell a
  // signed-in customer that something is waiting for them without costing every visitor a request.
  setTimeout(function(){
    if (signedIn !== null) return;
    api("/api/me").then(function(me){
      if (!me || !me.authenticated) return;
      signedIn = true;
      loadList().catch(function(){});
    }).catch(function(){});
  }, 1500);

  if (state.open) openPanel();
})();
