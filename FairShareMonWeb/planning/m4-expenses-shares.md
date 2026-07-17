# M4 — Expenses & Shares (the core)

## Objective

Build the FairShareMonWeb ledger core: the expense list with the full filter set, the expense
**detail** view (info + shares breakdown + change history), the **atomic create** form (general info +
payer/category defaults + the owner-rep-0đ share editor), general-info **edit**, **delete** (cascade
+ surviving audit), the **settled** toggle, the **share sub-CRUD** (add/edit/remove/change-member on an
existing expense), the per-expense **audit history** timeline, and the per-expense **CSV export**. All
against the feature-complete, stable `api/v1/expenses` controller and its shares/settled/history/export
sub-routes, on the locked foundation stack (M1–M3 primitives, hooks, and conventions). This is the hub
the rest of the app feeds (M2 members, M3 categories/tags) and depends on (M5 events, M6 stats, M7 QR).

## Background

- **Roadmap:** M4 is the core, size **L**, in `planning/feature-roadmap.md` (locked 2026-07-17, all 6
  roadmap OQs at option a). It may be split into sub-phases at its own checkpoint — this doc recommends
  a two-phase split within the single cycle (see OQ2).
- **Backend is feature-complete and stable.** `Controllers/ExpensesController.cs` exposes ~13 routes
  (list, get, create, update, delete, settled, event assign/remove [M5], share add/update/delete,
  export, qr [M7], history). Backend semantics + all 20 backend OQs are locked in
  `FairShareMonApi/planning/expenses-shares-audit.md`. The atomic expense+shares transaction, owner-rep
  0đ auto-inject + protection (`7002`/`7003`), derived total (`total = SUM(shares.amount)`, no stored
  column), same-owner cross-link validation (`6001/6002/6003/7001`), hard-delete + surviving immutable
  audit, ownership 404 (`6000`/`7000`), and the settled flag are all backend-side; the UI mirrors and
  surfaces them.
- **§4 mandatory rules this milestone honors** (`The-ideal.md` §3.5, §3.8, §4): R1 absolute privacy
  (ownership miss = 404 → not-found, never leak existence); R2 same-owner link integrity (option lists
  only show the caller's own **active** members/categories/tags); R3 money exact (render the API-derived
  `total`; the share editor sums for **display only**, never authoritative; never float-math); R4
  closed-event immutability (disable every write control **except** the settled toggle when
  `eventIsClosed` — inert in M4 until M5 creates events, but wired now); R5 atomic create (one submit
  builds expense + shares); R7/R8 soft-deleted members/categories/tags are unselectable for new data but
  shown in historical detail with a "(đã xóa)" treatment; R9 the Free monthly-expense limit (`13002`)
  blocks only create, never existing data.
- **Shipped foundation reused (do not fork):** the centralized `api` client (`api.get/post/put/delete`
  + `api.blob`), the envelope unwrap + typed `ApiError` (`code`/`message`/`fields`), `ErrorCodes` mirror
  + `classifyError`/`resolveErrorMessage`/`applyFieldErrors`, TanStack Query hook pattern (query-key
  factory + `invalidateQueries` on mutate), RHF + Zod localized-factory schemas, react-i18next
  namespaces, `formatMoneyVnd`/`formatDateTime`/`getTimeZone`. Design system: `Table` family,
  `Dialog`, `Money`, `Badge`, `CategoryMarker`, `Button` (incl. `asChild`), `PageHeader`/`Stack`,
  `DescriptionList`/`DescriptionRow`, `Card`, `Form`/`FieldStack`/`FormError`/`FormActions`,
  `TextField`, `EmptyState`/`ErrorState`/`Skeleton`, `LimitNotice`/`UpgradePrompt`, toasts.
- **M2/M3 hooks reused for pickers:** `useMembersQuery(false)` (payer + share members; owner-rep via
  `isOwnerRepresentative`), `useCategoriesQuery(false)` (category; default via `isDefault`),
  `useTagsQuery(false)` (tag set). All fetch active-only for selection (R8), rendered verbatim in
  backend order.
- **Net-new design-system need:** no `Select`/combobox, multi-select, or money-input primitive exists
  yet (M2/M3 used only `TextField`, `ColorPicker`, `IconPicker`). M4's forms (payer/category select, tag
  multi-select, share amount inputs) require them — a **ui-designer pass is warranted** (see Design note
  + OQ8/OQ9).
- **Carried-forward M3 review nits** (apply where M4 touches those areas): (1) delete/confirm dialogs
  currently close on ANY error (`onOpenChange(false)` in `finally`) — M4's delete-expense and
  delete-share confirms should close only on success + terminal codes, keeping network/transient errors
  open for in-place retry (OQ12); (2) emoji SR labels — cosmetic, `CategoryMarker` reuse inherits it.

## Requirements

### Functional

1. **List (`/expenses`)** — the caller's expenses in backend order (`expenseTime` DESC), summary
   columns: name, payer, category (`CategoryMarker`), total (`Money`), expense time, settled state, and
   event/loose indicator. Loading (skeleton rows), empty (EmptyState + "add expense"), and error
   (ErrorState + retry) states.
2. **Filters** — the full API filter set combined AND: date range (`from`/`to`, inclusive), category,
   tag, settled/unsettled, and loose-only. (The event-select filter needs the M5 events list; M4 wires
   loose-only + shows the event column, defers the event picker — OQ7.) Filters reflected in the URL.
3. **Detail (`/expenses/:uuid`)** — full expense info, the shares breakdown table (member, amount, note,
   derived total), tags, category, payer, settled state, event linkage (read-only), plus the audit
   history timeline and the export + settled actions. Ownership 404 → shared not-found view (R1).
4. **Create (`/expenses/new`)** — atomic form: name, description, expense time, payer (defaults to
   owner-rep), category (defaults to the default category), tag set, and the **share editor** (rows of
   member + amount + note; the owner-rep 0đ row auto-present, pinned, non-removable, member locked;
   add/remove rows; live display-only sum). Single submit → `POST /expenses` (R5).
5. **Edit general info** — name/description/expense time/payer/category/tag set (full replace), via a
   dialog on the detail page. Does **not** touch shares.
6. **Delete** — confirm dialog explaining hard-delete + cascade of shares, with the surviving-audit
   meaning stated. On success return to the list.
7. **Settled toggle** — its own action (list row + detail), immediate mutate, no confirm; the one write
   allowed on a closed-event expense (R4).
8. **Share sub-CRUD** — on the detail page: add a share, edit amount/note/change-member, remove a share.
   Respects owner-rep protection (no remove control + defensive `7002`; member locked) and no-duplicate
   member (picker excludes members already sharing + defensive `7003`).
9. **Audit history** — the immutable per-expense change log (create/update/delete of the expense and its
   shares) as a time-ordered timeline with before/after readable diffs.
10. **CSV export** — per-expense export via `api.blob`, triggered from the detail page; browser download
    using the server-provided filename.
11. **Limits/gating** — `13002` monthly-expense limit on create → informational `LimitNotice`; the
    cross-link 400s (`6001/6002/6003/7001`) and owner-rep protections (`7002`/`7003`) surfaced with
    clear field/inline messaging.

### Non-functional / conventions

- One centralized client only; all types feature-local under `features/expenses/api/types.ts` mirroring
  the backend DTOs. Branch on numeric `code`; render `error.message` verbatim.
