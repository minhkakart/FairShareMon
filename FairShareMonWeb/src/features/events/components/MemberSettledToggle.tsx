import { useT } from "@/i18n/useT";
import { useToast } from "@/app/ToastHost";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { SettledSwitch } from "@/features/expenses/components/SettledSwitch";
import { useSetMemberSettled } from "../hooks/useEvents";

export type MemberSettledToggleProps = {
  eventUuid: string;
  memberUuid: string;
  memberName: string;
  isSettled: boolean;
};

/**
 * Per-member net-clearance settled toggle (Layer B, R5) — a compact
 * `SettledSwitch` in the balance overlay. Flips the member's net-debt clearance
 * via `PUT /v1/events/{eventUuid}/members/{memberUuid}/settled`. Enabled on OPEN
 * and CLOSED events (the sole closed-event write, R6). Refetch-based (OQ6a):
 * disabled while pending; the balance-overlay refetch drives `outstanding` → 0
 * and flips the status badge; error → toast (verbatim server message).
 */
export function MemberSettledToggle({
  eventUuid,
  memberUuid,
  memberName,
  isSettled,
}: MemberSettledToggleProps) {
  const { t } = useT();
  const toast = useToast();
  const setMemberSettled = useSetMemberSettled();

  async function onToggle() {
    const next = !isSettled;
    try {
      await setMemberSettled.mutateAsync({
        eventUuid,
        memberUuid,
        body: { isSettled: next },
      });
      toast.push({
        tone: "success",
        title: next
          ? t("events:balance.settledToastOn")
          : t("events:balance.settledToastOff"),
      });
    } catch (error) {
      toast.push({ tone: "danger", title: resolveErrorMessage(error, t) });
    }
  }

  return (
    <SettledSwitch
      size="sm"
      isSettled={isSettled}
      onToggle={() => void onToggle()}
      pending={setMemberSettled.isPending}
      accessibleName={t("events:balance.settledAriaNamed", { name: memberName })}
      labelOn={t("events:balance.statusSettled")}
      labelOff={t("events:balance.statusOwing")}
    />
  );
}
