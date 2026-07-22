import { useEffect } from "react";
import Lightbox from "yet-another-react-lightbox";
import type { Slide } from "yet-another-react-lightbox";
import Zoom from "yet-another-react-lightbox/plugins/zoom";
import Captions from "yet-another-react-lightbox/plugins/captions";
import Counter from "yet-another-react-lightbox/plugins/counter";
import Download from "yet-another-react-lightbox/plugins/download";
import Share from "yet-another-react-lightbox/plugins/share";
import "yet-another-react-lightbox/styles.css";
import "yet-another-react-lightbox/plugins/captions.css";
import "yet-another-react-lightbox/plugins/counter.css";
import { useT } from "@/i18n/useT";
import { formatMoneyVnd } from "@/i18n/format";
import type { MemberQrResponse } from "../api/types";
import { downloadMemberQr, shareMemberQr } from "../qrShare";
import type { QrDialogKind } from "./QrDialog";
import styles from "./QrPreviewDialog.module.css";

export type QrPreviewDialogProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** All still-owing members, one QR per slide (owned by QrDialog). */
  members: MemberQrResponse[];
  /** For the per-slide img alt (expense/event). */
  kind: QrDialogKind;
  /** Which member to open on (the dialog shows index 0). */
  startIndex?: number;
};

/**
 * Full-viewport, zoomable/pannable QR preview (lightbox), built on
 * `yet-another-react-lightbox` (YARL). One slide per still-owing member: prev/next
 * + native swipe when there is more than one, hidden for a single member. Each
 * slide is captioned with the member's name (title) + amount (description) via the
 * Captions plugin, and Counter renders index/total. The Download + Share toolbar
 * buttons act on the ACTIVE slide through our shared per-member helpers
 * (`downloadMemberQr`/`shareMemberQr`); YARL's Share plugin auto-hides its button
 * when the Web Share API is unsupported (`isShareSupported()`).
 *
 * The QR images are `data:image/png` data URLs — nothing to create or revoke here.
 *
 * Layering (R5): YARL portals to `<body>` at a very high z-index; we pin its portal
 * z-index to our `--fs-z-lightbox` (330) token so it sits above the base QR dialog
 * (`--fs-z-modal` 310) + popovers (320) and below toasts (400).
 *
 * Scannability (D6): a white ground is reinstated behind the slide image (scoped to
 * `.yarl__slide_image`, see the module CSS) so the QR always scans over YARL's dark
 * backdrop, while the toolbar/close chrome stays dark for contrast.
 */
export function QrPreviewDialog({
  open,
  onOpenChange,
  members,
  kind,
  startIndex = 0,
}: QrPreviewDialogProps) {
  const { t } = useT();

  const isOpen = open && members.length > 0;

  // R6 — if the parent opened us but the member list vanished (destination-switch
  // refetch, error, base-dialog close), close (belt-and-suspenders with the
  // parent's own reset on an empty list).
  useEffect(() => {
    if (open && members.length === 0) onOpenChange(false);
  }, [open, members.length, onOpenChange]);

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

  const altKey =
    kind === "expense" ? "wallet:qr.imageAltExpense" : "wallet:qr.imageAltEvent";

  const slides = members.map((m) => ({
    src: m.image,
    alt: t(altKey, { name: m.memberName }),
    title: m.memberName,
    description: formatMoneyVnd(m.amount),
  }));

  // Resolve the MemberQrResponse behind the slide YARL hands the Download/Share
  // custom functions (data URLs are unique per member).
  const memberForSlide = (slide: Slide): MemberQrResponse | undefined =>
    "src" in slide ? members.find((m) => m.image === slide.src) : undefined;

  const single = members.length === 1;

  return (
    <Lightbox
      open={isOpen}
      close={() => onOpenChange(false)}
      slides={slides}
      index={startIndex}
      plugins={[Zoom, Captions, Counter, Download, Share]}
      zoom={{
        maxZoomPixelRatio: 4,
        wheelZoomDistanceFactor: 100,
        pinchZoomDistanceFactor: 100,
        scrollToZoom: true,
      }}
      captions={{ descriptionTextAlign: "center" }}
      download={{
        download: ({ slide }) => {
          const member = memberForSlide(slide);
          if (member) downloadMemberQr(member);
        },
      }}
      share={{
        share: ({ slide }) => {
          const member = memberForSlide(slide);
          if (member) {
            void shareMemberQr(
              member,
              t("wallet:qr.shareTitle"),
              t("wallet:qr.shareText", {
                name: member.memberName,
                amount: formatMoneyVnd(member.amount),
              }),
            );
          }
        },
      }}
      // Finite carousel; hide prev/next only for a single member.
      carousel={{ finite: true }}
      render={
        single
          ? { buttonPrev: () => null, buttonNext: () => null }
          : undefined
      }
      // Localize YARL's chrome from our i18n. `Lightbox` is the dialog's accessible
      // name; the Zoom / Download / Share plugins augment `Labels`.
      labels={{
        Lightbox: t("wallet:qr.previewTitle"),
        Close: t("wallet:qr.close"),
        "Zoom in": t("wallet:qr.zoomIn"),
        "Zoom out": t("wallet:qr.zoomOut"),
        Download: t("wallet:qr.download"),
        Share: t("wallet:qr.share"),
      }}
      className={styles.lightbox}
      // Pin the portal z-index to our token so the lightbox layers correctly (R5).
      styles={{ root: { "--yarl__portal_zindex": "var(--fs-z-lightbox)" } }}
    />
  );
}