- Money: render the API `total`/`amount` via `Money`/`formatMoneyVnd`; the editor sum is display-only.
  Never float-math money for authoritative values.
- Datetimes: send `X-Time-Zone` (client already does); present via `formatDateTime`; `expenseTime`
  submitted as offset-aware ISO-8601.
- i18n: new `expenses` namespace (vi-VN + en-US), fixed domain terms (phiếu chi tiêu / phần gánh / đợt /
  đã trả). Validation strings under the `validation` namespace.
- a11y: labeled controls, keyboard-operable pickers + share editor, color-independent status (settled
  badge text + icon, not color alone), named row-action buttons, timeline as an ordered list.
- Extend the `ErrorCodes` mirror (`src/lib/api/errors.ts`) with the M4 codes it does not yet carry:
  `ExpensePayerInvalid 6001`, `ExpenseCategoryInvalid 6002`, `ExpenseTagInvalid 6003`,
  `ShareMemberInvalid 7001`, `OwnerRepresentativeShareNotDeletable 7002`, `DuplicateShareMember 7003`.
  (`6000`/`7000`/`9001`/`13002` already present.) Append only; never renumber.

## Open Questions

> Each has a firm **Recommendation**; the orchestrator auto-accepts recommendations. Only OQ1 is flagged
> **needs-user consideration** (it shapes navigation topology and every other surface); it still has a
> safe recommended default.
>
> **Resolution (2026-07-17):** OQ1–OQ14 all **Resolved**. OQ1–OQ5 and OQ7–OQ14 accepted at option
> **(a)**; **OQ6** accepted at option **(b)** — the split-evenly helper is deferred to Future
> Improvements. Implemented in the M4 build cycle (Phase A + Phase B) recorded in the Progress Log.

### OQ1 — Detail: dedicated route vs modal. (recommend a; low-risk)

M4 is the first surface complex enough (info + shares + audit + actions) to justify a detail route,
exercising the ownership-404 pattern.
- **(a) Recommended — a dedicated route `/expenses/:uuid`.** Deep-linkable, back-button friendly,
  room for the shares table + audit timeline + actions; ownership 404 → shared not-found view inline.
  Trade-off: one more route + a data-fetch on navigation (cheap; cache-warmed from the list).
- (b) A large modal from the list. Trade-off: no deep-link, cramped for the audit timeline, and the
  create form (share editor) still wants a full surface — inconsistent.

### OQ2 — Build the cycle in sub-phases? (recommend a)

- **(a) Recommended — two internal sub-phases in one cycle, one review at the end.** Phase A: types +
  hooks + client, the new design-system pickers (`Select`, `TagMultiSelect`, `MoneyInput`), list +
  filters, detail (read-only), create + share editor. Phase B: edit-general-info, delete, settled
  toggle, share sub-CRUD, audit timeline, CSV export. Sequences the dependency (create before
  sub-CRUD; detail read before detail writes) without adding a mid-cycle checkpoint. Trade-off: a long
  single cycle — mitigated by the phase boundary being a natural implement/test rhythm.
- (b) Split into two separate cycles + checkpoints (list/detail/create, then edit/shares/history/
  export). Trade-off: a real mid-core checkpoint, at the cost of a second full cycle for one feature
  area and a half-usable ledger between them.

### OQ3 — Create surface: full page vs dialog. (recommend a)

