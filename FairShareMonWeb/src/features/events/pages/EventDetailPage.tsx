import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  DescriptionList,
  DescriptionRow,
  ErrorState,
  Skeleton,
  Stack,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useToast } from "@/app/ToastHost";
import { classifyError, resolveErrorMessage } from "@/lib/api/http-error-handling";
import { formatDate, formatDateTime } from "@/i18n/format";
import { NotFound } from "@/routes/NotFound";
import { useEventQuery, useExportEvent } from "../hooks/useEvents";
import type { EventResponse } from "../api/types";
import { formatRange } from "../dateRange";
import { EventStatusBadge } from "../components/EventStatusBadge";
import { EventBalanceTable } from "../components/EventBalanceTable";
import { EventExpensesSection } from "../components/EventExpensesSection";
import { EventFormDialog } from "../components/EventFormDialog";
import { DeleteEventDialog } from "../components/DeleteEventDialog";
import { CloseEventDialog } from "../components/CloseEventDialog";
import { QrDialog } from "@/features/wallet/components/QrDialog";
import { QrIcon } from "@/features/wallet/components/icons";
import { AddExpenseDialog } from "../components/AddExpenseDialog";
import {
  DownloadIcon,
  LockIcon,
  PencilIcon,
  PlusIcon,
  TrashIcon,
} from "../components/icons";
import styles from "./EventDetailPage.module.css";

function DetailSkeleton() {
  return (
    <Card>
      <CardBody>
        <Stack gap="4">
          <Skeleton width="40%" height="2rem" />
          <Skeleton width="100%" height="1.5rem" />
          <Skeleton width="80%" height="1.5rem" />
          <Skeleton width="60%" height="1.5rem" />
        </Stack>
      </CardBody>
    </Card>
  );
}

