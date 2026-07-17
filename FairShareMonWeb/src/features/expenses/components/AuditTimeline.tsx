import type { ReactNode } from "react";
import { Badge, cx, Money } from "@/components/ui";
import { useT } from "@/i18n/useT";
import type { AppTFunction } from "@/i18n/useT";
import { formatDateTime, formatMoneyVnd } from "@/i18n/format";
import type { AuditLogResponse } from "../api/types";
import styles from "./AuditTimeline.module.css";

/** Snapshot keys that are internal ids — never shown in the readable diff. */
const HIDDEN_KEYS = new Set([
  "uuid",
  "expenseUuid",
  "payerMemberUuid",
  "categoryUuid",
  "memberUuid",
]);

/** Stable display order for the known snapshot fields. */
const EXPENSE_ORDER = [
  "name",
  "description",
  "expenseTime",
  "payerMemberName",
  "categoryName",
  "tags",
  "isSettled",
];
const SHARE_ORDER = ["memberName", "amount", "note"];

const LABEL_KEYS: Record<string, string> = {
  name: "expenses:audit.field.name",
  description: "expenses:audit.field.description",
  expenseTime: "expenses:audit.field.expenseTime",
  payerMemberName: "expenses:audit.field.payer",
  categoryName: "expenses:audit.field.category",
  tags: "expenses:audit.field.tags",
  isSettled: "expenses:audit.field.isSettled",
  memberName: "expenses:audit.field.member",
  amount: "expenses:audit.field.amount",
  note: "expenses:audit.field.note",
};

type DiffField = { key: string; label: string; before?: ReactNode; after?: ReactNode };

function fieldLabel(key: string, t: AppTFunction): string {
  const mapped = LABEL_KEYS[key];
  return mapped ? t(mapped as never) : key;
}

/** Renders one snapshot value by key (money via Money, datetime formatted, tags as chips). */
function renderValue(key: string, value: unknown, t: AppTFunction): ReactNode {
  if (value === null || value === undefined || value === "") {
    return <span className={styles.muted}>—</span>;
  }
  if (key === "expenseTime" && typeof value === "string") {
    return formatDateTime(value);
  }
  if (key === "amount") {
    return <Money amount={Number(value)} size="sm" format={formatMoneyVnd} />;
  }
  if (key === "isSettled") {
    return value ? t("expenses:settled.on") : t("expenses:settled.off");
  }
  if (key === "tags" && Array.isArray(value)) {
    if (value.length === 0) return <span className={styles.muted}>—</span>;
    return (
      <span className={styles.chipRow}>
        {value.map((tag, i) => {
          const name =
            tag && typeof tag === "object" && "name" in tag
              ? String((tag as { name: unknown }).name)
              : String(tag);
          return (
            <Badge key={i} tone="neutral">
              {name}
            </Badge>
          );
        })}
      </span>
    );
  }
  if (typeof value === "object") return <>{JSON.stringify(value)}</>;
  // eslint-disable-next-line @typescript-eslint/no-base-to-string -- primitive fallback
  return <>{String(value)}</>;
}

function keysToShow(entityType: string, snapshot: Record<string, unknown>): string[] {
  const base = entityType === "Expense" ? EXPENSE_ORDER : SHARE_ORDER;
  const present = Object.keys(snapshot).filter((k) => !HIDDEN_KEYS.has(k));
  const presentSet = new Set(present);
  const ordered = base.filter((k) => presentSet.has(k));
  const extra = present.filter((k) => !base.includes(k));
  return [...ordered, ...extra];
}

/**
 * Builds the readable field-diff for an audit entry (OQ11a). Create shows the new
 * snapshot; Update shows only changed fields (before → after); Delete shows the
 * removed snapshot. Unknown fields fall back to a raw key/value line, so the
 * renderer never breaks on snapshot-shape drift.
 */
