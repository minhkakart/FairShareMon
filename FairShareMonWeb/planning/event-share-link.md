# Event Share Link — public read-only report for a closed event + owner share management

## Objective

Ship a frontend feature that lets a **Premium owner** of a **CLOSED event** create a
temporary, public, read-only share URL (`{origin}/share/{token}`, 1-day TTL) they can
copy / revoke / regenerate, and lets **anyone** open that URL — with no account or login —
to see a live read-only settlement report:

- one row per member with that member's aggregated amount,
- each row expands to show the member's **own** per-expense breakdown (the expenses the
  member has a share in — amount per expense, and what the member advanced as payer),
- a **QR** button at the end of each still-owing row that opens that member's VietQR.

Two surfaces:

1. **Owner side** — a `Share` dialog launched from the event detail page (Premium-gated,
   closed-events-only): pick a destination bank account, create the link, then view the
   active link + copy + revoke + regenerate.
2. **Public side** — a standalone unauthenticated page at `/share/:token`.

## Background

- Domain + rules of engagement are in `FairShareMonApi/The-ideal.md` §4 (absolute privacy —
  ownership miss = 404 never leak; closed events immutable; Premium/Free gating) and the
  locked frontend conventions in `FairShareMonWeb/CLAUDE.md` +
  `planning/frontend-foundation.md` (React Router v7, TanStack Query v5, one centralized
  typed `api` client, branch on numeric `error.code`, vi-VN-first i18n, VND/`formatMoneyVnd`,
  offset-aware datetimes/`formatDate`, CSS Modules + `@/components/ui` primitives).
- **Backend contract is being built in parallel** (no `FairShareMonApi/Controllers/*Share*`
  or `FairShareMonApi/planning/event-share-link.md` exists yet). This plan builds against the
  contract handed down in the task; if the shipped backend deviates, the affected DTO/type/
  code entries here must be reconciled before implementation.
- Verified reusable building blocks in `src/`:
  - **Router** (`src/routes/router.tsx`): a single `RootLayout` (`src/routes/RootLayout.tsx`
    renders `<Outlet/>` and registers the session-expired handler) with children
    `PublicOnlyRoute` (login/register), `ProtectedRoute → AppShellLayout` (the whole authed
    app), and a trailing `{ path: "*", element: <NotFound/> }`. A public share route must be a
    **sibling** of those, directly under `RootLayout`, so it is neither auth-gated nor
    bounced.
  - **Session bootstrap** (`src/app/useSessionBootstrap.ts`): for an anonymous visitor with no
    refresh token in `localStorage`, it calls `markUnauthenticated()` and renders children —
    no redirect. The session-expired redirect handler only fires on a real `401` from an
    **authed** request; the public endpoints are called with `{ anonymous: true,
    skipAuthRefresh: true }`, so they never trigger it. The public page is safe under
    `AppProviders`.
  - **API client** (`src/lib/api/client.ts`): `api.get/post/delete` unwrap `ApiResult<T>` and
    throw a typed `ApiError` (numeric `code`, localized `message`, `httpStatus`). `RequestOptions`
    already supports `anonymous` and `skipAuthRefresh`. Paths are `env.apiBaseUrl` + a
    `/v1/...` path (e.g. `qrApi` uses `/v1/events/${uuid}/qr/members`).
  - **Error codes** (`src/lib/api/errors.ts`): `ErrorCodes` mirror stops at admin `14003`;
    it has **no `15xxx` entries yet** — the two share codes must be appended (see Impact).
    `isApiError`, `FREE_LIMIT_CODES` exist. `PremiumFeatureRequired = 13003` exists.
  - **Balance table** (`src/features/events/components/EventBalanceTable.tsx`): the read/write
    per-member table (advanced / owed / balance / outstanding / status + `MemberSettledToggle`).
    The public table is adapted from it with **all write controls stripped**. `MemberBalanceRow`
    lives in `src/features/events/api/types.ts` (advanced/owed/balance/outstanding/isSettled/
    isDeleted/isOwnerRepresentative/memberUuid/memberName).
  - **QR preview** (`src/features/wallet/components/QrPreviewDialog.tsx`): props
    `{ open, onOpenChange, members: MemberQrResponse[], kind: "expense"|"event", startIndex? }`
    — a YARL lightbox carousel, one slide per member, captioned name+amount, localized via the
    `wallet` namespace (`kind` selects `wallet:qr.imageAltEvent`). `MemberQrResponse`
    (`{ memberUuid, memberName, amount, image }`, `image` = `data:image/png;base64,…`) lives in
    `src/features/wallet/api/types.ts`. `QrDialogKind` is exported from
    `src/features/wallet/components/QrDialog.tsx`.
  - **Destination bank picker**: the QR dialog (`QrDialog.tsx`) renders a plain `Select` from
    `@/components/ui` over `useBankAccountsQuery()` (`src/features/wallet/hooks/useBankAccounts.ts`,
    options `bankName · maskAccount(accountNumber)`, default account first). This — **not** the
    NAPAS-BIN `Combobox`/`bankOptions.tsx` — is the pattern the Share dialog's destination picker
    reuses. `BankAccountResponse` is in `src/features/wallet/api/types.ts`.
  - **Premium tier read**: `useCurrentUser()` (`src/features/auth/hooks/useAuth.ts`) returns
    `SessionUser | null`; QrDialog computes `isPremium = (user?.tier ?? "").toUpperCase() ===
    "PREMIUM"`. The Premium affordance is `UpgradePrompt` from `@/components/ui`
    (`src/components/ui/Premium/Premium.tsx`, variants `cta` | `info` | `active`).
  - **Table** (`@/components/ui` `Table`) supports `stackOnMobile` + per-cell `data-label`.
  - **i18n** (`src/i18n/index.ts`): namespaces registered in `resources` + `NAMESPACES`; a
    new namespace needs vi-VN + en-US JSON files imported and added to both arrays. Parity
    is guarded by `*I18n.test.ts` (see `src/features/wallet/walletI18n.test.ts`).

## Requirements

### Owner side (authed, in the event detail page)

