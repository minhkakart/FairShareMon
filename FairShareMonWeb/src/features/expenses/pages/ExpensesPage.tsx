import { Link, useSearchParams } from "react-router-dom";
import {
  Button,
  Card,
  CardBody,
  EmptyState,
  ErrorState,
  PageHeader,
  Skeleton,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import type { ExpenseFilter } from "../api/types";
import { useExpensesQuery } from "../hooks/useExpenses";
import { dateBoundToIso } from "../dateTime";
import { ExpenseFilterBar } from "../components/ExpenseFilterBar";
import type { SettledFilter, UiFilters } from "../components/ExpenseFilterBar";
import { ExpensesTable } from "../components/ExpensesTable";
import styles from "./ExpensesPage.module.css";

const SKELETON_ROWS = 5;
const COLUMN_COUNT = 8;

function LoadingRows() {
  return (
    <>
      {Array.from({ length: SKELETON_ROWS }).map((_, index) => (
        <TableRow key={index}>
          <TableHeaderCell scope="row">
            <Skeleton width="12rem" />
          </TableHeaderCell>
          <TableCell>
            <Skeleton width="7rem" />
          </TableCell>
          <TableCell>
            <Skeleton width="8rem" />
          </TableCell>
          <TableCell numeric>
            <Skeleton width="6rem" />
          </TableCell>
          <TableCell>
            <Skeleton width="8rem" />
          </TableCell>
          <TableCell>
            <Skeleton width="6rem" />
          </TableCell>
          <TableCell>
            <Skeleton width="5rem" />
          </TableCell>
          <TableCell actions>
            <Skeleton width="3rem" />
          </TableCell>
        </TableRow>
      ))}
    </>
  );
}

function parseSettled(value: string | null): SettledFilter {
  return value === "yes" || value === "no" ? value : "all";
}

/**
 * /expenses — the caller's ledger in backend order (`expenseTime` DESC). Full
 * filter set (date range / category / tag / settled tri-state / loose-only) with
 * state reflected in the URL, plus a client-side name search over the loaded
 * rows. Loading (skeleton), error (retry), and two empty states (no expenses vs
 * no matches). Rows expose the live settled toggle and link to the detail route.
 */
export function ExpensesPage() {
  const { t } = useT();
  const [searchParams, setSearchParams] = useSearchParams();

  const filters: UiFilters = {
    from: searchParams.get("from") ?? "",
    to: searchParams.get("to") ?? "",
    categoryUuid: searchParams.get("category") ?? "",
    tagUuid: searchParams.get("tag") ?? "",
    settled: parseSettled(searchParams.get("settled")),
    looseOnly: searchParams.get("loose") === "1",
    eventUuid: searchParams.get("event") ?? "",
    q: searchParams.get("q") ?? "",
  };

  const hasActiveFilters =
    filters.from !== "" ||
    filters.to !== "" ||
    filters.categoryUuid !== "" ||
    filters.tagUuid !== "" ||
    filters.settled !== "all" ||
    filters.looseOnly ||
    filters.eventUuid !== "" ||
    filters.q !== "";

  function applyPatch(patch: Partial<UiFilters>) {
    const next = { ...filters, ...patch };
    const params = new URLSearchParams();
    if (next.from) params.set("from", next.from);
    if (next.to) params.set("to", next.to);
    if (next.categoryUuid) params.set("category", next.categoryUuid);
    if (next.tagUuid) params.set("tag", next.tagUuid);
    if (next.settled !== "all") params.set("settled", next.settled);
    if (next.looseOnly) params.set("loose", "1");
    if (next.eventUuid) params.set("event", next.eventUuid);
    if (next.q) params.set("q", next.q);
    setSearchParams(params, { replace: true });
  }

  function clearFilters() {
    setSearchParams(new URLSearchParams(), { replace: true });
  }

  const apiFilter: ExpenseFilter = {
    from: dateBoundToIso(filters.from, false),
    to: dateBoundToIso(filters.to, true),
    categoryUuid: filters.categoryUuid || undefined,
    tagUuid: filters.tagUuid || undefined,
    settled:
      filters.settled === "yes"
        ? true
        : filters.settled === "no"
          ? false
          : undefined,
    looseOnly: filters.looseOnly || undefined,
    eventUuid: filters.eventUuid || undefined,
  };

  const expensesQuery = useExpensesQuery(apiFilter);
  const allExpenses = expensesQuery.data ?? [];
  const search = filters.q.trim().toLocaleLowerCase();
  const expenses = search
    ? allExpenses.filter((e) => e.name.toLocaleLowerCase().includes(search))
    : allExpenses;

  const noMatches = allExpenses.length > 0 || hasActiveFilters;

  return (
    <Stack gap="6">
      <PageHeader
        title={t("expenses:title")}
        description={t("expenses:subtitle")}
        actions={
          <Button asChild variant="primary">
            <Link to="/expenses/new">{t("expenses:add")}</Link>
          </Button>
        }
      />

      <Card>
        <CardBody>
          <ExpenseFilterBar
            filters={filters}
            onChange={applyPatch}
            onClear={clearFilters}
            hasActiveFilters={hasActiveFilters}
          />
        </CardBody>
      </Card>

      {expensesQuery.isError ? (
        <ErrorState
          title={t("expenses:list.errorTitle")}
          description={resolveErrorMessage(expensesQuery.error, t)}
          action={
            <Button
              variant="secondary"
              onClick={() => void expensesQuery.refetch()}
            >
              {t("expenses:list.retry")}
            </Button>
          }
        />
      ) : expensesQuery.isPending ? (
        <Table caption={t("expenses:list.caption")} captionHidden>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t("expenses:list.name")}</TableHeaderCell>
              <TableHeaderCell>{t("expenses:list.payer")}</TableHeaderCell>
              <TableHeaderCell>{t("expenses:list.category")}</TableHeaderCell>
              <TableHeaderCell numeric>
                {t("expenses:list.total")}
              </TableHeaderCell>
              <TableHeaderCell>{t("expenses:list.time")}</TableHeaderCell>
              <TableHeaderCell>{t("expenses:list.settled")}</TableHeaderCell>
              <TableHeaderCell>{t("expenses:list.event")}</TableHeaderCell>
              <TableHeaderCell align="right">
                {t("expenses:list.actions")}
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            <LoadingRows />
          </TableBody>
        </Table>
      ) : expenses.length === 0 ? (
        <Table caption={t("expenses:list.caption")} captionHidden>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t("expenses:list.name")}</TableHeaderCell>
              <TableHeaderCell>{t("expenses:list.payer")}</TableHeaderCell>
              <TableHeaderCell>{t("expenses:list.category")}</TableHeaderCell>
              <TableHeaderCell numeric>
                {t("expenses:list.total")}
              </TableHeaderCell>
              <TableHeaderCell>{t("expenses:list.time")}</TableHeaderCell>
              <TableHeaderCell>{t("expenses:list.settled")}</TableHeaderCell>
              <TableHeaderCell>{t("expenses:list.event")}</TableHeaderCell>
              <TableHeaderCell align="right">
                {t("expenses:list.actions")}
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            <TableEmpty colSpan={COLUMN_COUNT}>
              {noMatches ? (
                <EmptyState
                  title={t("expenses:list.noMatchesTitle")}
                  description={t("expenses:list.noMatchesBody")}
                  action={
                    <Button variant="secondary" onClick={clearFilters}>
                      {t("expenses:filter.clear")}
                    </Button>
                  }
                />
              ) : (
                <EmptyState
                  title={t("expenses:list.emptyTitle")}
                  description={t("expenses:list.emptyBody")}
                  action={
                    <Button asChild variant="primary">
                      <Link to="/expenses/new">{t("expenses:add")}</Link>
                    </Button>
                  }
                />
              )}
            </TableEmpty>
          </TableBody>
        </Table>
      ) : (
        <div className={styles.tableWrap}>
          <ExpensesTable expenses={expenses} />
        </div>
      )}
    </Stack>
  );
}
