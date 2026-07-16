import type { InputHTMLAttributes, ReactNode, Ref } from "react";
import { useId } from "react";
import { cx } from "../utils/cx";
import styles from "./TextField.module.css";

export type TextFieldProps = Omit<
  InputHTMLAttributes<HTMLInputElement>,
  "id"
> & {
  /** Visible label — required for accessibility. */
  label: ReactNode;
  /** Helper text under the field (uses secondary ink, AA-legible). */
  hint?: ReactNode;
  /**
   * Error message. When present the field is styled invalid, aria-invalid is
   * set, and the message is associated via aria-describedby. Pass the server
   * `error.fields[field]` or the RHF field error message straight through.
   */
  error?: ReactNode;
  /** Marks the label with a required asterisk (visual + SR text). */
  required?: boolean;
  /** Optional element rendered inside the field on the trailing edge. */
  addonEnd?: ReactNode;
  id?: string;
  /** Forwarded to the input — RHF's register() ref lands here (React 19). */
  ref?: Ref<HTMLInputElement>;
};

/**
 * Labeled text input with hint + error, fully wired for a11y:
 *   <label for> ↔ input id, aria-invalid, aria-describedby → hint & error.
 * Presentational only — the implementer supplies value/onChange (or RHF
 * register spread) and the localized label/hint/error strings.
 */
export function TextField({
  label,
  hint,
  error,
  required,
  addonEnd,
  id,
  className,
  ref,
  ...inputProps
}: TextFieldProps) {
  const autoId = useId();
  const fieldId = id ?? autoId;
  const hintId = `${fieldId}-hint`;
  const errorId = `${fieldId}-error`;
  const invalid = Boolean(error);

  const describedBy =
    cx(hint ? hintId : undefined, invalid ? errorId : undefined) || undefined;

  return (
    <div className={cx(styles.field, className)}>
      <label className={styles.label} htmlFor={fieldId}>
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
          className={styles.input}
          aria-invalid={invalid || undefined}
          aria-describedby={describedBy}
          aria-required={required || undefined}
          {...inputProps}
        />
        {addonEnd ? <span className={styles.addonEnd}>{addonEnd}</span> : null}
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
