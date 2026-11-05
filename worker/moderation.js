/**
 * Server-only text sanitising and slur censoring.
 *
 * The browser never imports this module. The built-in rules catch the n-word
 * and common evasions; the extended phrase list is supplied by the
 * CHAT_FILTER_TERMS Worker secret so it is not shipped in the public site.
 */

const ZERO_WIDTH = /[\u200B-\u200D\uFEFF]/g;
const CONTROL = /[\u0000-\u001F\u007F]/g;

// Separators allow a term to be broken up with spaces, dots, dashes,
// punctuation, symbols and zero-width characters.
const SEP = "[\\s\\p{P}\\p{S}\\u200B-\\u200D\\uFEFF]{0,4}";

// Common leetspeak and homoglyph substitutions. The classes stay wide enough
// to catch n1gga, n!gg@, n1663r, n i g g a, n.i.g.g.a and similar variants.
const N = "[nNñÑńŃňŇпП]";
const I = "[iI1!|¡lLìíîïіІι]";
const G = "[gG6qQ9ɡց]";
const VOWEL = "[aA4@àáâäаАαeE3èéêëеЕεuUùúûü]";
const SUFFIX = "[rRhHzZsS2$гГнН]";

const SLUR = new RegExp(
  `${N}+${SEP}(?:${I}${SEP})+(?:${G}${SEP}){2,}${VOWEL}(?:${SEP}${SUFFIX})?(?![\\p{L}\\p{N}\\p{M}])`,
  "giu"
);

// Also catch the common standalone truncation ("nigg", "n1gg"), but only when
// it is the whole token so normal words such as "niggardly" stay intact.
const SHORT = new RegExp(
  `\\b${N}+${SEP}(?:${I}${SEP})+(?:${G}${SEP}){2,}(?![\\p{L}\\p{N}\\p{M}])`,
  "giu"
);

let termPatterns = [];

function escapeRegex(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function parseTerms(value) {
  if (Array.isArray(value)) return value;
  if (typeof value !== "string" || !value.trim()) return [];

  try {
    const parsed = JSON.parse(value);
    if (Array.isArray(parsed)) return parsed;
  } catch {
    // A newline-separated list is also valid.
  }

  return value.split(/\r?\n/);
}

function termPattern(term) {
  const normalized = String(term == null ? "" : term)
    .normalize("NFKC")
    .toLowerCase()
    .replace(/[^\p{L}\p{N}]+/gu, "");

  if (normalized.length < 3 || normalized.length > 80) return null;

  const body = Array.from(normalized).map(escapeRegex).join(SEP);
  return new RegExp(`(?<![\\p{L}\\p{N}])${body}(?![\\p{L}\\p{N}])`, "giu");
}

/**
 * Load the extended term list for the current request. The secret is shared by
 * every request in an isolate, so this is safe to call at the start of each
 * handler and keeps the list out of the browser bundle.
 */
export function configureModeration(value) {
  termPatterns = parseTerms(value).map(termPattern).filter(Boolean);
}

export function cleanText(value, max) {
  let text = String(value == null ? "" : value);
  if (text.normalize) text = text.normalize("NFKC");
  text = text
    .replace(/<[^>]*>/g, " ")
    .replace(/https?:\/\/\S+/gi, " ")
    .replace(/www\.\S+/gi, " ")
    .replace(ZERO_WIDTH, "")
    .replace(CONTROL, " ")
    .replace(/\s+/g, " ")
    .trim();
  return max ? text.slice(0, max) : text;
}

export function censorText(value) {
  let result = String(value == null ? "" : value)
    .replace(SLUR, (match) => "*".repeat(match.length))
    .replace(SHORT, (match) => "*".repeat(match.length));

  for (const pattern of termPatterns) {
    result = result.replace(pattern, (match) => "*".repeat(match.length));
  }

  return result;
}

export function cleanAndCensor(value, max) {
  const cleaned = cleanText(value);
  const censored = censorText(cleaned);
  return max ? censored.slice(0, max) : censored;
}
