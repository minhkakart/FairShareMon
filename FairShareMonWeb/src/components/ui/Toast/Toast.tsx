import type { ReactNode } from "react";
import * as RadixToast from "@radix-ui/react-toast";
import { cx } from "../utils/cx";
import styles from "./Toast.module.css";

/**
 * Themed toast built on Radix Toast (swipe-to-dismiss, timed auto-close,
 * hotkey focus, aria-live region — all Radix). This layer is PRESENTATIONAL.
 *
 * The implementer owns the toast state: mount <ToastProvider> once near the
 * app root with a <ToastViewport/>, keep a queue in a store/context, and render
 * one <Toast> per queued item. Example wiring lives in the design-system README.
 */
export const ToastProvider = RadixToast.Provider;

export type ToastTone = "info" | "success" | "warning" | "danger";

export type ToastProps = {
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  duration?: number;
  tone?: ToastTone;
  title: ReactNode;
  description?: ReactNode;
  /** Optional single action (e.g. "Hoàn tác"). */
  action?: ReactNode;
  closeLabel?: string;
};

export function Toast({
  open,
  onOpenChange,
  duration,
  tone = "info",
  title,
  description,
  action,
  closeLabel = "Đóng",
}: ToastProps) {
  return (
    <RadixToast.Root
      className={cx(styles.toast, styles[tone])}
      open={open}
      onOpenChange={onOpenChange}
      duration={duration}
    >
      <span className={styles.stripe} aria-hidden="true" />
      <div className={styles.content}>
        <RadixToast.Title className={styles.title}>{title}</RadixToast.Title>
        {description ? (
          <RadixToast.Description className={styles.description}>
            {description}
          </RadixToast.Description>
        ) : null}
      </div>
      {action ? (
        <RadixToast.Action asChild altText={closeLabel}>
          {action}
        </RadixToast.Action>
      ) : null}
      <RadixToast.Close className={styles.close} aria-label={closeLabel}>
        <svg
          viewBox="0 0 20 20"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          aria-hidden="true"
        >
          <path d="M5 5l10 10M15 5L5 15" strokeLinecap="round" />
        </svg>
      </RadixToast.Close>
    </RadixToast.Root>
  );
}

/** Fixed-position stack for toasts. Mount once inside <ToastProvider>. */
export function ToastViewport({ className }: { className?: string }) {
  return <RadixToast.Viewport className={cx(styles.viewport, className)} />;
}
