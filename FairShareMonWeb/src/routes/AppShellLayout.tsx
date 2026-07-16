import { Link, NavLink, Outlet, useNavigate } from "react-router-dom";
import { useT } from "@/i18n/useT";
import {
  AppShell,
  Button,
  LanguageToggle,
  NavItem,
  ThemeToggle,
} from "@/components/ui";
import type { Locale, ThemePreference } from "@/components/ui";
import { useLocale } from "@/i18n/LocaleProvider";
import { useTheme } from "@/theme/ThemeProvider";
import { useCurrentUser, useLogout } from "@/features/auth/hooks/useAuth";
import { getSession } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { useToast } from "@/app/ToastHost";

const NAV_ITEMS = [
  { to: "/dashboard", key: "common:nav.dashboard" },
  { to: "/members", key: "common:nav.members" },
  { to: "/expenses", key: "common:nav.expenses" },
  { to: "/events", key: "common:nav.events" },
  { to: "/stats", key: "common:nav.stats" },
  { to: "/wallet", key: "common:nav.wallet" },
] as const;

export function AppShellLayout() {
  const { t } = useT();
  const navigate = useNavigate();
  const { locale, setLocale } = useLocale();
  const { theme, setTheme } = useTheme();
  const logout = useLogout();
  const user = useCurrentUser();
  const toast = useToast();

  const doLogout = async () => {
    try {
      await logout.mutateAsync();
    } catch {
      // Revoke is best-effort; clear locally regardless.
    }
    getSession().clearSession();
    queryClient.clear();
    toast.push({ tone: "success", title: t("auth:logout.success") });
    void navigate("/login", { replace: true });
  };

  const themeLabels: Record<ThemePreference, string> = {
    light: t("common:theme.light"),
    dark: t("common:theme.dark"),
    system: t("common:theme.system"),
  };
  const localeLabels: Record<Locale, string> = {
    "vi-VN": t("common:locale.vi"),
    "en-US": t("common:locale.en"),
  };

  return (
    <AppShell
      skipToContentLabel={t("common:skipToContent")}
      brand={
        <Link to="/dashboard" style={{ fontWeight: "var(--fs-weight-bold)" }}>
          {t("common:appName")}
        </Link>
      }
      nav={NAV_ITEMS.map((item) => (
        <NavLink key={item.to} to={item.to}>
          {({ isActive }) => <NavItem active={isActive}>{t(item.key)}</NavItem>}
        </NavLink>
      ))}
      actions={
        <>
          <LanguageToggle
            value={locale}
            onChange={setLocale}
            labels={localeLabels}
            groupLabel={t("common:locale.label")}
          />
          <ThemeToggle
            value={theme}
            onChange={setTheme}
            labels={themeLabels}
            groupLabel={t("common:theme.label")}
          />
          <Link to="/settings/change-password">
            <Button variant="ghost" size="sm">
              {user?.username ?? t("common:account")}
            </Button>
          </Link>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => void doLogout()}
            loading={logout.isPending}
          >
            {t("common:logout")}
          </Button>
        </>
      }
    >
      <Outlet />
    </AppShell>
  );
}
