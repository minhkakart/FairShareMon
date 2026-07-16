import { useT } from "@/i18n/useT";
import {
  Badge,
  Button,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@/components/ui";
import type { MemberResponse } from "../api/types";
import styles from "./MembersTable.module.css";

export type MembersTableProps = {
  members: MemberResponse[];
  onRename: (member: MemberResponse) => void;
  onDelete: (member: MemberResponse) => void;
};

/**
 * Renders the members list in backend order (owner-rep first, then A→Z — never
 * re-sorted client-side). Owner-rep rows show the owner Badge and a rename
 * action but NO delete control, with a short explanation (R3). Deleted rows are
 * muted with a "Đã xóa" Badge and carry no actions (R4/OQ4a).
 */
export function MembersTable({ members, onRename, onDelete }: MembersTableProps) {
  const { t } = useT();

  return (
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
        {members.map((member) => (
          <TableRow key={member.uuid} deleted={member.isDeleted}>
            <TableHeaderCell scope="row">{member.name}</TableHeaderCell>
            <TableCell>
              {member.isDeleted ? (
                <Badge tone="neutral">{t("members:badge.deleted")}</Badge>
              ) : member.isOwnerRepresentative ? (
                <Badge tone="info">{t("members:badge.ownerRep")}</Badge>
              ) : null}
            </TableCell>
            {member.isDeleted ? (
              // Deleted rows are read-only (no reactivate endpoint exists).
              <TableCell actions />
            ) : member.isOwnerRepresentative ? (
              <TableCell actions>
                <span className={styles.notDeletable}>
                  {t("members:ownerRep.notDeletable")}
                </span>
                <Button
                  variant="ghost"
                  size="sm"
                  aria-label={t("members:actions.renameNamed", {
                    name: member.name,
                  })}
                  onClick={() => onRename(member)}
                >
                  {t("members:actions.rename")}
                </Button>
              </TableCell>
            ) : (
              <TableCell actions>
                <Button
                  variant="ghost"
                  size="sm"
                  aria-label={t("members:actions.renameNamed", {
                    name: member.name,
                  })}
                  onClick={() => onRename(member)}
                >
                  {t("members:actions.rename")}
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  aria-label={t("members:actions.deleteNamed", {
                    name: member.name,
                  })}
                  onClick={() => onDelete(member)}
                >
                  {t("members:actions.delete")}
                </Button>
              </TableCell>
            )}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
