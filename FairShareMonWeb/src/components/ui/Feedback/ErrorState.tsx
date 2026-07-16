import type { ReactNode } from "react";
import { cx } from "../utils/cx";
import styles from "./Feedback.module.css";

export type ErrorStateProps = {
  title: ReactNode;
  description?: ReactNode;
  /** e.g. a Retry button; error states should offer a way forward. */
  action?: ReactNode;
  className?: string;
};

/**
 * Section/route-level error placeholder (failed query, unexpected throw).
 * role="alert" so it is announced. For the ownership-404 / not-found case the
 * app uses its dedicated NotFound route, not this.
 */
export function ErrorState({
  title,
  description,
  action,
  className,
}: ErrorStateProps) {
  return (
    <div className={cx(styles.state, className)} role="alert">
      <div
        className={cx(styles.stateIcon, styles.stateIconDanger)}
        aria-hidden="true"
      >
        <svg viewBox="0 0 24 24" fill="currentColor" width="28" height="28">
          <path d="M12 2a10 10 0 100 20 10 10 0 000-20zm0 5a1.25 1.25 0 011.25 1.25v5a1.25 1.25 0 01-2.5 0v-5A1.25 1.25 0 0112 7zm0 10.5a1.4 1.4 0 110-2.8 1.4 1.4 0 010 2.8z" />
        </svg>
      </div>
      <p className={styles.stateTitle}>{title}</p>
      {description ? <p className={styles.stateDesc}>{description}</p> : null}
      {action ? <div className={styles.stateAction}>{action}</div> : null}
    </div>
  );
}
