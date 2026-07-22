import { useEffect, useRef, useState } from "react";
import type { KeyboardEvent, PointerEvent, WheelEvent } from "react";
import * as RadixDialog from "@radix-ui/react-dialog";
import { cx } from "@/components/ui";
import { useT } from "@/i18n/useT";
import type { QrDialogKind } from "./QrDialog";
import { FitIcon, ZoomInIcon, ZoomOutIcon } from "./icons";
import styles from "./QrPreviewDialog.module.css";

export type QrPreviewDialogProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The object URL owned by QrDialog (reused, never revoked here). */
  imageUrl: string | null;
  /** For the img alt (expense/event). */
  kind: QrDialogKind;
};

const MIN = 1;
const MAX = 4;
const STEP = 1.4;

const clamp = (value: number, lo: number, hi: number): number =>
  Math.min(hi, Math.max(lo, value));

type Transform = { scale: number; tx: number; ty: number };
const FIT: Transform = { scale: 1, tx: 0, ty: 0 };

/**
 * Hand-rolled zoom/pan (D5) — Pointer Events + a CSS transform, no new
 * dependency. `scale === 1` is fit-to-viewport; MIN 1 / MAX 4. Wheel zooms toward
 * the cursor, one pointer pans, two pointers pinch toward the midpoint, buttons
 * step ×1.4, keyboard +/-/0 zoom/reset (Escape is Radix's). Pan is clamped so the
 * image can't leave the viewport. jsdom-safe: `setPointerCapture` is optional and
 * a zero-size `getBoundingClientRect` skips the geometry math.
 */
function useZoomPan(open: boolean) {
  const viewportRef = useRef<HTMLDivElement>(null);
  const [transform, setTransform] = useState<Transform>(FIT);
  const [dragging, setDragging] = useState(false);

  // The imperative Pointer/wheel handlers read the latest transform from a ref.
  const transformRef = useRef<Transform>(transform);
  transformRef.current = transform;

  const pointers = useRef(new Map<number, { x: number; y: number }>());
  const pinch = useRef<{ dist: number; scale: number } | null>(null);

  // Reset to fit whenever the preview opens.
  useEffect(() => {
    if (!open) return;
    setTransform(FIT);
    setDragging(false);
    pointers.current.clear();
    pinch.current = null;
  }, [open]);

  function liveRect(): DOMRect | null {
    const el = viewportRef.current;
    if (!el) return null;
    const rect = el.getBoundingClientRect();
    // jsdom (no layout) returns a zero-size rect — skip the geometry math.
    if (rect.width === 0 || rect.height === 0) return null;
    return rect;
  }

  function commit(next: Transform) {
    const rect = liveRect();
    if (!rect) {
      setTransform({ scale: next.scale, tx: 0, ty: 0 });
      return;
    }
    const maxX = ((next.scale - 1) * rect.width) / 2;
    const maxY = ((next.scale - 1) * rect.height) / 2;
    setTransform({
      scale: next.scale,
      tx: clamp(next.tx, -maxX, maxX),
      ty: clamp(next.ty, -maxY, maxY),
    });
  }

  // Zoom to `nextScale` while keeping the content point under (dx, dy) — offsets
  // from the viewport centre — fixed on screen.
  function zoomToPoint(nextScale: number, dx: number, dy: number) {
    const current = transformRef.current;
    const scale = clamp(nextScale, MIN, MAX);
    const ratio = scale / current.scale;
    commit({
      scale,
      tx: dx - (dx - current.tx) * ratio,
      ty: dy - (dy - current.ty) * ratio,
    });
  }

  function zoomStep(factor: number) {
    const current = transformRef.current;
    const scale = clamp(current.scale * factor, MIN, MAX);
    const ratio = scale / current.scale;
    commit({ scale, tx: current.tx * ratio, ty: current.ty * ratio });
  }

  const zoomIn = () => zoomStep(STEP);
  const zoomOut = () => zoomStep(1 / STEP);
  const resetToFit = () => commit(FIT);

  function onWheel(event: WheelEvent<HTMLDivElement>) {
    const current = transformRef.current;
    const nextScale = clamp(current.scale * Math.exp(-event.deltaY * 0.0015), MIN, MAX);
    const rect = liveRect();
    if (!rect) {
      commit({ scale: nextScale, tx: current.tx, ty: current.ty });
      return;
    }
    const dx = event.clientX - rect.left - rect.width / 2;
    const dy = event.clientY - rect.top - rect.height / 2;
    zoomToPoint(nextScale, dx, dy);
  }

  function onPointerDown(event: PointerEvent<HTMLDivElement>) {
    viewportRef.current?.setPointerCapture?.(event.pointerId);
    pointers.current.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (pointers.current.size === 1) {
      setDragging(true);
    } else if (pointers.current.size === 2) {
      const [a, b] = [...pointers.current.values()];
      pinch.current = {
        dist: Math.hypot(a.x - b.x, a.y - b.y),
        scale: transformRef.current.scale,
      };
    }
  }

  function onPointerMove(event: PointerEvent<HTMLDivElement>) {
    const prev = pointers.current.get(event.pointerId);
    if (!prev) return;
    const cur = { x: event.clientX, y: event.clientY };
    pointers.current.set(event.pointerId, cur);
    const current = transformRef.current;

    if (pointers.current.size === 1) {
      commit({
        scale: current.scale,
        tx: current.tx + (cur.x - prev.x),
        ty: current.ty + (cur.y - prev.y),
      });
      return;
    }

    if (pointers.current.size >= 2 && pinch.current && pinch.current.dist > 0) {
      const [a, b] = [...pointers.current.values()];
      const dist = Math.hypot(a.x - b.x, a.y - b.y);
      const nextScale = clamp(
        pinch.current.scale * (dist / pinch.current.dist),
        MIN,
        MAX,
      );
      const rect = liveRect();
      if (!rect) {
        commit({ scale: nextScale, tx: current.tx, ty: current.ty });
        return;
      }
      const dx = (a.x + b.x) / 2 - rect.left - rect.width / 2;
      const dy = (a.y + b.y) / 2 - rect.top - rect.height / 2;
      zoomToPoint(nextScale, dx, dy);
    }
  }

  function onPointerUp(event: PointerEvent<HTMLDivElement>) {
    viewportRef.current?.releasePointerCapture?.(event.pointerId);
    pointers.current.delete(event.pointerId);
    if (pointers.current.size < 2) pinch.current = null;
    if (pointers.current.size === 0) setDragging(false);
  }

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    // Escape is intentionally NOT handled — Radix's Escape-stack closes the preview.
    if (event.key === "+" || event.key === "=") {
      event.preventDefault();
      zoomIn();
    } else if (event.key === "-" || event.key === "_") {
      event.preventDefault();
      zoomOut();
    } else if (event.key === "0") {
      event.preventDefault();
      resetToFit();
    }
  }

  return {
    viewportRef,
    scale: transform.scale,
    tx: transform.tx,
    ty: transform.ty,
    dragging,
    onWheel,
    onPointerDown,
    onPointerMove,
    onPointerUp,
    onKeyDown,
    zoomIn,
    zoomOut,
    resetToFit,
  };
}

