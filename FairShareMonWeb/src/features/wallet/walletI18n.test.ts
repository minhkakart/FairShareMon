import { describe, expect, it } from "vitest";
import viWallet from "@/i18n/locales/vi-VN/wallet.json";
import enWallet from "@/i18n/locales/en-US/wallet.json";
import viValidation from "@/i18n/locales/vi-VN/validation.json";
import enValidation from "@/i18n/locales/en-US/validation.json";

/**
 * i18n parity — the `wallet` namespace and the `validation.bankAccount.*` subtree
 * must exist in BOTH vi-VN (authoritative) and en-US with the exact same key shape
 * (no missing / no extra keys), so no wallet or QR surface falls back to a raw key
 * or the wrong language. Structural test over the JSON catalogs.
 */

/** All leaf key paths of a nested translation object, dot-joined + sorted. */
function leafKeys(obj: unknown, prefix = ""): string[] {
  if (obj === null || typeof obj !== "object") return [prefix];
  return Object.entries(obj as Record<string, unknown>)
    .flatMap(([key, value]) =>
      leafKeys(value, prefix ? `${prefix}.${key}` : key),
    )
    .sort();
}

function allValues(obj: unknown): string[] {
  return obj === null || typeof obj !== "object"
    ? [String(obj)]
    : Object.values(obj as Record<string, unknown>).flatMap(allValues);
}

describe("wallet i18n parity", () => {
  it("WalletNamespace_ViAndEn_HaveIdenticalKeyShape", () => {
    expect(leafKeys(enWallet)).toEqual(leafKeys(viWallet));
  });

  it("WalletNamespace_NoLeafIsEmpty_InEitherLocale", () => {
    expect(allValues(viWallet).every((v) => v.trim() !== "")).toBe(true);
    expect(allValues(enWallet).every((v) => v.trim() !== "")).toBe(true);
  });

  it("ValidationBankAccount_ViAndEn_HaveIdenticalKeyShape", () => {
    const vi = viValidation as Record<string, unknown>;
    const en = enValidation as Record<string, unknown>;
    expect(leafKeys(en.bankAccount)).toEqual(leafKeys(vi.bankAccount));
  });

  it("ValidationBankAccount_CoversEveryRuleTheSchemaReferences", () => {
    // The Zod factory references these keys — both locales must define them.
    const keys = [
      "bankNameRequired",
      "bankNameTooLong",
      "binRequired",
      "binPattern",
      "accountNumberRequired",
      "accountNumberInvalid",
      "holderRequired",
      "holderTooLong",
    ];
    const vi = viValidation as { bankAccount: Record<string, string> };
    const en = enValidation as { bankAccount: Record<string, string> };
    for (const k of keys) {
      expect(vi.bankAccount[k]).toBeTruthy();
      expect(en.bankAccount[k]).toBeTruthy();
    }
  });

  it("WalletNamespace_FixedDomainTerms_UseViVnCopy", () => {
    // Fixed domain terms (ví / mặc định / Premium-Free) per CLAUDE.md — guard
    // against drift to voucher/record/batch.
    expect(viWallet.title).toBe("Ví của tôi");
    expect(viWallet.badge.default).toBe("Mặc định");
    expect(viWallet.tier.premium).toBe("Premium");
    expect(viWallet.tier.free).toBe("Free");
  });
});
