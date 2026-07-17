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
import type { TagResponse } from "../api/types";

export type TagsTableProps = {
  tags: TagResponse[];
  onRename: (tag: TagResponse) => void;
  onDelete: (tag: TagResponse) => void;
};

/**
 * Renders the tags list in backend order (name A→Z — never re-sorted
 * client-side). Normal rows expose Rename + Delete, each named for a11y. Deleted
 * rows are muted with a "Đã xóa" Badge and carry no actions (R7 — reactivation
 * happens implicitly via create).
 */
export function TagsTable({ tags, onRename, onDelete }: TagsTableProps) {
  const { t } = useT();

  return (
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
        {tags.map((tag) => (
          <TableRow key={tag.uuid} deleted={tag.isDeleted}>
            <TableHeaderCell scope="row">{tag.name}</TableHeaderCell>
            <TableCell>
              {tag.isDeleted ? (
                <Badge tone="neutral">{t("tags:badge.deleted")}</Badge>
              ) : null}
            </TableCell>
            {tag.isDeleted ? (
              // Deleted rows are read-only (reactivation happens via create).
              <TableCell actions />
            ) : (
              <TableCell actions>
                <Button
                  variant="ghost"
                  size="sm"
                  aria-label={t("tags:actions.renameNamed", { name: tag.name })}
                  onClick={() => onRename(tag)}
                >
                  {t("tags:actions.rename")}
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  aria-label={t("tags:actions.deleteNamed", { name: tag.name })}
                  onClick={() => onDelete(tag)}
                >
                  {t("tags:actions.delete")}
                </Button>
              </TableCell>
            )}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
