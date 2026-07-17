import { z } from "zod";
import type { AppTFunction } from "@/i18n/useT";

/**
 * Category form schema — mirrors the backend validators
 * (`CreateCategoryRequestValidator` / `UpdateCategoryRequestValidator`): name is
 * required (non-empty after trim) 1–100; color required + valid `#RRGGBB`; icon
 * optional ≤50. Built as a factory so messages are localized with the active `t`.
 * The server stays authoritative — a `1001` `error.fields.*` still surfaces on
 * the matching field, and a `4001` name-duplicate maps onto `name`.
 */
export const CATEGORY_NAME_MAX = 100;
export const CATEGORY_ICON_MAX = 50;
export const HEX_COLOR = /^#[0-9A-Fa-f]{6}$/;

export function categoryFormSchema(t: AppTFunction) {
  return z.object({
    name: z
      .string()
      .trim()
      .min(1, t("validation:category.nameRequired"))
      .max(CATEGORY_NAME_MAX, t("validation:category.nameTooLong")),
    color: z.string().regex(HEX_COLOR, t("validation:category.colorInvalid")),
    icon: z
      .string()
      .trim()
      .max(CATEGORY_ICON_MAX, t("validation:category.iconTooLong"))
      .nullable()
      .optional(),
  });
}

export type CategoryFormValues = z.infer<ReturnType<typeof categoryFormSchema>>;
