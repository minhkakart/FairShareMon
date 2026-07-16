import type { FormHTMLAttributes, HTMLAttributes, ReactNode } from "react";
import { cx } from "../utils/cx";
import styles from "./Form.module.css";

/** <form> with a consistent vertical rhythm between fields. */
export function Form({
  className,
  ...rest
}: FormHTMLAttributes<HTMLFormElement>) {
  return <form className={cx(styles.form, className)} {...rest} />;
}

/** A stack of fields (same rhythm as Form; use to group inside a Form). */
export function FieldStack({
  className,
  ...rest
}: HTMLAttributes<HTMLDivElement>) {
  return <div className={cx(styles.stack, className)} {...rest} />;
}

/**
 * Form-level error banner — for a submit failure that isn't tied to one field
 * (e.g. code 2001 invalid credentials, or an unknown-field server error).
 * role="alert" announces it; it is NOT color-only (icon + text).
 */
export function FormError({
  children,
  className,
}: {
  children: ReactNode;
  className?: string;
}) {
  return (
    <div className={cx(styles.formError, className)} role="alert">
      <svg
        className={styles.formErrorIcon}
        viewBox="0 0 20 20"
        aria-hidden="true"
        fill="currentColor"
      >
        <path d="M10 2a8 8 0 100 16 8 8 0 000-16zm0 4a1 1 0 011 1v4a1 1 0 11-2 0V7a1 1 0 011-1zm0 8.5a1.15 1.15 0 110-2.3 1.15 1.15 0 010 2.3z" />
      </svg>
      <span>{children}</span>
    </div>
  );
}

/** Right-aligned actions row (submit / cancel). Stacks on narrow screens. */
export function FormActions({
  className,
  ...rest
}: HTMLAttributes<HTMLDivElement>) {
  return <div className={cx(styles.actions, className)} {...rest} />;
}
