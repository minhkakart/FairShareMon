import { useState } from "react";
import { useT } from "@/i18n/useT";
import {
  Button,
  Card,
  EmptyState,
  ErrorState,
  PageHeader,
  Skeleton,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  TierBadge,
  UpgradePrompt,
} from "@/components/ui";
import { useToast } from "@/app/ToastHost";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { useCurrentUser } from "@/features/auth/hooks/useAuth";
import {
  useBankAccountsQuery,
  useSetDefaultBankAccount,
} from "../hooks/useBankAccounts";
import { BankAccountsTable } from "../components/BankAccountsTable";
import { BankAccountFormDialog } from "../components/BankAccountFormDialog";
import { DeleteBankAccountDialog } from "../components/DeleteBankAccountDialog";
import type { BankAccountResponse } from "../api/types";
import { PlusIcon, WalletIcon } from "../components/icons";
import styles from "./WalletPage.module.css";

const SKELETON_ROWS = 3;

function LoadingRows() {
  return (
    <>
      {Array.from({ length: SKELETON_ROWS }).map((_, index) => (
        <TableRow key={index}>
          <TableHeaderCell scope="row">
            <Skeleton width="8rem" />
          </TableHeaderCell>
          <TableCell>
            <Skeleton width="7rem" />
          </TableCell>
          <TableCell>
            <Skeleton width="9rem" />
          </TableCell>
          <TableCell>
            <Skeleton width="5rem" />
          </TableCell>
        </TableRow>
      ))}
    </>
  );
}

/**
 * /wallet — the caller's receiving bank accounts (default first). Wallet reads
 * are Free; every mutation is Premium. The hybrid gate (OQ1a): a Free user sees
 * a read-only table + an informational `UpgradePrompt` (no self-serve — Premium
 * is a manual admin grant); a Premium user gets create / edit / set-default /
 * delete. Set-default + delete-promotion are entirely server-side — the client
 * invalidates the list and re-renders the backend's `isDefault`.
 */
export function WalletPage() {
  const { t } = useT();
  const toast = useToast();
  const user = useCurrentUser();
  const isPremium = (user?.tier ?? "").toUpperCase() === "PREMIUM";

  const accountsQuery = useBankAccountsQuery();
  const accounts = accountsQuery.data ?? [];
  const setDefault = useSetDefaultBankAccount();

  const [formOpen, setFormOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<BankAccountResponse | undefined>(
    undefined,
  );
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<BankAccountResponse | null>(
    null,
  );

  function openCreate() {
    setEditTarget(undefined);
    setFormOpen(true);
  }

  function openEdit(account: BankAccountResponse) {
    setEditTarget(account);
    setFormOpen(true);
  }

  function openDelete(account: BankAccountResponse) {
    setDeleteTarget(account);
    setDeleteOpen(true);
  }

  async function onSetDefault(account: BankAccountResponse) {
    try {
      await setDefault.mutateAsync(account.uuid);
      toast.push({ tone: "success", title: t("wallet:setDefault.toast") });
    } catch (error) {
      // 13003 (stale-tier gate) / 12000 (stale row) surface localized text.
      toast.push({ tone: "danger", title: resolveErrorMessage(error, t) });
    }
  }

  return (
    <Stack gap="6">
      <PageHeader
        title={t("wallet:title")}
        description={t("wallet:subtitle")}
        actions={
          <div className={styles.headerActions}>
            <TierBadge
              tier={user?.tier}
              freeLabel={t("wallet:tier.free")}
              premiumLabel={t("wallet:tier.premium")}
            />
            {isPremium ? (
              <Button
                variant="primary"
                iconStart={<PlusIcon />}
                onClick={openCreate}
              >
                {t("wallet:add")}
              </Button>
            ) : null}
          </div>
        }
      />

      {accountsQuery.isError ? (
        <ErrorState
          title={t("wallet:error.title")}
          description={resolveErrorMessage(accountsQuery.error, t)}
          action={
            <Button
              variant="secondary"
              onClick={() => void accountsQuery.refetch()}
            >
              {t("wallet:error.retry")}
            </Button>
          }
        />
      ) : accountsQuery.isPending ? (
        <Card padded={false}>
          <Table caption={t("wallet:table.caption")} captionHidden>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t("wallet:table.bank")}</TableHeaderCell>
                <TableHeaderCell>
                  {t("wallet:table.accountNumber")}
                </TableHeaderCell>
                <TableHeaderCell>{t("wallet:table.holder")}</TableHeaderCell>
                <TableHeaderCell>{t("wallet:table.default")}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              <LoadingRows />
            </TableBody>
          </Table>
        </Card>
      ) : accounts.length === 0 ? (
        <Card>
          {isPremium ? (
            <EmptyState
              icon={<WalletIcon />}
              title={t("wallet:empty.premiumTitle")}
              description={t("wallet:empty.premiumBody")}
              action={
                <Button
                  variant="primary"
                  iconStart={<PlusIcon />}
                  onClick={openCreate}
                >
                  {t("wallet:add")}
                </Button>
              }
            />
          ) : (
            <EmptyState
              icon={<WalletIcon />}
              title={t("wallet:empty.title")}
              description={t("wallet:empty.body")}
            />
          )}
        </Card>
      ) : isPremium ? (
        <BankAccountsTable
          accounts={accounts}
          mode="premium"
          onSetDefault={onSetDefault}
          onEdit={openEdit}
          onDelete={openDelete}
        />
      ) : (
        <div className={styles.walletStack}>
          <UpgradePrompt
            variant="info"
            title={t("wallet:premium.title")}
            description={t("wallet:premium.info")}
          />
          <BankAccountsTable
            accounts={accounts}
            mode="free"
            onSetDefault={onSetDefault}
            onEdit={openEdit}
            onDelete={openDelete}
          />
        </div>
      )}

      <BankAccountFormDialog
        mode={editTarget ? "edit" : "create"}
        account={editTarget}
        open={formOpen}
        onOpenChange={setFormOpen}
      />
      <DeleteBankAccountDialog
        account={deleteTarget}
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
      />
    </Stack>
  );
}
