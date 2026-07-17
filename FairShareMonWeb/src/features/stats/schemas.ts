import { z } from "zod";
import type { AppTFunction } from "@/i18n/useT";

/**
 * Custom-range schema mirroring the backend validator (`StatsRangeRequest`):
 * when both `from` and `to` are provided, `from <= to`. Used to block an invalid
 * custom range client-side before it reaches the API (which would answer `1001`).
 * Bounds are `YYYY-MM-DD` date-only strings, comparable lexicographically. The
 * message resolves through the shared `validation` namespace.
 */
export function customRangeSchema(t: AppTFunction) {
  return z
    .object({
      from: z.string(),
      to: z.string(),
    })
    .superRefine((value, ctx) => {
      if (value.from && value.to && value.from > value.to) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: t("validation:stats.rangeInvalid"),
          path: ["to"],
        });
      }
    });
}

export type CustomRangeValues = z.infer<ReturnType<typeof customRangeSchema>>;
