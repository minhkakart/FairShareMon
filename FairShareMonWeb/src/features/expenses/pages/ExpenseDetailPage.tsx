import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  CategoryMarker,
  DescriptionList,
  DescriptionRow,
  ErrorState,
  Money,
  Skeleton,
  Stack,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useToast } from "@/app/ToastHost";
import { classifyError, resolveErrorMessage } from "@/lib/api/http-error-handling";
import { formatDateTime, formatMoneyVnd } from "@/i18n/format";
import { NotFound } from "@/routes/NotFound";
import { useMembersQuery } from "@/features/members/hooks/useMembers";
import { useCategoriesQuery } from "@/features/categories/hooks/useCategories";
import { useTagsQuery } from "@/features/tags/hooks/useTags";
import { useExpenseQuery, useExportExpense } from "../hooks/useExpenses";
import type { ExpenseResponse } from "../api/types";
import { SettledToggle } from "../components/SettledToggle";
import { SharesSection } from "../components/SharesSection";
import { ExpenseAuditSection } from "../components/ExpenseAuditSection";
import { ExpenseEditDialog } from "../components/ExpenseEditDialog";
import { DeleteExpenseDialog } from "../components/DeleteExpenseDialog";
import { ExpenseEventControl } from "../components/ExpenseEventControl";
import { QrDialog } from "@/features/wallet/components/QrDialog";
import { QrIcon } from "@/features/wallet/components/icons";
import {
  CheckIcon,
  ClockIcon,
  DownloadIcon,
  LockIcon,
  PencilIcon,
  TrashIcon,
} from "../components/icons";
import styles from "./ExpenseDetailPage.module.css";

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

