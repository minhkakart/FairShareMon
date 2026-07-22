# QR Image Preview (Lightbox) — Zoomable QR in the QR Dialog — Web

Add a **zoomable, pannable full-viewport preview (lightbox)** to the shared VietQR dialog
(`QrDialog`), so a user can enlarge the composite settlement sheet and read/scan each per-member QR
individually. The QR PNG the backend returns is a **composite** — one QR per still-owing member
stacked vertically (`aspect-ratio: 3/4`, `max-width: 17rem` in the dialog) — which is too small to
scan comfortably at dialog size. The preview lets the user zoom in (wheel / pinch / on-screen
buttons), drag-to-pan, and reset-to-fit, without leaving the dialog and without refetching the image.

This is a **frontend-only, presentation-only** feature. It calls **no new API**, adds **no new
dependency**, and touches **no business rule** — it reuses the blob object-URL the shipped `QrDialog`
already creates for the `<img>`. Applies to **both** QR kinds (expense + event).

> **Scope note (locked).** The stack is LOCKED (`FairShareMonWeb/CLAUDE.md`). No zoom/pan/lightbox
> library is introduced — the viewer is HAND-ROLLED with Pointer Events + a CSS `transform`. Adding
> such a library would be a foundation-level Open Question; this plan explicitly does NOT take it (see
> Decision Log D5).

## Objective

When the QR image is successfully loaded and displayed in `QrDialog` (the `isReady && imageUrl`
branch), let the user open a full-viewport **QR preview** that:

- Opens from **two** triggers, both labeled `wallet:qr.enlarge`: (1) the QR `<img>` itself, made
  clickable + keyboard-focusable, and (2) a small expand/magnify icon-button pinned top-right of the
  QR frame.
- Presents the same QR on a **white ground** (so it still scans), fit to the viewport, with **wheel +
  pinch zoom, drag-to-pan, and on-screen zoom-in / zoom-out / reset-to-fit controls**.
- Reuses the **existing blob object-URL** (no refetch, no second `createObjectURL`); the blob and its
  URL lifecycle stay owned by `QrDialog`.
- Nests correctly over the base QR dialog: Escape closes the **preview first** (not the base dialog),
  focus is trapped in the preview and restored to the trigger on close, scroll stays locked.

## Background

Confirmed against the live SPA (2026-07-22):

- **`QrDialog`** (`src/features/wallet/components/QrDialog.tsx`) is the shared expense/event QR modal.
  It owns the query (`useExpenseQrQuery`/`useEventQrQuery`), the error-code → state mapping, and the
  **blob object-URL lifecycle**: on new `qrQuery.data` it does `URL.createObjectURL(data.blob)` →
  `setImageUrl(url)` and returns a cleanup that `URL.revokeObjectURL(url)` on unmount / blob change /
  destination change (`QrDialog.tsx` lines 99–111). `isReady = qrQuery.isSuccess && imageUrl != null`.
  The image is rendered by the presentational `QrDialogInner` inside `.qrWell > .qrFrame.<kind>`:
  `<img className={styles.qrImage} src={imageUrl} alt={imageAltExpense|imageAltEvent} />`, else a
  `Skeleton` occupying the same fixed-aspect footprint (`QrDialog.tsx` lines 312–328).
- **`QrDialog` is built on the design-system `DialogContent`** (`size="sm"`), which wraps
  `RadixDialog.Portal > Overlay(.overlay) > Content(.content)`. The overlay is hardcoded at
  `--fs-z-overlay` (300) with **no overlay className override**, and there is **no fullscreen size
  variant** (`components/ui/Dialog/Dialog.tsx` + `Dialog.module.css` — overlay `z-index:
  var(--fs-z-overlay)`, content `z-index: var(--fs-z-modal)` = 310). A second `DialogContent` for the
  preview would therefore render its backdrop at 300 — **behind** the base QR dialog's content at 310
  — so the composed lightbox cannot be built on `DialogContent`. It must be built on **raw Radix
  primitives** with an explicit higher z-index (see Decision Log D2).
- **z-index tokens** (`src/styles/tokens.css` lines 299–307): `--fs-z-base 0`, `--fs-z-sticky 100`,
  `--fs-z-dropdown 200`, `--fs-z-overlay 300`, `--fs-z-modal 310`, `--fs-z-popover 320`,
  `--fs-z-toast 400`. There is a gap between `--fs-z-popover` (320) and `--fs-z-toast` (400) for a
  lightbox layer that must sit above the base modal + popovers but below toasts.
- **QR frame CSS** (`QrDialog.module.css`): `.qrFrame` is `position: relative`, white background
  (`#ffffff`, deliberately light in both themes so the code scans), `overflow: hidden`,
  `aspect-ratio: 3/4` for both kinds; `.qrImage` is `object-fit: contain`. The frame being
  `position: relative` already anchors an absolutely-positioned overlay button + badge without new
  wrappers.
- **Feature icons** live in `src/features/wallet/components/icons.tsx` — inline `aria-hidden` SVGs,
  `viewBox="0 0 20 20"`, `stroke="currentColor"`, no new dependency. Existing glyphs: `QrIcon`,
  `DownloadIcon`, `CopyIcon`, `CheckIcon`, `WalletIcon`, etc.
- **i18n** for the QR UI is the `wallet` namespace under `qr.*` (`i18n/locales/{vi-VN,en-US}/wallet.json`).
  Existing relevant keys: `qr.close`, `qr.imageAltExpense`, `qr.imageAltEvent`. Parity is enforced by
  `src/features/wallet/walletI18n.test.ts` (`WalletNamespace_ViAndEn_HaveIdenticalKeyShape` +
  `NoLeafIsEmpty`) — both locales must stay key-in-sync with no empty leaves.
- **Tests** for the dialog live in `src/features/wallet/qrDialog.test.tsx` (Vitest + RTL + MSW). It
  already has a `pngResponse()` helper, spies `URL.createObjectURL`/`revokeObjectURL`, pins vi-VN,
  mocks `downloadBlob`, and asserts the `<img role="img" name={/VietQR/}>` renders from the object URL,
  plus a revoke-on-unmount regression. New preview tests extend this file.
