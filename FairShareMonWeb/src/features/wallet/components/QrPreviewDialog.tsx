import { useEffect } from "react";
import Lightbox from "yet-another-react-lightbox";
import Zoom from "yet-another-react-lightbox/plugins/zoom";
import "yet-another-react-lightbox/styles.css";
import { useT } from "@/i18n/useT";
import type { QrDialogKind } from "./QrDialog";
import styles from "./QrPreviewDialog.module.css";

export type QrPreviewDialogProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The object URL owned by QrDialog (reused, never revoked here). */
  imageUrl: string | null;
  /** For the img alt (expense/event). */
  kind: QrDialogKind;
};

/**
 * Full-viewport, zoomable/pannable QR preview (lightbox), built on
 * `yet-another-react-lightbox` (YARL) + its Zoom plugin. It only READS the
 * `imageUrl` object URL owned by `QrDialog` — it never creates or revokes one
 * (R4). Single image, so prev/next navigation is hidden and the carousel is
 * finite.
 *
 * Layering (R5): YARL portals to `<body>` at a very high z-index (default 9999);
 * we pin its portal z-index to our `--fs-z-lightbox` (330) token so it still sits
 * above the base QR dialog (`--fs-z-modal` 310) + popovers (320) and below toasts
 * (400).
 *
 * Scannability (D6): YARL's backdrop is dark and the composite QR PNG carries a
 * white quiet-zone but is not guaranteed opaque, so a white ground is reinstated
 * behind the slide image (scoped to `.yarl__slide_image`, see the module CSS) —
 * matching the shipped `.qrFrame` treatment — while the toolbar/close chrome stays
 * dark for contrast.
 */
export function QrPreviewDialog({
  open,
  onOpenChange,
  imageUrl,
  kind,
}: QrPreviewDialogProps) {
  const { t } = useT();

  const isOpen = open && imageUrl != null;

  // R6 — if the parent opened us but the image vanished (destination-switch
  // refetch, error, base-dialog close), close (belt-and-suspenders with the
  // parent's own reset on `imageUrl → null`).
  useEffect(() => {
    if (open && imageUrl == null) onOpenChange(false);
  }, [open, imageUrl, onOpenChange]);

  // R5 — Escape closes the PREVIEW only, leaving the base QR dialog open. The base
  // dialog is a Radix Dialog whose Escape listener runs in the DOCUMENT capture
  // phase; YARL is a body-level portal that is NOT part of Radix's
  // dismissable-layer stack, so a bare Escape would dismiss BOTH. Intercept Escape
  // at the WINDOW capture phase (which fires before document capture), stop it from
  // reaching Radix, and close only the preview.
  useEffect(() => {
    if (!isOpen) return;
    const onEscapeCapture = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      event.stopImmediatePropagation();
      event.preventDefault();
      onOpenChange(false);
    };
    window.addEventListener("keydown", onEscapeCapture, { capture: true });
    return () =>
      window.removeEventListener("keydown", onEscapeCapture, { capture: true });
  }, [isOpen, onOpenChange]);

  const alt = t(
    kind === "expense" ? "wallet:qr.imageAltExpense" : "wallet:qr.imageAltEvent",
  );

  return (
    <Lightbox
      open={isOpen}
      close={() => onOpenChange(false)}
      slides={imageUrl ? [{ src: imageUrl, alt }] : []}
      plugins={[Zoom]}
      zoom={{
        maxZoomPixelRatio: 4,
        wheelZoomDistanceFactor: 100,
        pinchZoomDistanceFactor: 100,
        scrollToZoom: true,
      }}
      // Single image → finite carousel + no prev/next affordances.
      carousel={{ finite: true }}
      render={{ buttonPrev: () => null, buttonNext: () => null }}
      // Localize YARL's chrome from our i18n. `Lightbox` is the dialog's
      // accessible name (defaults to the English literal otherwise); the Zoom
      // plugin augments `Labels` with "Zoom in" / "Zoom out".
      labels={{
        Lightbox: t("wallet:qr.previewTitle"),
        Close: t("wallet:qr.close"),
        "Zoom in": t("wallet:qr.zoomIn"),
        "Zoom out": t("wallet:qr.zoomOut"),
      }}
      className={styles.lightbox}
      // Pin the portal z-index to our token so the lightbox layers correctly (R5).
      styles={{ root: { "--yarl__portal_zindex": "var(--fs-z-lightbox)" } }}
    />
  );
}
