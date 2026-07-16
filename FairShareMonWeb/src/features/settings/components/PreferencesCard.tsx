import { useT } from "@/i18n/useT";
import {
  Card,
  CardBody,
  CardHeader,
  DescriptionList,
  DescriptionRow,
  LanguageToggle,
  ThemeToggle,
} from "@/components/ui";
import type { Locale, ThemePreference } from "@/components/ui";
import { useLocale } from "@/i18n/LocaleProvider";
import { useTheme } from "@/theme/ThemeProvider";

/**
 * Theme + language controls surfaced on /settings (OQ2a). Reuses the same
 * controlled `ThemeToggle`/`LanguageToggle` primitives (wired to the shared
 * `useTheme`/`useLocale` context) as the header — one source of truth, no
 * duplicated state.
 */
export function PreferencesCard() {
  const { t } = useT();
  const { locale, setLocale } = useLocale();
  const { theme, setTheme } = useTheme();

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
    <Card>
      <CardHeader title={t("settings:preferences.title")} />
      <CardBody>
        <DescriptionList>
          <DescriptionRow term={t("settings:preferences.theme")}>
            <ThemeToggle
              value={theme}
              onChange={setTheme}
              labels={themeLabels}
              groupLabel={t("settings:preferences.theme")}
            />
          </DescriptionRow>
          <DescriptionRow term={t("settings:preferences.language")}>
            <LanguageToggle
              value={locale}
              onChange={setLocale}
              labels={localeLabels}
              groupLabel={t("settings:preferences.language")}
            />
          </DescriptionRow>
        </DescriptionList>
      </CardBody>
    </Card>
  );
}
