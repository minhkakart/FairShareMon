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

/**
 * `stream.*` — the live-update terminal/settled copy added by
 * `public-share-sse-updates.md` (OQ1: distinct copy per reason, deliberately
 * NOT collapsed into the generic pre-load `expired.title/body` no-leak copy).
 * The generic parity/non-empty/token-matching assertions above already cover
 * these keys structurally (they're picked up automatically); this block adds
 * the OQ1-specific regression guard: the three new strings must stay
 * pairwise distinct from each other and from the pre-load `expired.*` copy.
 */
describe("share i18n stream keys (public-share-sse-updates.md)", () => {
  it("ShareStreamKeys_ExistAndAreNonEmpty_InBothLocales", () => {
    for (const locale of [viShare, enShare]) {
      expect(locale.stream.revokedTitle.trim()).not.toBe("");
      expect(locale.stream.revokedBody.trim()).not.toBe("");
      expect(locale.stream.expiredTitle.trim()).not.toBe("");
      expect(locale.stream.expiredBody.trim()).not.toBe("");
      expect(locale.stream.qrMemberSettledTitle.trim()).not.toBe("");
      expect(locale.stream.qrMemberSettledBody.trim()).not.toBe("");
    }
  });

  it("ShareStreamKeys_InterpolationTokens_MatchAcrossLocales", () => {
    // The current draft copy has no interpolation at all — assert both
    // locales agree on that (an empty token set on both sides), so a future
    // edit that adds a token to only one locale is caught here too, not only
    // by the whole-namespace parity test above.
    const keys = [
      "revokedTitle",
      "revokedBody",
      "expiredTitle",
      "expiredBody",
      "qrMemberSettledTitle",
      "qrMemberSettledBody",
    ] as const;
    for (const key of keys) {
      expect(tokens(enShare.stream[key])).toEqual(tokens(viShare.stream[key]));
    }
  });

  it("ShareStreamKeys_RevokedAndExpiredAndQrSettled_ArePairwiseDistinct", () => {
    // Regression guard against accidentally collapsing OQ1's distinct-copy
    // decision back down: revoked/expired/qrMemberSettled must each be a
    // genuinely different string from one another, in both locales.
    for (const locale of [viShare, enShare]) {
      const titles = [
        locale.stream.revokedTitle,
        locale.stream.expiredTitle,
        locale.stream.qrMemberSettledTitle,
      ];
      expect(new Set(titles).size).toBe(titles.length);
      const bodies = [
        locale.stream.revokedBody,
        locale.stream.expiredBody,
        locale.stream.qrMemberSettledBody,
      ];
      expect(new Set(bodies).size).toBe(bodies.length);
    }
  });

  it("ShareStreamKeys_RevokedAndExpired_AreDistinctFromThePreLoadExpiredCopy", () => {
    // The pre-load `expired.title/body` screen deliberately uses IDENTICAL
    // copy for expired/revoked/missing to avoid an existence leak (untouched,
    // locked decision from `event-share-link.md`). The mid-session
    // `stream.revoked*`/`stream.expired*` copy must be textually distinct from
    // it — that's the whole point of OQ1's recommended option (a visitor here
    // already saw a real, loaded report, so naming the reason leaks nothing).
    for (const locale of [viShare, enShare]) {
      expect(locale.stream.revokedTitle).not.toBe(locale.expired.title);
      expect(locale.stream.revokedBody).not.toBe(locale.expired.body);
      expect(locale.stream.expiredTitle).not.toBe(locale.expired.title);
      expect(locale.stream.expiredBody).not.toBe(locale.expired.body);
    }
  });
});
