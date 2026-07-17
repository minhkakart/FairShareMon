import { useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import {
  Button,
  Card,
  CardBody,
  EmptyState,
  ErrorState,
  PageHeader,
  Select,
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
import type { SelectOption } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import type { EventFilter } from "../api/types";
import { useEventsQuery } from "../hooks/useEvents";
import { EventsTable } from "../components/EventsTable";
import { EventFormDialog } from "../components/EventFormDialog";
import { PlusIcon } from "../components/icons";
import styles from "./EventsPage.module.css";

type StatusFilter = "all" | "open" | "closed";

const COLUMN_COUNT = 6;

function parseStatus(value: string | null): StatusFilter {
  return value === "open" || value === "closed" ? value : "all";
}

function toApiFilter(status: StatusFilter): EventFilter {
  if (status === "open") return { closed: false };
  if (status === "closed") return { closed: true };
  return {};
}

function HeaderRow() {
  const { t } = useT();
  return (
    <TableRow>
      <TableHeaderCell>{t("events:list.name")}</TableHeaderCell>
      <TableHeaderCell>{t("events:list.range")}</TableHeaderCell>
      <TableHeaderCell>{t("events:list.status")}</TableHeaderCell>
      <TableHeaderCell numeric>{t("events:list.expenseCount")}</TableHeaderCell>
      <TableHeaderCell>{t("events:list.createdAt")}</TableHeaderCell>
      <TableHeaderCell align="right">{t("events:list.actions")}</TableHeaderCell>
    </TableRow>
  );
}

function LoadingRows() {
  return (
    <>
      {Array.from({ length: 5 }).map((_, index) => (
        <TableRow key={index}>
          <TableHeaderCell scope="row">
            <Skeleton width="12rem" />
          </TableHeaderCell>
          <TableCell>
            <Skeleton width="10rem" />
          </TableCell>
          <TableCell>
            <Skeleton width="6rem" />
          </TableCell>
          <TableCell numeric>
            <Skeleton width="3rem" />
          </TableCell>
          <TableCell>
            <Skeleton width="7rem" />
          </TableCell>
          <TableCell actions>
            <Skeleton width="3rem" />
          </TableCell>
        </TableRow>
      ))}
    </>
  );
}

/**
 * /events — the caller's events in backend order (`startDate` DESC then
 * `createdAt` DESC). An all/open/closed filter reflected in the URL (`?status=`)
 * and a "Thêm đợt" create button opening the shared `EventFormDialog`. Loading
 * (skeleton), error (retry), empty (no events) and no-matches (filter active)
 * states.
 */
export function EventsPage() {
  const { t } = useT();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [createOpen, setCreateOpen] = useState(false);

  const status = parseStatus(searchParams.get("status"));
  const hasActiveFilters = status !== "all";

  const statusOptions: SelectOption[] = [
    { value: "all", label: t("events:filter.statusAll") },
    { value: "open", label: t("events:filter.statusOpen") },
    { value: "closed", label: t("events:filter.statusClosed") },
  ];

  function setStatus(next: StatusFilter) {
    const params = new URLSearchParams();
    if (next !== "all") params.set("status", next);
    setSearchParams(params, { replace: true });
  }

  function clearFilters() {
    setSearchParams(new URLSearchParams(), { replace: true });
  }

  const eventsQuery = useEventsQuery(toApiFilter(status));
  const events = eventsQuery.data ?? [];

  return (
    <Stack gap="6">
      <PageHeader
        title={t("events:title")}
        description={t("events:subtitle")}
        actions={
          <Button
            variant="primary"
            iconStart={<PlusIcon />}
            onClick={() => setCreateOpen(true)}
          >
            {t("events:add")}
          </Button>
        }
      />

      <Card>
        <CardBody>
          <div className={styles.filterBar}>
            <Select
              className={styles.filterField}
              label={t("events:filter.status")}
              value={status}
              onValueChange={(v) => setStatus(v as StatusFilter)}
              options={statusOptions}
            />
            <div className={styles.filterClear}>
              <Button
                variant="ghost"
                size="sm"
                onClick={clearFilters}
                disabled={!hasActiveFilters}
              >
                {t("events:filter.clear")}
              </Button>
            </div>
          </div>
        </CardBody>
      </Card>

      {eventsQuery.isError ? (
        <ErrorState
          title={t("events:list.errorTitle")}
          description={resolveErrorMessage(eventsQuery.error, t)}
          action={
            <Button
              variant="secondary"
              onClick={() => void eventsQuery.refetch()}
            >
              {t("events:list.retry")}
            </Button>
          }
        />
      ) : eventsQuery.isPending ? (
        <Table caption={t("events:list.caption")} captionHidden>
          <TableHead>
            <HeaderRow />
          </TableHead>
          <TableBody>
            <LoadingRows />
          </TableBody>
        </Table>
      ) : events.length === 0 ? (
        <Table caption={t("events:list.caption")} captionHidden>
          <TableHead>
            <HeaderRow />
          </TableHead>
          <TableBody>
            <TableEmpty colSpan={COLUMN_COUNT}>
              {hasActiveFilters ? (
                <EmptyState
                  title={t("events:list.noMatchesTitle")}
                  description={t("events:list.noMatchesBody")}
                  action={
                    <Button variant="secondary" onClick={clearFilters}>
                      {t("events:filter.clear")}
                    </Button>
                  }
                />
              ) : (
                <EmptyState
                  title={t("events:list.emptyTitle")}
                  description={t("events:list.emptyBody")}
                  action={
                    <Button
                      variant="primary"
                      iconStart={<PlusIcon />}
                      onClick={() => setCreateOpen(true)}
                    >
                      {t("events:add")}
                    </Button>
                  }
                />
              )}
            </TableEmpty>
          </TableBody>
        </Table>
      ) : (
        <div className={styles.tableWrap}>
          <EventsTable events={events} />
        </div>
      )}

      <EventFormDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        onCreated={(uuid) => void navigate(`/events/${uuid}`)}
      />
    </Stack>
  );
}
