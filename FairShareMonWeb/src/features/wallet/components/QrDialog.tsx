import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useT } from "@/i18n/useT";
import {
  Alert,
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  EmptyState,
  ErrorState,
  Money,
  Select,
  Skeleton,
  UpgradePrompt,
} from "@/components/ui";
import { useToast } from "@/app/ToastHost";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import { formatMoneyVnd } from "@/i18n/format";
import { downloadBlob } from "@/lib/download/downloadBlob";
import { useCurrentUser } from "@/features/auth/hooks/useAuth";
import { useBankAccountsQuery } from "../hooks/useBankAccounts";
import { useEventQrQuery, useExpenseQrQuery } from "../hooks/useQr";
import type { BankAccountResponse } from "../api/types";
import { maskAccount, groupAccount } from "../format";
import { CheckIcon, CopyIcon, DownloadIcon, WalletIcon } from "./icons";
import styles from "./QrDialog.module.css";

export type QrDialogKind = "expense" | "event";

export type QrDialogProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  kind: QrDialogKind;
  /** The expense or event UUID the QR is generated for. */
  targetUuid: string;
  /** Localized dialog title. */
  title: string;
  /** Expense total (shown in the account block for the expense QR). */
  amount?: number;
};

/**
 * The shared VietQR display modal (OQ3a). Owns the query, the error-code → state
 * mapping, and the blob object-URL lifecycle: it fetches the PNG via `api.blob`,
 * creates an object URL, and revokes it on unmount / re-fetch / destination
 * change. The QR image is decorative — the human-readable account block beneath
 * it is the accessible channel and the source for "copy details" (OQ4a: holder +
 * number, never the raw TLV payload).
 *
 * Hybrid Premium gate (OQ1a): a Free user (proactive, by session tier) sees the
 * informational upgrade panel and the query never fires; a stale-tier `403 13003`
 * (reactive) renders the same panel. Error codes branch to friendly states:
 * `12001` no-account (→ /wallet), `12003` no-debt, `12002` not-closed (defensive).
 * An ownership 404 (`6000`/`9000`) closes the dialog with a toast (no existence
 * leak — never a dialog state).
 */
