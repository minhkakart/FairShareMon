import { z } from "zod";
import type { AppTFunction } from "@/i18n/useT";

/**
 * Bank-account form schema — mirrors the backend validators
 * (`CreateBankAccountRequestValidator` / `UpdateBankAccountRequestValidator`):
 *  - `bankBin`     exactly 6 digits (`^\d{6}$`)
 *  - `bankName`    required (non-empty after trim), max 100
 *  - `accountNumber` digits only, 6–19 chars (`^\d{6,19}$`)
 *  - `accountHolderName` required (non-empty after trim), max 100
 * Built as a factory so messages are localized with the active `t`. The server
 * stays authoritative: a `1001` `error.fields.*` still surfaces on the field.
 */
export const BANK_NAME_MAX = 100;
export const ACCOUNT_HOLDER_MAX = 100;
export const BANK_BIN_PATTERN = /^\d{6}$/;
export const ACCOUNT_NUMBER_PATTERN = /^\d{6,19}$/;

export function bankAccountFormSchema(t: AppTFunction) {
  return z.object({
    bankName: z
      .string()
      .trim()
      .min(1, t("validation:bankAccount.bankNameRequired"))
      .max(BANK_NAME_MAX, t("validation:bankAccount.bankNameTooLong")),
    bankBin: z
      .string()
      .trim()
      .min(1, t("validation:bankAccount.binRequired"))
      .regex(BANK_BIN_PATTERN, t("validation:bankAccount.binPattern")),
    accountNumber: z
      .string()
      .trim()
      .min(1, t("validation:bankAccount.accountNumberRequired"))
      .regex(
        ACCOUNT_NUMBER_PATTERN,
        t("validation:bankAccount.accountNumberInvalid"),
      ),
    accountHolderName: z
      .string()
      .trim()
      .min(1, t("validation:bankAccount.holderRequired"))
      .max(ACCOUNT_HOLDER_MAX, t("validation:bankAccount.holderTooLong")),
  });
}

export type BankAccountFormValues = z.infer<
  ReturnType<typeof bankAccountFormSchema>
>;
