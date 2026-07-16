import { useT } from "@/i18n/useT";
import { Button, PageHeader, Stack } from "@/components/ui";
import { useLogoutAction } from "@/features/auth/hooks/useLogoutAction";
import { ProfileCard } from "../components/ProfileCard";
import { PreferencesCard } from "../components/PreferencesCard";
import { SecurityCard } from "../components/SecurityCard";
import { TierStatusPanel } from "../components/TierStatusPanel";

/**
 * Account / settings home (/settings): profile, tier status, preferences,
 * security (change-password link), and logout. Reads only the already-loaded
 * session `user` — no new API call. Change-password keeps its own route (OQ3a);
 * theme/language stay in the header too (OQ2a).
 */
export function SettingsPage() {
  const { t } = useT();
  const { doLogout, isPending: loggingOut } = useLogoutAction();

  return (
    <Stack gap="6">
      <PageHeader
        title={t("settings:title")}
        description={t("settings:subtitle")}
        actions={
          <Button
            variant="secondary"
            size="sm"
            onClick={() => void doLogout()}
            loading={loggingOut}
          >
            {t("settings:logout")}
          </Button>
        }
      />
      <Stack gap="5">
        <ProfileCard />
        <TierStatusPanel />
        <PreferencesCard />
        <SecurityCard />
      </Stack>
    </Stack>
  );
}
