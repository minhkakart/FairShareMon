import { useRef } from "react";
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
  const optionRefs = useRef<Array<HTMLButtonElement | null>>([]);

  const selectedIndex = ORDER.indexOf(value);
  const focusIndex = selectedIndex >= 0 ? selectedIndex : 0;

  // WAI-ARIA radiogroup pattern: arrow keys / Home / End move focus AND select
  // (a single tab stop lands on the checked option; the rest are removed from
  // the tab order). Space/Enter select via the button's native click.
  const selectAt = (index: number) => {
    const next = ((index % ORDER.length) + ORDER.length) % ORDER.length;
    optionRefs.current[next]?.focus();
    onChange(ORDER[next]);
  };

  const onKeyDown = (event: React.KeyboardEvent, index: number) => {
    switch (event.key) {
      case "ArrowRight":
      case "ArrowDown":
        event.preventDefault();
        selectAt(index + 1);
        break;
      case "ArrowLeft":
      case "ArrowUp":
        event.preventDefault();
        selectAt(index - 1);
        break;
      case "Home":
        event.preventDefault();
        selectAt(0);
        break;
      case "End":
        event.preventDefault();
        selectAt(ORDER.length - 1);
        break;
      default:
        break;
    }
  };

  return (
    <div
      className={cx(styles.segmented, className)}
      role="radiogroup"
      aria-label={groupLabel}
    >
      {ORDER.map((option, index) => (
        <button
          key={option}
          type="button"
          role="radio"
          ref={(el) => {
            optionRefs.current[index] = el;
          }}
          aria-checked={value === option}
          aria-label={labels[option]}
          title={labels[option]}
          tabIndex={index === focusIndex ? 0 : -1}
          className={cx(
            styles.segment,
            styles.segmentText,
            value === option && styles.segmentActive,
          )}
          onClick={() => onChange(option)}
          onKeyDown={(event) => onKeyDown(event, index)}
        >
          {SHORT[option]}
        </button>
      ))}
    </div>
  );
}
