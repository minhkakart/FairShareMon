import { NavLink, Outlet } from "react-router-dom";
import { useT } from "@/i18n/useT";
import { ShieldIcon } from "../components/icons";
import styles from "../components/admin.module.css";

const TABS = [
  { to: "/admin/dashboard", key: "admin:nav.dashboard" as const },
  { to: "/admin/revenue", key: "admin:nav.revenue" as const },
  { to: "/admin/users", key: "admin:nav.users" as const },
];

/**
 * The tabbed admin console shell (OQ7a). A high-privilege framed region — an
 * "Quản trị" eyebrow + title over a tab sub-nav (Bảng chỉ số · Doanh thu · Người
 * dùng) — visually distinct from the member AppShell so an operator always knows
 * they are in the privileged area. Each tab is a `NavLink` carrying
 * `aria-current="page"` when active; the Users tab stays active on the detail
 * route. The active sub-route renders in the body via `<Outlet/>`.
 */
export function AdminLayout() {
  const { t } = useT();

  return (
    <div className={styles.console}>
      <div className={styles.consoleHeader}>
        <span className={styles.eyebrow}>
          <span className={styles.eyebrowIcon}>
            <ShieldIcon />
          </span>
          {t("admin:console.eyebrow")}
        </span>
        <h1 className={styles.consoleTitle}>{t("admin:console.title")}</h1>
        <p className={styles.consoleDesc}>{t("admin:console.description")}</p>
        <nav className={styles.tabs} aria-label={t("admin:console.navLabel")}>
          {TABS.map((tab) => (
            <NavLink key={tab.to} to={tab.to}>
              {({ isActive }) => (
                <span
                  className={`${styles.tab}${isActive ? ` ${styles.tabActive}` : ""}`}
                  aria-current={isActive ? "page" : undefined}
                >
                  {t(tab.key)}
                </span>
              )}
            </NavLink>
          ))}
        </nav>
      </div>
      <div className={styles.consoleBody}>
        <Outlet />
      </div>
    </div>
  );
}
