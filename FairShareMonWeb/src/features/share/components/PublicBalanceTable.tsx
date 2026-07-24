import { Fragment, useEffect, useState } from "react";
import {
  Badge,
  Button,
  EmptyState,
  Money,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useToast } from "@/app/ToastHost";
import { formatMoneyVnd } from "@/i18n/format";
import { QrIcon } from "@/features/wallet/components/icons";
import { QrPreviewDialog } from "@/features/wallet/components/QrPreviewDialog";
import { CheckIcon, ClockIcon } from "@/features/events/components/icons";
import type { MemberBalanceRow, PublicEventShareResponse } from "../api/types";
import { usePublicShareMemberQrsQuery } from "../hooks/useShare";
import { MemberExpenseBreakdown } from "./MemberExpenseBreakdown";
import styles from "./PublicBalanceTable.module.css";

export type PublicBalanceTableProps = {
  token: string;
  data: PublicEventShareResponse;
};

/**
 * The color-independent polarity word beside a signed balance figure — mirrors
 * `EventBalanceTable`'s cue, backing the `Money variant="balance"` sign glyph.
 */
function BalanceAmount({ amount }: { amount: number }) {
  const { t } = useT();
  const label =
    amount > 0
      ? t("share:public.positiveLabel")
      : amount < 0
        ? t("share:public.negativeLabel")
        : t("share:public.zeroLabel");
  return (
    <span className={styles.balanceCell}>
      <Money amount={amount} variant="balance" format={formatMoneyVnd} />
      <span className={styles.polarity}>{label}</span>
    </span>
  );
}

/** Read-only đã-trả/còn-nợ badge for an owing member (no toggle, no write). */
function StatusCell({ row }: { row: MemberBalanceRow }) {
  const { t } = useT();
  if (row.balance >= 0) {
    return <span className={styles.muted}>—</span>;
  }
  return (
    <Badge
      tone={row.isSettled ? "settled" : "warning"}
      icon={row.isSettled ? <CheckIcon /> : <ClockIcon />}
    >
      {row.isSettled
        ? t("share:public.statusSettled")
        : t("share:public.statusOwing")}
    </Badge>
  );
}

/**
 * The public read-only balance table (adapted from `EventBalanceTable`, every
 * write control stripped). One row per member: advanced / owed / balance /
 * outstanding + a read-only status badge. A leading toggle expands the member's
 * per-expense breakdown (a sibling row); a trailing QR button (only when `hasQr`
 * and the row is still owing) lazily fetches the per-member QR images and opens
 * the shared `QrPreviewDialog` carousel at that member's slide.
 */