export function QrDialog({
  open,
  onOpenChange,
  kind,
  targetUuid,
  title,
  amount,
}: QrDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const user = useCurrentUser();
  const isPremium = (user?.tier ?? "").toUpperCase() === "PREMIUM";

  // Free-safe read, deferred until the dialog opens (destination picker + block).
  const accountsQuery = useBankAccountsQuery(open && isPremium);
  const accounts = accountsQuery.data ?? [];
  const defaultUuid =
    accounts.find((a) => a.isDefault)?.uuid ?? accounts[0]?.uuid;

  // Destination override (OQ2a). `undefined` → the implicit default account.
  const [selectedUuid, setSelectedUuid] = useState<string | undefined>(undefined);
  useEffect(() => {
    if (!open) setSelectedUuid(undefined);
  }, [open]);

  const displayUuid = selectedUuid ?? defaultUuid;
  // Only send ?bankAccountUuid= when the user picks a non-default account.
  const queryBankAccountUuid =
    displayUuid && displayUuid !== defaultUuid ? displayUuid : undefined;
  const selectedAccount: BankAccountResponse | undefined =
    accounts.find((a) => a.uuid === displayUuid) ?? accounts[0];

  const enabled = open && isPremium;
  const expenseQr = useExpenseQrQuery(
    targetUuid,
    queryBankAccountUuid,
    enabled && kind === "expense",
  );
  const eventQr = useEventQrQuery(
    targetUuid,
    queryBankAccountUuid,
    enabled && kind === "event",
  );
  const qrQuery = kind === "expense" ? expenseQr : eventQr;

  // Blob object-URL lifecycle: create on new data, revoke on the previous URL
  // whenever the blob/destination changes AND on unmount.
  const [imageUrl, setImageUrl] = useState<string | null>(null);
  useEffect(() => {
    const data = qrQuery.data;
    if (!data) {
      setImageUrl(null);
      return;
    }
    const url = URL.createObjectURL(data.blob);
    setImageUrl(url);
    return () => URL.revokeObjectURL(url);
  }, [qrQuery.data]);

  // Ownership 404 → close + toast (never a dialog state; no existence leak). A
  // one-shot ref guards against re-firing: the toast context value is recreated
  // each render and the effect itself pushes a toast, which would otherwise loop.
  const error = qrQuery.error;
  const handled404 = useRef(false);
  useEffect(() => {
    if (!open) {
      handled404.current = false;
      return;
    }
    if (handled404.current) return;
    if (
      isApiError(error) &&
      (error.code === ErrorCodes.ExpenseNotFound ||
        error.code === ErrorCodes.EventNotFound)
    ) {
      handled404.current = true;
      toast.push({ tone: "danger", title: error.message });
      onOpenChange(false);
    }
  }, [error, open, onOpenChange, toast]);

  const errorCode = isApiError(error) ? error.code : 0;
  const isReady = qrQuery.isSuccess && imageUrl != null;
  const showPicker = accounts.length >= 2;

  const fallbackName =
    kind === "expense"
      ? t("wallet:qr.downloadNameExpense")
      : t("wallet:qr.downloadNameEvent");

  function onDownload() {
    if (qrQuery.data) downloadBlob(qrQuery.data, fallbackName);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent title={title} size="sm" closeLabel={t("wallet:qr.close")}>
        <div className={styles.qrBody}>
          <QrDialogInner
            kind={kind}
            isPremium={isPremium}
            errorCode={errorCode}
            isReady={isReady}
            imageUrl={imageUrl}
            hasError={qrQuery.isError}
            showPicker={showPicker}
            accounts={accounts}
            selectedAccount={selectedAccount}
            displayUuid={displayUuid}
            onSelectDestination={setSelectedUuid}
            amount={amount}
            onRetry={() => void qrQuery.refetch()}
          />
        </div>
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              {t("wallet:qr.close")}
            </Button>
          </DialogClose>
          {isReady ? (
            <>
              <CopyDetailsButton account={selectedAccount} />
              <Button
                type="button"
                variant="primary"
                iconStart={<DownloadIcon />}
                onClick={onDownload}
              >
                {t("wallet:qr.download")}
              </Button>
            </>
          ) : null}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/** The state-machine body: premium-gate → no-account → no-debt → not-closed →
 *  error → loading | ready. Presentational; the parent owns the data. */
function QrDialogInner({
  kind,
  isPremium,
  errorCode,
  isReady,
  imageUrl,
  hasError,
  showPicker,
  accounts,
  selectedAccount,
  displayUuid,
  onSelectDestination,
  amount,
  onRetry,
}: {
  kind: QrDialogKind;
  isPremium: boolean;
  errorCode: number;
  isReady: boolean;
  imageUrl: string | null;
  hasError: boolean;
  showPicker: boolean;
  accounts: BankAccountResponse[];
  selectedAccount?: BankAccountResponse;
  displayUuid?: string;
  onSelectDestination: (uuid: string) => void;
  amount?: number;
  onRetry: () => void;
}) {
  const { t } = useT();

  // Premium gate (OQ1a) — proactive (Free) OR reactive (403 13003).
  if (!isPremium || errorCode === ErrorCodes.PremiumFeatureRequired) {
    return (
      <UpgradePrompt
        variant="info"
        title={t("wallet:premium.gateTitle")}
        description={t("wallet:premium.gateBody")}
      />
    );
  }

  // No receiving bank account (12001) — route the user to the wallet.
  if (errorCode === ErrorCodes.NoBankAccountForQr) {
    return (
      <EmptyState
        icon={<WalletIcon />}
        title={t("wallet:qr.noAccountTitle")}
        description={t("wallet:qr.noAccountBody")}
        action={
          <Button asChild variant="primary" size="sm">
            <Link to="/wallet">{t("wallet:qr.addAccount")}</Link>
          </Button>
        }
      />
    );
  }

  // Event: nobody owes (12003) — informational, not an error.
  if (errorCode === ErrorCodes.NoOutstandingDebtForQr) {
    return (
      <Alert tone="info" title={t("wallet:qr.noDebtTitle")}>
        {t("wallet:qr.noDebtBody")}
      </Alert>
    );
  }

  // Event: not closed (12002) — defensive; the button is hidden until closed.
  if (errorCode === ErrorCodes.EventNotClosedForQr) {
    return (
      <Alert tone="warning" title={t("wallet:qr.notClosedTitle")}>
        {t("wallet:qr.notClosedBody")}
      </Alert>
    );
  }

  // Generic load failure — offer a retry.
  if (hasError) {
    return (
      <ErrorState
        title={t("wallet:qr.errorTitle")}
        description={t("wallet:qr.errorBody")}
        action={
          <Button variant="secondary" size="sm" onClick={onRetry}>
            {t("wallet:qr.retry")}
          </Button>
        }
      />
    );
  }

  // loading | ready — same footprint (destination picker + fixed-aspect well).
  return (
    <>
      {showPicker ? (
        <div className={styles.destination}>
          <Select
            label={t("wallet:qr.destinationLabel")}
            value={displayUuid}
            onValueChange={onSelectDestination}
            options={accounts.map((a) => ({
              value: a.uuid,
              label: `${a.bankName} · ${maskAccount(a.accountNumber)}`,
            }))}
          />
        </div>
      ) : null}

      <div className={styles.qrWell}>
        <div className={`${styles.qrFrame} ${styles[kind]}`}>
          {isReady && imageUrl ? (
            <img
              className={styles.qrImage}
              src={imageUrl}
              alt={
                kind === "expense"
                  ? t("wallet:qr.imageAltExpense")
                  : t("wallet:qr.imageAltEvent")
              }
            />
          ) : (
            <Skeleton className={styles.qrSkeleton} width="auto" height="auto" />
          )}
        </div>
      </div>

      {isReady && selectedAccount ? (
        <dl className={styles.accountCard}>
          <dt className={styles.accountTerm}>{t("wallet:qr.bank")}</dt>
          <dd className={styles.accountValue}>{selectedAccount.bankName}</dd>
          <dt className={styles.accountTerm}>{t("wallet:qr.accountNumber")}</dt>
          <dd className={`${styles.accountValue} ${styles.accountNumber}`}>
            {groupAccount(selectedAccount.accountNumber)}
          </dd>
          <dt className={styles.accountTerm}>{t("wallet:qr.holder")}</dt>
          <dd className={styles.accountValue}>
            {selectedAccount.accountHolderName}
          </dd>
          {kind === "expense" && amount != null ? (
            <>
              <dt className={styles.accountTerm}>{t("wallet:qr.amount")}</dt>
              <dd className={styles.accountValue}>
                <Money amount={amount} size="sm" format={formatMoneyVnd} />
              </dd>
            </>
          ) : null}
        </dl>
      ) : null}
    </>
  );
}

/** Copy holder name + account number + bank (OQ4a — NOT the raw VietQR payload). */
function CopyDetailsButton({ account }: { account?: BankAccountResponse }) {
  const { t } = useT();
  const [copied, setCopied] = useState(false);

  function onCopy() {
    // Only confirm the copy on a SUCCESSFUL write. If the Clipboard API is
    // unavailable (insecure origin / older browser) or `writeText` rejects, the
    // copied state must NOT appear — otherwise we'd claim success on a no-op.
    if (!account || !navigator.clipboard) return;
    Promise.resolve(
      navigator.clipboard.writeText(
        `${account.accountHolderName}\n${account.accountNumber}\n${account.bankName}`,
      ),
    )
      .then(() => {
        setCopied(true);
        window.setTimeout(() => setCopied(false), 1600);
      })
      .catch(() => {});
  }

  return (
    <Button
      type="button"
      variant="secondary"
      onClick={onCopy}
      iconStart={copied ? <CheckIcon /> : <CopyIcon />}
    >
      {copied ? t("wallet:qr.copied") : t("wallet:qr.copyDetails")}
    </Button>
  );
}
