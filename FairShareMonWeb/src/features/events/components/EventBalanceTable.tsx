import {
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  EmptyState,
  ErrorState,
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
 * đã-trả/còn-nợ status with a per-member settled toggle for owing members
 * (`balance < 0`, OQ5a), plus a `totalOutstanding`/X-of-Y summary read verbatim
 * from the API. Shown for open AND closed events (OQ8a/OQ9a); the per-member
 * toggle is enabled on both (the sole closed-event write, R6). An event with no
 * expenses shows a calm empty note.
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
        {t("events:balance.outstanding")}
      </TableHeaderCell>
      <TableHeaderCell>{t("events:balance.statusColumn")}</TableHeaderCell>
    </TableRow>
  );
}

/**
 * The overlay status cell (OQ4a/OQ5a): for an owing member (`balance < 0`), a
 * color-independent đã-trả/còn-nợ badge (icon + text) plus the per-member settled
 * toggle; owed/zero members (`balance >= 0`) show a muted "—" (marking them has
 * no overlay effect — OQ5a).
 */
function StatusCell({
  eventUuid,
  row,
}: {
  eventUuid: string;
  row: MemberBalanceRow;
}) {
  const { t } = useT();
  if (row.balance >= 0) {
    return <span className={styles.muted}>—</span>;
  }
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
      />
    </div>
  );
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
                {row.outstanding > 0 ? (
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
            </TableCell>
          </TableRow>
        </TableFoot>
      ) : null}
    </Table>
  );
}
