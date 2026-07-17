import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useT } from "@/i18n/useT";
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  FieldStack,
  Form,
  FormError,
  UpgradePrompt,
  TextField,
} from "@/components/ui";
import { useToast } from "@/app/ToastHost";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import {
  applyFieldErrors,
  resolveErrorMessage,
} from "@/lib/api/http-error-handling";
import { bankAccountFormSchema } from "../schemas";
import type { BankAccountFormValues } from "../schemas";
import type { BankAccountResponse } from "../api/types";
import {
  useCreateBankAccount,
  useUpdateBankAccount,
} from "../hooks/useBankAccounts";

export type BankAccountFormDialogProps = {
  mode: "create" | "edit";
  /** The account being edited (required in "edit" mode). */
  account?: BankAccountResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

const FIELD_NAMES = [
  "bankName",
  "bankBin",
  "accountNumber",
  "accountHolderName",
] as const;

const EMPTY: BankAccountFormValues = {
  bankName: "",
  bankBin: "",
  accountNumber: "",
  accountHolderName: "",
};

/**
 * Shared modal form for creating + editing a bank account. RHF + Zod mirror the
 * backend validators (BIN `^\d{6}$`, account `^\d{6,19}$`, names ≤100). On error
 * it maps `1001` onto the fields, renders an inline `<UpgradePrompt variant="cta">`
 * for a stale-tier Premium gate `13003` (form stays open), and toasts+closes a
 * stale-`12000` edit. Create + edit are both Premium mutations.
 */
export function BankAccountFormDialog({
  mode,
  account,
  open,
  onOpenChange,
}: BankAccountFormDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const createAccount = useCreateBankAccount();
  const updateAccount = useUpdateBankAccount();
  const [formError, setFormError] = useState<string | null>(null);
  const [gateMessage, setGateMessage] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<BankAccountFormValues>({
    resolver: zodResolver(bankAccountFormSchema(t)),
    defaultValues: EMPTY,
  });

  // Re-seed the fields + clear transient errors whenever the dialog (re)opens or
  // the target account changes — create starts empty, edit pre-fills.
  useEffect(() => {
    if (open) {
      reset(
        mode === "edit" && account
          ? {
              bankName: account.bankName,
              bankBin: account.bankBin,
              accountNumber: account.accountNumber,
              accountHolderName: account.accountHolderName,
            }
          : EMPTY,
      );
      setFormError(null);
      setGateMessage(null);
    }
  }, [open, mode, account, reset]);

  const onSubmit = handleSubmit(async (values) => {
    setFormError(null);
    setGateMessage(null);
    try {
      if (mode === "create") {
        await createAccount.mutateAsync(values);
        toast.push({ tone: "success", title: t("wallet:toast.created") });
      } else if (account) {
        await updateAccount.mutateAsync({ uuid: account.uuid, body: values });
        toast.push({ tone: "success", title: t("wallet:toast.updated") });
      }
      onOpenChange(false);
    } catch (error) {
      if (isApiError(error)) {
        // Stale-tier Premium gate: inline UpgradePrompt, form stays open.
        if (error.code === ErrorCodes.PremiumFeatureRequired) {
          setGateMessage(error.message);
          return;
        }
        // Editing an account deleted elsewhere: toast + close (stale list).
        if (error.code === ErrorCodes.BankAccountNotFound) {
          toast.push({ tone: "danger", title: error.message });
          onOpenChange(false);
          return;
        }
      }
      const formLevel = applyFieldErrors(error, FIELD_NAMES, (field, message) =>
        setError(field as keyof BankAccountFormValues, { message }),
      );
      setFormError(formLevel[0] ?? resolveErrorMessage(error, t));
    }
  });

  const title =
    mode === "create"
      ? t("wallet:form.createTitle")
      : t("wallet:form.editTitle");
  const submitLabel =
    mode === "create"
      ? t("wallet:form.submitCreate")
      : t("wallet:form.submitEdit");

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent title={title} size="sm" closeLabel={t("wallet:form.cancel")}>
        <Form onSubmit={onSubmit} noValidate>
          {formError ? <FormError>{formError}</FormError> : null}
          {gateMessage ? (
            <UpgradePrompt
              variant="cta"
              title={t("wallet:premium.gateTitle")}
              description={gateMessage}
            />
          ) : null}
          <FieldStack>
            <TextField
              label={t("wallet:form.bankNameLabel")}
              placeholder={t("wallet:form.bankNamePlaceholder")}
              autoComplete="off"
              autoFocus
              required
              maxLength={100}
              error={errors.bankName?.message}
              {...register("bankName")}
            />
            <TextField
              label={t("wallet:form.binLabel")}
              placeholder={t("wallet:form.binPlaceholder")}
              hint={t("wallet:form.binHint")}
              inputMode="numeric"
              autoComplete="off"
              required
              maxLength={6}
              error={errors.bankBin?.message}
              {...register("bankBin")}
            />
            <TextField
              label={t("wallet:form.accountNumberLabel")}
              placeholder={t("wallet:form.accountNumberPlaceholder")}
              hint={t("wallet:form.accountNumberHint")}
              inputMode="numeric"
              autoComplete="off"
              required
              maxLength={19}
              error={errors.accountNumber?.message}
              {...register("accountNumber")}
            />
            <TextField
              label={t("wallet:form.holderLabel")}
              placeholder={t("wallet:form.holderPlaceholder")}
              autoComplete="off"
              required
              maxLength={100}
              error={errors.accountHolderName?.message}
              {...register("accountHolderName")}
            />
          </FieldStack>
          <DialogFooter>
            <DialogClose asChild>
              <Button type="button" variant="ghost">
                {t("wallet:form.cancel")}
              </Button>
            </DialogClose>
            <Button type="submit" variant="primary" loading={isSubmitting}>
              {submitLabel}
            </Button>
          </DialogFooter>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
