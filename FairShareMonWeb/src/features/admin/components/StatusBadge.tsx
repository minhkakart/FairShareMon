import { Badge } from "@/components/ui";
import { useT } from "@/i18n/useT";
import type { Status } from "../api/types";
import { BanIcon, CheckIcon } from "./icons";

/**
 * Status badge — DISABLED wears danger + a ban glyph; ACTIVE wears success + a
 * check. Icon + distinct text carry the meaning (never color alone). Feature-local
 * per the plan.
 */
export function StatusBadge({ status }: { status: Status }) {
  const { t } = useT();
  return status === "DISABLED" ? (
    <Badge tone="danger" icon={<BanIcon />}>
      {t("admin:statusBadge.disabled")}
    </Badge>
  ) : (
    <Badge tone="success" icon={<CheckIcon />}>
      {t("admin:statusBadge.active")}
    </Badge>
  );
}