- **Accessibility baseline** (`CLAUDE.md`): labeled controls, visible focus (`:focus-visible`),
  keyboard nav, color-independent status, `prefers-reduced-motion`, `<html lang>` synced. Radix
  provides focus-trap / focus-restore / scroll-lock / `aria-modal` / Escape-stack for free when built
  on its Dialog primitives.

## Requirements

### Functional

1. **R1 — Two triggers, one preview.** In the `QrDialog` `isReady && imageUrl` branch only, the QR
   `<img>` is wrapped by / overlaid with a clickable + keyboard-focusable surface, AND a small
   expand/magnify icon-button sits top-right of the QR frame. Both open the **same** preview. Both
   carry the accessible name `wallet:qr.enlarge` ("Phóng to mã QR"). Neither appears while the QR is
   loading, gated, errored, or empty (R6).
2. **R2 — Full-viewport zoomable/pannable viewer.** The preview is a full-viewport lightbox that
   presents the same QR on a white ground and supports: mouse-wheel zoom (toward the cursor), touch
   pinch zoom (toward the pinch midpoint), one-pointer drag-to-pan, and on-screen zoom-in / zoom-out /
   reset-to-fit buttons. Keyboard `+` / `-` / `0` on the focused viewport zoom in / out / reset.
3. **R3 — Both kinds.** Works identically for `kind === "expense"` and `kind === "event"`; the preview
   img `alt` reuses `wallet:qr.imageAltExpense` / `wallet:qr.imageAltEvent`.
4. **R4 — Reuse the existing blob URL.** The preview receives the `imageUrl` string already created by
   `QrDialog` (one `createObjectURL` per blob). No refetch, no second object URL. The blob + URL
   lifecycle stays entirely owned by `QrDialog` (the preview never revokes it).
5. **R5 — Nested modal behavior.** The preview is a nested Radix dialog rendered as a sibling of the
   base `DialogContent` inside the same `<Dialog>`. Escape closes the **preview first** (Radix
   Escape-stack precedence), leaving the base QR dialog open; focus is trapped in the preview and
   restored to the trigger on close; scroll stays locked; the preview overlay + content sit above the
   base dialog (see D3, `--fs-z-lightbox`).
6. **R6 — Only when ready; safe teardown.** The enlarge triggers exist only when `isReady`. If the
   preview is open and `imageUrl` becomes `null` (destination switch refetch, error, dialog close),
   the preview closes and early-returns `null` (guard `open && imageUrl == null`). Closing the base
   QR dialog resets `previewOpen`.
7. **R7 — Fit + reveal a tall composite.** At open, the QR fits the viewport (scale = 1 = fit) so the
   whole composite sheet is visible; zoom + pan reveal each per-member QR at scannable size. Panning
   is clamped so the image can't be dragged fully off-screen.

### Non-functional / conventions

- **No new dependency** (stack LOCKED) — hand-rolled Pointer Events + CSS `transform` (D5).
- **No new API, no new query, no `errors.ts` change, no business-rule change.** Presentation only.
- **All copy through i18n**, vi-VN authoritative + en-US parity (`walletI18n.test.ts` enforces).
- **a11y:** Radix focus-trap/restore/Escape-stack; every control labeled; `:focus-visible` rings;
  `prefers-reduced-motion` neutralizes transitions; color-independent (icons + labels).
- **Theme:** the QR ground stays `#ffffff` in both themes (a QR needs dark modules on a light
  quiet-zone to scan) — matching the shipped `.qrFrame` treatment.
- **jsdom-safe:** guard `setPointerCapture?.` and zero-size `getBoundingClientRect` so component tests
  don't throw; wheel/pinch geometry is not asserted in jsdom (covered via buttons + optional E2E).

## Open Questions

> **RESOLVED — all decided by the user before drafting; recorded here as decisions, NOT reopened.**
> Each carries its resolution; the Implementation Plan is synced to these. See the Decision Log for the
> binding entries.

### ~~OQ1~~ — Enlarge trigger surface

> **RESOLVED:** BOTH — the QR `<img>` is clickable + keyboard-focusable, AND a small expand/magnify
> icon-button sits top-right of the QR frame. Both open the same preview, both labeled
> `wallet:qr.enlarge` ("Phóng to mã QR"). (See D1.)

### ~~OQ2~~ — Viewer implementation (library vs hand-rolled)

> **RESOLVED:** HAND-ROLLED with Pointer Events + a CSS `transform`. The stack is LOCKED; a
> zoom/lightbox library is an Open Question we explicitly are NOT taking. Full-viewport lightbox with
> wheel + pinch zoom, drag-to-pan, and on-screen zoom-in / zoom-out / reset-to-fit controls. (See D5.)

### ~~OQ3~~ — Which kinds; blob reuse

> **RESOLVED:** Applies to BOTH expense + event. Reuses the EXISTING blob object-URL already created in
> `QrDialog` (no refetch, one `createObjectURL` per blob; the preview never owns/revokes it). (See D4.)

### ~~OQ4~~ — Dialog primitive (design-system `DialogContent` vs raw Radix)

> **RESOLVED:** Build on RAW `@radix-ui/react-dialog` primitives (Root/Portal/Overlay/Content), NOT the
> design-system `DialogContent`. Reason: `DialogContent` hardcodes the overlay at `--fs-z-overlay`
> (300) with no overlay className override and has no fullscreen size — a second `DialogContent` would
> render its backdrop BEHIND the base QR dialog's content (310). Raw primitives still give focus-trap
> nesting, focus restore, scroll-lock, `aria-modal`, and Escape-stack precedence. (See D2.)

### ~~OQ5~~ — z-index layering

> **RESOLVED:** Add ONE z-index token to `src/styles/tokens.css`: `--fs-z-lightbox: 330;` (above
> `--fs-z-popover` 320, below `--fs-z-toast` 400). The preview overlay + content both use it. (See D3.)

