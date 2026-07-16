import { createContext, use, useState } from "react";
import type { ReactNode } from "react";
import { Toast, ToastProvider, ToastViewport } from "@/components/ui";
import type { ToastTone } from "@/components/ui";

interface ToastItem {
  id: number;
  tone: ToastTone;
  title: ReactNode;
  description?: ReactNode;
  open: boolean;
}

export interface PushToastInput {
  tone?: ToastTone;
  title: ReactNode;
  description?: ReactNode;
}

interface ToastApi {
  push: (toast: PushToastInput) => void;
}

const ToastContext = createContext<ToastApi | null>(null);

let idCounter = 0;

/**
 * App-owned toast queue over the presentational Radix `Toast` primitive (queue
 * ownership is the implementer's per the design README). Mount once near the
 * root; consume via `useToast().push(...)` for mutation feedback.
 */
export function ToastHost({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([]);

  function remove(id: number) {
    setItems((prev) => prev.filter((item) => item.id !== id));
  }

  function handleOpenChange(id: number, open: boolean) {
    if (open) return;
    // Flag closed (lets Radix animate out), then drop from the queue.
    setItems((prev) =>
      prev.map((item) => (item.id === id ? { ...item, open: false } : item)),
    );
    window.setTimeout(() => remove(id), 200);
  }

  function push(toast: PushToastInput) {
    const id = ++idCounter;
    setItems((prev) => [
      ...prev,
      {
        id,
        open: true,
        tone: toast.tone ?? "info",
        title: toast.title,
        description: toast.description,
      },
    ]);
  }

  return (
    <ToastContext value={{ push }}>
      <ToastProvider swipeDirection="right">
        {children}
        {items.map((item) => (
          <Toast
            key={item.id}
            tone={item.tone}
            title={item.title}
            description={item.description}
            open={item.open}
            onOpenChange={(open) => handleOpenChange(item.id, open)}
          />
        ))}
        <ToastViewport />
      </ToastProvider>
    </ToastContext>
  );
}

export function useToast(): ToastApi {
  const ctx = use(ToastContext);
  if (!ctx) throw new Error("useToast must be used within ToastHost");
  return ctx;
}
