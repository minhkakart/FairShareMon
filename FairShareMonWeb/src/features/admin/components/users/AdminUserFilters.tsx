import { useEffect, useState } from "react";
import { Select, TextField } from "@/components/ui";
import { useT } from "@/i18n/useT";
import type { Role, Status, Tier } from "../../api/types";
import styles from "../admin.module.css";

export interface AdminUserFiltersProps {
  tier?: Tier;
  status?: Status;
  role?: Role;
  search: string;
  onTierChange: (value?: Tier) => void;
  onStatusChange: (value?: Status) => void;
  onRoleChange: (value?: Role) => void;
  onSearchChange: (value: string) => void;
}

const ALL = "all";

/**
 * The user-admin filter bar (URL-synced by the page, OQ5a): tier/status/role
 * `Select`s + a debounced username search. Selecting "all" clears that filter.
 * The search field keeps a local value (seeded from the URL) and pushes to the
 * page after a short debounce so typing doesn't refetch on every keystroke.
 */
export function AdminUserFilters({
  tier,
  status,
  role,
  search,
  onTierChange,
  onStatusChange,
  onRoleChange,
  onSearchChange,
}: AdminUserFiltersProps) {
  const { t } = useT();
  const [searchInput, setSearchInput] = useState(search);

  // Keep the local input in sync when the URL search changes externally
  // (back/forward, deep-link).
  useEffect(() => {
    setSearchInput(search);
  }, [search]);

  // Debounce pushing the typed value up to the URL owner.
  useEffect(() => {
    if (searchInput === search) return;
    const id = window.setTimeout(() => onSearchChange(searchInput), 300);
    return () => window.clearTimeout(id);
  }, [searchInput, search, onSearchChange]);

  return (
    <div className={styles.filterBar}>
      <div className={styles.filterField}>
        <Select
          label={t("admin:users.filters.tier")}
          value={tier ?? ALL}
          onValueChange={(v) => onTierChange(v === ALL ? undefined : (v as Tier))}
          options={[
            { value: ALL, label: t("admin:users.filters.allTier") },
            { value: "FREE", label: t("admin:tierBadge.free") },
            { value: "PREMIUM", label: t("admin:tierBadge.premium") },
          ]}
        />
      </div>
      <div className={styles.filterField}>
        <Select
          label={t("admin:users.filters.status")}
          value={status ?? ALL}
          onValueChange={(v) =>
            onStatusChange(v === ALL ? undefined : (v as Status))
          }
          options={[
            { value: ALL, label: t("admin:users.filters.allStatus") },
            { value: "ACTIVE", label: t("admin:statusBadge.active") },
            { value: "DISABLED", label: t("admin:statusBadge.disabled") },
          ]}
        />
      </div>
      <div className={styles.filterField}>
        <Select
          label={t("admin:users.filters.role")}
          value={role ?? ALL}
          onValueChange={(v) => onRoleChange(v === ALL ? undefined : (v as Role))}
          options={[
            { value: ALL, label: t("admin:users.filters.allRole") },
            { value: "USER", label: t("admin:roleBadge.user") },
            { value: "ADMIN", label: t("admin:roleBadge.admin") },
          ]}
        />
      </div>
      <div className={styles.filterSearch}>
        <TextField
          label={t("admin:users.filters.search")}
          placeholder={t("admin:users.filters.searchPlaceholder")}
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          autoComplete="off"
        />
      </div>
    </div>
  );
}
