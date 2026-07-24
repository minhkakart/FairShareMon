import { useT } from "@/i18n/useT";
import { formatMoneyVnd, formatDateTime } from "@/i18n/format";
import { Money } from "@/components/ui";
import type { PublicExpense } from "../api/types";
import styles from "./MemberExpenseBreakdown.module.css";

export type MemberExpenseBreakdownProps = {
  memberUuid: string;
  memberName: string;
  expenses: PublicExpense[];
};

/**
 * The per-member drill-in (public, read-only): for the expanded member, the
 * expenses they have a share in — expense name + time + the member's own share
 * amount — annotated with what the member advanced when they are the payer.
 * Purely presentational; VND via `formatMoneyVnd`, dates via `formatDateTime`.
 */
export function MemberExpenseBreakdown({
  memberUuid,
  memberName,
  expenses,
}: MemberExpenseBreakdownProps) {
  const { t } = useT();

  // Group by picking this member's share out of each expense (an expense the
  // member has no share in is skipped). Preserves the payload's expense order.
  const items = expenses
    .map((expense) => ({
      expense,
      share: expense.shares.find((s) => s.memberUuid === memberUuid),
    }))
    .filter(
      (item): item is { expense: PublicExpense; share: NonNullable<typeof item.share> } =>
        item.share != null,
    );

  return (
    <div className={styles.breakdown}>
      <h2 className={styles.title}>{t("share:breakdown.title", { name: memberName })}</h2>
      {items.length === 0 ? (
        <p className={styles.empty}>{t("share:breakdown.empty")}</p>
      ) : (
        <ul className={styles.list}>
          {items.map(({ expense, share }) => {
            const isPayer = expense.payerMemberUuid === memberUuid;
            return (
              <li key={expense.uuid} className={styles.item}>
                <div className={styles.itemMain}>
                  <span className={styles.expenseName}>{expense.name}</span>
                  <Money
                    amount={share.amount}
                    format={formatMoneyVnd}
                    className={styles.shareAmount}
                  />
                </div>
                <div className={styles.itemMeta}>
                  <span className={styles.time}>
                    {formatDateTime(expense.expenseTime)}
                  </span>
                  <span className={styles.payer}>
                    {t("share:breakdown.paidBy", { name: expense.payerName })}
                  </span>
                  {share.isSettled ? (
                    <span className={styles.settledTag}>
                      {t("share:breakdown.settledTag")}
                    </span>
                  ) : null}
                </div>
                {isPayer ? (
                  <div className={styles.advanced}>
                    {t("share:breakdown.advancedAsPayer", {
                      amount: formatMoneyVnd(expense.total),
                    })}
                  </div>
                ) : null}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