function DetailView({ event }: { event: EventResponse }) {
  const { t } = useT();
  const toast = useToast();
  const exportEvent = useExportEvent();
  const [editOpen, setEditOpen] = useState(false);
  const [addExpenseOpen, setAddExpenseOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [closeOpen, setCloseOpen] = useState(false);
  const [qrOpen, setQrOpen] = useState(false);

  const closed = event.isClosed;

  async function onExport() {
    try {
      await exportEvent.mutateAsync({
        uuid: event.uuid,
        fallbackName: `${event.name || "event"}.csv`,
      });
    } catch (error) {
      toast.push({ tone: "danger", title: resolveErrorMessage(error, t) });
    }
  }

  return (
    <Stack gap="6">
      <div className={styles.detailHeader}>
        <div className={styles.detailTitleBlock}>
          <h1 className={styles.detailTitle}>{event.name}</h1>
          <div className={styles.detailBadges}>
            <EventStatusBadge isClosed={closed} />
            <span className={styles.range}>
              {formatRange(event.startDate, event.endDate)}
            </span>
          </div>
        </div>
        <div className={styles.detailActions}>
          {!closed ? (
            <>
              <Button
                variant="primary"
                size="sm"
                iconStart={<PlusIcon />}
                onClick={() => setAddExpenseOpen(true)}
              >
                {t("events:detail.addExpense")}
              </Button>
              <Button
                variant="secondary"
                size="sm"
                iconStart={<PencilIcon />}
                onClick={() => setEditOpen(true)}
              >
                {t("events:detail.edit")}
              </Button>
            </>
          ) : null}
          <Button
            variant="secondary"
            size="sm"
            iconStart={<DownloadIcon />}
            loading={exportEvent.isPending}
            onClick={() => void onExport()}
          >
            {t("events:detail.export")}
          </Button>
          {closed ? (
            <Button
              variant="secondary"
              size="sm"
              iconStart={<QrIcon />}
              onClick={() => setQrOpen(true)}
            >
              {t("wallet:qr.showEvent")}
            </Button>
          ) : null}
          {!closed ? (
            <>
              <Button
                variant="secondary"
                size="sm"
                iconStart={<LockIcon />}
                onClick={() => setCloseOpen(true)}
              >
                {t("events:detail.close")}
              </Button>
              <Button
                variant="danger"
                size="sm"
                iconStart={<TrashIcon />}
                onClick={() => setDeleteOpen(true)}
              >
                {t("events:detail.delete")}
              </Button>
            </>
          ) : null}
        </div>
      </div>

      {closed ? (
        <Alert tone="warning" title={t("events:detail.closedTitle")}>
          {t("events:detail.closedBody")}
        </Alert>
      ) : null}

      <Card>
        <CardHeader title={t("events:detail.infoTitle")} />
        <CardBody>
          <DescriptionList>
            <DescriptionRow term={t("events:detail.description")}>
              {event.description ? (
                event.description
              ) : (
                <span className={styles.muted}>—</span>
              )}
            </DescriptionRow>
            <DescriptionRow term={t("events:detail.range")}>
              {formatRange(event.startDate, event.endDate)}
            </DescriptionRow>
            <DescriptionRow term={t("events:detail.status")}>
              <EventStatusBadge isClosed={closed} />
            </DescriptionRow>
            {closed && event.closedAt ? (
              <DescriptionRow term={t("events:detail.closedAt")}>
                {formatDateTime(event.closedAt)}
              </DescriptionRow>
            ) : null}
            <DescriptionRow term={t("events:detail.expenseCount")}>
              {event.expenseCount}
            </DescriptionRow>
            <DescriptionRow term={t("events:detail.createdAt")}>
              {formatDate(event.createdAt)}
            </DescriptionRow>
          </DescriptionList>
        </CardBody>
      </Card>

      <EventBalanceTable uuid={event.uuid} />

      <EventExpensesSection event={event} />

      {!closed ? (
        <AddExpenseDialog
          event={event}
          open={addExpenseOpen}
          onOpenChange={setAddExpenseOpen}
        />
      ) : null}
      <EventFormDialog event={event} open={editOpen} onOpenChange={setEditOpen} />
      <DeleteEventDialog
        uuid={event.uuid}
        name={event.name}
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
      />
      <CloseEventDialog
        uuid={event.uuid}
        name={event.name}
        open={closeOpen}
        onOpenChange={setCloseOpen}
      />
      {closed ? (
        <QrDialog
          open={qrOpen}
          onOpenChange={setQrOpen}
          kind="event"
          targetUuid={event.uuid}
          title={t("wallet:qr.eventTitle")}
        />
      ) : null}
    </Stack>
  );
}

/**
 * /events/:uuid — the event detail. Shows info + status + one-way close +
 * export + the debt-balance table + the event's expenses (assign/remove). An
 * ownership miss (code 9000) renders the shared NotFound view inline (R1 — never
 * leak existence); other errors show a retry; write controls disable when the
 * event is closed (only export + viewing remain).
 */
export function EventDetailPage() {
  const { t } = useT();
  const { uuid = "" } = useParams();
  const eventQuery = useEventQuery(uuid);

  if (eventQuery.isError) {
    if (classifyError(eventQuery.error) === "notFound") {
      return <NotFound />;
    }
    return (
      <Stack gap="6">
        <Button asChild variant="ghost">
          <Link to="/events">{t("events:detail.back")}</Link>
        </Button>
        <ErrorState
          title={t("events:detail.errorTitle")}
          description={resolveErrorMessage(eventQuery.error, t)}
          action={
            <Button variant="secondary" onClick={() => void eventQuery.refetch()}>
              {t("events:list.retry")}
            </Button>
          }
        />
      </Stack>
    );
  }

  return (
    <Stack gap="6">
      <Button asChild variant="ghost">
        <Link to="/events">{t("events:detail.back")}</Link>
      </Button>
      {eventQuery.isPending ? (
        <DetailSkeleton />
      ) : (
        <DetailView event={eventQuery.data} />
      )}
    </Stack>
  );
}
