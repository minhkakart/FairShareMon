import { useEffect, useState } from "react";
import { Controller, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useT } from "@/i18n/useT";
import {
  Button,
  Combobox,
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
import type { ComboboxOption } from "@/components/ui";
import { useToast } from "@/app/ToastHost";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import {
  applyFieldErrors,
  resolveErrorMessage,
} from "@/lib/api/http-error-handling";
import { bankAccountFormSchema } from "../schemas";
import type { BankAccountFormValues } from "../schemas";
import type { BankAccountResponse } from "../api/types";
import type { VietqrBank } from "../api/vietqrDirectoryApi";
import {
  useCreateBankAccount,
  useUpdateBankAccount,
} from "../hooks/useBankAccounts";
import { useVietqrBanks } from "../hooks/useVietqrBanks";
import { buildBankOptions, makeRenderBankOption } from "./bankOptions";

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

  const banks = useVietqrBanks();
  const renderBankOption = makeRenderBankOption(t);

  const {
    register,
    handleSubmit,
    reset,
    setError,
    setValue,
    watch,
    control,
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
            <Controller
              name="bankBin"
              control={control}
              render={({ field, fieldState }) => {
                const baseOptions = buildBankOptions(banks.data ?? []);
                // Legacy/unknown BIN (edit of an account whose BIN isn't in the
                // directory): inject a synthetic option carrying the stored name
                // + BIN (no logo) so nothing is lost and it still pre-selects.
                const known =
                  !field.value ||
                  baseOptions.some((o) => o.value === field.value);
                const options: ComboboxOption<VietqrBank>[] = known
                  ? baseOptions
                  : [
                      {
                        value: field.value,
                        label: watch("bankName") || field.value,
                        keywords: [field.value],
                      },
                      ...baseOptions,
                    ];
                return (
                  <Combobox<VietqrBank>
                    label={t("wallet:form.bankPicker.label")}
                    placeholder={t("wallet:form.bankPicker.placeholder")}
                    searchPlaceholder={t(
                      "wallet:form.bankPicker.searchPlaceholder",
                    )}
                    emptyLabel={t("wallet:form.bankPicker.emptyLabel")}
                    loading={
                      banks.isFetching
                        ? t("wallet:form.bankPicker.loading")
                        : false
                    }
                    required
                    name={field.name}
                    ref={field.ref}
                    value={field.value || undefined}
                    options={options}
                    renderOption={renderBankOption}
                    onValueChange={(bin) => {
                      field.onChange(bin);
                      const bank = banks.data?.find((b) => b.bin === bin);
                      // Persist the picked short name into bankName (D3); for the
                      // synthetic legacy option keep the stored name.
                      setValue("bankName", bank ? bank.shortName : watch("bankName"), {
                        shouldValidate: true,
                      });
                    }}
                    error={fieldState.error?.message ?? errors.bankName?.message}
                  />
                );
              }}
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
