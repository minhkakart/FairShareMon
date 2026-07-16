import { z } from "zod";
import type { AppTFunction } from "@/i18n/useT";

/**
 * Zod schemas mirroring the backend FluentValidation rules
 * (`Validators/Auth/**`). Built as factories so messages are localized with the
 * active `t`. Kept in sync with:
 *   username: 3–32 chars, ^[a-zA-Z0-9_.-]+$ (stored lowercase)
 *   password: min 8 chars, max 72 BYTES (BCrypt limit — byte length, not chars)
 */
export const USERNAME_MIN = 3;
export const USERNAME_MAX = 32;
export const USERNAME_PATTERN = /^[a-zA-Z0-9_.-]+$/;
export const PASSWORD_MIN = 8;
export const PASSWORD_MAX_BYTES = 72;

/** UTF-8 byte length (mirrors the backend's Encoding.UTF8.GetByteCount). */
export function utf8ByteLength(value: string): number {
  return new TextEncoder().encode(value).length;
}

export interface LoginFormValues {
  username: string;
  password: string;
}

export interface RegisterFormValues {
  username: string;
  password: string;
}

export interface ChangePasswordFormValues {
  currentPassword: string;
  newPassword: string;
}

export function loginSchema(t: AppTFunction) {
  return z.object({
    username: z.string().min(1, t("validation:username.required")),
    password: z.string().min(1, t("validation:password.required")),
  });
}

export function registerSchema(t: AppTFunction) {
  return z.object({
    username: z
      .string()
      .min(1, t("validation:username.required"))
      .refine(
        (value) => value.length >= USERNAME_MIN && value.length <= USERNAME_MAX,
        t("validation:username.length", {
          min: USERNAME_MIN,
          max: USERNAME_MAX,
        }),
      )
      .refine(
        (value) => USERNAME_PATTERN.test(value),
        t("validation:username.format"),
      ),
    password: z
      .string()
      .min(1, t("validation:password.required"))
      .min(PASSWORD_MIN, t("validation:password.min", { min: PASSWORD_MIN }))
      .refine(
        (value) => utf8ByteLength(value) <= PASSWORD_MAX_BYTES,
        t("validation:password.maxBytes", { max: PASSWORD_MAX_BYTES }),
      ),
  });
}

export function changePasswordSchema(t: AppTFunction) {
  return z.object({
    currentPassword: z
      .string()
      .min(1, t("validation:currentPassword.required")),
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
