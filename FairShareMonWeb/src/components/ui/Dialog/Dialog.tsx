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
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
    <path d="M5 5l10 10M15 5L5 15" strokeLinecap="round" />
  </svg>
);

export type DialogContentProps = {
  /** Accessible dialog title — REQUIRED (Radix warns without it). */
  title: ReactNode;
  /** Optional supporting description, associated via aria-describedby. */
  description?: ReactNode;
  children?: ReactNode;
  /** sm | md (default) | lg — max width of the panel. */
  size?: "sm" | "md" | "lg";
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
  showClose = true,
  closeLabel = "Đóng",
  className,
}: DialogContentProps) {
  return (
    <RadixDialog.Portal>
      <RadixDialog.Overlay className={styles.overlay} />
      <RadixDialog.Content className={cx(styles.content, styles[size], className)}>
        <div className={styles.header}>
          <RadixDialog.Title className={styles.title}>{title}</RadixDialog.Title>
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
export function DialogFooter({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cx(styles.footer, className)}>{children}</div>;
}
