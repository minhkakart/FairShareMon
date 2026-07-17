import { afterAll, beforeAll, describe, expect, it } from "vitest";
import i18n from "@/i18n";
import type { AppTFunction } from "@/i18n/useT";
import {
  grantTierSchema,
  resetPasswordSchema,
  revokeTierSchema,
} from "./schemas";

/**
 * Admin Zod schemas — mirror the backend admin validators. Rule-firing is asserted
 * with a key-echoing `t` stub (decoupled from copy); a separate case asserts the
 * exact localized vi-VN message via the real i18n catalog. Proves the grant amount
 * rule (required, integer, ≥ 0 → `1001 fields.amount`), the reference/note/currency
 * length caps, the revoke note cap, and the reset-password policy (min 8, ≤72 bytes).
 */

const tStub = ((key: string) => key) as unknown as AppTFunction;

describe("grantTierSchema", () => {
  const schema = grantTierSchema(tStub);

  it("GrantTierSchema_ValidGrant_Passes", () => {
    expect(
      schema.safeParse({ amount: 200000, reference: "VCB-1", note: "gia hạn" })
        .success,
    ).toBe(true);
  });

  it("GrantTierSchema_ZeroAmount_Passes", () => {
    // 0 is a valid free grant (amount ≥ 0).
    expect(schema.safeParse({ amount: 0 }).success).toBe(true);
  });

  it("GrantTierSchema_NullAmount_FailsRequired", () => {
    const result = schema.safeParse({ amount: null });
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues[0].message).toBe("admin:validation.amountRequired");
    }
  });

  it("GrantTierSchema_NegativeAmount_FailsNegativeOnAmountField", () => {
    const result = schema.safeParse({ amount: -100 });
    expect(result.success).toBe(false);
    if (!result.success) {
      const issue = result.error.issues.find((i) => i.path[0] === "amount");
      expect(issue?.message).toBe("admin:validation.amountNegative");
    }
  });

  it("GrantTierSchema_NonIntegerAmount_Fails", () => {
    expect(schema.safeParse({ amount: 1000.5 }).success).toBe(false);
  });

  it("GrantTierSchema_OverlongReferenceNoteCurrency_Fail", () => {
    expect(
      schema.safeParse({ amount: 1, reference: "x".repeat(256) }).success,
    ).toBe(false);
    expect(
      schema.safeParse({ amount: 1, note: "x".repeat(501) }).success,
    ).toBe(false);
    expect(
      schema.safeParse({ amount: 1, currency: "VNDX" }).success,
    ).toBe(false);
  });
});

describe("revokeTierSchema", () => {
  const schema = revokeTierSchema(tStub);

  it("RevokeTierSchema_EmptyOrShortNote_Passes", () => {
    expect(schema.safeParse({}).success).toBe(true);
    expect(schema.safeParse({ note: "thu hồi" }).success).toBe(true);
  });

  it("RevokeTierSchema_OverlongNote_Fails", () => {
    const result = schema.safeParse({ note: "x".repeat(501) });
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues[0].message).toBe("admin:validation.noteTooLong");
    }
  });
});

describe("resetPasswordSchema", () => {
  const schema = resetPasswordSchema(tStub);

  it("ResetPasswordSchema_StrongPassword_Passes", () => {
    expect(schema.safeParse({ newPassword: "Abcd3fgh!jkm" }).success).toBe(true);
  });

  it("ResetPasswordSchema_TooShort_Fails", () => {
    expect(schema.safeParse({ newPassword: "Ab3!" }).success).toBe(false);
  });

  it("ResetPasswordSchema_Empty_Fails", () => {
    expect(schema.safeParse({ newPassword: "" }).success).toBe(false);
  });

  it("ResetPasswordSchema_Over72Bytes_Fails", () => {
    expect(schema.safeParse({ newPassword: "A1!" + "a".repeat(72) }).success).toBe(
      false,
    );
  });
});

describe("grantTierSchema — localized message", () => {
  beforeAll(async () => {
    await i18n.changeLanguage("vi-VN");
  });
  afterAll(async () => {
    await i18n.changeLanguage("vi-VN");
  });

  it("GrantTierSchema_ViVn_ProducesTheViVnNegativeCopy", () => {
    const t = i18n.t.bind(i18n) as unknown as AppTFunction;
    const result = grantTierSchema(t).safeParse({ amount: -1 });
    expect(result.success).toBe(false);
    if (!result.success) {
      const issue = result.error.issues.find((i) => i.path[0] === "amount");
      expect(issue?.message).toBe("Số tiền không được âm.");
    }
  });
});
