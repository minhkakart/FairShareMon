import { useT } from "@/i18n/useT";
import { useToast } from "@/app/ToastHost";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { useSetShareSettled } from "../hooks/useExpenses";
import type { ShareResponse } from "../api/types";
import { SettledSwitch } from "./SettledSwitch";

export type ShareSettledToggleProps = {
  expenseUuid: string;
  share: ShareResponse;
  /**
   * The owning event's uuid, when this expense belongs to one (event-expense-
   * settlement-sync, Direction 2). When present, a successful toggle also
   * credits/claws back this member's event-level `ClearedAmount`, so the toast
   * copy and cache invalidation both change accordingly. A loose expense
   * (absent) keeps today's plain behavior.
   */
  eventUuid?: string | null;
};

/**
 * Per-share settled toggle (Layer A, R1) — a compact `SettledSwitch` on a shares
 * row. Flips `share.isSettled` via `PUT /v1/expenses/{expenseUuid}/shares/
 * {shareUuid}/settled`. Exempt from the closed-event `disabled` gate (the sole
 * write allowed on a closed event, R6). Refetch-based (OQ6a): disabled while
 * pending, reconciles from the expense-detail refetch; error → toast (verbatim).
 * When the expense belongs to an event (Direction 2), this flip also credits/
 * claws back the event's per-member `ClearedAmount` overlay.
 */
export function ShareSettledToggle({
  expenseUuid,
  share,
  eventUuid,
}: ShareSettledToggleProps) {
  const { t } = useT();
  const toast = useToast();
  const setShareSettled = useSetShareSettled();

  async function onToggle() {
    const next = !share.isSettled;
    try {
      await setShareSettled.mutateAsync({
        expenseUuid,
        shareUuid: share.uuid,
        body: { isSettled: next },
        eventUuid,
      });
      toast.push({
        tone: "success",
        title: eventUuid
          ? t("expenses:shares.settledToastSynced")
          : next
            ? t("expenses:shares.settledToastOn")
            : t("expenses:shares.settledToastOff"),
      });
    } catch (error) {
      toast.push({ tone: "danger", title: resolveErrorMessage(error, t) });
    }
  }

  return (
    <SettledSwitch
      size="sm"
      isSettled={share.isSettled}
      onToggle={() => void onToggle()}
      pending={setShareSettled.isPending}
      accessibleName={t("expenses:shares.settledAriaNamed", {
        name: share.member.name,
      })}
      labelOn={t("expenses:settled.on")}
      labelOff={t("expenses:settled.off")}
    />
  );
}
