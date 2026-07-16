import type { HTMLAttributes, ReactNode } from "react";
import { cx } from "../utils/cx";
import styles from "./Layout.module.css";

/** Vertical-rhythm spacing steps (subset of the --fs-space scale). */
export type StackGap = "2" | "3" | "4" | "5" | "6" | "8";

export type StackProps = HTMLAttributes<HTMLDivElement> & {
  /** Gap between children, mapped to --fs-space-* (default "5"). */
  gap?: StackGap;
};

/**
 * A single-axis vertical layout: children stacked with a consistent, token-based
 * gap. Use it for page-level section stacks (e.g. the settings page's column of
 * cards) so vertical rhythm never relies on collapsing per-element margins.
 */
export function Stack({ gap = "5", className, ...rest }: StackProps) {
  return (
    <div
      className={cx(styles.stack, styles[`gap${gap}`], className)}
      {...rest}
    />
  );
}

export type PageHeaderProps = {
  /** Page title — rendered as the page's <h1>. */
  title: ReactNode;
  /** Optional supporting line under the title. */
  description?: ReactNode;
  /** Optional trailing actions (buttons) aligned to the end. */
  actions?: ReactNode;
  className?: string;
};

/**
 * The heading block at the top of a routed page: an <h1> title, an optional
 * description, and optional end-aligned actions. Gives every feature page one
 * consistent title hierarchy and top spacing. Tolerant of long Vietnamese
 * titles — the title/description column wraps and the actions drop below on
 * narrow viewports.
 */
export function PageHeader({
  title,
  description,
  actions,
  className,
}: PageHeaderProps) {
  return (
    <div className={cx(styles.pageHeader, className)}>
      <div className={styles.pageHeaderText}>
        <h1 className={styles.pageTitle}>{title}</h1>
        {description ? (
          <p className={styles.pageDescription}>{description}</p>
        ) : null}
      </div>
      {actions ? (
        <div className={styles.pageHeaderActions}>{actions}</div>
      ) : null}
    </div>
  );
}

/**
 * A semantic term/value list (<dl>) for read-only detail rows — profile fields,
 * event details, etc. Each row is a <DescriptionRow>. On wide viewports the term
 * sits in a fixed-width column beside the value; on narrow viewports it stacks.
 */
export function DescriptionList({
  className,
  ...rest
}: HTMLAttributes<HTMLDListElement>) {
  return <dl className={cx(styles.descList, className)} {...rest} />;
}

export type DescriptionRowProps = {
  /** The field label (<dt>). */
  term: ReactNode;
  /** The field value (<dd>) — may be a Skeleton, Badge, or plain text. */
  children: ReactNode;
  className?: string;
};

export function DescriptionRow({
  term,
  children,
  className,
}: DescriptionRowProps) {
  return (
    <div className={cx(styles.descRow, className)}>
      <dt className={styles.descTerm}>{term}</dt>
      <dd className={styles.descValue}>{children}</dd>
    </div>
  );
}
