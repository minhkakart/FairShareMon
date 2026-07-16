import type { ButtonHTMLAttributes, ReactNode } from "react";
import { cx } from "../utils/cx";
import { Spinner } from "../Spinner/Spinner";
import styles from "./Button.module.css";

export type ButtonVariant =
  | "primary" /* the one main action per view (jade) */
  | "secondary" /* neutral bordered action */
  | "ghost" /* low-emphasis, text-like */
  | "danger" /* destructive: delete expense, close event */
  | "premium"; /* gold — upgrade / unlock a Premium feature */

export type ButtonSize = "sm" | "md" | "lg";

export type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: ButtonVariant;
  size?: ButtonSize;
  /** Shows a spinner and blocks interaction; keeps width stable. */
  loading?: boolean;
  /** Stretch to the full width of the container (mobile forms, dialogs). */
  fullWidth?: boolean;
  /** Optional leading icon element (decorative — label carries the meaning). */
  iconStart?: ReactNode;
  iconEnd?: ReactNode;
};

/**
 * The primary action primitive. `loading` disables the button and swaps in a
 * spinner while keeping the label for width stability and screen readers
 * (aria-busy communicates the pending state).
 */
export function Button({
  variant = "secondary",
  size = "md",
  loading = false,
  fullWidth = false,
  iconStart,
  iconEnd,
  disabled,
  className,
  children,
  type = "button",
  ...rest
}: ButtonProps) {
  return (
    <button
      // eslint-disable-next-line react/button-has-type -- type is a constrained prop with a default
      type={type}
      className={cx(
        styles.button,
        styles[variant],
        styles[size],
        fullWidth && styles.fullWidth,
        loading && styles.loading,
        className,
      )}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...rest}
    >
      {loading ? (
        <span className={styles.spinnerSlot} aria-hidden="true">
          <Spinner size={size === "sm" ? 14 : 18} />
        </span>
      ) : null}
      <span className={styles.content}>
        {iconStart ? (
          <span className={styles.icon} aria-hidden="true">
            {iconStart}
          </span>
        ) : null}
        {children}
        {iconEnd ? (
          <span className={styles.icon} aria-hidden="true">
            {iconEnd}
          </span>
        ) : null}
      </span>
    </button>
  );
}
