import { cx } from "../utils/cx";
import styles from "./Controls.module.css";

/** Matches the backend supported cultures. */
export type Locale = "vi-VN" | "en-US";

const ORDER: Locale[] = ["vi-VN", "en-US"];
const SHORT: Record<Locale, string> = { "vi-VN": "VI", "en-US": "EN" };

export type LanguageToggleProps = {
  value: Locale;
  onChange: (value: Locale) => void;
  /** Localized accessible labels, e.g. { "vi-VN": "Tiếng Việt", "en-US": "English" }. */
  labels: Record<Locale, string>;
  groupLabel: string;
  className?: string;
};

/**
 * Compact language switch (VI / EN). Presentational: the parent owns the locale
 * and must sync it to i18next AND the API client's Accept-Language header, and
 * update <html lang>. The design layer only renders the control.
 */
export function LanguageToggle({
  value,
  onChange,
  labels,
  groupLabel,
  className,
}: LanguageToggleProps) {
  return (
    <div
      className={cx(styles.segmented, className)}
      role="radiogroup"
      aria-label={groupLabel}
    >
      {ORDER.map((option) => (
        <button
          key={option}
          type="button"
          role="radio"
          aria-checked={value === option}
          aria-label={labels[option]}
          title={labels[option]}
          className={cx(
            styles.segment,
            styles.segmentText,
            value === option && styles.segmentActive,
          )}
          onClick={() => onChange(option)}
        >
          {SHORT[option]}
        </button>
      ))}
    </div>
  );
}
