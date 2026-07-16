import type { ReactNode } from "react";
import { cx } from "../utils/cx";
import styles from "./Premium.module.css";

const CrownIcon = (
  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
    <path d="M3 7l4.5 3L12 4l4.5 6L21 7l-1.6 10.2a1 1 0 01-1 .8H5.6a1 1 0 01-1-.8L3 7z" />
  </svg>
);

/**
 * UpgradePrompt — the affordance for a Premium-gated feature (API code 13003,
 * "Premium feature required"). Distinct gold treatment marks it apart from a
 * generic forbidden error. Wallet mutations, QR generation, and extra export
 * formats surface this. The implementer wires the upgrade action.
 */
export function UpgradePrompt({
  title,
  description,
  action,
  compact = false,
  className,
}: {
  title: ReactNode;
  description?: ReactNode;
  action?: ReactNode;
  /** Inline banner rather than a full panel. */
  compact?: boolean;
  className?: string;
}) {
  return (
    <div
      className={cx(styles.upgrade, compact && styles.compact, className)}
      role="status"
    >
      <span className={styles.crown} aria-hidden="true">
        {CrownIcon}
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
