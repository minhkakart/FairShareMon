import { describe, expect, it } from "vitest";
import type { z } from "zod";
import {
  ACCOUNT_HOLDER_MAX,
  BANK_NAME_MAX,
  bankAccountFormSchema,
} from "./schemas";
import type { AppTFunction } from "@/i18n/useT";

/**
 * `bankAccountFormSchema` mirrors the backend validators
 * (`CreateBankAccountRequestValidator` / `UpdateBankAccountRequestValidator`):
 *  - `bankBin`     exactly 6 digits (`^\d{6}$`)
 *  - `bankName`    required (non-empty after trim), ≤100
 *  - `accountNumber` digits only, 6–19 chars (`^\d{6,19}$`)
 *  - `accountHolderName` required (non-empty after trim), ≤100
 * The `t` stub echoes the message key so assertions target which rule fired, not
 * the copy (copy parity is covered by walletI18n.test.ts).
 */
const t = ((key: string) => key) as unknown as AppTFunction;
const schema = bankAccountFormSchema(t);

function issues(result: z.ZodSafeParseResult<unknown>): string[] {
  return result.success ? [] : result.error.issues.map((i) => i.message);
}

const VALID = {
  bankName: "Vietcombank",
  bankBin: "970436",
  accountNumber: "0071001234567",
  accountHolderName: "NGUYEN VAN MINH",
};

describe("bankAccountFormSchema", () => {
  it("BankAccountFormSchema_ValidPayload_Passes", () => {
    expect(schema.safeParse(VALID).success).toBe(true);
  });

  it("BankAccountFormSchema_TrimsSurroundingWhitespaceOnParse", () => {
    const result = schema.safeParse({
      ...VALID,
      bankName: "  Vietcombank  ",
      accountHolderName: "  NGUYEN VAN MINH  ",
    });
    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.bankName).toBe("Vietcombank");
      expect(result.data.accountHolderName).toBe("NGUYEN VAN MINH");
    }
  });

  // ── BIN (^\d{6}$) ──────────────────────────────────────────────────────────
  it("BankAccountFormSchema_BinExactlySixDigits_Passes", () => {
    expect(schema.safeParse({ ...VALID, bankBin: "970436" }).success).toBe(true);
  });

  it("BankAccountFormSchema_BinFiveDigits_FailsPatternRule", () => {
    const result = schema.safeParse({ ...VALID, bankBin: "97043" });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:bankAccount.binPattern");
  });

  it("BankAccountFormSchema_BinSevenDigits_FailsPatternRule", () => {
    const result = schema.safeParse({ ...VALID, bankBin: "9704360" });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:bankAccount.binPattern");
  });

  it("BankAccountFormSchema_BinWithLetters_FailsPatternRule", () => {
    const result = schema.safeParse({ ...VALID, bankBin: "97043A" });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:bankAccount.binPattern");
  });

  it("BankAccountFormSchema_BinEmpty_FailsRequiredRule", () => {
    const result = schema.safeParse({ ...VALID, bankBin: "" });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:bankAccount.binRequired");
  });

  // ── Account number (^\d{6,19}$) ────────────────────────────────────────────
  it("BankAccountFormSchema_AccountNumberSixDigits_Passes", () => {
    expect(
      schema.safeParse({ ...VALID, accountNumber: "123456" }).success,
    ).toBe(true);
  });

  it("BankAccountFormSchema_AccountNumberNineteenDigits_Passes", () => {
    expect(
      schema.safeParse({ ...VALID, accountNumber: "1".repeat(19) }).success,
    ).toBe(true);
  });

  it("BankAccountFormSchema_AccountNumberFiveDigits_FailsInvalidRule", () => {
    const result = schema.safeParse({ ...VALID, accountNumber: "12345" });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain(
      "validation:bankAccount.accountNumberInvalid",
    );
  });

  it("BankAccountFormSchema_AccountNumberTwentyDigits_FailsInvalidRule", () => {
    const result = schema.safeParse({ ...VALID, accountNumber: "1".repeat(20) });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain(
      "validation:bankAccount.accountNumberInvalid",
    );
  });

  it("BankAccountFormSchema_AccountNumberWithLetters_FailsInvalidRule", () => {
    const result = schema.safeParse({ ...VALID, accountNumber: "12345A" });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain(
      "validation:bankAccount.accountNumberInvalid",
    );
  });

  it("BankAccountFormSchema_AccountNumberEmpty_FailsRequiredRule", () => {
    const result = schema.safeParse({ ...VALID, accountNumber: "" });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain(
      "validation:bankAccount.accountNumberRequired",
    );
  });

  // ── Names (required + ≤100) ────────────────────────────────────────────────
  it("BankAccountFormSchema_EmptyBankName_FailsRequiredRule", () => {
    const result = schema.safeParse({ ...VALID, bankName: "" });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:bankAccount.bankNameRequired");
  });

  it("BankAccountFormSchema_WhitespaceBankName_FailsRequiredRuleAfterTrim", () => {
    const result = schema.safeParse({ ...VALID, bankName: "   " });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:bankAccount.bankNameRequired");
  });

  it("BankAccountFormSchema_BankNameAtMax_Passes", () => {
    expect(
      schema.safeParse({ ...VALID, bankName: "a".repeat(BANK_NAME_MAX) }).success,
    ).toBe(true);
  });

  it("BankAccountFormSchema_BankNameOverMax_FailsTooLongRule", () => {
    const result = schema.safeParse({
      ...VALID,
      bankName: "a".repeat(BANK_NAME_MAX + 1),
    });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:bankAccount.bankNameTooLong");
  });

  it("BankAccountFormSchema_EmptyHolder_FailsRequiredRule", () => {
    const result = schema.safeParse({ ...VALID, accountHolderName: "" });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:bankAccount.holderRequired");
  });

  it("BankAccountFormSchema_HolderOverMax_FailsTooLongRule", () => {
    const result = schema.safeParse({
      ...VALID,
      accountHolderName: "a".repeat(ACCOUNT_HOLDER_MAX + 1),
    });
    expect(result.success).toBe(false);
    expect(issues(result)).toContain("validation:bankAccount.holderTooLong");
  });
});
