import { useT } from "@/i18n/useT";
import { EmptyState } from "@/components/ui";

/** The stubbed feature-area title keys (nav namespace). */
export type StubTitleKey =
  | "common:nav.members"
  | "common:nav.categories"
  | "common:nav.tags"
  | "common:nav.expenses"
  | "common:nav.events"
  | "common:nav.stats"
  | "common:nav.wallet"
  | "common:nav.admin";

/**
 * Placeholder for feature areas stubbed this cycle (OQ13a). Each real screen
 * replaces its stub in a later feature cycle.
 */
export function StubPage({ titleKey }: { titleKey: StubTitleKey }) {
  const { t } = useT();
  return (
    <div style={{ display: "grid", gap: "var(--fs-space-4)" }}>
      <h1>{t(titleKey)}</h1>
      <EmptyState
        title={t("common:placeholder.comingSoon")}
        description={t("common:placeholder.body")}
      />
    </div>
  );
}