- A `Share` action button in `EventDetailPage` `DetailView`, next to the QR action, shown
  **only when `event.isClosed`** (mirrors the QR button's `closed ?` guard).
- Clicking opens `ShareEventDialog`:
  - **Free** user (proactive, by session tier) OR a stale-tier reactive `403 13003` →
    render `UpgradePrompt` (`variant="info"` — Premium is granted manually, no self-serve
    click target, matching the QR dialog gate); the create mutation never fires proactively.
  - **Premium** user → on open, `GET /v1/events/{uuid}/share` fetches the active link.
    - active link present → show the URL, its expiry, **Copy**, **Revoke**, **Regenerate**.
    - no active link → show a destination **bank-account picker** (`Select` over
      `useBankAccountsQuery()`, default-first) + a **Create link** button.
  - Create posts `{ bankAccountUuid?, regenerate? }`; on success show the URL + expiry +
    copy/revoke/regenerate controls.
  - Copy writes `{window.location.origin}/share/{token}` to the clipboard (only confirm on a
    successful `navigator.clipboard.writeText`, per the `CopyDetailsButton` precedent).
- Error handling (branch on numeric `code`, render `error.message` verbatim via
  `resolveErrorMessage`):
  - `13003` PremiumFeatureRequired → `UpgradePrompt`.
  - `15001` event-not-closed (defensive; button hidden until closed) → warning alert.
  - `12001` no bank account for QR → empty state routing to `/wallet` (only if the backend
    rejects create without a destination — see OQ2).
  - event ownership `404` (`9000`) → close the dialog with a danger toast (no existence leak),
    matching `QrDialog`'s `handled404` pattern.

### Public side (anonymous)

- Route `/share/:token` renders `PublicSharePage` on its **own minimal layout** (NOT
  `AppShellLayout`; no authed nav/logout).
- On mount, `GET /v1/public/shares/{token}` with `{ anonymous: true, skipAuthRefresh: true }`,
  `retry: false`.
  - loading → skeleton.
  - `15000` (expired / revoked / missing) OR any not-found → a single friendly
    "link expired or not found" screen that **does not disclose whether the token ever
    existed** (no existence leak; identical copy for all three).
  - success → event header (`eventName`, `closedAt`, summary: `totalOutstanding`,
    `owingMemberCount`, `settledMemberCount`) + `PublicBalanceTable`.
- `PublicBalanceTable`: read-only per-member rows; each row expands (toggle) to
  `MemberExpenseBreakdown`; a **QR** button renders **only when the row is still owing**
  (`outstanding > 0`) and `hasQr` is true.
- QR flow: on the **first** QR-button click, lazily `GET /v1/public/shares/{token}/qr/members`
  (`{ anonymous: true, skipAuthRefresh: true }`), then open the existing `QrPreviewDialog`
  (`kind="event"`) at the clicked member's index (mapped by `memberUuid`). Subsequent clicks
  reuse the cached result and just reposition `startIndex`.
- `MemberExpenseBreakdown`: from the payload `expenses[]`, group `expenses[].shares` by
  `memberUuid`; for the expanded member show each expense they have a share in (expense name,
  time, the member's share amount) and, where the member is the payer, what they advanced.
- Money via `formatMoneyVnd`, datetimes via `formatDate`/`formatDateTime`; never float math.
- All copy through a new `share` i18n namespace (vi-VN + en-US).
- Accessibility baseline: semantic landmarks, an accessible expand/collapse control
  (`aria-expanded` + `aria-controls`), labeled QR button, color-independent status, focus
  management, `<html lang>` already synced by `LocaleProvider`.

## Open Questions

> None of the 5 resolved decisions are reopened. These are **new** ambiguities surfaced while
> planning against the (still-in-flight) backend contract; each needs a call before build.

**OQ1 — What error code / shape does the owner-side `GET /v1/events/{uuid}/share` return when
no active link exists yet?** The contract says "active link or 404". If that 404 carries a
generic `1003`/`9000`, the dialog cannot tell "you own this event but haven't shared it yet"
(→ show the create form) apart from "not found". Options:
  - (a) Backend returns `isSuccess:true` with `data: null` for "no active link" (a 200), so a
    real 404 stays unambiguous. *(Recommended — cleanest for the owner UI.)*
  - (b) Backend returns 404 with a dedicated code (e.g. a `ShareLinkNotFound` distinct from
    event-not-found) and the dialog maps that code → "no link yet" empty state.
  - (c) Backend returns 404 with `15000` for "no active link" too; the dialog treats `15000`
    on the *owner* GET as "no link yet" (never the public "expired" screen).
Trade-off: (a) avoids overloading a 404 as a normal state; (b)/(c) keep it RESTful but require
the exact code to be pinned so the owner dialog branches correctly.

**OQ2 — Behavior when the Premium owner has NO bank account at share-create time.** The create
body's `bankAccountUuid` is optional and the public payload has a `hasQr` flag, implying a
link can exist without QR. Options:
  - (a) Allow creating a link with no destination → `hasQr:false`, public page shows amounts
    but no QR buttons. *(Recommended — the report is still useful.)*
  - (b) Backend rejects create with `12001` (no bank account) → dialog shows an empty state
    routing to `/wallet` (reuse the QR dialog's no-account treatment) and blocks create.
This determines whether the destination picker is required or optional in the create form.

**OQ3 — Confirmation before Revoke and/or Regenerate.** Both are destructive to an
already-distributed URL (revoke kills it; regenerate kills the old token and mints a new one).
Options:
  - (a) Inline confirm step (a second click / "Are you sure?" within the dialog) for both.
    *(Recommended — cheap safety on an irreversible share invalidation.)*
  - (b) A separate confirm dialog (like `DeleteEventDialog`).
  - (c) No confirmation (immediate), relying on the 1-day TTL to limit blast radius.

**OQ4 — Expiry presentation + proactive-expiry handling on the owner side.** Options for the
`expiresAt` display: (a) absolute datetime via `formatDateTime`; (b) relative countdown
("còn ~23 giờ"); (c) both. And: if the owner opens the dialog after the link expired, do we
(i) show an "expired — regenerate" state, or (ii) treat expiry the same as "no active link"
and show the create form? Needs a preference.

**OQ5 — Public page language & theme controls for an anonymous visitor.** The visitor has no
session/locale preference; the page defaults to vi-VN (backend `Accept-Language` from the
active i18n locale, which persists in `localStorage` per `LOCALE_STORAGE_KEY`). Options:
  - (a) Render a minimal header with a **locale toggle** (and optionally a theme toggle) so a
    visitor can switch to en-US. *(Recommended — the audience may be mixed-language.)*
  - (b) No controls; inherit whatever locale/theme is stored, default vi-VN.

**OQ6 — Should `/share/:token` be excluded from search-engine indexing?** These are temporary
URLs exposing member names + money. Options:
  - (a) Emit `<meta name="robots" content="noindex,nofollow">` while the share page is mounted
    (and document a `robots.txt` `Disallow: /share/` for the deploy). *(Recommended — privacy
    of financial data.)*
  - (b) Do nothing (rely on token unguessability + 1-day TTL).

**OQ7 — What columns does the public read-only table show?** The payload rows are full
`MemberBalanceRow`s (advanced/owed/balance/outstanding/isSettled). Options:
  - (a) Mirror `EventBalanceTable`: advanced / owed / balance / outstanding + a
    đã-trả/còn-nợ **status badge** (read-only, no toggle). *(Recommended — familiar, complete.)*
  - (b) Simplified public view: member + a single "amount" column (outstanding) + QR only,
    hiding the accounting breakdown behind the row expand.
The choice drives the table's column set and whether a settled badge appears publicly.

**OQ8 — Does the public per-member QR endpoint honor the destination chosen at create time?**
Assumed yes (the create picks/stores a `bankAccountUuid` and `GET …/qr/members` renders QR to
that stored destination — the public visitor cannot choose). Confirm there is no
visitor-facing destination picker on the public page (there is none in this plan).

## Assumptions

- The backend ships exactly the contract in the task: authed `POST/GET/DELETE
  /v1/events/{uuid}/share` and anonymous `GET /v1/public/shares/{token}` +
  `GET /v1/public/shares/{token}/qr/members`, with codes `15000` (expired/revoked/missing) and
  `15001` (event not closed on create) plus `13003` (Premium) on create.
- `env.apiBaseUrl` already prefixes `/api`, so share paths are the `/v1/...` strings above,
  same as `qrApi`.
- The public payload's `rows` are the same `MemberBalanceRow` shape as the event balance
  (reused from `src/features/events/api/types.ts`); `expenses[]` is a public-safe expense list
  (`PublicExpense`) whose `shares` carry at least `memberUuid`, `memberName`, `amount`, plus
  the parent expense's name/time/payer — exact fields confirmed against the shipped DTO.
- `PublicEventShareResponse.hasQr` is the single gate for whether QR is available at all;
  per-row QR buttons additionally require `outstanding > 0`.
- The 1-day TTL is enforced server-side; the client only displays `expiresAt` and reacts to
  `15000` — it never computes expiry to decide access.
- The public page is client-rendered (no SSR); `window.location.origin` is the copy-URL base.
- `MemberQrResponse` from the public QR endpoint is identical to the wallet one, so
  `QrPreviewDialog` is reused unchanged.
- No new runtime dependency is introduced (YARL, TanStack Query, RHF, Radix already approved).

## Implementation Plan

> Paths under `FairShareMonWeb/`. All user-facing strings via the new `share` namespace
> (vi-VN authoritative). Reuse existing primitives/hooks — no parallel systems.

### Step 1 — Error-code mirror

1. In `src/lib/api/errors.ts`, append a `15xxx` block:
   `ShareLinkNotFound: 15000` (expired / revoked / missing) and
   `EventNotClosedForShare: 15001`. (Names are the frontend mirror's own; keep the numeric
   values from the contract. Reconcile names with the backend area doc when it lands.)

### Step 2 — Share feature module `src/features/share/`

1. `api/types.ts`:
   - `ShareLinkResponse { token: string; expiresAt: string; bankName: string;
     accountNumber: string; accountHolderName: string }` (the "bank display" the create/get
     return — exact field names confirmed against the DTO).
   - `CreateShareLinkRequest { bankAccountUuid?: string; regenerate?: boolean }`.
   - `PublicShare { memberUuid: string; memberName: string; amount: number; note?: string | null }`.
   - `PublicExpense { uuid: string; name: string; expenseTime: string; total: number;
     payerMemberUuid: string; payerMemberName: string; shares: PublicShare[] }`.
   - `PublicEventShareResponse { eventName: string; closedAt: string;
     rows: MemberBalanceRow[]; expenses: PublicExpense[]; totalOutstanding: number;
     owingMemberCount: number; settledMemberCount: number; hasQr: boolean }`
     — importing `MemberBalanceRow` from `@/features/events/api/types` and re-exporting
     `MemberQrResponse` from `@/features/wallet/api/types`.
2. `api/shareApi.ts` (over the centralized `api`):
   - `getActiveLink(eventUuid) → api.get<ShareLinkResponse | null>(\`/v1/events/${eventUuid}/share\`)`.
   - `createLink(eventUuid, body) → api.post<ShareLinkResponse>(\`/v1/events/${eventUuid}/share\`, body)`.
   - `revokeLink(eventUuid) → api.delete<{ message: string }>(\`/v1/events/${eventUuid}/share\`)`.
   - `getPublicShare(token) → api.get<PublicEventShareResponse>(\`/v1/public/shares/${token}\`,
     { anonymous: true, skipAuthRefresh: true })`.
   - `getPublicShareMemberQrs(token) → api.get<MemberQrResponse[]>(
     \`/v1/public/shares/${token}/qr/members\`, { anonymous: true, skipAuthRefresh: true })`.
3. `hooks/useShare.ts` (TanStack Query; key factory `shareKeys`):
   - `shareKeys = { active: (eventUuid) => ["share","active",eventUuid], public: (token) =>
     ["share","public",token], publicQrs: (token) => ["share","public-qrs",token] }`.
   - `useActiveShareLinkQuery(eventUuid, enabled)` — `enabled = dialogOpen && isPremium`,
     `retry:false`.
   - `usePublicShareQuery(token)` — `retry:false`, `gcTime` default; the public page's primary
     read.
   - `usePublicShareMemberQrsQuery(token, { enabled })` — lazy (enabled toggled true on first
     QR click), `retry:false`, `gcTime:0` (mirrors `useQr.ts`).
   - `useCreateShareLink()` / `useRevokeShareLink()` mutations; `onSuccess` invalidates
     `shareKeys.active(eventUuid)`. (No optimistic updates — established convention.)
4. `components/ShareEventDialog.tsx` — see Step 4.
5. `components/PublicBalanceTable.tsx` — see Step 5.
6. `components/MemberExpenseBreakdown.tsx` — see Step 5.
7. `pages/PublicSharePage.tsx` — see Step 5.

### Step 3 — Route wiring

1. In `src/routes/router.tsx`, add a **sibling** of `PublicOnlyRoute`/`ProtectedRoute`, inside
   `RootLayout.children`, **before** the `{ path: "*" }` catch-all:
   `{ path: "share/:token", element: <PublicSharePage /> }`.
   It is deliberately outside both guards: not auth-gated, not bounced by `PublicOnlyRoute`.
2. No change to `RootLayout` (its session-expired handler is inert for anonymous visitors) or
   to `AppProviders`/`useSessionBootstrap` (anonymous visitor → `markUnauthenticated` → renders).

### Step 4 — Owner: `ShareEventDialog` + event-detail wiring

1. `ShareEventDialog.tsx` props `{ open, onOpenChange, event: EventResponse }`. Structure a
   presentational inner state machine mirroring `QrDialogInner`:
   - Premium gate: `!isPremium || errorCode === ErrorCodes.PremiumFeatureRequired` →
     `UpgradePrompt variant="info"` (`share:premium.gateTitle/Body`). Query never fires for Free.
   - `useActiveShareLinkQuery(event.uuid, open && isPremium)`:
     - loading → skeleton.
     - no active link (per OQ1 resolution) → destination `Select` over `useBankAccountsQuery()`
       (`share:create.destinationLabel`, default-first, optional/required per OQ2) + a
       **Create link** button (`useCreateShareLink`).
     - active link → a read-only URL field (`{origin}/share/{token}`) + **Copy** button + an
       expiry line (`formatDateTime(expiresAt)` and/or countdown per OQ4) + the resolved bank
       display + **Revoke** (`useRevokeShareLink`) + **Regenerate** (`useCreateShareLink` with
       `{ regenerate: true, bankAccountUuid }`), with confirmation per OQ3.
   - Error branching: `15001` → warning alert; `12001` → wallet empty state (if OQ2b); event
     `9000` 404 → close + danger toast (`handled404` ref pattern).
   - Mutation failures → `resolveErrorMessage(error, t)` toast (`useToast`).
2. In `src/features/events/pages/EventDetailPage.tsx` `DetailView`:
   - add `const [shareOpen, setShareOpen] = useState(false)`.
   - render a `Share` button inside the `closed ?` branch, next to the existing
     `wallet:qr.showEvent` QR button (`variant="secondary"`, `size="sm"`, a share icon,
     label `share:action.share`), `onClick={() => setShareOpen(true)}`.
   - render `{closed ? <ShareEventDialog open={shareOpen} onOpenChange={setShareOpen}
     event={event} /> : null}` next to the existing closed-only `QrDialog`.

### Step 5 — Public page + table + breakdown + QR

1. `pages/PublicSharePage.tsx`:
   - `const { token = "" } = useParams();` then `usePublicShareQuery(token)`.
   - loading → a skeleton report (header + table skeleton, reusing `Skeleton`).
   - error: if `isApiError(error) && error.code === ErrorCodes.ShareLinkNotFound` OR any
     not-found → a `share:expired.*` friendly screen (identical copy for expired/revoked/missing;
     no retry that could probe existence).
   - success → minimal layout wrapper (optional locale/theme header per OQ5) + an event header
     (`eventName`, `share:header.closedAt` via `formatDateTime(closedAt)`, summary counts +
     `formatMoneyVnd(totalOutstanding)`) + `<PublicBalanceTable data={data} token={token} />`.
2. `components/PublicBalanceTable.tsx` (adapted from `EventBalanceTable`, write controls
   stripped — no `MemberSettledToggle`, no `useEventBalanceQuery`; data passed in):
   - `@/components/ui` `Table` with `stackOnMobile` + `data-label` per cell; `Money` +
     `formatMoneyVnd`; columns per OQ7 (default: member / advanced / owed / balance /
     outstanding / read-only status badge) plus an expand toggle cell and a trailing QR cell.
   - Each row: a `<button aria-expanded aria-controls>` expand toggle; when expanded, render
     `<MemberExpenseBreakdown memberUuid expenses={data.expenses} />` in a spanning row.
   - QR cell: render a QR `<button>` only when `row.outstanding > 0 && data.hasQr`. On click:
     set lazy-QR `enabled=true`, remember `memberUuid`; when `usePublicShareMemberQrsQuery`
     resolves, open `QrPreviewDialog` at `members.findIndex(m => m.memberUuid === memberUuid)`
     (fallback 0). While fetching, show a spinner/disabled state on the button.
   - Owns the `QrPreviewDialog` instance (`open`, `onOpenChange`, `members`, `kind="event"`,
     `startIndex`), plus loading/error handling for the QR fetch (a toast on failure).
3. `components/MemberExpenseBreakdown.tsx`:
   - props `{ memberUuid: string; memberName: string; expenses: PublicExpense[] }`.
   - derive the member's rows: for each expense, find `expense.shares.find(s => s.memberUuid ===
     memberUuid)`; if present, show expense name + `formatDateTime(expenseTime)` + the member's
     `share.amount`; annotate expenses where `expense.payerMemberUuid === memberUuid` with what
     the member advanced (`expense.total`). Empty → a calm `share:breakdown.empty` note.
   - presentational; VND via `formatMoneyVnd`, dates via `formatDateTime`.

### Step 6 — i18n

1. Add `src/i18n/locales/vi-VN/share.json` + `src/i18n/locales/en-US/share.json`.
2. Register in `src/i18n/index.ts`: import both, add `share` to `resources["vi-VN"]`,
   `resources["en-US"]`, and the `NAMESPACES` array.
3. Reuse the existing `wallet` namespace for `QrPreviewDialog` chrome (already covers
   `qr.imageAltEvent`, `qr.previewTitle`, `qr.close`, `qr.zoom*`, `qr.download`, `qr.share`,
   `qr.shareTitle`, `qr.shareText`) — no duplication.

### i18n keys (initial; vi-VN + en-US, `share` namespace)

- `action.share` (button label on event detail).
- `dialog.title`, `dialog.description`.
- `premium.gateTitle`, `premium.gateBody`.
- `create.destinationLabel`, `create.destinationHint`, `create.submit`, `create.noAccountTitle`,
  `create.noAccountBody`, `create.addAccount` (if OQ2b).
- `link.urlLabel`, `link.copy`, `link.copied`, `link.expiresAt`, `link.expiresIn`,
  `link.bank`, `link.accountNumber`, `link.holder`, `link.revoke`, `link.regenerate`,
  `link.confirmRevokeTitle`, `link.confirmRevokeBody`, `link.confirmRegenerateTitle`,
  `link.confirmRegenerateBody`, `link.revoked` (toast), `link.regenerated` (toast).
- `notClosed.title`, `notClosed.body` (`15001`).
- `public.title`, `public.closedAt`, `public.summary`, `public.member`, `public.advanced`,
  `public.owed`, `public.balance`, `public.outstanding`, `public.statusColumn`,
  `public.statusSettled`, `public.statusOwing`, `public.expandLabel`, `public.collapseLabel`,
  `public.showQr`, `public.caption`, `public.emptyTitle`, `public.emptyBody`.
- `breakdown.title`, `breakdown.expense`, `breakdown.time`, `breakdown.shareAmount`,
  `breakdown.advancedAsPayer`, `breakdown.empty`.
- `expired.title`, `expired.body` (single copy for expired/revoked/missing).
- `error.title`, `error.qrTitle`, `error.qrBody`, `error.retry`.

### API endpoints consumed (verb + path + DTO + codes)

| Screen/hook | Verb + Path | Request | Response `data` | Notable codes |
| --- | --- | --- | --- | --- |
| ShareEventDialog (get) | `GET /v1/events/{uuid}/share` | — (Bearer) | `ShareLinkResponse` or null (OQ1) | `13003`, `9000` 404, no-link (OQ1) |
| ShareEventDialog (create/regen) | `POST /v1/events/{uuid}/share` | `{ bankAccountUuid?, regenerate? }` | `ShareLinkResponse` | `13003`, `15001`, `12001` (OQ2), `9000` |
| ShareEventDialog (revoke) | `DELETE /v1/events/{uuid}/share` | — (Bearer) | `{ message }` | `9000`, `13003` |
| PublicSharePage | `GET /v1/public/shares/{token}` | — (`anonymous`, `skipAuthRefresh`) | `PublicEventShareResponse` | `15000` |
| PublicBalanceTable QR | `GET /v1/public/shares/{token}/qr/members` | — (`anonymous`, `skipAuthRefresh`) | `MemberQrResponse[]` | `15000` |

Envelope: all go through the centralized `api`; success unwraps `data`; failures throw
`ApiError` — screens branch on `code`, render `error.message`, never parse message text.

### Loading / empty / error states

- **Owner dialog**: skeleton while `useActiveShareLinkQuery` pending; `UpgradePrompt` (Free/
  `13003`); create/revoke buttons `loading` while their mutation is pending; toast on failure;
  no-link → create form; not-closed `15001` → warning alert.
- **Public page**: skeleton report while pending; `15000`/not-found → single friendly
  "expired or not found" screen (no existence leak, no probe-y retry); generic load failure →
  `ErrorState` with a retry that re-runs the query.
- **Public QR**: button spinner while the lazy QR query runs; success → open `QrPreviewDialog`;
  failure → danger toast (`share:error.qrTitle`), preview stays closed.
- **Public table empty** (`rows.length === 0`) → `EmptyState` in a `TableEmpty` row.

### Form / input validation (mirrors backend)

- The only owner input is the **destination bank-account** picker — a select over the owner's
  existing accounts; value must be one of the returned `bankAccountUuid`s (or omitted if OQ2a).
  No free-text; no client-side money entry. `regenerate` is a boolean flag, not user input.
- Public side is entirely read-only — no forms.

### Accessibility requirements

- Public page: a semantic `<main>` landmark, an `<h1>` event name; the table uses
  `@/components/ui` `Table` semantics (`<th scope>`); the row expander is a real `<button>`
  with `aria-expanded` + `aria-controls` pointing at the breakdown region; the QR button has an
  accessible name (`share:public.showQr` with the member name); status is color-independent
  (icon + text badge, per `EventBalanceTable`); focus is visible; `<html lang>` is already
  synced by `LocaleProvider`. `QrPreviewDialog` brings its own focus-trap/labels.
- Owner dialog: labeled destination select, labeled copy/revoke/regenerate buttons, confirm
  steps keyboard-operable; the URL field is read-only + selectable.

### Tests the web-test-engineer should write (Vitest + RTL, MSW at the client boundary)

- **`shareI18n.test.ts`** — vi-VN/en-US key-shape parity for the `share` namespace + no empty
  leaves (mirror `walletI18n.test.ts`); assert fixed domain terms (đợt/ví/đã trả) in vi-VN.
- **`shareApi.test.ts`** — `getPublicShare`/`getPublicShareMemberQrs` send `anonymous` +
  `skipAuthRefresh` (no `Authorization` header, no refresh on a synthetic 401); authed
  create/get/delete hit the right verb+path and unwrap `data`.
- **`publicSharePage.test.tsx`** — success renders header + one row per member + summary;
  `15000` renders the friendly expired screen with **no member data leaked** and identical copy
  for expired vs revoked vs missing; loading renders the skeleton; a generic failure offers
  retry.
- **`publicBalanceTable.test.tsx`** — QR button shows only for `outstanding > 0 && hasQr`;
  clicking it fires the lazy QR query once and opens `QrPreviewDialog` at the clicked member's
  index; the expand toggle reveals `MemberExpenseBreakdown` and sets `aria-expanded`.
- **`memberExpenseBreakdown.test.tsx`** — groups `expenses[].shares` by `memberUuid` correctly;
  shows the member's share amount per expense and the advanced-as-payer annotation; empty state
  when the member has no shares.
- **`shareEventDialog.test.tsx`** — Free user (or `403 13003`) sees `UpgradePrompt` and no
  create call fires; Premium with an active link shows URL + copy + revoke + regenerate;
  Premium with no link shows the create form; copy writes `{origin}/share/{token}`; revoke/
  regenerate honor the confirm step (OQ3); `15001` → warning; event `9000` closes with a toast.
- **`eventDetailShare.test.tsx`** (or extend the existing event-detail test) — the `Share`
  button renders only when `event.isClosed` and opens the dialog.
- All deterministic: pinned `TZ` (Asia/Ho_Chi_Minh) + locale (vi-VN), MSW-mocked, no wall clock.

## Impact Analysis

- **APIs (consumed):** authed `GET/POST/DELETE /v1/events/{uuid}/share`; anonymous
  `GET /v1/public/shares/{token}` + `…/qr/members`. No frontend-side API is authored.
- **Routing:** one new public route `share/:token` under `RootLayout` (sibling of the guards).
- **New source:** `src/features/share/` (`api/types.ts`, `api/shareApi.ts`, `hooks/useShare.ts`,
  `pages/PublicSharePage.tsx`, `components/{ShareEventDialog,PublicBalanceTable,
  MemberExpenseBreakdown}.tsx` + their CSS Modules), the two `share.json` locale files.
- **Edited source:** `src/routes/router.tsx` (route + import), `src/i18n/index.ts` (namespace
  registration), `src/lib/api/errors.ts` (append `15xxx`), `src/features/events/pages/
  EventDetailPage.tsx` (Share button + dialog).
- **Reused (unchanged):** `QrPreviewDialog`, `MemberQrResponse`, `MemberBalanceRow`,
  `useBankAccountsQuery`, `UpgradePrompt`, `Table`/`Money`/`Skeleton`/`EmptyState`/`ErrorState`,
  `formatMoneyVnd`/`formatDateTime`, `resolveErrorMessage`, `useToast`, `useCurrentUser`.
- **Infrastructure/DB:** none (frontend only). Backend must ship the contract + `15xxx` codes.
- **Docs:** this planning doc; `share` namespace parity test.
- **Security/privacy:** public page must never leak existence (`15000` = single friendly
  screen) and never expose owner PII beyond the report; consider `noindex` (OQ6). The public
  endpoints are anonymous by design and must not carry a Bearer token.

## Decision Log

### Decision — public route is an ungated sibling under `RootLayout`
Placed `share/:token` as a direct child of `RootLayout`, outside `PublicOnlyRoute` and
`ProtectedRoute`, on its own minimal layout.
**Reason:** an anonymous visitor must neither be redirected to `/login` (ProtectedRoute) nor
bounced to the app (PublicOnlyRoute); `useSessionBootstrap` already resolves to
`markUnauthenticated` with no token, so the page renders cleanly.
**Alternatives considered:** a separate router/entry for the public page (overkill, loses shared
providers/i18n/theme); nesting under a guard with an exception (fragile).

### Decision — reuse `QrPreviewDialog` (`kind="event"`) and the `wallet` i18n chrome for public QR
**Reason:** the public per-member QR is the same `MemberQrResponse[]` shape; the lightbox
carousel, captions, download/share, and localized chrome already exist and are accessible.
**Alternatives considered:** a bespoke public QR viewer (duplicates a reviewed component).

### Decision — adapt (not fork) `EventBalanceTable` into a read-only `PublicBalanceTable`
**Reason:** the read-only report is the balance table minus write controls; adapting keeps the
column semantics, `Money`/`stackOnMobile` treatment, and CVD-safe status consistent.
**Alternatives considered:** parameterizing `EventBalanceTable` with a `readOnly` flag (couples
the authed component to the public payload + anonymous data source — riskier).

### Decision — destination picker reuses the QR dialog's `Select`-over-`useBankAccountsQuery`
pattern, not the NAPAS-BIN combobox.
**Reason:** the share destination is one of the owner's **saved** bank accounts (ví), exactly
like the QR dialog's destination override — not a bank-directory lookup.

### Decision (2026-07-24, orchestrator) — the 8 Open Questions resolved + confirmed contract

The orchestrator handed down the confirmed backend contract (serialized camelCase) and resolved
all 8 Open Questions. These are binding for implementation:

- **Contract / error codes:** the shipped codes are **`16000` (ShareLinkNotFoundOrExpired)** and
  **`16001` (EventNotClosedForShare)** — NOT the `15000/15001` placeholders this plan drafted
  against before the contract landed. `src/lib/api/errors.ts` mirrors them under a `16xxx` block;
  every reference to `15000/15001` in this doc is superseded by `16000/16001`.
  `ShareLinkResponse` carries `{ token, expiresAt, createdAt, hasQr, bankName?, accountNumber?,
  accountHolderName? }`; `PublicEventShareResponse` carries `{ eventName, closedAt?, rows[],
  expenses[], totalOutstanding, owingMemberCount, settledMemberCount, hasQr }`; `PublicExpense` =
  `{ uuid, name, payerMemberUuid, payerName, expenseTime, total, shares[] }`; `PublicShare` =
  `{ memberUuid, memberName, amount, isSettled, note? }`. Public endpoints are called with
  `{ anonymous: true, skipAuthRefresh: true }`. The public per-member QR endpoint may return an
  EMPTY array (nobody owes / `hasQr` false).
- **OQ1 → (a):** owner-side `GET …/share` returns `200` with `data:null` for "not shared yet" → the
  dialog shows the create form; a real `404`/`16000` is a distinct, separate case.
- **OQ2 → bank OPTIONAL:** if the owner HAS bank accounts, the create dialog requires picking one
  (default preselected); if the owner has NONE, a QR-less link can still be created and an inline
  hint links to `/wallet`. The public page hides QR buttons when `hasQr` is false or a row's
  `outstanding <= 0`.
- **OQ3 → inline confirm:** a two-step "are you sure?" WITHIN the dialog for BOTH Revoke and
  Regenerate.
- **OQ4 → (a):** expiry is shown as an absolute datetime via `formatDateTime`; if the owner opens
  the dialog after expiry, an "expired — regenerate" state is shown.
- **OQ5 → (a):** the public page renders a minimal header with a locale toggle (vi-VN/en-US); a
  theme toggle was skipped (optional, kept out to reduce risk).
- **OQ6 → (a):** while `PublicSharePage` is mounted, a `<meta name="robots"
  content="noindex,nofollow">` tag is injected (removed on unmount).
- **OQ7 → (a):** the public read-only table mirrors `EventBalanceTable`'s columns (advanced / owed
  / balance / outstanding) plus a READ-ONLY đã-trả/còn-nợ status badge — no toggles, no write
  controls.
- **OQ8 → confirmed:** the public per-member QR uses the destination snapshotted at creation; there
  is NO visitor-facing bank picker.

## Progress Log

### 2026-07-24

- Feature-planner: completed required reading — `The-ideal.md` domain/privacy rules,
  `FairShareMonWeb/CLAUDE.md` + `planning/frontend-foundation.md` (locked stack), and the live
  `src/`: `routes/router.tsx` + `RootLayout.tsx` (guard structure), `app/useSessionBootstrap.ts`
  (anonymous-visitor behavior), `lib/api/client.ts` (`anonymous`/`skipAuthRefresh` options) +
  `lib/api/errors.ts` (code mirror stops at `14003`), `features/events` (`EventBalanceTable`,
  `EventDetailPage`, `api/types.ts` `MemberBalanceRow`), `features/wallet`
  (`QrPreviewDialog`/`QrDialog` contracts, `MemberQrResponse`, `useBankAccounts`,
  `bankOptions`), `features/auth/hooks/useAuth` (tier read), `components/ui` `UpgradePrompt` +
  `Table`, and `i18n/index.ts` + `walletI18n.test.ts` (namespace registration + parity test).
- Confirmed the backend share area is not yet in the repo (no `*Share*` controller / planning
  doc); planned strictly against the task's contract and flagged the drift risk.
- Drafted this plan: new `src/features/share/` module (api/types/hooks + `PublicSharePage`,
  `ShareEventDialog`, `PublicBalanceTable`, `MemberExpenseBreakdown`), the ungated
  `share/:token` route, `share` i18n namespace + parity test, `15xxx` code-mirror addition,
  event-detail Share button, and the full test list.
- **Raised 8 new Open Questions** (owner GET "no link" code, no-bank-account create behavior,
  revoke/regenerate confirmation, expiry display + proactive-expiry, public-page language/theme
  controls, `noindex` for temporary financial URLs, public table column set, public QR
  destination). None reopen the 5 resolved decisions. Awaiting the checkpoint before build.

### 2026-07-24 (implement — web-implementer)

- **All 8 Open Questions resolved by the orchestrator** against the confirmed backend contract
  (see the new Decision Log entry); recorded before implementing. The shipped error codes are
  `16000`/`16001` (the `15xxx` placeholders in the pre-contract draft are superseded).
- **Error-code mirror:** appended a `16xxx` block to `src/lib/api/errors.ts`
  (`ShareLinkNotFoundOrExpired: 16000`, `EventNotClosedForShare: 16001`).
- **New feature module `src/features/share/`:** `api/types.ts` (share DTOs, re-exporting
  `MemberBalanceRow`/`MemberQrResponse`), `api/shareApi.ts` (authed create/get/revoke +
  anonymous public report + public member-QR), `hooks/useShare.ts` (`shareKeys`,
  `useActiveShareLinkQuery` retry:false, `usePublicShareQuery` retry:false,
  `usePublicShareMemberQrsQuery` lazy/gcTime:0, `useCreateShareLink`/`useRevokeShareLink`),
  `pages/PublicSharePage.tsx` (minimal layout + locale toggle + `noindex` meta + loading/expired/
  success states), `components/PublicBalanceTable.tsx` (read-only balance table adapted from
  `EventBalanceTable`, per-row expand + lazy per-member QR → existing `QrPreviewDialog`),
  `components/MemberExpenseBreakdown.tsx`, `components/ShareEventDialog.tsx` (Premium-gated owner
  dialog: create form with optional bank picker, link view with copy/absolute-expiry/inline-confirm
  revoke+regenerate), plus CSS Modules for each.
- **Wiring:** ungated `share/:token` route added as a sibling under `RootLayout` in
  `src/routes/router.tsx`; `share` namespace (vi-VN + en-US) registered in `src/i18n/index.ts` +
  `src/i18n/useT.ts`; a Premium-gated, closed-only **Chia sẻ** button + `ShareEventDialog` added to
  `EventDetailPage`'s `DetailView` next to the QR action.
- **Endpoints consumed:** authed `GET/POST/DELETE /v1/events/{uuid}/share`; anonymous
  `GET /v1/public/shares/{token}` + `…/qr/members`.
- **MSW:** added share handlers (owner create/get/revoke + anonymous public report + public QR) to
  `src/test/msw/handlers.ts`, plus a one-shot admin demo seed (a closed event with owing members)
  and a canned `demo` public token, so the feature runs under `VITE_ENABLE_MOCKS=true`.
- **Quality gates:** `pnpm lint` clean (exit 0; only pre-existing fast-refresh warnings in
  unrelated files), `tsc -b` types clean, `pnpm build` succeeds. Ran the app with mocks +
  Playwright/chromium and observed: public report at `/share/demo` (summary + per-member table +
  status badges + QR buttons only on owing rows), member drill-in, the QR carousel opening at the
  clicked member's slide, the no-leak expired/not-found screen at an unknown token, and the owner
  flow (closed event → **Chia sẻ** → bank preselected → create → URL + absolute expiry + bank
  snapshot → copy `{origin}/share/{token}` → inline-confirm revoke). No new Open Questions.

### 2026-07-24 (test — web-test-engineer)

- Wrote the full Vitest + RTL test list from the plan's "Tests the web-test-engineer
  should write" section, network mocked at the client boundary (MSW), pinned vi-VN +
  Asia/Ho_Chi_Minh. **7 new files, 45 tests, all green** under `src/features/share/`:
  - **`shareI18n.test.ts`** (5) — vi-VN/en-US `share` namespace key-shape parity, no
    empty leaves, per-key interpolation-token parity (`{{name}}`/`{{time}}`/`{{amount}}`),
    fixed domain terms in vi-VN (Chia sẻ / Đã trả / phần gánh / Ví / Premium), and an
    en-US-is-not-a-vi-copy guard.
  - **`shareApi.test.ts`** (6) — the two PUBLIC endpoints send NO `Authorization` header
    even with an active session and do NOT trip refresh on a synthetic 401 (asserted the
    refresh endpoint is never called); authed get/create/delete hit the right verb+path,
    send the body, and unwrap `data` (incl. `data:null` → `null` for OQ1 "not shared yet").
  - **`publicSharePage.test.tsx`** (7) — loading skeleton → success report; header +
    summary (VND vi-VN grouping) + one rowheader per member; the 16000 friendly
    "link unavailable" screen with NO member data / token leaked AND identical copy across
    expired/revoked/missing tokens; `noindex,nofollow` robots meta present while mounted +
    removed on unmount; locale toggle switches vi→en copy; generic failure → retry that
    recovers.
  - **`publicBalanceTable.test.tsx`** (8) — one rowheader per member; read-only (no
    switch/checkbox in DOM) with color-independent status badges; QR button only for
    `hasQr && outstanding>0`; `hasQr:false` hides the QR column entirely; QR click lazily
    fetches once and opens `QrPreviewDialog` at the CLICKED member's slide (Counter/caption
    assert index 1 for the 2nd member, index 0 for the 1st); QR-fetch failure → danger toast,
    preview stays closed; expand toggle reveals `MemberExpenseBreakdown` with correct
    `aria-expanded`/`aria-controls`.
  - **`memberExpenseBreakdown.test.tsx`** (5) — groups `expenses[].shares` by `memberUuid`;
    shows the member's own share amount (not the expense total); annotates the payer's
    advanced total; multi-expense non-payer shows all shares + no advanced line; settled tag;
    empty note when the member has no shares.
  - **`shareEventDialog.test.tsx`** (10) — Free → UpgradePrompt with the active-link query
    NEVER firing; reactive 403 13003 → UpgradePrompt; no-link → create form with the default
    bank preselected; no-bank → /wallet hint + QR-less create (no `bankAccountUuid` in the
    body); active link → URL + copy + absolute expiry + bank snapshot + revoke/regenerate;
    copy writes `{origin}/share/{token}`; expired-link state hides the URL field; two-step
    inline-confirm Revoke (DELETE + toast) and Regenerate (POST `regenerate:true` + toast);
    open-event 16001 → warning alert; ownership 9000 → close-once + danger toast (no leak).
  - **`eventDetailShare.test.tsx`** (3) — the "Chia sẻ" button is closed-events-only and
    opens `ShareEventDialog`.
- **Extra coverage beyond the plan list:** interpolation-token parity in the i18n test;
  the `hasQr:false` hides-QR-column case; the QR-fetch-failure toast path; the retry-recovers
  path on the public page; the expired-link "no URL field" assertion.
- **Result:** `pnpm test` → **949/949 passing (114 files)**; `pnpm lint` clean (only the
  pre-existing unrelated fast-refresh warnings); `tsc -b` clean. **No product bug found** —
  all product code behaved to contract; only test files + no harness changes were added.
- One harness note: `renderWithProviders` mounts `ToastHost`, whose toast viewport is an
  `<ol>` (role `list`), so table/breakdown specs scope list assertions to `listitem`/text
  rather than a bare `getByRole("list")`. Recorded for future authors.

## Final Outcome

(pending)

## Future Improvements

- Share links for open events or for expenses (explicitly out of scope now — event/closed only).
- A "regenerate resets TTL" affordance + a visible countdown that live-updates.
- Owner-side history/telemetry (view count, last-opened) if the backend later exposes it.
- A per-visitor download/export of the public report (CSV/PDF) if requested.
- Configurable TTL (e.g. 1h / 1d / 7d) if the backend later supports it.
- A branded/OG-preview meta for the public URL (respecting the privacy `noindex` decision).

### 2026-07-24 — review closure (orchestrator)

- **Frontend code review: clean on all blocking axes** (privacy/no-leak, ungated routing, Premium +
  closed-only gating, money/time, i18n parity, reuse discipline). Applied all four non-blocking nits:
  - Bank-picker race — the create form now holds the skeleton while `accountsQuery.isPending` too, so
    an owner with accounts can't briefly hit the QR-less create path (OQ2).
  - Empty QR array — an info toast (`share:qr.empty*`) instead of a silent no-op.
  - Unused i18n keys — wired `share:public.documentTitle` to `document.title` (restored on unmount);
    dropped the dead `public.heading` + `breakdown.expense/time/shareAmount` keys (both locales, parity kept).
  - Heading skip — `MemberExpenseBreakdown` heading demoted `<h3>` → `<h2>`.
- Final: `tsc -b` clean, `oxlint` clean, `pnpm build` succeeds, `pnpm test` share suite **45 passed**
  (full suite 949 passed prior to the nits; share files + share.json only touched since).
