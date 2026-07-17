import type { ReactNode } from "react";
import { TextField } from "@/components/ui";
import { useT } from "@/i18n/useT";
import type { Bucket } from "../../api/types";
import type { RangePreset, RangeValue } from "../../dateRange";
import { isCustomRangeInvalid } from "../../dateRange";
import styles from "../admin.module.css";

const PRESETS: RangePreset[] = [
  "thisMonth",
  "last30Days",
  "thisYear",
  "allTime",
  "custom",
];
const BUCKETS: Bucket[] = ["month", "day"];

export interface AdminRangeControlProps {
  value: RangeValue;
  onChange: (value: RangeValue) => void;
  /** A server-returned `1001` message to surface inline (client-side normally
   *  prevents this). */
  apiError?: ReactNode;
}

/**
 * The dashboard date-range control (M8): preset chips + a Custom mode revealing
 * two date inputs, plus a month/day bucket toggle for the over-time charts.
 * Controlled via `value`/`onChange`. Accessible: `role="group"` + label; active
 * preset/bucket carry `aria-pressed` (state is not color-alone). An inverted
 * custom range (`from > to`) shows an inline message; the page skips the query
 * while invalid.
 */
export function AdminRangeControl({
  value,
  onChange,
  apiError,
}: AdminRangeControlProps) {
  const { t } = useT();
  const invalid = isCustomRangeInvalid(value);
  const rangeMessage = t("admin:range.invalid");

  return (
    <div
      className={styles.rangeControl}
      role="group"
      aria-label={t("admin:range.label")}
    >
      <span className={styles.rangeLabel}>{t("admin:range.label")}</span>
      <div className={styles.presetChips}>
        {PRESETS.map((preset) => (
          <button
            key={preset}
            type="button"
            className={styles.chip}
            aria-pressed={value.preset === preset}
            onClick={() => onChange({ ...value, preset })}
          >
            {t(`admin:range.preset.${preset}`)}
          </button>
        ))}
      </div>

      {value.preset === "custom" ? (
        <div className={styles.customRow}>
          <TextField
            className={styles.customField}
            label={t("admin:range.from")}
            type="date"
            value={value.from}
            max={value.to || undefined}
            onChange={(e) => onChange({ ...value, from: e.target.value })}
          />
          <TextField
            className={styles.customField}
            label={t("admin:range.to")}
            type="date"
            value={value.to}
            min={value.from || undefined}
            onChange={(e) => onChange({ ...value, to: e.target.value })}
            error={invalid ? rangeMessage : undefined}
          />
        </div>
      ) : null}

      <div className={styles.bucketRow} role="group" aria-label={t("admin:range.bucketLabel")}>
        <span className={styles.rangeLabel}>{t("admin:range.bucketLabel")}</span>
        {BUCKETS.map((bucket) => (
          <button
            key={bucket}
            type="button"
            className={styles.chip}
            aria-pressed={value.bucket === bucket}
            onClick={() => onChange({ ...value, bucket })}
          >
            {t(`admin:range.bucket.${bucket}`)}
          </button>
        ))}
      </div>

      {apiError ? (
        <p className={styles.rangeInvalid} role="alert">
          {apiError}
        </p>
      ) : null}
    </div>
  );
}
