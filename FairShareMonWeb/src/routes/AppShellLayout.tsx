import { Link, NavLink, Outlet } from "react-router-dom";
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
import { useCurrentUser } from "@/features/auth/hooks/useAuth";
import { useLogoutAction } from "@/features/auth/hooks/useLogoutAction";
import { useNavEntries } from "./navConfig";

export function AppShellLayout() {
  const { t } = useT();
  const { locale, setLocale } = useLocale();
  const { theme, setTheme } = useTheme();
  const { doLogout, isPending: loggingOut } = useLogoutAction();
  const user = useCurrentUser();
  const navEntries = useNavEntries();

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
      navLabel={t("common:nav.primary")}
      mobileMenuLabel={t("common:nav.menu")}
      mobileMenuCloseLabel={t("common:nav.closeMenu")}
      brand={
        <Link to="/dashboard" style={{ fontWeight: "var(--fs-weight-bold)" }}>
          {t("common:appName")}
        </Link>
      }
      nav={navEntries.map((entry) => (
        <NavLink key={entry.to} to={entry.to} end={entry.end}>
          {({ isActive }) => (
            <NavItem active={isActive}>{t(entry.labelKey)}</NavItem>
          )}
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
          <Button asChild variant="ghost" size="sm">
            <Link to="/settings">
              {user?.username ?? t("common:account")}
            </Link>
          </Button>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => void doLogout()}
            loading={loggingOut}
          >
            {t("common:logout")}
          </Button>
        </>
      }
      secondaryActions={
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
          <Button asChild variant="ghost" size="sm" fullWidth>
            <Link to="/settings">
              {user?.username ?? t("common:account")}
            </Link>
          </Button>
          <Button
            variant="secondary"
            size="sm"
            fullWidth
            onClick={() => void doLogout()}
            loading={loggingOut}
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
