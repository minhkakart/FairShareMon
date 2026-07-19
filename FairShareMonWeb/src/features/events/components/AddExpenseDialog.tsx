import {
  Button,
  Dialog,
  DialogContent,
  ErrorState,
  Skeleton,
  Stack,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useToast } from "@/app/ToastHost";
import { useMembersQuery } from "@/features/members/hooks/useMembers";
import { useCategoriesQuery } from "@/features/categories/hooks/useCategories";
import { useTagsQuery } from "@/features/tags/hooks/useTags";
import { ExpenseCreateForm } from "@/features/expenses/components/ExpenseCreateForm";
import type { EventResponse } from "../api/types";

export type AddExpenseDialogProps = {
  /** Provides the uuid + name the expense is locked to. */
  event: EventResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

/**
 * The body runs the form-data queries + renders the create form. It is only
 * mounted while the dialog is open (Radix portals its content on open), so the
 * queries + RHF reset cleanly on every open.
 */
function AddExpenseDialogBody({
  event,
  onOpenChange,
}: {
  event: EventResponse;
  onOpenChange: (open: boolean) => void;
}) {
  const { t } = useT();
  const toast = useToast();
  const membersQuery = useMembersQuery(false);
  const categoriesQuery = useCategoriesQuery(false);
  const tagsQuery = useTagsQuery(false);

  const isPending =
    membersQuery.isPending || categoriesQuery.isPending || tagsQuery.isPending;
  const isError =
    membersQuery.isError || categoriesQuery.isError || tagsQuery.isError;

  if (isError) {
    return (
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
    );
  }

  if (isPending) {
    return (
      <Stack gap="4">
        <Skeleton width="100%" height="2.5rem" />
        <Skeleton width="100%" height="2.5rem" />
        <Skeleton width="60%" height="2.5rem" />
      </Stack>
    );
  }

  return (
    <ExpenseCreateForm
      variant="dialog"
      members={membersQuery.data ?? []}
      categories={categoriesQuery.data ?? []}
      tags={tagsQuery.data ?? []}
      lockedEventUuid={event.uuid}
      lockedEventName={event.name}
      onCreated={() => onOpenChange(false)}
      onEventUnavailable={(message) => {
        toast.push({ tone: "danger", title: message });
        onOpenChange(false);
      }}
    />
  );
}

/**
 * Feature 2 — the "Thêm phiếu" popup on an OPEN event's detail page. Holds the
 * shared create-expense form with the current event pre-selected and locked
 * (non-editable). On success the dialog closes, a success toast shows, and the
 * event detail (balance + expenses section + `expenseCount`) refreshes via
 * `useCreateExpense`'s conditional events invalidation. If the event has closed
 * or vanished since the dialog opened (`9001`/`9000`), it toasts + closes.
 */
export function AddExpenseDialog({
  event,
  open,
  onOpenChange,
}: AddExpenseDialogProps) {
  const { t } = useT();
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        size="lg"
        title={t("events:addExpense.title", { name: event.name })}
        closeLabel={t("expenses:form.cancel")}
      >
        <AddExpenseDialogBody event={event} onOpenChange={onOpenChange} />
      </DialogContent>
    </Dialog>
  );
}
