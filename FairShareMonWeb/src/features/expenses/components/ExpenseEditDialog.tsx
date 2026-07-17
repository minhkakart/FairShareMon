import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  Form,
  FormError,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useToast } from "@/app/ToastHost";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import {
  applyFieldErrors,
  resolveErrorMessage,
} from "@/lib/api/http-error-handling";
import type { MemberResponse } from "@/features/members/api/types";
import type { CategoryResponse } from "@/features/categories/api/types";
import type { TagResponse } from "@/features/tags/api/types";
import { expenseGeneralSchema } from "../schemas";
import type { ExpenseGeneralValues } from "../schemas";
import type { ExpenseResponse, UpdateExpenseRequest } from "../api/types";
import { useUpdateExpense } from "../hooks/useExpenses";
import { dateTimeLocalToIso, isoToDateTimeLocal } from "../dateTime";
import { ExpenseGeneralForm } from "./ExpenseGeneralForm";

export type ExpenseEditDialogProps = {
  expense: ExpenseResponse;
  members: MemberResponse[];
  categories: CategoryResponse[];
  tags: TagResponse[];
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

const KNOWN_FIELDS = [
  "name",
  "description",
  "expenseTime",
  "payerMemberUuid",
  "categoryUuid",
  "tagUuids",
];

function toDefaults(expense: ExpenseResponse): ExpenseGeneralValues {
  return {
    name: expense.name,
    description: expense.description ?? "",
    expenseTime: isoToDateTimeLocal(expense.expenseTime),
    payerMemberUuid: expense.payer.uuid,
    categoryUuid: expense.category.uuid,
    tagUuids: expense.tags.map((tag) => tag.uuid),
  };
}

/**
 * Edit general info (B1) via a dialog on the detail page: name / description /
 * expense time / payer / category / tag set (full replace). Never touches shares.
 * Errors map `6001/6002/6003` and `1001` `error.fields.*` onto fields; a stale
 * `6000` and a closed-event `9001` toast + close.
 */
export function ExpenseEditDialog({
  expense,
  members,
  categories,
  tags,
  open,
  onOpenChange,
}: ExpenseEditDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const updateExpense = useUpdateExpense();
  const [formError, setFormError] = useState<string | null>(null);

  const {
    register,
    control,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<ExpenseGeneralValues>({
    resolver: zodResolver(expenseGeneralSchema(t)),
    defaultValues: toDefaults(expense),
  });

  useEffect(() => {
    if (open) {
      reset(toDefaults(expense));
      setFormError(null);
    }
  }, [open, expense, reset]);

  const onSubmit = handleSubmit(async (values) => {
    setFormError(null);
    const body: UpdateExpenseRequest = {
      name: values.name.trim(),
      description: values.description?.trim() ? values.description.trim() : undefined,
      expenseTime: dateTimeLocalToIso(values.expenseTime),
      payerMemberUuid: values.payerMemberUuid || undefined,
      categoryUuid: values.categoryUuid || undefined,
      tagUuids: values.tagUuids,
    };
    try {
      await updateExpense.mutateAsync({ uuid: expense.uuid, body });
      toast.push({ tone: "success", title: t("expenses:toast.updated") });
      onOpenChange(false);
    } catch (error) {
      if (isApiError(error)) {
        if (error.code === ErrorCodes.ExpensePayerInvalid) {
          setError("payerMemberUuid", { message: error.message });
          return;
        }
        if (error.code === ErrorCodes.ExpenseCategoryInvalid) {
          setError("categoryUuid", { message: error.message });
          return;
        }
        if (error.code === ErrorCodes.ExpenseTagInvalid) {
          setError("tagUuids", { message: error.message });
          return;
        }
        if (
          error.code === ErrorCodes.ExpenseNotFound ||
          error.code === ErrorCodes.EventClosed
        ) {
          toast.push({ tone: "danger", title: error.message });
          onOpenChange(false);
          return;
        }
      }
      const formLevel = applyFieldErrors(error, KNOWN_FIELDS, (field, message) =>
        setError(field as keyof ExpenseGeneralValues, { message }),
      );
      setFormError(formLevel[0] ?? resolveErrorMessage(error, t));
    }
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={t("expenses:form.editTitle")}
        size="lg"
        closeLabel={t("expenses:form.cancel")}
      >
        <Form onSubmit={onSubmit} noValidate>
          {formError ? <FormError>{formError}</FormError> : null}
          <ExpenseGeneralForm
            control={control}
            register={register}
            errors={errors}
            members={members}
            categories={categories}
            tags={tags}
          />
          <DialogFooter>
            <DialogClose asChild>
              <Button type="button" variant="ghost">
                {t("expenses:form.cancel")}
              </Button>
            </DialogClose>
            <Button type="submit" variant="primary" loading={isSubmitting}>
              {t("expenses:form.submitEdit")}
            </Button>
          </DialogFooter>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
