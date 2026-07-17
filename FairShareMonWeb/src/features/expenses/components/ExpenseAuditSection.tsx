import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Skeleton,
  Stack,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { useExpenseHistoryQuery } from "../hooks/useExpenses";
import { AuditTimeline } from "./AuditTimeline";
import styles from "./ExpenseAuditSection.module.css";

export type ExpenseAuditSectionProps = {
  uuid: string;
};

/**
 * The per-expense change-history card (B5): loads the immutable audit log and
 * renders it as an ordered timeline. Loading → skeleton; error → inline retry;
 * empty → a calm "no changes yet" note.
 */
export function ExpenseAuditSection({ uuid }: ExpenseAuditSectionProps) {
  const { t } = useT();
  const historyQuery = useExpenseHistoryQuery(uuid);
  const entries = historyQuery.data ?? [];

  return (
    <Card>
      <CardHeader title={t("expenses:audit.title")} />
      <CardBody>
        {historyQuery.isError ? (
          <div className={styles.inlineError} role="alert">
            <span>{resolveErrorMessage(historyQuery.error, t)}</span>
            <Button
              variant="secondary"
              size="sm"
              onClick={() => void historyQuery.refetch()}
            >
              {t("expenses:list.retry")}
            </Button>
          </div>
        ) : historyQuery.isPending ? (
          <Stack gap="3">
            <Skeleton width="80%" height="1.5rem" />
            <Skeleton width="60%" height="1.5rem" />
            <Skeleton width="70%" height="1.5rem" />
          </Stack>
        ) : entries.length === 0 ? (
          <p className={styles.empty}>{t("expenses:audit.empty")}</p>
        ) : (
          <AuditTimeline entries={entries} />
        )}
      </CardBody>
    </Card>
  );
}
