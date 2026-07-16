import type { CSSProperties } from "react";
import { cx } from "../utils/cx";
import styles from "./Spinner.module.css";

export type SpinnerProps = {
  /** Diameter in px. Defaults to 20. */
  size?: number;
  /** Stroke color; defaults to currentColor so it inherits the surrounding text. */
  className?: string;
  /** Accessible label; omit inside a button that already conveys busy state. */
  label?: string;
};

/**
 * Indeterminate loading spinner. Respects prefers-reduced-motion (the animation
 * is defined in CSS and the global reduce rule neutralizes it).
 */
export function Spinner({ size = 20, className, label }: SpinnerProps) {
  const style = { "--fs-spinner-size": `${size}px` } as CSSProperties;
  return (
    <span
      className={cx(styles.spinner, className)}
      style={style}
      role={label ? "status" : undefined}
      aria-hidden={label ? undefined : true}
    >
      {label ? <span className="fs-visually-hidden">{label}</span> : null}
    </span>
  );
}
