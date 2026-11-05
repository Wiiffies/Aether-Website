import { onRequestPost as createDonation } from "../functions/api/donations/index.js";
import { onRequestGet as recentDonations } from "../functions/api/donations/recent.js";
import { onRequestPost as receiveIpn, onRequestGet as rejectIpnGet } from "../functions/api/nowpayments/ipn.js";

function notFound() {
  return new Response(JSON.stringify({ error: "Not found" }), {
    status: 404,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff"
    }
  });
}

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);
    const path = url.pathname.replace(/\/+$/, "") || "/";
    const context = { request, env, ctx };

    if (request.method === "POST" && path === "/api/donations") {
      return createDonation(context);
    }

    if (request.method === "GET" && path === "/api/donations/recent") {
      return recentDonations(context);
    }

    if (request.method === "POST" && path === "/api/nowpayments/ipn") {
      return receiveIpn(context);
    }

    if (request.method === "GET" && path === "/api/nowpayments/ipn") {
      return rejectIpnGet(context);
    }

    return notFound();
  }
};