const CloseGlyph = (
  <svg
    viewBox="0 0 20 20"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    aria-hidden="true"
    width="20"
    height="20"
  >
    <path d="M5 5l10 10M15 5L5 15" strokeLinecap="round" />
  </svg>
);

/**
 * Full-viewport, zoomable/pannable QR preview (lightbox). Built on RAW
 * `@radix-ui/react-dialog` primitives (D2) with an explicit `--fs-z-lightbox`
 * layer so it sits above the base QR dialog (which is a design-system
 * `DialogContent` fixed at `--fs-z-modal`). It only READS the `imageUrl` object
 * URL owned by `QrDialog` — it never creates or revokes one (D4). The QR ground
 * stays white in both themes so the code still scans (D6).
 */
export function QrPreviewDialog({
  open,
  onOpenChange,
  imageUrl,
  kind,
}: QrPreviewDialogProps) {
  const { t } = useT();
  const zoom = useZoomPan(open);

  // R6 — if the parent opened us but the image vanished (destination-switch
  // refetch, error, base-dialog close), close and render nothing.
  useEffect(() => {
    if (open && imageUrl == null) onOpenChange(false);
  }, [open, imageUrl, onOpenChange]);

  if (open && imageUrl == null) return null;

  const alt = t(
    kind === "expense" ? "wallet:qr.imageAltExpense" : "wallet:qr.imageAltEvent",
  );

  return (
    <RadixDialog.Root open={open && imageUrl != null} onOpenChange={onOpenChange}>
      <RadixDialog.Portal>
        <RadixDialog.Overlay className={styles.overlay} />
        <RadixDialog.Content className={styles.content} aria-describedby={undefined}>
          <RadixDialog.Title className="fs-visually-hidden">
            {t("wallet:qr.previewTitle")}
          </RadixDialog.Title>

          <div
            ref={zoom.viewportRef}
            className={cx(styles.viewport, zoom.dragging && styles.dragging)}
            tabIndex={0}
            onWheel={zoom.onWheel}
            onPointerDown={zoom.onPointerDown}
            onPointerMove={zoom.onPointerMove}
            onPointerUp={zoom.onPointerUp}
            onPointerCancel={zoom.onPointerUp}
            onKeyDown={zoom.onKeyDown}
          >
            <div className={styles.ground}>
              <img
                className={styles.image}
                src={imageUrl ?? undefined}
                alt={alt}
                data-scale={zoom.scale}
                draggable={false}
                style={{
                  transform: `translate(${zoom.tx}px, ${zoom.ty}px) scale(${zoom.scale})`,
                }}
              />
            </div>
          </div>

          <div
            className={styles.toolbar}
            role="group"
            aria-label={t("wallet:qr.zoomControls")}
          >
            <button
              type="button"
              className={styles.toolButton}
              aria-label={t("wallet:qr.zoomOut")}
              onClick={zoom.zoomOut}
            >
              <ZoomOutIcon />
            </button>
            <button
              type="button"
              className={styles.toolButton}
              aria-label={t("wallet:qr.resetZoom")}
              onClick={zoom.resetToFit}
            >
              <FitIcon />
            </button>
            <button
              type="button"
              className={styles.toolButton}
              aria-label={t("wallet:qr.zoomIn")}
              onClick={zoom.zoomIn}
            >
              <ZoomInIcon />
            </button>
          </div>

          <RadixDialog.Close
            className={styles.close}
            aria-label={t("wallet:qr.close")}
          >
            {CloseGlyph}
          </RadixDialog.Close>
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  );
}
