import { Link, useNavigate } from "react-router-dom";
import {
  Button,
  Card,
  CardBody,
  ErrorState,
  PageHeader,
  Skeleton,
  Stack,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useMembersQuery } from "@/features/members/hooks/useMembers";
import { useCategoriesQuery } from "@/features/categories/hooks/useCategories";
import { useTagsQuery } from "@/features/tags/hooks/useTags";
import { useEventsQuery } from "@/features/events/hooks/useEvents";
import { ExpenseCreateForm } from "../components/ExpenseCreateForm";

/**
 * /expenses/new — the atomic create surface (OQ3a). A full page carrying an
 * optional OPEN-event picker + the general-info fields + the multi-row share
 * editor; a single submit builds the expense and its shares in one request.
 * Waits for the members/categories/tags pickers to load so the owner-rep +
 * default-category defaults seed correctly; the OPEN-events query is optional
 * (R3) and never blocks the form — the event picker degrades gracefully.
 */
export function ExpenseCreatePage() {
  const { t } = useT();
  const navigate = useNavigate();
  const membersQuery = useMembersQuery(false);
  const categoriesQuery = useCategoriesQuery(false);
  const tagsQuery = useTagsQuery(false);
  const eventsQuery = useEventsQuery({ closed: false });

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
          variant="page"
          members={membersQuery.data ?? []}
          categories={categoriesQuery.data ?? []}
          tags={tagsQuery.data ?? []}
          openEvents={eventsQuery.data ?? []}
          eventsLoading={eventsQuery.isPending}
          eventsError={eventsQuery.isError}
          onCreated={(expense) => void navigate(`/expenses/${expense.uuid}`)}
        />
      )}
    </Stack>
  );
}
