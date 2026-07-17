import type { ReactNode } from "react";
import { TextField } from "@/components/ui";
import { useT } from "@/i18n/useT";
import type { RangePreset, RangeValue } from "../dateRange";
import { isCustomRangeInvalid } from "../dateRange";
import styles from "./stats.module.css";

const PRESETS: RangePreset[] = [
  "thisMonth",
  "last30Days",
  "thisYear",
  "allTime",
  "custom",
];

export interface StatsRangeControlProps {
  value: RangeValue;
  onChange: (value: RangeValue) => void;
  /** A server-returned `1001` message to surface inline (client-side normally
   *  prevents this). */
  apiError?: ReactNode;
}

/**
 * The date-range control (OQ4a): preset chips (This month · Last 30 days · This
 * year · All time) + a Custom mode revealing two date inputs. Controlled via
 * `value` + `onChange`. Accessible: `role="group"` + label; the active preset
 * carries `aria-pressed` (state is not color-alone). An inverted custom range
 * (`from > to`) shows an inline message; the page skips the query while invalid.
 */
export function StatsRangeControl({
  value,
  onChange,
  apiError,
}: StatsRangeControlProps) {
  const { t } = useT();
  const invalid = isCustomRangeInvalid(value);
  const rangeMessage = t("validation:stats.rangeInvalid");

  return (
    <div
      className={styles.rangeControl}
      role="group"
      aria-label={t("stats:range.label")}
    >
      <span className={styles.rangeLabel}>{t("stats:range.label")}</span>
      <div className={styles.presetChips}>
        {PRESETS.map((preset) => (
          <button
            key={preset}
            type="button"
            className={styles.chip}
            aria-pressed={value.preset === preset}
            onClick={() => onChange({ ...value, preset })}
          >
            {t(`stats:range.preset.${preset}`)}
          </button>
        ))}
      </div>

      {value.preset === "custom" ? (
        <>
          <div className={styles.customRow}>
            <TextField
              className={styles.customField}
              label={t("stats:range.from")}
              type="date"
              value={value.from}
              max={value.to || undefined}
              onChange={(e) => onChange({ ...value, from: e.target.value })}
            />
            <TextField
              className={styles.customField}
              label={t("stats:range.to")}
              type="date"
              value={value.to}
              min={value.from || undefined}
              onChange={(e) => onChange({ ...value, to: e.target.value })}
              error={invalid ? rangeMessage : undefined}
            />
          </div>
        </>
      ) : null}

      {apiError ? (
        <p className={styles.rangeInvalid} role="alert">
          {apiError}
        </p>
      ) : null}
    </div>
  );
}
