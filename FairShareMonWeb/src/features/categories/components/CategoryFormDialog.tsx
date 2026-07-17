import { useEffect, useState } from "react";
import { Controller, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useT } from "@/i18n/useT";
import {
  Button,
  ColorPicker,
  CURATED_COLORS,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  FieldStack,
  Form,
  FormError,
  IconPicker,
  TextField,
} from "@/components/ui";
import { useToast } from "@/app/ToastHost";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import {
  applyFieldErrors,
  resolveErrorMessage,
} from "@/lib/api/http-error-handling";
import { categoryFormSchema } from "../schemas";
import type { CategoryFormValues } from "../schemas";
import type { CategoryResponse } from "../api/types";
import { useCreateCategory, useUpdateCategory } from "../hooks/useCategories";
import styles from "./CategoryFormDialog.module.css";

export type CategoryFormDialogProps = {
  mode: "create" | "edit";
  /** The category being edited (required in "edit" mode). */
  category?: CategoryResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

/** The seed color for a fresh create form (first curated swatch) — color is never empty. */
const DEFAULT_COLOR = CURATED_COLORS[0];

function toDefaults(
  mode: "create" | "edit",
  category?: CategoryResponse,
): CategoryFormValues {
  if (mode === "edit" && category) {
    return {
      name: category.name,
      color: category.color,
      icon: category.icon ?? null,
    };
  }
  return { name: "", color: DEFAULT_COLOR, icon: null };
}

/**
 * Shared modal form for creating and editing a category (OQ5a). Fields: name
 * (TextField), color (ColorPicker), icon (IconPicker), plus a static reactivate
 * hint (OQ3a). RHF + Zod mirror the backend validators. On error it maps `4001`
 * (name duplicate) and `1001` `error.fields.*` onto the fields, toasts+closes a
 * stale `4000` on edit, and surfaces anything else form-level.
 */
export function CategoryFormDialog({
  mode,
  category,
  open,
  onOpenChange,
}: CategoryFormDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const createCategory = useCreateCategory();
  const updateCategory = useUpdateCategory();
  const [formError, setFormError] = useState<string | null>(null);

  const {
    register,
    control,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<CategoryFormValues>({
    resolver: zodResolver(categoryFormSchema(t)),
    defaultValues: toDefaults(mode, category),
  });

  // Re-seed the fields + clear transient errors whenever the dialog (re)opens or
  // the target changes, so create starts fresh and edit pre-fills.
  useEffect(() => {
    if (open) {
      reset(toDefaults(mode, category));
      setFormError(null);
    }
  }, [open, mode, category, reset]);

  const onSubmit = handleSubmit(async (values) => {
    setFormError(null);
    const icon = values.icon?.trim() ? values.icon.trim() : null;
    const body = { name: values.name, color: values.color, icon };
    try {
      if (mode === "create") {
        await createCategory.mutateAsync(body);
        toast.push({ tone: "success", title: t("categories:toast.created") });
      } else if (category) {
        await updateCategory.mutateAsync({ uuid: category.uuid, body });
        toast.push({ tone: "success", title: t("categories:toast.updated") });
      }
      onOpenChange(false);
    } catch (error) {
      if (isApiError(error)) {
        // Active name collision — a field-level "name already exists" reading.
        if (error.code === ErrorCodes.CategoryNameDuplicate) {
          setError("name", { message: error.message });
          return;
        }
        // Editing a category deleted elsewhere: toast + close (stale list).
        if (error.code === ErrorCodes.CategoryNotFound) {
          toast.push({ tone: "danger", title: error.message });
          onOpenChange(false);
          return;
        }
      }
      const formLevel = applyFieldErrors(
        error,
        ["name", "color", "icon"],
        (field, message) =>
          setError(field as keyof CategoryFormValues, { message }),
      );
      setFormError(formLevel[0] ?? resolveErrorMessage(error, t));
    }
  });

  const title =
    mode === "create"
      ? t("categories:form.createTitle")
      : t("categories:form.editTitle");
  const submitLabel =
    mode === "create"
      ? t("categories:form.submitCreate")
      : t("categories:form.submitEdit");

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent title={title} size="md" closeLabel={t("categories:form.cancel")}>
        <Form onSubmit={onSubmit} noValidate>
          {formError ? <FormError>{formError}</FormError> : null}
          <FieldStack>
            <TextField
              label={t("categories:form.nameLabel")}
              placeholder={t("categories:form.namePlaceholder")}
              autoComplete="off"
              autoFocus
              required
              maxLength={100}
              error={errors.name?.message}
              {...register("name")}
            />
            <Controller
              control={control}
              name="color"
              render={({ field }) => (
                <ColorPicker
                  value={field.value}
                  onChange={field.onChange}
                  label={t("categories:form.colorLabel")}
                  hexLabel={t("categories:form.colorHexLabel")}
                  invalidHexMessage={t("categories:form.colorInvalidHex")}
                  error={errors.color?.message}
                  required
                />
              )}
            />
            <Controller
              control={control}
              name="icon"
              render={({ field }) => (
                <IconPicker
                  value={field.value ?? null}
                  onChange={field.onChange}
                  label={t("categories:form.iconLabel")}
                  noIconLabel={t("categories:form.iconNone")}
                  error={errors.icon?.message}
                />
              )}
            />
            <p className={styles.hint}>{t("categories:form.reactivateHint")}</p>
          </FieldStack>
          <DialogFooter>
            <DialogClose asChild>
              <Button type="button" variant="ghost">
                {t("categories:form.cancel")}
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
