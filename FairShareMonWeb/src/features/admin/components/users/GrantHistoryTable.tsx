import {
  Badge,
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
import { formatDateTime } from "@/i18n/format";
import type { TierGrantRow } from "../../api/types";
import { AdminTierBadge } from "../AdminTierBadge";
import { BanIcon, CheckIcon } from "../icons";
import styles from "../admin.module.css";

const COLUMN_COUNT = 7;

/**
 * The tier grant/revoke history table (from tier_grants — R10-safe: it is admin
 * data, not ledger data). GRANT rows show a success badge + `<Money>` amount;
 * REVOKE rows a neutral badge + a "—" amount (revokes carry 0 / no revenue).
 */
export function GrantHistoryTable({
  grants,
  username,
}: {
  grants: TierGrantRow[];
  username: string;
}) {
  const { t } = useT();

  return (
    <Table
      caption={t("admin:detail.grantHistory.caption", { name: username })}
      captionHidden
    >
      <TableHead>
        <TableRow>
          <TableHeaderCell scope="col">
            {t("admin:detail.grantHistory.action")}
          </TableHeaderCell>
          <TableHeaderCell scope="col">
            {t("admin:detail.grantHistory.tier")}
          </TableHeaderCell>
          <TableHeaderCell scope="col" numeric>
            {t("admin:detail.grantHistory.amount")}
          </TableHeaderCell>
          <TableHeaderCell scope="col">
            {t("admin:detail.grantHistory.reference")}
          </TableHeaderCell>
          <TableHeaderCell scope="col">
            {t("admin:detail.grantHistory.note")}
          </TableHeaderCell>
          <TableHeaderCell scope="col">
            {t("admin:detail.grantHistory.grantedBy")}
          </TableHeaderCell>
          <TableHeaderCell scope="col">
            {t("admin:detail.grantHistory.createdAt")}
          </TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {grants.length === 0 ? (
          <TableEmpty colSpan={COLUMN_COUNT}>
            <EmptyState title={t("admin:detail.grantHistory.empty")} />
          </TableEmpty>
        ) : (
          grants.map((g) => {
            const isGrant = g.action === "GRANT";
            return (
              <TableRow key={g.uuid}>
                <TableCell>
                  {isGrant ? (
                    <Badge tone="success" icon={<CheckIcon />}>
                      {t("admin:grantAction.grant")}
                    </Badge>
                  ) : (
                    <Badge tone="neutral" icon={<BanIcon />}>
                      {t("admin:grantAction.revoke")}
                    </Badge>
                  )}
                </TableCell>
                <TableCell>
                  <AdminTierBadge tier={g.tier} />
                </TableCell>
                <TableCell numeric>
                  {isGrant ? (
                    <Money amount={g.amount} />
                  ) : (
                    <span className={styles.mono}>{t("admin:users.none")}</span>
                  )}
                </TableCell>
                <TableCell>
                  <span className={styles.mono}>
                    {g.reference ?? t("admin:users.none")}
                  </span>
                </TableCell>
                <TableCell>{g.note ?? t("admin:users.none")}</TableCell>
                <TableCell>{g.grantedByUsername}</TableCell>
                <TableCell>
                  <span className={styles.mono}>
                    {formatDateTime(g.createdAt)}
                  </span>
                </TableCell>
              </TableRow>
            );
          })
        )}
      </TableBody>
    </Table>
  );
}
