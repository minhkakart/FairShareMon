# Per-member QR sharing — one own QR per still-owing member, in the QR dialog + lightbox — Web

Swap the shared VietQR dialog (`QrDialog`) off the single **composite** PNG and onto the backend's
new **per-member** JSON endpoints: each still-owing member gets their **OWN** single QR (a
`data:image/png;base64,…` data URL rendered server-side). The QR dialog's preview pane shows the
**first** member's QR with a caption (name + amount + a "1/N" indicator + an enlarge hint); enlarging
opens the existing `yet-another-react-lightbox` (YARL) preview as a **multi-slide carousel** across ALL
members (swipe / arrows), each slide captioned with the member's name + amount. Two per-member actions
are added — **Download current** (that member's QR PNG, `qr-{memberName}.png`) and **Share current**
(Web Share API with the QR image file) — on both the dialog (the shown member) and the lightbox (the
active slide). The old composite `…/qr` blob path is dropped from the web.

This is a **frontend data-shape + presentation** change. It calls the two NEW JSON endpoints
(no new dependency — YARL is already installed with its Captions / Counter / Download / Share / Zoom
plugins), touches no business rule, and keeps the `QrDialog` public contract (`{ open, onOpenChange,
kind, targetUuid, title }`) unchanged so the expense/event detail call sites are untouched.

> **Relationship to prior docs.** This supersedes the **composite** QR-image plumbing shipped in
> `planning/m7-wallet-qr.md` (the `api.blob` QR path + object-URL lifecycle) and extends
> `planning/qr-image-preview.md` (the YARL lightbox, D7): the lightbox goes from a **single** slide to a
> **multi-slide** carousel, and gains Captions + per-member Download/Share. It consumes the backend
> shipped in `FairShareMonApi/planning/per-member-qr-sharing.md`.

## Objective

For a Premium user opening the QR dialog on an expense or a closed event:

- Fetch the **list** of still-owing members and their own QR data URLs from the new endpoints instead of
  one composite PNG blob.
- Show the **first** member's QR in the dialog with a caption (member name + `formatMoneyVnd(amount)`,
  a "1/N" indicator, an "N members" count, and a hint to enlarge to see everyone).
- Enlarge → open the YARL lightbox as a **multi-slide carousel** across all members (prev/next + native
  swipe), each slide captioned with the member's name + amount, positioned at the currently-shown member
  (index 0 by default).
- Offer **Download current** (`qr-{memberName}.png`) and **Share current** (Web Share API with the QR
  image file) on both surfaces — the dialog acts on the shown member, the lightbox on the active slide.
- Preserve every existing state (Premium gate, no-account, no-debt, not-closed, generic error, ownership
  404 → close+toast), the destination picker, and the "copy details" affordance.

## Background

Confirmed against the live SPA (2026-07-22):

- **Backend contract (shipped — `FairShareMonApi/planning/per-member-qr-sharing.md`):**
  - `GET /api/v1/expenses/{uuid}/qr/members?bankAccountUuid={uuid?}` → `ApiResult<MemberQrResponse[]>`.
  - `GET /api/v1/events/{uuid}/qr/members?bankAccountUuid={uuid?}` → `ApiResult<MemberQrResponse[]>`
    (closed-only; open event → 12002).
  - `MemberQrResponse` (camelCase JSON): `{ memberUuid: string; memberName: string; amount: number;
    image: string }`, `image` = `data:image/png;base64,<…>` (each member's OWN single QR PNG, ready for
    an `<img src>`).
  - Same error/gating contract as the composite `…/qr`: Premium gate `403 13003`; no account `400
    12001`; empty billed set `400 12003`; ownership `404` (`6000` expense / `9000` event); event open
    `400 12002`; owned override miss `404 12000`.
  - The composite `GET …/qr` (single PNG blob) endpoints still exist but the web STOPS using them.
- **`QrDialog`** (`src/features/wallet/components/QrDialog.tsx`) is the shared expense/event QR modal
  built on the design-system `DialogContent` (`size="sm"`). Today it:
  - fetches ONE blob via `useExpenseQrQuery`/`useEventQrQuery` (TanStack Query, `retry:false`,
    `gcTime:0`), then runs a **blob object-URL lifecycle** (`URL.createObjectURL` on new data,
    `revokeObjectURL` on unmount / blob change) into `imageUrl` (lines 111–130);
  - `isReady = qrQuery.isSuccess && imageUrl != null`;
  - runs the error-code → state machine in `QrDialogInner`: Premium gate (`13003` or Free session),
    no-account (`12001`), no-debt (`12003`, kind-aware copy), not-closed (`12002`), generic error, then
    loading | ready; ownership `404` (`6000`/`9000`) → close + toast via a one-shot ref;
  - shows a destination `Select` when `accounts.length >= 2` (picking a non-default account drives
    `?bankAccountUuid=` on the refetch);
  - renders the `<img>` in `.qrWell > .qrFrame.<kind>` with two enlarge triggers (`.enlargeSurface`
    full-cover button + `.enlargeBadge` top-right chip, both `aria-label` `wallet:qr.enlarge`);
  - footer: Close / `CopyDetailsButton` (copies holder + number + bank, NOT the raw payload) / Download
    (via `downloadBlob(qrQuery.data, fallbackName)`).
- **`QrPreviewDialog`** (`src/features/wallet/components/QrPreviewDialog.tsx`) is the YARL lightbox:
  props `{ open, onOpenChange, imageUrl, kind }`, **one** slide, `plugins={[Zoom]}`, localized `labels`
  (`Lightbox`→`qr.previewTitle`, `Close`→`qr.close`, zoom in/out), `render` hides prev/next (single
  image), portal z-index pinned to `--fs-z-lightbox` (330) via
  `styles.root["--yarl__portal_zindex"]`, a **window-capture** Escape interceptor (closes the preview
  first, not the base Radix dialog), and a scoped white-ground rule
  (`.lightbox :global(.yarl__slide_image){ background-color:#fff }`) in `QrPreviewDialog.module.css`.
- **YARL** (`yet-another-react-lightbox@3.32.1`, already a dependency) ships, in the SAME package
  (verified in `node_modules/.../package.json` exports), the plugins we need: `plugins/captions`
  (+ `captions.css`), `plugins/counter` (+ `counter.css`), `plugins/download`, `plugins/share` (exports
  `isShareSupported()`), and `plugins/zoom` (in use). **No new dependency.** The Download plugin accepts
  a custom `download({ slide, saveAs })` function and per-slide `download:{url,filename}`; the Share
  plugin accepts a custom `share({ slide })` function and auto-hides its button when `isShareSupported()`
  is false.
