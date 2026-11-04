/**
 * Donation ranks are based on the confirmed USD total shown on the public wall.
 * Keep this in sync with the rank ladder on donate.html.
 */
export const DONATION_RANKS = Object.freeze([
  Object.freeze({ id: "legend", label: "Legend", min: 100 }),
  Object.freeze({ id: "guardian", label: "Guardian", min: 50 }),
  Object.freeze({ id: "benefactor", label: "Benefactor", min: 25 }),
  Object.freeze({ id: "patron", label: "Patron", min: 10 }),
  Object.freeze({ id: "supporter", label: "Supporter", min: 3 })
]);

export function rankForAmount(amount) {
  const value = Number(amount);
  const normalized = Number.isFinite(value) ? Math.max(0, value) : 0;
  return DONATION_RANKS.find((rank) => normalized >= rank.min) || DONATION_RANKS[DONATION_RANKS.length - 1];
}
