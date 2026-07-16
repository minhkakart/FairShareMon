import { Link } from "react-router-dom";
import { useT } from "@/i18n/useT";
import { Button, Card, CardBody, PageHeader, Stack } from "@/components/ui";
import { useCurrentUser } from "@/features/auth/hooks/useAuth";
import { useNavEntries } from "@/routes/navConfig";

/**
 * Minimal home (M1): a welcome greeting + quick-link cards into each visible
 * area (role-filtered via `useNavEntries`, so the admin tile shows only for
 * admins). No charts and no data fetching beyond the session `user` — the
 * data-rich dashboard is deferred to M6.
 */
export function DashboardPage() {
  const { t } = useT();
  const user = useCurrentUser();
  const navEntries = useNavEntries();

  // Skip the home entry itself — you're already here.
  const quickLinks = navEntries.filter((entry) => entry.to !== "/dashboard");

  return (
    <Stack gap="6">
      <PageHeader
        title={
          user?.username
            ? t("common:home.welcome", { name: user.username })
            : t("common:home.welcomeGeneric")
        }
        description={t("common:home.subtitle")}
      />
      <div
        style={{
          display: "grid",
          gap: "var(--fs-space-4)",
          gridTemplateColumns:
            "repeat(auto-fill, minmax(min(100%, 16rem), 1fr))",
        }}
      >
        {quickLinks.map((entry) => (
          <Card key={entry.to}>
            <CardBody>
              <Stack gap="3">
                <div>
                  <h2
                    style={{
                      margin: 0,
                      fontSize: "var(--fs-text-lg)",
                      fontWeight: "var(--fs-weight-semibold)",
                    }}
                  >
                    {t(entry.labelKey)}
                  </h2>
                  {entry.descriptionKey ? (
                    <p
                      style={{
                        margin: "var(--fs-space-1) 0 0",
                        color: "var(--fs-color-text-muted)",
                      }}
                    >
                      {t(entry.descriptionKey)}
                    </p>
                  ) : null}
                </div>
                <div>
                  <Link to={entry.to} aria-label={t(entry.labelKey)}>
                    <Button variant="ghost" size="sm">
                      {t("common:home.open")}
                    </Button>
                  </Link>
                </div>
              </Stack>
            </CardBody>
          </Card>
        ))}
      </div>
    </Stack>
  );
}
