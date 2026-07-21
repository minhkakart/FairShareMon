import { useT } from "@/i18n/useT";
import { useToast } from "@/app/ToastHost";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { useSetSettled } from "../hooks/useExpenses";
import { SettledSwitch } from "./SettledSwitch";

export type SettledToggleProps = {
  uuid: string;
  isSettled: boolean;
  /** Optional accessible name suffix (e.g. the expense name) for list rows. */
  contextName?: string;
};

/**
 * The whole-expense settled toggle (B3) — its own immediate mutate, no confirm.
 * Now built on the shared presentational `SettledSwitch` (OQ1a): the color-
 * independent `role="switch"` markup lives there; this wrapper owns the mutation,
 * toast, and accessible name (behavior unchanged). This is the ONE write allowed
 * on a closed-event expense (R4), so it is never disabled by the closed-event
 * guard. Error → toast (verbatim server message); the invalidate-on-success
 * refetch reconciles the displayed state. The backend cascades this flag to every
 * billable share (OQ3a).
 */
export function SettledToggle({
  uuid,
  isSettled,
  contextName,
}: SettledToggleProps) {
  const { t } = useT();
  const toast = useToast();
  const setSettled = useSetSettled();

  const accessibleName = contextName
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
    <SettledSwitch
      isSettled={isSettled}
      onToggle={() => void onToggle()}
      pending={setSettled.isPending}
      accessibleName={accessibleName}
      labelOn={t("expenses:settled.on")}
      labelOff={t("expenses:settled.off")}
    />
  );
}