- **Data client** (`src/lib/api/client.ts`): `api.get<T>` unwraps `ApiResult<T>` → `T` and throws a
  typed `ApiError` (numeric `code`) on failure; `api.blob` returns a `BlobResult`. `buildUrl` drops
  `undefined`/`null` query values, so `{ query: { bankAccountUuid } }` omits the param for the implicit
  default account.
- **`downloadBlob`** (`src/lib/download/downloadBlob.ts`): `downloadBlob(result: BlobResult,
  fallbackName)` — clicks a hidden `<a download>` using `result.filename ?? fallbackName`. Reusable for
  a per-member PNG once we wrap a `Blob` as a `BlobResult`.
- **Money** via `formatMoneyVnd` (`src/i18n/format.ts`) — vi-VN grouping, 0 fraction digits; render the
  API value, never do float math.
- **Error codes** exist in `src/lib/api/errors.ts`: `ExpenseNotFound 6000`, `EventNotFound 9000`,
  `BankAccountNotFound 12000`, `NoBankAccountForQr 12001`, `EventNotClosedForQr 12002`,
  `NoOutstandingDebtForQr 12003`, `PremiumFeatureRequired 13003` — all reused, no `errors.ts` change.
- **i18n:** `src/i18n/locales/{vi-VN,en-US}/wallet.json` under `qr.*`. Existing keys: `close`, `enlarge`,
  `previewTitle`, `zoomIn`, `zoomOut`, `imageAltExpense`, `imageAltEvent`, `download`,
  `downloadNameExpense`, `downloadNameEvent`, `copyDetails`, `copied`, `noAccount*`, `noDebt*`,
  `notClosed*`, `error*`, `retry`, `destinationLabel`, `bank`, `accountNumber`, `holder`. Parity +
  non-empty is enforced by `src/features/wallet/walletI18n.test.ts`.
- **Consumers (grep-before-change):** the ONLY product consumer of `qrApi.expenseQr`/`eventQr` +
  `useExpenseQrQuery`/`useEventQrQuery` is `QrDialog.tsx`. `src/features/wallet/useQr.test.tsx` tests the
  blob hooks directly (must be rewritten). `expensesApi.ts`/`eventsApi.ts` use `api.blob` for **CSV
  export**, not QR. `M7Showcase.tsx`/`StyleGuide.tsx` render `QrDialog` via its public props only.
