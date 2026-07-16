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
import { useCurrentUser } from "@/features/auth/hooks/useAuth";
import { useMembersQuery } from "../hooks/useMembers";
import { MembersTable } from "../components/MembersTable";
import { MemberFormDialog } from "../components/MemberFormDialog";
import { DeleteMemberDialog } from "../components/DeleteMemberDialog";
import type { MemberResponse } from "../api/types";
import styles from "./MembersPage.module.css";

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
 * /members — the caller's members. Lists in backend order (owner-rep first, then
 * A→Z), with a show-deleted toggle, create/rename/delete via modal dialogs, and
 * owner-rep protection (no delete control). The Free member-limit (13000) is
 * surfaced reactively inside the create dialog (OQ3a).
 */
export function MembersPage() {
  const { t } = useT();
  const user = useCurrentUser();
  const isFreeTier = (user?.tier ?? "").toUpperCase() === "FREE";
  const toggleId = useId();

  const [includeDeleted, setIncludeDeleted] = useState(false);
  const [formOpen, setFormOpen] = useState(false);
  const [renameTarget, setRenameTarget] = useState<MemberResponse | undefined>(
    undefined,
  );
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<MemberResponse | null>(null);

  const membersQuery = useMembersQuery(includeDeleted);
  const members = membersQuery.data ?? [];
  const activeCount = members.filter((m) => !m.isDeleted).length;

  function openCreate() {
    setRenameTarget(undefined);
    setFormOpen(true);
  }

  function openRename(member: MemberResponse) {
    setRenameTarget(member);
    setFormOpen(true);
  }

  function openDelete(member: MemberResponse) {
    setDeleteTarget(member);
    setDeleteOpen(true);
  }

  return (
    <Stack gap="6">
      <PageHeader
        title={t("members:title")}
        description={t("members:subtitle")}
        actions={
          <Button variant="primary" onClick={openCreate}>
            {t("members:add")}
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
          <span>{t("members:showDeleted")}</span>
        </label>
        {isFreeTier && membersQuery.isSuccess ? (
          <span className={styles.count}>
            {t("members:activeCount", { count: activeCount })}
          </span>
        ) : null}
      </div>

      {membersQuery.isError ? (
        <ErrorState
          title={t("members:error.title")}
          description={resolveErrorMessage(membersQuery.error, t)}
          action={
            <Button
              variant="secondary"
              onClick={() => void membersQuery.refetch()}
            >
              {t("members:error.retry")}
            </Button>
          }
        />
      ) : membersQuery.isPending ? (
        <Table caption={t("members:table.caption")} captionHidden>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t("members:table.name")}</TableHeaderCell>
              <TableHeaderCell>{t("members:table.status")}</TableHeaderCell>
              <TableHeaderCell align="right">
                {t("members:table.actions")}
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            <LoadingRows />
          </TableBody>
        </Table>
      ) : members.length === 0 ? (
        <Table caption={t("members:table.caption")} captionHidden>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t("members:table.name")}</TableHeaderCell>
              <TableHeaderCell>{t("members:table.status")}</TableHeaderCell>
              <TableHeaderCell align="right">
                {t("members:table.actions")}
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            <TableEmpty colSpan={3}>
              <EmptyState
                title={t("members:empty.title")}
                description={t("members:empty.body")}
                action={
                  <Button variant="primary" onClick={openCreate}>
                    {t("members:add")}
                  </Button>
                }
              />
            </TableEmpty>
          </TableBody>
        </Table>
      ) : (
        <MembersTable
          members={members}
          onRename={openRename}
          onDelete={openDelete}
        />
      )}

      <MemberFormDialog
        mode={renameTarget ? "rename" : "create"}
        member={renameTarget}
        open={formOpen}
        onOpenChange={setFormOpen}
      />
      <DeleteMemberDialog
        member={deleteTarget}
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
      />
    </Stack>
  );
}
