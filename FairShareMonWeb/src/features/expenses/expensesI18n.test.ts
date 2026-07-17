import { describe, expect, it } from "vitest";
import viExpenses from "@/i18n/locales/vi-VN/expenses.json";
import enExpenses from "@/i18n/locales/en-US/expenses.json";
import viValidation from "@/i18n/locales/vi-VN/validation.json";
import enValidation from "@/i18n/locales/en-US/validation.json";

/**
 * i18n parity — the `expenses` namespace and the `validation.expense.*` /
 * `validation.share.*` subtrees must exist in BOTH vi-VN (authoritative) and en-US
 * with the exact same key shape (no missing / no extra keys), so no surface falls
 * back to a raw key or the wrong language. This is a structural test over the JSON
 * catalogs, independent of any single component.
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

describe("expenses i18n parity", () => {
  it("ExpensesNamespace_ViAndEn_HaveIdenticalKeyShape", () => {
    expect(leafKeys(enExpenses)).toEqual(leafKeys(viExpenses));
  });

  it("ExpensesNamespace_NoLeafIsEmpty_InEitherLocale", () => {
    const allValues = (obj: unknown): string[] =>
      obj === null || typeof obj !== "object"
        ? [String(obj)]
        : Object.values(obj as Record<string, unknown>).flatMap(allValues);
    expect(allValues(viExpenses).every((v) => v.trim() !== "")).toBe(true);
    expect(allValues(enExpenses).every((v) => v.trim() !== "")).toBe(true);
  });

  it("ValidationExpenseAndShare_ViAndEn_HaveIdenticalKeyShape", () => {
    const vi = viValidation as Record<string, unknown>;
    const en = enValidation as Record<string, unknown>;
    expect(leafKeys(en.expense)).toEqual(leafKeys(vi.expense));
    expect(leafKeys(en.share)).toEqual(leafKeys(vi.share));
  });

  it("ValidationExpenseAndShare_CoverEveryRuleTheSchemasReference", () => {
    // The Zod factories reference these keys — both locales must define them.
    const expenseKeys = [
      "nameRequired",
      "nameTooLong",
      "timeRequired",
      "descriptionTooLong",
    ];
    const shareKeys = [
      "memberRequired",
      "amountNegative",
      "noteTooLong",
      "duplicateMember",
      "ownerRepRequired",
    ];
    const vi = viValidation as { expense: Record<string, string>; share: Record<string, string> };
    const en = enValidation as { expense: Record<string, string>; share: Record<string, string> };
    for (const k of expenseKeys) {
      expect(vi.expense[k]).toBeTruthy();
      expect(en.expense[k]).toBeTruthy();
    }
    for (const k of shareKeys) {
      expect(vi.share[k]).toBeTruthy();
      expect(en.share[k]).toBeTruthy();
    }
  });

  it("ExpensesNamespace_KnownDomainTerms_UseFixedViVnCopy", () => {
    // Fixed domain terms (phiếu chi tiêu / phần gánh / đã trả / phiếu lẻ) per the
    // plan — guard against drift to voucher/record/batch.
    expect(viExpenses.title).toBe("Phiếu chi tiêu");
    expect(viExpenses.shares.sectionTitle).toBe("Phần gánh");
    expect(viExpenses.settled.on).toBe("Đã trả");
    expect(viExpenses.badge.loose).toBe("Phiếu lẻ");
  });
});
