import { z } from "zod";
import type { AppTFunction } from "@/i18n/useT";

/**
 * Tag form schema — mirrors the backend validators (`CreateTagRequestValidator` /
 * `UpdateTagRequestValidator`): name required (non-empty after trim) 1–100. Built
 * as a factory so messages are localized with the active `t`. The server stays
 * authoritative — a `1001` `error.fields.name` still surfaces on the field, and a
 * `5001` name-duplicate maps onto `name`.
 */
export const TAG_NAME_MAX = 100;

export function tagFormSchema(t: AppTFunction) {
  return z.object({
    name: z
      .string()
      .trim()
      .min(1, t("validation:tag.nameRequired"))
      .max(TAG_NAME_MAX, t("validation:tag.nameTooLong")),
  });
}

export type TagFormValues = z.infer<ReturnType<typeof tagFormSchema>>;