function buildDiff(entry: AuditLogResponse, t: AppTFunction): DiffField[] {
  const before = (entry.before ?? {}) as Record<string, unknown>;
  const after = (entry.after ?? {}) as Record<string, unknown>;

  if (entry.action === "Create") {
    return keysToShow(entry.entityType, after).map((key) => ({
      key,
      label: fieldLabel(key, t),
      after: renderValue(key, after[key], t),
    }));
  }
  if (entry.action === "Delete") {
    return keysToShow(entry.entityType, before).map((key) => ({
      key,
      label: fieldLabel(key, t),
      before: renderValue(key, before[key], t),
    }));
  }
  // Update — show only changed fields.
  const union = new Set([
    ...keysToShow(entry.entityType, before),
    ...keysToShow(entry.entityType, after),
  ]);
  const changed: DiffField[] = [];
  for (const key of union) {
    if (JSON.stringify(before[key]) === JSON.stringify(after[key])) continue;
    changed.push({
      key,
      label: fieldLabel(key, t),
      before: renderValue(key, before[key], t),
      after: renderValue(key, after[key], t),
    });
  }
  return changed;
}

const ACTION_TONE: Record<string, "success" | "info" | "danger"> = {
  Create: "success",
  Update: "info",
  Delete: "danger",
};

const ACTION_DOT: Record<string, string> = {
  Create: styles.dotCreate,
  Update: styles.dotUpdate,
  Delete: styles.dotDelete,
};

export type AuditTimelineProps = {
  entries: AuditLogResponse[];
};

/**
 * The immutable per-expense change log as an ordered timeline (`<ol>`), rendered
 * time-ascending as returned. Each entry: an action badge (Tạo/Cập nhật/Xóa), the
 * entity type (Phiếu/Phần gánh), the timestamp, and the readable field-diff.
 */
export function AuditTimeline({ entries }: AuditTimelineProps) {
  const { t } = useT();

  return (
    <ol className={styles.timeline}>
      {entries.map((entry) => {
        const tone = ACTION_TONE[entry.action] ?? "info";
        const actionLabel =
          entry.action === "Create"
            ? t("expenses:audit.actionCreate")
            : entry.action === "Update"
              ? t("expenses:audit.actionUpdate")
              : t("expenses:audit.actionDelete");
        const entityLabel =
          entry.entityType === "Expense"
            ? t("expenses:audit.entityExpense")
            : t("expenses:audit.entityShare");
        const fields = buildDiff(entry, t);
        return (
          <li key={entry.uuid} className={styles.timelineItem}>
            <span
              className={cx(styles.timelineDot, ACTION_DOT[entry.action])}
              aria-hidden="true"
            />
            <div className={styles.timelineBody}>
              <div className={styles.timelineHead}>
                <Badge tone={tone}>{actionLabel}</Badge>
                <span className={styles.timelineEntity}>{entityLabel}</span>
                <time className={styles.timelineTime}>
                  {formatDateTime(entry.createdAt)}
                </time>
              </div>
              {fields.length > 0 ? (
                <dl className={styles.diff}>
                  {fields.map((f) => (
                    <div key={f.key} className={styles.diffRow}>
                      <dt className={styles.diffLabel}>{f.label}</dt>
                      <dd className={styles.diffValue}>
                        {entry.action === "Update" ? (
                          <span className={styles.diffChange}>
                            <span className={styles.diffBefore}>
                              {f.before ?? <span className={styles.muted}>—</span>}
                            </span>
                            <span
                              className={styles.diffArrow}
                              aria-label={t("expenses:audit.changedTo")}
                            >
                              →
                            </span>
                            <span className={styles.diffAfter}>{f.after}</span>
                          </span>
                        ) : entry.action === "Delete" ? (
                          <span className={styles.diffRemoved}>{f.before}</span>
                        ) : (
                          <span className={styles.diffAfter}>{f.after}</span>
                        )}
                      </dd>
                    </div>
                  ))}
                </dl>
              ) : null}
            </div>
          </li>
        );
      })}
    </ol>
  );
}
