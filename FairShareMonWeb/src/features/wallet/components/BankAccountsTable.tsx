import { useState } from "react";
import { useT } from "@/i18n/useT";
import {
  Badge,
  Button,
  Card,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@/components/ui";
import type { BankAccountResponse } from "../api/types";
import { maskAccount, groupAccount } from "../format";
import { useVietqrBanks } from "../hooks/useVietqrBanks";
import { BankLogo } from "./BankLogo";
import {
  EyeIcon,
  EyeOffIcon,
  StarIcon,
  StarOutlineIcon,
} from "./icons";
import styles from "./BankAccountsTable.module.css";

export type BankAccountsTableProps = {
  accounts: BankAccountResponse[];
  /**
   * `premium` shows the action column (set-default / edit / delete); `free` is
   * read-only (no action column). The masked-number reveal is Free-safe in both
   * (reading the user's own number).
   */
  mode: "premium" | "free";
  onSetDefault: (account: BankAccountResponse) => void;
  onEdit: (account: BankAccountResponse) => void;
  onDelete: (account: BankAccountResponse) => void;
};

/**
 * The bank-account list surface: bank + BIN, masked account number (`•••• 1234`)
 * with a per-row `aria-pressed` reveal toggle (OQ5a), holder name, a
 * `Badge tone="settled"`+star default marker (OQ6), and — for Premium — the
 * set-default / edit / delete row actions.
 */
export function BankAccountsTable({
  accounts,
  mode,
  onSetDefault,
  onEdit,
  onDelete,
}: BankAccountsTableProps) {
  const { t } = useT();
  const [revealed, setRevealed] = useState<Record<string, boolean>>({});
  const isPremium = mode === "premium";
  // Re-derive logo + short name from the stored BIN via the cached directory
  // query (one lookup map — never a hook per row).
  const { data: banks } = useVietqrBanks();
  const bankByBin = new Map((banks ?? []).map((b) => [b.bin, b]));

  return (
    <Card padded={false}>
      <Table caption={t("wallet:table.caption")} captionHidden stackOnMobile>
        <TableHead>
          <TableRow>
            <TableHeaderCell scope="col">
              {t("wallet:table.bank")}
            </TableHeaderCell>
            <TableHeaderCell scope="col">
              {t("wallet:table.accountNumber")}
            </TableHeaderCell>
            <TableHeaderCell scope="col">
              {t("wallet:table.holder")}
            </TableHeaderCell>
            <TableHeaderCell scope="col">
              {t("wallet:table.default")}
            </TableHeaderCell>
            {isPremium ? (
              <TableHeaderCell scope="col">
                <span className={styles.srOnly}>{t("wallet:table.actions")}</span>
              </TableHeaderCell>
            ) : null}
          </TableRow>
        </TableHead>
        <TableBody>
          {accounts.map((account) => {
            const show = revealed[account.uuid] ?? false;
            const bank = bankByBin.get(account.bankBin);
            const displayName = bank?.shortName ?? account.bankName;
            return (
              <TableRow key={account.uuid}>
                <TableHeaderCell scope="row">
                  <span className={styles.bankCell}>
                    <BankLogo
                      imageId={bank?.imageId}
                      name={displayName}
                      alt=""
                      size="sm"
                    />
                    <span className={styles.bankText}>
                      <span className={styles.bankName}>{displayName}</span>
                      <span className={styles.bankBin}>
                        {t("wallet:table.bin", { bin: account.bankBin })}
                      </span>
                    </span>
                  </span>
                </TableHeaderCell>
                <TableCell data-label={t("wallet:table.accountNumber")}>
                  <span className={styles.acctCell}>
                    <span className={styles.acctNumber}>
                      {show
                        ? groupAccount(account.accountNumber)
                        : maskAccount(account.accountNumber)}
                    </span>
                    <button
                      type="button"
                      className={styles.revealBtn}
                      aria-pressed={show}
                      aria-label={
                        show
                          ? t("wallet:table.hide", { bank: displayName })
                          : t("wallet:table.reveal", { bank: displayName })
                      }
                      onClick={() =>
                        setRevealed((prev) => ({
                          ...prev,
                          [account.uuid]: !show,
                        }))
                      }
                    >
                      {show ? <EyeOffIcon /> : <EyeIcon />}
                    </button>
                  </span>
                </TableCell>
                <TableCell
                  className={styles.holderCell}
                  data-label={t("wallet:table.holder")}
                >
                  {account.accountHolderName}
                </TableCell>
                <TableCell data-label={t("wallet:table.default")}>
                  {account.isDefault ? (
                    <Badge tone="settled" icon={<StarIcon />}>
                      {t("wallet:badge.default")}
                    </Badge>
                  ) : (
                    <span className={styles.note}>—</span>
                  )}
                </TableCell>
                {isPremium ? (
                  <TableCell actions>
                    {account.isDefault ? null : (
                      <Button
                        variant="ghost"
                        size="sm"
                        aria-label={t("wallet:setDefault.actionNamed", {
                          bank: displayName,
                        })}
                        onClick={() => onSetDefault(account)}
                      >
                        <span className={styles.setDefaultBtn}>
                          <span className={styles.setDefaultIcon} aria-hidden="true">
                            <StarOutlineIcon />
                          </span>
                          {t("wallet:setDefault.action")}
                        </span>
                      </Button>
                    )}
                    <Button
                      variant="ghost"
                      size="sm"
                      aria-label={t("wallet:actions.editNamed", {
                        bank: displayName,
                      })}
                      onClick={() => onEdit(account)}
                    >
                      {t("wallet:actions.edit")}
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      aria-label={t("wallet:actions.deleteNamed", {
                        bank: displayName,
                      })}
                      onClick={() => onDelete(account)}
                    >
                      {t("wallet:actions.delete")}
                    </Button>
                  </TableCell>
                ) : null}
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </Card>
  );
}
