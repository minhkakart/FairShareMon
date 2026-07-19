import { z } from "zod";
import type { AppTFunction } from "@/i18n/useT";

/**
 * Expense + share form schemas — localized factories mirroring the backend
 * validators (`CreateExpenseRequestValidator` / `UpdateExpenseRequestValidator` /
 * `Create|UpdateShareRequestValidator`): name required 1–200; description ≤1000;
 * expenseTime required; share amount a non-negative integer (whole VND, OQ4a);
 * note ≤500. The atomic-create schema also mirrors no-duplicate-member (7003) and
 * owner-representative-present. The server stays authoritative — a `1001`
 * `error.fields.*` still surfaces on the matching field, and the cross-link /
 * owner-rep codes map onto fields too.
 */
export const EXPENSE_NAME_MAX = 200;
export const EXPENSE_DESC_MAX = 1000;
export const SHARE_NOTE_MAX = 500;

/** Shared general-info fields (create + edit). */
export function expenseGeneralSchema(t: AppTFunction) {
  return z.object({
    name: z
      .string()
      .trim()
      .min(1, t("validation:expense.nameRequired"))
      .max(EXPENSE_NAME_MAX, t("validation:expense.nameTooLong")),
    description: z
      .string()
      .max(EXPENSE_DESC_MAX, t("validation:expense.descriptionTooLong"))
      .optional(),
    expenseTime: z
      .string()
      .min(1, t("validation:expense.timeRequired")),
    /** Empty string → omit (backend applies the owner-rep default). */
    payerMemberUuid: z.string().optional(),
    /** Empty string → omit (backend applies the default category). */
    categoryUuid: z.string().optional(),
    /** Always provided by the forms' defaultValues (no zod default → stable RHF I/O type). */
    tagUuids: z.array(z.string()),
  });
}

export type ExpenseGeneralValues = z.infer<
  ReturnType<typeof expenseGeneralSchema>
>;

/** One share row in the editor (amount is `number | null` from MoneyInput). */
export function shareRowSchema(t: AppTFunction) {
  return z.object({
    /** The share's member (sent to the API). */
    memberUuid: z.string().min(1, t("validation:share.memberRequired")),
    amount: z
      .number()
      .int(t("validation:share.amountNegative"))
      .min(0, t("validation:share.amountNegative"))
      .nullable(),
    note: z
      .string()
      .max(SHARE_NOTE_MAX, t("validation:share.noteTooLong"))
      .optional(),
  });
}

export type ShareRowValues = z.infer<ReturnType<typeof shareRowSchema>>;

/**
 * The atomic-create schema: general info + the shares array, with the
 * no-duplicate-member (7003) and owner-representative-present refinements. Pass
 * the owner-rep member uuid so the "owner-rep present" rule can be enforced (the
 * editor also injects + locks that row).
 */
export function createExpenseSchema(t: AppTFunction, ownerRepUuid?: string) {
  return expenseGeneralSchema(t)
    .extend({
      shares: z.array(shareRowSchema(t)),
      /** Create-only: empty string → loose expense; else the OPEN event to join. */
      eventUuid: z.string().optional(),
    })
    .superRefine((values, ctx) => {
      const seen = new Set<string>();
      values.shares.forEach((share, index) => {
        if (!share.memberUuid) return;
        if (seen.has(share.memberUuid)) {
          ctx.addIssue({
            code: z.ZodIssueCode.custom,
            message: t("validation:share.duplicateMember"),
            path: ["shares", index, "memberUuid"],
          });
        }
        seen.add(share.memberUuid);
      });
      if (
        ownerRepUuid &&
        !values.shares.some((s) => s.memberUuid === ownerRepUuid)
      ) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: t("validation:share.ownerRepRequired"),
          path: ["shares"],
        });
      }
    });
}

export type CreateExpenseValues = z.infer<
  ReturnType<typeof createExpenseSchema>
>;

/** A single add/edit share on the detail page. */
export function shareFormSchema(t: AppTFunction) {
  return z.object({
    memberUuid: z.string().min(1, t("validation:share.memberRequired")),
    amount: z
      .number()
      .int(t("validation:share.amountNegative"))
      .min(0, t("validation:share.amountNegative"))
      .nullable(),
    note: z
      .string()
      .max(SHARE_NOTE_MAX, t("validation:share.noteTooLong"))
      .optional(),
  });
}

export type ShareFormValues = z.infer<ReturnType<typeof shareFormSchema>>;
