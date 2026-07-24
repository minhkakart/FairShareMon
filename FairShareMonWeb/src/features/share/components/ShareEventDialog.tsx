import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import {
  Alert,
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  ErrorState,
  Select,
  Skeleton,
  UpgradePrompt,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useToast } from "@/app/ToastHost";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { formatDateTime } from "@/i18n/format";
import { useCurrentUser } from "@/features/auth/hooks/useAuth";
import { useBankAccountsQuery } from "@/features/wallet/hooks/useBankAccounts";
import { maskAccount, groupAccount } from "@/features/wallet/format";
import { CheckIcon, CopyIcon, WalletIcon } from "@/features/wallet/components/icons";
import type { EventResponse } from "@/features/events/api/types";
import {
  useActiveShareLinkQuery,
  useCreateShareLink,
  useRevokeShareLink,
} from "../hooks/useShare";
import type { ShareLinkResponse } from "../api/types";
import styles from "./ShareEventDialog.module.css";

export type ShareEventDialogProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  event: EventResponse;
};

type ConfirmKind = "revoke" | "regenerate" | null;

/**
 * Owner-side Share dialog (Premium-gated, closed-events-only). Mirrors
 * `QrDialog`'s hybrid gate: a Free user (or a stale-tier reactive `403 13003`)
 * sees the informational upgrade panel and the create mutation never fires. A
 * Premium owner sees either the create form (pick a receiving account — optional
 * per OQ2) or, if a link already exists, its public URL + copy + expiry +
 * inline-confirm revoke/regenerate (OQ3).
 */
