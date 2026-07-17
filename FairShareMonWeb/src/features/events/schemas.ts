import { z } from "zod";
import type { AppTFunction } from "@/i18n/useT";

/**
 * Event form schema — a localized factory mirroring the backend
 * `CreateEventRequestValidator` / `UpdateEventRequestValidator`: name required
 * 1–200; description ≤1000; start/end required; `endDate >= startDate` (mirrors
 * the `1001` validator + the DB `ck_events_date_range` CHECK). The date fields
 * are the raw `<input type="date">` "YYYY-MM-DD" values; lexicographic order on
 * that format equals chronological order, so the range refinement is exact. The
 * server stays authoritative — a `1001` `error.fields.*` still surfaces onto the
 * matching field, and `9003` (range excludes assigned expense) surfaces too.
 */
export const EVENT_NAME_MAX = 200;
export const EVENT_DESC_MAX = 1000;

export function eventFormSchema(t: AppTFunction) {
  return z
    .object({
      name: z
        .string()
        .trim()
        .min(1, t("validation:event.nameRequired"))
        .max(EVENT_NAME_MAX, t("validation:event.nameTooLong")),
      description: z
        .string()
        .max(EVENT_DESC_MAX, t("validation:event.descriptionTooLong"))
        .optional(),
      startDate: z.string().min(1, t("validation:event.startRequired")),
      endDate: z.string().min(1, t("validation:event.endRequired")),
    })
    .superRefine((values, ctx) => {
      if (
        values.startDate &&
        values.endDate &&
        values.endDate < values.startDate
      ) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: t("validation:event.rangeInvalid"),
          path: ["endDate"],
        });
      }
    });
}

export type EventFormValues = z.infer<ReturnType<typeof eventFormSchema>>;
