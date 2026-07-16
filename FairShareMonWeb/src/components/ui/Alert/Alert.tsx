import type { ReactNode } from "react";
import { cx } from "../utils/cx";
import styles from "./Alert.module.css";

export type AlertTone = "info" | "success" | "warning" | "danger";

const ICONS: Record<AlertTone, ReactNode> = {
  info: (
    <path d="M10 2a8 8 0 100 16 8 8 0 000-16zm0 4a1 1 0 011 1v4a1 1 0 11-2 0V7a1 1 0 011-1zm0 8.5a1.15 1.15 0 110-2.3 1.15 1.15 0 010 2.3z" />
  ),
  success: (
    <path d="M10 2a8 8 0 100 16 8 8 0 000-16zm3.7 5.3a1 1 0 010 1.4l-4 4a1 1 0 01-1.4 0l-2-2a1 1 0 111.4-1.4l1.3 1.3 3.3-3.3a1 1 0 011.4 0z" />
  ),
  warning: (
    <path d="M9.1 3.2a1 1 0 011.8 0l6.7 12.3a1 1 0 01-.9 1.5H3.3a1 1 0 01-.9-1.5L9.1 3.2zM10 7a1 1 0 00-1 1v3a1 1 0 102 0V8a1 1 0 00-1-1zm0 7.5a1.1 1.1 0 110-2.2 1.1 1.1 0 010 2.2z" />
  ),
  danger: (
    <path d="M10 2a8 8 0 100 16 8 8 0 000-16zm0 4a1 1 0 011 1v4a1 1 0 11-2 0V7a1 1 0 011-1zm0 8.5a1.15 1.15 0 110-2.3 1.15 1.15 0 010 2.3z" />
  ),
};

export type AlertProps = {
  tone?: AlertTone;
  title?: ReactNode;
  children?: ReactNode;
  /** Optional actions row (e.g. a Retry button). */
  action?: ReactNode;
  className?: string;
};

/**
 * Inline callout. Always icon + text so meaning is not color-only.
 * `danger` announces via role="alert"; the rest use role="status".
 */
export function Alert({
  tone = "info",
  title,
  children,
  action,
  className,
}: AlertProps) {
  return (
    <div
      className={cx(styles.alert, styles[tone], className)}
      role={tone === "danger" ? "alert" : "status"}
    >
      <svg
        className={styles.icon}
        viewBox="0 0 20 20"
        aria-hidden="true"
        fill="currentColor"
      >
        {ICONS[tone]}
      </svg>
      <div className={styles.content}>
        {title ? <p className={styles.title}>{title}</p> : null}
        {children ? <div className={styles.body}>{children}</div> : null}
        {action ? <div className={styles.action}>{action}</div> : null}
      </div>
    </div>
  );
}
