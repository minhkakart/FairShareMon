import type { CSSProperties } from "react";
import { cx } from "../utils/cx";
import styles from "./Feedback.module.css";

export type SkeletonProps = {
  /** CSS width, e.g. "100%", "8rem". */
  width?: string;
  /** CSS height; defaults to a text line. */
  height?: string;
  /** Pill radius for avatar/thumbnail placeholders. */
  circle?: boolean;
  className?: string;
};

/** Content placeholder shown while data loads. Shimmer respects reduced motion. */
export function Skeleton({ width = "100%", height = "1em", circle, className }: SkeletonProps) {
  const style = { width, height } as CSSProperties;
  return (
    <span
      className={cx(styles.skeleton, circle && styles.skeletonCircle, className)}
      style={style}
      aria-hidden="true"
    />
  );
}
