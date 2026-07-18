import {
  useState,
  type HTMLAttributes,
  type MouseEvent,
  type ReactNode,
} from "react";
import * as RadixDialog from "@radix-ui/react-dialog";
import { cx } from "../utils/cx";
import styles from "./AppShell.module.css";

const MenuIcon = (
  <svg
    viewBox="0 0 20 20"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    aria-hidden="true"
  >
    <path d="M3 5h14M3 10h14M3 15h14" strokeLinecap="round" />
  </svg>
);
const CloseIcon = (
  <svg
    viewBox="0 0 20 20"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    aria-hidden="true"
  >
    <path d="M5 5l10 10M15 5L5 15" strokeLinecap="round" />
  </svg>
);

export type AppShellProps = {
  /** Brand / app name — usually a link to the dashboard. */
  brand: ReactNode;
  /**
   * Primary navigation (the implementer supplies router NavLinks). The SAME
   * nodes render inline in the header at wide viewports and inside the mobile
   * slide-in menu below the collapse breakpoint (64rem) — supply them once.
   * Each entry should be a link/button so keyboard activation closes the menu.
   */
  nav?: ReactNode;
  /**
   * Trailing header actions: language + theme toggle, user menu, logout. Shown
   * inline in the header ONLY at/above the nav breakpoint (lg / 64rem); below it
   * the header is brand + hamburger only (so it never overflows on a phone) and
   * these controls belong in `secondaryActions` instead.
   */
  actions?: ReactNode;
  /**
   * Secondary actions for the mobile nav drawer footer (language/theme toggles,
   * account link, logout). Rendered pinned to the bottom of the slide-in drawer,
   * which only exists below the nav breakpoint — so this is the mobile home for
   * the same controls `actions` shows inline on desktop. It inherits the drawer's
   * focus trap / Escape / focus-restore (Radix), so no extra a11y wiring is
   * needed. Omit it and the drawer has no footer.
   */
  secondaryActions?: ReactNode;
  /** Main routed content. */
  children: ReactNode;
  /** Text for the skip-to-content link (localized). */
  skipToContentLabel?: string;
  /** Accessible label for the mobile menu button + drawer title (localized). */
  mobileMenuLabel?: string;
  /** Accessible label for the mobile menu close button (localized). */
  mobileMenuCloseLabel?: string;
  /** Accessible label for the primary <nav> landmark (localized). */
  navLabel?: string;
};

/**
 * Authenticated app shell: a sticky landmarked header (brand · nav · actions)
 * over a width-constrained <main>. Semantic landmarks (header/nav/main) + a
 * skip link give keyboard/AT users a fast path past the chrome.
 *
 * Responsive nav (mobile-first): below 64rem the inline nav is hidden and a
 * labeled hamburger opens a Radix-Dialog-backed slide-in drawer holding the
 * SAME nav nodes; at/above 64rem the inline nav shows and the button is hidden.
 * Radix supplies the focus trap, Escape-to-close, focus restore, aria-modal,
 * and the trigger's aria-expanded/aria-controls. The drawer closes when a nav
 * entry is activated (click or keyboard). Presentational only — routing and
 * active states are the implementer's.
 */
export function AppShell({
  brand,
  nav,
  actions,
  secondaryActions,
  children,
  skipToContentLabel = "Bỏ qua tới nội dung",
  mobileMenuLabel = "Menu",
  mobileMenuCloseLabel = "Đóng",
  navLabel = "Chính",
}: AppShellProps) {
  const [menuOpen, setMenuOpen] = useState(false);

  // Close the drawer when a nav entry is activated. Anchors/buttons fire a
  // click on both pointer and keyboard (Enter/Space) activation, so this keeps
  // the close behavior color- and pointer-independent.
  const closeOnNavActivate = (event: MouseEvent<HTMLElement>) => {
    if ((event.target as HTMLElement).closest("a,button")) setMenuOpen(false);
  };

  // Drawer-footer close: fire ONLY when a link (e.g. the account link) is
  // activated — navigating away should dismiss the drawer. The language/theme
  // toggles are <button>s that mutate state in place and must NOT close the
  // drawer; logout navigates to /login, which unmounts the shell (and drawer).
  const closeOnFooterLinkActivate = (event: MouseEvent<HTMLElement>) => {
    if ((event.target as HTMLElement).closest("a")) setMenuOpen(false);
  };

  return (
    <div className={styles.shell}>
      <a href="#fs-main" className={styles.skipLink}>
        {skipToContentLabel}
      </a>
      <header className={styles.header}>
        <div className={styles.headerInner}>
          <div className={styles.brand}>{brand}</div>

          {nav ? (
            <RadixDialog.Root open={menuOpen} onOpenChange={setMenuOpen}>
              <RadixDialog.Trigger asChild>
                <button
                  type="button"
                  className={styles.menuButton}
                  aria-label={mobileMenuLabel}
                >
                  <span className={styles.menuButtonIcon} aria-hidden="true">
                    {MenuIcon}
                  </span>
                </button>
              </RadixDialog.Trigger>
              <RadixDialog.Portal>
                <RadixDialog.Overlay className={styles.menuOverlay} />
                <RadixDialog.Content className={styles.menuPanel}>
                  <div className={styles.menuHeader}>
                    <RadixDialog.Title className={styles.menuTitle}>
                      {mobileMenuLabel}
                    </RadixDialog.Title>
                    <RadixDialog.Close
                      className={styles.menuClose}
                      aria-label={mobileMenuCloseLabel}
                    >
                      <span
                        className={styles.menuButtonIcon}
                        aria-hidden="true"
                      >
                        {CloseIcon}
                      </span>
                    </RadixDialog.Close>
                  </div>
                  {/* eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions -- delegated close on link activation, not a control */}
                  <nav
                    className={styles.menuNav}
                    aria-label={navLabel}
                    onClick={closeOnNavActivate}
                  >
                    {nav}
                  </nav>
                  {secondaryActions ? (
                    // eslint-disable-next-line jsx-a11y/no-static-element-interactions -- delegated close on link activation only (toggles are buttons and stay open)
                    <div
                      className={styles.menuFooter}
                      onClick={closeOnFooterLinkActivate}
                    >
                      {secondaryActions}
                    </div>
                  ) : null}
                </RadixDialog.Content>
              </RadixDialog.Portal>
            </RadixDialog.Root>
          ) : null}

          {nav ? (
            <nav className={styles.nav} aria-label={navLabel}>
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
