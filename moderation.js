/**
 * Shared text sanitising and slur censoring.
 *
 * This module is deliberately dependency-free so the exact same rules run in
 * the browser preview and in the Worker. The Worker is always authoritative:
 * client-side filtering is only there to show the donor what will be stored.
 */

const ZERO_WIDTH = /[\u200B-\u200D\uFEFF]/g;
const CONTROL = /[\u0000-\u001F\u007F]/g;

const SEP = "[\\s\\p{P}\\p{S}\\u200B-\\u200D\\uFEFF]{0,4}";
const N = "[nNñÑńŃňŇпП]";
const I = "[iI1!|¡lLìíîïіІι]";
const G = "[gG6qQ9ɡց]";
const VOWEL = "[aA4@àáâäаАαeE3èéêëеЕεuUùúûü]";
const SUFFIX = "[rRhHzZsS2$гГнН]";

const SLUR = new RegExp(
  `${N}+${SEP}(?:${I}${SEP})+(?:${G}${SEP}){2,}${VOWEL}(?:${SEP}${SUFFIX})?(?![\\p{L}\\p{N}\\p{M}])`,
  "giu"
);

const SHORT = new RegExp(
  `\\b${N}+${SEP}(?:${I}${SEP})+(?:${G}${SEP}){2,}(?![\\p{L}\\p{N}\\p{M}])`,
  "giu"
);

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
  return String(value == null ? "" : value)
    .replace(SLUR, (match) => "*".repeat(match.length))
    .replace(SHORT, (match) => "*".repeat(match.length));
}

export function cleanAndCensor(value, max) {
  const cleaned = cleanText(value);
  const censored = censorText(cleaned);
  return max ? censored.slice(0, max) : censored;
}
