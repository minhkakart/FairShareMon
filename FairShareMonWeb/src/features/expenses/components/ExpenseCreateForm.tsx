import { useState } from "react";
import { Link } from "react-router-dom";
import { Controller, useForm } from "react-hook-form";
import type { Control, UseFormRegister, FieldErrors } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  DialogClose,
  DialogFooter,
  FieldStack,
  Form,
  FormActions,
  FormError,
  LimitNotice,
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
import type { EventSummaryResponse } from "@/features/events/api/types";
import { createExpenseSchema } from "../schemas";
import type { CreateExpenseValues, ExpenseGeneralValues } from "../schemas";
import type { CreateExpenseRequest, ExpenseResponse } from "../api/types";
import { useCreateExpense } from "../hooks/useExpenses";
import { dateTimeLocalToIso, nowDateTimeLocal } from "../dateTime";
import { ExpenseGeneralForm } from "./ExpenseGeneralForm";
import { ExpenseEventField } from "./ExpenseEventField";
import { ShareEditor } from "./ShareEditor";
import styles from "./ExpenseCreateForm.module.css";

const GENERAL_FIELDS = [
  "name",
  "description",
  "expenseTime",
  "payerMemberUuid",
  "categoryUuid",
  "tagUuids",
] as const;

export type ExpenseCreateFormProps = {
  members: MemberResponse[];
  categories: CategoryResponse[];
  tags: TagResponse[];
  /** OPEN events for the optional picker (Feature 1). Ignored when lockedEventUuid is set. */
  openEvents?: EventSummaryResponse[];
  /** Background-refresh flag → the picker shows a subtle loading hint (never blocks). */
  eventsLoading?: boolean;
  /**
   * The open-events query errored. When true we do NOT show the "no open events"
   * hint (which would be misleading — the user may well have open events); the
   * picker falls back to the loose-only option so loose creation still works.
   */
  eventsError?: boolean;
  /**
   * Feature 2: fix the expense to this event. The picker becomes a read-only
   * locked display and `eventUuid` is always submitted.
   */
  lockedEventUuid?: string;
  lockedEventName?: string;
  /** Layout + action-row variant (OQ2a). */
  variant: "page" | "dialog";
  /**
   * Post-success hook. The form always toasts `expenses:toast.created` +
   * invalidates. Page → navigate to the created expense; dialog → close.
   */
  onCreated: (expense: ExpenseResponse) => void;
  /**
   * 9000/9001 handler (dialog: toast danger + close). Absent → the codes surface
   * as a form-level `FormError` (create page).
   */
  onEventUnavailable?: (message: string) => void;
};

/**
 * The reusable atomic create-expense form (R9). Owns the RHF form, builds
 * `CreateExpenseRequest`, calls `useCreateExpense`, toasts on success, and maps
 * the backend error codes onto fields / notices. Rendered on `/expenses/new`
 * (`variant="page"`, Cards + `FormActions`) and inside `AddExpenseDialog`
 * (`variant="dialog"`, flat sections + `DialogFooter`). The shared
 * `ExpenseGeneralForm` stays event-unaware — the event control lives here.
 */
export function ExpenseCreateForm({
  members,
  categories,
  tags,
  openEvents,
  eventsLoading,
  eventsError,
  lockedEventUuid,
  lockedEventName,
  variant,
  onCreated,
  onEventUnavailable,
}: ExpenseCreateFormProps) {
  const { t } = useT();
  const toast = useToast();
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
      eventUuid: lockedEventUuid ?? "",
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
      eventUuid: values.eventUuid || undefined,
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
      onCreated(created);
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
        if (error.code === ErrorCodes.ExpenseTimeOutOfEventRange) {
          setError("expenseTime", { message: error.message });
          return;
        }
        if (
          error.code === ErrorCodes.EventClosed ||
          error.code === ErrorCodes.EventNotFound
        ) {
          if (onEventUnavailable) {
            onEventUnavailable(error.message);
          } else {
            setFormError(error.message);
          }
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

  const locked = Boolean(lockedEventUuid && lockedEventName);
  const noOpenEvents =
    !locked && !eventsLoading && !eventsError && (openEvents?.length ?? 0) === 0;

  const eventField = lockedEventUuid && lockedEventName ? (
    <ExpenseEventField lockedName={lockedEventName} />
  ) : noOpenEvents ? (
    <p className={styles.muted}>{t("expenses:expenseEvent.noOpenEvents")}</p>
  ) : (
    <Controller
      control={control}
      name="eventUuid"
      render={({ field }) => (
        <ExpenseEventField
          value={field.value}
          onChange={field.onChange}
          events={openEvents ?? []}
          loading={eventsLoading}
        />
      )}
    />
  );

  const generalSection = (
    <FieldStack>
      {eventField}
      <ExpenseGeneralForm
        control={control as unknown as Control<ExpenseGeneralValues>}
        register={register as unknown as UseFormRegister<ExpenseGeneralValues>}
        errors={errors as FieldErrors<ExpenseGeneralValues>}
        members={members}
        categories={categories}
        tags={tags}
        autoFocusName={variant === "page"}
      />
    </FieldStack>
  );

  const shareSection = (
    <ShareEditor
      control={control}
      register={register}
      errors={errors}
      members={members}
      ownerRepUuid={ownerRep?.uuid}
    />
  );

  const notices = (
    <>
      {limitReached ? (
        <LimitNotice
          title={t("expenses:limit.title")}
          description={t("expenses:limit.body")}
        />
      ) : null}
      {formError ? <FormError>{formError}</FormError> : null}
    </>
  );

  if (variant === "dialog") {
    return (
      <Form onSubmit={onSubmit} noValidate>
        {notices}
        <section className={styles.section}>
          <h3 className={styles.sectionHeading}>
            {t("expenses:form.generalSection")}
          </h3>
          {generalSection}
        </section>
        <section className={styles.section}>
          <h3 className={styles.sectionHeading}>
            {t("expenses:shares.sectionTitle")}
          </h3>
          {shareSection}
        </section>
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              {t("expenses:form.cancel")}
            </Button>
          </DialogClose>
          <Button type="submit" variant="primary" loading={isSubmitting}>
            {t("expenses:form.submitCreate")}
          </Button>
        </DialogFooter>
      </Form>
    );
  }

  return (
    <Form onSubmit={onSubmit} noValidate>
      {notices}
      <Card>
        <CardHeader title={t("expenses:form.generalSection")} />
        <CardBody>{generalSection}</CardBody>
      </Card>
      <Card>
        <CardHeader title={t("expenses:shares.sectionTitle")} />
        <CardBody>{shareSection}</CardBody>
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