## Assumptions

- The shipped `QrDialog` blob-URL lifecycle is unchanged; the preview only *consumes* the `imageUrl`
  string via a prop. If `QrDialog` later stops holding a stable `imageUrl`, re-sync.
- The composite QR is a single PNG (one image element); the preview zooms/pans that one raster. If the
  backend ever returns multiple images, this plan's single-`<img>` transform model must be revisited
  (Future Improvement).
- jsdom does not implement layout (`getBoundingClientRect` returns zeros) nor Pointer Events geometry;
  wheel/pinch math is therefore covered by button-driven and E2E tests, not jsdom assertions.
- No change to the QR query, the error-state machine, the download/copy footer, or the Premium gate.
- React 19 + React Compiler is in effect — write idiomatic components; do not hand-add
  `useMemo`/`useCallback`. The zoom/pan state uses `useState` + `useRef` + event handlers; refs hold
  the latest transform for the imperative Pointer handlers.

## Implementation Plan

> Paths under `FairShareMonWeb/src/`. Steps marked **[MOD]** modify shipped files; **[NEW]** create
> files. Presentation-only; no API/hooks/query changes.

### Step 1 — z-index token **[MOD]** `styles/tokens.css`

Add one token in the z-index block (after `--fs-z-popover: 320;`, before `--fs-z-toast: 400;`):

```css
--fs-z-lightbox: 330; /* QR preview (nested full-viewport lightbox) — above the
                         base modal (310) + portalled popovers (320), below toasts (400) */
```

The preview overlay AND content both consume `var(--fs-z-lightbox)`.

### Step 2 — Icons **[MOD]** `features/wallet/components/icons.tsx`

Add four inline `aria-hidden` SVGs matching the existing style (`viewBox="0 0 20 20"`,
`fill="none"`, `stroke="currentColor"`, `strokeWidth ~1.7`, `strokeLinecap/Linejoin="round"`,
`width/height="20"` where used at 20×20):

- `ExpandIcon` — a "maximize/expand" glyph (four corner arrows) for the enlarge badge + image surface.
- `ZoomInIcon` — magnifier with `+`.
- `ZoomOutIcon` — magnifier with `−`.
- `FitIcon` — reset-to-fit glyph (frame with inward corners / "fit" arrows).

All decorative (`aria-hidden`) — the buttons carry the accessible names via i18n labels.

### Step 3 — **[NEW]** `features/wallet/components/QrPreviewDialog.tsx` (lightbox shell + `useZoomPan`)

Props:

```ts
export type QrPreviewDialogProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  imageUrl: string | null;   // the object URL owned by QrDialog (reused, never revoked here)
  kind: QrDialogKind;        // for the img alt (expense/event)
};
```

Structure (raw Radix primitives, D2):

```tsx
import * as RadixDialog from "@radix-ui/react-dialog";
// ...
// Early-close guard (R6): if the parent opened us but the image vanished, don't render.
if (open && imageUrl == null) { /* fire onOpenChange(false) via effect, render null */ }

<RadixDialog.Root open={open && imageUrl != null} onOpenChange={onOpenChange}>
  <RadixDialog.Portal>
    <RadixDialog.Overlay className={styles.overlay} />           {/* z-index: var(--fs-z-lightbox) */}
    <RadixDialog.Content className={styles.content} aria-describedby={undefined}>
      <VisuallyHidden><RadixDialog.Title>{t("wallet:qr.previewTitle")}</RadixDialog.Title></VisuallyHidden>
      <div
        className={cx(styles.viewport, dragging && styles.dragging)}
        tabIndex={0}
        onWheel={onWheel}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onKeyDown={onKeyDown}   // +/-/0 ; NOT Escape (Radix owns Escape)
      >
        <div className={styles.ground}>   {/* white #ffffff, aspect-ratio 3/4 → still scans */}
          <img
            className={styles.image}
            src={imageUrl}
            alt={t(kind === "expense" ? "wallet:qr.imageAltExpense" : "wallet:qr.imageAltEvent")}
            style={{ transform: `translate(${tx}px, ${ty}px) scale(${scale})` }}
            data-scale={scale}   // test hook
            draggable={false}
          />
        </div>
      </div>
      <div className={styles.toolbar} role="group" aria-label={t("wallet:qr.zoomControls")}>
        <button aria-label={t("wallet:qr.zoomOut")} onClick={zoomOut}><ZoomOutIcon/></button>
        <button aria-label={t("wallet:qr.resetZoom")} onClick={resetToFit}><FitIcon/></button>
        <button aria-label={t("wallet:qr.zoomIn")} onClick={zoomIn}><ZoomInIcon/></button>
      </div>
      <RadixDialog.Close className={styles.close} aria-label={t("wallet:qr.close")}>{/* × glyph */}</RadixDialog.Close>
    </RadixDialog.Content>
  </RadixDialog.Portal>
</RadixDialog.Root>
```

- **`VisuallyHidden`** — reuse the design-system visually-hidden primitive if one exists in
  `@/components/ui`; else a local `.srOnly` CSS-module class. (Confirm which — the ui-designer owns the
  primitive; do not fork.) The `RadixDialog.Title` is required by Radix for the a11y name.
- `aria-describedby={undefined}` suppresses Radix's missing-description warning (the QR needs no
  description beyond the img `alt`).

**Local `useZoomPan()` hook** (in the same file), state `{ scale, tx, ty }` with `scale === 1` = fit:

- Constants `MIN = 1`, `MAX = 4`.
- **Wheel:** `factor = Math.exp(-e.deltaY * 0.0015)`; new scale clamped to `[MIN, MAX]`; zoom **toward
  the cursor** — adjust `tx/ty` so the point under the cursor stays fixed, using the viewport
  `getBoundingClientRect()`.
