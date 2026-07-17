import { useId } from "react";
import { Button, Select, TextField } from "@/components/ui";
import type { SelectOption } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useCategoriesQuery } from "@/features/categories/hooks/useCategories";
import { useTagsQuery } from "@/features/tags/hooks/useTags";
import { useEventsQuery } from "@/features/events/hooks/useEvents";
import {
  buildCategoryOptions,
  makeRenderCategoryOption,
} from "./pickerOptions";
import type { CategoryMeta } from "./pickerOptions";
import styles from "./ExpenseFilterBar.module.css";

export type SettledFilter = "all" | "yes" | "no";

export type UiFilters = {
  from: string;
  to: string;
  categoryUuid: string;
  tagUuid: string;
  settled: SettledFilter;
  looseOnly: boolean;
  /** Single-event filter (M5). Mutually exclusive with `looseOnly`. */
  eventUuid: string;
  /** Client-side name search (not sent to the API). */
  q: string;
};

export type ExpenseFilterBarProps = {
  filters: UiFilters;
  onChange: (patch: Partial<UiFilters>) => void;
  onClear: () => void;
  /** True when any server or client filter is active (enables "clear"). */
  hasActiveFilters: boolean;
};

const ALL = "all";

/**
 * The list filter bar (OQ7a/OQ13a): date range, category `Select`, tag `Select`,
 * settled tri-state `Select`, an event `Select` (M5 — completes the M4 OQ7
 * deferral), a loose-only toggle, and a client-side name search. Uses an "all"
 * sentinel (never an empty-string item value) for the optional single-select
 * filters. Selecting an event and loose-only are mutually exclusive (selecting
 * one clears the other). Filter state lives in the URL (owned by the page); this
 * component is controlled.
 */
export function ExpenseFilterBar({
  filters,
  onChange,
  onClear,
  hasActiveFilters,
}: ExpenseFilterBarProps) {
  const { t } = useT();
  const looseId = useId();
  const renderCategoryOption = makeRenderCategoryOption(t);

  const categoriesQuery = useCategoriesQuery(false);
  const tagsQuery = useTagsQuery(false);
  const eventsQuery = useEventsQuery({});

  const categoryOptions: SelectOption<CategoryMeta | undefined>[] = [
    { value: ALL, label: t("expenses:filter.categoryAll") },
    ...buildCategoryOptions(categoriesQuery.data ?? []),
  ];

  const tagOptions: SelectOption[] = [
    { value: ALL, label: t("expenses:filter.tagAll") },
    ...(tagsQuery.data ?? []).map((tag) => ({
      value: tag.uuid,
      label: tag.name,
    })),
  ];

  const settledOptions: SelectOption[] = [
    { value: "all", label: t("expenses:filter.settledAll") },
    { value: "yes", label: t("expenses:filter.settledYes") },
    { value: "no", label: t("expenses:filter.settledNo") },
  ];

  const eventOptions: SelectOption[] = [
    { value: ALL, label: t("expenses:filter.eventAll") },
    ...(eventsQuery.data ?? []).map((event) => ({
      value: event.uuid,
      label: event.isClosed
        ? `${event.name} (${t("events:status.closed")})`
        : event.name,
    })),
  ];

  return (
    <div className={styles.filterBar}>
      <TextField
        className={styles.filterDate}
        label={t("expenses:filter.from")}
        type="date"
        value={filters.from}
        onChange={(e) => onChange({ from: e.target.value })}
      />
      <TextField
        className={styles.filterDate}
        label={t("expenses:filter.to")}
        type="date"
        value={filters.to}
        onChange={(e) => onChange({ to: e.target.value })}
      />
      <Select
        className={styles.filterField}
        label={t("expenses:filter.category")}
        value={filters.categoryUuid || ALL}
        onValueChange={(v) => onChange({ categoryUuid: v === ALL ? "" : v })}
        options={categoryOptions}
        renderOption={(o) =>
          o.value === ALL
            ? o.label
            : renderCategoryOption(o as SelectOption<CategoryMeta>)
        }
      />
      <Select
        className={styles.filterField}
        label={t("expenses:filter.tag")}
        value={filters.tagUuid || ALL}
        onValueChange={(v) => onChange({ tagUuid: v === ALL ? "" : v })}
        options={tagOptions}
      />
      <Select
        className={styles.filterField}
        label={t("expenses:filter.settled")}
        value={filters.settled}
        onValueChange={(v) => onChange({ settled: v as SettledFilter })}
        options={settledOptions}
      />
      <Select
        className={styles.filterField}
        label={t("expenses:filter.event")}
        value={filters.eventUuid || ALL}
        onValueChange={(v) =>
          onChange(
            v === ALL
              ? { eventUuid: "" }
              : { eventUuid: v, looseOnly: false },
          )
        }
        options={eventOptions}
      />
      <TextField
        className={styles.filterField}
        label={t("expenses:filter.search")}
        type="search"
        placeholder={t("expenses:filter.searchPlaceholder")}
        value={filters.q}
        onChange={(e) => onChange({ q: e.target.value })}
      />
      <label className={styles.filterToggle} htmlFor={looseId}>
        <input
          id={looseId}
          type="checkbox"
          checked={filters.looseOnly}
          onChange={(e) =>
            onChange(
              e.target.checked
                ? { looseOnly: true, eventUuid: "" }
                : { looseOnly: false },
            )
          }
        />
        <span>{t("expenses:filter.looseOnly")}</span>
      </label>
      <div className={styles.filterClear}>
        <Button
          variant="ghost"
          size="sm"
          onClick={onClear}
          disabled={!hasActiveFilters}
        >
          {t("expenses:filter.clear")}
        </Button>
      </div>
    </div>
  );
}
