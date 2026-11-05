import {
  cleanText,
  censorText,
  cleanAndCensor,
  configureModeration
} from "../../../worker/moderation.js";

const MAX_BODY_BYTES = 8 * 1024;

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

  if (!body || typeof body !== "object" || body.company) {
    return json({ error: "Invalid request." }, 400);
  }

  configureModeration(env.CHAT_FILTER_TERMS);

  return json({
    username: censorText(cleanText(body.username, 24)),
    message: cleanAndCensor(body.message, 140)
  });
}
