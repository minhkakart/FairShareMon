# M7 — Wallet (bank accounts) & QR

## Objective

Deliver the seventh feature milestone of the FairShareMonWeb roadmap: the **wallet** (a CRUD list of
the caller's receiving **bank accounts** with exactly one default) plus **on-demand VietQR image
generation** for a single expense (M4 detail) and for a **closed** event (M5 detail). This is the
product's **first Premium-gated feature**: wallet **reads** (list/get) stay Free, while every wallet
**mutation** (create / update / set-default / delete) **and both QR endpoints** are Premium →
`403 13003 PremiumFeatureRequired` → the `<UpgradePrompt>` affordance (informational only — Premium is
a manual admin grant, there is no self-serve endpoint). Replaces the `/wallet` `StubPage`; adds a
QR action to the expense detail (M4-MOD) and the closed-event QR action to the event detail (M5-MOD).

## Background

- **The roadmap entry** (`FairShareMonWeb/planning/feature-roadmap.md` M7, locked 2026-07-17):
  bank-account list (default first) + create/edit + set-default + delete (default-promotion handled
  server-side); per-expense QR PNG on the expense detail; per-event composite QR PNG (closed only) on
  the event detail; all Premium-gated (the OQ5b read-vs-mutation split); blob/image via `api.blob(...)`.
- **The backend is feature-complete and stable** (verified against the live controllers/DTOs):
  - `BankAccountsController` → `api/v1/bank-accounts`: `GET` (list, default-first), `GET /{uuid}`,
    `POST` (`CreateBankAccountRequest`), `PUT /{uuid}` (`UpdateBankAccountRequest`),
    `PUT /{uuid}/default` (atomic swap, success message), `DELETE /{uuid}` (success message,
    default-promotion). All resource-owned (miss → `404 12000 BankAccountNotFound`, never 403).
    Mutations are Premium (`403 13003`). Reads are Free.
  - `ExpensesController.GetQrAsync` → `GET api/v1/expenses/{uuid}/qr` with `?bankAccountUuid=` and
    `?format=payload`. Default: a PNG (`FileContentResult`, `image/png`, streamed unwrapped via the M8
    file-response bypass). `?format=payload`: the raw VietQR string in `ApiResult<string>`. Premium
    (`403 13003`); expense miss → `404 6000`; no bank account → `400 12001`; override miss → `404
    12000`.
  - `EventsController.GetQrAsync` → `GET api/v1/events/{uuid}/qr` with `?bankAccountUuid=`. Always a
    composite PNG (`FileContentResult`, `image/png`). Premium (`403 13003`); event miss → `404 9000`;
    open event → `400 12002 EventNotClosedForQr`; nobody owes → `400 12003 NoOutstandingDebtForQr`; no
    bank account → `400 12001 NoBankAccountForQr`.
  - Backend semantics: `FairShareMonApi/planning/wallet-and-qr.md` (17 OQs resolved) +
    `FairShareMonApi/planning/tiers-premium-free.md` + `The-ideal.md` §3.10.
- **DTO shapes (verified):**
  - `CreateBankAccountRequest` / `UpdateBankAccountRequest` = `{ bankBin, bankName, accountNumber,
    accountHolderName }` (all four; update never touches the default flag).
  - `BankAccountResponse` = `{ uuid, bankBin, bankName, accountNumber, accountHolderName, isDefault,
    createdAt }`.
  - Backend validators (mirror in Zod): `bankBin` `^\d{6}$`; `bankName` required, ≤100; `accountNumber`
    `^\d{6,19}$`; `accountHolderName` required, ≤100.
- **The frontend foundation + M1–M6 are shipped and reused wholesale:**
  - Blob path: `api.blob("GET", path, { query })` → `BlobResult { blob, filename, contentType }`;
    `downloadBlob(result, fallbackName)` (`src/lib/download/downloadBlob.ts`) already earmarked "reused
    by M5 event export + M7 QR" — creates an object URL, clicks a hidden `<a download>`, revokes on the
    next tick. QR PNG rides the exact same seam. Error responses on the blob path still arrive as the
    JSON envelope and throw a typed `ApiError` (`requestBlob` in `client.ts`).
  - Error handling: `ErrorCodes` already mirrors the full 12xxx + 13xxx blocks
    (`BankAccountNotFound 12000`, `NoBankAccountForQr 12001`, `EventNotClosedForQr 12002`,
    `NoOutstandingDebtForQr 12003`, `PremiumFeatureRequired 13003`). `classifyError` already maps
    `13003 → "premiumRequired"` and `12000 → "notFound"`; `resolveErrorMessage` renders the backend's
    localized `error.message` verbatim; `applyFieldErrors` maps `1001` field errors onto RHF.
  - Tier affordances: `<UpgradePrompt variant="cta"|"info"|"active">` + `<LimitNotice>` +
    `<TierBadge>` (all in `src/components/ui`, `Premium.tsx`/`TierBadge.tsx`); `useCurrentUser()`
    exposes the session `tier` (case-insensitive; absent/unknown → Free, non-privileged). M1's
    `TierStatusPanel` is the reference for the informational (no-navigation) Premium copy.
  - Design-system primitives available: `Table` family, `Dialog`/`DialogContent`/`DialogFooter`/
    `DialogClose`, `Card`/`CardHeader`/`CardBody`, `Badge`, `Button` (incl. `asChild`, `loading`,
    `iconStart`), `Select`, `TextField`, `Form`/`FieldStack`/`FormError`, `PageHeader`, `Stack`,
    `Money`, `Skeleton`, `EmptyState`, `ErrorState`, `Alert`, `Spinner`, toasts (`useToast`).
  - Established feature patterns (mirror exactly): `src/features/<area>/{api,hooks,components,pages}` +
    `schemas.ts`; TanStack Query key-factory + invalidation (`useMembers.ts`); RHF+Zod dialog with
    reactive limit/premium handling (`MemberFormDialog.tsx`); confirm-delete dialog
    (`DeleteMemberDialog.tsx`); list page with loading/empty/error (`MembersPage.tsx`); detail-page
    action wiring (`ExpenseDetailPage.tsx`, `EventDetailPage.tsx`).
- **Current state:** `/wallet` is a `StubPage` (`router.tsx` line 70-72). No `src/features/wallet/`
  tree exists yet. The i18n registry (`src/i18n/index.ts`) has no `wallet` namespace yet.

## Requirements

From the roadmap (M7), `The-ideal.md` §3.10, and the locked API contract:

- **R-W1 — Wallet list:** `/wallet` lists the caller's bank accounts default-first (backend order,
  rendered verbatim): bank name, account number (masked per OQ5), holder name, and a clear default
  marker. Loading / empty / error states.
