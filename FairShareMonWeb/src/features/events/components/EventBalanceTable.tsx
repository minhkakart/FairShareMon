import {
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  EmptyState,
  ErrorState,
  HelpHint,
  Money,
  Skeleton,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableFoot,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { formatMoneyVnd } from "@/i18n/format";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { SettlementMeter } from "@/features/expenses/components/SettlementMeter";
import {
  SettlementStatusBadge,
  type SettlementTriState,
} from "@/features/expenses/components/SettlementStatusBadge";
import type { EventBalanceResponse, MemberBalanceRow } from "../api/types";
import { useEventBalanceQuery } from "../hooks/useEvents";
import { MemberSettledToggle } from "./MemberSettledToggle";
import { CheckIcon, ClockIcon } from "./icons";
import styles from "./EventBalanceTable.module.css";

export type EventBalanceTableProps = {
  uuid: string;
};

/**
 * The polarity word beside a signed balance figure — the color-independent cue
 * that backs up the `Money variant="balance"` +/− sign glyph (CVD-safe).
 */
function BalanceAmount({ amount }: { amount: number }) {
  const { t } = useT();
  const label =
    amount > 0
      ? t("events:balance.positiveLabel")
      : amount < 0
        ? t("events:balance.negativeLabel")
        : t("events:balance.zeroLabel");
  return (
    <span className={styles.balanceCell}>
      <Money amount={amount} variant="balance" format={formatMoneyVnd} />
      <span className={styles.polarity}>{label}</span>
    </span>
  );
}

// member | advanced | owed | balance | còn nợ | trạng thái (§6 overlay, OQ4a).
const COLUMN_COUNT = 6;

/**
 * The §3.7 debt-balance table (ui-designer spec) + the §6 settled overlay (D2).
 * One row per participating member (incl. the owner-rep at 0đ and soft-deleted
 * members): advanced / owed / balance rendered via `Money` (verbatim, never
 * re-computed) — those columns and the sum-to-zero `TableFoot` total stay PURE
 * and untouched. Additive overlay columns render `outstanding` (còn nợ) + a
 * per-member settled toggle for owing members (`balance < 0`, OQ5a) — and,
 * since event-expense-settlement-sync (2026-08-25, Direction 1), for net-
 * creditor members eligible for the auto-cascade too (`balance >= 0 &&
 * isEligibleForAutoCascade`, M1-R2/OQ2) — plus a `totalOutstanding`/X-of-Y
 * summary read verbatim from the API. Since Direction 2 (M2-R4/R5), the owing-
 * row status is a 3-state `SettlementStatusBadge` (`Unsettled`/
 * `PartiallySettled`/`Settled`, driven by `row.settlementStatus`, never re-
 * derived), and a `PartiallySettled` row's "Còn nợ" cell renders a
 * `SettlementMeter` fraction (`clearedAmount` / `clearedAmount + outstanding`)
 * instead of the plain `<Money>` figure; the footer also surfaces
 * `partiallySettledMemberCount`. Shown for open AND closed events (OQ8a/OQ9a);
 * the per-member toggle is enabled on both (the sole closed-event write, R6).
 * An event with no expenses shows a calm empty note.
 */
export function EventBalanceTable({ uuid }: EventBalanceTableProps) {
  const { t } = useT();
  const balanceQuery = useEventBalanceQuery(uuid);

  return (
    <Card>
      <CardHeader title={t("events:balance.title")} />
      <CardBody>
        {balanceQuery.isError ? (
          <ErrorState
            title={t("events:balance.errorTitle")}
            description={resolveErrorMessage(balanceQuery.error, t)}
            action={
              <Button
                variant="secondary"
                onClick={() => void balanceQuery.refetch()}
              >
                {t("events:list.retry")}
              </Button>
            }
          />
        ) : balanceQuery.isPending ? (
          <Table caption={t("events:balance.caption")} captionHidden>
            <TableHead>
              <BalanceHeadRow />
            </TableHead>
            <TableBody>
              {Array.from({ length: 3 }).map((_, index) => (
                <TableRow key={index}>
                  <TableHeaderCell scope="row">
                    <Skeleton width="10rem" />
                  </TableHeaderCell>
                  <TableCell numeric>
                    <Skeleton width="6rem" />
                  </TableCell>
                  <TableCell numeric>
                    <Skeleton width="6rem" />
                  </TableCell>
                  <TableCell numeric>
                    <Skeleton width="6rem" />
                  </TableCell>
                  <TableCell numeric>
                    <Skeleton width="6rem" />
                  </TableCell>
                  <TableCell>
                    <Skeleton width="7rem" />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <BalanceRows eventUuid={uuid} balance={balanceQuery.data} />
        )}
      </CardBody>
    </Card>
  );
}

function BalanceHeadRow() {
  const { t } = useT();
  return (
    <TableRow>
      <TableHeaderCell>{t("events:balance.member")}</TableHeaderCell>
      <TableHeaderCell numeric>{t("events:balance.advanced")}</TableHeaderCell>
      <TableHeaderCell numeric>{t("events:balance.owed")}</TableHeaderCell>
      <TableHeaderCell numeric>{t("events:balance.balance")}</TableHeaderCell>
      <TableHeaderCell numeric>
        <span className={styles.headerHint}>
          {t("events:balance.outstanding")}
          <HelpHint
            label={t("events:balance.clearedModelHintLabel")}
            placement="bottom"
          >
            {t("events:balance.clearedModelHint")}
          </HelpHint>
        </span>
      </TableHeaderCell>
      <TableHeaderCell>{t("events:balance.statusColumn")}</TableHeaderCell>
    </TableRow>
  );
}

/**
 * Maps the wire tri-state (`MemberBalanceRow.settlementStatus`, PascalCase) to
 * `SettlementStatusBadge`'s local, decoupled `SettlementTriState` union. Falls
 * back to `"unsettled"` for any unrecognized value (defensive only — the API
 * always returns one of the three named states).
 */
function toSettlementTriState(
  status: MemberBalanceRow["settlementStatus"],
): SettlementTriState {
  if (status === "Settled") return "settled";
  if (status === "PartiallySettled") return "partial";
  return "unsettled";
}

/**
 * The overlay status cell (OQ4a/OQ5a, extended by event-expense-settlement-sync
 * M1-R2/OQ2 and M2-R4): for an owing member (`balance < 0`, unchanged branch),
 * the color-independent 3-state `SettlementStatusBadge` (Unsettled/
 * PartiallySettled/Settled, driven by `row.settlementStatus`, never re-derived)
 * plus the per-member settled toggle. For a net creditor (`balance >= 0`) that
 * is Direction-1 auto-cascade-eligible, the plain (binary) `Badge` + toggle is
 * shown unchanged from Milestone 1, paired with a `HelpHint` explaining the
 * cascade — Milestone 2's partial-credit status never applies to a creditor row
 * (`Outstanding` floors at 0 regardless). For a net creditor that is NOT
 * eligible (holds a debtor-share elsewhere in the event), the toggle is hidden
 * entirely and the muted "—" is replaced by a `HelpHint` explaining why. A true
 * net-zero balance stays exactly as today: plain muted "—", no hint (never
 * folded into the ineligible-creditor branch).
 */
function StatusCell({
  eventUuid,
  row,
}: {
  eventUuid: string;
  row: MemberBalanceRow;
}) {
  const { t } = useT();

  if (row.balance < 0) {
    return (
      <div className={styles.statusCell}>
        <SettlementStatusBadge
          status={toSettlementTriState(row.settlementStatus)}
          labelUnsettled={t("events:balance.statusOwing")}
          labelPartial={t("events:balance.statusPartial")}
          labelSettled={t("events:balance.statusSettled")}
        />
        <MemberSettledToggle
          eventUuid={eventUuid}
          memberUuid={row.memberUuid}
          memberName={row.memberName}
          isSettled={row.isSettled}
          isEligibleForAutoCascade={row.isEligibleForAutoCascade}
        />
      </div>
    );
  }

  if (row.balance > 0 && row.isEligibleForAutoCascade) {
    return (
      <div className={styles.statusCell}>
        <Badge
          tone={row.isSettled ? "settled" : "warning"}
          icon={row.isSettled ? <CheckIcon /> : <ClockIcon />}
        >
          {row.isSettled
            ? t("events:balance.statusSettled")
            : t("events:balance.statusOwing")}
        </Badge>
        <MemberSettledToggle
          eventUuid={eventUuid}
          memberUuid={row.memberUuid}
          memberName={row.memberName}
          isSettled={row.isSettled}
          isEligibleForAutoCascade={row.isEligibleForAutoCascade}
        />
        <HelpHint label={t("events:balance.creditorEligibleHintLabel")}>
          {t("events:balance.creditorEligibleHint")}
        </HelpHint>
      </div>
    );
  }

  if (row.balance > 0 && !row.isEligibleForAutoCascade) {
    return (
      <div className={styles.statusCell}>
        <span className={styles.muted}>—</span>
        <HelpHint label={t("events:balance.creditorIneligibleHintLabel")}>
          {t("events:balance.creditorIneligibleHint")}
        </HelpHint>
      </div>
    );
  }

  return <span className={styles.muted}>—</span>;
}

function BalanceRows({
  eventUuid,
  balance,
}: {
  eventUuid: string;
  balance: EventBalanceResponse;
}) {
  const { t } = useT();
  const rows = balance.rows;

  // Footer column sums. Money amounts are whole VND (0 fraction digits), so
  // integer addition is exact — no fractional float math (R3). The balance total
  // is the API's documented sum-to-zero invariant, rendered as 0 (never
  // client-summed, per the plan). `totalOutstanding`/counts are read verbatim
  // from the API (D2 — never client-derived).
  const advancedTotal = rows.reduce((sum, r) => sum + r.advanced, 0);
  const owedTotal = rows.reduce((sum, r) => sum + r.owed, 0);

  return (
    <Table caption={t("events:balance.caption")} captionHidden>
      <TableHead>
        <BalanceHeadRow />
      </TableHead>
      <TableBody>
        {rows.length === 0 ? (
          <TableEmpty colSpan={COLUMN_COUNT}>
            <EmptyState
              title={t("events:balance.emptyTitle")}
              description={t("events:balance.emptyBody")}
            />
          </TableEmpty>
        ) : (
          rows.map((row) => (
            <TableRow
              key={row.memberUuid}
              deleted={row.isDeleted}
              data-testid="event-balance-row"
            >
              <TableHeaderCell scope="row">
                <span className={styles.memberCell}>
                  <span className={styles.memberName}>{row.memberName}</span>
                  {row.isOwnerRepresentative ? (
                    <span className={styles.repTag}>
                      {t("events:balance.ownerRep")}
                    </span>
                  ) : null}
                  {row.isDeleted ? (
                    <span className={styles.deletedTag}>
                      {t("events:balance.deletedTag")}
                    </span>
                  ) : null}
                </span>
              </TableHeaderCell>
              <TableCell numeric>
                <Money amount={row.advanced} format={formatMoneyVnd} />
              </TableCell>
              <TableCell numeric>
                <Money amount={row.owed} format={formatMoneyVnd} />
              </TableCell>
              <TableCell numeric data-testid="balance-amount">
                <BalanceAmount amount={row.balance} />
              </TableCell>
              <TableCell numeric data-testid="outstanding-amount">
                {row.settlementStatus === "PartiallySettled" ? (
                  <SettlementMeter
                    clearedAmount={row.clearedAmount}
                    netOwed={row.clearedAmount + row.outstanding}
                    format={formatMoneyVnd}
                    accessibleLabel={t("events:balance.clearedAriaNamed", {
                      name: row.memberName,
                    })}
                  />
                ) : row.outstanding > 0 ? (
                  <Money amount={row.outstanding} format={formatMoneyVnd} />
                ) : (
                  <span className={styles.muted}>—</span>
                )}
              </TableCell>
              <TableCell>
                <StatusCell eventUuid={eventUuid} row={row} />
              </TableCell>
            </TableRow>
          ))
        )}
      </TableBody>
      {rows.length > 0 ? (
        <TableFoot>
          <TableRow total data-testid="event-balance-total">
            <TableHeaderCell scope="row">
              {t("events:balance.totalRow")}
              <span className={styles.sumHint}>
                {t("events:balance.sumsToZeroHint")}
              </span>
            </TableHeaderCell>
            <TableCell numeric>
              <Money amount={advancedTotal} format={formatMoneyVnd} />
            </TableCell>
            <TableCell numeric>
              <Money amount={owedTotal} format={formatMoneyVnd} />
            </TableCell>
            <TableCell numeric>
              <BalanceAmount amount={0} />
            </TableCell>
            <TableCell numeric data-testid="total-outstanding">
              <Money amount={balance.totalOutstanding} format={formatMoneyVnd} />
            </TableCell>
            <TableCell>
              <span className={styles.summary}>
                {t("events:balance.summary", {
                  settled: balance.settledMemberCount,
                  total:
                    balance.settledMemberCount + balance.owingMemberCount,
                  amount: formatMoneyVnd(balance.totalOutstanding),
                })}
              </span>
              {balance.partiallySettledMemberCount > 0 ? (
                <span className={styles.summary}>
                  {t("events:balance.summaryPartial", {
                    count: balance.partiallySettledMemberCount,
                  })}
                </span>
              ) : null}
            </TableCell>
          </TableRow>
        </TableFoot>
      ) : null}
    </Table>
  );
}