- **Pointer Events:** track active pointers in a ref map. **1 pointer** = pan (delta added to `tx/ty`).
  **2 pointers** = pinch: use the distance ratio between the two pointers to scale, zoom toward the
  **midpoint**. `dragging` boolean state toggles `.dragging` (disables the CSS transition).
- **Buttons:** `zoomIn` = ×1.4 (clamped), `zoomOut` = ÷1.4 (clamped), `resetToFit` = `{ scale: 1, tx:
  0, ty: 0 }`.
- **Keyboard** on `.viewport` (`tabIndex 0`): `+`/`=` zoom in, `-` zoom out, `0` reset. **Escape is NOT
  handled here** — Radix's Escape-stack closes the preview.
- **Clamp pan** per axis to `±(scale - 1) * rect.<dim> / 2` (so the image can't leave the viewport;
  at `scale === 1`, pan is pinned to 0).
- **Reset to fit on open** (effect on `open` transition to true).
- **jsdom guards:** wrap `el.setPointerCapture?.(id)` optionally; if `getBoundingClientRect()` returns
  a zero-size rect, skip the geometry math (no divide-by-zero / NaN transform).

### Step 4 — **[NEW]** `features/wallet/components/QrPreviewDialog.module.css`

- `.overlay` — `position: fixed; inset: 0; z-index: var(--fs-z-lightbox);` dark scrim
  (`--fs-color-overlay` or a heavier tint for a media lightbox), fade-in.
- `.content` — `position: fixed; inset: 0; z-index: var(--fs-z-lightbox);` full-viewport flex column,
  centers the `.viewport`.
- `.viewport` — `flex: 1; overflow: hidden; touch-action: none; overscroll-behavior: contain;
  cursor: grab;` and `:focus-visible` ring. `touch-action: none` is required so mobile pinch/pan don't
  scroll the page.
- `.ground` — `background: #ffffff; aspect-ratio: 3 / 4;` centered, max-height to viewport; the white
  quiet-zone ground so the QR still scans (matches shipped `.qrFrame`).
- `.image` — `transform-origin: center; will-change: transform;` with a short transition on
  `transform` for the button zooms; `.dragging .image { transition: none }` for 1:1 drag/pinch.
- `.toolbar` — pinned bottom-center chip of three icon buttons; `:focus-visible` rings; labeled group.
- `.close` — pinned top-right; `:focus-visible` ring.
- `@media (prefers-reduced-motion: reduce)` — neutralize the transform transition + overlay fade.

### Step 5 — **[MOD]** `features/wallet/components/QrDialog.tsx`

- Add `previewOpen` state (`useState(false)`).
- Reset it when the base dialog closes (extend the existing `!open` effect) and when `imageUrl`
  becomes `null` (effect on `imageUrl`) — belt-and-suspenders with the preview's own R6 guard.
- Pass an `onEnlarge={() => setPreviewOpen(true)}` callback into `QrDialogInner` (used by both
  triggers in the ready branch).
- Render `<QrPreviewDialog open={previewOpen} onOpenChange={setPreviewOpen} imageUrl={imageUrl}
  kind={kind} />` as a **sibling of `<DialogContent>` inside the same `<Dialog>`** (so both nest under
  one Radix Root context for Escape-stack + focus nesting).
- In `QrDialogInner`, in the `isReady && imageUrl` image branch, wrap/overlay the `<img>` inside the
  `.qrFrame` with:
  - `.enlargeSurface` — an absolutely-positioned (`inset: 0`), transparent `<button type="button">`
    covering the image, `aria-label={t("wallet:qr.enlarge")}`, `onClick={onEnlarge}` (keyboard-focusable
    by nature of being a button). This makes the whole image clickable + focusable (OQ1).
  - `.enlargeBadge` — a small icon chip pinned top-right (`<ExpandIcon/>`), also a
    `<button type="button" aria-label={t("wallet:qr.enlarge")} onClick={onEnlarge}>`.
  - Keep the `<img>` non-interactive underneath (it stays decorative-with-alt; the button provides the
    interaction + name). Ensure the `.qrFrame` remains the positioning context (it already is
    `position: relative`).

### Step 6 — **[MOD]** `features/wallet/components/QrDialog.module.css`

- `.enlargeSurface` — `position: absolute; inset: 0; background: transparent; border: 0; cursor:
  zoom-in;` with a `:focus-visible` ring (inset, so it reads over the light QR ground).
- `.enlargeBadge` — `position: absolute; top: var(--fs-space-2); right: var(--fs-space-2);` a small
  rounded chip (surface + border + subtle shadow) sized ~28–32px, `display: grid; place-items:
  center;` icon at 20px; `:focus-visible` ring; sits above `.enlargeSurface` (z within the frame).
  Ensure the chip has enough contrast over the white QR ground.

### Step 7 — **[MOD]** i18n `i18n/locales/{vi-VN,en-US}/wallet.json` (under `qr`)

Add (keep both files key-in-sync; `walletI18n.test.ts` enforces parity + non-empty):

| Key | vi-VN | en-US |
| --- | --- | --- |
| `qr.enlarge` | "Phóng to mã QR" | "Enlarge QR code" |
| `qr.previewTitle` | "Xem mã QR phóng to" | "QR code preview" |
| `qr.zoomControls` | "Điều khiển thu phóng" | "Zoom controls" |
| `qr.zoomIn` | "Phóng to" | "Zoom in" |
| `qr.zoomOut` | "Thu nhỏ" | "Zoom out" |
| `qr.resetZoom` | "Khôi phục vừa khung" | "Fit to screen" |

- Reuse existing `qr.imageAltExpense` / `qr.imageAltEvent` for the preview `<img alt>`.
- Reuse existing `qr.close` for the preview close button.

### API endpoints consumed

**None.** This feature adds no request. It reuses the blob already fetched by
`useExpenseQrQuery` / `useEventQrQuery` (`GET /v1/expenses/{uuid}/qr` / `GET /v1/events/{uuid}/qr` via
`api.blob(...)`) — the object URL created once in `QrDialog`. The `ApiResult<T>` envelope, error
`code`s, refresh, and Premium gate are all handled by the shipped `QrDialog` state machine and are
untouched.

