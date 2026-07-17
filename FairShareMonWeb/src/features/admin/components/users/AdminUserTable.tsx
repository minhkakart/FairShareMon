import { Link } from "react-router-dom";
import {
  Button,
  EmptyState,
  Skeleton,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { formatCount, formatDate } from "@/i18n/format";
import type { AdminUserRow } from "../../api/types";
import { AdminTierBadge } from "../AdminTierBadge";
import { RoleBadge } from "../RoleBadge";
import { StatusBadge } from "../StatusBadge";
import { SortAscIcon, SortDescIcon, SortNoneIcon } from "../icons";
import styles from "../admin.module.css";

/** Backend-supported sort keys. */
export type SortKey = "createdAt" | "username" | "tier" | "status";
export type SortDirection = "asc" | "desc";

const COLUMN_COUNT = 8;
const SKELETON_ROWS = 5;

export interface AdminUserTableProps {
  rows: AdminUserRow[];
  sort: string;
  direction: SortDirection;
  onSort: (key: SortKey) => void;
  loading?: boolean;
}

/**
 * The user-admin table (metadata + grant summary ONLY — R10). Sortable headers
 * (createdAt default) toggle asc/desc through `onSort`; the active header carries
 * `aria-sort` and a direction glyph (never color alone). Each row links to the
 * user detail. Loading → skeleton rows; empty → an in-table `EmptyState`.
 */
export function AdminUserTable({
  rows,
  sort,
  direction,
  onSort,
  loading,
}: AdminUserTableProps) {
  const { t } = useT();

  return (
    <Table caption={t("admin:users.caption")}>
      <TableHead>
        <TableRow>
          <SortHeader
            column="username"
            label={t("admin:users.columns.username")}
            sort={sort}
            direction={direction}
            onSort={onSort}
          />
          <SortHeader
            column="tier"
            label={t("admin:users.columns.tier")}
            sort={sort}
            direction={direction}
            onSort={onSort}
          />
          <TableHeaderCell scope="col">
            {t("admin:users.columns.role")}
          </TableHeaderCell>
          <SortHeader
            column="status"
            label={t("admin:users.columns.status")}
            sort={sort}
            direction={direction}
            onSort={onSort}
          />
          <SortHeader
            column="createdAt"
            label={t("admin:users.columns.createdAt")}
            sort={sort}
            direction={direction}
            onSort={onSort}
          />
          <TableHeaderCell scope="col" numeric>
            {t("admin:users.columns.grantCount")}
          </TableHeaderCell>
          <TableHeaderCell scope="col">
            {t("admin:users.columns.lastGrantAt")}
          </TableHeaderCell>
          <TableHeaderCell scope="col">
            <span className={styles.srOnly}>
              {t("admin:users.columns.actions")}
            </span>
          </TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {loading ? (
          Array.from({ length: SKELETON_ROWS }).map((_, i) => (
            <TableRow key={i}>
              {Array.from({ length: COLUMN_COUNT }).map((__, j) => (
                <TableCell key={j}>
                  <Skeleton width="80%" />
                </TableCell>
              ))}
            </TableRow>
          ))
        ) : rows.length === 0 ? (
          <TableEmpty colSpan={COLUMN_COUNT}>
            <EmptyState
              title={t("admin:users.states.empty")}
              description={t("admin:users.states.emptyBody")}
            />
          </TableEmpty>
        ) : (
          rows.map((u) => (
            <TableRow key={u.uuid} deleted={u.status === "DISABLED"}>
              <TableHeaderCell scope="row">{u.username}</TableHeaderCell>
              <TableCell>
                <AdminTierBadge tier={u.tier} />
              </TableCell>
              <TableCell>
                <RoleBadge role={u.role} />
              </TableCell>
              <TableCell>
                <StatusBadge status={u.status} />
              </TableCell>
              <TableCell>
                <span className={styles.mono}>{formatDate(u.createdAt)}</span>
              </TableCell>
              <TableCell numeric>{formatCount(u.grantCount)}</TableCell>
              <TableCell>
                <span className={styles.mono}>
                  {u.lastGrantAt
                    ? formatDate(u.lastGrantAt)
                    : t("admin:users.none")}
                </span>
              </TableCell>
              <TableCell actions>
                <Button asChild variant="ghost" size="sm">
                  <Link
                    to={`/admin/users/${u.uuid}`}
                    aria-label={t("admin:users.viewLabel", { name: u.username })}
                  >
                    {t("admin:users.view")}
                  </Link>
                </Button>
              </TableCell>
            </TableRow>
          ))
        )}
      </TableBody>
    </Table>
  );
}

function SortHeader({
  column,
  label,
  sort,
  direction,
  onSort,
}: {
  column: SortKey;
  label: string;
  sort: string;
  direction: SortDirection;
  onSort: (key: SortKey) => void;
}) {
  const { t } = useT();
  const active = sort === column;
  const ariaSort = active
    ? direction === "asc"
      ? "ascending"
      : "descending"
    : "none";
  const nextLabelKey =
    active && direction === "asc"
      ? "admin:users.sortDescLabel"
      : "admin:users.sortAscLabel";

  return (
    <TableHeaderCell scope="col" aria-sort={ariaSort}>
      <button
        type="button"
        className={styles.sortButton}
        onClick={() => onSort(column)}
        aria-label={t(nextLabelKey, { column: label })}
      >
        {label}
        <span
          className={`${styles.sortIcon}${active ? ` ${styles.sortIconActive}` : ""}`}
        >
          {active ? (
            direction === "asc" ? (
              <SortAscIcon />
            ) : (
              <SortDescIcon />
            )
          ) : (
            <SortNoneIcon />
          )}
        </span>
      </button>
    </TableHeaderCell>
  );
}
