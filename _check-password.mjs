// _check-password.mjs — offline helper: does a candidate password match an
// Aether `password_hash` (PBKDF2-SHA256, 100000 iterations, "salt.hash" base64)?
//
// Why this exists: the hash stored in D1 is one-way, so a forgotten password
// cannot be read back. The only useful question is "does this candidate match?",
// and this script answers it locally. It never touches the network and never
// writes anything — read the hash (a single `users.password_hash` value) into a
// local, untracked file or pass it as an argument.
//
// It is a diagnostic, not a login path: the site's own login/reset flow stays in
// worker/src/index.js. Keep this script's constants in sync with
// PBKDF2_ITERATIONS there.
//
// Usage:
//   node _check-password.mjs --hash <salt.hash> candidate [candidate ...]
//   AETHER_HASH=<salt.hash> node _check-password.mjs candidate1 candidate2
//   printf '%s\n' guess1 guess2 | node _check-password.mjs --hash <salt.hash>
//   node _check-password.mjs --hash <salt.hash> --stdin < guesses.txt
//   node _check-password.mjs --selftest
//
// Exit codes: 0 = a candidate matched, 1 = none matched, 2 = usage error.
// Candidates are never echoed back — only their position in the list.
//
// Security notes:
//   - Pass the hash via --hash or AETHER_HASH, never commit it to the repo.
//   - Prefer piping candidates in over the command line when others may see the
//     process list; either way they stay on this machine.
//   - Verifying a candidate here does not log you in. Log in through the site.

import { pbkdf2Sync, randomBytes, timingSafeEqual } from "node:crypto";
import { readFileSync } from "node:fs";

const ITERATIONS = 100000; // must equal PBKDF2_ITERATIONS in worker/src/index.js
const KEY_BYTES = 32; // the Worker derives 32*8 bits
const DIGEST = "sha256";

function b64(buf) {
  return Buffer.from(buf).toString("base64");
}

// Same shape the Worker produces: base64(salt) + "." + base64(derivedKey)
function hashPassword(password, salt) {
  const bits = pbkdf2Sync(Buffer.from(String(password), "utf8"), salt, ITERATIONS, KEY_BYTES, DIGEST);
  return b64(salt) + "." + bits.toString("base64");
}

function verifyPassword(password, stored) {
  try {
    const [saltB64, hashB64] = String(stored).split(".");
    if (!saltB64 || !hashB64) return false;
    const salt = Buffer.from(saltB64, "base64");
    const expected = Buffer.from(hashB64, "base64");
    if (!salt.length || expected.length !== KEY_BYTES) return false;
    const actual = pbkdf2Sync(Buffer.from(String(password), "utf8"), salt, ITERATIONS, expected.length, DIGEST);
    if (actual.length !== expected.length) return false;
    return timingSafeEqual(actual, expected);
  } catch {
    return false;
  }
}

