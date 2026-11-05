import test from "node:test";
import assert from "node:assert/strict";
import { censorText, cleanAndCensor, configureModeration } from "../worker/moderation.js";
import { enrichDonations } from "../functions/api/donations/recent.js";
import { onRequestPost as previewDonation } from "../functions/api/donations/preview.js";

test("censors the n-word and common evasions", () => {
  const samples = [
    "nigga",
    "nigger",
    "N1GG4",
    "n!gg@",
    "n i g g a",
    "n.i.g.g.a",
    "n-i-g-g-a",
    "n_i_g_g_a",
    "n1663r",
    "ni99a",
    "n1gguh",
    "n1ggaz",
    "n\u200B1gga",
    "ñiggа"
  ];

  for (const sample of samples) {
    const result = censorText(sample);
    assert.match(result, /^\*+$/u, `${sample} -> ${result}`);
  }
});

test("does not censor ordinary words that begin with similar letters", () => {
  assert.equal(censorText("Niger"), "Niger");
  assert.equal(censorText("Nigeria"), "Nigeria");
  assert.equal(censorText("niggardly"), "niggardly");
});

test("masks only the slur and leaves the rest of the message readable", () => {
  assert.equal(censorText("hello n1gga world"), "hello ***** world");
  assert.equal(censorText("keep it clean"), "keep it clean");
});

test("cleans markup and links before applying the filter", () => {
  assert.equal(cleanAndCensor("  hi <b>n1gga</b> https://x.test  ", 140), "hi *****");
});

test("censors phrases supplied by the Worker secret", () => {
  configureModeration("bad phrase\nkkk");
  assert.match(censorText("bad phrase"), /^\*+$/u);
  assert.match(censorText("b a d p h r a s e"), /^\*+$/u);
  assert.equal(censorText("KKK"), "***");
  assert.equal(censorText("a bad idea"), "a bad idea");
  configureModeration([]);
});

test("preview endpoint censors without exposing the rules", async () => {
  const request = new Request("https://get-aether.de/api/donations/preview", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Origin: "https://get-aether.de"
    },
    body: JSON.stringify({ username: "bad phrase", message: "bad phrase" })
  });
  const response = await previewDonation({ request, env: { CHAT_FILTER_TERMS: "bad phrase" } });
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), {
    username: "**********",
    message: "**********"
  });
});

test("sanitises stored wall entries when they are read", () => {
  configureModeration([]);
  const [entry] = enrichDonations([
    {
      username: "n1gga",
      message: "n i g g a",
      filter: "plain",
      amount: 5,
      date: "2026-11-05T00:00:00.000Z"
    }
  ]);

  assert.equal(entry.username, "*****");
  assert.equal(entry.message, "*********");
});
