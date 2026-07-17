import { cx } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useToast } from "@/app/ToastHost";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { useSetSettled } from "../hooks/useExpenses";
import { CheckIcon, ClockIcon } from "./icons";
import styles from "./SettledToggle.module.css";

export type SettledToggleProps = {
  uuid: string;
  isSettled: boolean;
  /** Optional accessible name suffix (e.g. the expense name) for list rows. */
  contextName?: string;
};

/**
 * The settled toggle (B3) — its own immediate mutate, no confirm. Color-
 * independent: a text label ("Đã trả" / "Chưa trả") plus an icon, never color
 * alone (`role="switch"` + `aria-checked`). This is the ONE write allowed on a
 * closed-event expense (R4), so it is never disabled by the closed-event guard.
 * Error → toast (verbatim server message); the invalidate-on-success refetch
 * reconciles the displayed state.
 */
export function SettledToggle({
  uuid,
  isSettled,
  contextName,
}: SettledToggleProps) {
  const { t } = useT();
  const toast = useToast();
  const setSettled = useSetSettled();

  const label = isSettled ? t("expenses:settled.on") : t("expenses:settled.off");
  const accessibleLabel = contextName
    ? t("expenses:settled.ariaNamed", { name: contextName })
    : t("expenses:settled.aria");

  async function onToggle() {
    const next = !isSettled;
    try {
      await setSettled.mutateAsync({ uuid, body: { isSettled: next } });
      toast.push({
        tone: "success",
        title: next
          ? t("expenses:settled.toastOn")
          : t("expenses:settled.toastOff"),
      });
    } catch (error) {
      toast.push({ tone: "danger", title: resolveErrorMessage(error, t) });
    }
  }

  return (
    <button
      type="button"
      role="switch"
      aria-checked={isSettled}
      aria-label={accessibleLabel}
      className={cx(styles.switch, isSettled && styles.switchOn)}
      disabled={setSettled.isPending}
      onClick={() => void onToggle()}
    >
      <span className={styles.switchTrack} aria-hidden="true">
        <span className={styles.switchThumb} />
      </span>
      <span className={styles.icon} aria-hidden="true">
        {isSettled ? <CheckIcon /> : <ClockIcon />}
      </span>
      <span className={styles.switchLabel}>{label}</span>
    </button>
  );
}
