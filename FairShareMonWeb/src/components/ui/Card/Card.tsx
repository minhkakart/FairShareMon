import type { HTMLAttributes, ReactNode } from "react";
import { cx } from "../utils/cx";
import styles from "./Card.module.css";

export type CardProps = HTMLAttributes<HTMLDivElement> & {
  /** flat = border only; raised = subtle shadow (default). */
  elevation?: "flat" | "raised";
  /** Removes inner padding when the card wraps its own padded regions/tables. */
  padded?: boolean;
};

export function Card({
  elevation = "raised",
  padded = true,
  className,
  ...rest
}: CardProps) {
  return (
    <div
      className={cx(
        styles.card,
        styles[elevation],
        padded && styles.padded,
        className,
      )}
      {...rest}
    />
  );
}

export function CardHeader({
  title,
  action,
  className,
}: {
  title: ReactNode;
  action?: ReactNode;
  className?: string;
}) {
  return (
    <div className={cx(styles.header, className)}>
      <h3 className={styles.title}>{title}</h3>
      {action ? <div className={styles.headerAction}>{action}</div> : null}
    </div>
  );
}

export function CardBody({
  className,
  ...rest
}: HTMLAttributes<HTMLDivElement>) {
  return <div className={cx(styles.body, className)} {...rest} />;
}