export function PublicBalanceTable({ token, data }: PublicBalanceTableProps) {
  const { t } = useT();
  const toast = useToast();
  const rows = data.rows;
  const showQrColumn = data.hasQr;
  const columnCount = 7 + (showQrColumn ? 1 : 0);

  const [expanded, setExpanded] = useState<Set<string>>(new Set());

  // Lazy QR: the query stays disabled until the first QR-button click.
  const [qrEnabled, setQrEnabled] = useState(false);
  const [targetMemberUuid, setTargetMemberUuid] = useState<string | null>(null);
  const [pendingOpen, setPendingOpen] = useState(false);
  const [previewOpen, setPreviewOpen] = useState(false);
  const qrQuery = usePublicShareMemberQrsQuery(token, { enabled: qrEnabled });
  const members = qrQuery.data ?? [];

  // When the user is waiting to open the preview, react to the lazy query
  // resolving: success (with members) → open at the clicked member; error →
  // a friendly toast, preview stays closed.
  useEffect(() => {
    if (!pendingOpen) return;
    if (qrQuery.isSuccess) {
      if (members.length > 0) setPreviewOpen(true);
      // Reachable only if hasQr is true yet the endpoint returns no debtors: surface a brief info
      // toast rather than a silent no-op (the button just stopped spinning).
      else
        toast.push({
          tone: "info",
          title: t("share:qr.emptyTitle"),
          description: t("share:qr.emptyBody"),
        });
      setPendingOpen(false);
    } else if (qrQuery.isError) {
      toast.push({
        tone: "danger",
        title: t("share:error.qrTitle"),
        description: t("share:error.qrBody"),
      });
      setPendingOpen(false);
    }
  }, [pendingOpen, qrQuery.isSuccess, qrQuery.isError, members.length, toast, t]);

  function toggleRow(memberUuid: string) {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(memberUuid)) next.delete(memberUuid);
      else next.add(memberUuid);
      return next;
    });
  }

  function onQrClick(memberUuid: string) {
    setTargetMemberUuid(memberUuid);
    setPendingOpen(true);
    if (!qrEnabled) {
      setQrEnabled(true);
    } else if (qrQuery.isError) {
      // A prior fetch failed; retry rather than re-toast a stale error.
      void qrQuery.refetch();
    }
  }

  const startIndex = Math.max(
    0,
    members.findIndex((m) => m.memberUuid === targetMemberUuid),
  );

  return (
    <>
      <Table caption={t("share:public.caption")} captionHidden stackOnMobile>
        <TableHead>
          <TableRow>
            <TableHeaderCell>
              <span className={styles.srOnly}>{t("share:public.member")}</span>
            </TableHeaderCell>
            <TableHeaderCell>{t("share:public.member")}</TableHeaderCell>
            <TableHeaderCell numeric>{t("share:public.advanced")}</TableHeaderCell>
            <TableHeaderCell numeric>{t("share:public.owed")}</TableHeaderCell>
            <TableHeaderCell numeric>{t("share:public.balance")}</TableHeaderCell>
            <TableHeaderCell numeric>
              {t("share:public.outstanding")}
            </TableHeaderCell>
            <TableHeaderCell>{t("share:public.statusColumn")}</TableHeaderCell>
            {showQrColumn ? (
              <TableHeaderCell>{t("share:public.qrColumn")}</TableHeaderCell>
            ) : null}
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.length === 0 ? (
            <TableEmpty colSpan={columnCount}>
              <EmptyState
                title={t("share:public.emptyTitle")}
                description={t("share:public.emptyBody")}
              />
            </TableEmpty>
          ) : (
            rows.map((row) => {
              const isOpen = expanded.has(row.memberUuid);
              const regionId = `share-breakdown-${row.memberUuid}`;
              const showQrButton = showQrColumn && row.outstanding > 0;
              const isThisLoading =
                pendingOpen &&
                qrQuery.isFetching &&
                targetMemberUuid === row.memberUuid;
              return (
                <Fragment key={row.memberUuid}>
                  <TableRow deleted={row.isDeleted}>
                    <TableCell data-label="">
                      <button
                        type="button"
                        className={styles.expandButton}
                        aria-expanded={isOpen}
                        aria-controls={regionId}
                        aria-label={t(
                          isOpen
                            ? "share:public.collapse"
                            : "share:public.expand",
                          { name: row.memberName },
                        )}
                        onClick={() => toggleRow(row.memberUuid)}
                      >
                        <span
                          className={`${styles.chevron} ${isOpen ? styles.chevronOpen : ""}`}
                          aria-hidden="true"
                        >
                          <ChevronIcon />
                        </span>
                      </button>
                    </TableCell>
                    <TableHeaderCell
                      scope="row"
                      data-label={t("share:public.member")}
                    >
                      <span className={styles.memberCell}>
                        <span className={styles.memberName}>
                          {row.memberName}
                        </span>
                        {row.isOwnerRepresentative ? (
                          <span className={styles.repTag}>
                            {t("share:public.ownerRep")}
                          </span>
                        ) : null}
                        {row.isDeleted ? (
                          <span className={styles.deletedTag}>
                            {t("share:public.deletedTag")}
                          </span>
                        ) : null}
                      </span>
                    </TableHeaderCell>
                    <TableCell numeric data-label={t("share:public.advanced")}>
                      <Money amount={row.advanced} format={formatMoneyVnd} />
                    </TableCell>
                    <TableCell numeric data-label={t("share:public.owed")}>
                      <Money amount={row.owed} format={formatMoneyVnd} />
                    </TableCell>
                    <TableCell numeric data-label={t("share:public.balance")}>
                      <BalanceAmount amount={row.balance} />
                    </TableCell>
                    <TableCell
                      numeric
                      data-label={t("share:public.outstanding")}
                    >
                      {row.outstanding > 0 ? (
                        <Money amount={row.outstanding} format={formatMoneyVnd} />
                      ) : (
                        <span className={styles.muted}>—</span>
                      )}
                    </TableCell>
                    <TableCell data-label={t("share:public.statusColumn")}>
                      <StatusCell row={row} />
                    </TableCell>
                    {showQrColumn ? (
                      <TableCell data-label={t("share:public.qrColumn")}>
                        {showQrButton ? (
                          <Button
                            type="button"
                            variant="secondary"
                            size="sm"
                            iconStart={<QrIcon />}
                            loading={isThisLoading}
                            aria-label={t("share:public.showQr", {
                              name: row.memberName,
                            })}
                            onClick={() => onQrClick(row.memberUuid)}
                          >
                            {t("share:public.qrColumn")}
                          </Button>
                        ) : (
                          <span className={styles.muted}>—</span>
                        )}
                      </TableCell>
                    ) : null}
                  </TableRow>
                  {isOpen ? (
                    <TableRow>
                      <TableCell colSpan={columnCount}>
                        <div id={regionId} className={styles.breakdownRegion}>
                          <MemberExpenseBreakdown
                            memberUuid={row.memberUuid}
                            memberName={row.memberName}
                            expenses={data.expenses}
                          />
                        </div>
                      </TableCell>
                    </TableRow>
                  ) : null}
                </Fragment>
              );
            })
          )}
        </TableBody>
      </Table>

      <QrPreviewDialog
        open={previewOpen}
        onOpenChange={setPreviewOpen}
        members={members}
        kind="event"
        startIndex={startIndex}
      />
    </>
  );
}

const ChevronIcon = () => (
  <svg
    viewBox="0 0 20 20"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.9"
    aria-hidden="true"
    width="1em"
    height="1em"
  >
    <path d="M7 5l6 5-6 5" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
