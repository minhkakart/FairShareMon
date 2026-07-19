import { Card, CardBody, EmptyState } from "@/components/ui";
import { useT } from "@/i18n/useT";
import styles from "../admin.module.css";

export interface ReferencesListProps {
  references: string[];
}

/**
 * The payment references list (revenue), newest-first (API order, rendered
 * verbatim). References are admin/tier-grant data — in scope, not ledger data.
 */
export function ReferencesList({ references }: ReferencesListProps) {
  const { t } = useT();
  return (
    <Card>
      <CardBody>
        <h3 className={styles.panelTitle}>{t("admin:revenue.references.title")}</h3>
        {references.length === 0 ? (
          <EmptyState title={t("admin:revenue.references.empty")} />
        ) : (
          <ul className={styles.refList}>
            {references.map((ref, i) => (
              <li key={`${ref}-${i}`} className={styles.refItem}>
                <span className={styles.refText}>{ref}</span>
              </li>
            ))}
          </ul>
        )}
      </CardBody>
    </Card>
  );
}
