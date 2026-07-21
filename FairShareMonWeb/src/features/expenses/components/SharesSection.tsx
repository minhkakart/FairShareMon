import { useState } from "react";
import {
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  Money,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { formatMoneyVnd } from "@/i18n/format";
import { useMembersQuery } from "@/features/members/hooks/useMembers";
import type { ExpenseResponse, ShareResponse } from "../api/types";
import { ShareFormDialog } from "./ShareFormDialog";
import { DeleteShareDialog } from "./DeleteShareDialog";
import { ShareSettledToggle } from "./ShareSettledToggle";
import { CheckIcon, ClockIcon, PlusIcon, StarIcon } from "./icons";
import styles from "./SharesSection.module.css";

export type SharesSectionProps = {
  expense: ExpenseResponse;
  /** True when the owning event is closed — write controls are disabled (R4). */
  disabled: boolean;
};

/**
 * The derived rollup chip (R2/OQ2a) — display-only, computed over billable shares
 * (payer-own + 0đ shares excluded, R3): all settled → "Đã trả toàn bộ";
 * some → "Đã trả một phần (X/Y phần)"; none → "Chưa trả". Hidden when there is no
 * billable share to roll up. State is carried by icon + text, never color alone.
 */
function SharesRollup({ settled, total }: { settled: number; total: number }) {
  const { t } = useT();
  if (total === 0) return null;
  if (settled === total) {
    return (
      <Badge tone="settled" icon={<CheckIcon />}>
        {t("expenses:shares.rollupAll")}
      </Badge>
    );
  }
  if (settled > 0) {
    return (
      <Badge tone="warning" icon={<ClockIcon />}>
        {t("expenses:shares.rollupPartial", { settled, total })}
      </Badge>
    );
  }
  return (
    <Badge tone="warning" icon={<ClockIcon />}>
      {t("expenses:shares.rollupNone")}
    </Badge>
  );
}

/**
 * The shares breakdown (B4): a table of member / amount / note with the derived
 * total row, plus add / edit / delete controls. The owner-representative row has
 * no delete control (mirrors `7002`) and a "khóa" note; a soft-deleted member
 * shows "(đã xóa)". All write controls are hidden/disabled when the event is
 * closed. Add/edit pickers exclude members that already have a share (mirrors
 * `7003`).
 */
export function SharesSection({ expense, disabled }: SharesSectionProps) {
  const { t } = useT();
  const membersQuery = useMembersQuery(false);
  const members = membersQuery.data ?? [];

  const [addOpen, setAddOpen] = useState(false);
  const [editOpen, setEditOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<ShareResponse | undefined>();
  const [deleteTarget, setDeleteTarget] = useState<ShareResponse | null>(null);

  function openEdit(share: ShareResponse) {
    setEditTarget(share);
    setEditOpen(true);
  }

  // A share is billable (owes) when it is NOT the payer's own share and the
  // amount is > 0 (R3/OQ3a — payer-own + 0đ shares are settled-by-definition and
  // excluded from the rollup). Payer detection is client-side from the DTOs.
  const payerUuid = expense.payer.uuid;
  const isBillable = (share: ShareResponse) =>
    share.member.uuid !== payerUuid && share.amount > 0;

  const billableShares = expense.shares.filter(isBillable);
  const billableCount = billableShares.length;
  const settledCount = billableShares.filter((s) => s.isSettled).length;

  return (
    <Card>
      <CardHeader
        title={t("expenses:shares.sectionTitle")}
        action={
          <div className={styles.headerActions}>
            <SharesRollup settled={settledCount} total={billableCount} />
            {disabled ? null : (
              <Button
                variant="secondary"
                size="sm"
                iconStart={<PlusIcon />}
                disabled={membersQuery.isPending}
                onClick={() => setAddOpen(true)}
              >
                {t("expenses:shares.add")}
              </Button>
            )}
          </div>
        }
      />
      <CardBody>
        <Table caption={t("expenses:shares.caption")} captionHidden>
          <TableHead>
            <TableRow>
              <TableHeaderCell scope="col">
                {t("expenses:shares.memberLabel")}
              </TableHeaderCell>
              <TableHeaderCell scope="col" numeric>
                {t("expenses:shares.amountLabel")}
              </TableHeaderCell>
              <TableHeaderCell scope="col">
                {t("expenses:shares.settledLabel")}
              </TableHeaderCell>
              <TableHeaderCell scope="col">
                {t("expenses:shares.noteLabel")}
              </TableHeaderCell>
              <TableHeaderCell scope="col" align="right">
                <span className={styles.srOnly}>
                  {t("expenses:list.actions")}
                </span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {expense.shares.map((share) => {
              const ownerRep = share.member.isOwnerRepresentative;
              return (
                <TableRow key={share.uuid} deleted={share.member.isDeleted}>
                  <TableHeaderCell scope="row">
                    <span className={styles.memberCell}>
                      {share.member.name}
                      {ownerRep ? (
                        <Badge tone="info" icon={<StarIcon />}>
                          {t("expenses:badge.ownerRep")}
                        </Badge>
                      ) : null}
                      {share.member.isDeleted ? (
                        <span className={styles.deletedTag}>
                          {t("expenses:badge.deletedTag")}
                        </span>
                      ) : null}
                    </span>
                  </TableHeaderCell>
                  <TableCell numeric>
                    <Money amount={share.amount} format={formatMoneyVnd} />
                  </TableCell>
                  <TableCell>
                    {isBillable(share) ? (
                      <ShareSettledToggle
                        expenseUuid={expense.uuid}
                        share={share}
                      />
                    ) : (
                      <span className={styles.muted}>
                        {t("expenses:shares.notOwed")}
                      </span>
                    )}
                  </TableCell>
                  <TableCell>
                    {share.note ? (
                      share.note
                    ) : (
                      <span className={styles.muted}>—</span>
                    )}
                  </TableCell>
                  <TableCell actions>
                    {disabled ? (
                      <span className={styles.muted}>
                        {t("expenses:shares.readOnly")}
                      </span>
                    ) : (
                      <>
                        <Button
                          variant="ghost"
                          size="sm"
                          aria-label={t("expenses:shares.editNamed", {
                            name: share.member.name,
                          })}
                          onClick={() => openEdit(share)}
                        >
                          {t("expenses:shares.edit")}
                        </Button>
                        {ownerRep ? (
                          <span className={styles.muted}>
                            {t("expenses:shares.locked")}
                          </span>
                        ) : (
                          <Button
                            variant="ghost"
                            size="sm"
                            aria-label={t("expenses:shares.removeNamed", {
                              name: share.member.name,
                            })}
                            onClick={() => setDeleteTarget(share)}
                          >
                            {t("expenses:shares.remove")}
                          </Button>
                        )}
                      </>
                    )}
                  </TableCell>
                </TableRow>
              );
            })}
            <TableRow>
              <TableHeaderCell scope="row">
                {t("expenses:shares.total")}
              </TableHeaderCell>
              <TableCell numeric>
                <Money amount={expense.total} size="lg" format={formatMoneyVnd} />
              </TableCell>
              <TableCell />
              <TableCell />
              <TableCell />
            </TableRow>
          </TableBody>
        </Table>
      </CardBody>

      <ShareFormDialog
        expenseUuid={expense.uuid}
        mode="add"
        members={members}
        existingShares={expense.shares}
        open={addOpen}
        onOpenChange={setAddOpen}
      />
      <ShareFormDialog
        expenseUuid={expense.uuid}
        mode="edit"
        share={editTarget}
        members={members}
        existingShares={expense.shares}
        open={editOpen}
        onOpenChange={setEditOpen}
      />
      <DeleteShareDialog
        expenseUuid={expense.uuid}
        share={deleteTarget}
        open={deleteTarget !== null}
        onOpenChange={(open) => {
          if (!open) setDeleteTarget(null);
        }}
      />
    </Card>
  );
}
