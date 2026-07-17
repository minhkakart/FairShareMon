import { describe, expect, it } from "vitest";
import { generateTempPassword } from "./generatePassword";

/**
 * generateTempPassword (OQ3a) — the client-side strong temp-password generator.
 * Proves: length is clamped to 12–16 (default 14); each password carries ≥1 upper
 * / lower / digit / symbol; ambiguous glyphs (0 O 1 l I o) are excluded for
 * hand-off legibility; it meets the backend policy (≥8 chars, ≤72 bytes); and
 * successive calls differ (CSPRNG-backed, not a constant).
 */

const UPPER = /[A-HJ-NP-Z]/; // excludes I, O
const LOWER = /[a-hj-km-np-z]/; // excludes l, o
const DIGIT = /[2-9]/; // excludes 0, 1
const SYMBOL = /[!@#$%^&*?]/;
const AMBIGUOUS = /[0O1lIo]/;

describe("generateTempPassword", () => {
  it("GenerateTempPassword_Default_Is14Chars", () => {
    expect(generateTempPassword()).toHaveLength(14);
  });

  it("GenerateTempPassword_Length_ClampedTo12To16", () => {
    expect(generateTempPassword(4)).toHaveLength(12);
    expect(generateTempPassword(13)).toHaveLength(13);
    expect(generateTempPassword(99)).toHaveLength(16);
  });

  it("GenerateTempPassword_EveryPassword_HasOneOfEachClass", () => {
    for (let i = 0; i < 100; i += 1) {
      const pw = generateTempPassword();
      expect(UPPER.test(pw), `upper in ${pw}`).toBe(true);
      expect(LOWER.test(pw), `lower in ${pw}`).toBe(true);
      expect(DIGIT.test(pw), `digit in ${pw}`).toBe(true);
      expect(SYMBOL.test(pw), `symbol in ${pw}`).toBe(true);
    }
  });

  it("GenerateTempPassword_NeverContainsAmbiguousGlyphs", () => {
    for (let i = 0; i < 100; i += 1) {
      expect(AMBIGUOUS.test(generateTempPassword())).toBe(false);
    }
  });

  it("GenerateTempPassword_MeetsBackendPolicy_MinCharsMaxBytes", () => {
    const pw = generateTempPassword();
    expect(pw.length).toBeGreaterThanOrEqual(8);
    expect(new TextEncoder().encode(pw).length).toBeLessThanOrEqual(72);
  });

  it("GenerateTempPassword_SuccessiveCalls_Differ", () => {
    const set = new Set(Array.from({ length: 50 }, () => generateTempPassword()));
    // Overwhelmingly unique for a CSPRNG-backed 14-char generator.
    expect(set.size).toBeGreaterThan(45);
  });
});
