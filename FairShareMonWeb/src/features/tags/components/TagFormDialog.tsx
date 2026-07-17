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
  TextField,
} from "@/components/ui";
import { useToast } from "@/app/ToastHost";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import {
  applyFieldErrors,
  resolveErrorMessage,
} from "@/lib/api/http-error-handling";
import { tagFormSchema } from "../schemas";
import type { TagFormValues } from "../schemas";
import type { TagResponse } from "../api/types";
import { useCreateTag, useRenameTag } from "../hooks/useTags";
import styles from "./TagFormDialog.module.css";

export type TagFormDialogProps = {
  mode: "create" | "rename";
  /** The tag being renamed (required in "rename" mode). */
  tag?: TagResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

/**
 * Shared modal form for creating and renaming a tag (OQ5a). One TextField (name)
 * + a static reactivate hint (OQ3a — a name matching a deleted tag revives it).
 * RHF + Zod mirror the backend validator. On error it maps `5001` (name
 * duplicate) and `1001` `error.fields.name` onto the name field, toasts+closes a
 * stale `5000` on rename, and surfaces anything else form-level.
 */
export function TagFormDialog({
  mode,
  tag,
  open,
  onOpenChange,
}: TagFormDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const createTag = useCreateTag();
  const renameTag = useRenameTag();
  const [formError, setFormError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<TagFormValues>({
    resolver: zodResolver(tagFormSchema(t)),
    defaultValues: { name: mode === "rename" ? (tag?.name ?? "") : "" },
  });

  useEffect(() => {
    if (open) {
      reset({ name: mode === "rename" ? (tag?.name ?? "") : "" });
      setFormError(null);
    }
  }, [open, mode, tag, reset]);

  const onSubmit = handleSubmit(async (values) => {
    setFormError(null);
    try {
      if (mode === "create") {
        await createTag.mutateAsync({ name: values.name });
        toast.push({ tone: "success", title: t("tags:toast.created") });
      } else if (tag) {
        await renameTag.mutateAsync({
          uuid: tag.uuid,
          body: { name: values.name },
        });
        toast.push({ tone: "success", title: t("tags:toast.renamed") });
      }
      onOpenChange(false);
    } catch (error) {
      if (isApiError(error)) {
        // Active name collision — a field-level "name already exists" reading.
        if (error.code === ErrorCodes.TagNameDuplicate) {
          setError("name", { message: error.message });
          return;
        }
        // Renaming a tag deleted elsewhere: toast + close (stale list).
        if (error.code === ErrorCodes.TagNotFound) {
          toast.push({ tone: "danger", title: error.message });
          onOpenChange(false);
          return;
        }
      }
      const formLevel = applyFieldErrors(error, ["name"], (field, message) =>
        setError(field as keyof TagFormValues, { message }),
      );
      setFormError(formLevel[0] ?? resolveErrorMessage(error, t));
    }
  });

  const title =
    mode === "create" ? t("tags:form.createTitle") : t("tags:form.renameTitle");
  const submitLabel =
    mode === "create" ? t("tags:form.submitCreate") : t("tags:form.submitRename");

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent title={title} size="sm" closeLabel={t("tags:form.cancel")}>
        <Form onSubmit={onSubmit} noValidate>
          {formError ? <FormError>{formError}</FormError> : null}
          <FieldStack>
            <TextField
              label={t("tags:form.nameLabel")}
              placeholder={t("tags:form.namePlaceholder")}
              autoComplete="off"
              autoFocus
              required
              maxLength={100}
              error={errors.name?.message}
              {...register("name")}
            />
            <p className={styles.hint}>{t("tags:form.reactivateHint")}</p>
          </FieldStack>
          <DialogFooter>
            <DialogClose asChild>
              <Button type="button" variant="ghost">
                {t("tags:form.cancel")}
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
