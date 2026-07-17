import { useT } from "@/i18n/useT";
import {
  Badge,
  Button,
  CategoryMarker,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@/components/ui";
import type { CategoryResponse } from "../api/types";
import styles from "./CategoriesTable.module.css";

export type CategoriesTableProps = {
  categories: CategoryResponse[];
  onEdit: (category: CategoryResponse) => void;
  onSetDefault: (category: CategoryResponse) => void;
  onDelete: (category: CategoryResponse) => void;
};

/** Filled star — the default-category badge glyph. */
const StarIcon = (
  <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
    <path d="M10 2l2.4 5 5.6.6-4.2 3.8 1.2 5.6L10 14.8 5 17l1.2-5.6L2 7.6 7.6 7z" />
  </svg>
);

/** Outline star — the set-default action glyph. */
const StarOutlineIcon = (
  <svg
    viewBox="0 0 20 20"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.5"
    aria-hidden="true"
  >
    <path
      d="M10 2.6l2.15 4.5 4.95.55-3.7 3.35 1.05 4.9L10 13.9 5.5 16.3l1.05-4.9-3.7-3.35 4.95-.55z"
      strokeLinejoin="round"
    />
  </svg>
);

/**
 * Renders the categories list in backend order (default-first, then A→Z — never
 * re-sorted client-side), each row showing a `CategoryMarker` (color + icon +
 * name). The default row shows a "Mặc định" Badge + a star pip, has an Edit
 * action but NO set-default and NO delete control, with a short not-deletable
 * note (R3/R6). Deleted rows are muted with a "Đã xóa" Badge and carry no actions
 * (R7). Normal rows expose Edit + Set default + Delete, each named for a11y.
 */
export function CategoriesTable({
  categories,
  onEdit,
  onSetDefault,
  onDelete,
}: CategoriesTableProps) {
  const { t } = useT();
  const defaultLabel = t("categories:badge.default");

  return (
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
        {categories.map((category) => (
          <TableRow key={category.uuid} deleted={category.isDeleted}>
            <TableHeaderCell scope="row">
              <CategoryMarker
                color={category.color}
                icon={category.icon}
                name={category.name}
                showLabel
                isDefault={category.isDefault && !category.isDeleted}
                defaultLabel={defaultLabel}
              />
            </TableHeaderCell>
            <TableCell>
              {category.isDeleted ? (
                <Badge tone="neutral">{t("categories:badge.deleted")}</Badge>
              ) : category.isDefault ? (
                <Badge tone="warning" icon={StarIcon}>
                  {defaultLabel}
                </Badge>
              ) : null}
            </TableCell>
            {category.isDeleted ? (
              // Deleted rows are read-only (reactivation happens via create).
              <TableCell actions />
            ) : category.isDefault ? (
              <TableCell actions>
                <span className={styles.note}>
                  {t("categories:default.notDeletable")}
                </span>
                <Button
                  variant="ghost"
                  size="sm"
                  aria-label={t("categories:actions.editNamed", {
                    name: category.name,
                  })}
                  onClick={() => onEdit(category)}
                >
                  {t("categories:actions.edit")}
                </Button>
              </TableCell>
            ) : (
              <TableCell actions>
                <Button
                  variant="ghost"
                  size="sm"
                  aria-label={t("categories:actions.editNamed", {
                    name: category.name,
                  })}
                  onClick={() => onEdit(category)}
                >
                  {t("categories:actions.edit")}
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  aria-label={t("categories:actions.setDefaultNamed", {
                    name: category.name,
                  })}
                  onClick={() => onSetDefault(category)}
                >
                  <span className={styles.setDefault}>
                    <span className={styles.setDefaultIcon} aria-hidden="true">
                      {StarOutlineIcon}
                    </span>
                    {t("categories:actions.setDefault")}
                  </span>
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  aria-label={t("categories:actions.deleteNamed", {
                    name: category.name,
                  })}
                  onClick={() => onDelete(category)}
                >
                  {t("categories:actions.delete")}
                </Button>
              </TableCell>
            )}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