export function ShareEventDialog({ open, onOpenChange, event }: ShareEventDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const user = useCurrentUser();
  const isPremium = (user?.tier ?? "").toUpperCase() === "PREMIUM";

  const enabled = open && isPremium;
  const activeQuery = useActiveShareLinkQuery(event.uuid, enabled);
  const accountsQuery = useBankAccountsQuery(enabled);
  const createMut = useCreateShareLink();
  const revokeMut = useRevokeShareLink();

  const accounts = accountsQuery.data ?? [];
  const defaultUuid = accounts.find((a) => a.isDefault)?.uuid ?? accounts[0]?.uuid;
  const [selectedUuid, setSelectedUuid] = useState<string | undefined>(undefined);
  const displayUuid = selectedUuid ?? defaultUuid;

  const [confirm, setConfirm] = useState<ConfirmKind>(null);
  const [copied, setCopied] = useState(false);

  // Reset transient UI state whenever the dialog closes.
  useEffect(() => {
    if (!open) {
      setSelectedUuid(undefined);
      setConfirm(null);
      setCopied(false);
    }
  }, [open]);

  // Ownership 404 → close + danger toast (no existence leak, never a dialog
  // state). A one-shot ref stops the toast-pushing effect from looping.
  const handled404 = useRef(false);
  useEffect(() => {
    if (!open) {
      handled404.current = false;
      return;
    }
    if (handled404.current) return;
    const err = activeQuery.error;
    if (isApiError(err) && err.code === ErrorCodes.EventNotFound) {
      handled404.current = true;
      toast.push({ tone: "danger", title: err.message });
      onOpenChange(false);
    }
  }, [activeQuery.error, open, onOpenChange, toast]);

  const errorCode = isApiError(activeQuery.error) ? activeQuery.error.code : 0;
  const hasBank = accounts.length > 0;

  async function onCreate(regenerate: boolean) {
    try {
      await createMut.mutateAsync({
        eventUuid: event.uuid,
        body: {
          bankAccountUuid: hasBank ? displayUuid : undefined,
          regenerate: regenerate || undefined,
        },
      });
      setConfirm(null);
      toast.push({
        tone: "success",
        title: t(regenerate ? "share:link.regenerated" : "share:link.created"),
      });
    } catch (error) {
      toast.push({ tone: "danger", title: resolveErrorMessage(error, t) });
    }
  }

  async function onRevoke() {
    try {
      await revokeMut.mutateAsync(event.uuid);
      setConfirm(null);
      toast.push({ tone: "success", title: t("share:link.revoked") });
    } catch (error) {
      toast.push({ tone: "danger", title: resolveErrorMessage(error, t) });
    }
  }

  function onCopy(token: string) {
    if (!navigator.clipboard) return;
    const url = `${window.location.origin}/share/${token}`;
    Promise.resolve(navigator.clipboard.writeText(url))
      .then(() => {
        setCopied(true);
        window.setTimeout(() => setCopied(false), 1600);
      })
      .catch(() => {});
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={t("share:dialog.title")}
        description={t("share:dialog.description")}
        size="sm"
        closeLabel={t("share:link.cancel")}
      >
        <div className={styles.body}>
          {!isPremium || errorCode === ErrorCodes.PremiumFeatureRequired ? (
            <UpgradePrompt
              variant="info"
              title={t("share:premium.gateTitle")}
              description={t("share:premium.gateBody")}
            />
          ) : errorCode === ErrorCodes.EventNotClosedForShare ? (
            <Alert tone="warning" title={t("share:notClosed.title")}>
              {t("share:notClosed.body")}
            </Alert>
          ) : errorCode === ErrorCodes.EventNotFound ? (
            <Skeleton width="100%" height="6rem" />
          ) : activeQuery.isPending || accountsQuery.isPending ? (
            // Hold the skeleton until BOTH the active-link GET and the bank-accounts GET resolve, so
            // the create form never briefly shows the "no receiving account" (QR-less) path to an owner
            // who actually has accounts (OQ2) before accountsQuery lands from a cold cache.
            <Skeleton width="100%" height="6rem" />
          ) : activeQuery.isError ? (
            <ErrorState
              title={t("share:error.title")}
              description={resolveErrorMessage(activeQuery.error, t)}
              action={
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => void activeQuery.refetch()}
                >
                  {t("share:error.retry")}
                </Button>
              }
            />
          ) : activeQuery.data ? (
            <LinkView
              link={activeQuery.data}
              copied={copied}
              confirm={confirm}
              revoking={revokeMut.isPending}
              regenerating={createMut.isPending}
              onCopy={onCopy}
              onSetConfirm={setConfirm}
              onRevoke={() => void onRevoke()}
              onRegenerate={() => void onCreate(true)}
            />
          ) : (
            <CreateForm
              hasBank={hasBank}
              accounts={accounts.map((a) => ({
                value: a.uuid,
                label: `${a.bankName} · ${maskAccount(a.accountNumber)}`,
              }))}
              value={displayUuid}
              onSelect={setSelectedUuid}
              creating={createMut.isPending}
              onCreate={() => void onCreate(false)}
            />
          )}
        </div>
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              {t("share:link.cancel")}
            </Button>
          </DialogClose>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/** No active link yet → pick a receiving account (optional) + create. */
function CreateForm({
  hasBank,
  accounts,
  value,
  onSelect,
  creating,
  onCreate,
}: {
  hasBank: boolean;
  accounts: { value: string; label: string }[];
  value: string | undefined;
  onSelect: (uuid: string) => void;
  creating: boolean;
  onCreate: () => void;
}) {
  const { t } = useT();
  return (
    <div className={styles.create}>
      {hasBank ? (
        <Select
          label={t("share:create.destinationLabel")}
          placeholder={t("share:create.destinationPlaceholder")}
          hint={t("share:create.destinationHint")}
          value={value}
          onValueChange={onSelect}
          options={accounts}
          required
        />
      ) : (
        <div className={styles.noBank}>
          <span className={styles.noBankIcon} aria-hidden="true">
            <WalletIcon />
          </span>
          <div>
            <p className={styles.noBankTitle}>{t("share:create.noBankTitle")}</p>
            <p className={styles.noBankBody}>{t("share:create.noBankHint")}</p>
            <Button asChild variant="ghost" size="sm">
              <Link to="/wallet">{t("share:create.toWallet")}</Link>
            </Button>
          </div>
        </div>
      )}
      <Button
        type="button"
        variant="primary"
        loading={creating}
        onClick={onCreate}
      >
        {t("share:create.submit")}
      </Button>
    </div>
  );
}

/** An active link exists → URL + copy + expiry + inline-confirm revoke/regenerate. */
function LinkView({
  link,
  copied,
  confirm,
  revoking,
  regenerating,
  onCopy,
  onSetConfirm,
  onRevoke,
  onRegenerate,
}: {
  link: ShareLinkResponse;
  copied: boolean;
  confirm: ConfirmKind;
  revoking: boolean;
  regenerating: boolean;
  onCopy: (token: string) => void;
  onSetConfirm: (kind: ConfirmKind) => void;
  onRevoke: () => void;
  onRegenerate: () => void;
}) {
  const { t } = useT();
  const url = `${window.location.origin}/share/${link.token}`;
  const isExpired = new Date(link.expiresAt).getTime() < Date.now();

  return (
    <div className={styles.link}>
      {isExpired ? (
        <Alert tone="warning" title={t("share:link.expiredTitle")}>
          {t("share:link.expiredBody")}
        </Alert>
      ) : (
        <>
          <div className={styles.urlField}>
            <span className={styles.fieldLabel}>{t("share:link.urlLabel")}</span>
            <div className={styles.urlRow}>
              <input
                className={styles.urlInput}
                type="text"
                readOnly
                value={url}
                aria-label={t("share:link.urlLabel")}
                onFocus={(e) => e.currentTarget.select()}
              />
              <Button
                type="button"
                variant="secondary"
                size="sm"
                iconStart={copied ? <CheckIcon /> : <CopyIcon />}
                onClick={() => onCopy(link.token)}
              >
                {copied ? t("share:link.copied") : t("share:link.copy")}
              </Button>
            </div>
          </div>
          <p className={styles.expiry}>
            {t("share:link.expiresAt", { time: formatDateTime(link.expiresAt) })}
          </p>
        </>
      )}

      {link.hasQr && link.bankName ? (
        <dl className={styles.bank}>
          <dt className={styles.bankTerm}>{t("share:link.bank")}</dt>
          <dd className={styles.bankValue}>{link.bankName}</dd>
          {link.accountNumber ? (
            <>
              <dt className={styles.bankTerm}>{t("share:link.accountNumber")}</dt>
              <dd className={`${styles.bankValue} ${styles.bankNumber}`}>
                {groupAccount(link.accountNumber)}
              </dd>
            </>
          ) : null}
          {link.accountHolderName ? (
            <>
              <dt className={styles.bankTerm}>{t("share:link.holder")}</dt>
              <dd className={styles.bankValue}>{link.accountHolderName}</dd>
            </>
          ) : null}
        </dl>
      ) : (
        <p className={styles.noQrHint}>{t("share:link.noQrHint")}</p>
      )}

      {/* Inline-confirm actions (OQ3): a two-step "are you sure?" for both. */}
      {confirm === "revoke" ? (
        <div className={styles.confirm}>
          <p className={styles.confirmText}>{t("share:link.confirmRevoke")}</p>
          <div className={styles.actions}>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => onSetConfirm(null)}
            >
              {t("share:link.cancel")}
            </Button>
            <Button
              type="button"
              variant="danger"
              size="sm"
              loading={revoking}
              onClick={onRevoke}
            >
              {t("share:link.confirmRevokeConfirm")}
            </Button>
          </div>
        </div>
      ) : confirm === "regenerate" ? (
        <div className={styles.confirm}>
          <p className={styles.confirmText}>{t("share:link.confirmRegenerate")}</p>
          <div className={styles.actions}>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => onSetConfirm(null)}
            >
              {t("share:link.cancel")}
            </Button>
            <Button
              type="button"
              variant="primary"
              size="sm"
              loading={regenerating}
              onClick={onRegenerate}
            >
              {t("share:link.confirmRegenerateConfirm")}
            </Button>
          </div>
        </div>
      ) : (
        <div className={styles.actions}>
          <Button
            type="button"
            variant="danger"
            size="sm"
            onClick={() => onSetConfirm("revoke")}
          >
            {t("share:link.revoke")}
          </Button>
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={() => onSetConfirm("regenerate")}
          >
            {t("share:link.regenerate")}
          </Button>
        </div>
      )}
    </div>
  );
}
