# Expense–Event Linkage at Create Time

## Objective

Ship two related front-end features that let a user attach a newly-created expense to an
**OPEN** event, using the create-time linkage the backend already supports
(`POST /v1/expenses` accepts an optional `eventUuid`). No backend change is required.

1. **Optional event selector in the Add Expense form** (`/expenses/new`). A user can pick one of
   their OPEN events when creating an expense. The field is optional — the default (none) keeps the
   current "loose expense" behavior.
2. **"Add expense" popup on the Event detail page** (OPEN events only). A button opens a Dialog
   holding the same create-expense form with the **current event pre-selected and locked**
   (non-editable). On success the dialog closes, a success toast shows, and the event detail
   (balance table + expenses section + `expenseCount`) refreshes.

## Background

- **Backend contract (verified).** `CreateExpenseRequest` (`Models/Expenses/CreateExpenseRequest.cs`)
  carries `EventUuid` as an optional (`string?`) property — Swagger summary: *"Nếu có, đợt phải thuộc
  cùng tài khoản, đang mở và chứa thời điểm chi; bỏ trống để phiếu không thuộc đợt nào."*
  `ExpensesService.CreateAsync` passes `request.EventUuid?.Trim()` into
  `ExpenseRepository.CreateAsync`, which enforces ownership + open + within-range and maps failures to
  the same codes used by assign-to-event: `EventNotFound 9000`, `EventClosed 9001`,
  `ExpenseTimeOutOfEventRange 9002` (see `ExpensesService` `ThrowIfFailed` switch). There is **no**
  validator making `EventUuid` required on create (unlike `AssignEventRequestValidator`), confirming
  the field is optional.
- **Domain rule (`The-ideal.md` §3.6).** The expense's time must fall inside the event's date range,
  checked both at assign and at create; an expense need not belong to any event. **Closed events are
  read-only** (§3.6 / §4): every write control is disabled except the settled toggle — so the
  create-from-event affordance is OPEN-only.
- **Current front-end shape (verified).**
  - `ExpenseCreatePage.tsx` holds an inner `ExpenseCreateForm` that owns the RHF form, builds
    `CreateExpenseRequest` in `onSubmit`, calls `useCreateExpense`, toasts `expenses:toast.created`,
    then navigates to `/expenses/:uuid`. It composes `ExpenseGeneralForm` (shared with
    `ExpenseEditDialog`) + `ShareEditor`, wrapped in two `Card`s with a `FormActions` row.
  - `ExpenseGeneralForm` is typed to `ExpenseGeneralValues` and is **shared with the edit dialog**, so
    it must NOT gain an event field (edit does not set the event; that is a separate flow via
    `AssignExpenseDialog` / the `expenseEvent` controls).
  - `createExpenseSchema(t, ownerRepUuid)` = `expenseGeneralSchema` `.extend({ shares })` with the
    duplicate-member + owner-rep refinements. `CreateExpenseValues` is inferred from it.
  - `CreateExpenseRequest` (FE type) has **no `eventUuid`** yet.
  - `useCreateExpense` invalidates `expensesKeys.all` + the created expense's `detail`/`history`, but
    does **not** touch `eventsKeys` — so an event's `expenseCount` (`eventsKeys.detail`) and balance
    (`eventsKeys.balance`) would go stale after a create-with-event. (`useAssignExpenseEvent` already
    invalidates `eventsKeys.all` for exactly this reason; `useExpenses.ts` already imports
    `eventsKeys`.)
  - `EventDetailPage.tsx` `DetailView` renders write controls in `.detailActions` gated on
    `!closed`; the event's expenses live in `EventExpensesSection` (uses
    `useExpensesQuery({ eventUuid })` → under `expensesKeys`, so invalidated already), and the balance
    in `EventBalanceTable` (uses `eventsKeys.balance`, NOT invalidated by `useCreateExpense`).
  - Established dialog patterns: `AssignExpenseDialog` (event-side dialog living in
    `features/events/components/`, imports expenses hooks; toasts + closes on 9000/9001, inline
    message on 9002), `ExpenseEditDialog`, `EventFormDialog`.
  - Primitives available: `Combobox` (searchable, ARIA-1.2, Vietnamese diacritic-insensitive,
    controlled `value`/`onValueChange`, `renderOption`, `loading`, `emptyLabel`), `Select`, `Dialog`
    family, `LimitNotice`, `FormError`, `Alert`, `Badge` (with `icon`, used for the owner-rep lock).
  - Locale files `expenses.json` / `events.json` already carry an `expenseEvent.noOpenEvents`
    ("Chưa có đợt nào đang mở") string; an i18n parity test enforces identical key sets across
    vi-VN / en-US.

