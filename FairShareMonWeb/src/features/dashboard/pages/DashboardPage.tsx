import { Link } from "react-router-dom";
import { useT } from "@/i18n/useT";
import { Card, CardBody, PageHeader, Stack } from "@/components/ui";
import { useCurrentUser } from "@/features/auth/hooks/useAuth";
import { useNavEntries } from "@/routes/navConfig";
import { DashboardOverview } from "../components/DashboardOverview";
import { DashboardCategoryBreakdown } from "../components/DashboardCategoryBreakdown";
import { RecentActivityCard } from "../components/RecentActivityCard";
import styles from "../components/dashboard.module.css";

/**
 * Rich home (M6): a welcome greeting, a this-month KPI row, a two-column region
 * (compact category breakdown + recent expenses & quick actions), and the
 * existing role-filtered quick-link cards (via `useNavEntries`, so the admin tile
 * shows only for admins). All new data comes through the shared stats + expense
 * hooks — no new API surface.
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

      <DashboardOverview />

      <div className={styles.homeGrid}>
        <DashboardCategoryBreakdown />
        <RecentActivityCard />
      </div>

      <section>
        <h2 className={styles.quickAccessTitle}>{t("common:home.quickAccess")}</h2>
        <div className={styles.quickLinks}>
          {quickLinks.map((entry) => (
            <Card key={entry.to}>
              <CardBody>
                <Link className={styles.quickLink} to={entry.to}>
                  <span className={styles.quickLinkTitle}>
                    {t(entry.labelKey)}
                  </span>
                  {entry.descriptionKey ? (
                    <span className={styles.quickLinkDesc}>
                      {t(entry.descriptionKey)}
                    </span>
                  ) : null}
                </Link>
              </CardBody>
            </Card>
          ))}
        </div>
      </section>
    </Stack>
  );
}
