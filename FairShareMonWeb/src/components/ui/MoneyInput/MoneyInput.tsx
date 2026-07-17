import type { ReactNode, Ref } from "react";
import { useId, useState } from "react";
import { cx } from "../utils/cx";
import styles from "./MoneyInput.module.css";

/** Default whole-VND grouping (vi-VN thousands, no currency symbol, no decimals).
 *  The implementer SHOULD inject the app's shared grouping formatter via `format`
 *  so there is one formatter of record; this is only a self-contained fallback. */
const defaultGrouping = new Intl.NumberFormat("vi-VN", {
  maximumFractionDigits: 0,
  useGrouping: true,
});
const formatGroupedVnd = (n: number) => defaultGrouping.format(n);

/** Keep digits only → a non-negative integer, or null when empty. */
function parseWholeVnd(raw: string): number | null {
  const digits = raw.replace(/\D+/g, "");
  if (digits === "") return null;
  // Number is safe here: the tier caps bound realistic amounts well under 2^53.
  return Number.parseInt(digits, 10);
}

export type MoneyInputProps = {
  /** Current amount as a whole-VND integer, or `null` when empty. */
  value: number | null;
  /** Emits the parsed integer (≥ min), or `null` when cleared. */
  onChange: (value: number | null) => void;
  /** Visible label — required for accessibility. */
  label: ReactNode;
  hint?: ReactNode;
  /** Error message — sets invalid styling + aria-describedby. */
  error?: ReactNode;
  required?: boolean;
  disabled?: boolean;
  /**
   * Visually hide the label while keeping it for assistive tech. Use only when an
   * adjacent visible header already labels the field (e.g. a share-editor row).
   */
  hideLabelVisually?: boolean;
  placeholder?: string;
  /** Minimum accepted value (default 0 — negatives are impossible: digits only). */
  min?: number;
  /** Optional maximum; the value is clamped on blur. */
  max?: number;
  /** Trailing unit addon (default the đồng sign "₫"). Pass null to hide. */
  unit?: ReactNode;
  /** Inject the app's shared grouping formatter; defaults to a vi-VN fallback. */
  format?: (value: number) => string;
  id?: string;
  name?: string;
  className?: string;
  /** Extra ids to append to aria-describedby (e.g. a shared editor description). */
  ariaDescribedBy?: string;
  ref?: Ref<HTMLInputElement>;
};

/**
 * Whole-VND numeric input. VND has no minor unit, so this accepts integers only
 * (digits are stripped; negatives are impossible) and emits a plain `number`
 * (or `null`). It shows a grouped figure (`1.234.567`) while blurred for
 * readability and the raw digits while focused for easy editing — no caret
 * jumps. Pairs with the `Money` display primitive (same vi-VN, 0-decimal rule).
 *
 * Presentational + controlled: wire `value`/`onChange` to RHF (a `Controller`,
 * since the value is a number) and pass localized label/hint/error strings.
 */
export function MoneyInput({
  value,
  onChange,
  label,
  hint,
  error,
  required,
  disabled,
  hideLabelVisually,
  placeholder,
  min = 0,
  max,
  unit = "₫",
  format = formatGroupedVnd,
  id,
  name,
  className,
  ariaDescribedBy,
  ref,
}: MoneyInputProps) {
  const autoId = useId();
  const fieldId = id ?? autoId;
  const hintId = `${fieldId}-hint`;
  const errorId = `${fieldId}-error`;
  const invalid = Boolean(error);
  const [focused, setFocused] = useState(false);

  const describedBy =
    cx(
      hint && !invalid ? hintId : undefined,
      invalid ? errorId : undefined,
      ariaDescribedBy,
    ) || undefined;

  // Focused → raw digits for frictionless editing; blurred → grouped display.
  const display =
    value == null ? "" : focused ? String(value) : format(value);

  const handleChange = (raw: string) => {
    onChange(parseWholeVnd(raw));
  };

  const handleBlur = () => {
    setFocused(false);
    if (value == null) return;
    let next = value;
    if (next < min) next = min;
    if (max != null && next > max) next = max;
    if (next !== value) onChange(next);
  };

  return (
    <div className={cx(styles.field, className)}>
      <label
        className={cx(styles.label, hideLabelVisually && styles.labelHidden)}
        htmlFor={fieldId}
      >
        <span>{label}</span>
        {required ? (
          <span className={styles.required} aria-hidden="true">
            *
          </span>
        ) : null}
      </label>

      <div className={cx(styles.control, invalid && styles.controlInvalid)}>
        <input
          id={fieldId}
          ref={ref}
          name={name}
          className={styles.input}
          type="text"
          inputMode="numeric"
          autoComplete="off"
          value={display}
          placeholder={placeholder}
          disabled={disabled}
          aria-invalid={invalid || undefined}
          aria-describedby={describedBy}
          aria-required={required || undefined}
          onFocus={() => setFocused(true)}
          onBlur={handleBlur}
          onChange={(event) => handleChange(event.target.value)}
        />
        {unit != null ? (
          <span className={styles.unit} aria-hidden="true">
            {unit}
          </span>
        ) : null}
      </div>

      {hint && !invalid ? (
        <p id={hintId} className={styles.hint}>
          {hint}
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