## Requirements

- **R1.** Add `eventUuid` to the create schema, the FE `CreateExpenseRequest` type, and the submit
  body — sent only when a value is present (empty → omitted → loose expense, unchanged behavior).
- **R2 (Feature 1).** On `/expenses/new`, render an **optional** OPEN-event picker fed by
  `useEventsQuery({ closed: false })`. Only OPEN events are selectable. The user can clear back to
  "no event". When there are no OPEN events, show a hint instead of an empty control; the field is
  simply absent (loose expense) — never an error.
- **R3 (Feature 1).** Loading the events list must not block the rest of the form (the event is
  optional): the general fields + share editor render on the members/categories/tags gate as today;
  the event picker degrades gracefully if the events query is still loading or errors.
- **R4 (Feature 2).** On `EventDetailPage`, for **OPEN** events only, add an "Thêm phiếu" button in
  `.detailActions` that opens a Dialog containing the create-expense form. Closed events show no such
  button (read-only rule).
- **R5 (Feature 2).** In that dialog the event is **locked** to the current event: the picker is
  replaced by a read-only display (event name + lock affordance); the user cannot change or clear it;
  `eventUuid = event.uuid` is always submitted.
- **R6 (Feature 2).** On success: close the dialog, toast `expenses:toast.created`, and refresh the
  event detail data (`expenseCount`, balance table, expenses section).
- **R7.** Mirror the backend validators/business rules in the UI error handling: `13002` monthly
  limit → `LimitNotice`; `1001` field errors → RHF fields; `9002` out-of-range → actionable field
  error; `9000/9001` (event gone/closed since open) → handled per context.
- **R8.** All copy through i18n (vi-VN + en-US, parity preserved). VND via `formatMoneyVnd`,
  datetimes via the shared formatters. Accessibility baseline honored (labeled controls, keyboard
  nav, color-independent status).
- **R9.** Reuse the create form across both surfaces via one extracted component — do not fork a
  parallel form.

## Open Questions

> Each option lists a one-line trade-off; the **Recommended** option is marked. These are genuine
> UI/preference calls the orchestrator should bring to the user.

### OQ1 — Event picker control + how to clear back to "no event"

The event is optional, so the user must be able to both pick and un-pick. `Select`/`Combobox` emit a
value only on choosing an option; neither has a native "clear".

- **(a) Recommended — `Combobox` with a prepended "Không thuộc đợt (phiếu lẻ)" option (value `""`).**
  Searchable + Vietnamese-diacritic-insensitive (events can be numerous; matches the bank-picker
  precedent), and the explicit loose option gives an obvious, accessible way to clear. Trade-off: one
  synthetic option in the list.
- (b) `Select` with a prepended "Không thuộc đợt" option. Lighter, matches payer/category controls.
  Trade-off: no search — poor with many events.
- (c) `Combobox`/`Select` with no clear option + a separate "Gỡ" text button. Explicit. Trade-off:
  extra control/space for a rare action.

### OQ2 — Create-form layout inside the Dialog

The page renders the form in two `Card`s (`generalSection`, `shares.sectionTitle`) with a
`FormActions` row. A Dialog is narrower and has its own chrome.

- **(a) Recommended — flat layout inside `DialogContent size="lg"`: section headings (no `Card`
  wrappers), `DialogFooter` with `DialogClose` cancel + submit.** Fits dialog width, mirrors
  `ExpenseEditDialog` (which renders `ExpenseGeneralForm` directly, no Cards). Trade-off: the form
  component must support a `variant` that swaps Card-wrapping + the action row.
- (b) Reuse the exact two-Card page layout inside the dialog. Zero layout divergence. Trade-off:
  Cards-in-dialog is visually heavy and cramped on phones.
- (c) Radically trim the dialog form (e.g. collapse the share editor). Compact. Trade-off: diverges
  from the page form's capabilities; users expect the same fields — not recommended.

### OQ3 — Placement of the event field on the create page

