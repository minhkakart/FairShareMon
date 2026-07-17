import { beforeAll, describe, expect, it } from "vitest";
import type { z } from "zod";
import i18n from "@/i18n";
import type { AppTFunction } from "@/i18n/useT";
import { customRangeSchema } from "./schemas";

/**
 * Custom-range Zod schema — mirrors the backend `StatsRangeRequest` validator:
 * when BOTH `from` and `to` are set, `from <= to` (issue attached to `to`, the
 * `1001` the API would otherwise answer). Rule-firing is asserted with a
 * key-echoing `t` stub (decoupled from copy); a separate case asserts the exact
 * localized vi-VN/en-US message through the real i18n catalog.
 */
const tStub = ((key: string) => key) as unknown as AppTFunction;
const schema = customRangeSchema(tStub);

function issues(result: z.ZodSafeParseResult<unknown>): { message: string; path: PropertyKey[] }[] {
  return result.success
    ? []
    : result.error.issues.map((i) => ({ message: i.message, path: i.path }));
}

describe("customRangeSchema", () => {
  it("CustomRangeSchema_FromBeforeTo_Passes", () => {
    expect(
      schema.safeParse({ from: "2026-03-05", to: "2026-03-20" }).success,
    ).toBe(true);
  });

  it("CustomRangeSchema_FromEqualsTo_Passes", () => {
    // Inclusive range — a single-day [from,to] is valid.
    expect(
      schema.safeParse({ from: "2026-03-05", to: "2026-03-05" }).success,
    ).toBe(true);
  });

  it("CustomRangeSchema_EitherBoundEmpty_Passes", () => {
    // Not validated until both bounds are provided (optional-bound contract).
    expect(schema.safeParse({ from: "2026-03-20", to: "" }).success).toBe(true);
    expect(schema.safeParse({ from: "", to: "2026-03-05" }).success).toBe(true);
    expect(schema.safeParse({ from: "", to: "" }).success).toBe(true);
  });

  it("CustomRangeSchema_FromAfterTo_FailsRangeInvalidRuleOnTo", () => {
    const found = issues(
      schema.safeParse({ from: "2026-03-20", to: "2026-03-05" }),
    );
    expect(found).toHaveLength(1);
    expect(found[0].message).toBe("validation:stats.rangeInvalid");
    // The issue attaches to `to` so the control flags the right field.
    expect(found[0].path).toEqual(["to"]);
  });
});

describe("customRangeSchema — localized message", () => {
  beforeAll(async () => {
    await i18n.changeLanguage("vi-VN");
  });

  it("CustomRangeSchema_ViVn_ProducesTheViVnRangeInvalidCopy", async () => {
    await i18n.changeLanguage("vi-VN");
    const t = i18n.t.bind(i18n) as unknown as AppTFunction;
    const result = customRangeSchema(t).safeParse({
      from: "2026-03-20",
      to: "2026-03-05",
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues[0].message).toBe(
        "“Đến ngày” phải sau hoặc bằng “Từ ngày”.",
      );
    }
  });

  it("CustomRangeSchema_EnUs_ProducesTheEnUsRangeInvalidCopy", async () => {
    await i18n.changeLanguage("en-US");
    const t = i18n.t.bind(i18n) as unknown as AppTFunction;
    const result = customRangeSchema(t).safeParse({
      from: "2026-03-20",
      to: "2026-03-05",
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      // en-US parity — a non-empty, English message (not a raw key).
      const msg = result.error.issues[0].message;
      expect(msg).toBeTruthy();
      expect(msg).not.toContain("validation:");
      expect(msg).not.toBe("“Đến ngày” phải sau hoặc bằng “Từ ngày”.");
    }
    await i18n.changeLanguage("vi-VN");
  });
});
