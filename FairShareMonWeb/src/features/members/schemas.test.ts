import { describe, expect, it } from "vitest";
import type { z } from "zod";
import { MEMBER_NAME_MAX, memberFormSchema } from "./schemas";
import type { AppTFunction } from "@/i18n/useT";

/**
 * `memberFormSchema` mirrors the backend validators
 * (`CreateMemberRequestValidator` / `UpdateMemberRequestValidator`): name is
 * required (non-empty after trim) and at most 100 chars, trimmed before submit.
 * The `t` stub echoes the message key so we assert which rule fired without
 * coupling to copy.
 */
const t = ((key: string) => key) as unknown as AppTFunction;
const schema = memberFormSchema(t);

function issues(result: z.ZodSafeParseResult<unknown>): string[] {
  return result.success ? [] : result.error.issues.map((i) => i.message);
}

describe("memberFormSchema", () => {
  it("MemberFormSchema_ValidName_Passes", () => {
    expect(schema.safeParse({ name: "An Nguyễn" }).success).toBe(true);
  });

  it("MemberFormSchema_TrimsSurroundingWhitespaceOnParse", () => {
    const result = schema.safeParse({ name: "  An Nguyễn  " });
    expect(result.success).toBe(true);
    if (result.success) expect(result.data.name).toBe("An Nguyễn");
  });

  it("MemberFormSchema_EmptyName_FailsRequiredRule", () => {
    const result = schema.safeParse({ name: "" });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:member.nameRequired");
  });

  it("MemberFormSchema_WhitespaceOnlyName_FailsRequiredRuleAfterTrim", () => {
    const result = schema.safeParse({ name: "   " });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:member.nameRequired");
  });

  it("MemberFormSchema_NameAtMaxLength_Passes", () => {
    const result = schema.safeParse({ name: "a".repeat(MEMBER_NAME_MAX) });
    expect(result.success).toBe(true);
  });

  it("MemberFormSchema_NameOverMaxLength_FailsTooLongRule", () => {
    const result = schema.safeParse({ name: "a".repeat(MEMBER_NAME_MAX + 1) });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:member.nameTooLong");
  });

  it("MemberFormSchema_MaxLengthMeasuredAfterTrim_HundredCharsWithSpacesPasses", () => {
    // Benign edge (per the plan): the client trims THEN checks 1–100, so a
    // 100-char core wrapped in spaces passes client-side. The server stays
    // authoritative (it may validate pre-trim and reject with 1001).
    const result = schema.safeParse({
      name: `  ${"a".repeat(MEMBER_NAME_MAX)}  `,
    });
    expect(result.success).toBe(true);
    if (result.success) expect(result.data.name).toHaveLength(MEMBER_NAME_MAX);
  });
});
