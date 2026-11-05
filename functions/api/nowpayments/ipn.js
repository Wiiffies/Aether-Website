import { verifyWebhookSignature } from "@nowpaymentsio/nowpayments-sdk-nodejs";
import { cleanAndCensor } from "../../../moderation.js";

const MAX_BODY_BYTES = 64 * 1024;
const PAID_STATUSES = new Set(["confirmed", "finished"]);

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

export async function onRequestPost({ request, env }) {
  if (!env.NOWPAYMENTS_IPN_SECRET) return json({ error: "IPN is not configured." }, 503);

  const signature = request.headers.get("x-nowpayments-sig") || "";
  if (!signature) return json({ error: "Missing signature." }, 401);

  const raw = await request.text();
  if (raw.length > MAX_BODY_BYTES) return json({ error: "Payload is too large." }, 413);

  let payload;
  try {
    payload = JSON.parse(raw);
  } catch {
    return json({ error: "Invalid JSON." }, 400);
  }

  try {
    if (!verifyWebhookSignature(payload, signature, env.NOWPAYMENTS_IPN_SECRET)) {
      return json({ error: "Invalid signature." }, 401);
    }
  } catch {
    return json({ error: "Signature verification failed." }, 401);
  }

  const orderId = String(payload.order_id || "").trim();
  if (!orderId) return json({ ok: true });

  const status = String(payload.payment_status || "").toLowerCase();
  if (!PAID_STATUSES.has(status)) return json({ ok: true });

  if (!env.DONATIONS) return json({ ok: true });

  const key = "donation:" + orderId;
  let pending;
  try {
    pending = await env.DONATIONS.get(key, "json");
  } catch {
    return json({ ok: false }, 500);
  }

  if (!pending || pending.published) return json({ ok: true });

  if (!pending.showOnWall) {
    await env.DONATIONS.put(key, JSON.stringify({ ...pending, status, published: false, paidAt: new Date().toISOString() }), {
      expirationTtl: 60 * 60 * 24 * 30
    });
    return json({ ok: true });
  }

  const entry = {
    username: cleanAndCensor(pending.username, 24) || "Anonymous",
    message: cleanAndCensor(pending.message, 140),
    filter: ["plain", "glow", "sunset", "terminal"].includes(pending.filter) ? pending.filter : "plain",
    amount: Number(pending.amount) || 0,
    date: new Date().toISOString()
  };

  try {
    const recent = await env.DONATIONS.get("donations:recent:v1", "json");
    const list = Array.isArray(recent) ? recent : [];
    const withoutDuplicate = list.filter((item) => item.orderId !== orderId);
    withoutDuplicate.unshift({ ...entry, orderId });
    await env.DONATIONS.put("donations:recent:v1", JSON.stringify(withoutDuplicate.slice(0, 24)));
    await env.DONATIONS.put(key, JSON.stringify({ ...pending, status, published: true, paidAt: entry.date }), {
      expirationTtl: 60 * 60 * 24 * 30
    });
  } catch {
    return json({ ok: false }, 500);
  }

  return json({ ok: true });
}

export async function onRequestGet() {
  return json({ error: "Use POST for NOWPayments IPN." }, 405);
}
