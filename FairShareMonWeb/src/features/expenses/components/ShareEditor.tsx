import { Controller, useFieldArray, useWatch } from "react-hook-form";
import type { Control, FieldErrors, UseFormRegister } from "react-hook-form";
import {
  Badge,
  Button,
  cx,
  Money,
  MoneyInput,
  Select,
  TextField,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { formatMoneyVnd } from "@/i18n/format";
import type { MemberResponse } from "@/features/members/api/types";
import type { CreateExpenseValues } from "../schemas";
import {
  buildMemberOptions,
  makeRenderMemberOption,
} from "./pickerOptions";
import { LockIcon, PlusIcon, StarIcon, TrashIcon } from "./icons";
import styles from "./ShareEditor.module.css";

export type ShareEditorProps = {
  control: Control<CreateExpenseValues>;
  register: UseFormRegister<CreateExpenseValues>;
  errors: FieldErrors<CreateExpenseValues>;
  /** Active members (owner-rep first) — used to populate the per-row picker. */
  members: MemberResponse[];
  ownerRepUuid: string | undefined;
};

/**
 * The atomic-create share editor (OQ4a/OQ5a). RHF `useFieldArray` over `shares`.
 * The owner-representative row is seeded by the parent (amount 0), pinned first,
 * its member locked, its amount fixed at 0, and it has no remove control (mirrors
 * the backend auto-inject + `7002`). "Thêm phần gánh" appends a row whose member
 * `Select` excludes members already chosen (mirrors `7003`). A live, display-only
 * "Tổng (tạm tính)" sums the rows — the authoritative total comes from the API.
 */
export function ShareEditor({
  control,
  register,
  errors,
  members,
  ownerRepUuid,
}: ShareEditorProps) {
  const { t } = useT();
  const renderMemberOption = makeRenderMemberOption(t);

  const { fields, append, remove } = useFieldArray({ control, name: "shares" });
  const watched = useWatch({ control, name: "shares" }) ?? [];

  const activeMembers = members.filter((m) => !m.isDeleted);
  const chosen = new Set(watched.map((r) => r?.memberUuid).filter(Boolean));
  const total = watched.reduce((sum, r) => sum + (r?.amount ?? 0), 0);

  const memberName = (uuid: string) =>
    members.find((m) => m.uuid === uuid)?.name ?? uuid;

  const optionsFor = (currentUuid: string) =>
    buildMemberOptions(
      activeMembers.filter(
        (m) => m.uuid === currentUuid || !chosen.has(m.uuid),
      ),
    );

  const addRow = () => {
    const free = activeMembers.find((m) => !chosen.has(m.uuid));
    if (!free) return;
    append({ memberUuid: free.uuid, amount: null, note: "" });
  };

  const allChosen = activeMembers.every((m) => chosen.has(m.uuid));
  const sharesError =
    typeof errors.shares?.message === "string" ? errors.shares.message : null;

  return (
    <div className={styles.shareEditor}>
      {fields.map((field, index) => {
        const currentUuid = watched[index]?.memberUuid ?? field.memberUuid;
        const locked = Boolean(ownerRepUuid) && currentUuid === ownerRepUuid;
        const name = memberName(currentUuid);
        const rowErrors = errors.shares?.[index];
        return (
          <div
            key={field.id}
            className={cx(styles.shareRow, locked && styles.shareRowLocked)}
          >
            <div className={styles.shareMember}>
              {locked ? (
                <div className={styles.lockedMember}>
                  <span className={styles.lockedMemberName}>{name}</span>
                  <Badge tone="info" icon={<LockIcon />}>
                    {t("expenses:shares.ownerRepLock")}
                  </Badge>
                </div>
              ) : (
                <Controller
                  control={control}
                  name={`shares.${index}.memberUuid`}
                  render={({ field: f }) => (
                    <Select
                      label={t("expenses:shares.memberForRow", { name })}
                      hideLabelVisually
                      value={f.value || undefined}
                      onValueChange={f.onChange}
                      options={optionsFor(f.value)}
                      renderOption={renderMemberOption}
                      error={rowErrors?.memberUuid?.message}
                    />
                  )}
                />
              )}
            </div>

            <div className={styles.shareAmount}>
              <Controller
                control={control}
                name={`shares.${index}.amount`}
                render={({ field: f }) => (
                  <MoneyInput
                    label={t("expenses:shares.amountForRow", { name })}
                    hideLabelVisually
                    value={f.value ?? null}
                    onChange={f.onChange}
                    disabled={locked}
                    placeholder="0"
                    format={formatMoneyVnd}
                    unit={null}
                    error={rowErrors?.amount?.message}
                  />
                )}
              />
            </div>

            <div className={styles.shareNote}>
              <TextField
                label={t("expenses:shares.noteForRow", { name })}
                hideLabelVisually
                placeholder={t("expenses:shares.notePlaceholder")}
                maxLength={500}
                error={rowErrors?.note?.message}
                {...register(`shares.${index}.note`)}
              />
            </div>

            <div className={styles.shareRemove}>
              {locked ? (
                <span className={styles.lockHint} aria-hidden="true">
                  <LockIcon />
                </span>
              ) : (
                <Button
                  variant="ghost"
                  size="sm"
                  aria-label={t("expenses:shares.removeNamed", { name })}
                  onClick={() => remove(index)}
                >
                  <TrashIcon />
                </Button>
              )}
            </div>
          </div>
        );
      })}

      {sharesError ? <p className={styles.sharesError}>{sharesError}</p> : null}

      <div className={styles.shareFooter}>
        <Button
          variant="secondary"
          size="sm"
          onClick={addRow}
          disabled={allChosen}
          iconStart={<PlusIcon />}
        >
          {t("expenses:shares.add")}
        </Button>

        <div className={styles.runningTotal}>
          <span className={styles.runningTotalLabel}>
            {t("expenses:shares.runningTotal")}
            <span className={styles.runningTotalHint}>
              {" "}
              · {t("expenses:shares.runningTotalHint")}
            </span>
          </span>
          <Money amount={total} size="lg" format={formatMoneyVnd} />
        </div>
      </div>

      <p className={styles.ownerRepNote}>
        <StarIcon /> {t("expenses:shares.ownerRepNote")}
      </p>
    </div>
  );
}