- **R-W2 — Create / edit:** a modal form (bank name, BIN, account number, holder name) with Zod
  mirroring the backend validators; `1001` field errors mapped onto fields. Create + edit are Premium
  mutations.
- **R-W3 — Set default:** an atomic-swap action (`PUT /{uuid}/default`) — exactly one default whenever
  ≥1 account exists; the UI reflects the swap via cache invalidation, never client-side default logic.
  Premium mutation.
- **R-W4 — Delete:** a confirm dialog that explains the server's default-promotion behaviour (deleting
  the default promotes the most-recently-added remaining account; deleting the last leaves an empty
  wallet). Premium mutation.
- **R-W5 — Premium gate:** wallet reads are Free (a downgraded/never-Premium user can still *see* their
  accounts read-only); every mutation + both QR endpoints are Premium → `403 13003` →
  `<UpgradePrompt>`. Reflect the gate per OQ1 (proactive from session tier + reactive 403 fallback).
- **R-Q1 — Expense QR (M4-MOD):** a "show QR" action on `ExpenseDetailPage` that fetches
  `GET /expenses/{uuid}/qr` (PNG blob → object URL `<img>`), with download; Premium-gated; handles
  `12001` (no bank account → link to `/wallet`). Optional raw-payload handling per OQ4.
- **R-Q2 — Event QR (M5-MOD):** a "show settlement QR" action on `EventDetailPage`, **shown only for
  closed events**, fetching `GET /events/{uuid}/qr` (composite PNG → object URL `<img>`), with
  download; Premium-gated; handles `12003` (nobody owes → informational) and defensively `12002`.
- **R-X1 — Blob-image handling:** fetch as blob → `URL.createObjectURL` → `<img>`; **revoke on
  unmount / re-fetch**; download affordance; loading + error states.
- **R-X2 — Cross-cutting:** ownership 404 → shared `NotFound` never leaks existence (R1); render
  API money verbatim (R3); vi-VN default + en-US parity; a11y (labeled controls, alt text, focus,
  color-independent status).

## Open Questions

> Each carries a recommendation the orchestrator auto-accepts. **None is CRITICAL** — every one has a
> safe, reversible default. Flagged `needs-user` only where noted (none here).

> **All OQs RESOLVED 2026-07-17** (orchestrator auto-accept): OQ1 → (a) hybrid gate;
> OQ2 → (a) destination Select when ≥2 accounts; OQ3 → (a) modal QrDialog; OQ4 → (a)
> no raw payload, copy account details; OQ5 → (a) masked + reveal; OQ6 → (a) keep
> `tone="settled"` for the default marker. Implemented as such.

### OQ1 — How to reflect the Premium mutation gate: proactive, reactive, or hybrid? — RESOLVED 2026-07-17: (a) hybrid

Wallet reads are Free but every mutation + QR is Premium (`13003`). The session `tier` is known from
`useCurrentUser()`, but it can be stale (an admin grant/revoke doesn't push to the client until the
next `/auth/me`).