- **(a) Recommended — a dedicated field at the top of the "Thông tin chung" section (above name), or
  as the first row of that section.** Keeps event linkage visually adjacent to the general info.
  Trade-off: none material.
- (b) Its own small section/`Card` "Đợt (tùy chọn)" between general info and shares. More prominent.
  Trade-off: extra vertical space for an optional field.

## Assumptions

- The `eventUuid` field is create-only: it is added to `createExpenseSchema` (not to the shared
  `expenseGeneralSchema`), so the edit dialog is untouched.
- On `9000/9001` from the locked-event dialog, the dialog will **toast danger + close** (mirroring
  `AssignExpenseDialog`) via an `onEventUnavailable` callback; on the create page (selector), the same
  codes surface as a form-level `FormError` (no navigation). This mirrors the established sibling
  pattern and is treated as a consistency decision, not a silent preference (recorded in the Decision
  Log). If the user prefers otherwise it can be revisited.
- `9002` (expense time outside the event range) is surfaced as a field error on `expenseTime` in both
  contexts (most actionable), with the backend's localized `error.message` rendered verbatim.
- Members/categories/tags are fetched inside the dialog exactly as the page does
  (`useMembersQuery(false)` etc.); TanStack Query de-dups/serves cache so this is cheap.
- The events query for the picker uses `useEventsQuery({ closed: false })`; an error there is
  non-fatal — the picker is hidden and the form still creates a loose expense.
- The backend returns the created `ExpenseResponse` with its `eventUuid` populated, so the mutation
  can decide whether to invalidate the events caches.

## Implementation Plan

> Paths under `FairShareMonWeb/`. Concrete names below assume the recommended OQ options; substitute
> if the user chooses otherwise.

### Step 1 — Types + schema (R1)

1. `src/features/expenses/api/types.ts` — add to `CreateExpenseRequest`:
   `/** Omit → loose expense; else the OPEN event this expense joins at creation. */ eventUuid?: string;`
   (leave `UpdateExpenseRequest` untouched — event linkage on edit is a separate flow).
2. `src/features/expenses/schemas.ts` — add `eventUuid` to the **create** schema only. In
   `createExpenseSchema`, extend with `eventUuid: z.string().optional()` alongside `shares`. This
   flows into `CreateExpenseValues` automatically; `expenseGeneralSchema` (shared with edit) is
   unchanged.

### Step 2 — Extract the reusable create form (R9, the component boundary)

Extract the inner `ExpenseCreateForm` from `ExpenseCreatePage.tsx` into a standalone component
**`src/features/expenses/components/ExpenseCreateForm.tsx`** and generalize it. Exact boundary/props:

```ts
export type ExpenseCreateFormProps = {
  members: MemberResponse[];
  categories: CategoryResponse[];
  tags: TagResponse[];
  /** OPEN events for the optional picker (Feature 1). Ignored when lockedEventUuid is set. */
  openEvents?: EventSummaryResponse[];
  /** Background-refresh flag → the picker shows a subtle loading hint (never blocks). */
  eventsLoading?: boolean;
  /** Feature 2: fix the expense to this event. Picker becomes a read-only locked display;
   *  eventUuid is always submitted. */
  lockedEventUuid?: string;
  lockedEventName?: string;
  /** Layout + action-row variant (OQ2): "page" → Cards + FormActions/Link;
   *  "dialog" → flat sections + DialogFooter/DialogClose. */
  variant: "page" | "dialog";
  /** Post-success hook. The form always toasts expenses:toast.created + invalidates.
   *  Page → navigate(`/expenses/${expense.uuid}`); dialog → onOpenChange(false). */
  onCreated: (expense: ExpenseResponse) => void;
  /** 9000/9001 handler (dialog: toast danger + close). Absent → form-level FormError. */
  onEventUnavailable?: (message: string) => void;
};
```

Behavior inside the component (unchanged from today except where noted):

- Compute `ownerRep` / `defaultCategory`; seed `defaultValues` including
  `eventUuid: lockedEventUuid ?? ""`.
- Render the event field (new — see Step 3) **before** the general section (OQ3a), inside the form
  but outside `ExpenseGeneralForm` (so the shared general form is untouched).
