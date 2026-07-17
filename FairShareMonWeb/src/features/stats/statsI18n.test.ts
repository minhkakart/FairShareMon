import { describe, expect, it } from "vitest";
import viStats from "@/i18n/locales/vi-VN/stats.json";
import enStats from "@/i18n/locales/en-US/stats.json";
import viCommon from "@/i18n/locales/vi-VN/common.json";
import enCommon from "@/i18n/locales/en-US/common.json";
import viValidation from "@/i18n/locales/vi-VN/validation.json";
import enValidation from "@/i18n/locales/en-US/validation.json";

/**
 * i18n parity — the new `stats` namespace, the `common:home.*` home additions, and
 * `validation:stats.rangeInvalid` must exist in BOTH vi-VN (authoritative) and
 * en-US with the exact same key shape (no missing, no extra, no empty leaf), so no
 * M6 surface falls back to a raw key or the wrong language. Structural test over
 * the JSON catalogs, independent of any component.
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

describe("stats i18n parity", () => {
  it("StatsNamespace_ViAndEn_HaveIdenticalKeyShape", () => {
    expect(leafKeys(enStats)).toEqual(leafKeys(viStats));
  });

  it("StatsNamespace_NoLeafIsEmpty_InEitherLocale", () => {
    expect(allValues(viStats).every((v) => v.trim() !== "")).toBe(true);
    expect(allValues(enStats).every((v) => v.trim() !== "")).toBe(true);
  });

  it("CommonHome_ViAndEn_HaveIdenticalKeyShape", () => {
    const vi = viCommon as { home: Record<string, unknown> };
    const en = enCommon as { home: Record<string, unknown> };
    expect(leafKeys(en.home)).toEqual(leafKeys(vi.home));
  });

  it("CommonHome_M6Additions_PresentInBothLocales", () => {
    const homeKeys = [
      "overviewTitle",
      "viewStats",
      "categoryBreakdown",
      "categoryEmpty",
      "recentActivity",
      "recentExpensesEmpty",
      "viewAll",
      "quickActions",
      "addExpense",
      "newEvent",
    ];
    const vi = viCommon as unknown as { home: Record<string, string> };
    const en = enCommon as unknown as { home: Record<string, string> };
    for (const k of homeKeys) {
      expect(vi.home[k], `vi-VN home.${k}`).toBeTruthy();
      expect(en.home[k], `en-US home.${k}`).toBeTruthy();
    }
  });

  it("ValidationStats_RangeInvalid_PresentInBothLocales", () => {
    const vi = viValidation as { stats: Record<string, string> };
    const en = enValidation as { stats: Record<string, string> };
    expect(vi.stats.rangeInvalid).toBeTruthy();
    expect(en.stats.rangeInvalid).toBeTruthy();
    expect(leafKeys(en.stats)).toEqual(leafKeys(vi.stats));
  });

  it("StatsNamespace_FixedDomainTerms_UseAuthoritativeViVnCopy", () => {
    // Guard against drift on the fixed domain terms.
    expect(viStats.page.title).toBe("Thống kê");
    expect(viStats.kpi.totalSpending).toBe("Tổng chi tiêu");
    expect(viStats.kpi.expenseCount).toBe("Số phiếu chi tiêu");
    expect(viStats.byCategory.deleted).toBe("(đã xóa)");
    expect(viStats.byCategory.table.category).toBe("Danh mục");
  });
});