### Loading / empty / error states

- The enlarge triggers render **only** in the `isReady && imageUrl` branch — never during the
  Skeleton (loading), the Premium gate (`13003`), no-account (`12001`), no-debt (`12003`), not-closed
  (`12002`), or generic error states. No enlarge affordance can appear before the image exists (R6).
- The preview itself has no async: it renders the already-loaded raster. If `imageUrl` goes `null`
  while open (destination-switch refetch, error, base-dialog close), the preview closes and
  early-returns `null` (R6 guard) — no broken-image flash.
- No preview-specific error/empty state (nothing to load).

### Form validation rules

None — no forms, no inputs. The only "state" is the zoom/pan transform (clamped to `[MIN, MAX]` and
the pan bounds).

### Accessibility

- **Focus trap / restore / scroll-lock / `aria-modal` / Escape-stack** — inherited from
  `RadixDialog.Root/Portal/Overlay/Content`. Escape closes the **preview first** (nested Root), not the
  base QR dialog; focus returns to the trigger (the image surface or the badge) on close.
- **Named controls** — the two triggers (`wallet:qr.enlarge`), the zoom group (`role="group"` +
  `wallet:qr.zoomControls`), each zoom button (`wallet:qr.zoomIn/zoomOut/resetZoom`), the close button
  (`wallet:qr.close`). The `RadixDialog.Title` (visually hidden) provides the dialog name
  (`wallet:qr.previewTitle`).
- **Focusable viewport** — `.viewport` has `tabIndex 0` for keyboard `+`/`-`/`0`; a `:focus-visible`
  ring shows it's focused. Escape is NOT intercepted on the viewport (Radix owns it).
- **`:focus-visible` rings** on the enlarge surface, badge, viewport, zoom buttons, and close.
- **`prefers-reduced-motion`** — auto-neutralized (transform transition + overlay fade dropped);
  `.dragging` also disables the transition so drag/pinch tracks 1:1.
- **Color-independent** — controls are icon + accessible-name label, never color alone.
- **Triggers only when `isReady`** — no interaction is offered on a non-existent image.

### Tests (web-test-engineer — Vitest + RTL, MSW at the client boundary; vi-VN pinned)

Extend `src/features/wallet/qrDialog.test.tsx` (reuse `pngResponse()`, the `createObjectURL` spy, the
vi-VN pin, the `renderQr` helper):

1. **Open from the image surface** — with a ready expense QR, clicking the `.enlargeSurface` button
   (`name: /Phóng to mã QR/`) opens the preview; the preview dialog + a QR `<img>` inside it are
   present.
2. **Open from the expand badge** — clicking the `.enlargeBadge` button (same accessible name) opens
   the same preview.
3. **Escape closes the preview only** — with the preview open, pressing Escape closes the preview
   while the base QR dialog's image is still present (assert the base `<img name={/VietQR/}>` remains
   and the preview's title/viewport is gone). Guards the Escape-stack precedence (R5).
