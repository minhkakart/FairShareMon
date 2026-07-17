import { TierBadge } from "@/components/ui";
import { useT } from "@/i18n/useT";
import type { Tier } from "../api/types";

/** TierBadge wired to the admin namespace's tier labels (Free/Premium). */
export function AdminTierBadge({ tier }: { tier: Tier }) {
  const { t } = useT();
  return (
    <TierBadge
      tier={tier}
      freeLabel={t("admin:tierBadge.free")}
      premiumLabel={t("admin:tierBadge.premium")}
    />
  );
}
