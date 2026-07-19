import { useRef } from "react";
import { cx } from "../utils/cx";
import styles from "./Controls.module.css";

export type ThemePreference = "light" | "dark" | "system";

const SunIcon = (
  <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
    <path d="M10 6a4 4 0 100 8 4 4 0 000-8zm0-5a1 1 0 011 1v1.5a1 1 0 11-2 0V2a1 1 0 011-1zm0 15a1 1 0 011 1v1.5a1 1 0 11-2 0V17a1 1 0 011-1zM3.5 3.5a1 1 0 011.4 0l1 1A1 1 0 114.5 5.9l-1-1a1 1 0 010-1.4zm11 11a1 1 0 011.4 0l1 1a1 1 0 01-1.4 1.4l-1-1a1 1 0 010-1.4zM1 10a1 1 0 011-1h1.5a1 1 0 110 2H2a1 1 0 01-1-1zm15 0a1 1 0 011-1h1.5a1 1 0 110 2H17a1 1 0 01-1-1zM4.5 14.1a1 1 0 010 1.4l-1 1a1 1 0 01-1.4-1.4l1-1a1 1 0 011.4 0zm11-11a1 1 0 010 1.4l-1 1A1 1 0 1114.1 4.1l1-1a1 1 0 011.4 0z" />
  </svg>
);
const MoonIcon = (
  <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
    <path d="M17 11.2A7 7 0 118.8 3a5.5 5.5 0 108.2 8.2z" />
  </svg>
);
const SystemIcon = (
  <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
    <path d="M3 4h14a1 1 0 011 1v8a1 1 0 01-1 1h-5v2h2a1 1 0 110 2H6a1 1 0 110-2h2v-2H3a1 1 0 01-1-1V5a1 1 0 011-1zm1 2v6h12V6H4z" />
  </svg>
);

const ORDER: ThemePreference[] = ["light", "system", "dark"];
const ICONS: Record<ThemePreference, typeof SunIcon> = {
  light: SunIcon,
  system: SystemIcon,
  dark: MoonIcon,
};

export type ThemeToggleProps = {
  value: ThemePreference;
  onChange: (value: ThemePreference) => void;
  /** Localized accessible labels for each option. */
  labels: Record<ThemePreference, string>;
  /** Group label for screen readers, e.g. "Giao diện". */
  groupLabel: string;
  className?: string;
};

/**
 * Segmented theme control (light / system / dark). Presentational: the parent
 * owns the preference and is responsible for stamping <html data-theme> and
 * persisting it. `system` means "follow the OS" — remove the attribute.
 */
export function ThemeToggle({
  value,
  onChange,
  labels,
  groupLabel,
  className,
}: ThemeToggleProps) {
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
            value === option && styles.segmentActive,
          )}
          onClick={() => onChange(option)}
          onKeyDown={(event) => onKeyDown(event, index)}
        >
          <span className={styles.segmentIcon} aria-hidden="true">
            {ICONS[option]}
          </span>
        </button>
      ))}
    </div>
  );
}
