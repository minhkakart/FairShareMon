import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useForm } from "react-hook-form";
import type { Control, UseFormRegister, FieldErrors } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  ErrorState,
  Form,
  FormActions,
  FormError,
  LimitNotice,
  PageHeader,
  Skeleton,
  Stack,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useToast } from "@/app/ToastHost";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import {
  applyFieldErrors,
  resolveErrorMessage,
} from "@/lib/api/http-error-handling";
import { useMembersQuery } from "@/features/members/hooks/useMembers";
import { useCategoriesQuery } from "@/features/categories/hooks/useCategories";
import { useTagsQuery } from "@/features/tags/hooks/useTags";
import type { MemberResponse } from "@/features/members/api/types";
import type { CategoryResponse } from "@/features/categories/api/types";
import type { TagResponse } from "@/features/tags/api/types";
import { createExpenseSchema } from "../schemas";
import type { CreateExpenseValues, ExpenseGeneralValues } from "../schemas";
import type { CreateExpenseRequest } from "../api/types";
import { useCreateExpense } from "../hooks/useExpenses";
import { dateTimeLocalToIso, nowDateTimeLocal } from "../dateTime";
import { ExpenseGeneralForm } from "../components/ExpenseGeneralForm";
import { ShareEditor } from "../components/ShareEditor";

const GENERAL_FIELDS = [
  "name",
  "description",
  "expenseTime",
  "payerMemberUuid",
  "categoryUuid",
  "tagUuids",
] as const;

type LoadedProps = {
  members: MemberResponse[];
  categories: CategoryResponse[];
  tags: TagResponse[];
};

function ExpenseCreateForm({ members, categories, tags }: LoadedProps) {
  const { t } = useT();
  const toast = useToast();
  const navigate = useNavigate();
  const createExpense = useCreateExpense();
  const [formError, setFormError] = useState<string | null>(null);
  const [limitReached, setLimitReached] = useState(false);

  const ownerRep = members.find((m) => m.isOwnerRepresentative);
  const defaultCategory = categories.find((c) => c.isDefault);

  const {
    register,
    control,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<CreateExpenseValues>({
    resolver: zodResolver(createExpenseSchema(t, ownerRep?.uuid)),
    defaultValues: {
      name: "",
      description: "",
      expenseTime: nowDateTimeLocal(),
      payerMemberUuid: ownerRep?.uuid ?? "",
      categoryUuid: defaultCategory?.uuid ?? "",
      tagUuids: [],
      shares: ownerRep
        ? [{ memberUuid: ownerRep.uuid, amount: 0, note: "" }]
        : [],
    },
  });

  const onSubmit = handleSubmit(async (values) => {
    setFormError(null);
    setLimitReached(false);
    const body: CreateExpenseRequest = {
      name: values.name.trim(),
      description: values.description?.trim() ? values.description.trim() : undefined,
      expenseTime: dateTimeLocalToIso(values.expenseTime),
      payerMemberUuid: values.payerMemberUuid || undefined,
      categoryUuid: values.categoryUuid || undefined,
      tagUuids: values.tagUuids,
      shares: values.shares.map((s) => ({
        memberUuid: s.memberUuid,
        amount: s.amount ?? 0,
        note: s.note?.trim() ? s.note.trim() : undefined,
      })),
    };
    try {
      const created = await createExpense.mutateAsync(body);
      toast.push({ tone: "success", title: t("expenses:toast.created") });
      void navigate(`/expenses/${created.uuid}`);
    } catch (error) {
      if (isApiError(error)) {
        if (error.code === ErrorCodes.MonthlyExpenseLimitReached) {
          setLimitReached(true);
          return;
        }
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
          error.code === ErrorCodes.ShareMemberInvalid ||
          error.code === ErrorCodes.DuplicateShareMember
        ) {
          setFormError(error.message);
          return;
        }
      }
      const formLevel = applyFieldErrors(
        error,
        GENERAL_FIELDS as unknown as string[],
        (field, message) =>
          setError(field as keyof CreateExpenseValues, { message }),
      );
      setFormError(formLevel[0] ?? resolveErrorMessage(error, t));
    }
  });

  return (
    <Form onSubmit={onSubmit} noValidate>
      {limitReached ? (
        <LimitNotice
          title={t("expenses:limit.title")}
          description={t("expenses:limit.body")}
        />
      ) : null}
      {formError ? <FormError>{formError}</FormError> : null}

      <Card>
        <CardHeader title={t("expenses:form.generalSection")} />
        <CardBody>
          <ExpenseGeneralForm
            control={control as unknown as Control<ExpenseGeneralValues>}
            register={register as unknown as UseFormRegister<ExpenseGeneralValues>}
            errors={errors as FieldErrors<ExpenseGeneralValues>}
            members={members}
            categories={categories}
            tags={tags}
            autoFocusName
          />
        </CardBody>
      </Card>

      <Card>
        <CardHeader title={t("expenses:shares.sectionTitle")} />
        <CardBody>
          <ShareEditor
            control={control}
            register={register}
            errors={errors}
            members={members}
            ownerRepUuid={ownerRep?.uuid}
          />
        </CardBody>
      </Card>

      <FormActions>
        <Button asChild variant="ghost">
          <Link to="/expenses">{t("expenses:form.cancel")}</Link>
        </Button>
        <Button type="submit" variant="primary" loading={isSubmitting}>
          {t("expenses:form.submitCreate")}
        </Button>
      </FormActions>
    </Form>
  );
}

/**
 * /expenses/new — the atomic create surface (OQ3a). A full page carrying the
 * general-info fields + the multi-row share editor; a single submit builds the
 * expense and its shares in one request (R5). Empty payer/category are omitted so
 * the backend applies its defaults. Waits for the members/categories/tags pickers
 * to load so the owner-rep + default-category defaults seed correctly.
 */
export function ExpenseCreatePage() {
  const { t } = useT();
  const membersQuery = useMembersQuery(false);
  const categoriesQuery = useCategoriesQuery(false);
  const tagsQuery = useTagsQuery(false);

  const isPending =
    membersQuery.isPending || categoriesQuery.isPending || tagsQuery.isPending;
  const isError =
    membersQuery.isError || categoriesQuery.isError || tagsQuery.isError;

  return (
    <Stack gap="6">
      <PageHeader
        title={t("expenses:form.createTitle")}
        description={t("expenses:form.createSubtitle")}
        actions={
          <Button asChild variant="ghost">
            <Link to="/expenses">{t("expenses:detail.back")}</Link>
          </Button>
        }
      />

      {isError ? (
        <ErrorState
          title={t("expenses:form.loadErrorTitle")}
          description={t("expenses:form.loadErrorBody")}
          action={
            <Button
              variant="secondary"
              onClick={() => {
                void membersQuery.refetch();
                void categoriesQuery.refetch();
                void tagsQuery.refetch();
              }}
            >
              {t("expenses:list.retry")}
            </Button>
          }
        />
      ) : isPending ? (
        <Card>
          <CardBody>
            <Stack gap="4">
              <Skeleton width="100%" height="2.5rem" />
              <Skeleton width="100%" height="2.5rem" />
              <Skeleton width="60%" height="2.5rem" />
            </Stack>
          </CardBody>
        </Card>
      ) : (
        <ExpenseCreateForm
          members={membersQuery.data ?? []}
          categories={categoriesQuery.data ?? []}
          tags={tagsQuery.data ?? []}
        />
      )}
    </Stack>
  );
}
