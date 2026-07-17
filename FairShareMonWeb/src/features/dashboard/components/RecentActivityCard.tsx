import { Link } from "react-router-dom";
import {
  Button,
  Card,
  CategoryMarker,
  EmptyState,
  ErrorState,
  Money,
  Skeleton,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { formatDate } from "@/i18n/format";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { useExpensesQuery } from "@/features/expenses/hooks/useExpenses";
import type { ExpenseFilter } from "@/features/expenses/api/types";
import styles from "./dashboard.module.css";

const RECENT_N = 5;
const NO_FILTER: ExpenseFilter = {};

const PlusIcon = () => (
  <svg
    viewBox="0 0 20 20"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    aria-hidden="true"
    width="1em"
    height="1em"
  >
    <path d="M10 4v12M4 10h12" strokeLinecap="round" />
  </svg>
);
const EventIcon = () => (
  <svg
    viewBox="0 0 20 20"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.7"
    aria-hidden="true"
    width="1em"
    height="1em"
  >
    <rect x="3" y="4.5" width="14" height="12" rx="2" />
    <path d="M3 8h14M7 3v3M13 3v3" strokeLinecap="round" />
  </svg>
);

/**
 * Home "Recent expenses" card (OQ6a): the top-5 expenses (`GET /expenses`,
 * backend `expenseTime` DESC order, sliced client-side), each row linking into
 * its detail. Plus quick-action buttons (Add expense / New event). Empty ledger
 * → an empty state that still offers the quick actions.
 */
export function RecentActivityCard() {
  const { t } = useT();
  const expensesQuery = useExpensesQuery(NO_FILTER);
  const recent = expensesQuery.data?.slice(0, RECENT_N) ?? [];

  return (
    <Card>
      <div className={styles.cardHeadRow}>
        <span className={styles.cardTitle}>{t("common:home.recentActivity")}</span>
        <Link className={styles.viewAll} to="/expenses">
          {t("common:home.viewAll")}
        </Link>
      </div>

      {expensesQuery.isError ? (
        <ErrorState
          title={t("stats:states.loadError")}
          description={resolveErrorMessage(expensesQuery.error, t)}
          action={
            <Button
              variant="secondary"
              onClick={() => void expensesQuery.refetch()}
            >
              {t("stats:states.retry")}
            </Button>
          }
        />
      ) : expensesQuery.isPending ? (
        <div className={styles.recentList}>
          {Array.from({ length: RECENT_N }).map((_, i) => (
            <div key={i} className={styles.recentRow}>
              <div className={styles.recentMain}>
                <Skeleton width="12rem" />
                <Skeleton width="8rem" />
              </div>
              <Skeleton width="6rem" />
            </div>
          ))}
        </div>
      ) : recent.length === 0 ? (
        <EmptyState title={t("common:home.recentExpensesEmpty")} />
      ) : (
        <div className={styles.recentList}>
          {recent.map((expense) => (
            <Link
              key={expense.uuid}
              className={styles.recentRow}
              to={`/expenses/${expense.uuid}`}
            >
              <span className={styles.recentMain}>
                <span className={styles.recentName}>{expense.name}</span>
                <span className={styles.recentMeta}>
                  <CategoryMarker
                    color={expense.category.color}
                    icon={expense.category.icon}
                    name={expense.category.name}
                    showLabel
                    size="sm"
                  />
                  <span className={styles.recentDate}>
                    {formatDate(expense.expenseTime)}
                  </span>
                </span>
              </span>
              <Money amount={expense.total} className={styles.recentAmount} />
            </Link>
          ))}
        </div>
      )}

      <div className={styles.quickActions}>
        <Button asChild variant="primary" size="sm" iconStart={<PlusIcon />}>
          <Link to="/expenses/new">{t("common:home.addExpense")}</Link>
        </Button>
        <Button asChild variant="secondary" size="sm" iconStart={<EventIcon />}>
          <Link to="/events">{t("common:home.newEvent")}</Link>
        </Button>
      </div>
    </Card>
  );
}
