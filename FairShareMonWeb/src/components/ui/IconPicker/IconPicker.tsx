import type { ReactNode } from "react";
import { useId, useRef } from "react";
import { cx } from "../utils/cx";
import styles from "./IconPicker.module.css";

/**
 * Curated emoji palette for category icons. The backend stores the icon as a
 * free string and seeds emoji glyphs DIRECTLY (no icon-font key layer), so the
 * picker selects an emoji and stores that glyph verbatim. This set is a superset
 * that includes all five seed glyphs: 🍜 (Ăn uống) · 🚗 (Đi lại) · 🏨 (Khách
 * sạn) · 🛍️ (Mua sắm) · ⋯ (Khác). Ordered loosely by theme (food → shopping →
 * transport → stay → bills → leisure → misc) for scannability.
 */
export const CURATED_ICONS = [
  "🍜", "🍚", "🍲", "☕", "🍺", "🎂",
  "🛒", "🛍️", "🎁", "👕",
  "🚗", "🚕", "🛵", "🚌", "✈️", "⛽",
  "🏨", "🏠", "⛺",
  "💡", "💧", "📱", "🧾",
  "🎬", "🎮", "🎤", "⚽", "💊", "💰",
  "⋯",
] as const;

export type IconPickerProps = {
  /** Current icon (an emoji glyph) or `null` for no icon. */
  value: string | null;
  /** Called with the chosen emoji glyph, or `null` when "no icon" is picked. */
  onChange: (value: string | null) => void;
  /** Visible group label — names the control and the radiogroup. */
  label: ReactNode;
  /** Visible + accessible label for the "no icon" option (e.g. "Không có"). */
  noIconLabel: string;
  /** Form-level error (e.g. RHF `icon` error). Rendered with role="alert". */
  error?: ReactNode;
  /** Override the curated emoji set. */
  icons?: readonly string[];
  id?: string;
  className?: string;
};

const ClearIcon = (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" aria-hidden="true">
    <circle cx="10" cy="10" r="7" strokeWidth="1.6" />
    <path d="M5.4 5.4l9.2 9.2" strokeWidth="1.6" strokeLinecap="round" />
  </svg>
);

/**
 * Curated emoji palette as a labelled radiogroup, plus a "no icon" option (icon
 * is optional). Emits the emoji glyph verbatim (or `null`). Presentational +
 * controlled: the parent owns `value`/`onChange` and passes localized strings.
 *
 * Accessibility: each option is a `radio` with an accessible name (the emoji's
 * own name for glyphs; `noIconLabel` for the clear option); selection is shown
 * by a ring + tint + `aria-checked`, never by color alone; the grid is
 * keyboard-navigable (arrow keys / Home / End) via a roving tabindex.
 */
export function IconPicker({
  value,
  onChange,
  label,
  noIconLabel,
  error,
  icons = CURATED_ICONS,
  id,
  className,
}: IconPickerProps) {
  const autoId = useId();
  const fieldId = id ?? autoId;
  const labelId = `${fieldId}-label`;
  const errorId = `${fieldId}-error`;

  // Option 0 is the "no icon" (null) choice; the rest are the emoji glyphs.
  const options: Array<string | null> = [null, ...icons];
  const optionRefs = useRef<Array<HTMLButtonElement | null>>([]);

  const selectedIndex = options.findIndex((option) => option === value);
  const focusIndex = selectedIndex >= 0 ? selectedIndex : 0;

  const invalid = Boolean(error);
  const describedBy = invalid ? errorId : undefined;

  const selectAt = (index: number) => {
    const next = ((index % options.length) + options.length) % options.length;
    optionRefs.current[next]?.focus();
    onChange(options[next]);
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
        selectAt(options.length - 1);
        break;
      default:
        break;
    }
  };

  return (
    <div className={cx(styles.field, className)}>
      <span className={styles.label} id={labelId}>
        {label}
      </span>

      <div
        className={styles.grid}
        role="radiogroup"
        aria-labelledby={labelId}
        aria-describedby={describedBy}
      >
        {options.map((option, index) => {
          const selected = option === value;
          const isClear = option === null;
          return (
            <button
              key={isClear ? "__none__" : option}
              type="button"
              role="radio"
              ref={(el) => {
                optionRefs.current[index] = el;
              }}
              aria-checked={selected}
              aria-label={isClear ? noIconLabel : undefined}
              title={isClear ? noIconLabel : undefined}
              tabIndex={index === focusIndex ? 0 : -1}
              className={cx(
                styles.option,
                isClear && styles.clear,
                selected && styles.optionSelected,
              )}
              onClick={() => onChange(option)}
              onKeyDown={(event) => onKeyDown(event, index)}
            >
              {isClear ? (
                <span className={styles.clearIcon}>{ClearIcon}</span>
              ) : (
                <span className={styles.glyph}>{option}</span>
              )}
            </button>
          );
        })}
      </div>

      {invalid ? (
        <p id={errorId} className={styles.error} role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}
