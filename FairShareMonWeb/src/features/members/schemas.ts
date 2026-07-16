import { z } from "zod";
import type { AppTFunction } from "@/i18n/useT";

/**
 * Member form schema — mirrors the backend validators
 * (`CreateMemberRequestValidator` / `UpdateMemberRequestValidator`): name is
 * required (non-empty after trim) and at most 100 chars. Built as a factory so
 * messages are localized with the active `t`. The server stays authoritative:
 * a `1001` `error.fields.name` still surfaces on the field.
 */
export const MEMBER_NAME_MAX = 100;

export function memberFormSchema(t: AppTFunction) {
  return z.object({
    name: z
      .string()
      .trim()
      .min(1, t("validation:member.nameRequired"))
      .max(MEMBER_NAME_MAX, t("validation:member.nameTooLong")),
  });
}

export type MemberFormValues = z.infer<ReturnType<typeof memberFormSchema>>;