- `onSubmit` builds `CreateExpenseRequest` as today plus `eventUuid: values.eventUuid || undefined`
  (locked case: `values.eventUuid` is the locked uuid). Extend the `catch` to also branch on:
  - `ExpenseTimeOutOfEventRange` (9002) → `setError("expenseTime", { message })`;
  - `EventClosed` (9001) / `EventNotFound` (9000) → if `onEventUnavailable` given, call it (dialog
    closes + toast), else `setFormError(error.message)`.
  Keep the existing 13002 / payer / category / tag / share / field-error handling.
- `variant === "page"`: wrap general + shares in `Card`s, render `FormActions` with a
  `Link to="/expenses"` cancel + submit (as today). `variant === "dialog"`: render section headings
  without Cards and a `DialogFooter` (`DialogClose` cancel + submit). The `<Form>` element wraps the
  content in both; in the dialog it sits inside the caller's `DialogContent`.

### Step 3 — Event field component (R2, R5)

New **`src/features/expenses/components/ExpenseEventField.tsx`** (presentational):

```ts
type ExpenseEventFieldProps =
  | { lockedName: string }                               // Feature 2 locked display
  | { value?: string; onChange: (v: string) => void;    // Feature 1 picker
      events: EventSummaryResponse[]; loading?: boolean; error?: string };
```

- **Locked** (`lockedName`): a labeled read-only block — event name + a `Badge tone="info"` with a
  lock glyph (mirror the owner-rep lock in `ShareEditor`), label `expenses:form.eventLockedLabel`,
  hint `expenses:form.eventLockedHint`. No interactive control; not focusable as an input.
- **Picker**: a `Combobox` (OQ1a) with options `= [{ value: "", label: t("form.eventLoose") },
  ...events.map(e => ({ value: e.uuid, label: e.name, keywords: [formatRange...] }))]`,
  `value={value || ""}`, `onValueChange={onChange}`, `label=form.eventLabel`,
  `placeholder=form.eventPlaceholder`, `searchPlaceholder=form.eventSearchPlaceholder`,
  `emptyLabel=form.eventSearchEmpty`, `loading={loading}`, `hint=form.eventHint`.
- The **no-open-events** empty state is handled by the parent form (`ExpenseCreateForm`): when
  `openEvents` is empty (and not locked), render a muted hint `expenses:expenseEvent.noOpenEvents`
  instead of the picker (R2).

### Step 4 — Wire the create page (Feature 1)

`src/features/expenses/pages/ExpenseCreatePage.tsx`:

1. Import the extracted `ExpenseCreateForm`; delete the inner copy.
2. Add `const eventsQuery = useEventsQuery({ closed: false });` — **do not** add it to `isPending` /
   `isError` gating (event is optional, R3).
3. Render `<ExpenseCreateForm variant="page" members=… categories=… tags=…
   openEvents={eventsQuery.data ?? []} eventsLoading={eventsQuery.isPending}
   onCreated={(e) => navigate(\`/expenses/${e.uuid}\`)} />` (toast stays inside the form).

### Step 5 — Add-expense dialog (Feature 2)

