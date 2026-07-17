import { beforeAll, describe, expect, it } from "vitest";
import type { z } from "zod";
import i18n from "@/i18n";
import type { AppTFunction } from "@/i18n/useT";
import {
  EVENT_DESC_MAX,
  EVENT_NAME_MAX,
  eventFormSchema,
} from "./schemas";

/**
 * Event form Zod schema — a localized factory mirroring the backend
 * `CreateEventRequestValidator`/`UpdateEventRequestValidator`: name required
 * 1–200; description ≤1000; start/end required; `endDate >= startDate` (attached
 * to `endDate`). Rule-firing is asserted with a key-echoing `t` stub (decoupled
 * from copy); a separate case asserts the exact vi-VN messages through the real
 * i18n catalog.
 */
const t = ((key: string) => key) as unknown as AppTFunction;
const schema = eventFormSchema(t);

function issues(result: z.ZodSafeParseResult<unknown>): string[] {
  return result.success ? [] : result.error.issues.map((i) => i.message);
}

function base() {
  return {
    name: "Đà Lạt 07/2026",
    description: "",
    startDate: "2026-07-12",
    endDate: "2026-07-18",
  };
}

describe("eventFormSchema", () => {
  it("EventFormSchema_ValidInput_Passes", () => {
    expect(schema.safeParse(base()).success).toBe(true);
  });

  it("EventFormSchema_EmptyName_FailsRequiredRule", () => {
    expect(issues(schema.safeParse({ ...base(), name: "" }))).toContain(
      "validation:event.nameRequired",
    );
  });

  it("EventFormSchema_WhitespaceName_FailsRequiredAfterTrim", () => {
    expect(issues(schema.safeParse({ ...base(), name: "   " }))).toContain(
      "validation:event.nameRequired",
    );
  });

  it("EventFormSchema_NameAtMax_Passes", () => {
    expect(
      schema.safeParse({ ...base(), name: "a".repeat(EVENT_NAME_MAX) }).success,
    ).toBe(true);
  });

  it("EventFormSchema_NameOverMax_FailsTooLongRule", () => {
    expect(
      issues(schema.safeParse({ ...base(), name: "a".repeat(EVENT_NAME_MAX + 1) })),
    ).toContain("validation:event.nameTooLong");
  });

  it("EventFormSchema_DescriptionOverMax_FailsTooLongRule", () => {
    expect(
      issues(
        schema.safeParse({
          ...base(),
          description: "a".repeat(EVENT_DESC_MAX + 1),
        }),
      ),
    ).toContain("validation:event.descriptionTooLong");
  });

  it("EventFormSchema_MissingStart_FailsStartRequiredRule", () => {
    expect(issues(schema.safeParse({ ...base(), startDate: "" }))).toContain(
      "validation:event.startRequired",
    );
  });

  it("EventFormSchema_MissingEnd_FailsEndRequiredRule", () => {
    expect(issues(schema.safeParse({ ...base(), endDate: "" }))).toContain(
      "validation:event.endRequired",
    );
  });

  it("EventFormSchema_EndEqualsStart_Passes", () => {
    // Whole-day inclusive: a single-day event (end == start) is valid.
    const result = schema.safeParse({
      ...base(),
      startDate: "2026-07-12",
      endDate: "2026-07-12",
    });
    expect(result.success).toBe(true);
  });

  it("EventFormSchema_EndBeforeStart_FailsRangeRuleOnEndDate", () => {
    const result = schema.safeParse({
      ...base(),
      startDate: "2026-07-18",
      endDate: "2026-07-12",
    });
    expect(result.success).toBe(false);
    if (result.success) return;
    const issue = result.error.issues.find((i) => i.path[0] === "endDate");
    // The range violation is reported ON the endDate field (so it renders there).
    expect(issue?.message).toBe("validation:event.rangeInvalid");
  });
});

describe("eventFormSchema vi-VN messages", () => {
  let viSchema: ReturnType<typeof eventFormSchema>;

  beforeAll(async () => {
    await i18n.changeLanguage("vi-VN");
    viSchema = eventFormSchema(i18n.t as unknown as AppTFunction);
  });

  it("EventFormSchema_ViVn_UsesExactAuthoritativeCopy", () => {
    expect(issues(viSchema.safeParse({ ...base(), name: "" }))).toContain(
      "Tên đợt không được để trống.",
    );
    expect(
      issues(viSchema.safeParse({ ...base(), name: "a".repeat(EVENT_NAME_MAX + 1) })),
    ).toContain("Tên đợt không được vượt quá 200 ký tự.");
    const range = viSchema.safeParse({
      ...base(),
      startDate: "2026-07-18",
      endDate: "2026-07-12",
    });
    expect(issues(range)).toContain(
      "Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.",
    );
  });
});
