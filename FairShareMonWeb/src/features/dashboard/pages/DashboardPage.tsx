import { useT } from "@/i18n/useT";
import { Card, CardBody } from "@/components/ui";
import { useCurrentUser } from "@/features/auth/hooks/useAuth";

export function DashboardPage() {
  const { t } = useT();
  const user = useCurrentUser();

  return (
    <div style={{ display: "grid", gap: "var(--fs-space-4)" }}>
      <h1>{t("common:nav.dashboard")}</h1>
      <Card>
        <CardBody>
          <p>
            {t("common:appName")} — {t("common:tagline")}
          </p>
          {user?.username ? (
            <p style={{ color: "var(--fs-color-text-muted)", margin: 0 }}>
              {user.username}
            </p>
          ) : null}
        </CardBody>
      </Card>
    </div>
  );
}
