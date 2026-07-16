import type { ReactNode } from "react";
import { cx } from "../utils/cx";
import styles from "./Premium.module.css";

const CrownIcon = (
  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
    <path d="M3 7l4.5 3L12 4l4.5 6L21 7l-1.6 10.2a1 1 0 01-1 .8H5.6a1 1 0 01-1-.8L3 7z" />
  </svg>
);
const CheckIcon = (
  <svg
    viewBox="0 0 20 20"
    fill="none"
    stroke="currentColor"
    strokeWidth="3"
    aria-hidden="true"
  >
    <path d="M4 10l4 4 8-9" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);

export type UpgradePromptVariant =
  | "cta" /* gated feature — gold call-to-action (optional action) */
  | "info" /* informational — how Premium is obtained; no action */
  | "active"; /* confirmation — the account already has Premium */

/**
 * UpgradePrompt — the gold Premium affordance. Three variants share one visual
 * language:
 *  - `cta` (default): a Premium-gated feature (API code 13003). Distinct gold
 *    treatment sets it apart from a generic forbidden error; pass an `action`.
 *  - `info`: an informational panel (no navigating action). Use where Premium
 *    is granted manually — there is no self-serve purchase — so the prompt
 *    explains rather than invites a click that goes nowhere.
 *  - `active`: a subtle confirmation that the account already has Premium (a
 *    check-marked crown, no action).
 * The implementer wires any action; the copy is passed in (localized).
 */
export function UpgradePrompt({
  title,
  description,
  action,
  compact = false,
  variant = "cta",
  className,
}: {
  title: ReactNode;
  description?: ReactNode;
  action?: ReactNode;
  /** Inline banner rather than a full panel. */
  compact?: boolean;
  /** Visual + semantic mode (default `cta`). */
  variant?: UpgradePromptVariant;
  className?: string;
}) {
  return (
    <div
      className={cx(
        styles.upgrade,
        styles[variant],
        compact && styles.compact,
        className,
      )}
      role="status"
    >
      <span className={styles.crown} aria-hidden="true">
        {CrownIcon}
        {variant === "active" ? (
          <span className={styles.crownCheck}>{CheckIcon}</span>
        ) : null}
      </span>
      <div className={styles.body}>
        <p className={styles.title}>{title}</p>
        {description ? <p className={styles.desc}>{description}</p> : null}
        {action ? <div className={styles.action}>{action}</div> : null}
      </div>
    </div>
  );
}

/**
 * LimitNotice — Free-tier create-limit reached (codes 13000 member / 13001 open
 * event / 13002 monthly expense). Friendly, never alarming: existing data is
 * never touched, only new creation is blocked. Pair with an upgrade action.
 */
export function LimitNotice({
  title,
  description,
  action,
  className,
}: {
  title: ReactNode;
  description?: ReactNode;
  action?: ReactNode;
  className?: string;
}) {
  return (
    <div className={cx(styles.limit, className)} role="status">
      <div className={styles.body}>
        <p className={styles.title}>{title}</p>
        {description ? <p className={styles.desc}>{description}</p> : null}
        {action ? <div className={styles.action}>{action}</div> : null}
      </div>
    </div>
  );
}
