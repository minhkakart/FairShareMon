import { describe, expect, it } from "vitest";
import viAdmin from "@/i18n/locales/vi-VN/admin.json";
import enAdmin from "@/i18n/locales/en-US/admin.json";

/**
 * i18n parity — the new `admin` namespace must exist in BOTH vi-VN (authoritative)
 * and en-US with the exact same key shape (no missing, no extra, no empty leaf), so
 * no admin surface falls back to a raw key or the wrong language. Structural test
 * over the JSON catalogs, independent of any component. Also guards the fixed
 * vi-VN domain terms + the validation keys the grant form relies on.
 */

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

describe("admin i18n parity", () => {
  it("AdminNamespace_ViAndEn_HaveIdenticalKeyShape", () => {
    expect(leafKeys(enAdmin)).toEqual(leafKeys(viAdmin));
  });

  it("AdminNamespace_NoLeafIsEmpty_InEitherLocale", () => {
    expect(allValues(viAdmin).every((v) => v.trim() !== "")).toBe(true);
    expect(allValues(enAdmin).every((v) => v.trim() !== "")).toBe(true);
  });

  it("AdminNamespace_ValidationKeys_PresentInBothLocales", () => {
    const vi = viAdmin as { validation: Record<string, string> };
    const en = enAdmin as { validation: Record<string, string> };
    for (const k of [
      "amountRequired",
      "amountNegative",
      "currencyTooLong",
      "referenceTooLong",
      "noteTooLong",
    ]) {
      expect(vi.validation[k], `vi ${k}`).toBeTruthy();
      expect(en.validation[k], `en ${k}`).toBeTruthy();
    }
  });

  it("AdminNamespace_FixedDomainTerms_UseAuthoritativeViVnCopy", () => {
    const vi = viAdmin as unknown as {
      console: { eyebrow: string };
      nav: Record<string, string>;
      tierBadge: Record<string, string>;
      roleBadge: Record<string, string>;
      statusBadge: Record<string, string>;
    };
    expect(vi.console.eyebrow).toBe("Quản trị");
    expect(vi.nav.dashboard).toBe("Bảng chỉ số");
    expect(vi.nav.revenue).toBe("Doanh thu");
    expect(vi.nav.users).toBe("Người dùng");
    // Premium/Free are fixed domain terms — never translated.
    expect(vi.tierBadge.free).toBe("Free");
    expect(vi.tierBadge.premium).toBe("Premium");
    expect(vi.roleBadge.admin).toBe("Quản trị viên");
    expect(vi.statusBadge.disabled).toBe("Đã khóa");
  });

  it("AdminNamespace_EnUs_KeepsPremiumFreeAsFixedTerms", () => {
    const en = enAdmin as unknown as { tierBadge: Record<string, string> };
    expect(en.tierBadge.free).toBe("Free");
    expect(en.tierBadge.premium).toBe("Premium");
  });
});