- **Tests:** `src/features/wallet/qrDialog.test.tsx` (MSW; today mocks the PNG blob at `…/qr` + spies
  `URL.createObjectURL`/`revokeObjectURL`; queries the lightbox by `role="dialog"` name "Xem mã QR phóng
  to"; `pngResponse()` 1×1 PNG). Must be reworked for the JSON list + data URLs.

## Requirements

### Functional

1. **R1 — Per-member data.** Both QR kinds fetch `MemberQrResponse[]` from the new `…/qr/members`
   endpoints via the JSON `api.get` path (NOT `api.blob`). No composite blob, no object URL.
2. **R2 — Dialog shows the first member.** In the ready state the dialog renders `members[0].image`
   (a data URL, straight into `<img src>`) with a caption: `members[0].memberName`,
   `formatMoneyVnd(members[0].amount)`, a "1/N" indicator, an "N members" count, and an enlarge hint.
   The two enlarge triggers (`.enlargeSurface` + `.enlargeBadge`, `wallet:qr.enlarge`) stay and open the
   lightbox at index 0.
3. **R3 — Lightbox multi-slide.** Enlarge opens the YARL lightbox with one slide per member
   (`slides = members.map(...)`), prev/next enabled + native swipe when `members.length > 1` (hidden
   when `=== 1`, as today), each slide captioned with the member's name + amount (Captions plugin) and an
   index indicator (Counter plugin). Starts at the shown member (index 0). Keep Zoom, the white ground,
   `--fs-z-lightbox` portal layering, and the window-capture Escape interceptor.
4. **R4 — Download current.** On both surfaces, download the current member's QR PNG as
   `qr-{memberName}.png`: convert the data URL → `Blob` and reuse `downloadBlob`. Dialog → `members[0]`;
   lightbox → the active slide.
5. **R5 — Share current (Web Share API).** On both surfaces, share the current member's QR image:
   data URL → `Blob` → `File`, then `navigator.share({ files:[file], title, text })`, guarded by
   `navigator.canShare?.({ files:[file] })`. The control is **hidden** (never a dead button) when the
   Web Share API / file-share is unsupported. No "download all", no view-only.
6. **R6 — Composite dropped.** No composite toggle; the web no longer calls `GET …/qr`. Remove the blob
   `qrApi.expenseQr`/`eventQr` + `useExpenseQrQuery`/`useEventQrQuery` (grep-confirmed single consumer)
   and the object-URL lifecycle in `QrDialog`.
7. **R7 — All existing states preserved.** Premium gate (`13003` / Free session), no-account (`12001`),
   no-debt (`12003`, kind-aware), not-closed (`12002`), generic error + retry, ownership `404`
   (`6000`/`9000`) → close+toast one-shot, the destination picker (`?bankAccountUuid=` refetch), and
   `CopyDetailsButton` all behave exactly as today. `isReady` becomes `qrQuery.isSuccess &&
   members.length > 0` (the backend `12003`s an empty set first, so a success always has ≥1 member).
8. **R8 — Loading / empty / error.** Loading = the existing `Skeleton` in the fixed-aspect frame. Empty
   cannot occur (12003 shows the no-debt state). If `members` becomes empty/undefined while the lightbox
   is open (destination refetch race), the lightbox early-returns `null` and `previewOpen` resets.

### Non-functional / conventions

- **No new dependency** (YARL + all needed plugins already installed). No new route/page. No
  `errors.ts` change. No business-rule change.
- **All copy through i18n**, vi-VN authoritative + en-US parity (`walletI18n.test.ts` enforces
  key-in-sync + non-empty).
- **Data URLs need no object URL** — remove `createObjectURL`/`revokeObjectURL` for the QR image
  (a genuine lifecycle simplification). The Download path still creates a transient object URL inside
  `downloadBlob`, which it revokes itself.
- **a11y:** every control labeled; each lightbox slide named via its caption; the Share control is
  labeled AND hidden when unsupported; keep the Escape-first behavior and reduced-motion (YARL-owned).
- **Money** via `formatMoneyVnd`; **theme:** QR ground stays `#ffffff` in both themes.
- **React 19 + React Compiler** — idiomatic components, no hand-added `useMemo`/`useCallback`.

## Open Questions

> Resolved product decisions are recorded in the Decision Log (D1–D8) and NOT reopened. The items below
> are genuinely unresolved sub-choices; each carries a recommendation for the checkpoint.

### OQ1 — Lightbox Download/Share: YARL's built-in plugins vs fully custom toolbar buttons

The lightbox needs a per-slide Download and Share control. YARL ships `plugins/download` and
`plugins/share` (both in the installed package). Options:

- **(a) Use YARL's Download + Share plugins with CUSTOM functions (recommended).** Register both
  plugins; supply `download={{ download: ({ slide, saveAs }) => downloadCurrent(slide) }}` and
  `share={{ share: ({ slide }) => shareCurrent(slide) }}` so OUR helpers do the per-member data-URL →
  `Blob`/`File` work, while YARL provides the toolbar buttons, the localized labels (via `labels`
  `Download`/`Share`), and — critically — the Share button's automatic hide when
  `isShareSupported()` is false. Trade-off: two more plugins wired in; the built-in per-slide
  `share:{url,text}` shape can't share a *file*, so we MUST use the custom `share` function form
  (which we want anyway).
- **(b) Fully custom toolbar buttons via `render.toolbar`.** Render our own two `<button>`s into the
  YARL toolbar and resolve the active slide via a `view` callback / controller ref. Trade-off: full
  control + one shared code path with the dialog footer, but we re-implement the button chrome, labels,
  and the Share auto-hide that (a) gets for free; more code, more a11y surface to get right.

**Recommendation: (a)** — reuse YARL's accessible, tested toolbar + `isShareSupported()` auto-hide, and
put the per-member `Blob`/`File` logic in shared helpers (`downloadMemberQr`, `shareMemberQr`) that BOTH
the plugins' custom functions AND the dialog footer buttons call.

### OQ2 — Per-member alt text: repurpose the existing alt keys vs add new ones

The existing `qr.imageAltExpense`/`imageAltEvent` copy describes a **composite** ("Mã QR VietQR **tổng
hợp** … một mã cho mỗi thành viên"), which is now factually **wrong** — each image is one member's own
single QR. Options:

- **(a) Repurpose the two existing keys, rewrite the copy to interpolate `{{name}}` (recommended).**
  e.g. vi `"Mã QR VietQR để {{name}} chuyển khoản phần còn nợ của phiếu chi tiêu này."` /
  event `"… để {{name}} chuyển khoản phần còn nợ của đợt."`. Keeps the key names stable and keeps the
  substring "VietQR" (so existing image matchers still work). Trade-off: the keys now REQUIRE a `{{name}}`
  param — every call site must pass it.
- **(b) Add new keys `qr.imageAltMemberExpense`/`imageAltMemberEvent`** and leave the old composite keys
  in place (or delete them). Trade-off: cleaner separation, but two near-duplicate key families and a
  grep-before-delete step for the old ones.

**Recommendation: (a)** — the old copy is wrong and must change regardless; repurposing avoids key churn.

### OQ3 — Download filename for Vietnamese member names

The filename is `qr-{memberName}.png`; Vietnamese names carry spaces + diacritics (e.g. `qr-Nguyễn Văn
Minh.png`). Options:

- **(a) Raw name with only path-illegal chars stripped (recommended).** Replace `/ \ : * ? " < > |` and
  control chars with `-`; keep spaces + diacritics. Modern browsers accept UTF-8 download filenames.
  Trade-off: a rare OS may transliterate on save.
- **(b) ASCII-slugify** (strip diacritics, spaces → `-`, e.g. `qr-nguyen-van-minh.png`). Trade-off:
  universally safe, but loses the readable Vietnamese name and needs a diacritics-folding step.

**Recommendation: (a)** — readable, matches the brief's literal `qr-{memberName}.png`, minimal code.

### OQ4 — Dialog footer action crowding (Close / Copy details / Download / Share)

Adding Share to the footer makes four controls (Close, Copy details, Download current, Share current),
which may crowd `size="sm"` on narrow mobile. Options:

- **(a) Keep all four in the footer, wrapping on small widths (recommended).** Trade-off: the footer can
  wrap to two rows on the narrowest screens — acceptable and simplest.
- **(b) Move Download + Share into the QR frame as small icon buttons** (next to `.enlargeBadge`),
  leaving Close + Copy details in the footer. Trade-off: tidier footer, but more chrome over the QR
  image and more CSS; the icon buttons must stay clear of the enlarge surface/badge hit areas.
- **(c) Share only in the lightbox** (dialog footer keeps Close / Copy details / Download). Trade-off:
  fewer footer buttons, but the brief calls for Share on "both" surfaces — this narrows it.

**Recommendation: (a)** — least surprise, honors "Download current + Share current on both surfaces."

## Assumptions

- The backend returns members in a stable, meaningful order (billing order — API Decision 5); the web
  renders `members[0]` as "the first" and preserves the array order in the carousel. No client sort.
- `MemberQrResponse.image` is always a `data:image/png;base64,<…>` string; `atob` + `Blob`/`File` are
  available in jsdom for the download/share helpers (they are).
- Member counts are small (single digits to low tens) — rendering N data-URL `<img>`s (YARL only mounts
  the active + adjacent slides) and holding the array in the query cache is fine; no pagination.
- The destination picker sits behind the full-viewport lightbox, so a destination change while the
  lightbox is open is not a normal interaction; we still guard the refetch race (R8).
- `CopyDetailsButton` stays account-level (holder + number + bank), NOT per-member — unchanged.
- React 19 + React Compiler is in effect; the "current member" is derived state (dialog = index 0;
  lightbox = the slide YARL hands our custom download/share functions), no manual memoization.

## Implementation Plan

> Paths under `FairShareMonWeb/src/`. **[MOD]** modifies a shipped file; **[NEW]** creates one.

### Step 1 — Types **[MOD]** `features/wallet/api/types.ts`

Add the DTO mirroring the backend (camelCase):

```ts
/** One still-owing member's OWN single VietQR (per-member QR endpoints). `image`
 *  is a `data:image/png;base64,<…>` data URL, ready for an `<img src>`. */
export interface MemberQrResponse {
  memberUuid: string;
  memberName: string;
  amount: number;
  image: string;
}
```

### Step 2 — Data layer **[MOD]** `features/wallet/api/qrApi.ts`

Replace the blob methods with the JSON per-member methods (grep confirms `QrDialog` is the only product
consumer of the old ones):

```ts
expenseMemberQrs: (uuid: string, bankAccountUuid?: string) =>
  api.get<MemberQrResponse[]>(`/v1/expenses/${uuid}/qr/members`, { query: { bankAccountUuid } }),
eventMemberQrs: (uuid: string, bankAccountUuid?: string) =>
  api.get<MemberQrResponse[]>(`/v1/events/${uuid}/qr/members`, { query: { bankAccountUuid } }),
```

Remove `expenseQr`/`eventQr` (`api.blob`) and their `BlobResult` import. Update the file doc comment
(no longer a blob path; per-member data URLs).

### Step 3 — Hooks **[MOD]** `features/wallet/hooks/useQr.ts`

Replace the two blob hooks with `useExpenseMemberQrsQuery` / `useEventMemberQrsQuery` returning
`MemberQrResponse[]`, same shape (`enabled` gate, `retry:false`, `gcTime:0`) and query keys
(`["qr","expense",uuid,bankAccountUuid??null]` etc., can keep the same key roots or suffix `"members"`
— suffix to avoid any stale cache confusion):

```ts
export function useExpenseMemberQrsQuery(uuid, bankAccountUuid, enabled) {
  return useQuery<MemberQrResponse[]>({
    queryKey: ["qr","expense","members",uuid,bankAccountUuid ?? null],
    queryFn: () => qrApi.expenseMemberQrs(uuid, bankAccountUuid),
    enabled, retry: false, gcTime: 0,
  });
}
// …useEventMemberQrsQuery mirror for events.
```

### Step 4 — Shared per-member action helpers **[NEW]** `features/wallet/qrShare.ts`

Small pure helpers reused by the dialog footer AND the lightbox plugins (OQ1a):

- `dataUrlToBlob(dataUrl: string): Blob` — split on `,`, `atob` the base64 body, fill a `Uint8Array`,
  `new Blob([bytes], { type: "image/png" })`.
- `qrFileName(memberName: string): string` — `qr-${sanitize(memberName)}.png` (OQ3a sanitize:
  replace `/[\\/:*?"<>|\x00-\x1f]/g` with `-`).
- `downloadMemberQr(member: MemberQrResponse): void` — `downloadBlob({ blob: dataUrlToBlob(member.image),
  filename: null, contentType: "image/png" }, qrFileName(member.memberName))`.
- `canShareMemberQr(member): boolean` — feature-detect: `typeof navigator.share === "function"` and,
  when `navigator.canShare` exists, `navigator.canShare({ files:[<File>] })`.
- `shareMemberQr(member, title, text): Promise<void>` — build the `File`, guard `canShareMemberQr`, then
  `await navigator.share({ files:[file], title, text })` (swallow `AbortError`).

### Step 5 — **[MOD]** `features/wallet/components/QrDialog.tsx`

- Swap the queries to `useExpenseMemberQrsQuery` / `useEventMemberQrsQuery`; `data` is
  `MemberQrResponse[]`. Delete the blob object-URL effect (lines ~111–123) and the `imageUrl`
  state/effect; the QR image now comes straight from `members[0].image` (a data URL).
- `const members = qrQuery.data ?? []; const current = members[0];`
- `isReady = qrQuery.isSuccess && members.length > 0`.
- Reset `previewOpen` when the dialog closes (existing `!open` effect) AND when `members.length === 0`
  (replaces the `imageUrl == null` reset) — belt-and-suspenders with the lightbox's own guard (R8).
- **Footer:** keep Close + `CopyDetailsButton`. Replace the composite Download with **Download current**
  (`onClick={() => downloadMemberQr(current)}`, still `iconStart={<DownloadIcon/>}`,
  `wallet:qr.download`). Add **Share current** — render only when `canShareMemberQr(current)` (OQ4a),
  `iconStart={<ShareIcon/>}`, `wallet:qr.share`, `onClick` calls `shareMemberQr(current,
  t("wallet:qr.shareTitle"), t("wallet:qr.shareText", { name: current.memberName, amount:
  formatMoneyVnd(current.amount) }))`.
- Pass `members` (not `imageUrl`) and `kind` to `<QrPreviewDialog>`; drop the `imageUrl` prop.
- **`QrDialogInner`** ready branch: render `<img src={current.image}
  alt={t(kind === "expense" ? "wallet:qr.imageAltExpense" : "wallet:qr.imageAltEvent", { name:
  current.memberName })} />` (OQ2a — the alt now takes `{{name}}`). Below the frame add a
  `.qrCaption` block: member name, `formatMoneyVnd(amount)`, the "1/N" indicator
  (`t("wallet:qr.slideCounter", { index: 1, total: members.length })`), the count
  (`t("wallet:qr.memberCount", { count: members.length })`), and the enlarge hint
  (`t("wallet:qr.enlargeHint")`). Keep the two enlarge triggers → `onEnlarge` opens the lightbox at
  index 0.
- Remove the now-unused `downloadNameExpense`/`downloadNameEvent` fallback wiring (see Step 8).

### Step 6 — **[MOD]** `features/wallet/components/QrPreviewDialog.tsx`

- Props become `{ open, onOpenChange, members: MemberQrResponse[], kind, startIndex?: number }`.
- `isOpen = open && members.length > 0`; the R6 guard fires `onOpenChange(false)` when `open &&
  members.length === 0`.
- Import + register `Captions`, `Counter`, `Download`, `Share` plugins (alongside `Zoom`), and their CSS
  (`captions.css`, `counter.css`). `slides = members.map((m) => ({ src: m.image, alt: t(altKey, { name:
  m.memberName }), title: m.memberName, description: formatMoneyVnd(m.amount),
  download: { url: m.image, filename: qrFileName(m.memberName) } }))`.
- `index={startIndex ?? 0}` (YARL controlled index prop / `on={{ view }}` not required — the plugins hand
  us the active slide).
- Prev/next: keep `render={{ buttonPrev: () => null, buttonNext: () => null }}` ONLY when
  `members.length === 1`; when `> 1`, allow YARL's default prev/next (finite carousel) + native swipe.
- Download plugin: `download={{ download: ({ slide }) => downloadMemberQrFromSlide(slide) }}` (or rely on
  the per-slide `download` prop above and skip the custom fn — the per-slide `{url,filename}` reuses
  YARL's `saveAs`). Share plugin: `share={{ share: ({ slide }) => shareMemberQrFromSlide(slide, t(...))
  }}` (custom fn required to share the *file*, not a URL). Resolve the `MemberQrResponse` for a slide by
  matching `slide.src === m.image` (or attach `memberUuid`/`memberName`/`amount` onto the slide object
  and read them back).
- `labels` add `Download: t("wallet:qr.download")`, `Share: t("wallet:qr.share")`, plus the existing
  `Lightbox`/`Close`/zoom labels; Counter renders `index/total` chrome. Keep the white-ground CSS,
  `--fs-z-lightbox` portal pin, Zoom config, and the window-capture Escape interceptor unchanged.

### Step 7 — CSS **[MOD]** `features/wallet/components/QrDialog.module.css` (+ possibly `QrPreviewDialog.module.css`)

- Add `.qrCaption` (name emphasized, amount, muted "1/N · N members", enlarge hint) below `.qrWell`,
  using `--fs-space-*`/`--fs-color-*` tokens; center-aligned, `overflow-wrap` for long Vietnamese names.
- `QrPreviewDialog.module.css`: the Captions plugin renders its own caption chrome; only add a scoped
  tweak if the default caption contrast over the dark backdrop needs it (Captions ships readable
  defaults). Keep the single white-ground rule.

### Step 8 — Icons **[MOD]** `features/wallet/components/icons.tsx`

Add `ShareIcon` (inline `aria-hidden` SVG, `viewBox="0 0 20 20"`, `stroke="currentColor"`, matching the
existing glyph style) for the dialog footer Share button. (YARL supplies its own Share icon in the
lightbox.)

### Step 9 — i18n **[MOD]** `i18n/locales/{vi-VN,en-US}/wallet.json` (under `qr`)

Keep both locales key-in-sync (`walletI18n.test.ts`). Changes:

| Key | vi-VN | en-US | Note |
| --- | --- | --- | --- |
| `imageAltExpense` | "Mã QR VietQR để {{name}} chuyển khoản phần còn nợ của phiếu chi tiêu này." | "VietQR code for {{name}} to transfer their outstanding share of this expense." | **rewritten** (OQ2a) |
| `imageAltEvent` | "Mã QR VietQR để {{name}} chuyển khoản phần còn nợ của đợt này." | "VietQR code for {{name}} to settle their outstanding balance for this event." | **rewritten** (OQ2a) |
| `share` | "Chia sẻ" | "Share" | new — footer + YARL label |
| `shareTitle` | "Mã QR chuyển khoản" | "Transfer QR code" | new — share sheet title |
| `shareText` | "Mã QR chuyển khoản {{amount}} cho {{name}}" | "Transfer QR — {{amount}} for {{name}}" | new — share sheet text |
| `slideCounter` | "{{index}}/{{total}}" | "{{index}}/{{total}}" | new — "1/N" indicator |
| `memberCount` | "{{count}} thành viên" | "{{count}} members" | new — count line |
| `enlargeHint` | "Phóng to để xem mã QR của tất cả thành viên" | "Enlarge to view every member's QR code" | new |

Remove the now-unused composite filename keys `downloadNameExpense` / `downloadNameEvent` (grep-confirm
they have no other consumer before deleting; if `qrDialog.test.tsx` still references them, delete
together with the test rework in Step 11). `download`, `close`, `enlarge`, `previewTitle`, `zoomIn`,
`zoomOut`, `copyDetails`, `copied`, `noDebt*`, `noAccount*`, `notClosed*`, `error*`, `retry`,
`destinationLabel`, `bank`, `accountNumber`, `holder` are reused unchanged.

### Step 10 — API endpoints consumed (envelope + error handling)

| Screen / action | Verb + path | Request | Response | Errors handled |
| --- | --- | --- | --- | --- |
| Dialog (expense) | `GET /v1/expenses/{uuid}/qr/members` | `?bankAccountUuid=` (only when non-default) | `MemberQrResponse[]` (unwrapped by `api.get`) | `13003`→gate, `12001`→no-account, `12003`→no-debt, `6000`→close+toast, `12000`→generic |
| Dialog (event) | `GET /v1/events/{uuid}/qr/members` | `?bankAccountUuid=` | `MemberQrResponse[]` | as above + `12002`→not-closed, `9000`→close+toast |

`api.get` unwraps `ApiResult<T>`; failures throw a typed `ApiError`. The existing `QrDialogInner`
branches on `error.code` (numeric) — the mapping is unchanged from the composite path (same codes).

### Loading / empty / error states

- **Loading:** the existing `Skeleton` in the fixed-aspect `.qrFrame` while the query is pending. Enlarge
  triggers + footer Download/Share only render in the `isReady` branch.
- **Empty:** cannot occur — the backend returns `12003` (no-debt) instead of an empty array, mapped to
  the informational no-debt Alert (kind-aware). Defensive: `isReady` also requires `members.length > 0`.
- **Error:** unchanged code→state machine (gate / no-account / no-debt / not-closed / generic+retry /
  ownership-404 close+toast).
- **Lightbox:** no async of its own; early-returns `null` when `members` is empty (R8).

### Form validation rules

None — no forms/inputs. The only client state is the destination selection (existing) and the lightbox
active index (YARL-owned).

### Accessibility

- Dialog QR `<img>` has a per-member `alt` (name interpolated). The `.qrCaption` gives the sighted user
  the member/amount/index; it is real text (not color-coded).
- Enlarge triggers keep `wallet:qr.enlarge`. Footer Download = `wallet:qr.download`; Share =
  `wallet:qr.share` and is **hidden** when unsupported (never a dead/disabled mystery button).
- Lightbox: each slide is named by its Caption (member name + amount); Counter announces index/total;
  YARL's Download/Share/Close/zoom buttons carry our localized labels. Escape closes the preview first
  (window-capture interceptor); prev/next + swipe reachable by keyboard/pointer. Reduced-motion is
  YARL-owned. White QR ground in both themes.

### Tests (web-test-engineer — Vitest + RTL, MSW at the client boundary; vi-VN pinned)

Rework `src/features/wallet/qrDialog.test.tsx` and `src/features/wallet/useQr.test.tsx` for the JSON
list + data URLs (the blob PNG path and the `createObjectURL`/`revoke` spies for the QR image are gone).

- **Fixtures:** a `memberQrs()` helper returning `ok([{ memberUuid, memberName, amount, image:
  "data:image/png;base64,<1×1>" }, …])`; mock `*/api/v1/expenses/:uuid/qr/members` and
  `*/api/v1/events/:uuid/qr/members`.
- **`useQr.test.tsx` (rewrite):** `useExpenseMemberQrsQuery`/`useEventMemberQrsQuery` — disabled → no
  request; enabled → resolves a `MemberQrResponse[]`; non-default destination sends `?bankAccountUuid=`;
  terminal `13003`/`12003` do not retry (`retry:false`).
- **`qrDialog.test.tsx` (rework):**
  1. Ready dialog shows `members[0].image` (assert `<img src>` starts with `data:image/png;base64,`) +
     the caption (first member name, `formatMoneyVnd(amount)`, "1/2", "2 thành viên").
  2. The two enlarge triggers still exist (`/Phóng to mã QR/` → 2).
  3. Enlarge opens the YARL lightbox (`role="dialog"` name "Xem mã QR phóng to") with a QR `<img>`; the
     Captions/Counter reflect multiple members (assert the first member's caption + "1/2" counter);
     prev/next buttons present when N>1, absent when N=1.
  4. **Download current** (dialog footer) calls `downloadBlob` once with a `Blob` + fallback
     `qr-{firstMemberName}.png` (mock `downloadBlob`; let `dataUrlToBlob` run — `atob` works in jsdom).
  5. **Share current** — when `navigator.canShare`/`share` are stubbed to support files, clicking Share
     calls `navigator.share` with `{ files:[File] }`; when `navigator.share` is undefined the Share
     button is **absent** (feature-detected, never rendered).
  6. Destination picker: picking the non-default account refetches `…/qr/members?bankAccountUuid=ba-alt`
     (unchanged behavior over the new endpoint).
  7. Preserved states: `13003`/Free gate, `12001` no-account+wallet link, `12003` no-debt (kind-aware),
     `12002` not-closed, generic error+retry, `6000`/`9000` close+toast one-shot — all re-pointed at the
     `…/qr/members` endpoint.
  8. Escape closes the preview only (base dialog + first-member `<img>` survive) — the window-capture
     interceptor regression guard, now over the multi-slide lightbox.
- **`walletI18n.test.ts`** covers the new/rewritten keys structurally; optionally assert `qr.share`,
  `qr.shareText`, `qr.slideCounter`, `qr.memberCount`, `qr.enlargeHint` exist non-empty in both locales.
- **jsdom limits:** real swipe/pinch/zoom geometry and the actual OS share sheet are E2E territory
  (no layout, `navigator.share` is stubbed). Note in the test file; optional Playwright pass for
  mobile swipe + the real share sheet + real-sized QR on the dev server (vi-VN + Asia/Ho_Chi_Minh).

### Verification checklist

- `pnpm lint` clean (oxlint type-aware).
- `pnpm exec tsc -b` type-checks.
- `pnpm build` succeeds.
- `pnpm test` green (reworked `qrDialog.test.tsx` + `useQr.test.tsx` + `walletI18n.test.ts` parity).
- Manual dev-server pass (`VITE_ENABLE_MOCKS=true` or the real backend, PREMIUM user): open the QR dialog
  for an expense and a closed event; verify the first member's QR + caption ("1/N", count, hint);
  Download current saves `qr-{name}.png`; Share current opens the OS share sheet on a supporting device
  and the button is absent on desktop Chrome without file-share; enlarge → multi-slide carousel with
  captions + counter + prev/next; swipe on mobile; Escape closes the preview first; check **light +
  dark** (white QR ground) and long Vietnamese member names (caption + filename).

## Impact Analysis

- **APIs:** switches to NEW `GET /v1/expenses/{uuid}/qr/members` + `GET /v1/events/{uuid}/qr/members`
  (`ApiResult<MemberQrResponse[]>`); STOPS calling the composite `GET …/qr`. No backend change (shipped).
- **Database / Infrastructure / Services:** none (FE only).
- **Frontend (files):**
  - **[MOD]** `features/wallet/api/types.ts` (`MemberQrResponse`).
  - **[MOD]** `features/wallet/api/qrApi.ts` (`expenseMemberQrs`/`eventMemberQrs`; remove blob methods).
  - **[MOD]** `features/wallet/hooks/useQr.ts` (`useExpense/EventMemberQrsQuery`; remove blob hooks).
  - **[NEW]** `features/wallet/qrShare.ts` (`dataUrlToBlob`, `qrFileName`, `downloadMemberQr`,
    `canShareMemberQr`, `shareMemberQr`).
  - **[MOD]** `features/wallet/components/QrDialog.tsx` (list query, drop object-URL lifecycle,
    first-member render + caption, footer Download/Share current).
  - **[MOD]** `features/wallet/components/QrPreviewDialog.tsx` (multi-slide + Captions/Counter/Download/
    Share plugins).
  - **[MOD]** `features/wallet/components/QrDialog.module.css` (`.qrCaption`); possibly
    `QrPreviewDialog.module.css`.
  - **[MOD]** `features/wallet/components/icons.tsx` (`ShareIcon`).
  - **[MOD]** `i18n/locales/{vi-VN,en-US}/wallet.json` (rewrite alt keys; add share/counter/hint keys;
    remove composite filename keys).
  - **[MOD/REWRITE tests]** `features/wallet/qrDialog.test.tsx`, `features/wallet/useQr.test.tsx`.
  - **No new route/page, no `errors.ts` change, no new dependency.** `QrDialog` public props unchanged →
    `ExpenseDetailPage`/`EventDetailPage`/`M7Showcase`/`StyleGuide` untouched.
- **Data-fetching:** query keys change (`…/qr` → `…/qr/members`); `retry:false`, `gcTime:0` retained.
- **Design system:** no new primitive; reuses `Button`/`DialogContent`/`DialogFooter` + YARL (already a
  dependency). `--fs-z-lightbox` retained.
- **Documentation:** this doc; a cross-reference note may be added to `m7-wallet-qr.md` /
  `qr-image-preview.md` (optional).

## Decision Log

> D1–D8 are the user-resolved product decisions from the brief — recorded, NOT reopened.

### D1 — Per-member data via the new JSON endpoints
The web fetches `MemberQrResponse[]` from `…/qr/members` (JSON, data URLs) and drops the composite blob.
**Reason:** resolved with the user; each member shows their own QR. **Consequence:** the object-URL
lifecycle is removed (data URLs go straight into `<img src>`).

### D2 — Dialog shows the current (first) member; multi-slide lives ONLY in the lightbox
The dialog preview pane shows `members[0]` with a caption (name + amount + "1/N" + count + enlarge hint);
the swipeable carousel across all members is ONLY in the enlarged YARL lightbox.
**Reason:** resolved with the user ("Replace; lightbox only").

### D3 — Per-member Download current + Share current (Web Share API)
Download the current member's QR PNG (`qr-{memberName}.png`); Share the current member's QR image via
`navigator.share({ files, title, text })`, guarded by `canShare`, hidden when unsupported. Dialog = the
shown member; lightbox = the active slide. NO "download all", NO view-only.
**Reason:** resolved with the user.

### D4 — Composite QR dropped from the web
No composite toggle; the web stops calling `GET …/qr`. The blob `qrApi`/hooks are removed (single
consumer). **Reason:** resolved with the user; the backend keeps the composite endpoints live but unused.

### D5 — Reuse the shipped YARL lightbox, extended to multi-slide
Keep the `QrPreviewDialog` contract's spirit (open/onOpenChange/kind) but pass `members[]` + `startIndex`
instead of `imageUrl`; enable prev/next + swipe for N>1; add the Captions + Counter plugins (per-slide
name+amount + index/total). Keep Zoom, `--fs-z-lightbox`, white ground, and the window-capture Escape
interceptor. **Reason:** the lightbox already exists (qr-image-preview.md D7); no new dependency.

### D6 — Data URLs need no object URL (lifecycle simplification)
The QR image is a data URL, so `createObjectURL`/`revokeObjectURL` for the QR image are removed. The
download path still uses a transient object URL inside `downloadBlob` (self-revoking).
**Reason:** the new data shape makes the blob lifecycle unnecessary.

### D7 — Reset the lightbox on empty/close (refetch-race guard)
`previewOpen` resets when the dialog closes or `members` becomes empty; the lightbox early-returns `null`
when `members.length === 0`. **Reason:** parity with the old `imageUrl → null` guard over the new shape.

### D8 — `CopyDetailsButton` stays account-level
Copy details remains holder + number + bank (not per-member). **Reason:** it is destination info, not
per-member payment data; unchanged from today.

## Progress Log

### 2026-07-22

- Started planning the per-member QR sharing web feature. Read the backend plan
  (`FairShareMonApi/planning/per-member-qr-sharing.md` — the shipped contract, `MemberQrResponse`,
  error codes, ordering), and grounded the plan in the live SPA: `QrDialog.tsx` (blob object-URL
  lifecycle + error-code state machine + destination picker + footer), `QrPreviewDialog.tsx` (YARL
  single-slide + Zoom + Escape interceptor + white ground + `--fs-z-lightbox`), `qrApi.ts`/`useQr.ts`
  (`api.blob` + blob hooks), `types.ts`, `client.ts` (`api.get` unwrap + query-param dropping),
  `downloadBlob.ts`, `format.ts` (`formatMoneyVnd`), `errors.ts` (codes present), both `wallet.json`
  locales, and `qrDialog.test.tsx`/`useQr.test.tsx`.
- Verified YARL `3.32.1` already exports the `captions` / `counter` / `download` / `share` / `zoom`
  plugins in the installed package (no new dependency), and inspected the Download/Share plugin
  `.d.ts` (custom `download({slide,saveAs})` / `share({slide})` functions + `isShareSupported()`).
- Confirmed `QrDialog` is the ONLY product consumer of the blob `qrApi`/hooks (grep); the
  expense/event detail call sites use only the stable public props, so the contract stays.
- Recorded the four resolved product decisions as D1–D8; wrote the Implementation Plan (types → data
  layer → hooks → share helpers → dialog rework → lightbox multi-slide → CSS → icon → i18n → tests).
- Raised four Open Questions (OQ1 YARL vs custom download/share; OQ2 alt-key strategy; OQ3 filename
  sanitization; OQ4 footer crowding), each with a recommendation.
- **Conflict/premise note:** the existing `qr.imageAltExpense`/`imageAltEvent` copy describes a
  *composite* ("tổng hợp … một mã cho mỗi thành viên") and is now factually wrong for a per-member
  single QR — it MUST be rewritten regardless of OQ2. The composite filename keys
  `downloadNameExpense`/`downloadNameEvent` become dead and should be removed with a grep-before-delete
  check. `useQr.test.tsx` tests the blob hooks directly and must be rewritten alongside the hook swap.
- Status: **plan drafted; awaiting the Open-Question checkpoint before implementation.**

### 2026-07-22 (implementation — OQ1–OQ4 resolved)

- Implemented the feature end-to-end per the plan with the four Open Questions resolved as: OQ1 (a)
  YARL Download+Share plugins with custom functions; OQ2 (a) repurpose the alt keys with `{{name}}`;
  OQ3 (a) `qr-{memberName}.png` stripping only path-illegal + control chars; OQ4 (a) all four footer
  controls, wrapping.
- **Types:** added `MemberQrResponse { memberUuid, memberName, amount, image }` to `api/types.ts`.
- **Data layer:** `qrApi.ts` now exposes `expenseMemberQrs`/`eventMemberQrs` (JSON `api.get<MemberQrResponse[]>`
  at `…/qr/members`, optional `?bankAccountUuid=`); removed the blob `expenseQr`/`eventQr` + `BlobResult`.
- **Hooks:** `useQr.ts` now exports `useExpenseMemberQrsQuery`/`useEventMemberQrsQuery` (key roots
  suffixed `"members"`, `retry:false`, `gcTime:0`); removed the blob hooks.
- **Shared helpers:** new `qrShare.ts` — `dataUrlToBlob`, `qrFileName` (loop-based sanitize, no
  control-char regex → oxlint-clean), `downloadMemberQr` (→ `downloadBlob`), `canShareMemberQr`,
  `shareMemberQr` (swallows `AbortError`). Reused by both the dialog footer and the lightbox plugins.
- **QrDialog:** swapped to the list query; deleted the object-URL lifecycle + `imageUrl` state; renders
  `members[0].image` straight into `<img src>`; added the `.qrCaption` block (name + amount + "1/N" +
  "N thành viên" + enlarge hint); footer = Close / Copy details / Download current / Share current
  (Share only when `canShareMemberQr`); passes `members` + `startIndex={0}` to the lightbox. Public
  props unchanged → detail pages untouched.
- **QrPreviewDialog:** props now `{ open, onOpenChange, members, kind, startIndex? }`; multi-slide
  (`slides = members.map(...)`), prev/next hidden only when a single member; wired YARL `Captions`
  (name=title, amount=description) + `Counter` + `Download` + `Share` plugins (custom functions resolve
  the member via `slide.src`), + `captions.css`/`counter.css`; localized `Download`/`Share` labels;
  kept Zoom, white ground, `--fs-z-lightbox`, and the window-capture Escape interceptor.
- **CSS:** kept `.qrFrame` aspect at `3/4` (per-member `RenderSingle` PNG is portrait ~380×560, so the
  existing portrait frame fits; `object-fit: contain` letterboxes) and rewrote the now-stale "composite"
  comment; added `.qrCaption*` tokens; in `QrPreviewDialog.module.css` moved
  `.yarl__counter` to the bottom-left so it no longer collides with the top-left caption title.
- **Icons:** added `ShareIcon`.
- **i18n:** rewrote `imageAltExpense`/`imageAltEvent` to interpolate `{{name}}` (kept "VietQR"); added
  `qr.share`, `qr.shareTitle`, `qr.shareText`, `qr.slideCounter`, `qr.memberCount`, `qr.enlargeHint`;
  removed `qr.downloadNameExpense`/`downloadNameEvent`. Both locales key-in-sync (parity test green).
- **MSW (shared mock):** added `GET …/qr/members` handlers for expense + event (data-URL PNGs, same
  gating/ownership contract, `12003` on an empty billed set BEFORE the account check) so the dev server
  and the test-engineer's rework both have the JSON endpoint.
- **Verification:** `pnpm lint` clean (exit 0); `pnpm exec vite build` succeeds; `pnpm exec tsc -b`
  passes for all product code (fails ONLY on `useQr.test.tsx` importing the removed hooks — expected,
  left for the test-engineer); `walletI18n.test.ts` 7/7. Drove the REAL SPA (Playwright + MSW, PREMIUM
  `admin`): created an expense with two owing shares, opened the QR dialog (first member's data-URL QR +
  caption "An Nguyễn · 120.000 ₫ · 1/2 · 2 thành viên" + enlarge hint + interpolated alt), enlarged to
  the multi-slide lightbox (Captions title+amount, Counter "1/2", prev/next present for N>1, Download
  in toolbar, **Share auto-hidden** on desktop Chrome without file-share).

### 2026-07-22 (test-engineer — QR specs reworked for the per-member JSON path)

- **`useQr.test.tsx` (full rewrite).** Retargeted from the removed blob hooks onto
  `useExpenseMemberQrsQuery`/`useEventMemberQrsQuery` over `…/qr/members` (per-test
  `server.use(ok([...]))` overrides). 9 specs: disabled → no request (both kinds);
  enabled → resolves a `MemberQrResponse[]` (asserts shape + `image` is a data URL);
  default account omits `?bankAccountUuid=` while a passed value sends it (both
  kinds); terminal `13003`/`12003` do not retry (`retry:false`, queryFn ran once).
- **`qrDialog.test.tsx` (rework).** Dropped the PNG-blob fixture + the
  `URL.createObjectURL`/`revokeObjectURL` spies (both gone). Now mocks the JSON
  `…/qr/members` list with 1×1 data-URL PNGs and asserts the new surface:
  - **Ready** — first member's `<img src>` is the data URL (`data:image/png…`),
    caption shows name + amount (regex over the grouped digits to absorb Intl's
    U+202F narrow space) + "1/2" + "2 thành viên" + the account block.
  - **No object URL** — a `createObjectURL` spy is asserted NEVER called in the
    ready state (the lifecycle simplification, D6).
  - **Download current** — footer "Tải ảnh QR" calls `downloadBlob` once with a
    real `Blob` (via `dataUrlToBlob`) + fallback `qr-An Nguyễn.png` (spaces +
    diacritics kept, OQ3a).
  - **Share current** — with `navigator.share`/`canShare` stubbed available, the
    "Chia sẻ" button renders and clicking calls `navigator.share` with a `files:[File]`
    payload named `qr-An Nguyễn.png`; with the APIs undefined the button is ABSENT
    (feature-detected, never a dead button). Stubs restored in cleanup.
  - **Lightbox (YARL multi-slide)** — enlarge from BOTH triggers opens
    `role="dialog"` "Xem mã QR phóng to"; asserts a slide `<img>` (per-member alt),
    the Captions title+amount of the active (first) member, the Counter "1 / 2",
    and prev/next present for N>1 / absent for N=1; Escape + Close close the preview
    only (base dialog + first-member img survive — window-capture interceptor guard);
    event kind uses the event alt.
  - **Preserved states** re-pointed at `…/qr/members`: Premium gate (Free proactive
    + stale-tier `403 13003` reactive, query never fires for Free), `12001`
    no-account + /wallet link, `12003` no-debt (kind-aware expense/event), `12002`
    not-closed, generic `500`+retry, `6000`/`9000` close+toast one-shot, destination
    picker (`?bankAccountUuid=ba-alt` refetch), copy-details (holder+number+bank, not
    a payload; reject/absent-clipboard paths).
  - **i18n keys** spec extended to also assert the new `share`/`shareTitle`/`shareText`/
    `slideCounter`/`memberCount`/`enlargeHint` keys non-empty in both locales.
- **`walletI18n.test.ts`** — unchanged, still green (7/7); no references to the
  removed `downloadNameExpense`/`downloadNameEvent` keys anywhere.
- **Verification:** `pnpm exec tsc -b` clean; `pnpm lint` exit 0 (only pre-existing
  fast-refresh warnings in unrelated files); `pnpm test` **904 passed / 107 files**,
  fully deterministic (vi-VN pinned, network mocked at the client boundary, no
  wall-clock/timezone dependence).
- **No product bug found.** The implementation matched the plan; all rewritten
  specs pass against the shipped code.
- **E2E-only gaps (jsdom cannot cover):** real pointer/touch swipe + pinch/wheel
  zoom geometry, and the actual OS share sheet (Web Share is stubbed). These stay
  Playwright territory per the plan's jsdom-limits note.

## Final Outcome

**Shipped.** The web QR surface is fully off the composite blob and onto the per-member JSON endpoints.

- **Files created:** `src/features/wallet/qrShare.ts`.
- **Files edited:** `src/features/wallet/api/types.ts`, `src/features/wallet/api/qrApi.ts`,
  `src/features/wallet/hooks/useQr.ts`, `src/features/wallet/components/QrDialog.tsx`,
  `src/features/wallet/components/QrPreviewDialog.tsx`, `src/features/wallet/components/QrDialog.module.css`,
  `src/features/wallet/components/QrPreviewDialog.module.css`, `src/features/wallet/components/icons.tsx`,
  `src/i18n/locales/vi-VN/wallet.json`, `src/i18n/locales/en-US/wallet.json`,
  `src/test/msw/handlers.ts` (added the two `…/qr/members` handlers).
- **API consumed:** `GET /v1/expenses/{uuid}/qr/members` + `GET /v1/events/{uuid}/qr/members`
  (`ApiResult<MemberQrResponse[]>`, optional `?bankAccountUuid=`). The composite `GET …/qr` is no longer
  called from the web.
- **Quality:** lint clean; product code type-checks + vite-builds; i18n parity green; real-app run
  verified. No new dependency, no route change, no `errors.ts` change, `QrDialog` public props unchanged.
- **Handoff to the web-test-engineer:** `useQr.test.tsx` breaks at COMPILE (imports the removed
  `useExpenseQrQuery`/`useEventQrQuery` and asserts a `BlobResult` at `…/qr`) and `qrDialog.test.tsx`
  breaks at RUNTIME (mocks the PNG blob at `…/qr` + spies `URL.createObjectURL/revoke`); both must be
  reworked for the JSON list + data URLs against the new `…/qr/members` MSW handlers (already added).

## Future Improvements

- **Per-member "settled" affordance inline in the QR dialog** — mark a member paid without leaving the
  dialog (would need the settled endpoints + cache invalidation).
- **Promote the QR list into a reusable "share sheet" primitive** if a second per-item share surface
  appears (e.g. sharing an expense summary).
- **Optional Playwright E2E** for real mobile swipe/pinch + the OS share sheet + a real-sized QR fixture
  (jsdom can't assert gesture geometry or the native share sheet).
- **Retire the backend composite `…/qr` endpoints** once the web is confirmed off them (tracked as the
  backend's Open Question 1b) — the web no longer calls them after this ships.
- **Thumbnails strip** in the lightbox (YARL `thumbnails` plugin, same package) if member counts grow.
