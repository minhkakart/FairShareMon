import { Badge } from "@/components/ui";
import { useT } from "@/i18n/useT";
import type { Role } from "../api/types";
import { ShieldIcon } from "./icons";

/**
 * Role badge — ADMIN wears the info accent + a shield glyph; USER is neutral.
 * Icon + distinct text carry the meaning (never color alone). Feature-local per
 * the plan.
 */
export function RoleBadge({ role }: { role: Role }) {
  const { t } = useT();
  return role === "ADMIN" ? (
    <Badge tone="info" icon={<ShieldIcon />}>
      {t("admin:roleBadge.admin")}
    </Badge>
  ) : (
    <Badge tone="neutral">{t("admin:roleBadge.user")}</Badge>
  );
}
