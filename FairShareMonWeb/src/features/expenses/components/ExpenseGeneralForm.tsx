import { Controller } from "react-hook-form";
import type { Control, FieldErrors, UseFormRegister } from "react-hook-form";
import { FieldStack, Select, TagMultiSelect, TextField } from "@/components/ui";
import { useT } from "@/i18n/useT";
import type { MemberResponse } from "@/features/members/api/types";
import type { CategoryResponse } from "@/features/categories/api/types";
import type { TagResponse } from "@/features/tags/api/types";
import type { ExpenseGeneralValues } from "../schemas";
import {
  buildCategoryOptions,
  buildMemberOptions,
  makeRenderCategoryOption,
  makeRenderMemberOption,
} from "./pickerOptions";

export type ExpenseGeneralFormProps = {
  control: Control<ExpenseGeneralValues>;
  register: UseFormRegister<ExpenseGeneralValues>;
  errors: FieldErrors<ExpenseGeneralValues>;
  members: MemberResponse[];
  categories: CategoryResponse[];
  tags: TagResponse[];
  /** Focus the name field on mount (create page). */
  autoFocusName?: boolean;
};

/**
 * Shared general-info fields for the atomic-create page and the edit dialog: name,
 * description, expense time (native `datetime-local`, OQ10a), payer `Select`
 * (defaults to the owner-representative), category `Select` (defaults to the
 * default category), and the tag `TagMultiSelect`. Pickers render active-only
 * members/categories/tags (R2/R8). Presentational — the parent owns the RHF form.
 */
export function ExpenseGeneralForm({
  control,
  register,
  errors,
  members,
  categories,
  tags,
  autoFocusName,
}: ExpenseGeneralFormProps) {
  const { t } = useT();
  const renderMemberOption = makeRenderMemberOption(t);
  const renderCategoryOption = makeRenderCategoryOption(t);

  const activeMembers = members.filter((m) => !m.isDeleted);
  const activeCategories = categories.filter((c) => !c.isDeleted);
  const memberOptions = buildMemberOptions(activeMembers);
  const categoryOptions = buildCategoryOptions(activeCategories);
  const tagOptions = tags
    .filter((tag) => !tag.isDeleted)
    .map((tag) => ({ value: tag.uuid, label: tag.name }));

  return (
    <FieldStack>
      <TextField
        label={t("expenses:form.nameLabel")}
        placeholder={t("expenses:form.namePlaceholder")}
        autoComplete="off"
        autoFocus={autoFocusName}
        required
        maxLength={200}
        error={errors.name?.message}
        {...register("name")}
      />

      <TextField
        label={t("expenses:form.descriptionLabel")}
        placeholder={t("expenses:form.descriptionPlaceholder")}
        autoComplete="off"
        maxLength={1000}
        error={errors.description?.message}
        {...register("description")}
      />

      <TextField
        label={t("expenses:form.timeLabel")}
        type="datetime-local"
        required
        error={errors.expenseTime?.message}
        {...register("expenseTime")}
      />

      <Controller
        control={control}
        name="payerMemberUuid"
        render={({ field }) => (
          <Select
            label={t("expenses:form.payerLabel")}
            value={field.value || undefined}
            onValueChange={field.onChange}
            options={memberOptions}
            renderOption={renderMemberOption}
            placeholder={t("expenses:form.payerPlaceholder")}
            hint={t("expenses:form.payerDefaultHint")}
            error={errors.payerMemberUuid?.message}
          />
        )}
      />

      <Controller
        control={control}
        name="categoryUuid"
        render={({ field }) => (
          <Select
            label={t("expenses:form.categoryLabel")}
            value={field.value || undefined}
            onValueChange={field.onChange}
            options={categoryOptions}
            renderOption={renderCategoryOption}
            placeholder={t("expenses:form.categoryPlaceholder")}
            hint={t("expenses:form.categoryDefaultHint")}
            error={errors.categoryUuid?.message}
          />
        )}
      />

      <Controller
        control={control}
        name="tagUuids"
        render={({ field }) => (
          <TagMultiSelect
            label={t("expenses:form.tagsLabel")}
            value={field.value ?? []}
            onChange={field.onChange}
            options={tagOptions}
            placeholder={t("expenses:form.tagsPlaceholder")}
            toggleLabel={t("expenses:form.tagsToggle")}
            removeLabel={(label) =>
              t("expenses:form.tagsRemove", { name: label })
            }
            emptyLabel={t("expenses:form.tagsEmpty")}
            hint={t("expenses:form.tagsHint")}
            error={
              typeof errors.tagUuids?.message === "string"
                ? errors.tagUuids.message
                : undefined
            }
          />
        )}
      />
    </FieldStack>
  );
}
