import type { HTMLAttributes, ReactNode } from "react";
import { cx } from "../utils/cx";
import styles from "./AppShell.module.css";

export type AppShellProps = {
  /** Brand / app name — usually a link to the dashboard. */
  brand: ReactNode;
  /** Primary navigation (the implementer supplies router NavLinks). */
  nav?: ReactNode;
  /** Trailing header actions: language + theme toggle, user menu, logout. */
  actions?: ReactNode;
  /** Main routed content. */
  children: ReactNode;
  /** Text for the skip-to-content link (localized). */
  skipToContentLabel?: string;
};

/**
 * Authenticated app shell: a sticky landmarked header (brand · nav · actions)
 * over a width-constrained <main>. Semantic landmarks (header/nav/main) + a
 * skip link give keyboard/AT users a fast path past the chrome. Presentational
 * only — routing, active states, and the user menu are the implementer's.
 */
export function AppShell({
  brand,
  nav,
  actions,
  children,
  skipToContentLabel = "Bỏ qua tới nội dung",
}: AppShellProps) {
  return (
    <div className={styles.shell}>
      <a href="#fs-main" className={styles.skipLink}>
        {skipToContentLabel}
      </a>
      <header className={styles.header}>
        <div className={styles.headerInner}>
          <div className={styles.brand}>{brand}</div>
          {nav ? (
            <nav className={styles.nav} aria-label="Chính">
              {nav}
            </nav>
          ) : null}
          {actions ? <div className={styles.actions}>{actions}</div> : null}
        </div>
      </header>
      <main id="fs-main" className={styles.main} tabIndex={-1}>
        <div className={styles.content}>{children}</div>
      </main>
    </div>
  );
}

/** A horizontal nav item — presentational; wrap the router link via `as`/children. */
export function NavItem({
  children,
  active,
  className,
  ...rest
}: {
  children: ReactNode;
  active?: boolean;
  className?: string;
} & HTMLAttributes<HTMLElement>) {
  return (
    <span
      className={cx(styles.navItem, active && styles.navItemActive, className)}
      aria-current={active ? "page" : undefined}
      {...rest}
    >
      {children}
    </span>
  );
}

/**
 * Centered single-column layout for the public auth screens (login/register/
 * change-password). Keeps forms to a comfortable measure on any viewport.
 */
export function AuthLayout({
  children,
  header,
  footer,
}: {
  children: ReactNode;
  /** Brand lockup / title above the card. */
  header?: ReactNode;
  /** Below-card links (e.g. "Chưa có tài khoản?"). */
  footer?: ReactNode;
}) {
  return (
    <div className={styles.authLayout}>
      <div className={styles.authInner}>
        {header ? <div className={styles.authHeader}>{header}</div> : null}
        {children}
        {footer ? <div className={styles.authFooter}>{footer}</div> : null}
      </div>
    </div>
  );
}
