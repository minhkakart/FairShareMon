import type { ReactNode } from "react";
import { cx } from "../utils/cx";
import styles from "./Badge.module.css";

export type BadgeTone =
  | "neutral"
  | "success"
  | "warning"
  | "danger"
  | "info"
  | "settled" /* đã trả */
  | "premium" /* Premium tier / gated */
  | "free"; /* Free tier */

export type BadgeProps = {
  tone?: BadgeTone;
  children: ReactNode;
  /**
   * Optional leading glyph. Status meaning must not rest on color alone, so a
   * badge that encodes STATE (settled/closed/tier) should always pass an icon
   * or rely on its distinct text label.
   */
  icon?: ReactNode;
  className?: string;
};

export function Badge({ tone = "neutral", children, icon, className }: BadgeProps) {
  return (
    <span className={cx(styles.badge, styles[tone], className)}>
      {icon ? (
        <span className={styles.icon} aria-hidden="true">
          {icon}
        </span>
      ) : null}
      {children}
    </span>
  );
}
