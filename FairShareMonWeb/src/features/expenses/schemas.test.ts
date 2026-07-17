import { describe, expect, it } from "vitest";
import type { z } from "zod";
import {
  EXPENSE_DESC_MAX,
  EXPENSE_NAME_MAX,
  SHARE_NOTE_MAX,
  createExpenseSchema,
  expenseGeneralSchema,
  shareFormSchema,
  shareRowSchema,
} from "./schemas";
import type { AppTFunction } from "@/i18n/useT";

/**
 * Expense + share Zod schemas — localized factories mirroring the backend
 * validators: name required 1–200; description ≤1000; expenseTime required; share
 * amount a non-negative integer (whole VND); note ≤500; plus the atomic-create
 * no-duplicate-member and owner-representative-present refinements. The `t` stub
 * echoes the message key so we assert which rule fired without coupling to copy.
 */
const t = ((key: string) => key) as unknown as AppTFunction;

function issues(result: z.ZodSafeParseResult<unknown>): string[] {
  return result.success ? [] : result.error.issues.map((i) => i.message);
}

const OWNER = "m-owner";

/** A minimal-valid general-info object. */
function generalBase() {
  return {
    name: "Thuê xe",
    description: "",
    expenseTime: "2026-07-16T10:00",
    payerMemberUuid: "",
    categoryUuid: "",
    tagUuids: [] as string[],
  };
}

describe("expenseGeneralSchema", () => {
  const schema = expenseGeneralSchema(t);

  it("ExpenseGeneralSchema_ValidGeneralInfo_Passes", () => {
    expect(schema.safeParse(generalBase()).success).toBe(true);
  });

  it("ExpenseGeneralSchema_EmptyName_FailsRequiredRule", () => {
    const result = schema.safeParse({ ...generalBase(), name: "" });
    expect(issues(result)).toContain("validation:expense.nameRequired");
  });

  it("ExpenseGeneralSchema_WhitespaceName_FailsRequiredAfterTrim", () => {
    const result = schema.safeParse({ ...generalBase(), name: "   " });
    expect(issues(result)).toContain("validation:expense.nameRequired");
  });

  it("ExpenseGeneralSchema_NameAtMax_Passes", () => {
    const result = schema.safeParse({
      ...generalBase(),
      name: "a".repeat(EXPENSE_NAME_MAX),
    });
    expect(result.success).toBe(true);
  });

  it("ExpenseGeneralSchema_NameOverMax_FailsTooLongRule", () => {
    const result = schema.safeParse({
      ...generalBase(),
      name: "a".repeat(EXPENSE_NAME_MAX + 1),
    });
    expect(issues(result)).toContain("validation:expense.nameTooLong");
  });

  it("ExpenseGeneralSchema_DescriptionOverMax_FailsTooLongRule", () => {
    const result = schema.safeParse({
      ...generalBase(),
      description: "a".repeat(EXPENSE_DESC_MAX + 1),
    });
    expect(issues(result)).toContain("validation:expense.descriptionTooLong");
  });

  it("ExpenseGeneralSchema_MissingExpenseTime_FailsRequiredRule", () => {
    const result = schema.safeParse({ ...generalBase(), expenseTime: "" });
    expect(issues(result)).toContain("validation:expense.timeRequired");
  });
});

describe("shareRowSchema", () => {
  const schema = shareRowSchema(t);

  it("ShareRowSchema_ValidRow_Passes", () => {
    expect(
      schema.safeParse({ memberUuid: "m-1", amount: 1000, note: "" }).success,
    ).toBe(true);
  });

  it("ShareRowSchema_MissingMember_FailsRequiredRule", () => {
    const result = schema.safeParse({ memberUuid: "", amount: 0, note: "" });
    expect(issues(result)).toContain("validation:share.memberRequired");
  });

  it("ShareRowSchema_NegativeAmount_FailsNonNegativeRule", () => {
    const result = schema.safeParse({
      memberUuid: "m-1",
      amount: -5,
      note: "",
    });
    expect(issues(result)).toContain("validation:share.amountNegative");
  });

  it("ShareRowSchema_FractionalAmount_FailsIntegerRule", () => {
    const result = schema.safeParse({
      memberUuid: "m-1",
      amount: 100.5,
      note: "",
    });
    expect(issues(result)).toContain("validation:share.amountNegative");
  });

  it("ShareRowSchema_ZeroAmount_Passes", () => {
    expect(
      schema.safeParse({ memberUuid: "m-1", amount: 0, note: "" }).success,
    ).toBe(true);
  });

  it("ShareRowSchema_NullAmount_Passes", () => {
    // MoneyInput emits null when empty; the row schema tolerates it (the create
    // page coerces null → 0 on submit).
    expect(
      schema.safeParse({ memberUuid: "m-1", amount: null, note: "" }).success,
    ).toBe(true);
  });

  it("ShareRowSchema_NoteOverMax_FailsTooLongRule", () => {
    const result = schema.safeParse({
      memberUuid: "m-1",
      amount: 0,
      note: "a".repeat(SHARE_NOTE_MAX + 1),
    });
    expect(issues(result)).toContain("validation:share.noteTooLong");
  });
});

describe("createExpenseSchema refinements", () => {
  function createBase(shares: unknown[]) {
    return { ...generalBase(), shares };
  }

  it("CreateExpenseSchema_OwnerRepPresent_Passes", () => {
    const schema = createExpenseSchema(t, OWNER);
    const result = schema.safeParse(
      createBase([{ memberUuid: OWNER, amount: 0, note: "" }]),
    );
    expect(result.success).toBe(true);
  });

  it("CreateExpenseSchema_OwnerRepMissing_FailsOwnerRepRequiredRule", () => {
    const schema = createExpenseSchema(t, OWNER);
    const result = schema.safeParse(
      createBase([{ memberUuid: "m-other", amount: 100, note: "" }]),
    );
    expect(issues(result)).toContain("validation:share.ownerRepRequired");
  });

  it("CreateExpenseSchema_DuplicateMember_FailsDuplicateRule", () => {
    const schema = createExpenseSchema(t, OWNER);
    const result = schema.safeParse(
      createBase([
        { memberUuid: OWNER, amount: 0, note: "" },
        { memberUuid: "m-x", amount: 100, note: "" },
        { memberUuid: "m-x", amount: 200, note: "" },
      ]),
    );
    expect(issues(result)).toContain("validation:share.duplicateMember");
  });

  it("CreateExpenseSchema_NoOwnerRepUuid_SkipsOwnerRepRefinement", () => {
    // When no owner-rep uuid is supplied (defensive), the presence rule is
    // skipped — only the general + row rules apply.
    const schema = createExpenseSchema(t, undefined);
    const result = schema.safeParse(
      createBase([{ memberUuid: "m-x", amount: 100, note: "" }]),
    );
    expect(result.success).toBe(true);
  });
});

describe("shareFormSchema", () => {
  const schema = shareFormSchema(t);

  it("ShareFormSchema_ValidShare_Passes", () => {
    expect(
      schema.safeParse({ memberUuid: "m-1", amount: 1000, note: "" }).success,
    ).toBe(true);
  });

  it("ShareFormSchema_MissingMember_FailsRequiredRule", () => {
    const result = schema.safeParse({ memberUuid: "", amount: 0, note: "" });
    expect(issues(result)).toContain("validation:share.memberRequired");
  });

  it("ShareFormSchema_NegativeAmount_FailsNonNegativeRule", () => {
    const result = schema.safeParse({
      memberUuid: "m-1",
      amount: -1,
      note: "",
    });
    expect(issues(result)).toContain("validation:share.amountNegative");
  });
});
