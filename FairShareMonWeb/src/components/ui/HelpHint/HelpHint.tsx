import { useId, type ReactNode } from "react";
import { cx } from "../utils/cx";
import styles from "./HelpHint.module.css";

export type HelpHintProps = {
  /** Accessible name for the trigger button (e.g. "Vì sao không có nút này?"). */
  label: string;
  /** The hint text shown in the bubble. Keep it short — one or two sentences. */
  children: ReactNode;
  className?: string;
};

/**
 * A small inline "why" disclosure: an info-glyph button that reveals a short
 * explanatory bubble on hover or keyboard focus (`:focus-within`, so Tab
 * reveals it exactly like hover does) — pure CSS, no JS state, consistent with
 * every other presentational primitive in this system (`Money`, `Badge`,
 * `SettledSwitch`). No new dependency: this codebase has no Radix Tooltip
 * installed, and adding one is an Open Question for the foundation, not a
 * decision `ui-designer` can make unilaterally (`FairShareMonWeb/CLAUDE.md`).
 *
 * Introduced for event-expense-settlement-sync (2026-08-25) to explain, inline,
 * WHY a control looks the way it does — e.g. why an ineligible creditor's row
 * has no settle toggle, or why "số tiền đã tất toán" can read less than the sum
 * of a member's individually-settled bills — without a modal or help page.
 * Reusable anywhere a short "why" note belongs next to a control or figure.
 */
export function HelpHint({ label, children, className }: HelpHintProps) {
  const id = useId();
  return (
    <span className={cx(styles.root, className)}>
      <button
        type="button"
        className={styles.trigger}
        aria-describedby={id}
        aria-label={label}
      >
        <svg
          viewBox="0 0 20 20"
          aria-hidden="true"
          fill="currentColor"
          width="1em"
          height="1em"
        >
          <path d="M10 2a8 8 0 100 16 8 8 0 000-16zm0 4.2a1 1 0 011 1v5a1 1 0 11-2 0v-5a1 1 0 011-1zm0-3.4a1.15 1.15 0 110 2.3 1.15 1.15 0 010-2.3z" />
        </svg>
      </button>
      <span role="tooltip" id={id} className={styles.bubble}>
        {children}
      </span>
    </span>
  );
}
