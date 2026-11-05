import { rankForAmount } from "../../../donation-ranks.js";
import { cleanAndCensor } from "../../../moderation.js";

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

function normalizeName(value) {
  return String(value == null ? "" : value).trim().toLowerCase();
}

function roundMoney(value) {
  return Math.round((Number(value) || 0) * 100) / 100;
}

export function enrichDonations(donations) {
  const list = Array.isArray(donations) ? donations : [];
  const totals = new Map();

  list.forEach((donation) => {
    const key = normalizeName(donation && donation.username) || "anonymous";
    totals.set(key, roundMoney((totals.get(key) || 0) + (Number(donation && donation.amount) || 0)));
  });

  return list.slice(0, 12).map((donation) => {
    const key = normalizeName(donation.username) || "anonymous";
    const total = totals.get(key) || 0;
    const rank = rankForAmount(total);
    return {
      username: cleanAndCensor(donation && donation.username, 24),
      message: cleanAndCensor(donation && donation.message, 140),
      filter: donation.filter,
      amount: donation.amount,
      total,
      rank: { id: rank.id, label: rank.label },
      date: donation.date
    };
  });
}

export async function onRequestGet({ env }) {
  if (!env.DONATIONS) return json({ donations: [] });

  try {
    const donations = await env.DONATIONS.get("donations:recent:v1", "json");
    return json({ donations: enrichDonations(donations) });
  } catch {
    return json({ donations: [] });
  }
}