function DetailView({ expense }: { expense: ExpenseResponse }) {
  const { t } = useT();
  const toast = useToast();
  const exportExpense = useExportExpense();
  const [editOpen, setEditOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [qrOpen, setQrOpen] = useState(false);

  // Pickers for the edit dialog (cache-warmed; only needed on edit).
  const membersQuery = useMembersQuery(false);
  const categoriesQuery = useCategoriesQuery(false);
  const tagsQuery = useTagsQuery(false);

  const closed = expense.eventIsClosed === true;

  async function onExport() {
    try {
      await exportExpense.mutateAsync({
        uuid: expense.uuid,
        fallbackName: `${expense.name || "expense"}.csv`,
      });
    } catch (error) {
      toast.push({ tone: "danger", title: resolveErrorMessage(error, t) });
    }
  }

  return (
    <Stack gap="6">
      <div className={styles.detailHeader}>
        <div className={styles.detailTitleBlock}>
          <h1 className={styles.detailTitle}>{expense.name}</h1>
          <div className={styles.detailBadges}>
            <Badge
              tone={expense.isSettled ? "settled" : "warning"}
              icon={expense.isSettled ? <CheckIcon /> : <ClockIcon />}
            >
              {expense.isSettled
                ? t("expenses:settled.on")
                : t("expenses:settled.off")}
            </Badge>
            {expense.eventUuid ? (
              <Badge tone="neutral" icon={closed ? <LockIcon /> : undefined}>
                {closed
                  ? t("expenses:badge.closed")
                  : (expense.eventName ?? t("expenses:badge.event"))}
              </Badge>
            ) : (
              <Badge tone="neutral">{t("expenses:badge.loose")}</Badge>
            )}
          </div>
        </div>
        <div className={styles.detailActions}>
          <SettledToggle
            uuid={expense.uuid}
            isSettled={expense.isSettled}
            contextName={expense.name}
          />
          <Button
            variant="secondary"
            size="sm"
            iconStart={<PencilIcon />}
            disabled={closed}
            onClick={() => setEditOpen(true)}
          >
            {t("expenses:detail.edit")}
          </Button>
          <Button
            variant="secondary"
            size="sm"
            iconStart={<QrIcon />}
            onClick={() => setQrOpen(true)}
          >
            {t("wallet:qr.showExpense")}
          </Button>
          <Button
            variant="secondary"
            size="sm"
            iconStart={<DownloadIcon />}
            loading={exportExpense.isPending}
            onClick={() => void onExport()}
          >
            {t("expenses:detail.export")}
          </Button>
          <Button
            variant="danger"
            size="sm"
            iconStart={<TrashIcon />}
            disabled={closed}
            onClick={() => setDeleteOpen(true)}
          >
            {t("expenses:detail.delete")}
          </Button>
        </div>
      </div>

      {closed ? (
        <Alert tone="warning" title={t("expenses:detail.closedTitle")}>
          {t("expenses:detail.closedBody")}
        </Alert>
      ) : null}

      <div className={styles.detailGrid}>
        <Card>
          <CardHeader title={t("expenses:detail.infoTitle")} />
          <CardBody>
            <DescriptionList>
              <DescriptionRow term={t("expenses:detail.description")}>
                {expense.description ? (
                  expense.description
                ) : (
                  <span className={styles.muted}>—</span>
                )}
              </DescriptionRow>
              <DescriptionRow term={t("expenses:detail.time")}>
                {formatDateTime(expense.expenseTime)}
              </DescriptionRow>
              <DescriptionRow term={t("expenses:detail.payer")}>
                <span className={styles.inlineWrap}>
                  {expense.payer.name}
                  {expense.payer.isDeleted ? (
                    <span className={styles.deletedTag}>
                      {t("expenses:badge.deletedTag")}
                    </span>
                  ) : null}
                </span>
              </DescriptionRow>
              <DescriptionRow term={t("expenses:detail.category")}>
                <span className={styles.inlineWrap}>
                  <CategoryMarker
                    color={expense.category.color}
                    icon={expense.category.icon}
                    name={expense.category.name}
                    showLabel
                  />
                  {expense.category.isDeleted ? (
                    <span className={styles.deletedTag}>
                      {t("expenses:badge.deletedTag")}
                    </span>
                  ) : null}
                </span>
              </DescriptionRow>
              <DescriptionRow term={t("expenses:detail.tags")}>
                {expense.tags.length > 0 ? (
                  <span className={styles.chipRow}>
                    {expense.tags.map((tag) => (
                      <Badge key={tag.uuid} tone="neutral">
                        {tag.name}
                      </Badge>
                    ))}
                  </span>
                ) : (
                  <span className={styles.muted}>—</span>
                )}
              </DescriptionRow>
              <DescriptionRow term={t("expenses:detail.event")}>
                <ExpenseEventControl expense={expense} />
              </DescriptionRow>
              <DescriptionRow term={t("expenses:detail.total")}>
                <Money amount={expense.total} size="lg" format={formatMoneyVnd} />
              </DescriptionRow>
              <DescriptionRow term={t("expenses:detail.createdAt")}>
                {formatDateTime(expense.createdAt)}
              </DescriptionRow>
            </DescriptionList>
          </CardBody>
        </Card>

        <SharesSection expense={expense} disabled={closed} />
      </div>

      <ExpenseAuditSection uuid={expense.uuid} />

      <ExpenseEditDialog
        expense={expense}
        members={membersQuery.data ?? []}
        categories={categoriesQuery.data ?? []}
        tags={tagsQuery.data ?? []}
        open={editOpen}
        onOpenChange={setEditOpen}
      />
      <DeleteExpenseDialog
        uuid={expense.uuid}
        name={expense.name}
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
      />
      <QrDialog
        open={qrOpen}
        onOpenChange={setQrOpen}
        kind="expense"
        targetUuid={expense.uuid}
        title={t("wallet:qr.expenseTitle")}
      />
    </Stack>
  );
}

/**
 * /expenses/:uuid — the first detail route. Shows the full expense (info + shares
 * + audit) with the edit / delete / export / settled actions. An ownership miss
 * (code 6000) renders the shared NotFound view inline (R1 — never leak existence);
 * other errors show a retry; write controls are disabled when the owning event is
 * closed (R4), while the settled toggle stays enabled.
 */
export function ExpenseDetailPage() {
  const { t } = useT();
  const { uuid = "" } = useParams();
  const expenseQuery = useExpenseQuery(uuid);

  if (expenseQuery.isError) {
    if (classifyError(expenseQuery.error) === "notFound") {
      return <NotFound />;
    }
    return (
      <Stack gap="6">
        <Button asChild variant="ghost">
          <Link to="/expenses">{t("expenses:detail.back")}</Link>
        </Button>
        <ErrorState
          title={t("expenses:detail.errorTitle")}
          description={resolveErrorMessage(expenseQuery.error, t)}
          action={
            <Button
              variant="secondary"
              onClick={() => void expenseQuery.refetch()}
            >
              {t("expenses:list.retry")}
            </Button>
          }
        />
      </Stack>
    );
  }

  return (
    <Stack gap="6">
      <Button asChild variant="ghost">
        <Link to="/expenses">{t("expenses:detail.back")}</Link>
      </Button>
      {expenseQuery.isPending ? (
        <DetailSkeleton />
      ) : (
        <DetailView expense={expenseQuery.data} />
      )}
    </Stack>
  );
}
