import type { CSSProperties, ReactNode } from "react";
import { useId, useRef, useState } from "react";
import { cx } from "../utils/cx";
import styles from "./ColorPicker.module.css";

/**
 * Curated category palette — the 5 backend seed colors plus a chart-friendly
 * spread. Stored VERBATIM as `#RRGGBB`, so these are literal hex constants (not
 * theme-varying tokens): the same value must render identically on charts and in
 * both light/dark. Seeds: Ăn uống #F97316 · Đi lại #3B82F6 · Khách sạn #8B5CF6 ·
 * Mua sắm #EC4899 · Khác #6B7280.
 */
export const CURATED_COLORS = [
  "#E34948", // red
  "#F97316", // orange — seed (Ăn uống)
  "#EDA100", // amber
  "#0CA30C", // green
  "#14A074", // jade — brand
  "#1BAF7A", // aqua
  "#0EA5E9", // sky
  "#3B82F6", // blue — seed (Đi lại)
  "#4A3AA7", // indigo
  "#8B5CF6", // violet — seed (Khách sạn)
  "#EC4899", // pink — seed (Mua sắm)
  "#6B7280", // gray — seed (Khác)
] as const;

const HEX = /^#[0-9A-Fa-f]{6}$/;
const eq = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();

export type ColorPickerProps = {
  /** Current color as `#RRGGBB` (may be empty before a default is seeded). */
  value: string;
  /** Called with a validated, upper-cased `#RRGGBB`. */
  onChange: (value: string) => void;
  /** Visible group label — names the whole control and the swatch radiogroup. */
  label: ReactNode;
  /** Accessible label for the custom hex text input (and the native picker). */
  hexLabel: string;
  /** Form-level error (e.g. RHF `color` error). Rendered with role="alert". */
  error?: ReactNode;
  /** Shown under the hex input when the typed value is not a valid hex. */
  invalidHexMessage?: string;
  required?: boolean;
  /** Override the curated palette. */
  colors?: readonly string[];
  /** Accessible name per swatch (defaults to its hex). */
  getSwatchLabel?: (hex: string) => string;
  id?: string;
  className?: string;
};

/**
 * Curated color palette (radiogroup of swatches) plus a custom-hex path — a
 * native color input and a hex text field — all yielding a validated `#RRGGBB`.
 *
 * Presentational + controlled: the parent owns `value`/`onChange` (wire it to
 * RHF) and passes localized strings. Selection is conveyed by a shape halo + a
 * corner check on a surface disc (contrast-safe over any swatch color) plus
 * `aria-checked`, never by color alone. Swatches are keyboard-navigable
 * (arrow keys / Home / End) via a roving tabindex.
 */
export function ColorPicker({
  value,
  onChange,
  label,
  hexLabel,
  error,
  invalidHexMessage,
  required,
  colors = CURATED_COLORS,
  getSwatchLabel,
  id,
  className,
}: ColorPickerProps) {
  const autoId = useId();
  const fieldId = id ?? autoId;
  const labelId = `${fieldId}-label`;
  const errorId = `${fieldId}-error`;
  const hexId = `${fieldId}-hex`;
  const hexErrorId = `${fieldId}-hex-error`;

  const optionRefs = useRef<Array<HTMLButtonElement | null>>([]);

  // Keep the hex text input in sync with `value` while letting the user type a
  // partial/invalid hex without clobbering the last committed color (the
  // "adjust state during render" pattern — no effect needed).
  const [hexText, setHexText] = useState(value);
  const [prevValue, setPrevValue] = useState(value);
  if (value !== prevValue) {
    setPrevValue(value);
    setHexText(value);
  }

  const selectedIndex = colors.findIndex((c) => eq(c, value));
  const focusIndex = selectedIndex >= 0 ? selectedIndex : 0;
  // The native color control normalizes to lowercase internally — feed it a
  // lowercased hex so React's controlled value matches the DOM value.
  const nativeValue = HEX.test(value) ? value.toLowerCase() : "#000000";
  const typedInvalid = hexText.trim() !== "" && !HEX.test(hexText.trim());

  const invalid = Boolean(error);
  const describedBy = cx(invalid ? errorId : undefined) || undefined;

  const selectAt = (index: number) => {
    const next = ((index % colors.length) + colors.length) % colors.length;
    optionRefs.current[next]?.focus();
    onChange(colors[next].toUpperCase());
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
        selectAt(colors.length - 1);
        break;
      default:
        break;
    }
  };

  const handleHexText = (raw: string) => {
    setHexText(raw);
    const trimmed = raw.trim();
    if (HEX.test(trimmed)) onChange(trimmed.toUpperCase());
  };

  return (
    <div className={cx(styles.field, className)}>
      <span className={styles.label} id={labelId}>
        {label}
        {required ? (
          <span className={styles.required} aria-hidden="true">
            *
          </span>
        ) : null}
      </span>

      <div
        className={styles.swatches}
        role="radiogroup"
        aria-labelledby={labelId}
        aria-describedby={describedBy}
      >
        {colors.map((hex, index) => {
          const selected = eq(hex, value);
          return (
            <button
              key={hex}
              type="button"
              role="radio"
              ref={(el) => {
                optionRefs.current[index] = el;
              }}
              aria-checked={selected}
              aria-label={getSwatchLabel ? getSwatchLabel(hex) : hex}
              title={getSwatchLabel ? getSwatchLabel(hex) : hex}
              tabIndex={index === focusIndex ? 0 : -1}
              className={cx(styles.swatch, selected && styles.swatchSelected)}
              style={{ "--swatch-color": hex } as CSSProperties}
              onClick={() => onChange(hex.toUpperCase())}
              onKeyDown={(event) => onKeyDown(event, index)}
            >
              {selected ? (
                <span className={styles.check} aria-hidden="true">
                  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor">
                    <path
                      d="M4 10l4 4 8-9"
                      strokeWidth="2.5"
                      strokeLinecap="round"
                      strokeLinejoin="round"
                    />
                  </svg>
                </span>
              ) : null}
            </button>
          );
        })}
      </div>

      <div className={styles.custom}>
        <label className={styles.customPreview} htmlFor={`${hexId}-native`}>
          <span
            className={styles.previewSwatch}
            style={{ "--swatch-color": nativeValue } as CSSProperties}
            aria-hidden="true"
          />
          <input
            id={`${hexId}-native`}
            type="color"
            className={styles.nativeInput}
            value={nativeValue}
            aria-label={hexLabel}
            onChange={(event) => onChange(event.target.value.toUpperCase())}
          />
        </label>

        <span className={styles.hexField}>
          <label className={styles.hexLabel} htmlFor={hexId}>
            {hexLabel}
          </label>
          <input
            id={hexId}
            type="text"
            inputMode="text"
            spellCheck={false}
            autoCapitalize="characters"
            className={cx(styles.hexInput, typedInvalid && styles.hexInvalid)}
            value={hexText}
            placeholder="#RRGGBB"
            maxLength={7}
            aria-invalid={typedInvalid || undefined}
            aria-describedby={typedInvalid ? hexErrorId : undefined}
            onChange={(event) => handleHexText(event.target.value)}
          />
        </span>
      </div>

      {typedInvalid && invalidHexMessage ? (
        <p id={hexErrorId} className={styles.error} role="alert">
          {invalidHexMessage}
        </p>
      ) : null}

      {invalid ? (
        <p id={errorId} className={styles.error} role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}
