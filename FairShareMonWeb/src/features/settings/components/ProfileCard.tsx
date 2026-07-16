import { useT } from "@/i18n/useT";
import {
  Alert,
  Badge,
  Card,
  CardBody,
  CardHeader,
  DescriptionList,
  DescriptionRow,
  Skeleton,
  Stack,
  TierBadge,
} from "@/components/ui";
import { formatDate } from "@/i18n/format";
import {
  useCurrentUser,
  useProfileStatus,
} from "@/features/auth/hooks/useAuth";

/**
 * Read-only account profile: username, tier badge, role, and member-since.
 * Reads only the already-loaded session `user` — no request. While the profile
 * is still resolving each value shows a Skeleton; a degraded (`error`) profile
 * shows a neutral notice with whatever values are known.
 */
export function ProfileCard() {
  const { t } = useT();
  const user = useCurrentUser();
  const profileStatus = useProfileStatus();

  const pending = profileStatus === "pending";
  const isAdmin = user?.role === "ADMIN";

  return (
    <Card>
      <CardHeader title={t("settings:profile.title")} />
      <CardBody>
        <Stack gap="4">
          {profileStatus === "error" ? (
            <Alert tone="warning">{t("settings:profile.unavailable")}</Alert>
          ) : null}
          <DescriptionList>
            <DescriptionRow term={t("settings:profile.username")}>
              {pending && !user?.username ? (
                <Skeleton width="8rem" />
              ) : (
                (user?.username ?? "—")
              )}
            </DescriptionRow>
            <DescriptionRow term={t("settings:profile.tier")}>
              {pending && !user?.tier ? (
                <Skeleton width="6rem" />
              ) : (
                <TierBadge
                  tier={user?.tier}
                  freeLabel={t("settings:tier.free")}
                  premiumLabel={t("settings:tier.premium")}
                />
              )}
            </DescriptionRow>
            <DescriptionRow term={t("settings:profile.role")}>
              {pending && !user?.role ? (
                <Skeleton width="6rem" />
              ) : (
                <Badge tone={isAdmin ? "info" : "neutral"}>
                  {isAdmin
                    ? t("settings:profile.roleAdmin")
                    : t("settings:profile.roleUser")}
                </Badge>
              )}
            </DescriptionRow>
            <DescriptionRow term={t("settings:profile.memberSince")}>
              {user?.createdAt ? (
                formatDate(user.createdAt)
              ) : pending ? (
                <Skeleton width="7rem" />
              ) : (
                "—"
              )}
            </DescriptionRow>
          </DescriptionList>
        </Stack>
      </CardBody>
    </Card>
  );
}