// The Worker uses SubtleCrypto.deriveBits; prove this script's node:crypto path
// produces byte-identical output for the same salt and password, so a "no match"
// here really means "no match" there too.
async function selftest() {
  const fails = [];
  const ok = (name, cond) => {
    if (!cond) fails.push(name);
    console.log((cond ? "  ok   " : "  FAIL ") + name);
  };

  const sample = "correct horse battery staple";
  const salt = randomBytes(16);
  const stored = hashPassword(sample, salt);

  ok("format is salt.hash", /^[A-Za-z0-9+/=]+\.[A-Za-z0-9+/=]+$/.test(stored));
  ok("salt is 16 bytes", Buffer.from(stored.split(".")[0], "base64").length === 16);
  ok("hash is 32 bytes", Buffer.from(stored.split(".")[1], "base64").length === 32);
  ok("correct password matches", verifyPassword(sample, stored) === true);
  ok("wrong password does not", verifyPassword("Correct horse battery staple", stored) === false);
  ok("empty password does not", verifyPassword("", stored) === false);
  ok("malformed hash does not", verifyPassword(sample, "not-a-hash") === false);
  ok("truncated hash does not", verifyPassword(sample, stored.split(".")[0]) === false);
  ok("salt-only hash does not", verifyPassword(sample, stored.split(".")[0] + ".") === false);
  ok("deterministic per salt", hashPassword(sample, salt) === stored);

  if (globalThis.crypto && globalThis.crypto.subtle) {
    const key = await globalThis.crypto.subtle.importKey(
      "raw",
      new TextEncoder().encode(sample),
      "PBKDF2",
      false,
      ["deriveBits"]
    );
    const bits = await globalThis.crypto.subtle.deriveBits(
      { name: "PBKDF2", salt, iterations: ITERATIONS, hash: "SHA-256" },
      key,
      32 * 8
    );
    const viaWebCrypto = b64(salt) + "." + b64(bits);
    ok("matches SubtleCrypto.deriveBits byte for byte", viaWebCrypto === stored);
  } else {
    console.log("  skip  SubtleCrypto cross-check (no globalThis.crypto in this Node)");
  }

  if (fails.length) {
    console.log("\nselftest FAILED: " + fails.length + " check(s)");
    return 1;
  }
  console.log("\nselftest passed");
  return 0;
}

function usage(msg) {
  if (msg) console.error("error: " + msg);
  console.error(
    [
      "usage: node _check-password.mjs --hash <salt.hash> [candidate ...]",
      "       AETHER_HASH=<salt.hash> node _check-password.mjs [candidate ...]",
      "       node _check-password.mjs --hash <salt.hash> --stdin < guesses.txt",
      "       node _check-password.mjs --selftest",
      "",
      "Reads candidates from arguments, or from stdin when none are given.",
      "Exit: 0 = matched, 1 = no match, 2 = usage error.",
    ].join("\n")
  );
  return 2;
}

async function main() {
  const argv = process.argv.slice(2);
  const candidates = [];
  let stored = process.env.AETHER_HASH || "";
  let useStdin = false;

  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === "--selftest") return selftest();
    if (arg === "--help" || arg === "-h") return usage();
    if (arg === "--stdin") {
      useStdin = true;
      continue;
    }
    if (arg === "--hash") {
      stored = argv[++i] || "";
      continue;
    }
    if (arg.startsWith("--hash=")) {
      stored = arg.slice("--hash=".length);
      continue;
    }
    if (arg === "--") {
      candidates.push(...argv.slice(i + 1));
      break;
    }
    if (arg.startsWith("-") && arg !== "-") return usage("unknown option " + arg);
    candidates.push(arg);
  }

  if (!candidates.length) useStdin = true;

  if (useStdin) {
    const input = readFileSync(0, "utf8");
    for (const line of input.split(/\r?\n/)) {
      if (line !== "") candidates.push(line);
    }
  }

  if (!candidates.length) return usage("no candidate passwords given");
  if (!stored) return usage("no password hash given (--hash or AETHER_HASH)");

  const [, hashB64] = String(stored).split(".");
  if (!hashB64 || Buffer.from(hashB64, "base64").length !== KEY_BYTES) {
    return usage("--hash must look like base64(salt).base64(32-byte-pbkdf2-hash)");
  }

  console.log(
    "checking " +
      candidates.length +
      " candidate(s) against PBKDF2-SHA256/" +
      ITERATIONS +
      " (offline, nothing is written)"
  );

  let matched = 0;
  for (let i = 0; i < candidates.length; i++) {
    if (verifyPassword(candidates[i], stored)) {
      matched++;
      console.log("  #" + (i + 1) + " MATCH");
    } else {
      console.log("  #" + (i + 1) + " no match");
    }
  }

  if (matched) {
    console.log("\n" + matched + " of " + candidates.length + " matched. Log in through the site's normal flow.");
    return 0;
  }
  console.log("\nNo candidate matched. Either the password is different, or reset it instead.");
  return 1;
}

process.exit(await main());