- **(a) Recommended — a full page `/expenses/new`** (and the general-info edit as a dialog on detail).
  The atomic create carries the share editor (a variable-height, multi-row surface) plus the general
  fields — too tall for a comfortable modal; a page gives room and a clean cancel→back. Edit
  general-info has no share editor, so a dialog (mirroring M3's `CategoryFormDialog`) fits it.
  Trade-off: create and edit use different shells — acceptable, they are genuinely different forms
  (create is atomic-with-shares; edit is general-info-only per the backend split).
- (b) Both create and edit as dialogs. Trade-off: uniform, but the share editor is awkward in a modal.
- (c) Both as pages. Trade-off: a page for a 5-field general-info edit is heavier than needed.

### OQ4 — Share editor amount input: whole VND vs decimals. (recommend a)

Backend stores `decimal(18,2)` and accepts fractions, but VND has no minor unit.
- **(a) Recommended — a `MoneyInput` accepting whole VND** (grouped `1.234.567`, integer step, `min 0`),
  Zod `min(0)` + integer. Matches the currency, avoids `.00` noise, mirrors `formatMoneyVnd` (0
  fraction digits). Trade-off: rejects the fractional amounts the backend technically allows — none
  arise for VND; a future multi-currency feature (§6) would revisit.
- (b) Allow decimals (up to 2). Trade-off: literal to the column, but shows `.00` and invites rounding
  confusion the spec warns against (R3).

### OQ5 — Live editor sum display. (recommend a)

- **(a) Recommended — show a live "Tổng (tạm tính)" sum of the editor rows, clearly labelled
  display-only/non-authoritative;** the authoritative total comes from the API response after save.
  Helps the user split correctly. Trade-off: a client-side sum exists on screen — labelled as an
  estimate and never sent as a "total" (there is no total field), so no R3 drift risk.
- (b) No sum in the editor. Trade-off: simpler, but the user loses the running total while splitting.

### OQ6 — "Split evenly" helper in the share editor. (recommend b)

- (a) Include a "chia đều" button that distributes a typed amount across the current members (with a
  remainder rule). Trade-off: a real convenience, but adds a remainder-rounding UX + tests and grows
  the cycle.
- **(b) Recommended — defer to Future Improvements.** Keep M4's editor to manual per-row entry to
  right-size the largest cycle; add split-evenly once the editor ships. Trade-off: users split manually
  at first.

### OQ7 — Event filter + event linkage in M4 (events UI is M5). (recommend a)

The API filter supports `eventUuid` + `looseOnly` and the DTOs already carry `eventUuid`/`eventName`/
`eventIsClosed`, but there is no events-management UI until M5.
- **(a) Recommended — in M4 show the event/loose indicator column + detail linkage (read-only) and a
  `looseOnly` toggle in the filter bar; defer the event-select filter to M5** (it needs the events list
  to populate options). Wire the closed-event write-guard off `eventIsClosed` now (inert until M5).
  Trade-off: the filter bar gains its event dropdown in M5, not M4 — cheap and avoids an empty picker.
- (b) Build the event-select filter now with a placeholder options source. Trade-off: an unpopulated
  control until M5.

### OQ8 — Single-select primitive (payer, category, event later, filters). (recommend a)

No `Select`/combobox exists in the design system.
- **(a) Recommended — add a design-system `Select` built on Radix Select** (`@radix-ui/react-select`,
  already the sanctioned primitive family — no new top-level dep philosophy change), accessible
  (keyboard, typeahead, labelled), theme-token styled, supporting an option renderer (so category
  options show a `CategoryMarker`, member options show the owner-rep/(đã xóa) treatment). Reused by
  M5/M6/M7. Trade-off: a new primitive to design + test (ui-designer surface).
- (b) Use native `<select>` wrapped in a Field. Trade-off: fastest, keyboard-native, but cannot render
  a color swatch/marker in options and looks off-brand — the category picker especially wants the
  marker.
- (c) Build a fully custom combobox from scratch. Trade-off: most control, most code/a11y risk.

### OQ9 — Tag multi-select primitive. (recommend a)

- **(a) Recommended — a `TagMultiSelect` (design-system) — a token-input / checkbox-list combo** showing
  selected tags as removable chips (`Badge`), backed by the active tags list; keyboard-operable.
  Reused by M5 event forms if needed. Trade-off: a second new primitive (ui-designer surface).
- (b) A checkbox list of tags inline. Trade-off: simplest, but grows unwieldy past ~15 tags and reads
  poorly in a dialog.

### OQ10 — `expenseTime` input control. (recommend a)

- **(a) Recommended — a native `datetime-local` input wrapped in the Field pattern**, converting to/from
  offset-aware ISO on submit (viewer TZ). Zero new deps, keyboard + mobile friendly, sufficient for M4.
  Trade-off: native styling varies by browser — acceptable; a branded date-time picker is a future
  improvement.
- (b) Build/adopt a custom date-time picker. Trade-off: nicer, but a new component/dep and a11y burden
  for a field used in two forms.

### OQ11 — Audit before/after rendering. (recommend a)

The backend snapshots are denormalized (camelCase) — expense: `uuid,name,description,expenseTime,
payerMemberUuid,payerMemberName,categoryUuid,categoryName,tags[{uuid,name}],isSettled`; share:
`uuid,expenseUuid,memberUuid,memberName,amount,note`. `Before`/`After` arrive as generic objects.
- **(a) Recommended — a resilient field-diff renderer with a known-field label map** (localized labels
  for the fields above; money via `Money`, datetime via `formatDateTime`, tags as chips), falling back
  to a raw key/value line for any unknown field. Create shows the new snapshot; Update shows only
  changed fields (before → after); Delete shows the removed snapshot. Robust to snapshot-shape drift
  (no hard backend coupling). Trade-off: the label map is maintained client-side — the fallback covers
  gaps so it never breaks.
- (b) Render the raw JSON (pretty-printed). Trade-off: trivial, but unreadable for end users.

### OQ12 — Delete/confirm-dialog close-on-error (carry the M3 nit forward). (recommend a)

- **(a) Recommended — in M4's delete-expense and delete-share confirms, close only on success and on
  terminal codes** (`6000` stale expense, `7000` stale share, `7002` owner-rep, `9001` closed-event),
  and keep the dialog **open** on network/transient errors (show an inline error + let the user retry).
  Fixes the M3 nit in the new code. Trade-off: a small divergence from the M2/M3 delete dialogs (whose
  retrofit is tracked in M3 Future Improvements) — worth the correct behavior in new code.
- (b) Match M2/M3 exactly (close in `finally`). Trade-off: consistency now, but re-introduces the known
  nit.

### OQ13 — Client-side name search on the list. (recommend a)

The API has no name/text filter and no pagination (returns the full owned list).
- **(a) Recommended — a client-side "search by name" text box** filtering the already-fetched list
  (in addition to the server filters), display-only. Cheap, useful on a long ledger. Trade-off: only
  filters the loaded page — fine, since the list is unpaginated.
- (b) No text search. Trade-off: users scan/scroll a long list.

### OQ14 — Cross-cache invalidation reach on expense mutations. (recommend a)

Expense/share writes affect the expense list, the detail, and the history; they do not touch
members/categories/tags.
- **(a) Recommended — invalidate `["expenses"]` (list root) + the specific `detail(uuid)` +
  `history(uuid)` on every expense/share mutation; no optimistic updates in M4.** Simple, correct.
  Trade-off: a refetch per write — acceptable at this scale; optimistic updates are a Future Improvement.
- (b) Optimistic updates for settled/shares now. Trade-off: snappier, but more code/edge-cases in the
  largest cycle.

## Assumptions

- No backend change is needed; every route/DTO/code above exists and is stable (verified against
  `ExpensesController.cs`, `Models/Expenses/**`, `Models/Shares/**`, and
  `FairShareMonApi/planning/expenses-shares-audit.md`).
- The list is unpaginated (backend OQ13a) — the full owned list is returned; the tier cap bounds volume.
- The derived `total` is authoritative and present on both summary and full responses; the UI never
  computes an authoritative total.
- `GET /expenses/{uuid}/history` returns `[]` for an unknown/foreign uuid (leaks nothing) and still
  returns rows for a deleted-but-owned expense (backend OQ17a) — so the timeline coexists with a deleted
  expense; in M4 the timeline is shown on the still-live detail page.
- The Free monthly-expense limit surfaces only as `13002` on `POST /expenses`; there is no self-serve
  upgrade endpoint — the notice is informational (R9).
- Closed events do not exist until M5, so the `eventIsClosed` write-guard is inert in M4 but wired so
  M5 lights it up with no expense-side change.
- The new `Select`/`TagMultiSelect`/`MoneyInput` primitives use the existing Radix + CSS-Modules +
  token stack — no new top-level dependency beyond `@radix-ui/react-select` (same family already in the
  locked stack); if the implementer finds it is not yet installed, that install is a scoped follow-up,
  not a stack change (flag if so).

## Implementation Plan

> Paths under `FairShareMonWeb/src/`. Feature tree: `features/expenses/{api,hooks,pages,components}` +
> `schemas.ts`. New shared primitives under `components/ui/`. All copy via i18n; all data through the
> one `api` client + TanStack Query. Concrete names below reflect the recommended OQ options.

### Phase A — plumbing, primitives, list, detail (read), create

#### A0 — Design-system primitives (ui-designer + implementer)

1. `components/ui/Select/Select.tsx` (+ `.module.css`) — Radix Select wrapper (OQ8a): `value`,
   `onChange`, `options` or children, `label`, `placeholder`, `error`, `disabled`, an option renderer
   slot. Exported from `components/ui/index.ts`.
2. `components/ui/TagMultiSelect/TagMultiSelect.tsx` (+ `.module.css`) — multi-select (OQ9a): selected
   values as removable `Badge` chips + a checkbox/typeahead list; `value: string[]`, `onChange`,
   `options`, `label`, `error`. Exported.
3. `components/ui/MoneyInput/MoneyInput.tsx` (+ `.module.css`) — whole-VND numeric input (OQ4a): grouped
   display, integer parse, `min 0`, `value: number`, `onChange`, `label`, `error`, `aria-describedby`.
   Exported. Uses `formatMoneyVnd` for the display affordance; emits a plain number.
4. Reuse `CategoryMarker` inside category `Select` options; render member options with owner-rep +
   "(đã xóa)" affordances.

#### A1 — API types + client

- `features/expenses/api/types.ts` — mirror the DTOs: `ExpenseSummaryResponse`, `ExpenseResponse`,
  `ShareResponse`, `CreateExpenseRequest` + `CreateShareInput`, `UpdateExpenseRequest`,
  `CreateShareRequest`, `UpdateShareRequest`, `SetSettledRequest`, `ExpenseFilter`, `AuditLogResponse`.
  Reuse `MemberResponse`/`CategoryResponse`/`TagResponse` from the members/categories/tags feature
  types (import, don't redefine). `total`/`amount` typed `number`; datetimes `string` (ISO).
- `features/expenses/api/expensesApi.ts` — one object over `api`:
  - `list(filter: ExpenseFilter)` → `api.get<ExpenseSummaryResponse[]>("/v1/expenses", { query })`
    (only defined filter keys sent — the client drops `undefined`/`null`).
  - `get(uuid)` → `api.get<ExpenseResponse>(\`/v1/expenses/${uuid}\`)`.
  - `create(body)` → `api.post<ExpenseResponse>("/v1/expenses", body)`.
  - `update(uuid, body)` → `api.put<ExpenseResponse>(\`/v1/expenses/${uuid}\`, body)`.
  - `remove(uuid)` → `api.delete<MessageResponse>(\`/v1/expenses/${uuid}\`)`.
  - `setSettled(uuid, body)` → `api.put<MessageResponse>(\`/v1/expenses/${uuid}/settled\`, body)`.
  - `addShare(uuid, body)` → `api.post<ShareResponse>(\`/v1/expenses/${uuid}/shares\`, body)`.
  - `updateShare(uuid, shareUuid, body)` → `api.put<ShareResponse>(\`/v1/expenses/${uuid}/shares/${shareUuid}\`, body)`.
  - `removeShare(uuid, shareUuid)` → `api.delete<MessageResponse>(\`/v1/expenses/${uuid}/shares/${shareUuid}\`)`.
  - `history(uuid)` → `api.get<AuditLogResponse[]>(\`/v1/expenses/${uuid}/history\`)`.
  - `exportCsv(uuid)` → `api.blob("GET", \`/v1/expenses/${uuid}/export\`, { query: { format: "csv" } })`.
- Extend `src/lib/api/errors.ts` `ErrorCodes` with `6001/6002/6003/7001/7002/7003` (append only). No
  change to `NOT_FOUND_CODES` (already has `6000`/`7000`).

#### A2 — Hooks (`features/expenses/hooks/useExpenses.ts`)

- Key factory: `expensesKeys = { all: ["expenses"], list: (filter) => ["expenses","list",filter],
  detail: (uuid) => ["expenses","detail",uuid], history: (uuid) => ["expenses","history",uuid] }`.
- `useExpensesQuery(filter)`, `useExpenseQuery(uuid)`, `useExpenseHistoryQuery(uuid)` (queries).
- Mutations (each `onSuccess` invalidates `expensesKeys.all` + `detail(uuid)` + `history(uuid)` per
  OQ14a): `useCreateExpense`, `useUpdateExpense`, `useDeleteExpense`, `useSetSettled`, `useAddShare`,
  `useUpdateShare`, `useDeleteShare`.
- `useExportExpense` — `useMutation` calling `exportCsv`; on success triggers a browser download via a
  new shared helper `src/lib/download/downloadBlob.ts` (`objectURL` → `<a download>` → revoke), using
  `BlobResult.filename` with a sensible fallback. Toast/close side-effects stay in components.

#### A3 — Zod schemas (`features/expenses/schemas.ts`)

Localized factories mirroring the backend validators (OQ4a integer money):
- `expenseGeneralSchema(t)` — `name` trim min 1 max 200; `description` optional max 1000; `expenseTime`
  required valid datetime; `payerMemberUuid` optional string (empty → default); `categoryUuid` optional
  string (empty → default); `tagUuids` string[] default []. Used by create + edit.
- `shareRowSchema(t)` — `memberUuid` required; `amount` number int `min(0)`; `note` optional max 500.
- `createExpenseSchema(t)` = `expenseGeneralSchema` + `shares: shareRowSchema[]` with `superRefine`:
  no duplicate `memberUuid` (mirror `7003`), owner-rep row present (client also injects — see A6).
- `shareFormSchema(t)` — single share add/edit (memberUuid/amount/note) for the detail sub-CRUD.
- Export `*Values` types. Constants `EXPENSE_NAME_MAX=200`, `EXPENSE_DESC_MAX=1000`, `SHARE_NOTE_MAX=500`.

#### A4 — List page (`features/expenses/pages/ExpensesPage.tsx` + `.module.css`)

- Route: `/expenses`. `PageHeader` (title + "Thêm phiếu" `Button asChild` → `<Link to="/expenses/new">`).
- Filter bar `components/ExpenseFilterBar.tsx`: date range (two `datetime-local`/date inputs), category
  `Select`, tag `Select`, settled tri-state `Select` (all/settled/unsettled), `looseOnly` toggle
  (OQ7a), client-side name search box (OQ13a), and a "clear filters" action. Filter state lives in the
  URL via `useSearchParams` (shareable, back-button friendly); the hook reads them into an
  `ExpenseFilter`. Category/tag options from `useCategoriesQuery(false)`/`useTagsQuery(false)`.
- `components/ExpensesTable.tsx` (reuses `Table` family): columns name (row header) → payer → category
  (`CategoryMarker`) → total (`Money`, numeric) → expense time (`formatDateTime`) → settled (`Badge` +
  `SettledToggle`) → event/loose (`Badge`) → actions (view link). Rows link to the detail route (whole
  row or a "Xem" action). Deleted linked category/payer render with the muted treatment + "(đã xóa)".
- States: `isPending` → skeleton rows; `isError` → `ErrorState` + retry; `data.length === 0` →
  `TableEmpty` + `EmptyState` (respecting whether filters are active: "no matches" vs "no expenses yet").

#### A5 — Detail page read view (`features/expenses/pages/ExpenseDetailPage.tsx` + `.module.css`)

- Route: `/expenses/:uuid`. `useExpenseQuery(uuid)`.
- Ownership 404: if `classifyError(error) === "notFound"` (code `6000`) render the shared `NotFound`
  view inline (R1 — never leak existence); other errors → `ErrorState` + retry; pending → skeleton.
- Layout (ui-designer): header (name + `PageHeader` actions: Edit, Delete, Export CSV, `SettledToggle`),
  a `DescriptionList` (description, expense time, payer, category `CategoryMarker`, tags chips, event
  linkage read-only, total `Money` size lg, created at), the shares breakdown (`SharesSection`), and the
  audit timeline (`ExpenseAuditSection`). Write controls (Edit/Delete/Share writes) disabled when
  `expense.eventIsClosed === true` with an explanatory note; the `SettledToggle` stays enabled (R4).

#### A6 — Create page + share editor

- `features/expenses/pages/ExpenseCreatePage.tsx` (+ `.module.css`), route `/expenses/new` (OQ3a).
- `components/ExpenseGeneralForm.tsx` — shared general-info fields (name `TextField`, description
  `TextField` multiline, expense time input (OQ10a), payer `Select`, category `Select` defaulting to
  the `isDefault` category, tag `TagMultiSelect`). Used by create page + edit dialog.
- `components/ShareEditor.tsx` — RHF `useFieldArray` over `shares`. Seeds one **owner-rep row** at
  amount 0 (resolved from `useMembersQuery(false)` where `isOwnerRepresentative`), pinned first,
  member locked, no remove control (mirrors backend auto-inject + `7002`). "Thêm phần gánh" appends a
  row (member `Select` excluding already-chosen members → mirrors `7003`; `MoneyInput` amount; note
  `TextField`; remove button). Live "Tổng (tạm tính)" via `Money` (OQ5a, display-only). Zod duplicate +
  owner-rep-present refinements.
- Submit → build `CreateExpenseRequest` (empty payer/category → omit so backend applies defaults;
  `expenseTime` → ISO; `tagUuids`; `shares`) → `useCreateExpense`. On success: toast + navigate to the
  new expense's detail. Error handling:
  - `13002` (MonthlyExpenseLimitReached) → render `LimitNotice` inline (informational, R9); keep form.
  - `6001` → payer field; `6002` → category field; `6003` → tag field; `7001` → the offending share
    row's member field (map via `applyFieldErrors` where field keys align, else form-level).
  - `1001` field errors via `applyFieldErrors`; anything else → `FormError` form-level via
    `resolveErrorMessage`.

### Phase B — edit, delete, settled, share sub-CRUD, audit, export

#### B1 — Edit general info (`components/ExpenseEditDialog.tsx`)

- Dialog on the detail page (mirrors `CategoryFormDialog`), reusing `ExpenseGeneralForm` +
  `expenseGeneralSchema`. Pre-fills from the `ExpenseResponse`; tag set full-replace. Submit →
  `useUpdateExpense`. Errors: `6000` stale → toast + close; `6001/6002/6003` → fields; `9001`
  (closed-event) → toast + close; `1001` → fields; else form-level. Disabled entirely when
  `eventIsClosed`.

#### B2 — Delete (`components/DeleteExpenseDialog.tsx`)

- Confirm dialog; body explains hard-delete + cascade of shares AND that the change history is preserved
  (surviving audit). Close-on-error per OQ12a: close on success + terminal `6000`/`9001`; keep open +
  inline error on network. On success: toast + navigate to `/expenses`.

#### B3 — Settled toggle (`components/SettledToggle.tsx`)

- A labelled switch/checkbox (list row + detail). `useSetSettled` with the explicit `{ isSettled }`
  body; immediate mutate, success toast, no confirm. Color-independent (text "Đã trả"/"Chưa trả" +
  icon, not color alone). Remains enabled on closed-event expenses (R4). Error → toast (verbatim) +
  invalidate reconciles.

#### B4 — Share sub-CRUD (`components/SharesSection.tsx`, `ShareFormDialog.tsx`, `DeleteShareDialog.tsx`)

- `SharesSection` renders the shares `Table` (member, amount `Money`, note, actions) + an "Thêm phần
  gánh" button; deleted-member shares show "(đã xóa)". Owner-rep row: no delete control + a short
  explanation; member locked in edit.
- `ShareFormDialog` (add/edit) — `shareFormSchema`; member `Select` excludes members that already have a
  share (mirror `7003`), except when editing that row's own member; owner-rep member locked. Submit →
  `useAddShare`/`useUpdateShare`. Errors: `7001` → member field; `7003` → member field; `7000` stale →
  toast + close; `9001` closed-event → toast + close; `1001` → fields; else form-level.
- `DeleteShareDialog` — confirm; close-on-error per OQ12a (terminal `7000`/`7002`/`9001` close; network
  stays open). `7002` (owner-rep) is prevented in UI (no control) + defensive toast.
- All share write controls hidden/disabled when `eventIsClosed`.

#### B5 — Audit timeline (`components/ExpenseAuditSection.tsx`, `AuditTimeline.tsx`)

- `useExpenseHistoryQuery(uuid)`; render an ordered timeline (`<ol>`), time-ordered ascending as
  returned. Each entry: action badge (Create/Update/Delete), entity type (Phiếu/Phần gánh), timestamp
  (`formatDateTime`), and the readable field-diff (OQ11a) via a `renderAuditDiff` helper with the
  known-field label map (name, description, expenseTime, payerMemberName, categoryName, tags, isSettled;
  share: memberName, amount, note) + raw fallback. Empty → a calm "chưa có thay đổi" note. Loading →
  skeleton; error → inline retry.

#### B6 — CSV export

- An "Xuất CSV" `Button` on the detail header → `useExportExpense(uuid)` → `downloadBlob`. Loading spin
  on the button; error → toast (verbatim). (QR button is M7 — not built here.)

### Wiring

- `routes/router.tsx`: replace the `/expenses` `StubPage` with `ExpensesPage`; add child routes
  `expenses/new` → `ExpenseCreatePage` and `expenses/:uuid` → `ExpenseDetailPage` under the
  `AppShellLayout`/`ProtectedRoute` subtree.
- `i18n/index.ts`: register the new `expenses` namespace (vi-VN + en-US) in `resources` + `NAMESPACES`;
  add `locales/{vi-VN,en-US}/expenses.json`; extend `validation.json` with `expense.*`/`share.*` keys.

### i18n keys (namespace `expenses`, vi-VN authoritative + en-US parity)

- `title`, `subtitle`, `add`, `list.*` (table headers: name/payer/category/total/time/settled/event/
  actions; empty.title/body; noMatches.title/body; error.title/retry; searchPlaceholder).
- `filter.*` (from, to, category, tag, settled.all/yes/no, looseOnly, clear).
- `badge.settled`, `badge.unsettled`, `badge.loose`, `badge.event`, `badge.closed`, `badge.deleted`.
- `detail.*` (description, time, payer, category, tags, event, total, createdAt, notFound.title/body,
  actions.edit/delete/export/back).
- `form.*` (createTitle, editTitle, nameLabel/placeholder, descriptionLabel, timeLabel, payerLabel,
  payerDefaultHint, categoryLabel, categoryDefaultHint, tagsLabel, submitCreate, submitEdit, cancel).
- `shares.*` (sectionTitle, memberLabel, amountLabel, noteLabel, add, addTitle, editTitle, ownerRepLock,
  runningTotal, runningTotalHint, empty).
- `settled.*` (on, off, toast.on, toast.off).
- `delete.*` (title, body [cascade + surviving audit], confirmButton, cancel).
- `deleteShare.*` (title, body, confirmButton, cancel).
- `audit.*` (title, action.create/update/delete, entity.expense/share, empty, field labels map,
  changedFrom/To).
- `limit.*` (title, body for `13002`).
- `export.*` (button, toast.error), `toast.*` (created, updated, deleted, shareAdded, shareUpdated,
  shareDeleted).
- `validation` namespace: `expense.nameRequired/nameTooLong/timeRequired/descriptionTooLong`,
  `share.memberRequired/amountNegative/noteTooLong/duplicateMember/ownerRepRequired`.

### Accessibility

- New pickers keyboard-operable + labelled (Radix Select handles roving focus/typeahead;
  `TagMultiSelect` chips removable via keyboard; `MoneyInput` a plain labelled numeric field).
- Share editor: each row's controls labelled with the member/row context; add/remove buttons named
  (`aria-label` incl. member name where known).
- Settled state color-independent (text + icon); total/amounts in tabular numeric cells.
- Audit timeline as an ordered list; each entry has an accessible action+time label.
- Detail not-found path uses the shared `NotFound` (no existence leak).

### Tests (web-test-engineer — Vitest + RTL, MSW at the client boundary; pinned TZ + vi-VN)

**Design-system primitives:** `Select` (keyboard select, option render, error), `TagMultiSelect`
(add/remove chips, keyboard), `MoneyInput` (grouped display, integer parse, min 0).

**Schemas** (`schemas.test.ts`): name required/too-long; description too-long; expenseTime required;
share amount non-negative + integer; note too-long; duplicate-member refinement; owner-rep-present
refinement.

**Hooks** (`useExpenses.test.tsx`): query-key shapes; each mutation invalidates list + detail + history;
`useExportExpense` triggers `downloadBlob`.

**List** (`expensesPage.test.tsx`): loading skeleton; error + retry; empty (no expenses vs no matches);
rows render payer/category marker/total (`Money` vi-VN)/time/settled; filters update the URL and refetch;
client-side name search filters loaded rows; settled toggle mutates + toasts.

**Detail** (`expenseDetailPage.test.tsx`): renders info + shares + total; ownership `6000` → not-found
view; deleted linked member/category shown with "(đã xóa)"; closed-event (`eventIsClosed`) disables
write controls but not the settled toggle.

**Create** (`expenseCreatePage.test.tsx`): owner-rep 0đ row auto-present + locked + non-removable; add/
remove share rows; duplicate-member blocked (client) and `7003` mapped to member field; live sum;
empty payer/category omitted so defaults apply; success navigates to detail; `13002` → `LimitNotice`;
`6001/6002/6003/7001` mapped to fields; `1001` field errors.

**Edit/delete/settled** (`expenseEditDialog.test.tsx`, `deleteExpenseDialog.test.tsx`): edit pre-fills +
tag full-replace + `9001` closed → toast+close; delete confirm copy (cascade + surviving audit),
close-on-terminal vs stay-open-on-network (OQ12a), success navigates to list.

**Shares sub-CRUD** (`sharesSection.test.tsx`): add/edit/change-member/remove; owner-rep no-delete +
member-lock; `7003` mapping; `7002` defensive; closed-event disables writes.

**Audit** (`auditTimeline.test.tsx`): renders create/update/delete entries with readable diffs; unknown
field → raw fallback; empty state.

**Export** (`export.test.tsx`): CSV export calls `api.blob` and downloads with the server filename.

## Impact Analysis

- **APIs/Database/Services:** none — consumes existing stable `api/v1/expenses` routes + shares/settled/
  history/export sub-routes.
- **Frontend:**
  - New feature tree `src/features/expenses/**` (api, hooks, pages, components, schemas, tests).
  - New design-system primitives `Select`, `TagMultiSelect`, `MoneyInput` (+ index export) — reused by
    M5/M6/M7.
  - New shared helper `src/lib/download/downloadBlob.ts` (reused by M5 event export, M7 QR download).
  - `src/lib/api/errors.ts` — append `6001/6002/6003/7001/7002/7003` to the mirror.
  - `routes/router.tsx` — `/expenses`, `/expenses/new`, `/expenses/:uuid` replace the stub.
  - `i18n/index.ts` + new `expenses.json` (both locales) + `validation.json` additions.
  - Possible dep: `@radix-ui/react-select` if not already installed (same sanctioned family; flag if a
    new install).
- **Design system:** first `Select`/multi-select/money-input primitives; a ui-designer pass covers the
  share editor, filter bar, detail layout, and audit timeline.
- **Documentation:** this planning doc; roadmap M4 row already present.
- **Downstream:** M5 (events) lights up the wired `eventIsClosed` guard + adds the event-select filter +
  assign/remove; M6 (stats) reuses expense data; M7 (QR) adds the per-expense QR button on this detail
  page + reuses `downloadBlob`.

## Decision Log

### Decision

Adopt the plan above: detail as a route, a two-phase single cycle, create as a full page with an
embedded share editor, edit/shares on the detail page, three new design-system pickers, a resilient
audit-diff renderer, and the improved delete-dialog close-on-error behavior.

### Reason

M4 is the core and the largest surface; a detail route + full-page create give the share editor and
audit timeline the room they need, while the general-info edit and share sub-CRUD stay lightweight on
the detail page. The pickers are genuinely missing from the design system and are reused by three later
milestones, so building them properly now pays forward. The backend contract (atomic create, derived
total, owner-rep protection, cross-link 400s, ownership 404, closed-event guard) is fully locked, so the
UI's job is faithful mirroring + clear messaging.

### Alternatives Considered

- Detail as a modal (OQ1b) — rejected; too cramped for shares + timeline, no deep-link.
- Two separate cycles (OQ2b) — deferred to the checkpoint; recommended a single two-phase cycle.
- Native `<select>` (OQ8b) — rejected for category (needs the marker); Radix Select chosen.
- Raw-JSON audit (OQ11b) — rejected; unreadable.

## Progress Log

### 2026-07-17

- Feature-planner drafted this M4 plan. Required reading completed: `planning/feature-roadmap.md` (M4
  scope + locked roadmap OQs), `FairShareMonWeb/CLAUDE.md` (locked conventions), the backend
  `ExpensesController.cs` (all ~13 routes) + `Models/Expenses/**` + `Models/Shares/**` (exact DTO
  shapes) + `Services/Audit/AuditSnapshots.cs` (snapshot field shape), `FairShareMonApi/The-ideal.md`
  §3.5/§3.8/§4/§5, `FairShareMonApi/planning/expenses-shares-audit.md` (20 backend OQs), and the shipped
  SPA (`api` client + `api.blob`, `errors.ts`/`http-error-handling.ts`, `router.tsx`, i18n setup,
  `Money`/`CategoryMarker`/`Table`/`Dialog`/`Premium`/`Form` primitives, and the members/categories/tags
  features incl. the M3 review-nit Future Improvements).
- Mapped every M4 surface to concrete routes/files/components/hooks/endpoints/schemas/i18n keys/tests;
  identified the three net-new design-system primitives and the ui-designer surfaces; recorded 14 Open
  Questions each with a firm recommendation (OQ1 flagged for user consideration, safe default given).
- Awaiting the checkpoint (orchestrator auto-accepts recommendations) before Phase A implementation.

### 2026-07-17 (ui-designer — M4 design pass: primitives + complex surfaces)

- Added `@radix-ui/react-select@2.3.3` (sanctioned Radix family per OQ8a; it was
  not yet installed — a scoped install, not a stack change).
- **New design-system primitives** (under `src/components/ui/`, exported from the
  barrel, all theme-aware / WCAG-AA / Vietnamese-tolerant, tokens only):
  - `Select/` — Radix Select wrapper with a `renderOption` slot (category options
    render a `CategoryMarker`; member options render the owner-rep marker +
    "(đã xóa)"). Typed `value`/`onValueChange`, `label`, `placeholder`, `hint`,
    `error`, `required`, `disabled`, `hideLabelVisually`, `name`.
  - `TagMultiSelect/` — removable chips + native-checkbox popover (Escape /
    outside-click close). `value: string[]`, `onChange`, localized toggle/remove/
    empty labels.
  - `MoneyInput/` — whole-VND integer input; grouped `1.234.567` display when
    blurred, raw digits when focused; emits `number | null`; trailing `₫`.
- Added `hideLabelVisually` (additive) to `TextField`, `Select`, `MoneyInput` for
  the share-editor rows (column headers carry the visual label; SR gets the
  per-control label).
- **Complex-surface specs** delivered as a living, interactive showcase the
  implementer replicates: `src/styles/M4Showcase.tsx` (+ `.module.css`), mounted
  in `StyleGuide.tsx`. Covers: (4) the share editor — pinned/locked owner-rep 0đ
  row, per-row member `Select` excluding chosen members, `MoneyInput`, remove, an
  "add" affordance, and the display-only "Tổng (tạm tính)" sum; (5) the expense
  detail layout — actions cluster (Edit/Delete/Export/settled switch), info
  `DescriptionList`, shares `Table` with derived total, **plus the closed-event
  read-only variant** (all writes disabled, settled switch stays enabled); (6)
  the audit-history timeline — ordered `<ol>` of create (new snapshot) / update
  (changed fields, before→after) / delete (removed snapshot) with money via
  `Money`, formatted datetimes, tag chips, and a raw key/value fallback for
  unknown fields; (7) the filter bar — date range, category `Select`, tag
  `TagMultiSelect`, settled tri-state `Select`, loose-only switch, name search,
  wrapping responsively.
- Documented the new pickers in `src/styles/README.md`. Verified: `tsc -b`,
  `pnpm lint` (only pre-existing warnings), `pnpm build`, and a mount smoke test
  all clean.

### 2026-07-17 (web-implementer — Phase A: plumbing, primitives, list, detail read, create)

- Extended `src/lib/api/errors.ts` `ErrorCodes` (append-only) with `6001/6002/6003/7001/7002/7003`. Added
  the shared blob-download helper `src/lib/download/downloadBlob.ts` (filename\* aware; reused by M5/M7).
- Built the feature tree `src/features/expenses/`: `api/types.ts` (DTO mirror; member/category/tag types
  imported, not redefined), `api/expensesApi.ts` (all routes incl. `api.blob` CSV export),
  `hooks/useExpenses.ts` (key factory + queries + 7 mutations invalidating `all`+`detail`+`history` per
  OQ14a + `useExportExpense`→`downloadBlob`), `schemas.ts` (localized Zod: name≤200/desc≤1000/note≤500/
  amount int≥0/no-dup-member/owner-rep-present), `dateTime.ts` (datetime-local ↔ offset-aware ISO; date
  filter bounds), `components/pickerOptions.tsx` (member/category `SelectOption` builders + renderers),
  `components/icons.tsx`.
- List: `pages/ExpensesPage.tsx` + `components/ExpenseFilterBar.tsx` (date range, category/tag/settled
  `Select`, loose-only, client-side name search; state in the URL via `useSearchParams`; event-select
  filter deferred to M5) + `components/ExpensesTable.tsx` (backend order, `CategoryMarker`, `Money`,
  `SettledToggle`, event/loose badge, deleted "(đã xóa)"; loading/error/two-empty states).
- Detail (read): `pages/ExpenseDetailPage.tsx` — ownership `6000` → shared `NotFound` inline (R1),
  info `DescriptionList`, shares, audit, header actions; closed-event write-guard wired off
  `eventIsClosed` (inert until M5, settled stays enabled — R4).
- Create: `pages/ExpenseCreatePage.tsx` + `components/ExpenseGeneralForm.tsx` +
  `components/ShareEditor.tsx` (owner-rep 0đ row auto-present/pinned/locked/non-removable; per-row member
  `Select` via `availableFor`; live display-only "Tổng (tạm tính)"; empty payer/category omitted so
  backend defaults apply; `13002`→`LimitNotice`; `6001/6002/6003`→fields, `7001/7003`→form-level).
- Reused the ui-designer's `Select`/`TagMultiSelect`/`MoneyInput` primitives + `M4Showcase` surface specs
  verbatim (no restyle). New `expenses` i18n namespace (vi-VN + en-US) + `validation.expense.*`/`share.*`
  keys, registered in `i18n/index.ts` + `useT.ts`. Replaced the `/expenses` stub in `router.tsx` with
  `/expenses`, `/expenses/new`, `/expenses/:uuid`.

### 2026-07-17 (web-implementer — Phase B: edit, delete, settled, share sub-CRUD, audit, export)

- `components/SettledToggle.tsx` (own immediate mutate, color-independent switch, stays enabled on closed
  events — R4), `components/ExpenseEditDialog.tsx` (reuses `ExpenseGeneralForm`; `6001/6002/6003`+`1001`
  →fields; `6000`/`9001`→toast+close), `components/DeleteExpenseDialog.tsx` (cascade + surviving-audit
  copy; OQ12a close-on-terminal `6000`/`9001`, stay-open on network).
- Share sub-CRUD: `components/SharesSection.tsx` (shares `Table` + derived total, owner-rep no-delete +
  "khóa", deleted-member "(đã xóa)", writes hidden/disabled when `eventIsClosed`),
  `components/ShareFormDialog.tsx` (add/edit; picker excludes members already sharing → `7003`; owner-rep
  member locked; `7001/7003`→member field, `7000`/`9001`→toast+close), `components/DeleteShareDialog.tsx`
  (OQ12a close-on-terminal `7000`/`7002`/`9001`, stay-open on network; `7002` defensive).
- Audit: `components/AuditTimeline.tsx` (resilient field-diff renderer per OQ11a — known-field label map
  with money via `Money`, datetime via `formatDateTime`, tags as chips, raw fallback for unknown fields;
  Create=after / Update=before→after / Delete=removed) + `components/ExpenseAuditSection.tsx` (loading/
  error/empty).
- CSV export: detail-header "Xuất CSV" button → `useExportExpense` → `downloadBlob` (server filename).
- MSW: added a full expenses mock (`src/test/msw/handlers.ts`) — list+filters, get, atomic create with
  owner-rep auto-inject + `6001/6002/6003/7001/7003`, update, delete + surviving audit, settled, share
  add/update/delete (+`7002`), history (camelCase snapshots), and a CSV export blob with a
  `Content-Disposition` filename.
- **Verification.** `tsc -b` clean; `pnpm lint` clean (only pre-existing warnings); `pnpm build` succeeds;
  the full existing Vitest suite (280 tests) passes — no regressions. **Live contract check against the
  backend on :5200** (MariaDB+Redis): registered a user, created members, then exercised the atomic
  create (defaults applied, owner-rep 0đ auto-injected, derived total = 300000), list + category filter +
  loose-only, detail, settled toggle, edit general info, share add/update, duplicate-member → `7003`,
  owner-rep delete → `7002`, per-expense history (6 entries; **confirmed the snapshot keys exactly match
  the audit diff renderer**: `uuid,name,description,expenseTime,payerMemberUuid,payerMemberName,
  categoryUuid,categoryName,tags,isSettled`), CSV export (Content-Disposition filename present), foreign
  uuid → `404`/`6000`, foreign history → `[]` (no leak), delete + audit-survives-delete — all passed.
  Dev server booted against the live backend and served `/expenses` + transformed every new module.
  **Not exercised in a real browser** (no `chromium-cli`/Playwright driver in this environment — Playwright
  E2E is a documented Future Improvement); UI rendering is covered by the passing build + existing RTL
  suite through the shared providers, and the data contract is covered live.

### 2026-07-17 (web-test-engineer — M4 test suite)

- Added **14 test files / 127 M4 tests** (Vitest + RTL, MSW at the client boundary, pinned
  `Asia/Ho_Chi_Minh` TZ + vi-VN default). Full suite now **406 passing (43 files)**, up from 280;
  green on two consecutive `pnpm test` runs, `pnpm lint` clean (only pre-existing warnings), `tsc -b`
  clean. No product code changed.
- **Test-harness change (allowed):** added jsdom polyfills to `src/test/setup.ts`
  (`hasPointerCapture`/`setPointerCapture`/`releasePointerCapture`/`scrollIntoView` + a `ResizeObserver`
  stub) so Radix Select — M4's first combobox primitive — can open in jsdom. Additive; inert for
  non-Radix tests.
- **Design-system primitives:** `select.test.tsx` (combobox role, placeholder, click + keyboard select,
  `renderOption` slot, error/aria-invalid), `moneyInput.test.tsx` (whole-integer emit, non-digit strip,
  clear→null, grouped-vs-raw display, disabled, error), `tagMultiSelect.test.tsx` (check/uncheck, chips
  + keyboard remove, Escape-close, empty, grouped-under-label).
- **Schemas** (`schemas.test.ts`, 21): name required/too-long, description too-long, time required, share
  amount non-negative + integer + null-tolerant, note too-long, create-schema duplicate-member +
  owner-rep-present refinements (and the no-owner-rep-uuid skip), single-share form schema.
- **Hooks** (`useExpenses.test.tsx`): key-factory shapes; list query sends only defined filters; all 7
  mutations invalidate the `["expenses"]` root reaching list + `detail(uuid)` + `history(uuid)` (mounted
  all three, asserted each refetches); `useExportExpense` → `downloadBlob` with server filename + fallback.
- **List** (`expensesPage.test.tsx`, 15): skeleton, empty vs no-matches, error+retry, row
  payer/`CategoryMarker`/`Money`(vi-VN)/time/settled-switch/loose+event badge, deleted "(đã xóa)",
  detail link, loose/category/settled filters drive the URL + refetch, clear-filters, client-side name
  search (no extra GET), list settled toggle mutate+toast+reconcile, en-US chrome.
- **Detail** (`expenseDetailPage.test.tsx`): info+shares+derived total, audit empty note, ownership
  `6000`→shared NotFound (no leak), non-404→retry, deleted linked member/category "(đã xóa)",
  closed-event disables Edit/Delete/add-share but keeps the settled toggle + export enabled (R4).
- **Create** (`expenseCreatePage.test.tsx`, 14): owner-rep 0đ row present/locked/non-removable, add/remove
  rows, add disabled when no members remain (client dedup), live "Tổng (tạm tính)" sum, submit sends
  default payer/category + owner-rep share and navigates to detail, empty-name client block, `13002`→
  `LimitNotice`, `6001`→payer / `6002`→category / `6003`→tag fields, `7003`→form-level, `1001`→name
  field, per-row member Select excludes chosen members.
- **Edit** (`expenseEditDialog.test.tsx`): pre-fill, edited-name PUT+close+toast, **tag full-replace**
  body, `9001`→toast+close, `6002`→category field keeps open.
- **Delete** (`deleteExpenseDialog.test.tsx`): named title + cascade + surviving-audit copy, success
  toast+close+navigate to list, terminal `6000`/`9001` close, **transient 500 + network error stay open
  with inline error** (OQ12a).
- **Shares sub-CRUD** (`sharesSection.test.tsx`, 13): rows+derived total, owner-rep no-delete + lock,
  normal edit/remove, closed-event hides writes, add (POST body + picker excludes already-sharing →
  `7003` field map), edit amount (PUT subpath) + change-member (new memberUuid) + owner-rep member-lock,
  remove confirm (DELETE), terminal `7002` close, transient stays open (OQ12a).
- **Audit** (`auditTimeline.test.tsx`): `<ol>`, Create=after-snapshot (tags chips + `Money` +
  color-independent settled text), Update=changed-fields-only before→after with "đổi thành" label,
  Delete=removed snapshot, unknown-field raw fallback, order preserved; `ExpenseAuditSection`
  empty/loaded/error+retry.
- **Export** (`export.test.tsx`): detail "Xuất CSV" → blob endpoint (`format=csv`) + `downloadBlob` with
  server filename; export error toasts + no download.
- **i18n** (`expensesI18n.test.ts`): vi-VN↔en-US key-shape parity for the `expenses` namespace and
  `validation.expense.*`/`share.*`, no empty leaves, schema keys covered, fixed domain terms.
- **Coverage nuance (not a bug):** the literal "empty payer/category → omit" path can't be driven through
  the create UI because the form pre-fills the seeded owner-rep/default-category (both non-empty); the
  test asserts the equivalent observable outcome (defaults sent + owner-rep 0đ share auto-present) and the
  `value || undefined` omit branch is covered indirectly. On the atomic create, `7001/7003` surface
  form-level (not per-field) — matching the implementation and the Phase A progress note; the per-field
  `7003` mapping is verified in the share sub-CRUD dialog. **No product bugs surfaced.**

## Final Outcome

**Complete.** M4 shipped the ledger core across both phases. Routes: `/expenses` (list + full filter bar — date range, category, single-tag, settled tri-state, loose-only, client-side name search, URL-persisted), `/expenses/new` (atomic create with the share editor), `/expenses/:uuid` (detail — the first detail route, ownership `6000`→shared not-found). Share editor: owner-rep 0đ row auto-present/pinned/locked, per-row member exclusion + Zod dedup, display-only "Tổng (tạm tính)". Phase B: general-info edit, delete (OQ12a close-on-terminal-only), settled toggle (the sole write allowed under a closed event), share sub-CRUD (7002/7003), audit-history timeline (create/update/delete diffs), per-expense CSV export via `api.blob`+`downloadBlob`. Design-system additions: `Select` (option-renderer), `TagMultiSelect`, `MoneyInput` (whole-VND integers); `@radix-ui/react-select` added. Money is integer-VND end-to-end (never float; backend derives the total). Consumes the full `api/v1/expenses` controller; error mirror extended with 6001/6002/6003/7001/7002/7003. Verified live on :5200 (atomic create, defaults, owner-rep, filters, settled, edit, share CRUD, audit-key match, CSV, 404/6000). Tests +126 (suite 280→406); code review **APPROVE, 0 blocking**. Review nit fixed pre-close: corrected a misleading `memberUuid` comment. All 14 OQs shipped at recommended (OQ6=b deferred).

## Future Improvements

- **Split-evenly helper** in the share editor (OQ6a) with a documented remainder rule.
- **Optimistic updates** for the settled toggle and share sub-CRUD once the write patterns settle
  (OQ14b).
- **Branded date-time picker** replacing the native `datetime-local` (OQ10b).
- **Order-insensitive array diff in the audit timeline** (M4 review nit): `AuditTimeline` compares fields with `JSON.stringify`, which is order-sensitive for `tags`; if the backend ever returns a reordered tag set without a content change, a spurious "changed" row could appear. Use content-set comparison for array fields if it surfaces.
- **Per-row `7001`/`7003` mapping on atomic create** (M4 review nit / OQ, accepted deviation): the flat codes surface form-level on create (per-field in the share sub-CRUD dialog); map to the offending row if the backend ever returns a row index.
- **Event-select filter** on the list bar (arrives with M5's events list; OQ7).
- **Per-field audit diff + restore-from-snapshot** (backend §6 future) once the audit is richer.
- **Retrofit the M2/M3 delete dialogs** to the OQ12a close-on-error behavior for cross-feature
  consistency.
- **Shared reference-data list scaffolding** — extract the list+toolbar+dialog pattern now shared by
  members/categories/tags/expenses if a fourth CRUD surface appears.
- **Responsive stacked-card table variant** for the expense list on narrow screens.
- E2E (Playwright) coverage of the full ledger loop (add member → add expense with shares → settle →
  export) once a browser driver is available.
