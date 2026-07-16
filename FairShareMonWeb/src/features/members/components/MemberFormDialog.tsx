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
  LimitNotice,
  TextField,
} from "@/components/ui";
import { useToast } from "@/app/ToastHost";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import {
  applyFieldErrors,
  resolveErrorMessage,
} from "@/lib/api/http-error-handling";
import { memberFormSchema } from "../schemas";
import type { MemberFormValues } from "../schemas";
import type { MemberResponse } from "../api/types";
import { useCreateMember, useRenameMember } from "../hooks/useMembers";

export type MemberFormDialogProps = {
  mode: "create" | "rename";
  /** The member being renamed (required in "rename" mode). */
  member?: MemberResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

/**
 * Shared modal form for creating a member and renaming one (OQ5a). One
 * `TextField` (name), RHF + Zod mirroring the backend validator. On error it maps
 * `1001` onto the name field, renders a friendly `LimitNotice` for the Free
 * member-limit `13000` (create only — informational, no navigation, form stays
 * mounted), and toasts+closes a stale-`3000` rename.
 */
export function MemberFormDialog({
  mode,
  member,
  open,
  onOpenChange,
}: MemberFormDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const createMember = useCreateMember();
  const renameMember = useRenameMember();
  const [formError, setFormError] = useState<string | null>(null);
  const [limitMessage, setLimitMessage] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<MemberFormValues>({
    resolver: zodResolver(memberFormSchema(t)),
    defaultValues: { name: mode === "rename" ? (member?.name ?? "") : "" },
  });

  // Re-seed the field + clear transient errors whenever the dialog (re)opens or
  // the target member changes, so create starts empty and rename pre-fills.
  useEffect(() => {
    if (open) {
      reset({ name: mode === "rename" ? (member?.name ?? "") : "" });
      setFormError(null);
      setLimitMessage(null);
    }
  }, [open, mode, member, reset]);

  const onSubmit = handleSubmit(async (values) => {
    setFormError(null);
    setLimitMessage(null);
    try {
      if (mode === "create") {
        await createMember.mutateAsync({ name: values.name });
        toast.push({ tone: "success", title: t("members:toast.created") });
      } else if (member) {
        await renameMember.mutateAsync({
          uuid: member.uuid,
          body: { name: values.name },
        });
        toast.push({ tone: "success", title: t("members:toast.renamed") });
      }
      onOpenChange(false);
    } catch (error) {
      if (isApiError(error)) {
        // Free member-limit (create): informational LimitNotice, form stays open.
        if (error.code === ErrorCodes.MemberLimitReached) {
          setLimitMessage(error.message);
          return;
        }
        // Renaming a member deleted elsewhere: toast + close (stale list).
        if (error.code === ErrorCodes.MemberNotFound) {
          toast.push({ tone: "danger", title: error.message });
          onOpenChange(false);
          return;
        }
      }
      const formLevel = applyFieldErrors(error, ["name"], (field, message) =>
        setError(field as keyof MemberFormValues, { message }),
      );
      setFormError(formLevel[0] ?? resolveErrorMessage(error, t));
    }
  });

  const title =
    mode === "create"
      ? t("members:form.createTitle")
      : t("members:form.renameTitle");
  const submitLabel =
    mode === "create"
      ? t("members:form.submitCreate")
      : t("members:form.submitRename");

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent title={title} size="sm" closeLabel={t("members:form.cancel")}>
        <Form onSubmit={onSubmit} noValidate>
          {formError ? <FormError>{formError}</FormError> : null}
          {limitMessage ? (
            <LimitNotice
              title={t("members:limit.title")}
              description={limitMessage}
            />
          ) : null}
          <FieldStack>
            <TextField
              label={t("members:form.nameLabel")}
              placeholder={t("members:form.namePlaceholder")}
              autoComplete="off"
              autoFocus
              required
              maxLength={100}
              error={errors.name?.message}
              {...register("name")}
            />
          </FieldStack>
          <DialogFooter>
            <DialogClose asChild>
              <Button type="button" variant="ghost">
                {t("members:form.cancel")}
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
