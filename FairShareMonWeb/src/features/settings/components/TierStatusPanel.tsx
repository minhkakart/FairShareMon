import { useT } from "@/i18n/useT";
import {
  Card,
  CardBody,
  CardHeader,
  Stack,
  UpgradePrompt,
} from "@/components/ui";
import { useCurrentUser } from "@/features/auth/hooks/useAuth";

/**
 * Informational tier status (M1 slice of the dissolved Tiers milestone).
 *
 * There is NO self-serve upgrade endpoint — Premium is a manual admin grant — so
 * the Free state uses `UpgradePrompt variant="info"` with NO navigating action:
 * it explains how Premium is obtained rather than inviting a click that goes
 * nowhere. Premium shows the `variant="active"` confirmation. Reads only the
 * session `tier` (case-insensitive; absent/unknown → Free, non-privileged).
 */
export function TierStatusPanel() {
  const { t } = useT();
  const user = useCurrentUser();
  const isPremium = user?.tier?.toUpperCase() === "PREMIUM";

  return (
    <Card>
      <CardHeader title={t("settings:tier.title")} />
      <CardBody>
        {isPremium ? (
          <UpgradePrompt
            variant="active"
            title={t("settings:tier.currentIsPremium")}
            description={t("settings:tier.currentIsPremiumInfo")}
          />
        ) : (
          <Stack gap="3">
            <UpgradePrompt
              variant="info"
              title={t("settings:tier.upgradeTitle")}
              description={t("settings:tier.upgradeInfo")}
            />
            <p style={{ margin: 0, color: "var(--fs-color-text-muted)" }}>
              {t("settings:tier.premiumPerks")}
            </p>
          </Stack>
        )}
      </CardBody>
    </Card>
  );
}
