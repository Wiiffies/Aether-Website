import { NowPaymentsSDK } from "@nowpaymentsio/nowpayments-sdk-nodejs";
import {
  cleanText,
  cleanAndCensor,
  censorText,
  configureModeration
} from "../../../worker/moderation.js";

const VALID_FILTERS = new Set(["plain", "glow", "sunset", "terminal"]);
const MAX_BODY_BYTES = 16 * 1024;

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff"
    }
  });
}

function sameOrigin(request) {
  const origin = request.headers.get("Origin");
  if (!origin) return false;
  try {
    return origin === new URL(request.url).origin;
  } catch {
    return false;
  }
}

function clientIp(request) {
  return request.headers.get("CF-Connecting-IP") || "unknown";
}

async function checkRateLimit(env, ip) {
  if (!env.DONATIONS) return true;
  const minute = Math.floor(Date.now() / 60000);
  const key = `rate:${ip}:${minute}`;
  const count = Number(await env.DONATIONS.get(key) || 0);
  if (count >= 5) return false;
  await env.DONATIONS.put(key, String(count + 1), { expirationTtl: 120 });
  return true;
}

async function verifyTurnstile(env, token, ip) {
  // Turnstile is opt-in so the checkout still works when only the Worker
  // secrets are configured. Enable it with REQUIRE_TURNSTILE=true after the
  // public site key is set in config.js.
  if (env.REQUIRE_TURNSTILE !== "true") return true;
  if (!env.TURNSTILE_SECRET_KEY) return false;
  if (!token) return false;

  const body = new URLSearchParams();
  body.set("secret", env.TURNSTILE_SECRET_KEY);
  body.set("response", token);
  if (ip && ip !== "unknown") body.set("remoteip", ip);

  try {
    const response = await fetch("https://challenges.cloudflare.com/turnstile/v0/siteverify", {
      method: "POST",
      body
    });
    const result = await response.json();
    return result && result.success === true;
  } catch {
    return false;
  }
}

function validateBody(body) {
  if (!body || typeof body !== "object") return { error: "Invalid request." };

  const amount = Number(body.amount);
  if (!Number.isFinite(amount) || amount < 3 || amount > 500) {
    return { error: "Choose an amount between $3 and $500." };
  }

  const rawUsername = cleanText(body.username, 24);
  if (rawUsername.length < 2) return { error: "Use a display name with at least 2 characters." };
  if (!/^[\p{L}\p{N}_. -]+$/u.test(rawUsername)) {
    return { error: "The display name contains unsupported characters." };
  }

  const message = cleanAndCensor(body.message, 140);
  if (message.length < 2) return { error: "Write a message with at least 2 characters." };

  const filter = VALID_FILTERS.has(body.filter) ? body.filter : "plain";
  const showOnWall = body.showOnWall !== false;
  return {
    value: {
      amount: Math.round(amount * 100) / 100,
      username: censorText(rawUsername),
      message,
      filter,
      showOnWall
    }
  };
}

export async function onRequestPost({ request, env }) {
  if (!sameOrigin(request)) return json({ error: "Requests must come from the Aether site." }, 403);

  const contentType = request.headers.get("Content-Type") || "";
  if (!contentType.toLowerCase().includes("application/json")) {
    return json({ error: "Send JSON." }, 415);
  }

  const declaredLength = Number(request.headers.get("Content-Length") || 0);
  if (declaredLength > MAX_BODY_BYTES) return json({ error: "Request is too large." }, 413);

  let body;
  try {
    const raw = await request.text();
    if (raw.length > MAX_BODY_BYTES) return json({ error: "Request is too large." }, 413);
    body = JSON.parse(raw);
  } catch {
    return json({ error: "Invalid JSON." }, 400);
  }

  if (body && body.company) return json({ error: "The form could not be submitted." }, 400);

  configureModeration(env.CHAT_FILTER_TERMS);
  const validated = validateBody(body);
  if (validated.error) return json({ error: validated.error }, 400);

  if (!(await checkRateLimit(env, clientIp(request)))) {
    return json({ error: "Too many attempts. Wait a minute and try again." }, 429);
  }

  if (!(await verifyTurnstile(env, body.turnstileToken, clientIp(request)))) {
    return json({ error: "Cloudflare could not verify this browser." }, 403);
  }

  if (!env.NOWPAYMENTS_API_KEY) {
    return json({ error: "Crypto checkout is not configured yet." }, 503);
  }

  const origin = env.PUBLIC_ORIGIN || new URL(request.url).origin;
  const orderId = "AETH-" + Date.now().toString(36).toUpperCase() + "-" + crypto.randomUUID().slice(0, 8).toUpperCase();
  const callback = origin.replace(/\/$/, "") + "/api/nowpayments/ipn";
  const successUrl = origin.replace(/\/$/, "") + "/donate.html?status=success&order_id=" + encodeURIComponent(orderId);
  const cancelUrl = origin.replace(/\/$/, "") + "/donate.html?status=cancel";

  const pending = {
    orderId,
    username: validated.value.username,
    message: validated.value.message,
    filter: validated.value.filter,
    amount: validated.value.amount,
    showOnWall: validated.value.showOnWall,
    createdAt: new Date().toISOString(),
    status: "pending"
  };

  if (env.DONATIONS) {
    await env.DONATIONS.put("donation:" + orderId, JSON.stringify(pending), { expirationTtl: 60 * 60 * 24 * 30 });
  }

  try {
    const sdk = new NowPaymentsSDK({
      apiKey: env.NOWPAYMENTS_API_KEY,
      ipnSecret: env.NOWPAYMENTS_IPN_SECRET || undefined,
      ipnCallbackUrl: callback,
      successUrl,
      cancelUrl,
      timeoutMs: 15000,
      // workerd's fetch rejects a URL object; the SDK builds one internally.
      fetch: (input, init) => fetch(input instanceof URL ? input.toString() : input, init)
    });

    const checkout = await sdk.createCheckout({
      amount: validated.value.amount,
      currency: "usd",
      orderId,
      description: "Aether donation from @" + validated.value.username,
      ipnCallbackUrl: callback,
      successUrl,
      cancelUrl
    });

    const invoiceUrl = checkout.invoice_url || checkout.invoiceUrl || "";
    let parsed;
    try {
      parsed = new URL(invoiceUrl);
    } catch {
      return json({ error: "NOWPayments returned no checkout link." }, 502);
    }

    if (parsed.protocol !== "https:" || !/(^|\.)nowpayments\.io$/i.test(parsed.hostname)) {
      return json({ error: "NOWPayments returned an unexpected checkout link." }, 502);
    }

    return json({ invoiceUrl: parsed.toString(), checkoutUrl: parsed.toString(), orderId });
  } catch (error) {
    console.error("NOWPayments checkout failed", error && error.code ? error.code : "unknown");
    return json({ error: "The crypto checkout could not be opened. Try again in a moment." }, 502);
  }
}
