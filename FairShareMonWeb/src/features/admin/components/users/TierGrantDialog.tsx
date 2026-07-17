import { useEffect, useState } from "react";
import { Controller, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  FieldStack,
  Form,
  FormError,
  MoneyInput,
  TextField,
} from "@/components/ui";
import { useToast } from "@/app/ToastHost";
import { useT } from "@/i18n/useT";
import {
  applyFieldErrors,
  resolveErrorMessage,
} from "@/lib/api/http-error-handling";
import { grantTierSchema } from "../../schemas";
import type { GrantTierFormValues } from "../../schemas";
import { useGrantTier } from "../../hooks/useAdminUsers";
import type { UserActionDialogProps } from "./DisableUserDialog";

/**
 * Grant Premium (records amount + reference/note). RHF + `grantTierSchema`
 * (mirrors `GrantTierRequestValidator`): amount ≥ 0 via `MoneyInput` (0 = free
 * grant). `1001` field errors map onto fields; success toasts + closes; the hook
 * invalidates users + detail + dashboards.
 */
export function TierGrantDialog({
  user,
  open,
  onOpenChange,
}: UserActionDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const grant = useGrantTier(user.uuid);
  const [formError, setFormError] = useState<string | null>(null);

  const {
    control,
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<GrantTierFormValues>({
    resolver: zodResolver(grantTierSchema(t)),
    defaultValues: { amount: null, reference: "", note: "" },
  });

  useEffect(() => {
    if (open) {
      reset({ amount: null, reference: "", note: "" });
      setFormError(null);
    }
  }, [open, reset]);

  const onSubmit = handleSubmit(async (values) => {
    setFormError(null);
    try {
      await grant.mutateAsync({
        amount: values.amount ?? 0,
        reference: values.reference || undefined,
        note: values.note || undefined,
      });
      toast.push({
        tone: "success",
        title: t("admin:actions.grant.toast", { name: user.username }),
      });
      onOpenChange(false);
    } catch (error) {
      const formLevel = applyFieldErrors(
        error,
        ["amount", "reference", "note", "currency"],
        (field, message) =>
          setError(field as keyof GrantTierFormValues, { message }),
      );
      setFormError(formLevel[0] ?? resolveErrorMessage(error, t));
    }
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={t("admin:actions.grant.title")}
        description={t("admin:actions.grant.body")}
        closeLabel={t("admin:actions.cancel")}
      >
        <Form onSubmit={onSubmit} noValidate>
          {formError ? <FormError>{formError}</FormError> : null}
          <FieldStack>
            <Controller
              control={control}
              name="amount"
              render={({ field }) => (
                <MoneyInput
                  label={t("admin:actions.grant.amountLabel")}
                  hint={t("admin:actions.grant.amountHint")}
                  value={field.value ?? null}
                  onChange={field.onChange}
                  required
                  error={errors.amount?.message}
                />
              )}
            />
            <TextField
              label={t("admin:actions.grant.referenceLabel")}
              placeholder={t("admin:actions.grant.referencePlaceholder")}
              autoComplete="off"
              maxLength={255}
              error={errors.reference?.message}
              {...register("reference")}
            />
            <TextField
              label={t("admin:actions.grant.noteLabel")}
              placeholder={t("admin:actions.grant.notePlaceholder")}
              autoComplete="off"
              maxLength={500}
              error={errors.note?.message}
              {...register("note")}
            />
          </FieldStack>
          <DialogFooter>
            <DialogClose asChild>
              <Button type="button" variant="ghost">
                {t("admin:actions.cancel")}
              </Button>
            </DialogClose>
            <Button type="submit" variant="primary" loading={isSubmitting}>
              {t("admin:actions.grant.submit")}
            </Button>
          </DialogFooter>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
