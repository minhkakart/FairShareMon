import type { ButtonHTMLAttributes, ReactNode } from "react";
import { Slot, Slottable } from "@radix-ui/react-slot";
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
  /**
   * Render as the passed child element (Radix `Slot`) instead of a `<button>`,
   * merging the button styling onto it. Use it to give a router `<Link>` button
   * styling as a SINGLE `<a>` — no `<a><button>` nesting (two interactive
   * elements). The child must be exactly one element and carries the accessible
   * name. `loading` / `disabled` / `type` do not apply in this mode (a link has
   * no pending or disabled state); pass exactly the props the anchor needs.
   *
   *   <Button asChild variant="secondary">
   *     <Link to="/settings">Cài đặt</Link>
   *   </Button>
   */
  asChild?: boolean;
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
  asChild = false,
  disabled,
  className,
  children,
  type = "button",
  ...rest
}: ButtonProps) {
  const classes = cx(
    styles.button,
    styles[variant],
    styles[size],
    fullWidth && styles.fullWidth,
    !asChild && loading && styles.loading,
    className,
  );

  // asChild: merge styling onto the caller's element (e.g. a router <Link>) so
  // it renders as a single <a>. `Slottable` marks the child that becomes the
  // anchor; the optional icon spans ride along inside it. No <button>-only props
  // (type/disabled/aria-busy/spinner) here.
  if (asChild) {
    return (
      <Slot className={classes} {...rest}>
        {iconStart ? (
          <span className={styles.icon} aria-hidden="true">
            {iconStart}
          </span>
        ) : null}
        <Slottable>{children}</Slottable>
        {iconEnd ? (
          <span className={styles.icon} aria-hidden="true">
            {iconEnd}
          </span>
        ) : null}
      </Slot>
    );
  }

  return (
    <button
      // eslint-disable-next-line react/button-has-type -- type is a constrained prop with a default
      type={type}
      className={classes}
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
