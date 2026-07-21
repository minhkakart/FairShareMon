import { cx } from "@/components/ui";
import { CheckIcon, ClockIcon } from "./icons";
import styles from "./SettledSwitch.module.css";

export type SettledSwitchProps = {
  /** Current settled state (đã trả) — drives `aria-checked`, icon, and label. */
  isSettled: boolean;
  /** Fired on click; the caller owns the mutation + toast (refetch reconciles). */
  onToggle: () => void;
  /** Disable while the caller's mutation is in flight (no optimistic update). */
  pending?: boolean;
  /** Distinct accessible name (expense / share / member context). */
  accessibleName: string;
  /** Visible label when settled ("Đã trả" / "Settled"). */
  labelOn: string;
  /** Visible label when not settled ("Chưa trả" / "Unsettled"). */
  labelOff: string;
  /** Compact sizing for dense table cells. */
  size?: "md" | "sm";
};

/**
 * The presentational settled switch (OQ1a) — the ONE color-independent
 * `role="switch"` markup shared by all three settled toggles (whole-expense,
 * per-share, per-member). State is conveyed by an icon + text label, never color
 * alone. Pure: it holds no data and runs no mutation — the wrapper owns the
 * mutation, toast, and the accessible name.
 */
export function SettledSwitch({
  isSettled,
  onToggle,
  pending = false,
  accessibleName,
  labelOn,
  labelOff,
  size = "md",
}: SettledSwitchProps) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={isSettled}
      aria-label={accessibleName}
      className={cx(
        styles.switch,
        isSettled && styles.switchOn,
        size === "sm" && styles.switchSm,
      )}
      disabled={pending}
      onClick={onToggle}
    >
      <span className={styles.switchTrack} aria-hidden="true">
        <span className={styles.switchThumb} />
      </span>
      <span className={styles.icon} aria-hidden="true">
        {isSettled ? <CheckIcon /> : <ClockIcon />}
      </span>
      <span className={styles.switchLabel}>{isSettled ? labelOn : labelOff}</span>
    </button>
  );
}
