import type { ReactNode } from "react";
import * as RadixDialog from "@radix-ui/react-dialog";
import { cx } from "../utils/cx";
import styles from "./Dialog.module.css";

/**
 * Themed modal built on Radix Dialog — inherits focus trap, focus restore,
 * Escape-to-close, scroll lock, and aria-modal wiring. The visual layer is
 * ours; the behavior is Radix's.
 *
 * Composition (controlled or trigger-based):
 *   <Dialog open={open} onOpenChange={setOpen}>
 *     <DialogContent title="…" description="…">
 *       …body…
 *       <DialogFooter> … </DialogFooter>
 *     </DialogContent>
 *   </Dialog>
 */
export const Dialog = RadixDialog.Root;
export const DialogTrigger = RadixDialog.Trigger;
export const DialogClose = RadixDialog.Close;

const CloseIcon = (
  <svg
    viewBox="0 0 20 20"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    aria-hidden="true"
  >
    <path d="M5 5l10 10M15 5L5 15" strokeLinecap="round" />
  </svg>
);

/** Warning-triangle severity glyph for the danger tone (decorative — the title
 *  copy carries the meaning; the icon reinforces it, never stands alone). */
const DangerIcon = (
  <svg
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    aria-hidden="true"
  >
    <path
      d="M12 3.2 22 20H2L12 3.2Z"
      strokeLinejoin="round"
      strokeLinecap="round"
    />
    <path d="M12 9.5v4.5" strokeLinecap="round" />
    <circle cx="12" cy="17" r="0.6" fill="currentColor" stroke="none" />
  </svg>
);

export type DialogTone = "default" | "danger";

export type DialogContentProps = {
  /** Accessible dialog title — REQUIRED (Radix warns without it). */
  title: ReactNode;
  /** Optional supporting description, associated via aria-describedby. */
  description?: ReactNode;
  children?: ReactNode;
  /** sm | md (default) | lg — max width of the panel. */
  size?: "sm" | "md" | "lg";
  /**
   * Severity treatment. `default` is the ordinary confirm/form dialog. `danger`
   * marks a destructive or IRREVERSIBLE action (closing an event, a hard
   * delete): the panel gains a danger top accent and the title a warning-triangle
   * severity glyph, so it reads as distinct from a routine confirm at a glance.
   * Pair `danger` with a `variant="danger"` primary button and explicit
   * "không thể hoàn tác" copy.
   */
  tone?: DialogTone;
  /** Show the top-right close button (default true). */
  showClose?: boolean;
  /** Label for the close button (localized by the implementer). */
  closeLabel?: string;
  className?: string;
};

export function DialogContent({
  title,
  description,
  children,
  size = "md",
  tone = "default",
  showClose = true,
  closeLabel = "Đóng",
  className,
}: DialogContentProps) {
  return (
    <RadixDialog.Portal>
      <RadixDialog.Overlay className={styles.overlay} />
      <RadixDialog.Content
        className={cx(
          styles.content,
          styles[size],
          tone === "danger" && styles.danger,
          className,
        )}
      >
        <div className={styles.header}>
          <RadixDialog.Title className={styles.title}>
            {tone === "danger" ? (
              <span className={styles.severityIcon} aria-hidden="true">
                {DangerIcon}
              </span>
            ) : null}
            <span>{title}</span>
          </RadixDialog.Title>
          {showClose ? (
            <RadixDialog.Close className={styles.close} aria-label={closeLabel}>
              {CloseIcon}
            </RadixDialog.Close>
          ) : null}
        </div>
        {description ? (
          <RadixDialog.Description className={styles.description}>
            {description}
          </RadixDialog.Description>
        ) : null}
        <div className={styles.body}>{children}</div>
      </RadixDialog.Content>
    </RadixDialog.Portal>
  );
}

/** Right-aligned action row for a dialog footer. */
export function DialogFooter({
  children,
  className,
}: {
  children: ReactNode;
  className?: string;
}) {
  return <div className={cx(styles.footer, className)}>{children}</div>;
}
