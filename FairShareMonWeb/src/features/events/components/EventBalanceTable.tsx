import {
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
import type { MemberBalanceRow } from "../api/types";
import { useEventBalanceQuery } from "../hooks/useEvents";
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

const COLUMN_COUNT = 4;

/**
 * The §3.7 debt-balance table (ui-designer spec). One row per participating
 * member (incl. the owner-rep at 0đ and soft-deleted members): advanced / owed /
 * balance rendered via `Money` (verbatim, never re-computed). The `TableFoot`
 * total row proves sum-to-zero: advanced/owed are the exact whole-VND column
 * sums of the server-provided rows, and balance is rendered as the API's
 * documented sum-to-zero invariant (0) — never client-summed. Shown for open AND
 * closed events (OQ8a); an event with no expenses shows a calm empty note.
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
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <BalanceRows rows={balanceQuery.data.rows} />
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
    </TableRow>
  );
}

function BalanceRows({ rows }: { rows: MemberBalanceRow[] }) {
  const { t } = useT();

  // Footer column sums. Money amounts are whole VND (0 fraction digits), so
  // integer addition is exact — no fractional float math (R3). The balance total
  // is the API's documented sum-to-zero invariant, rendered as 0 (never
  // client-summed, per the plan).
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
            <TableRow key={row.memberUuid} deleted={row.isDeleted}>
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
              <TableCell numeric>
                <BalanceAmount amount={row.balance} />
              </TableCell>
            </TableRow>
          ))
        )}
      </TableBody>
      {rows.length > 0 ? (
        <TableFoot>
          <TableRow total>
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
          </TableRow>
        </TableFoot>
      ) : null}
    </Table>
  );
}