- **(a) [Recommended] Hybrid — proactive from session tier + reactive `13003` fallback.** For a Free
  user: render the wallet **read-only** — the "Add account" / row edit / set-default / delete controls
  are replaced by (or gated behind) an informational `<UpgradePrompt variant="info">` panel explaining
  wallet management is Premium (mirroring M1's `TierStatusPanel` no-navigation copy); QR action buttons
  stay visible but, on click for a Free user, open a dialog whose body is the `<UpgradePrompt>` instead
  of firing the request. **Additionally**, any mutation/QR that still reaches the server and returns
  `403 13003` (stale tier) is caught reactively and rendered as the same `<UpgradePrompt>` — the
  server is authoritative. Trade-off: two code paths, but it is the honest reflection of a
  manually-granted tier that can drift, and it never shows a Free user a control that will only fail.
- (b) Purely reactive — show every control to everyone; surface `<UpgradePrompt>` only on `403 13003`.
  Trade-off: simplest, always correct, but a Free user sees enabled controls that error on use (poor
  UX for the app's first gated feature).
- (c) Purely proactive — hide/disable mutation + QR controls entirely for Free by session tier.
  Trade-off: cleanest Free UX, but a stale-tier Premium user could be wrongly blocked with no server
  round-trip to correct it.

### OQ2 — QR destination account: expose a picker (`?bankAccountUuid=` override) or always use the default? — RESOLVED 2026-07-17: (a) Select when ≥2

§3.10 and the backend explicitly allow choosing a non-default account at generation time.

- **(a) [Recommended] Expose a destination `Select` in the QR dialog, defaulting to the default
  account, shown only when the wallet has ≥2 accounts** (otherwise the single/default account is used
  implicitly). Passes `?bankAccountUuid=` when the user picks a non-default. Trade-off: one small
  control, but honours the spec's "choose another account" intent at near-zero cost and reuses the
  Free-safe `useBankAccountsQuery` list already loaded.
- (b) Always use the default; no override in M7 (defer to Future Improvements). Trade-off: simpler
  dialog, but drops a spec-supported affordance.

### OQ3 — QR presentation: modal dialog or inline expandable section? — RESOLVED 2026-07-17: (a) modal QrDialog

- **(a) [Recommended] A modal `QrDialog`** launched from a "Show QR" button, consistent with the app's
  dialog-heavy pattern (edit/delete/close all use dialogs), keeps the detail pages uncluttered, and
  scopes the object-URL lifecycle to the dialog's mount. Trade-off: one extra click to view.
- (b) An inline card that expands under the detail. Trade-off: no click to reveal, but adds a large
  image block to already-dense detail pages and complicates the object-URL lifecycle.

### OQ4 — Raw VietQR payload (`?format=payload`) copy affordance on the expense QR? — RESOLVED 2026-07-17: (a) no payload; copy details

- **(a) [Recommended] Do NOT surface the raw payload string in M7.** The VietQR TLV string is a
  machine format for clients that render their own QR — it is not human- or banking-app-actionable
  (banks scan the image, they don't accept pasted TLV). Instead the dialog offers **download the image**
  and **copy the account number / holder name** (genuinely useful for a manual transfer). Trade-off:
  skips a backend capability, but avoids a confusing dead-end control; the payload endpoint remains
  available for a future "render your own QR" client.
- (b) Add a "copy VietQR string" secondary action calling `?format=payload`. Trade-off: exercises the
  full backend surface, but exposes a low-value string most users can't act on.

### OQ5 — Account-number display: mask or show in full? — RESOLVED 2026-07-17: (a) mask + reveal

The roadmap wording says "masked account number", but these are the owner's *own receiving* accounts
(the point is to receive money, and the user may want to verify the number before generating a QR).

- **(a) [Recommended] Mask by default (show last 4, e.g. `•••• 1234`) with a per-row click-to-reveal
  toggle** (`aria-pressed`, reveals the full grouped number). Honours the roadmap's "masked" wording
  while keeping the number verifiable on demand. Trade-off: one extra affordance per row.
- (b) Show the full account number always. Trade-off: matches the "verify before sharing" use case
  directly, but diverges from the roadmap's stated "masked".
- (c) Mask with no reveal. Trade-off: safest for shoulder-surfing, but the user can't confirm their own
  number.

### OQ6 — Default-account marker: match the M3 category default (gold star) or use `settled`? — RESOLVED 2026-07-17: (a) keep `settled`

Non-blocking, raised by the ui-designer during the design pass. Step 4.1 specifies
`Badge tone="settled"` (calm blue-gray) + a star for the default receiving account.
The M3 category default marker uses `tone="warning"` (gold) + a star. Both are
icon+text (color-independent), so either is accessible.

- **(a) [Recommended] Keep `tone="settled"`** — a default receiving account is a
  calm, ongoing state, not a caution; the gold warning tone reads as "attention
  needed". Distinct-domain treatment is acceptable. Delivered this way.
- (b) Match the category default (`tone="warning"` gold star) for a single
  cross-feature "default" visual language. One-token change if preferred.

## Assumptions

- No backend change is needed; every route/DTO above is live and stable. There is **no self-serve
  tier-upgrade endpoint** — the upgrade affordance is informational only.
- Free users may hold bank accounts (created while Premium, then downgraded); the read-only wallet must
  handle a non-empty list for a Free user, and an empty wallet for a never-Premium Free user (empty
  state doubles as the "wallet is a Premium feature" explainer).
- Set-default and delete-promotion are entirely server-side; the client never computes the default — it
  invalidates the list cache and re-renders the backend's `isDefault`.
- QR generation is a read (creates no rows, writes no audit); it is allowed on closed events (the event
  QR *requires* closed). QR responses are never cached beyond the dialog's lifetime (object URLs are
  revoked on unmount/re-fetch); the backend does not persist images.
- The expense-detail QR button follows the same closed-event rule as the rest of that page only insofar
  as QR is a *read* — it stays enabled on closed events (QR is explicitly read-allowed).
- The `wallet` i18n namespace is the home for all wallet + QR copy (including the QR strings consumed by
  the M4/M5 detail mods), registered in `src/i18n/index.ts`.
- MSW handlers for the bank-accounts CRUD + both QR endpoints (returning a small PNG blob) are added to
  the test harness by the web-test-engineer.

## Implementation Plan

> New feature tree: `src/features/wallet/`. Paths are workspace-relative to `FairShareMonWeb/`. All copy
> is vi-VN-first with en-US parity via the new `wallet` namespace. Concrete names below assume the
> recommended OQ options; the implementer adjusts if the checkpoint overrides one.

### Step 1 — API layer (`src/features/wallet/api/`)

1. `types.ts` — TypeScript mirrors of the DTOs:
   - `BankAccountResponse = { uuid; bankBin; bankName; accountNumber; accountHolderName; isDefault; createdAt }`
   - `CreateBankAccountRequest = { bankBin; bankName; accountNumber; accountHolderName }`
   - `UpdateBankAccountRequest` = same four fields.
2. `bankAccountsApi.ts` — over the centralized `api` client (mirror `membersApi.ts`):
   - `list()` → `api.get<BankAccountResponse[]>("/v1/bank-accounts")`
   - `get(uuid)` → `api.get<BankAccountResponse>("/v1/bank-accounts/{uuid}")` (reserved; likely unused
     in M7)
   - `create(body)` → `api.post<BankAccountResponse>("/v1/bank-accounts", body)`
   - `update(uuid, body)` → `api.put<BankAccountResponse>("/v1/bank-accounts/{uuid}", body)`
   - `setDefault(uuid)` → `api.put<MessageResponse>("/v1/bank-accounts/{uuid}/default")`
   - `remove(uuid)` → `api.delete<MessageResponse>("/v1/bank-accounts/{uuid}")`
3. `qrApi.ts` — QR lives in the wallet feature (routes are on expenses/events, but the concept + the
   `QrDialog` are wallet-owned, so both detail mods import one place):
   - `expenseQr(uuid, bankAccountUuid?)` → `api.blob("GET", "/v1/expenses/{uuid}/qr", { query: { bankAccountUuid } })`
   - `eventQr(uuid, bankAccountUuid?)` → `api.blob("GET", "/v1/events/{uuid}/qr", { query: { bankAccountUuid } })`
   - (Only if OQ4=b) `expenseQrPayload(uuid, bankAccountUuid?)` →
     `api.get<string>("/v1/expenses/{uuid}/qr", { query: { format: "payload", bankAccountUuid } })`

### Step 2 — Hooks (`src/features/wallet/hooks/`)

1. `useBankAccounts.ts` — key-factory `bankAccountsKeys = { all: ["bankAccounts"], list: () => [...] }`;
   `useBankAccountsQuery()` (Free-safe read); mutations `useCreateBankAccount`, `useUpdateBankAccount`,
   `useSetDefaultBankAccount`, `useDeleteBankAccount`, each `onSuccess` invalidating
   `bankAccountsKeys.all`. Toast/close side-effects stay in the components (established convention).
2. `useQr.ts` — `useExpenseQrQuery(uuid, bankAccountUuid, enabled)` and
   `useEventQrQuery(uuid, bankAccountUuid, enabled)` using `useQuery` returning `BlobResult`, `enabled`
   driven by the dialog's open state + Premium tier (a Free user's dialog shows the upgrade panel and
   never enables the query). `retry: false` for `13003`/`12xxx` (they're terminal, not transient). The
   dialog component owns the object-URL lifecycle (Step 4). No mutation — QR is a read.

### Step 3 — Zod schema (`src/features/wallet/schemas.ts`)

`bankAccountFormSchema(t)` mirroring the backend validators exactly (see `validation` namespace for
messages):
- `bankBin`: `z.string().regex(/^\d{6}$/, …)` — "BIN gồm đúng 6 chữ số".
- `bankName`: `z.string().trim().min(1).max(100)`.
- `accountNumber`: `z.string().regex(/^\d{6,19}$/, …)` — "Số tài khoản gồm 6–19 chữ số".
- `accountHolderName`: `z.string().trim().min(1).max(100)`.
Export `type BankAccountFormValues`.

### Step 4 — Components (`src/features/wallet/components/`)

1. `BankAccountsTable.tsx` (+ `.module.css`) — `Table` family; columns: bank name (with the default
   `<Badge tone="settled">` marker), masked account number (OQ5a: `•••• 1234` + reveal toggle button),
   holder name, actions. Actions per row: "Set default" (hidden on the current default), "Edit",
   "Delete" — all gated by tier per OQ1 (rendered only for Premium; a Free user sees a read-only table
   with no action column, or disabled actions with the upgrade panel above).
2. `BankAccountFormDialog.tsx` — `mode: "create" | "edit"`, mirroring `MemberFormDialog.tsx`: RHF +
   `zodResolver(bankAccountFormSchema(t))`; fields = `TextField` × bank name, BIN (inputMode numeric,
   maxLength 6), account number (inputMode numeric, maxLength 19), holder name. On submit: create/update
   mutation; `applyFieldErrors` maps `1001` onto fields; **`13003` caught reactively → render an inline
   `<UpgradePrompt variant="cta">`** (form stays open, mirroring the `LimitNotice` pattern);
   `12000` (edit of a stale/deleted account) → toast + close; success → toast + close + invalidate.
3. `SetDefaultButton.tsx` (or inline in the table) — calls `useSetDefaultBankAccount`; success toast;
   `13003` → toast/UpgradePrompt; `12000` → toast + refetch (stale row).
4. `DeleteBankAccountDialog.tsx` — confirm dialog (mirror `DeleteMemberDialog.tsx`); the body explains
   the default-promotion behaviour (`wallet:delete.bodyDefault` when deleting the default,
   `wallet:delete.body` otherwise); `13003`/`12000` surface localized text + close; success →
   toast + invalidate.
5. `QrDialog.tsx` (+ `.module.css`) — **the net-new, shared QR display component.** Props:
   `{ open, onOpenChange, kind: "expense" | "event", targetUuid, title }`. Behaviour:
   - Reads `useCurrentUser()`; if Free → dialog body is `<UpgradePrompt variant="cta">` (proactive,
     OQ1a), query never enabled.
   - Optional destination `Select` (OQ2a) populated from `useBankAccountsQuery()`, shown when ≥2
     accounts; drives `bankAccountUuid`.
   - Enables `useExpenseQrQuery`/`useExpenseQrQuery` (by `kind`); on success creates an object URL from
     the `BlobResult` in a `useEffect` and renders `<img alt={t("wallet:qr.imageAlt")}>`; **revokes the
     URL on unmount and whenever the blob/destination changes**.
   - States: loading (`Spinner`/`Skeleton`), error branching on code — `13003` → `<UpgradePrompt>`;
     `12001` → `<EmptyState>` with a `Button asChild`→`<Link to="/wallet">` "add a bank account";
     `12003` → informational `<Alert tone="info">` "nobody owes"; `12002` (defensive) → info alert
     "close the event first"; `6000`/`9000` → treat as not-found (close + toast); else `ErrorState`
     with retry.
   - Actions: "Download" (calls `downloadBlob(result, fallbackName)`); "Copy account details" (OQ4a) —
     copies holder name + account number to the clipboard.

### Step 5 — Wallet page (`src/features/wallet/pages/WalletPage.tsx` + `.module.css`)

Replaces the `/wallet` stub. Mirrors `MembersPage.tsx`:
- `PageHeader` (title + subtitle + `TierBadge` from session tier); an "Add account" primary button
  (Premium only per OQ1a; for Free, omitted and the read-only upgrade panel shown instead).
- Reads `useBankAccountsQuery()`; renders loading skeleton rows / `ErrorState` (retry) / `EmptyState`
  (empty; Free empty state = the "wallet is a Premium feature" explainer with `<UpgradePrompt
  variant="info">`; Premium empty state = "add your first account") / `BankAccountsTable`.
- For a **Free user with existing accounts**: a top-of-page `<UpgradePrompt variant="info">` banner
  ("managing accounts is a Premium feature") + the read-only table (no action controls).
- Owns dialog open-state for `BankAccountFormDialog` (create/edit) and `DeleteBankAccountDialog`.

### Step 6 — Router (`src/routes/router.tsx`)

Replace the `/wallet` `StubPage` (line 70-72) with `element: <WalletPage />` (import from
`@/features/wallet/pages/WalletPage`). No new nested routes (QR is dialog-driven from existing detail
routes).

### Step 7 — M4-MOD: Expense QR entry point (`ExpenseDetailPage.tsx`)

Add a "Show QR" `Button` (icon: a new `QrIcon` in `expenses/components/icons.tsx` or reuse a wallet
icon) to `DetailView`'s `detailActions`, **enabled even on closed events** (QR is read-allowed).
Local `qrOpen` state renders `<QrDialog kind="expense" targetUuid={expense.uuid}
title={t("wallet:qr.expenseTitle")} … />`. The button is visible to all; the dialog handles the
Free→UpgradePrompt and the `12001` no-account cases. No change to the existing edit/delete/settled/
export/audit wiring.

### Step 8 — M5-MOD: Event settlement QR entry point (`EventDetailPage.tsx`)

Add a "Show settlement QR" `Button` to `DetailView`'s `detailActions`, **rendered only when
`event.isClosed`** (matching the backend `12002` closed-only rule; keeps the open-event UI clean).
Local `qrOpen` state renders `<QrDialog kind="event" targetUuid={event.uuid}
title={t("wallet:qr.eventTitle")} … />`, which handles `12003` (nobody owes) informationally and the
Free→UpgradePrompt / `12001` no-account cases. No change to the existing close/edit/delete/export/
balance wiring.

### Step 9 — i18n (`src/i18n/`)

Register a new `wallet` namespace (add imports + `resources` + `NAMESPACES` in `index.ts`); create
`locales/vi-VN/wallet.json` + `locales/en-US/wallet.json`. Add the shared validator messages to the
existing `validation` namespace. Key groups (vi-VN authoritative, en-US parity):

- `wallet:title`, `wallet:subtitle`
- `wallet:add`, `wallet:table.caption`, `wallet:table.bank`, `wallet:table.accountNumber`,
  `wallet:table.holder`, `wallet:table.default`, `wallet:table.actions`, `wallet:table.reveal`,
  `wallet:table.hide`, `wallet:badge.default`
- `wallet:empty.title`, `wallet:empty.body`, `wallet:empty.premiumTitle`, `wallet:empty.premiumBody`
- `wallet:error.title`, `wallet:error.retry`
- `wallet:form.createTitle`, `wallet:form.editTitle`, `wallet:form.bankNameLabel`,
  `wallet:form.binLabel`, `wallet:form.accountNumberLabel`, `wallet:form.holderLabel`,
  `wallet:form.submitCreate`, `wallet:form.submitEdit`, `wallet:form.cancel`
- `wallet:setDefault.action`, `wallet:setDefault.toast`
- `wallet:delete.title`, `wallet:delete.body`, `wallet:delete.bodyDefault`, `wallet:delete.confirm`,
  `wallet:delete.cancel`, `wallet:delete.toast`
- `wallet:toast.created`, `wallet:toast.updated`, `wallet:toast.deleted`
- `wallet:premium.title`, `wallet:premium.info`, `wallet:premium.gateTitle`, `wallet:premium.gateBody`
  (the `13003` UpgradePrompt copy — informational, no navigation)
- `wallet:qr.expenseTitle`, `wallet:qr.eventTitle`, `wallet:qr.show`, `wallet:qr.imageAlt`,
  `wallet:qr.download`, `wallet:qr.downloadName`, `wallet:qr.copyDetails`, `wallet:qr.copied`,
  `wallet:qr.destinationLabel`, `wallet:qr.loading`, `wallet:qr.errorTitle`, `wallet:qr.noAccountTitle`,
  `wallet:qr.noAccountBody`, `wallet:qr.addAccount`, `wallet:qr.noDebt`, `wallet:qr.notClosed`
- `validation:bankBin`, `validation:accountNumber` (+ reuse existing required/max-length keys)

### Step 10 — Tests (owned by the web-test-engineer; definitive list)

All at the MSW client boundary, pinned TZ + locale, Vitest + RTL. Add MSW handlers for the six
bank-accounts routes + the two QR routes (QR handlers return a tiny PNG `Blob` and can be switched to
`403 13003` / `400 12001` / `400 12003` per test).

- `schemas.test.ts` — `bankAccountFormSchema`: BIN non-6-digit rejected, `^\d{6}$` accepted; account
  number outside `^\d{6,19}$` rejected; required + max-length on bank name / holder; valid payload
  passes.
- `useBankAccounts.test.tsx` — list query resolves; each mutation invalidates `bankAccountsKeys.all`;
  set-default + delete re-fetch.
- `walletPage.test.tsx` — Premium: renders default-first list, add/edit/set-default/delete controls
  present; empty state (Premium) offers "add first account". Free: read-only table + informational
  `UpgradePrompt`, no mutation controls; Free empty state = Premium-feature explainer. Error + loading
  states. Account-number masking + reveal toggle (OQ5a).
- `bankAccountFormDialog.test.tsx` — client validation errors; `1001` field mapping onto BIN/account;
  `13003` → inline `UpgradePrompt`, form stays open; `12000` on edit → toast + close; success → toast.
- `deleteBankAccountDialog.test.tsx` — default vs non-default body copy; success toast + invalidate;
  `13003`/`12000` surface localized text + close.
- `qrDialog.test.tsx` — Premium expense: enables query, renders `<img>` from the blob, download calls
  `downloadBlob`, object URL revoked on unmount; `12001` → no-account empty state with `/wallet` link;
  `13003` → UpgradePrompt (query not fired for Free proactively; fired-then-403 handled reactively);
  destination `Select` shown only with ≥2 accounts (OQ2a); event kind: `12003` → informational alert,
  `12002` defensive alert.
- `expenseDetailPage.test.tsx` (extend M4 tests) — "Show QR" button present + enabled on closed
  events; opens `QrDialog`.
- `eventDetailPage.test.tsx` (extend M5 tests) — "Show settlement QR" button present only when closed;
  absent when open; opens `QrDialog`.
- `walletI18n.test.ts` — vi-VN ↔ en-US key parity for the `wallet` namespace (+ new `validation` keys).

## Impact Analysis

- **APIs / Database / Services:** none — consumes existing, stable endpoints; no backend change.
- **Frontend:**
  - New `src/features/wallet/` tree (`api/`, `hooks/`, `components/`, `pages/`, `schemas.ts`).
  - Router: `/wallet` stub → `WalletPage`.
  - M4-MOD: `ExpenseDetailPage.tsx` (+ a `QrIcon`) gains a QR action.
  - M5-MOD: `EventDetailPage.tsx` gains a closed-only QR action.
  - i18n: new `wallet` namespace (vi-VN + en-US) registered in `index.ts`; new `validation` keys.
  - Reuses `downloadBlob` + `api.blob`, `UpgradePrompt`/`LimitNotice`/`TierBadge`, `useCurrentUser`,
    `classifyError`/`resolveErrorMessage`/`applyFieldErrors`, and the full `components/ui` primitive
    set — **no parallel systems**.
- **Design system:** one net-new composite component — `QrDialog` (image + download + copy-details +
  optional destination picker + loading/error/gate states). If the ui-designer judges it reusable
  beyond wallet, it could graduate to `components/ui`; default home is `features/wallet/components`. All
  other wallet UI reuses existing primitives.
- **Documentation:** this doc; the roadmap's M7 row is realized (no roadmap edit needed).
- **Downstream:** M8 (Admin) is independent. This is the milestone that first exercises the
  Premium-mutation gate end-to-end; the end-of-work tier-UX consistency sweep will confirm the wallet's
  `UpgradePrompt` copy reads coherently with M1/M2/M4/M5.

## Decision Log

### Decision

Build M7 as a new `src/features/wallet/` vertical (bank-account CRUD + set-default + delete) plus a
shared `QrDialog` consumed by the M4 expense-detail and M5 event-detail pages, reusing the shipped
blob path, tier affordances, and CRUD/dialog patterns; reflect the Premium mutation gate with a hybrid
proactive-plus-reactive approach.

### Reason

The API contract is fixed and the foundation + M1–M6 already provide every primitive, the blob/download
seam, and the error-code mirror (12xxx + 13xxx already present). Centralizing QR in the wallet feature
avoids duplicating the object-URL/blob logic across two detail pages. The hybrid gate (OQ1a) is the
honest reflection of a manually-granted tier that can drift, and never dangles a control that only
errors.

### Alternatives Considered

- Purely reactive gate (OQ1b) — rejected as default: poor first-gated-feature UX.
- Purely proactive gate (OQ1c) — rejected as default: a stale-tier Premium user could be wrongly
  blocked with no server correction.
- Placing QR api/components inside `features/expenses` + `features/events` — rejected: duplicates the
  blob/object-URL logic; wallet-ownership keeps it in one place.
- Exposing the raw VietQR payload string to users (OQ4b) — rejected as default: not user-actionable.

## Progress Log

### 2026-07-17

- Feature-planner drafted this M7 plan. Required reading completed: the frontend roadmap M7 entry +
  locked decisions; `FairShareMonWeb/CLAUDE.md`; the live `BankAccountsController` + `Models/Wallet/**`
  (`BankAccountResponse`, `Create/UpdateBankAccountRequest`, `QrImageResult`/`ExpenseQrResult`); the QR
  routes on `ExpensesController.GetQrAsync` (`?bankAccountUuid=`, `?format=payload`, PNG-or-payload) and
  `EventsController.GetQrAsync` (closed-only composite PNG); the backend
  `planning/wallet-and-qr.md` (17 OQs resolved — 12xxx codes, default invariant, VietQR, destination
  override, hard-delete) and the read-vs-mutation Premium split; and the shipped frontend surfaces to
  reuse — `downloadBlob` + `api.blob`/`requestBlob`, `ErrorCodes`/`classifyError`/`resolveErrorMessage`/
  `applyFieldErrors`, `UpgradePrompt`/`LimitNotice`/`TierBadge`, `useCurrentUser`, the M2 CRUD +
  dialog patterns (`MembersPage`, `MemberFormDialog`, `DeleteMemberDialog`), and the M4/M5 detail pages.
- Produced the implementation plan (wallet feature tree + M4/M5 QR mods), the Zod schema mirroring the
  backend validators, the hybrid Premium-gate UX, the i18n key set, and the test list; recorded 5 Open
  Questions with recommendations (none CRITICAL — all have safe defaults).
- Awaiting the checkpoint (orchestrator auto-accepts the recommended option per OQ) before the
  ui-designer + web-implementer cycle.

### 2026-07-17 (ui-designer — design pass)

- Added the M7 design spec as a reviewable showcase (light + dark), consistent
  with the M4/M5/M6 showcase pattern; no new tokens, no new dependency, all
  surfaces compose existing `components/ui` primitives.
- Files: `src/styles/M7Showcase.tsx` + `src/styles/M7Showcase.module.css` (new);
  mounted in `src/styles/StyleGuide.tsx` after M6; documented in
  `src/styles/README.md` (new "Wallet & QR (M7)" section).
- **QrDialog** delivered as a presentational state machine (the implementer wires
  data + the blob object-URL lifecycle): a `state` discriminated union
  (`loading` · `ready{imageUrl}` · `premiumGate` · `noAccount` · `noDebt` ·
  `notClosed` · `error`), a fixed-aspect QR frame per `kind` (expense square /
  event portrait) so the loading skeleton and image share one footprint,
  a human-readable account block as the accessible + copy channel, an optional
  destination `Select` (≥2 accounts), and Download + Copy-details footer actions.
  The QR frame is deliberately light-ground in both themes (a QR must scan). Both
  the live Radix dialog and inline per-state previews are shown.
- **Wallet list** delivered: masked account number (`•••• 1234`, mono/tabular)
  with a per-row `aria-pressed` reveal toggle, bank + BIN, holder, a
  `Badge tone="settled"`+star default marker, and the OQ1a Free read-only /
  Premium-managed split (info `UpgradePrompt` banner + no-action table for Free;
  set-default/edit/delete for Premium), plus empty/loading/error states.
- `tsc -b`, `pnpm lint` (no new warnings), and `pnpm build` are clean.
- One deliberate divergence flagged for the checkpoint (non-blocking): the default
  marker uses `tone="settled"` per this plan's Step 4.1, which differs from the M3
  category-default marker (gold `tone="warning"` + star). Kept as planned; if a
  single "default" visual language across categories + wallet is preferred, it is a
  one-token change. Recorded as OQ6 below.

### 2026-07-17 (web-implementer — build)

- OQ1–OQ6 resolved at the recommended option (a). Built the M7 wallet feature +
  the shared `QrDialog`, and the M4/M5 detail-page QR entry points.
- **New feature tree** `src/features/wallet/`:
  - `api/types.ts`, `api/bankAccountsApi.ts` (6 routes), `api/qrApi.ts`
    (`api.blob` for both QR routes).
  - `hooks/useBankAccounts.ts` (key factory `["bank-accounts"]`; list + create /
    update / set-default / delete, each invalidating the root; list read takes an
    `enabled` flag so the QR dialog defers it), `hooks/useQr.ts`
    (`useExpenseQrQuery` / `useEventQrQuery`, `retry:false`, `gcTime:0`, enabled by
    Premium + open).
  - `schemas.ts` (Zod mirroring the backend: BIN `^\d{6}$`, account `^\d{6,19}$`,
    names ≤100), `format.ts` (mask/group helpers), `components/icons.tsx`.
  - `components/BankAccountsTable.tsx` (+ `.module.css`) — masked number + per-row
    reveal toggle (OQ5a), `Badge tone="settled"`+star default (OQ6), Free/Premium
    action split; `components/BankAccountFormDialog.tsx` (create/edit, `1001`
    field-mapping, inline `UpgradePrompt` on `13003`, toast+close on `12000`);
    `components/DeleteBankAccountDialog.tsx` (default-promotion body copy);
    `components/QrDialog.tsx` (+ `.module.css`) — owns the query, error-code→state
    mapping, and the blob object-URL lifecycle (create/revoke on unmount / re-fetch
    / destination change), destination Select (≥2, OQ2a), Download + Copy-details
    (OQ4a), all seven states.
  - `pages/WalletPage.tsx` (+ `.module.css`) at `/wallet` — hybrid Premium gate
    (OQ1a), loading/empty/error, create/edit/set-default/delete wiring.
- **Router:** `/wallet` stub → `WalletPage` (removed the now-unused `StubPage`
  import).
- **M4-MOD** `ExpenseDetailPage`: "Xem mã QR" action (enabled on closed events —
  QR is a read) opening `QrDialog kind="expense"` with `amount={expense.total}`.
- **M5-MOD** `EventDetailPage`: "Xem mã QR quyết toán" action rendered only when
  `isClosed`, opening `QrDialog kind="event"`.
- **i18n:** new `wallet` namespace (vi-VN + en-US) registered in `index.ts` +
  `useT.ts`; new `validation:bankAccount.*` keys in both locales.
- **MSW:** added the six bank-account routes + both QR routes (tiny PNG blob;
  reads Free, mutations + QR gated to Premium profiles) to the shared handlers so
  the flows run under `VITE_ENABLE_MOCKS` and the harness.
- **Bug found + fixed during verification:** the `QrDialog` ownership-404 effect
  depended on the `toast` context value (recreated every render) while itself
  pushing a toast → an infinite render loop in production. Guarded with a one-shot
  ref so it fires exactly once per error.
- **Quality:** `pnpm lint` clean (only pre-existing fast-refresh warnings),
  `tsc -b` clean, `pnpm build` succeeds, full suite `570 passed`. Drove the real
  components against MSW (Premium list/create/set-default/delete, mask/reveal, Free
  read-only + gate, QR ready `<img>` from blob + download via object URL, Free
  proactive gate with no query fired, `12001`/`12003` states, `6000` close+toast)
  and booted the dev server with mocks (HTTP 200). Not exercised against the live
  `:5200` backend (not running in this environment); the binary PNG path was
  verified via the MSW blob → object-URL → `<img>` → `downloadBlob` seam.

### 2026-07-17 (web-test-engineer — tests)

- Added the M7 test suite (Vitest + RTL, MSW at the client boundary, pinned TZ +
  vi-VN locale, per-test store isolation via a fresh username). Full suite:
  **570 → 661 passed** (+91), green twice consecutively; `pnpm lint` clean (only
  pre-existing fast-refresh warnings), `tsc -b` clean.
- **Harness (additive, committed):**
  - `src/test/setup.ts` — polyfilled `URL.createObjectURL`/`revokeObjectURL`
    (absent in jsdom; needed by the QrDialog blob→`<img>` path + downloadBlob),
    guarded/inert like the M4 Radix polyfills so `vi.spyOn` can wrap them.
  - `src/test/msw/handlers.ts` — exported a test-only `registerTestProfile(username,
    tier)` so the committed Premium-gated wallet/QR handlers treat a fresh
    per-test user as PREMIUM (exercising the REAL atomic default-swap +
    delete-promotion + validation). Additive; browser-mock demo/admin untouched.
- **New spec files under `src/features/wallet/`:**
  - `schemas.test.ts` (18) — BIN `^\d{6}$`, account `^\d{6,19}$` bounds (6/19
    accept, 5/20/letters reject), required + ≤100 names, trim-on-parse.
  - `format.test.ts` (4) — `maskAccount` (`•••• 1234`) + `groupAccount` grouping.
  - `useBankAccounts.test.tsx` (8) — `bankAccountsKeys` scoping;
    list/create/update/setDefault/delete each invalidate `["bank-accounts"]` and
    refetch; `enabled` defers the read.
  - `useQr.test.tsx` (7) — expense+event blob queries `enabled`-gated (no request
    when disabled), resolve a `BlobResult`, `retry:false` (terminal 403 fires
    once), and pass `?bankAccountUuid=` for a non-default destination.
  - `walletPage.test.tsx` (16) — Premium default-first list, controls, mask+reveal
    (`aria-pressed`), create/edit/set-default (exactly-one-default swap)/delete
    (default-promotion copy + promotion reflected); Premium empty state; Free
    read-only + informational UpgradePrompt (no action controls), Free reveal
    (read-safe), Free empty explainer, stale-Premium `403` reactive toast; loading
    skeleton + error→retry.
  - `bankAccountFormDialog.test.tsx` (7) — client validation blocks with no
    request; `1001`→BIN/account field mapping; `13003`→inline UpgradePrompt (form
    stays open); `12000` edit→toast+close; create/edit success→toast+close+prefill.
  - `deleteBankAccountDialog.test.tsx` (6) — default vs non-default body copy;
    success toast+close; cancel (no request); `13003`/`12000`→localized toast+close.
  - `qrDialog.test.tsx` (17) — ready `<img>` from the blob object URL, Download →
    `downloadBlob(BlobResult)`, Copy-details copies holder+number+bank (not a TLV),
    revoke-on-unmount; destination Select only with ≥2 accounts + drives
    `?bankAccountUuid=`; Free proactive gate (query never fires) + reactive `13003`;
    `12001`→no-account EmptyState w/ `/wallet` link; event `12003` info alert /
    `12002` defensive warning; generic error→retry; **`6000`/`9000` ownership 404 →
    close+toast exactly once (regression guard on the fixed render-loop: `open`
    held true, `onOpenChange` asserted called once after a settle delay)**; en-US
    gate copy.
  - `walletI18n.test.ts` (5) — vi-VN↔en-US key-shape parity for `wallet` +
    `validation.bankAccount.*`, no empty leaves, fixed domain terms.
- **Extended detail specs:** `expenseDetailPage.test.tsx` (+3) — "Xem mã QR"
  present + enabled (incl. on a closed event — QR is a read) + opens the dialog;
  `eventDetailPage.test.tsx` (+3) — "Xem mã QR quyết toán" hidden when open, shown
  when closed, opens the dialog.
- **No product bugs found.** The QrDialog one-shot 404 guard holds under the
  regression probe (no repeated toast/`onOpenChange` storm). Coverage gaps: none
  material for M7; the binary-PNG seam is exercised via MSW blob → object-URL →
  `<img>` → `downloadBlob` (not against the live `:5200` backend, per environment).

## Final Outcome

**Complete.** M7 shipped the Wallet & QR feature (`src/features/wallet/`). Wallet page (`/wallet`): bank-account list (default-first, masked `•••• 1234` + per-row reveal, holder, default `Badge`), create/edit (`BankAccountFormDialog`, Zod BIN `^\d{6}$` / account `^\d{6,19}$` / names ≤100), atomic set-default (exactly-one-default), delete (server-side default-promotion, messaged). Shared **`QrDialog`**: blob → object-URL `<img>` (revoked on unmount/refetch/destination change), download via `downloadBlob`, copy account details (not the raw TLV), destination `Select` when ≥2 accounts, and a full state union incl. `12001` no-account / `12003` no-debt / `12002` not-closed / ownership-404 close+toast. **Hybrid Premium gate**: proactive from `useCurrentUser().tier` (Free → read-only wallet + informational UpgradePrompt, QR query never fires) + reactive on `403 13003`; wallet reads stay Free. QR entry points added to the expense detail (M4-MOD, enabled even on closed events) and the closed-event detail (M5-MOD). Consumes the 6 bank-account routes + both QR routes via `api.blob`. No new dependency; 12xxx/13xxx codes pre-existed. A real infinite-render-loop bug (QrDialog 404 effect vs. the recreated toast context value) was found and fixed during implementation with a one-shot ref guard + regression test. Tests +91 (suite 570→661); code review **APPROVE, 0 blocking** (sole nit = the documented white-QR-background exception, no change needed). All 6 OQs shipped at recommended.

## Future Improvements

- A "render your own QR" client path using the `?format=payload` string (deferred per OQ4a).
- Web Share API (`navigator.share`) for the QR image on mobile (native share sheet), beyond download.
- Graduate `QrDialog` to `components/ui` if a second consumer appears.
- Optimistic set-default (swap the `isDefault` flag locally before the round-trip) if the invalidation
  refetch feels sluggish on large wallets.
- A bank picker (BIN ↔ name/logo) reference dataset on the client to replace the free-text bank name +
  BIN fields, once a maintained NAPAS list is chosen (backend stores BIN + name verbatim today).
- E2E (Playwright) coverage of the full Premium wallet + QR loop once a Premium test account exists.
