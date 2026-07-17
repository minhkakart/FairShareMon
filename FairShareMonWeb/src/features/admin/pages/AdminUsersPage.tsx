import { useCallback } from "react";
import { useSearchParams } from "react-router-dom";
import { Button, ErrorState, Pagination, Stack } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import type { AdminUserListRequest, Role, Status, Tier } from "../api/types";
import { useAdminUsersQuery } from "../hooks/useAdminUsers";
import { AdminUserFilters } from "../components/users/AdminUserFilters";
import { AdminUserTable } from "../components/users/AdminUserTable";
import type { SortDirection, SortKey } from "../components/users/AdminUserTable";
import styles from "../components/admin.module.css";

const PAGE_SIZE = 20;
const DEFAULT_SORT: SortKey = "createdAt";
const DEFAULT_DIR: SortDirection = "desc";

/**
 * /admin/users — the paged/filterable/sortable user list. All list state lives in
 * the URL (OQ5a: `?tier=&status=&role=&search=&page=&sort=&dir=`) so a filtered
 * view deep-links, refreshes, and works with the back button. Metadata + grant
 * summary only (R10).
 */
export function AdminUsersPage() {
  const { t } = useT();
  const [params, setParams] = useSearchParams();

  const tier = (params.get("tier") as Tier | null) ?? undefined;
  const status = (params.get("status") as Status | null) ?? undefined;
  const role = (params.get("role") as Role | null) ?? undefined;
  const search = params.get("search") ?? "";
  const page = Math.max(1, Number(params.get("page")) || 1);
  const sort = params.get("sort") ?? DEFAULT_SORT;
  const direction = (params.get("dir") as SortDirection | null) ?? DEFAULT_DIR;

  const request: AdminUserListRequest = {
    tier,
    status,
    role,
    search: search || undefined,
    page,
    pageSize: PAGE_SIZE,
    sort,
    direction,
  };

  const query = useAdminUsersQuery(request);

  // Patch the URL search params; resetPage drops the page (a filter/sort change
  // returns to page 1). Uses the functional form so rapid updates compose.
  const patch = useCallback(
    (next: Record<string, string | undefined>, resetPage: boolean) => {
      setParams(
        (prev) => {
          const updated = new URLSearchParams(prev);
          for (const [key, value] of Object.entries(next)) {
            if (value == null || value === "") updated.delete(key);
            else updated.set(key, value);
          }
          if (resetPage) updated.delete("page");
          return updated;
        },
        { replace: false },
      );
    },
    [setParams],
  );

  const onSort = (key: SortKey) => {
    const nextDir: SortDirection =
      sort === key && direction === "asc" ? "desc" : "asc";
    patch({ sort: key, dir: nextDir }, true);
  };

  const data = query.data;

  return (
    <Stack gap="4">
      <AdminUserFilters
        tier={tier}
        status={status}
        role={role}
        search={search}
        onTierChange={(v) => patch({ tier: v }, true)}
        onStatusChange={(v) => patch({ status: v }, true)}
        onRoleChange={(v) => patch({ role: v }, true)}
        onSearchChange={(v) => patch({ search: v }, true)}
      />

      {query.isError ? (
        <ErrorState
          title={t("admin:users.states.error")}
          description={resolveErrorMessage(query.error, t)}
          action={
            <Button variant="secondary" onClick={() => void query.refetch()}>
              {t("admin:users.states.retry")}
            </Button>
          }
        />
      ) : (
        <div className={styles.listStack}>
          <AdminUserTable
            rows={data?.items ?? []}
            sort={sort}
            direction={direction}
            onSort={onSort}
            loading={query.isPending}
          />
          {data ? (
            <Pagination
              page={data.page}
              pageCount={data.totalPages}
              onPageChange={(p) => patch({ page: String(p) }, false)}
              disabled={query.isFetching}
              label={t("admin:users.pagination.label")}
              prevLabel={t("admin:users.pagination.prev")}
              nextLabel={t("admin:users.pagination.next")}
              pageInfo={(p, n) =>
                t("admin:users.pagination.info", { page: p, pageCount: n })
              }
              pageLabel={(n) => t("admin:users.pagination.page", { page: n })}
            />
          ) : null}
        </div>
      )}
    </Stack>
  );
}
