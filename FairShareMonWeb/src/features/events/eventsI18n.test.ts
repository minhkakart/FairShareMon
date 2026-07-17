import { describe, expect, it } from "vitest";
import viEvents from "@/i18n/locales/vi-VN/events.json";
import enEvents from "@/i18n/locales/en-US/events.json";
import viExpenses from "@/i18n/locales/vi-VN/expenses.json";
import enExpenses from "@/i18n/locales/en-US/expenses.json";
import viValidation from "@/i18n/locales/vi-VN/validation.json";
import enValidation from "@/i18n/locales/en-US/validation.json";

/**
 * i18n parity — the `events` namespace, the `validation.event.*` subtree, and the
 * new `expenses.filter.event*` / `expenses.expenseEvent.*` M5 keys must exist in
 * BOTH vi-VN (authoritative) and en-US with the exact same key shape (no missing,
 * no extra), so no M5 surface falls back to a raw key or the wrong language.
 * Structural test over the JSON catalogs, independent of any component.
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

describe("events i18n parity", () => {
  it("EventsNamespace_ViAndEn_HaveIdenticalKeyShape", () => {
    expect(leafKeys(enEvents)).toEqual(leafKeys(viEvents));
  });

  it("EventsNamespace_NoLeafIsEmpty_InEitherLocale", () => {
    expect(allValues(viEvents).every((v) => v.trim() !== "")).toBe(true);
    expect(allValues(enEvents).every((v) => v.trim() !== "")).toBe(true);
  });

  it("ValidationEvent_ViAndEn_HaveIdenticalKeyShape", () => {
    const vi = viValidation as Record<string, unknown>;
    const en = enValidation as Record<string, unknown>;
    expect(leafKeys(en.event)).toEqual(leafKeys(vi.event));
  });

  it("ValidationEvent_CoversEveryRuleTheSchemaReferences", () => {
    const eventKeys = [
      "nameRequired",
      "nameTooLong",
      "descriptionTooLong",
      "startRequired",
      "endRequired",
      "rangeInvalid",
    ];
    const vi = viValidation as { event: Record<string, string> };
    const en = enValidation as { event: Record<string, string> };
    for (const k of eventKeys) {
      expect(vi.event[k]).toBeTruthy();
      expect(en.event[k]).toBeTruthy();
    }
  });

  it("ExpensesFilterAndExpenseEvent_M5Keys_PresentInBothLocales", () => {
    const vi = viExpenses as {
      filter: Record<string, string>;
      expenseEvent: Record<string, unknown>;
    };
    const en = enExpenses as {
      filter: Record<string, string>;
      expenseEvent: Record<string, unknown>;
    };
    // The M4-OQ7 event filter labels.
    expect(vi.filter.event).toBeTruthy();
    expect(vi.filter.eventAll).toBeTruthy();
    expect(en.filter.event).toBeTruthy();
    expect(en.filter.eventAll).toBeTruthy();
    // The expense-side event control block has identical shape across locales.
    expect(leafKeys(en.expenseEvent)).toEqual(leafKeys(vi.expenseEvent));
  });

  it("EventsNamespace_FixedDomainTerms_UseAuthoritativeViVnCopy", () => {
    // Fixed domain terms per the plan — guard against drift.
    expect(viEvents.title).toBe("Đợt chi tiêu");
    expect(viEvents.status.open).toBe("Đang mở");
    expect(viEvents.status.closed).toBe("Đã chốt");
    expect(viEvents.balance.advanced).toBe("Đã ứng");
    expect(viEvents.balance.owed).toBe("Phải gánh");
    expect(viEvents.balance.balance).toBe("Cân bằng");
  });
});
