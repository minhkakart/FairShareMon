import type { ReactNode } from "react";
import { Badge } from "../Badge/Badge";

const CrownIcon = (
  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
    <path d="M3 7l4.5 3L12 4l4.5 6L21 7l-1.6 10.2a1 1 0 01-1 .8H5.6a1 1 0 01-1-.8L3 7z" />
  </svg>
);

export type TierBadgeProps = {
  /**
   * The raw tier string from the session/profile (backend default `FREE`,
   * `PREMIUM` when granted). Compared case-insensitively so casing drift never
   * mislabels a user; an absent/unknown value renders as Free (fail-safe,
   * non-privileged).
   */
  tier?: string | null;
  /** Localized label for the Free tier (implementer passes t("...")). */
  freeLabel: ReactNode;
  /** Localized label for the Premium tier. */
  premiumLabel: ReactNode;
  className?: string;
};

/**
 * Display-only tier indicator. Premium wears the reserved gold treatment with a
 * crown; Free is neutral. Meaning never rests on color alone — the label text
 * always differs and Premium additionally carries the crown glyph, so the tier
 * reads for CVD users and in monochrome. Reused by the settings profile, later
 * milestones, and (optionally) the shell account button.
 */
export function TierBadge({
  tier,
  freeLabel,
  premiumLabel,
  className,
}: TierBadgeProps) {
  const isPremium = tier?.toUpperCase() === "PREMIUM";
  return (
    <Badge
      tone={isPremium ? "premium" : "free"}
      icon={isPremium ? CrownIcon : undefined}
      className={className}
    >
      {isPremium ? premiumLabel : freeLabel}
    </Badge>
  );
}