New **`src/features/events/components/AddExpenseDialog.tsx`** (event-side, mirrors
`AssignExpenseDialog`'s location + conventions):

```ts
type AddExpenseDialogProps = {
  event: EventResponse;   // provides uuid + name (locked)
  open: boolean;
  onOpenChange: (open: boolean) => void;
};
```

- Fetch `useMembersQuery(false)`, `useCategoriesQuery(false)`, `useTagsQuery(false)`; while pending →
  skeleton inside `DialogContent`; on error → `ErrorState` with retry (reuse
  `expenses:form.loadErrorTitle/Body`).
- On loaded, render `<DialogContent size="lg" title={t("events:addExpense.title", { name:
  event.name })}>` containing `<ExpenseCreateForm variant="dialog" members=… categories=… tags=…
  lockedEventUuid={event.uuid} lockedEventName={event.name} onCreated={() => onOpenChange(false)}
  onEventUnavailable={(msg) => { toast.push({ tone: "danger", title: msg }); onOpenChange(false);
  }} />`.
- Rendered only while `open` (mount-on-open, like `AssignExpenseDialog`) so the queries + RHF reset
  cleanly each time.

### Step 6 — Event detail button (Feature 2, R4)

`src/features/events/pages/EventDetailPage.tsx` `DetailView`:

1. Add `const [addExpenseOpen, setAddExpenseOpen] = useState(false);`.
2. In `.detailActions`, inside the existing `{!closed ? (…) : null}` block (or a new one), add a
   primary/secondary `Button iconStart={<PlusIcon />} onClick={() => setAddExpenseOpen(true)}`
   labelled `events:detail.addExpense`. Import `PlusIcon` from `../components/icons` (already exists).
3. Render `{!closed ? <AddExpenseDialog event={event} open={addExpenseOpen}
   onOpenChange={setAddExpenseOpen} /> : null}` alongside the other dialogs.

### Step 7 — Query invalidation so the event refreshes (R6)

`src/features/expenses/hooks/useExpenses.ts` — make `useCreateExpense` also refresh the events
caches when the created expense joined an event (mirrors `useAssignExpenseEvent`; `eventsKeys` is
already imported):

```ts
export function useCreateExpense() {
  return useMutation({
    mutationFn: (body: CreateExpenseRequest) => expensesApi.create(body),
    onSuccess: (expense) => {
      invalidateExpense(expense.uuid);
      if (expense.eventUuid) {
        void queryClient.invalidateQueries({ queryKey: eventsKeys.all });
      }
    },
  });
}
```

This refreshes `eventsKeys.detail` (→ `expenseCount`) and `eventsKeys.balance` (→ the balance table);
the expenses section already refreshes via `expensesKeys.all`. Run
`gitnexus_impact({ target: "useCreateExpense", direction: "upstream" })` before editing and report the
blast radius (the only caller is the create form).

### Step 8 — i18n (R8, both locales, parity preserved)

`expenses.json` (vi-VN / en-US) — add under `form`:
`eventLabel` ("Đợt (tùy chọn)" / "Event (optional)"), `eventPlaceholder` ("Chọn đợt…" / "Choose an
event…"), `eventLoose` ("Không thuộc đợt (phiếu lẻ)" / "No event (loose expense)"),
`eventSearchPlaceholder` ("Tìm đợt…" / "Search events…"), `eventSearchEmpty` ("Không tìm thấy đợt" /
"No matching event"), `eventHint` ("Chỉ chọn được đợt đang mở, có khoảng ngày chứa thời điểm chi." /
"Only OPEN events whose date range contains the expense time can be selected."),
`eventLockedLabel` ("Đợt" / "Event"), `eventLockedHint` ("Phiếu sẽ được thêm vào đợt này." / "This
expense will be added to this event."). Reuse existing `expenseEvent.noOpenEvents` for the empty
state and `toast.created` for success.

`events.json` (vi-VN / en-US) — add: `detail.addExpense` ("Thêm phiếu" / "Add expense"),
`addExpense.title` ("Thêm phiếu vào đợt “{{name}}”" / "Add an expense to “{{name}}”").

### API endpoints consumed

| Screen/flow | Verb + path | Request DTO | Response `data` | Notable codes |
| --- | --- | --- | --- | --- |
| Create form (page + dialog) | `POST /v1/expenses` | `CreateExpenseRequest` incl. optional `eventUuid` | `ExpenseResponse` | `13002` monthly limit → `LimitNotice`; `6001/6002/6003` payer/category/tag → field; `7002/7003` share → form-level; `9002` out-of-range → `expenseTime` field; `9001/9000` event closed/gone → dialog toast+close / page form-level; `1001` → `applyFieldErrors` |
| Event picker (page) | `GET /v1/events?closed=false` | — | `EventSummaryResponse[]` | non-fatal on error → picker hidden |
| Dialog form data | `GET /v1/members`, `/v1/categories`, `/v1/tags` | — | respective `[]` | load error → `ErrorState` + retry |

All go through the centralized client; success unwraps `data`; failures throw `ApiError` (branch on
numeric `code`, render `error.message` verbatim).

### Loading / empty / error states

- **Create page:** unchanged skeleton on members/categories/tags; event picker shows a subtle
  `loading` hint while events load; **no open events →** muted hint (`expenseEvent.noOpenEvents`),
  field absent; events error → picker hidden (loose still works).
- **Dialog:** skeleton while members/categories/tags load; `ErrorState` + retry on load error;
  submit button `loading` while pending; locked event shown read-only throughout.
- **Errors:** as per the endpoint table; `LimitNotice` for 13002; field errors mapped via
  `applyFieldErrors`; `9002` on `expenseTime`.

### Form validation rules (mirroring backend)

- Existing create rules unchanged (name 1–200, description ≤1000, expenseTime required, share amount
  non-negative integer, note ≤500, no duplicate share member, owner-rep present).
- `eventUuid` is optional client-side; the backend stays authoritative on ownership/open/within-range
  (9000/9001/9002) — the client never re-checks the range locally (avoids TZ drift), it surfaces the
  server's localized message.

### Accessibility

- Event picker: `Combobox` provides ARIA-1.2 combobox/listbox + keyboard nav; label associated;
  error via `aria-describedby`. Locked display: a real `<label>`-associated read-only block with the
  lock conveyed by icon **and** text (`eventLockedLabel`/hint), not color alone.
- Dialog: focus trap + labelled title from the `Dialog` primitive; the "Thêm phiếu" trigger is a
  normal button with a text label (icon decorative).

### Tests the web-test-engineer should write (Vitest + RTL + MSW, pinned TZ/locale)

Feature 1 (`ExpenseCreatePage` / `ExpenseCreateForm`):
- Picker renders when `useEventsQuery({closed:false})` returns OPEN events; hidden with the
  `noOpenEvents` hint when the list is empty.
- Selecting an event puts `eventUuid` in the `POST /v1/expenses` body (assert via MSW request
  capture); default (no selection) omits `eventUuid` (loose).
- Choosing the "no event" option after a selection clears it (body omits `eventUuid`).
- `13002` renders `LimitNotice`; `9002` renders an error on the expenseTime field.

Feature 2 (`EventDetailPage` / `AddExpenseDialog`):
- OPEN event shows the "Thêm phiếu" button; CLOSED event does not.
- Opening the dialog shows the event locked (read-only, name visible, no editable event control).
- Submitting posts `eventUuid === event.uuid`; on success the dialog closes and
  `expenses:toast.created` shows.
- On success the event detail refetches: `expenseCount` / balance / expenses section update (assert
  refetch via MSW call count or updated rendered values).
- `9001`/`9000` → danger toast + dialog closes; `9002` → expenseTime field error, dialog stays open;
  `13002` → `LimitNotice` inside the dialog.
- i18n parity test stays green (new keys present in both locales).

## Impact Analysis

- **APIs:** none (backend feature-complete; `eventUuid` already accepted + validated).
- **UI (files):**
  - Edit: `api/types.ts` (add `eventUuid`), `schemas.ts` (create schema), `hooks/useExpenses.ts`
    (`useCreateExpense` events invalidation), `pages/ExpenseCreatePage.tsx` (use extracted form +
    events query), `pages/EventDetailPage.tsx` (button + dialog).
  - New: `components/ExpenseCreateForm.tsx` (extracted + generalized),
    `components/ExpenseEventField.tsx`, `features/events/components/AddExpenseDialog.tsx`
    (+ optional `.module.css`).
  - i18n: `locales/{vi-VN,en-US}/{expenses,events}.json`.
  - Tests: create-page/form + event-detail/add-dialog specs.
- **Services/state:** `useCreateExpense` gains conditional events invalidation — low risk, single
  caller; keeps invalidation in the hook layer (no query concerns leak into components).
- **Design system:** reuses `Combobox`, `Dialog`, `Badge`, `LimitNotice`, `FormError`, `ErrorState`,
  `Skeleton` — no new primitives.
- **Docs:** this planning doc; keep its Progress Log in sync.

## Decision Log

### Decision — one extracted `ExpenseCreateForm`, variant-driven, shared by page + dialog

Extract the page's inner form into `components/ExpenseCreateForm.tsx` with a `variant` ("page" |
"dialog") plus `lockedEventUuid`/`lockedEventName` and an `onCreated` hook. **Reason:** satisfies R9
(no forked form) while keeping the shared `ExpenseGeneralForm` untouched (edit dialog unaffected). The
event field lives in the create form (not the general form) because event linkage is create-only.
**Alternatives:** duplicating the form for the dialog (drift risk); pushing the event field into
`ExpenseGeneralForm` (would leak an event control into the edit dialog — rejected).

### Decision — `eventUuid` on the create schema only

Added to `createExpenseSchema`'s extend, not to `expenseGeneralSchema`, so edit stays clean and
`CreateExpenseValues` picks it up. **Reason:** event linkage is a create-time concern here; edit-time
linkage is a separate, already-shipped flow.

### Decision — events invalidation in `useCreateExpense` (conditional on `expense.eventUuid`)

Mirrors `useAssignExpenseEvent`. **Reason:** the event's `expenseCount` (`eventsKeys.detail`) and
balance (`eventsKeys.balance`) are not under `expensesKeys`, so without this the event detail would
show stale numbers after a create-from-event. Conditional so loose creates don't needlessly refetch
events.

### Decision — error routing: 9002 → field, 9000/9001 → context-dependent

`9002` on `expenseTime` (actionable); `9000/9001` toast+close in the dialog (via `onEventUnavailable`,
mirroring `AssignExpenseDialog`) and form-level on the page. **Reason:** consistency with the existing
event-side dialog convention; a locked event that vanished/closed makes the dialog moot.

## Progress Log

### 2026-07-19

- Feature-planner: completed required reading — `The-ideal.md` §3.6/§4 (event linkage + closed-event
  read-only), backend `CreateExpenseRequest` (`EventUuid` optional) + `ExpensesService.CreateAsync`
  error mapping (9000/9001/9002) + `AssignEventRequestValidator` (confirming create's `EventUuid` is
  NOT required), `ErrorCodes.cs`, and the front-end: `ExpenseCreatePage`, `ExpenseGeneralForm`,
  `ShareEditor`, `schemas.ts`, `api/types.ts`, `expensesApi.ts`, `useExpenses.ts`, `useEvents.ts`,
  events `api/types.ts`, `EventDetailPage`, `EventExpensesSection`, `AssignExpenseDialog`,
  `ExpenseEditDialog`, `EventFormDialog`, `Combobox`, the UI barrel, locale files, `CLAUDE.md`, and
  the frontend foundation doc.
- Drafted this two-feature plan: schema/type/body change, an extracted variant-driven
  `ExpenseCreateForm`, a new `ExpenseEventField`, an event-side `AddExpenseDialog`, the detail-page
  button, conditional events invalidation in `useCreateExpense`, i18n keys, states, a11y, and the
  test list.
- **3 Open Questions raised** (picker control + clear affordance; dialog form layout; event-field
  placement) — awaiting the checkpoint before implementation.

- Web-implementer: OQ1/OQ2/OQ3 resolved to recommended option **(a)**. Implemented all 8 steps:
  - Step 1 — `api/types.ts` `CreateExpenseRequest.eventUuid?`; `schemas.ts` `createExpenseSchema`
    `.extend({ eventUuid })` only (shared `expenseGeneralSchema` untouched).
  - Step 2/3 — extracted `components/ExpenseCreateForm.tsx` (variant `page`|`dialog`,
    `openEvents`/`eventsLoading`/`lockedEventUuid`/`lockedEventName`/`onCreated`/`onEventUnavailable`)
    + new presentational `components/ExpenseEventField.tsx` (locked display vs searchable Combobox
    with prepended loose option) + `.module.css` for both. Event control lives in the create form,
    rendered as the first row of the "Thông tin chung" section (OQ3a).
  - Step 4 — `pages/ExpenseCreatePage.tsx` now consumes the extracted form + `useEventsQuery({closed:
    false})` (not in the load gate).
  - Step 5 — new `features/events/components/AddExpenseDialog.tsx` (mount-on-open body → fresh
    queries + RHF per open; skeleton / ErrorState; locked event; 9000/9001 → toast+close).
  - Step 6 — `pages/EventDetailPage.tsx` OPEN-only "Thêm phiếu" (PlusIcon) primary button + dialog.
  - Step 7 — `useCreateExpense` now invalidates `eventsKeys.all` when `expense.eventUuid` present.
  - Step 8 — i18n keys added to both vi-VN + en-US (`expenses.json` form.event*, `events.json`
    detail.addExpense + addExpense.title); parity preserved.
  - `gitnexus_impact(useCreateExpense, upstream)` = **LOW** risk, 1 direct caller (the create form,
    itself refactored), 0 processes affected.
  - Verified: `pnpm lint` clean (pre-existing warnings only), `tsc -b` + `pnpm build` green, and a
    throwaway Playwright drive (MSW-mocked) confirmed the empty-state hint, the locked-event dialog,
    the success toast + dialog close, and the populated searchable picker.

- Web-test-engineer (tests): closed the MSW gap and added the linkage specs.
  - **MSW:** extended the `POST /v1/expenses` handler (`src/test/msw/handlers.ts`)
    to honour a submitted `eventUuid` and enforce ownership + open + within-range,
    mapping failures to the same codes as assign-to-event — `9000` (event not
    found), `9001` (event closed), `9002` (expense time outside range) — and to
    persist the resolved `eventUuid` on the created record instead of the previous
    hardcoded `null`. Empty/absent → loose expense (unchanged).
  - **F1** (extended `src/features/expenses/expenseCreatePage.test.tsx`, +6 cases):
    picker renders the loose option + OPEN events; empty OPEN list → muted
    `noOpenEvents` hint (control absent) and creation still posts a loose expense
    (no `eventUuid`); selecting an event puts `eventUuid` in the POST body; choosing
    the loose option after a selection clears it (body omits `eventUuid`); `9002` →
    `expenseTime` field error (no navigation); `9001` → form-level `FormError` on the
    page (no `onEventUnavailable` handler there).
  - **F2** (new `src/features/events/addExpenseDialog.test.tsx`, 7 cases): event is
    locked read-only (label + name + lock badge + hint, no editable control / no
    loose option); submit posts `eventUuid === event.uuid` then toasts
    `toast.created` + closes; a mounted events query refetches after a with-event
    create (proves `useCreateExpense`'s `eventsKeys.all` invalidation → detail
    refresh); `9001`/`9000` → danger toast + dialog closes; `9002` → `expenseTime`
    field error, dialog stays open; `13002` → `LimitNotice` inside the dialog.
  - **F2 button** (extended `src/features/events/eventDetailPage.test.tsx`, +3 cases):
    OPEN event shows an enabled "Thêm phiếu" button, CLOSED event hides it
    (read-only rule), and clicking opens the dialog with the event locked.
  - i18n parity stays green via the existing `expensesI18n`/`eventsI18n` structural
    tests (new `form.event*` + `detail.addExpense`/`addExpense.title` keys present in
    both locales). Full suite `pnpm test` = 861 passed / 102 files; `tsc -b` +
    `pnpm lint` clean. No product bugs found.

## Final Outcome

**Shipped (2026-07-19).** Both features implemented per plan with the recommended OQ options.

Files added:
- `src/features/expenses/components/ExpenseCreateForm.tsx` (+ `.module.css`)
- `src/features/expenses/components/ExpenseEventField.tsx` (+ `.module.css`)
- `src/features/events/components/AddExpenseDialog.tsx`

Files changed:
- `src/features/expenses/api/types.ts` — `CreateExpenseRequest.eventUuid?`
- `src/features/expenses/schemas.ts` — `createExpenseSchema` gains optional `eventUuid`
- `src/features/expenses/hooks/useExpenses.ts` — `useCreateExpense` conditional `eventsKeys.all`
  invalidation
- `src/features/expenses/pages/ExpenseCreatePage.tsx` — uses the extracted form + OPEN-events query
- `src/features/events/pages/EventDetailPage.tsx` — OPEN-only "Thêm phiếu" button + `AddExpenseDialog`
- `src/i18n/locales/{vi-VN,en-US}/expenses.json` and `.../events.json` — new keys (parity preserved)

API consumed: `POST /v1/expenses` (now with optional `eventUuid`), `GET /v1/events?closed=false`,
`GET /v1/members|/categories|/tags`. No backend change.

The shared `ExpenseGeneralForm` and `ExpenseEditDialog` are behaviour-unchanged (the event control
was added to the create form only). Vitest specs (per the test list above) are left for the
web-test-engineer; the MSW `POST /v1/expenses` handler still hardcodes `eventUuid: null` and does not
yet emit 9000/9001/9002 — the test-engineer will extend it to exercise the linkage + error branches.

## Future Improvements

- Optional pre-fill of the expense's `expenseTime` to fall inside the event's range when opening the
  dialog (reduce 9002 friction), and/or a client-side range hint on the picker.
- Allow choosing/creating an event inline from the picker (a "Tạo đợt mới" affordance) for users who
  realize mid-create they want a new event.
- Consider a shared `useExpenseFormData()` hook to co-locate the members/categories/tags queries the
  page and dialog both need.
