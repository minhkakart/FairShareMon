import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
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
  TextField,
} from "@/components/ui";
import { useToast } from "@/app/ToastHost";
import { useT } from "@/i18n/useT";
import {
  applyFieldErrors,
  resolveErrorMessage,
} from "@/lib/api/http-error-handling";
import { revokeTierSchema } from "../../schemas";
import type { RevokeTierFormValues } from "../../schemas";
import { useRevokeTier } from "../../hooks/useAdminUsers";
import type { UserActionDialogProps } from "./DisableUserDialog";

/**
 * Revoke Premium (danger-lite confirm + optional note). Records a REVOKE row (0
 * amount, no revenue). Success toasts + closes; the hook invalidates users +
 * detail + dashboards.
 */
export function TierRevokeDialog({
  user,
  open,
  onOpenChange,
}: UserActionDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const revoke = useRevokeTier(user.uuid);
  const [formError, setFormError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<RevokeTierFormValues>({
    resolver: zodResolver(revokeTierSchema(t)),
    defaultValues: { note: "" },
  });

  useEffect(() => {
    if (open) {
      reset({ note: "" });
      setFormError(null);
    }
  }, [open, reset]);

  const onSubmit = handleSubmit(async (values) => {
    setFormError(null);
    try {
      await revoke.mutateAsync({ note: values.note || undefined });
      toast.push({
        tone: "success",
        title: t("admin:actions.revoke.toast", { name: user.username }),
      });
      onOpenChange(false);
    } catch (error) {
      const formLevel = applyFieldErrors(error, ["note"], (field, message) =>
        setError(field as keyof RevokeTierFormValues, { message }),
      );
      setFormError(formLevel[0] ?? resolveErrorMessage(error, t));
    }
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        tone="danger"
        title={t("admin:actions.revoke.title")}
        description={t("admin:actions.revoke.body")}
        closeLabel={t("admin:actions.cancel")}
      >
        <Form onSubmit={onSubmit} noValidate>
          {formError ? <FormError>{formError}</FormError> : null}
          <FieldStack>
            <TextField
              label={t("admin:actions.revoke.noteLabel")}
              placeholder={t("admin:actions.revoke.notePlaceholder")}
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
            <Button type="submit" variant="danger" loading={isSubmitting}>
              {t("admin:actions.revoke.submit")}
            </Button>
          </DialogFooter>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
