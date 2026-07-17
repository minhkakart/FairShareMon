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
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { useTagsQuery } from "../hooks/useTags";
import { TagsTable } from "../components/TagsTable";
import { TagFormDialog } from "../components/TagFormDialog";
import { DeleteTagDialog } from "../components/DeleteTagDialog";
import type { TagResponse } from "../api/types";
import styles from "./TagsPage.module.css";

const SKELETON_ROWS = 4;

function LoadingRows() {
  return (
    <>
      {Array.from({ length: SKELETON_ROWS }).map((_, index) => (
        <TableRow key={index}>
          <TableHeaderCell scope="row">
            <Skeleton width="10rem" />
          </TableHeaderCell>
          <TableCell>
            <Skeleton width="5rem" />
          </TableCell>
          <TableCell actions>
            <Skeleton width="6rem" />
          </TableCell>
        </TableRow>
      ))}
    </>
  );
}

/**
 * /tags — the caller's tags. Lists in backend order (name A→Z), with a
 * show-deleted toggle, create/rename/delete via modal dialogs, and the
 * reactivation-on-name-reuse hint surfaced in the create form (R6/R7). Unlike
 * categories, the tag list can be genuinely empty (nothing seeded).
 */
export function TagsPage() {
  const { t } = useT();
  const toggleId = useId();

  const [includeDeleted, setIncludeDeleted] = useState(false);
  const [formOpen, setFormOpen] = useState(false);
  const [renameTarget, setRenameTarget] = useState<TagResponse | undefined>(
    undefined,
  );
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<TagResponse | null>(null);

  const tagsQuery = useTagsQuery(includeDeleted);
  const tags = tagsQuery.data ?? [];

  function openCreate() {
    setRenameTarget(undefined);
    setFormOpen(true);
  }

  function openRename(tag: TagResponse) {
    setRenameTarget(tag);
    setFormOpen(true);
  }

  function openDelete(tag: TagResponse) {
    setDeleteTarget(tag);
    setDeleteOpen(true);
  }

  return (
    <Stack gap="6">
      <PageHeader
        title={t("tags:title")}
        description={t("tags:subtitle")}
        actions={
          <Button variant="primary" onClick={openCreate}>
            {t("tags:add")}
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
          <span>{t("tags:showDeleted")}</span>
        </label>
      </div>

      {tagsQuery.isError ? (
        <ErrorState
          title={t("tags:error.title")}
          description={resolveErrorMessage(tagsQuery.error, t)}
          action={
            <Button variant="secondary" onClick={() => void tagsQuery.refetch()}>
              {t("tags:error.retry")}
            </Button>
          }
        />
      ) : tagsQuery.isPending ? (
        <Table caption={t("tags:table.caption")} captionHidden>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t("tags:table.tag")}</TableHeaderCell>
              <TableHeaderCell>{t("tags:table.status")}</TableHeaderCell>
              <TableHeaderCell align="right">
                {t("tags:table.actions")}
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            <LoadingRows />
          </TableBody>
        </Table>
      ) : tags.length === 0 ? (
        <Table caption={t("tags:table.caption")} captionHidden>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t("tags:table.tag")}</TableHeaderCell>
              <TableHeaderCell>{t("tags:table.status")}</TableHeaderCell>
              <TableHeaderCell align="right">
                {t("tags:table.actions")}
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            <TableEmpty colSpan={3}>
              <EmptyState
                title={t("tags:empty.title")}
                description={t("tags:empty.body")}
                action={
                  <Button variant="primary" onClick={openCreate}>
                    {t("tags:add")}
                  </Button>
                }
              />
            </TableEmpty>
          </TableBody>
        </Table>
      ) : (
        <TagsTable tags={tags} onRename={openRename} onDelete={openDelete} />
      )}

      <TagFormDialog
        mode={renameTarget ? "rename" : "create"}
        tag={renameTarget}
        open={formOpen}
        onOpenChange={setFormOpen}
      />
      <DeleteTagDialog
        tag={deleteTarget}
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
      />
    </Stack>
  );
}
