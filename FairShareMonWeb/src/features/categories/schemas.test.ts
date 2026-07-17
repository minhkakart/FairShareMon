import { describe, expect, it } from "vitest";
import type { z } from "zod";
import {
  CATEGORY_ICON_MAX,
  CATEGORY_NAME_MAX,
  HEX_COLOR,
  categoryFormSchema,
} from "./schemas";
import type { AppTFunction } from "@/i18n/useT";

/**
 * `categoryFormSchema` mirrors the backend validators
 * (`CreateCategoryRequestValidator` / `UpdateCategoryRequestValidator`): name
 * required (non-empty after trim) 1–100; color required + valid `#RRGGBB`; icon
 * optional ≤50. The `t` stub echoes the message key so we assert which rule fired
 * without coupling to copy.
 */
const t = ((key: string) => key) as unknown as AppTFunction;
const schema = categoryFormSchema(t);

function issues(result: z.ZodSafeParseResult<unknown>): string[] {
  return result.success ? [] : result.error.issues.map((i) => i.message);
}

const VALID = { name: "Ăn uống", color: "#F97316", icon: "🍜" };

describe("categoryFormSchema", () => {
  it("CategoryFormSchema_ValidValues_Passes", () => {
    expect(schema.safeParse(VALID).success).toBe(true);
  });

  it("CategoryFormSchema_TrimsSurroundingWhitespaceOnName", () => {
    const result = schema.safeParse({ ...VALID, name: "  Ăn uống  " });
    expect(result.success).toBe(true);
    if (result.success) expect(result.data.name).toBe("Ăn uống");
  });

  it("CategoryFormSchema_EmptyName_FailsRequiredRule", () => {
    const result = schema.safeParse({ ...VALID, name: "" });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:category.nameRequired");
  });

  it("CategoryFormSchema_WhitespaceOnlyName_FailsRequiredRuleAfterTrim", () => {
    const result = schema.safeParse({ ...VALID, name: "   " });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:category.nameRequired");
  });

  it("CategoryFormSchema_NameAtMaxLength_Passes", () => {
    const result = schema.safeParse({
      ...VALID,
      name: "a".repeat(CATEGORY_NAME_MAX),
    });
    expect(result.success).toBe(true);
  });

  it("CategoryFormSchema_NameOverMaxLength_FailsTooLongRule", () => {
    const result = schema.safeParse({
      ...VALID,
      name: "a".repeat(CATEGORY_NAME_MAX + 1),
    });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:category.nameTooLong");
  });

  it("CategoryFormSchema_ValidHexColors_Pass", () => {
    for (const color of ["#000000", "#FFFFFF", "#3B82F6", "#abcdef"]) {
      expect(schema.safeParse({ ...VALID, color }).success).toBe(true);
    }
  });

  it("CategoryFormSchema_InvalidColor_FailsColorRule", () => {
    for (const color of ["", "F97316", "#F9731", "#GGGGGG", "#F973166", "red"]) {
      const result = schema.safeParse({ ...VALID, color });
      expect(result.success).toBe(false);
      expect(issues(result)).toContain("validation:category.colorInvalid");
    }
  });

  it("CategoryFormSchema_HexColorRegex_MatchesSixDigitHashOnly", () => {
    expect(HEX_COLOR.test("#F97316")).toBe(true);
    expect(HEX_COLOR.test("#f97316")).toBe(true);
    expect(HEX_COLOR.test("F97316")).toBe(false);
    expect(HEX_COLOR.test("#F9731")).toBe(false);
  });

  it("CategoryFormSchema_NoIcon_IsOptional", () => {
    expect(schema.safeParse({ name: "X", color: "#000000" }).success).toBe(true);
    expect(
      schema.safeParse({ name: "X", color: "#000000", icon: null }).success,
    ).toBe(true);
  });

  it("CategoryFormSchema_IconAtMaxLength_Passes", () => {
    const result = schema.safeParse({
      ...VALID,
      icon: "a".repeat(CATEGORY_ICON_MAX),
    });
    expect(result.success).toBe(true);
  });

  it("CategoryFormSchema_IconOverMaxLength_FailsTooLongRule", () => {
    const result = schema.safeParse({
      ...VALID,
      icon: "a".repeat(CATEGORY_ICON_MAX + 1),
    });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:category.iconTooLong");
  });
});
