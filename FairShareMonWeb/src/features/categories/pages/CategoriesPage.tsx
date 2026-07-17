import { useId, useState } from "react";
import { useT } from "@/i18n/useT";
import {
  Button,
  EmptyState,
  ErrorState,
  PageHeader,
  Skeleton,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@/components/ui";
import { useToast } from "@/app/ToastHost";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import {
  useCategoriesQuery,
  useSetDefaultCategory,
} from "../hooks/useCategories";
import { CategoriesTable } from "../components/CategoriesTable";
import { CategoryFormDialog } from "../components/CategoryFormDialog";
import { DeleteCategoryDialog } from "../components/DeleteCategoryDialog";
import type { CategoryResponse } from "../api/types";
import styles from "./CategoriesPage.module.css";

const SKELETON_ROWS = 5;

function LoadingRows() {
  return (
    <>
      {Array.from({ length: SKELETON_ROWS }).map((_, index) => (
        <TableRow key={index}>
          <TableHeaderCell scope="row">
            <Skeleton width="12rem" />
          </TableHeaderCell>
          <TableCell>
            <Skeleton width="6rem" />
          </TableCell>
          <TableCell actions>
            <Skeleton width="8rem" />
          </TableCell>
        </TableRow>
      ))}
    </>
  );
}

/**
 * /categories — the caller's expense categories. Lists in backend order
 * (default-first, then A→Z), with a show-deleted toggle, create/edit/set-default/
 * delete via modal dialogs + row actions, the exactly-one-default marker, and the
 * not-deletable default guard (R1/R3/R6). Set-default is a safe atomic swap done
 * directly from the row action (no confirm).
 */
export function CategoriesPage() {
  const { t } = useT();
  const toast = useToast();
  const toggleId = useId();

  const [includeDeleted, setIncludeDeleted] = useState(false);
  const [formOpen, setFormOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<CategoryResponse | undefined>(
    undefined,
  );
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<CategoryResponse | null>(
    null,
  );

  const categoriesQuery = useCategoriesQuery(includeDeleted);
  const categories = categoriesQuery.data ?? [];
  const setDefault = useSetDefaultCategory();

  function openCreate() {
    setEditTarget(undefined);
    setFormOpen(true);
  }

  function openEdit(category: CategoryResponse) {
    setEditTarget(category);
    setFormOpen(true);
  }

  function openDelete(category: CategoryResponse) {
    setDeleteTarget(category);
    setDeleteOpen(true);
  }

  async function onSetDefault(category: CategoryResponse) {
    try {
      await setDefault.mutateAsync(category.uuid);
      toast.push({ tone: "success", title: t("categories:toast.defaultSet") });
    } catch (error) {
      // 4000 (stale — deleted elsewhere) surfaces the localized server message;
      // the invalidate-on-settled refetch reconciles the list.
      toast.push({ tone: "danger", title: resolveErrorMessage(error, t) });
    }
  }

  return (
    <Stack gap="6">
      <PageHeader
        title={t("categories:title")}
        description={t("categories:subtitle")}
        actions={
          <Button variant="primary" onClick={openCreate}>
            {t("categories:add")}
          </Button>
        }
      />

      <div className={styles.toolbar}>
        <label className={styles.toggle} htmlFor={toggleId}>
          <input
            id={toggleId}
            type="checkbox"
            checked={includeDeleted}
            onChange={(event) => setIncludeDeleted(event.target.checked)}
          />
          <span>{t("categories:showDeleted")}</span>
        </label>
      </div>

      {categoriesQuery.isError ? (
        <ErrorState
          title={t("categories:error.title")}
          description={resolveErrorMessage(categoriesQuery.error, t)}
          action={
            <Button
              variant="secondary"
              onClick={() => void categoriesQuery.refetch()}
            >
              {t("categories:error.retry")}
            </Button>
          }
        />
      ) : categoriesQuery.isPending ? (
        <Table caption={t("categories:table.caption")} captionHidden>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t("categories:table.category")}</TableHeaderCell>
              <TableHeaderCell>{t("categories:table.status")}</TableHeaderCell>
              <TableHeaderCell align="right">
                {t("categories:table.actions")}
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            <LoadingRows />
          </TableBody>
        </Table>
      ) : categories.length === 0 ? (
        <Table caption={t("categories:table.caption")} captionHidden>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t("categories:table.category")}</TableHeaderCell>
              <TableHeaderCell>{t("categories:table.status")}</TableHeaderCell>
              <TableHeaderCell align="right">
                {t("categories:table.actions")}
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            <TableEmpty colSpan={3}>
              <EmptyState
                title={t("categories:empty.title")}
                description={t("categories:empty.body")}
                action={
                  <Button variant="primary" onClick={openCreate}>
                    {t("categories:add")}
                  </Button>
                }
              />
            </TableEmpty>
          </TableBody>
        </Table>
      ) : (
        <CategoriesTable
          categories={categories}
          onEdit={openEdit}
          onSetDefault={(category) => void onSetDefault(category)}
          onDelete={openDelete}
        />
      )}

      <CategoryFormDialog
        mode={editTarget ? "edit" : "create"}
        category={editTarget}
        open={formOpen}
        onOpenChange={setFormOpen}
      />
      <DeleteCategoryDialog
        category={deleteTarget}
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
      />
    </Stack>
  );
}
