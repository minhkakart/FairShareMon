import { describe, expect, it } from "vitest";
import viShare from "@/i18n/locales/vi-VN/share.json";
import enShare from "@/i18n/locales/en-US/share.json";

/**
 * i18n parity — the `share` namespace must exist in BOTH vi-VN (authoritative)
 * and en-US with the exact same key shape (no missing / no extra keys) and no
 * empty leaves, so no share surface (owner dialog OR public report) falls back to
 * a raw key or the wrong language. Structural test over the JSON catalogs, mirrors
 * `walletI18n.test.ts`. Also guards the vi-VN fixed domain terms + the
 * interpolation tokens (`{{name}}`/`{{time}}`/`{{amount}}`) against drift.
 */

/** All leaf key paths of a nested translation object, dot-joined + sorted. */
function leafKeys(obj: unknown, prefix = ""): string[] {
  if (obj === null || typeof obj !== "object") return [prefix];
  return Object.entries(obj as Record<string, unknown>)
    .flatMap(([key, value]) => leafKeys(value, prefix ? `${prefix}.${key}` : key))
    .sort();
}

/** Map of leaf-key-path → its string value. */
function leaves(obj: unknown, prefix = ""): Record<string, string> {
  if (obj === null || typeof obj !== "object") {
    return { [prefix]: String(obj) };
  }
  return Object.entries(obj as Record<string, unknown>).reduce<
    Record<string, string>
  >((acc, [key, value]) => {
    Object.assign(acc, leaves(value, prefix ? `${prefix}.${key}` : key));
    return acc;
  }, {});
}

/** The `{{token}}` interpolation names in a string, sorted + de-duped. */
function tokens(value: string): string[] {
  return [...value.matchAll(/\{\{\s*([\w]+)\s*\}\}/g)]
    .map((m) => m[1])
    .sort();
}

describe("share i18n parity", () => {
  it("ShareNamespace_ViAndEn_HaveIdenticalKeyShape", () => {
    expect(leafKeys(enShare)).toEqual(leafKeys(viShare));
  });

  it("ShareNamespace_NoLeafIsEmpty_InEitherLocale", () => {
    for (const value of Object.values(leaves(viShare))) {
      expect(value.trim()).not.toBe("");
    }
    for (const value of Object.values(leaves(enShare))) {
      expect(value.trim()).not.toBe("");
    }
  });

  it("ShareNamespace_InterpolationTokens_MatchAcrossLocales", () => {
    // A translator dropping `{{name}}`/`{{time}}`/`{{amount}}` would silently
    // break the rendered copy — assert per-key token sets are identical.
    const vi = leaves(viShare);
    const en = leaves(enShare);
    for (const key of Object.keys(vi)) {
      expect(tokens(en[key])).toEqual(tokens(vi[key]));
    }
  });

  it("ShareNamespace_FixedDomainTerms_UseViVnCopy", () => {
    // Fixed domain terms per CLAUDE.md: share = "phần gánh", settled = "đã trả",
    // wallet/bank account = "Ví", Premium kept as-is. Guard against drift to
    // voucher/record/batch.
    expect(viShare.action.share).toBe("Chia sẻ");
    expect(viShare.public.statusSettled).toBe("Đã trả");
    expect(viShare.breakdown.settledTag).toBe("đã trả");
    expect(viShare.breakdown.title).toContain("phần gánh");
    expect(viShare.premium.gateTitle).toContain("Premium");
    expect(viShare.create.noBankHint).toContain("Ví");
  });

  it("ShareNamespace_EnUs_UsesEnglishCopy", () => {
    // A cheap guard that the en-US file is not a copy of vi-VN.
    expect(enShare.action.share).toBe("Share");
    expect(enShare.public.statusSettled).toBe("Settled");
    expect(enShare.expired.title).toBe("Link unavailable");
  });
});
