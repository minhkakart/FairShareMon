import { Badge } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { LockIcon, OpenIcon } from "./icons";

export type EventStatusBadgeProps = {
  isClosed: boolean;
};

/**
 * The open/closed event status badge (ui-designer spec): open = success + clock
 * glyph, closed = neutral + lock glyph. Meaning is carried by the text label +
 * icon, never color alone.
 */
export function EventStatusBadge({ isClosed }: EventStatusBadgeProps) {
  const { t } = useT();
  return isClosed ? (
    <Badge tone="neutral" icon={<LockIcon />}>
      {t("events:status.closed")}
    </Badge>
  ) : (
    <Badge tone="success" icon={<OpenIcon />}>
      {t("events:status.open")}
    </Badge>
  );
}