4. **Zoom-in raises `data-scale`; reset returns to 1** — click zoom-in → the preview `<img>`
   `data-scale` attribute increases above 1; click reset-to-fit → `data-scale` is back to `1`.
   (jsdom can't assert wheel/pinch geometry — buttons are the deterministic surface.)
5. **Same blob URL reused (no 2nd `createObjectURL`)** — spy `URL.createObjectURL`; open the QR, open
   the preview; assert `createObjectURL` was called exactly once (the preview reuses the object URL,
   R4). Assert the preview `<img src>` equals the base image's src.
6. **Revoke-on-unmount stays green** — the existing `QrDialog_Unmount_RevokesTheObjectUrl` test still
   passes (the preview never revokes; the base dialog owns it). Add an assertion that opening +
   closing the preview does NOT call `revokeObjectURL` (the URL is only revoked on base-dialog
   unmount/blob change).
7. **Locale key-in-sync** — `walletI18n.test.ts` already asserts vi-VN↔en-US parity + non-empty over
   the whole `wallet` namespace; the six new `qr.*` keys are covered by that structural test (no
   change needed beyond adding the keys to both files). Optionally add an explicit assertion that
   `qr.enlarge` / `qr.previewTitle` etc. exist in both.

> jsdom cannot exercise real wheel/pinch/pan geometry (no layout, no Pointer Events geometry). Those
> paths are covered via the button-driven `data-scale` test (#4) and an **optional Playwright E2E**
> (`e2e/`) for real pinch/pan on the dev server (vi-VN + Asia/Ho_Chi_Minh pinned).

### Verification checklist

- `pnpm lint` clean (oxlint type-aware).
- `tsc -b` type-checks.
- `pnpm build` succeeds.
- `pnpm test` green (existing `qrDialog.test.tsx` regressions + `walletI18n.test.ts` parity + the new
  preview specs).
- Manual dev-server check (`VITE_ENABLE_MOCKS=true` or against the backend): open the QR dialog for an
  expense and an event; open the preview from both triggers; verify wheel zoom toward cursor, mobile
  pinch (touch-action none — page doesn't scroll), drag-to-pan with clamping, the three zoom buttons,
  keyboard `+`/`-`/`0`, Escape closing the preview first; check **light + dark** themes (the QR ground
  stays white in both) and a tall composite (fit shows the whole sheet; zoom+pan reveals each member).
- Optional Playwright E2E for real pinch/pan geometry.

## Impact Analysis

- **APIs:** none — no request added; reuses the shipped QR blob. **Database / Infrastructure /
  Services:** none (FE only).
- **Frontend (files):**
  - **[NEW]** `features/wallet/components/QrPreviewDialog.tsx` (lightbox shell + local `useZoomPan`),
    `features/wallet/components/QrPreviewDialog.module.css`.
  - **[MOD]** `features/wallet/components/QrDialog.tsx` (`previewOpen` state + reset effects +
    `onEnlarge` + render `<QrPreviewDialog>` + the two triggers in the ready branch).
  - **[MOD]** `features/wallet/components/QrDialog.module.css` (`.enlargeSurface`, `.enlargeBadge` +
    focus rings).
  - **[MOD]** `features/wallet/components/icons.tsx` (`ExpandIcon`, `ZoomInIcon`, `ZoomOutIcon`,
    `FitIcon`).
  - **[MOD]** `styles/tokens.css` (one token `--fs-z-lightbox: 330`).
  - **[MOD]** `i18n/locales/{vi-VN,en-US}/wallet.json` (six new `qr.*` keys, parity kept).
  - **No `errors.ts` change; no new route/page; no new hook/query; no new dependency.**
- **Design system:** does NOT reuse `DialogContent` (D2 — z-index/fullscreen limitation); builds on raw
  Radix primitives it already depends on. Adds one z-index token to the design-system token file
  (`tokens.css` is ui-designer-owned — a **modest ui-designer touch** for the token + the lightbox CSS
  polish, focus rings, reduced-motion). Flag if the raw-Radix lightbox reveals a genuine reusable
  primitive gap (a future `Lightbox`/`Overlay` primitive — Future Improvement).
- **Data-fetching:** unchanged — no new query keys, no invalidation.
- **Tests:** extend `qrDialog.test.tsx`; `walletI18n.test.ts` covers the new keys; optional E2E.
- **Documentation:** this planning doc; the `m7-wallet-qr.md` QR feature doc may get a cross-reference
  note (optional).

## Decision Log

> All decided by the user before drafting; recorded here, NOT reopened.

### D1 — Two enlarge triggers, one preview
Both the QR `<img>` (clickable + keyboard-focusable via a `.enlargeSurface` transparent button) AND a
top-right `.enlargeBadge` icon-button open the same preview; both labeled `wallet:qr.enlarge`.
**Reason:** the image is the obvious affordance for touch/mouse; the badge is an explicit, discoverable
affordance and keeps the interaction a real `<button>` for a11y/keyboard.

### D2 — Raw `@radix-ui/react-dialog` primitives, not `DialogContent`
Build the preview on `RadixDialog.Root/Portal/Overlay/Content`. **Reason:** the design-system
`DialogContent` hardcodes the overlay at `--fs-z-overlay` (300) with no overlay className override and
no fullscreen size, so a second `DialogContent` backdrop would render BEHIND the base QR dialog's
content (310). Raw primitives still deliver focus-trap nesting, focus restore, scroll-lock,
`aria-modal`, and Escape-stack precedence, with an explicit higher z-index.
**Alternatives considered:** (a) extend `DialogContent` with an overlay className + fullscreen size —
larger blast radius on a shared primitive; deferred as a possible Future Improvement. (b) portal a
plain `<div>` — loses Radix's focus-trap/Escape-stack/scroll-lock for free.

### D3 — One new z-index token `--fs-z-lightbox: 330`
Between `--fs-z-popover` (320) and `--fs-z-toast` (400); the preview overlay + content both use it.
**Reason:** the lightbox must sit above the base modal (310) + portalled popovers (320) but below
toasts (400).

### D4 — Reuse the existing blob object-URL (no refetch)
The preview takes `imageUrl` as a prop; the blob + URL lifecycle stays owned by `QrDialog` (one
`createObjectURL` per blob; the preview never revokes). **Reason:** avoids a second network fetch + a
second object URL to manage; single ownership prevents double-revoke/leak bugs.

### D5 — Hand-rolled zoom/pan (no new dependency)
Pointer Events + CSS `transform`; state `{ scale, tx, ty }`, `MIN 1` / `MAX 4`; wheel zoom toward
cursor (`exp(-deltaY*0.0015)`), 1-pointer pan, 2-pointer pinch toward midpoint, buttons ×1.4 / reset,
keyboard `+`/`-`/`0`, clamped pan, reset-to-fit on open, jsdom guards. **Reason:** the stack is LOCKED;
a zoom/lightbox library is an Open Question we explicitly are NOT taking. Trade-off: more hand-written
interaction code + jsdom can't assert geometry (covered via buttons + optional E2E) — accepted to
avoid an unapproved dependency.

### D6 — White QR ground in both themes; `touch-action: none`
The preview `.ground` stays `#ffffff` (aspect-ratio 3/4) in light + dark so the QR still scans;
`.viewport { touch-action: none; overscroll-behavior: contain }` so mobile pinch/pan don't scroll the
page. **Reason:** matches the shipped `.qrFrame` scan-safety rule and enables reliable touch gestures.

## Progress Log

### 2026-07-22

- Started planning the QR image preview (lightbox) web feature. Read the planning template
  (`.claude/rules/rule.md` via the API mirror), `FairShareMonWeb/CLAUDE.md`, and a peer doc
  (`planning/settled-per-member.md`) for section style.
- Grounded the plan in the live SPA: `QrDialog.tsx` (blob object-URL lifecycle lines 99–111, the
  `isReady && imageUrl` image branch lines 312–328, `QrDialogInner` state machine), `QrDialog.module.css`
  (`.qrFrame` position:relative + white ground + aspect-ratio 3/4), the design-system `Dialog.tsx` +
  `Dialog.module.css` (overlay hardcoded `--fs-z-overlay`, content `--fs-z-modal`, no overlay className,
  no fullscreen — confirms D2), `styles/tokens.css` z-index block (300/310/320/400 gap → D3
  `--fs-z-lightbox: 330`), `icons.tsx` (inline SVG convention), the `wallet.json` `qr.*` keys (existing
  `close`/`imageAltExpense`/`imageAltEvent` reused), `qrDialog.test.tsx` (pngResponse + createObjectURL
  spy + vi-VN pin), and `walletI18n.test.ts` (parity enforcement).
- Recorded the six user-resolved Open Questions as decisions D1–D6; wrote the Implementation Plan
  (token → icons → `QrPreviewDialog` + `useZoomPan` → CSS → `QrDialog` wiring → `QrDialog.module.css` →
  i18n), the a11y + state + endpoint notes, and the test list.
- Confirmed **no conflicts** with the plan (see Final Outcome): the shipped code matches every premise —
  `.qrFrame` is already `position: relative` (anchors the overlay button/badge with no new wrapper), the
  blob URL is a stable `imageUrl` string ready to pass as a prop, and the `DialogContent` z-index
  limitation is real (overlay at 300, no override). One thing to verify at build time: whether a
  `VisuallyHidden` primitive is exported from `@/components/ui` (reuse it) or a local `.srOnly` class is
  needed for the required `RadixDialog.Title` — flagged in Step 3.
- Status: **plan finalized; ready for implementation** (Open Questions pre-resolved by the user).

### 2026-07-22 — Implementation (frontend engineer)

Implemented the feature faithfully to the plan (Steps 1–7); no deviations, no new Open Questions.

- **Step 1** — `styles/tokens.css`: added `--fs-z-lightbox: 330;` between `--fs-z-popover` (320) and
  `--fs-z-toast` (400).
- **Step 2** — `features/wallet/components/icons.tsx`: added `ExpandIcon`, `ZoomInIcon`, `ZoomOutIcon`,
  `FitIcon` (inline `aria-hidden` SVGs, `viewBox="0 0 20 20"`, `stroke="currentColor"`, 20×20).
- **Step 3** — **[NEW]** `features/wallet/components/QrPreviewDialog.tsx`: raw Radix
  Root/Portal/Overlay/Content lightbox + local `useZoomPan(open)` hook (state `{ scale, tx, ty }`,
  `MIN 1`/`MAX 4`, wheel zoom-toward-cursor `exp(-deltaY*0.0015)`, 1-pointer pan / 2-pointer pinch
  toward midpoint, buttons ×1.4 / reset-to-fit, keyboard `+`/`-`/`0` — Escape left to Radix, pan clamp
  `±(scale-1)*rect/2`, reset-to-fit on open, jsdom guards on `setPointerCapture?.` + zero-size
  `getBoundingClientRect`). R6 early-close guard (`open && imageUrl == null` → fire `onOpenChange(false)`
  + render `null`; Root `open={open && imageUrl != null}`). `data-scale` test hook on the `<img>`.
  **VisuallyHidden:** no such primitive is exported from `@/components/ui`; used the codebase's existing
  global utility class `fs-visually-hidden` (as `Spinner` does) directly on `RadixDialog.Title` — no
  fork of the design system, no new local class needed.
- **Step 4** — **[NEW]** `features/wallet/components/QrPreviewDialog.module.css`: `.overlay`/`.content`
  at `var(--fs-z-lightbox)`, `.viewport` (`touch-action:none; overscroll-behavior:contain`, focusable
  ring), `.ground` (`#ffffff`, `aspect-ratio:3/4` — white in both themes), `.image`
  (transform + short transition; `.dragging .image { transition:none }`), `.toolbar` (bottom-centre
  chip, role=group), `.close` (top-right), reduced-motion neutralizes animation + transition.
- **Step 5** — `features/wallet/components/QrDialog.tsx`: `previewOpen` state; reset on base-close (in
  the existing `!open` effect) AND on `imageUrl → null` (new effect); `onEnlarge` passed to
  `QrDialogInner`; `<QrPreviewDialog>` rendered as a sibling of `<DialogContent>` inside the same
  `<Dialog>`; in the `isReady && imageUrl` branch, `.enlargeSurface` (transparent full-cover button) +
  `.enlargeBadge` (top-right `<ExpandIcon>` chip), both `aria-label={t("wallet:qr.enlarge")}`.
- **Step 6** — `features/wallet/components/QrDialog.module.css`: `.enlargeSurface` (inset:0 transparent
  button, inset focus ring, `cursor:zoom-in`) + `.enlargeBadge` (surface+border+shadow chip, focus ring).
- **Step 7** — `i18n/locales/{vi-VN,en-US}/wallet.json`: added the six `qr.*` keys per the table, both
  locales in sync; `imageAltExpense`/`imageAltEvent`/`close` reused.

Verification: `pnpm exec tsc -b` clean; `pnpm lint` exit 0 (only pre-existing `only-export-components`
warnings in unrelated files); `pnpm build` succeeds. Existing `qrDialog.test.tsx` (14) +
`walletI18n.test.ts` (12, now covering the six new keys) both green — 26 passed, no product-test changes.
Drove the real app (Playwright + MSW, PREMIUM seed user `admin`): opened the QR dialog, opened the
preview from BOTH triggers, zoom-in raised `data-scale` above 1, reset returned it to `1`, and Escape
closed the preview while the base dialog's QR `<img>` survived (Escape-stack precedence).

### 2026-07-22 — Tests (web-test-engineer)

Added a `describe("QrDialog QR preview")` block to `src/features/wallet/qrDialog.test.tsx` (extends
the existing harness — `pngResponse()`, `createObjectURL` spy, vi-VN pin, `renderQr()`,
`seedSession("PREMIUM")`, `findByRole("img",{name:/VietQR/})` ready gate). Ten new specs, all green;
no product code touched. The preview `<img data-scale>` is the deterministic surface (jsdom has no
layout / Pointer geometry, so wheel/pinch/pan is left to E2E — noted below).

- `QrPreview_ReadyImage_ExposesTwoEnlargeTriggers` — the ready branch shows exactly two
  `/Phóng to mã QR/` buttons (the `.enlargeSurface` + `.enlargeBadge`, D1).
- `QrPreview_ClickImageSurface_OpensPreview` — clicking the first trigger opens the lightbox (its
  labeled zoom-control `role="group"` "Điều khiển thu phóng" appears).
- `QrPreview_ClickExpandBadge_OpensPreview` — clicking the second trigger opens the same preview.
- `QrPreview_Escape_ClosesPreviewOnly_BaseDialogStaysOpen` — Escape closes the preview (group gone)
  while the base QR `<img>` (`/VietQR/`) survives — Radix Escape-stack precedence (R5).
- `QrPreview_ZoomInThenReset_DrivesDataScale` — opens at `data-scale="1"`; "Phóng to" (zoom-in)
  raises it above 1; "Khôi phục vừa khung" (reset) returns it to `"1"` (R2, button-driven).
- `QrPreview_ZoomOut_StaysClampedAtFit` — zoom-out from the fit floor clamps at `1` (MIN, never < 1).
- `QrPreview_Open_ReusesSameBlobUrl_NoSecondCreateObjectUrl` — opening the preview adds NO
  `createObjectURL` call and the preview raster `src` equals the base blob URL (R4).
- `QrPreview_OpenThenClose_NeverRevokesTheObjectUrl` — opening + Escaping the preview never calls
  `revokeObjectURL` (the base dialog owns the lifecycle, R4/R6).
- `QrPreview_EventKind_UsesEventImageAlt` — for `kind="event"` the preview img reuses
  `wallet:qr.imageAltEvent` (R3).
- `QrPreviewKeys_ExistInBothLocales_NonEmpty` (extra) — asserts the six new `qr.*` keys are present
  and non-empty in both vi-VN + en-US, on top of the structural parity in `walletI18n.test.ts`.

Verification: `pnpm exec vitest run qrDialog.test.tsx walletI18n.test.ts` → **36 passed** (26 existing
kept green — incl. `QrDialog_Unmount_RevokesTheObjectUrl` — + 10 new). Full suite `pnpm test` →
**901 passed / 107 files**. `tsc -b` clean. `pnpm lint` exit 0 (only pre-existing
`only-export-components` warnings in unrelated files; none in the wallet/test files). No product bug
found. jsdom cannot exercise real wheel/pinch/pan geometry (zero-size `getBoundingClientRect`, no
PointerEvent geometry) — the zoom state machine is covered via the on-screen buttons; real pinch/pan
remains E2E territory (no `fireEvent.wheel` smoke added, as it would assert nothing deterministic in
jsdom).

### 2026-07-22 — Review (web-code-reviewer)

Clean review — no blocker/high/medium findings; D1–D6 all implemented as specified. `useZoomPan`
math (clamps, zoom-toward-cursor, pinch, pointer cleanup, jsdom guards), Radix nesting/layering, blob
lifecycle, a11y, i18n, and tokens all verified sound. Post-review fixes applied:
- **Deleted** the throwaway `e2e/zz-qr-preview-verify.spec.ts` (untracked implementer artifact — never committed).
- `.toolbar` radius `--fs-radius-full` (50% → ellipse) → **`--fs-radius-pill`** (stadium).
- Corrected the `QrPreviewDialog.module.css` header comment (the scrim is fixed-dark; the toolbar/close
  chrome is theme-dependent). No functional change.

Re-verified after fixes: `pnpm build` succeeds, wallet suite **116 passed**, `tsc -b`/lint clean.

## Final Outcome

Implemented, reviewed, and verified 2026-07-22. All seven plan steps landed with no deviations and no
new Open Questions. Files: **[NEW]** `QrPreviewDialog.tsx`, `QrPreviewDialog.module.css`; **[MOD]** `QrDialog.tsx`,
`QrDialog.module.css`, `icons.tsx`, `styles/tokens.css`, `i18n/locales/{vi-VN,en-US}/wallet.json`. No
API/hook/query/`errors.ts`/dependency change (presentation-only, reuses the shipped blob object-URL).

Notes for the web-test-engineer / reviewer:
1. **`data-scale` test hook** is on the preview `<img>` (`img[data-scale]`) — the deterministic surface
   for the zoom-in/reset assertions (jsdom can't do wheel/pinch geometry).
2. **Two enlarge triggers, same accessible name** `wallet:qr.enlarge` ("Phóng to mã QR"): `.enlargeSurface`
   (DOM order first) and `.enlargeBadge` (second) — `getByRole("button", { name: /Phóng to mã QR/ })`
   returns **2**; scope with `.first()` / `.last()` or the class.
3. **VisuallyHidden:** used the existing global `fs-visually-hidden` class on `RadixDialog.Title`
   (no `@/components/ui` VisuallyHidden export exists; matches the `Spinner` pattern) — no design-system fork.
4. `QrDialogInner` gained a required `onEnlarge` prop, but it is an internal (non-exported) component the
   tests don't instantiate directly, so the public `QrDialog` contract is unchanged.

No conflicts found between the shipped code and the decided design. Notes for the implementer: (1)
confirm whether `@/components/ui` exports a `VisuallyHidden` primitive for the required
`RadixDialog.Title` (reuse it; else add a local `.srOnly` class — do not fork the design system); (2)
the base QR dialog uses `size="sm"` on `DialogContent` — the preview is intentionally NOT a
`DialogContent` (D2); (3) keep the `previewOpen` reset effects (base-close + `imageUrl → null`) in sync
with the preview's own R6 early-close guard so both cover the destination-switch refetch race.

## Future Improvements

- **Promote the lightbox to a reusable design-system primitive** (a `Lightbox`/`MediaOverlay` on raw
  Radix with an overlay-className + fullscreen contract) if a second media-preview surface appears —
  or extend `DialogContent` with an overlay className + `size="fullscreen"` so future nested modals
  don't each hand-roll z-index (would retire part of D2's rationale).
- **Double-tap / double-click to zoom** toward the tapped point (a common lightbox gesture) once the
  Pointer model is in place.
- **Per-member QR extraction** — if the backend ever returns individual per-member QR images (not one
  composite raster), the preview could paginate/scroll per member instead of zoom+pan a single raster.
- **Optional Playwright E2E** for real pinch/pan/wheel geometry (jsdom can't assert it) — promote the
  "optional" E2E in the test plan to a standing spec if the interaction regresses.
- **Reduced-data / print affordance** — a "download this member's QR" or print action from within the
  preview.
