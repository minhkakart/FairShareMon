import { describe, expect, it } from "vitest";
import type { z } from "zod";
import { TAG_NAME_MAX, tagFormSchema } from "./schemas";
import type { AppTFunction } from "@/i18n/useT";

/**
 * `tagFormSchema` mirrors the backend validators (`CreateTagRequestValidator` /
 * `UpdateTagRequestValidator`): name required (non-empty after trim) 1–100. The
 * `t` stub echoes the message key so we assert which rule fired without coupling
 * to copy.
 */
const t = ((key: string) => key) as unknown as AppTFunction;
const schema = tagFormSchema(t);

function issues(result: z.ZodSafeParseResult<unknown>): string[] {
  return result.success ? [] : result.error.issues.map((i) => i.message);
}

describe("tagFormSchema", () => {
  it("TagFormSchema_ValidName_Passes", () => {
    expect(schema.safeParse({ name: "Công tác" }).success).toBe(true);
  });

  it("TagFormSchema_TrimsSurroundingWhitespaceOnParse", () => {
    const result = schema.safeParse({ name: "  Công tác  " });
    expect(result.success).toBe(true);
    if (result.success) expect(result.data.name).toBe("Công tác");
  });

  it("TagFormSchema_EmptyName_FailsRequiredRule", () => {
    const result = schema.safeParse({ name: "" });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:tag.nameRequired");
  });

  it("TagFormSchema_WhitespaceOnlyName_FailsRequiredRuleAfterTrim", () => {
    const result = schema.safeParse({ name: "   " });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:tag.nameRequired");
  });

  it("TagFormSchema_NameAtMaxLength_Passes", () => {
    const result = schema.safeParse({ name: "a".repeat(TAG_NAME_MAX) });
    expect(result.success).toBe(true);
  });

  it("TagFormSchema_NameOverMaxLength_FailsTooLongRule", () => {
    const result = schema.safeParse({ name: "a".repeat(TAG_NAME_MAX + 1) });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:tag.nameTooLong");
  });
});
