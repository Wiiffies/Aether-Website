const API = "https://api.get-aether.de";
const email = "unicode.probe@example.com";
const password = "unicode-probe-1";

async function post(path, body, token) {
  const headers = { "content-type": "application/json" };
  if (token) headers.authorization = "Bearer " + token;
  const res = await fetch(API + path, { method: "POST", headers, body: JSON.stringify(body) });
  const text = await res.text();
  let json; try { json = JSON.parse(text); } catch { json = { raw: text }; }
  return { status: res.status, json };
}

const describe = (s) => JSON.stringify(s) + "  hex=" + [...s].map(c => c.codePointAt(0).toString(16)).join(" ");

const DISCORD = "caf\u00e9 \u2014 \u00fcn\u00efcode \u2713";
const TITLE = "R\u00e9sum\u00e9 site \u2014 \u20ac40";
const MESSAGE = 'Em dash \u2014 euro \u20ac \u2014 quotes \u201clike this\u201d \u2014 arrow \u2192 \u2713';

const reg = await post("/api/auth/register", { email, password, discord: DISCORD });
console.log("register:", reg.status, reg.json.ok ? "ok" : JSON.stringify(reg.json));
const token = reg.json.token;

const inv = await post("/api/invoice", {
  amount: 40, pay_currency: "btc", email, discord: DISCORD,
  type: "website", package: "TEST", description: TITLE,
}, token);
console.log("invoice:", inv.status, inv.json.ok ? ("ok orderId=" + inv.json.orderId) : JSON.stringify(inv.json));
const orderId = inv.json.orderId;

const msg = await post(`/api/orders/${orderId}/message`, { body: MESSAGE }, token);
const stored = (msg.json.messages || [])[0] || {};
console.log("sent    :", describe(MESSAGE));
console.log("returned:", describe(String(stored.body || "")));
console.log("MATCH   :", stored.body === MESSAGE ? "YES - no mojibake" : "NO - MISMATCH");

const det = await fetch(`${API}/api/orders/${orderId}`, { headers: { authorization: "Bearer " + token } });
const detJson = await det.json();
console.log("description round-trip MATCH:", detJson.order && detJson.order.description === TITLE ? "YES" : "NO -> " + JSON.stringify(detJson.order && detJson.order.description));

const del = await fetch(`${API}/api/me`, { method: "DELETE", headers: { authorization: "Bearer " + token } });
console.log("account deleted:", del.status);
console.log("ORDERID=" + orderId);
