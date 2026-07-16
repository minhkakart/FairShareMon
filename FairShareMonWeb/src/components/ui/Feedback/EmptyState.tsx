import type { ReactNode } from "react";
import { cx } from "../utils/cx";
import styles from "./Feedback.module.css";

export type EmptyStateProps = {
  /** Decorative illustration or icon. */
  icon?: ReactNode;
  title: ReactNode;
  description?: ReactNode;
  /** Primary call to action (e.g. "Tạo phiếu đầu tiên"). */
  action?: ReactNode;
  className?: string;
};

/** Shown when a list/section has no data yet — invites the first action. */
export function EmptyState({
  icon,
  title,
  description,
  action,
  className,
}: EmptyStateProps) {
  return (
    <div className={cx(styles.state, className)}>
      {icon ? (
        <div className={styles.stateIcon} aria-hidden="true">
          {icon}
        </div>
      ) : null}
      <p className={styles.stateTitle}>{title}</p>
      {description ? <p className={styles.stateDesc}>{description}</p> : null}
      {action ? <div className={styles.stateAction}>{action}</div> : null}
    </div>
  );
}
