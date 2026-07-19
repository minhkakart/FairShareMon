import { useT } from "@/i18n/useT";
import { Button, ErrorState } from "@/components/ui";
import {
  invalidateCurrentUser,
  useProfileStatus,
} from "@/features/auth/hooks/useAuth";

/**
 * Shown when an authenticated user hits an area they are not allowed into.
 *
 * A non-401 `/auth/me` failure fail-safe-denies even a real ADMIN here (the
 * profile settles to `error` with no `role`). Offer a retry that re-triggers the
 * profile fetch so a transient network/5xx blip is recoverable without a full
 * page reload. This is only a recovery affordance — it never widens who is
 * admitted; the guard still reads the freshly-synced role.
 */
export function Forbidden() {
  const { t } = useT();
  const degraded = useProfileStatus() === "error";

  return (
    <ErrorState
      title={t("common:forbidden.title")}
      description={t("common:forbidden.body")}
      action={
        degraded ? (
          <Button
            variant="secondary"
            onClick={() => void invalidateCurrentUser()}
          >
            {t("common:retry")}
          </Button>
        ) : undefined
      }
    />
  );
}
