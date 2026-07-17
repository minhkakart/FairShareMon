/**
 * Strong random temp-password generator (M8 OQ3a). Uses `crypto.getRandomValues`
 * (CSPRNG), 12–16 chars with ≥1 of each class (upper / lower / digit / symbol),
 * ambiguous glyphs (0/O, 1/l/I) excluded for hand-off legibility. It meets the
 * backend policy (min 8 chars, ≤72 bytes). The value is created client-side, held
 * in component state only, and never persisted or logged.
 */

const SETS = [
  "ABCDEFGHJKLMNPQRSTUVWXYZ", // upper (no I, O)
  "abcdefghijkmnpqrstuvwxyz", // lower (no l, o)
  "23456789", // digits (no 0, 1)
  "!@#$%^&*?", // symbols
] as const;

const ALL = SETS.join("");

/** A uniform integer in [0, n) from the CSPRNG. */
function randInt(n: number): number {
  const buf = new Uint32Array(1);
  crypto.getRandomValues(buf);
  return buf[0] % n;
}

/** Generate a temp password of `len` chars (default 14, clamped 12–16). */
export function generateTempPassword(len = 14): string {
  const length = Math.min(16, Math.max(12, len));
  // One guaranteed char from each class, then fill from the full alphabet.
  const chars = SETS.map((set) => set[randInt(set.length)]);
  for (let i = chars.length; i < length; i += 1) {
    chars.push(ALL[randInt(ALL.length)]);
  }
  // Fisher–Yates shuffle so the guaranteed chars aren't always in front.
  for (let i = chars.length - 1; i > 0; i -= 1) {
    const j = randInt(i + 1);
    [chars[i], chars[j]] = [chars[j], chars[i]];
  }
  return chars.join("");
}
