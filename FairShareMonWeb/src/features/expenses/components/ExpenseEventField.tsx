import { useId } from "react";
import { Badge, Combobox } from "@/components/ui";
import type { ComboboxOption } from "@/components/ui";
import { useT } from "@/i18n/useT";
import type { EventSummaryResponse } from "@/features/events/api/types";
import { formatRange } from "@/features/events/dateRange";
import { LockIcon } from "./icons";
import styles from "./ExpenseEventField.module.css";

/**
 * The create-time event control (create-only — never in the shared general form).
 *
 * - **Locked** (Feature 2): a labelled read-only block showing the event name and
 *   an info `Badge` with a lock glyph. No interactive control; the lock is conveyed
 *   by icon AND text, not colour alone.
 * - **Picker** (Feature 1, OQ1a): a searchable `Combobox` whose first option is the
 *   explicit "no event (loose)" choice (value `""`), so the user can both pick and
 *   clear back to a loose expense.
 */
export type ExpenseEventFieldProps =
  | { lockedName: string }
  | {
      value?: string;
      onChange: (value: string) => void;
      events: EventSummaryResponse[];
      loading?: boolean;
      error?: string;
    };

export function ExpenseEventField(props: ExpenseEventFieldProps) {
  const { t } = useT();
  const labelId = useId();

  if ("lockedName" in props) {
    return (
      <div className={styles.locked}>
        <span className={styles.label} id={labelId}>
          {t("expenses:form.eventLockedLabel")}
        </span>
        <div className={styles.lockedValue} aria-labelledby={labelId}>
          <span className={styles.lockedName}>{props.lockedName}</span>
          <Badge tone="info" icon={<LockIcon />}>
            {t("expenses:shares.locked")}
          </Badge>
        </div>
        <p className={styles.hint}>{t("expenses:form.eventLockedHint")}</p>
      </div>
    );
  }

  const { value, onChange, events, loading, error } = props;
  const options: ComboboxOption[] = [
    { value: "", label: t("expenses:form.eventLoose") },
    ...events.map((event) => ({
      value: event.uuid,
      label: event.name,
      keywords: [formatRange(event.startDate, event.endDate)],
    })),
  ];

  return (
    <Combobox
      label={t("expenses:form.eventLabel")}
      value={value ?? ""}
      onValueChange={onChange}
      options={options}
      placeholder={t("expenses:form.eventPlaceholder")}
      searchPlaceholder={t("expenses:form.eventSearchPlaceholder")}
      emptyLabel={t("expenses:form.eventSearchEmpty")}
      loading={loading}
      hint={t("expenses:form.eventHint")}
      error={error}
    />
  );
}
