import { z } from "zod";
import type { AppTFunction } from "@/i18n/useT";
import {
  PASSWORD_MAX_BYTES,
  PASSWORD_MIN,
  utf8ByteLength,
} from "@/features/auth/schemas";

/**
 * Zod schemas mirroring the backend admin validators (`Validators/Admin/**`).
 * Factories so messages localize with the active `t`. The password rule reuses
 * the auth policy constants (min 8 chars, max 72 BYTES — the BCrypt limit), same
 * as `ResetPasswordRequestValidator`.
 */

/** `GrantTierRequestValidator`: amount ≥ 0 (0 = free grant); optional currency
 *  (≤3), reference (≤255), note (≤500). Amount comes from `MoneyInput` (number|null). */
export function grantTierSchema(t: AppTFunction) {
  return z.object({
    amount: z
      .number()
      .int()
      .min(0, t("admin:validation.amountNegative"))
      .nullable()
      .refine((value) => value !== null, t("admin:validation.amountRequired")),
    currency: z
      .string()
      .max(3, t("admin:validation.currencyTooLong"))
      .optional(),
    reference: z
      .string()
      .max(255, t("admin:validation.referenceTooLong"))
      .optional(),
    note: z.string().max(500, t("admin:validation.noteTooLong")).optional(),
  });
}
export type GrantTierFormValues = {
  amount: number | null;
  currency?: string;
  reference?: string;
  note?: string;
};

/** `RevokeTierRequestValidator`: optional note (≤500). */
export function revokeTierSchema(t: AppTFunction) {
  return z.object({
    note: z.string().max(500, t("admin:validation.noteTooLong")).optional(),
  });
}
export type RevokeTierFormValues = { note?: string };

/** `ResetPasswordRequestValidator`: same policy as change-password. Validates the
 *  client-generated (optionally edited) temp password before submit. */
export function resetPasswordSchema(t: AppTFunction) {
  return z.object({
    newPassword: z
      .string()
      .min(1, t("validation:newPassword.required"))
      .min(PASSWORD_MIN, t("validation:password.min", { min: PASSWORD_MIN }))
      .refine(
        (value) => utf8ByteLength(value) <= PASSWORD_MAX_BYTES,
        t("validation:password.maxBytes", { max: PASSWORD_MAX_BYTES }),
      ),
  });
}
export type ResetPasswordFormValues = { newPassword: string };
