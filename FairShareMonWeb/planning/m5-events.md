# M5 — Events (lifecycle + closed-event UI)

## Objective

Build the FairShareMonWeb **events** surface: the events list (with an open/closed filter), the event
**detail** (info + status + one-way close + the §3.7 **debt-balance table** + the event's expenses +
assign/remove-expense + CSV export), event **create/edit** (open-only), **delete** (open-only; expenses
become loose), and the **one-way close** action. This lights up the closed-event immutability write-guard
that M4 wired but left inert (`eventIsClosed`), and closes the M4 deferral (OQ7) by adding the
**event-select filter** to the expenses list now that an events list/query exists. All against the
feature-complete, stable `api/v1/events` controller (+ `/balance`, `/export`, `/close`) and the
expense-side assign/remove routes, on the locked foundation stack and the M2–M4 primitives.

## Background

- **Roadmap:** M5 is size **M/L**, locked 2026-07-17 in `planning/feature-roadmap.md` (all 6 roadmap OQs
  at option a). It depends on **M4** (expenses reference events; balance is computed from expense shares;
  the assign/remove routes live on the expense) and reuses M2 (members) for balance-row identity.
- **Backend is feature-complete and stable.** `Controllers/EventsController.cs` exposes 6 event routes +
  `GET /events/{uuid}/balance` + `GET /events/{uuid}/export` + `GET /events/{uuid}/qr` (QR is M7, not
  built here); the expense-side `PUT /expenses/{uuid}/event` + `DELETE /expenses/{uuid}/event` handle
  assign/remove. Backend semantics + all 16 backend OQs are locked in `FairShareMonApi/planning/events.md`
  and honored by `The-ideal.md` §3.6/§3.7/§4.4/§5. Verified DTO shapes against `Models/Events/**` and
  `Models/Stats/{EventBalanceResponse,MemberBalanceRow}.cs`.
- **Locked backend contract this UI mirrors:**
  - **One-way close** (`PUT /events/{uuid}/close`): never reopenable, never automatic; re-close →
    `9001 EventClosed`. No preconditions (an empty event can close).
  - **Closed events are immutable** (§4.4): edit/delete/assign/remove and every expense/share write are
    rejected on a closed event (`9001`); the **sole exception is the per-share settled flag on expenses**
    (there is no settled concept on the event itself). M4 already disables expense/share write controls
    off `eventIsClosed`; M5 disables event-level write controls off `IsClosed`.
  - **Delete is open-only** and **hard**; its expenses are **not** deleted — they go loose
    (`event_id` → null, DB `SET NULL`). Delete on a closed event → `9001`.
  - **Date range** is whole-day inclusive, normalized to day bounds **in the request timezone**
    (`X-Time-Zone`) → UTC by the M4/M6 timezone feature; the DTO carries offset-aware ISO `startDate`
    (00:00:00) / `endDate` (23:59:59.999999). DB CHECK `ck_events_date_range` (`end_date >= start_date`);
    on-input `endDate < startDate` is a **`1001`** validation error (mirror in Zod), not a 9xxx code.
  - **Assign / move** (`PUT /expenses/{uuid}/event`): the target event must be **owned + OPEN** and the
    expense's `expenseTime` **within the range**; a **move** out of a **closed source** event → `9001`;
    out-of-range → `9002`; unknown/foreign target → `9000`. **Remove** (`DELETE …/event`) is a no-op if
    already loose, and blocked while the current event is closed (`9001`).
  - **Range edit that would exclude an assigned expense** → `9003`
    (`EventRangeExcludesAssignedExpenses`).
  - **Balance** (`GET /events/{uuid}/balance` → `EventBalanceResponse`): per participating member (payer
    or bearer, **incl. the owner-rep at 0đ and soft-deleted members**) `advanced` / `owed` / `balance`
    (= advanced − owed); the row set **sums to zero**; **settled is ignored**; viewable for **both open
    and closed** events; no expenses → empty `rows`. Money is `decimal`, rendered **verbatim** (never
    float-math, R3).
  - **Export** (`GET /events/{uuid}/export?format=csv`): CSV blob for **both open and closed** events;
    unsupported `format` → `400`; ownership miss → `404`.
  - **Detail does not embed expenses** (backend OQ15): `GET /events/{uuid}` returns fields + derived
    `expenseCount`; the event's expenses are listed via `GET /expenses?eventUuid=…` — the M4 filter seam
    the backend already carries on `ExpenseFilter`.
- **Error codes already present** in the mirror (`src/lib/api/errors.ts`): `9000 EventNotFound` (in
  `NOT_FOUND_CODES` + `classifyError` → notFound), `9001 EventClosed`, `9002 ExpenseTimeOutOfEventRange`,
  `9003 EventRangeExcludesAssignedExpenses`, and `13001 OpenEventLimitReached` (in `FREE_LIMIT_CODES` →
  `limit`). **No `errors.ts` change is needed** in M5 (append-only mirror already complete).
- **Shipped foundation + M2–M4 reused (do not fork):** the centralized `api` client
  (`api.get/post/put/delete` + `api.blob`), envelope unwrap + typed `ApiError` (`code`/`message`/
  `fields`), `classifyError`/`resolveErrorMessage`/`applyFieldErrors`, TanStack Query hook pattern
  (query-key factory + `invalidateQueries` on mutate), RHF + Zod localized-factory schemas, react-i18next
  namespaces, `formatMoneyVnd`/`formatDate`/`formatDateTime`/`getTimeZone`, the shared
  `src/lib/download/downloadBlob.ts` (built in M4). Design system: `Table` family, `Dialog`, `Money`,
  `Badge`, `Button` (incl. `asChild`), `Select`, `PageHeader`/`Stack`, `Card`/`CardHeader`/`CardBody`,
  `DescriptionList`/`DescriptionRow`, `Alert`, `EmptyState`/`ErrorState`/`Skeleton`, `LimitNotice`,
  toasts. M4 seams consumed: `useExpensesQuery` (event/loose filter), `ExpensesTable`/`ExpenseFilterBar`,
  the `eventUuid`/`eventName`/`eventIsClosed` fields already on the expense DTOs.
- **M4 seams this milestone completes:**
  - `ExpenseFilter` (frontend `features/expenses/api/types.ts`) carries `looseOnly` but **not yet**
    `eventUuid`; `ExpenseFilterBar` explicitly defers the event-select filter to M5. M5 adds both.
  - The expense-side assign/move/remove routes are **not yet** in `expensesApi`/`useExpenses`; M5 adds
    them + an `ExpenseEventControl` on the expense detail (today the event row is read-only).
- **Carried-forward M4 review nits (apply where M5 touches those areas):**
  - **Delete/confirm close-on-error** (M4 OQ12a): M5's delete-event + close-event confirms close only on
    success + terminal codes (`9000`/`9001`), staying open on network/transient for in-place retry.
  - The M4 **audit order-insensitive array-diff** nit and the **delete-dialog retrofit** for M2/M3 are
    **not relevant** to M5 (events are not audited; M5 adds no audit UI).

## Requirements

### Functional

1. **List (`/events`)** — the caller's events in backend order (`startDate` DESC, then `createdAt`
   DESC — rendered verbatim, no client re-sort). Columns: name (row header), date range
   (`formatDate` start–end), status badge (open/closed), expense count, created-at. An **open/closed
   filter** (all / open / closed) reflected in the URL. A "Thêm đợt" create button. Loading (skeleton
   rows), empty (EmptyState + "create"), no-matches (filtered), and error (retry) states.
2. **Detail (`/events/:uuid`)** — header (name, date range, status badge, `ClosedAt` when closed) with
   actions: **Edit**, **Delete**, **Close** (open-only), **Export CSV** (always). The **debt-balance
   table** (per member advanced/owed/balance via `Money`, sum-to-zero footer, owner-rep + deleted
   markers). The **event's expenses** section (`GET /expenses?eventUuid=…`) with an **Assign expense**
   picker and per-row **remove-from-event** (open-only). Ownership `9000` → shared not-found view (R1).
3. **Create** — dialog (name, description, start date, end date). New events are always open. Single
   submit → `POST /events`. Free open-event limit `13001` → informational `LimitNotice` (create only, R9).
4. **Edit** — same dialog, pre-filled, **open-only** (closed → no edit control + defensive `9001`
   toast+close). Range edit that would exclude an assigned expense → `9003` surfaced clearly.
5. **Delete** — confirm dialog explaining hard-delete + that the event's expenses **become loose** (not
   deleted); **open-only**. On success → back to `/events`.
6. **Close** — a prominent, clearly-worded **irreversible** confirm; one-way. Re-close (`9001`) handled
   defensively. On success → status flips to closed; all event-level write controls disable; the event's
   expenses become read-only (M4 guard lights up).
7. **Assign / move / remove expense (both surfaces — OQ3a):**
   - **Event detail:** an "Assign expense" picker (eligible expenses = loose + in-range — OQ4a) → `PUT
     /expenses/{uuid}/event`; a per-row "remove from event" → `DELETE /expenses/{uuid}/event`. Open-only.
   - **Expense detail:** an `ExpenseEventControl` to assign/move (open event `Select`) or remove.
   - Within-range (`9002`) and range-conflict/closed-source (`9001`/`9003`) surfaced with clear copy.
8. **CSV export** — an "Xuất CSV" action on the event detail → `api.blob` → `downloadBlob`, for both
   open and closed events.
9. **Expenses-list event filter (M4 OQ7 deferral):** add an **event `Select`** to `ExpenseFilterBar`
   (options from `useEventsQuery`), wire `eventUuid` through `ExpenseFilter` + the URL on `ExpensesPage`.

### Non-functional / conventions

- One centralized client only; all types feature-local under `features/events/api/types.ts` mirroring the
  backend DTOs (reuse `MemberResponse` where relevant — the balance row is denormalized, so it carries
  its own `memberUuid`/`memberName`). Branch on numeric `code`; render `error.message` verbatim.
- Money: render `advanced`/`owed`/`balance` via `Money`/`formatMoneyVnd`; **never float-math** and never
  client-sum the balance (the API is authoritative and sums to zero).
- Datetimes: send `X-Time-Zone` (client already does); present via `formatDate` (range) /
  `formatDateTime` (`closedAt`, `createdAt`). Date-range submitted per OQ5.
- i18n: new `events` namespace (vi-VN authoritative + en-US parity); validation strings under the
  `validation` namespace (`event.*`). Fixed domain terms: đợt (event), phiếu chi tiêu (expense), phần
  gánh (share), đã trả (settled), đã ứng / phải gánh / cân bằng (advanced / owed / balance).
- a11y: labeled controls; balance table has a `<caption>` + a sum-to-zero footer row; status
  color-independent (badge text + icon, not color alone); balance sign color-independent
  (sign/label + `Money`, not color alone); the close confirm is keyboard-operable and clearly worded.
- No `errors.ts` change (all 9xxx + 13001 already present). Append-only if any gap is found (flag it).

## Open Questions

> Each has a firm **Recommendation**; the orchestrator auto-accepts recommendations. None is flagged
> CRITICAL — every one has a safe, reversible default consistent with M2–M4. OQ5 is the only one with a
> subtle correctness dimension (date-range/timezone) and is called out for extra care.

### OQ1 — Event detail: dedicated route vs modal. (recommend a) — RESOLVED 2026-07-17: option a

- **(a) Recommended — a dedicated route `/events/:uuid`.** Consistent with M4's `/expenses/:uuid`
  (OQ1a); deep-linkable, room for the balance table + expenses section + actions; ownership `9000` →
  shared not-found inline (R1). Trade-off: one more route + a fetch on navigation (cache-warmed from the
  list). The event's expenses are a second query (`GET /expenses?eventUuid=…`) — cheap, matches the
  backend's non-embedding design (OQ15).
- (b) A modal from the list. Trade-off: no deep-link; cramped for the balance table + expenses list.

### OQ2 — Create/edit surface: dialog vs page. (recommend a) — RESOLVED 2026-07-17: option a

- **(a) Recommended — a shared `EventFormDialog` for create + edit** (mirrors `MemberFormDialog`/
  `CategoryFormDialog`). The event form is small (name, description, two dates) with no variable-height
  editor, so a dialog fits; create opens from the list + detail, edit from the detail. Trade-off: none
  material.
- (b) A full page `/events/new` + `/events/:uuid/edit`. Trade-off: heavier shells for a 4-field form.

### OQ3 — Where assign/remove-expense lives. (recommend a) — RESOLVED 2026-07-17: option a

- **(a) Recommended — both surfaces.** Event detail carries an "Assign expense" picker dialog + a
  per-row "remove from event" action; the expense detail carries an `ExpenseEventControl`
  (assign/move/remove). Both call the same expense-side routes; both respect open-only + within-range.
  Matches the roadmap ("assign/remove actions" on the event detail *and* "assign/remove-event
  expense-side routes to consume"). Trade-off: two entry points to build — mitigated by a shared hook
  pair (`useAssignExpenseEvent`/`useRemoveExpenseEvent`) and shared error mapping.
- (b) Event detail only. Trade-off: the expense detail's event row stays read-only (a gap for the "move
  this expense" flow from the expense side).
- (c) Expense detail only. Trade-off: the event detail can't curate its own expenses — awkward for the
  "add these to the trip" flow.

### OQ4 — Eligible expenses in the event-detail "Assign expense" picker. (recommend a) — RESOLVED 2026-07-17: option a

- **(a) Recommended — offer the caller's *loose* expenses whose `expenseTime` is within the event's date
  range** (fetched via `useExpensesQuery({ looseOnly: true, from, to })` seeded from the event range),
  with a client note; rely on the backend for final validation (`9002`). This is the dominant "add my
  un-grouped expenses to this trip" case and keeps the picker small. Trade-off: moving an expense that is
  *already in another open event* is done from the **expense** side (`ExpenseEventControl`), not this
  picker.
- (b) Offer *all* in-range expenses (loose + assigned-elsewhere), enabling a move from the picker.
  Trade-off: richer, but mixes "add" and "move" semantics and shows expenses already owned by other
  events (confusing) — the source-closed case must be handled per row.
- (c) Offer *all* the caller's expenses (no range pre-filter), leaning entirely on `9002`. Trade-off:
  simplest to build, most server round-trips into validation errors.

### OQ5 — Date-range submission format / timezone anchoring. (recommend a — extra care) — RESOLVED 2026-07-17: option a

The backend normalizes the incoming `startDate`/`endDate` to whole-day bounds **in the request
timezone** (`X-Time-Zone`) → UTC. We must send a datetime that resolves to the intended **calendar day
in the viewer's zone**.

- **(a) Recommended — the user picks calendar dates (`<input type="date">`); the client submits each as
  an offset-aware ISO at **local noon (12:00) of that day** in the viewer's zone** (e.g.
  `new Date("2026-07-16T12:00:00").toISOString()`). Noon-anchoring makes the calendar date unambiguous
  under any DST/offset so the backend's day extraction can never drift by ±1 day. The backend then
  normalizes to the full-day bounds itself. Trade-off: we send noon, not midnight — irrelevant because
  the backend re-normalizes to `00:00:00`/`23:59:59.999999`; the noon anchor exists purely to pin the
  date.
- (b) Submit local **midnight** ISO for both (mirrors `dateBoundToIso(…, false)`). Trade-off: consistent
  with the expense filter helper, but midnight sits exactly on the day boundary — a defensive risk if the
  backend ever extracts the date in a different zone; noon is strictly safer at no cost.
- (c) Submit a bare `YYYY-MM-DD` string. Trade-off: cleanest intent, but the DTO is `DateTime`;
  serialization/kind is ambiguous — avoid.

### OQ6 — Balance visualization (bar) now or later. (recommend a) — RESOLVED 2026-07-17: option a

- **(a) Recommended — table-only for M5** (advanced/owed/balance columns + a sum-to-zero footer). Matches
  the roadmap ("the balance is a table, not a chart — dataviz not required; a light bar … is optional and
  deferred"). Trade-off: no at-a-glance bar — deferred to Future Improvements (and the M6 dataviz layer).
- (b) Add a light diverging advanced/owed bar now. Trade-off: pulls a slice of dataviz forward before the
  M6 chart layer exists — churn.

### OQ7 — Delete/close confirm close-on-error behavior. (recommend a) — RESOLVED 2026-07-17: option a

- **(a) Recommended — carry M4 OQ12a forward:** the delete-event and close-event confirms close on
  success and on terminal codes (`9000` stale, `9001` already-closed/closed-guard), and stay **open** on
  network/transient errors with an inline message for in-place retry. Trade-off: a small divergence from
  the M2/M3 dialogs (their retrofit is already tracked) — worth the correct behavior in new code.
- (b) Close in `finally` (match M2/M3). Trade-off: re-introduces the known nit.

### OQ8 — Show the balance for open events too, or only closed. (recommend a) — RESOLVED 2026-07-17: option a

- **(a) Recommended — show the balance for both open and closed events** (the backend computes it for
  both). Useful mid-trip ("who owes what so far"); the table simply reflects current data. Trade-off:
  none — the number is live and re-fetched on expense/share changes via invalidation.
- (b) Only render the balance once closed. Trade-off: hides a genuinely useful view the API already
  supports.

### OQ9 — Free open-event limit (`13001`) surfacing. (recommend a) — RESOLVED 2026-07-17: option a

- **(a) Recommended — reactive inside the create dialog**, mirroring M2's member-limit pattern: on
  `13001` show an inline `LimitNotice` (informational, form stays mounted, no navigation). Only Free
  users hit it; there is no self-serve upgrade (R9). Trade-off: the notice appears on attempt, not
  proactively — consistent with M2/M4 and avoids pre-counting open events client-side.
- (b) Proactively disable "Thêm đợt" when at the limit. Trade-off: needs a client-side open-event count +
  the tier's limit value (not exposed) — guesswork; rejected.

### OQ10 — CSV export availability. (recommend a) — RESOLVED 2026-07-17: option a

- **(a) Recommended — expose "Xuất CSV" for both open and closed events** (the backend allows both).
  Trade-off: none. (The per-event **QR** is Premium + closed-only and is **M7**, not built here.)
- (b) Closed-only export. Trade-off: contradicts the backend, which supports open-event export.

## Assumptions

- No backend change is needed; every route/DTO/code above exists and is stable (verified against
  `EventsController.cs`, `ExpensesController.cs` assign/remove routes, `Models/Events/**`,
  `Models/Expenses/AssignEventRequest.cs`, `Models/Stats/{EventBalanceResponse,MemberBalanceRow}.cs`, and
  `FairShareMonApi/planning/events.md`).
- The events list is **unpaginated** (backend OQ10a) — the full owned list is returned; the tier cap
  bounds volume.
- `GET /events/{uuid}/balance` returns `rows: []` for an event with no expenses and never leaks a foreign
  event (ownership `9000`). `balance` sums to zero across rows and is authoritative (rendered verbatim).
- `eventName`/`eventIsClosed` are already on both expense DTOs (M4), so the event's expenses render their
  linkage without extra calls; the closed-event write guard on those expenses is already wired off
  `eventIsClosed` (M4) — closing an event and invalidating the expense caches lights it up with no
  expense-side change.
- The 9xxx codes + `13001` are already in `src/lib/api/errors.ts`; `9000` already classifies as
  `notFound` and `13001` as `limit`. No mirror edit expected (flag if a gap surfaces).
- The date-range timezone normalization is entirely backend-side (M4/M6 timezone feature); the client's
  job is to submit a datetime that pins the intended calendar day (OQ5).

## Implementation Plan

> Paths under `FairShareMonWeb/src/`. Feature tree: `features/events/{api,hooks,pages,components}` +
> `schemas.ts`. All copy via i18n; all data through the one `api` client + TanStack Query. Concrete names
> reflect the recommended OQ options. Steps marked **[M4-MOD]** modify shipped M4 files.

### Step 1 — API types (`features/events/api/types.ts`)

Mirror the backend DTOs (datetimes as ISO `string`; money as `number`, rendered never computed):
- `EventSummaryResponse { uuid; name; startDate; endDate; isClosed; closedAt?; expenseCount; createdAt }`.
- `EventResponse` (adds `description?`).
- `CreateEventRequest { name; description?; startDate; endDate }`,
  `UpdateEventRequest` (same shape).
- `EventFilter { closed?: boolean }`.
- `MemberBalanceRow { memberUuid; memberName; isOwnerRepresentative; isDeleted; advanced; owed; balance }`.
- `EventBalanceResponse { eventUuid; eventName; isClosed; rows: MemberBalanceRow[] }`.
- `AssignEventRequest { eventUuid: string }` (also add to expenses feature — Step 8).

### Step 2 — API client (`features/events/api/eventsApi.ts`)

One object over `api` (only defined filter keys sent):
- `list(filter: EventFilter)` → `api.get<EventSummaryResponse[]>("/v1/events", { query: { closed: filter.closed } })`.
- `get(uuid)` → `api.get<EventResponse>(\`/v1/events/${uuid}\`)`.
- `create(body)` → `api.post<EventResponse>("/v1/events", body)`.
- `update(uuid, body)` → `api.put<EventResponse>(\`/v1/events/${uuid}\`, body)`.
- `remove(uuid)` → `api.delete<MessageResponse>(\`/v1/events/${uuid}\`)`.
- `close(uuid)` → `api.put<MessageResponse>(\`/v1/events/${uuid}/close\`)` (no body).
- `balance(uuid)` → `api.get<EventBalanceResponse>(\`/v1/events/${uuid}/balance\`)`.
- `exportCsv(uuid)` → `api.blob("GET", \`/v1/events/${uuid}/export\`, { query: { format: "csv" } })`.

### Step 3 — Hooks (`features/events/hooks/useEvents.ts`)

- Key factory: `eventsKeys = { all: ["events"], list: (filter) => ["events","list",filter],
  detail: (uuid) => ["events","detail",uuid], balance: (uuid) => ["events","balance",uuid] }`.
- Queries: `useEventsQuery(filter)`, `useEventQuery(uuid)`, `useEventBalanceQuery(uuid)`.
- Mutations (invalidation reach below): `useCreateEvent`, `useUpdateEvent`, `useDeleteEvent`,
  `useCloseEvent`.
- `useExportEvent` — `useMutation` calling `exportCsv` → `downloadBlob` (reuse `src/lib/download/`).
- **Cross-cache invalidation:** `useUpdateEvent`/`useCloseEvent`/`useDeleteEvent` invalidate
  `eventsKeys.all` + the specific `detail(uuid)` + `balance(uuid)` **and** `expensesKeys.all`
  (close flips `eventIsClosed` on expenses; delete makes them loose). `useCreateEvent` invalidates
  `eventsKeys.all`.

### Step 4 — Zod schema (`features/events/schemas.ts`)

Localized factory mirroring `CreateEventRequestValidator`/`UpdateEventRequestValidator`:
- `eventFormSchema(t)` — `name` trim min 1 max **200** (`validation:event.nameRequired`/`nameTooLong`);
  `description` optional max **1000** (`descriptionTooLong`); `startDate` required date string
  (`startRequired`); `endDate` required (`endRequired`); `.superRefine` → `endDate >= startDate`
  (`rangeInvalid`, attached to `endDate`) — mirrors the backend `1001` validator + the
  `ck_events_date_range` CHECK. Constants `EVENT_NAME_MAX = 200`, `EVENT_DESC_MAX = 1000`.
- Export `EventFormValues`.

### Step 5 — Date-range helper (`features/events/dateRange.ts`)

- `isoToDateInput(iso)` → `"YYYY-MM-DD"` in the viewer's zone (for pre-filling edit).
- `dateInputToIso(date)` → offset-aware ISO at **local noon** (OQ5a) for submit.
- `formatRange(startIso, endIso, formatDate)` → a localized "start – end" display string.

### Step 6 — List page (`features/events/pages/EventsPage.tsx` + `.module.css`)

- Route `/events`. `PageHeader` (title + "Thêm đợt" `Button` → opens `EventFormDialog` in create mode).
- **Open/closed filter** via a `Select` (all / open / closed) reflected in the URL (`?status=`) with
  `useSearchParams`; maps to `EventFilter.closed` (`open`→false, `closed`→true, `all`→undefined).
- `components/EventsTable.tsx` (reuse `Table` family): name (row header, links to detail) → date range
  (`formatRange`) → status (`EventStatusBadge`) → expense count → created-at → actions (view link).
- States: `isPending` → skeleton rows; `isError` → `ErrorState` + retry; empty (no events) vs no-matches
  (filter active) → `TableEmpty` + `EmptyState`.

### Step 7 — Detail page (`features/events/pages/EventDetailPage.tsx` + `.module.css`)

- Route `/events/:uuid`. `useEventQuery(uuid)`. Ownership `9000` → `classifyError === "notFound"` →
  shared `NotFound` inline (R1); other errors → `ErrorState` + retry; pending → skeleton. Back link to
  `/events`.
- Header (ui-designer): name, date range, `EventStatusBadge`, `closedAt` when closed; actions cluster:
  **Edit** (open-only), **Close** (open-only, opens `CloseEventDialog`), **Export CSV**
  (`useExportEvent`), **Delete** (open-only). When `isClosed`, render an `Alert` (tone warning, lock)
  explaining the event is closed/immutable and that only export + viewing remain; disable all write
  actions.
- Info `Card` (`DescriptionList`): description, date range, status, closedAt, expenseCount, createdAt.
- `components/EventBalanceTable.tsx` — the **debt-balance table** (Step 9).
- `components/EventExpensesSection.tsx` — the event's expenses + assign/remove (Step 10).
- Mounts `EventFormDialog` (edit), `DeleteEventDialog`, `CloseEventDialog`, `AssignExpenseDialog`.

### Step 8 — [M4-MOD] Expense-side assign/move/remove + the event filter

- **[M4-MOD]** `features/expenses/api/types.ts` — add `eventUuid?: string` to `ExpenseFilter`; add
  `AssignEventRequest { eventUuid: string }`.
- **[M4-MOD]** `features/expenses/api/expensesApi.ts` — add to `filterQuery`: `eventUuid: filter.eventUuid`;
  add `assignEvent(uuid, body: AssignEventRequest)` → `api.put<ExpenseResponse>(\`/v1/expenses/${uuid}/event\`, body)`
  and `removeEvent(uuid)` → `api.delete<MessageResponse>(\`/v1/expenses/${uuid}/event\`)`.
- **[M4-MOD]** `features/expenses/hooks/useExpenses.ts` — add `useAssignExpenseEvent` +
  `useRemoveExpenseEvent`; both `onSuccess` invalidate `expensesKeys.all` + `detail(uuid)` **and**
  `eventsKeys.all` (+ the affected event `detail`/`balance`) so counts/balances refresh.
- **[M4-MOD]** `features/expenses/components/ExpenseFilterBar.tsx` — add an **event `Select`** (all +
  the caller's events from `useEventsQuery(undefined)`, labelled with a closed marker) as a new
  `UiFilters.eventUuid`; update the "deferred to M5" comment. (Keep `looseOnly` toggle; an event
  selection and loose-only are mutually exclusive — selecting an event clears loose-only and vice versa.)
- **[M4-MOD]** `features/expenses/pages/ExpensesPage.tsx` — add `eventUuid` to the URL params
  (`?event=`), the `UiFilters`, `hasActiveFilters`, `applyPatch`, and the `apiFilter`.
- **New** `features/expenses/components/ExpenseEventControl.tsx` — on the expense detail: shows the
  current event (or "loose") with an assign/move `Select` of **open** events (`useEventsQuery`, filtered
  to `!isClosed`) + a remove action; disabled when the expense's own `eventIsClosed` (can't move out of a
  closed event → defensive `9001`). Error mapping: `9000`→toast (stale target), `9001`→toast (closed
  source/target), `9002`→inline "expense time outside the event range". Wire into `ExpenseDetailPage`
  (replace the read-only event `DescriptionRow` action).

### Step 9 — Balance table (`features/events/components/EventBalanceTable.tsx` + `.module.css`)

- `useEventBalanceQuery(uuid)`. Reuse `Table` family. Columns: member (name; owner-rep marker; "(đã xóa)"
  when `isDeleted`, R7) → advanced (`Money`, numeric) → owed (`Money`, numeric) → balance (`Money`,
  numeric, **sign-labelled**: positive = "được nợ / nên nhận", negative = "đang nợ / nên trả",
  color-independent). A **footer row** shows the column sums (from the API rows, display-only) proving
  sum-to-zero for balance; label "Tổng" with a note that balances net to 0.
- States: pending → skeleton; error → inline retry; `rows.length === 0` → calm "Chưa có phiếu nào trong
  đợt" empty note (balance only exists once there are expenses). Rendered for both open and closed (OQ8a).

### Step 10 — Event expenses + assign/remove (`features/events/components/`)

- `EventExpensesSection.tsx` — lists the event's expenses via `useExpensesQuery({ eventUuid: uuid })`
  (reuse the M4 hook). Renders a compact table (reuse `Table` primitives): name (link to
  `/expenses/:uuid`) → payer → total (`Money`) → expense time → **remove-from-event** action (open-only).
  Header action "Gán phiếu" (open-only) opens `AssignExpenseDialog`. States: pending/empty/error. When
  the event is closed, the remove + assign controls are hidden and a short read-only note is shown.
- `AssignExpenseDialog.tsx` — the picker (OQ4a): `useExpensesQuery({ looseOnly: true, from, to })` seeded
  from the event's date range; a searchable/selectable list (name + time + total); confirm → for each
  selected, `useAssignExpenseEvent({ uuid: expenseUuid, body: { eventUuid } })`. Error per code
  (`9002` out-of-range → inline per row; `9001`/`9000` → toast). Close-on-success; keep open on transient.

### Step 11 — Dialogs + badge

- `components/EventFormDialog.tsx` — shared create/edit (OQ2a), RHF + `eventFormSchema`. Fields: name
  `TextField`, description `TextField` (multiline), start `<input type="date">`, end `<input type="date">`
  (Field-wrapped). Submit builds `Create/UpdateEventRequest` with `dateInputToIso` (OQ5a). Errors:
  `13001` (create) → inline `LimitNotice` (OQ9a); `9001` (edit closed) → toast+close; `9003` (range
  excludes assigned) → form-level message (or attach to endDate); `9000` (edit stale) → toast+close;
  `1001` → `applyFieldErrors` onto name/description/startDate/endDate; else `FormError`.
- `components/DeleteEventDialog.tsx` — confirm; body explains hard-delete + **expenses become loose**
  (not deleted). Open-only. Close-on-error per OQ7a (terminal `9000`/`9001` close; network stays open).
  Success → toast + navigate to `/events`.
- `components/CloseEventDialog.tsx` — a strong, clearly-worded **irreversible** confirm ("Chốt đợt là
  hành động một chiều — không thể mở lại. Sau khi chốt, mọi thay đổi phiếu/phần gánh của đợt bị khóa, trừ
  trạng thái đã trả."). `useCloseEvent`. Close-on-error per OQ7a; re-close `9001` → toast+close (already
  closed) + invalidate. Success → toast; detail re-renders closed.
- `components/EventStatusBadge.tsx` — `Badge` (open: neutral/positive; closed: neutral + lock icon),
  color-independent text.

### Step 12 — Wiring

- `routes/router.tsx`: replace the `/events` `StubPage` with a child route group — `events` index →
  `EventsPage`, `events/:uuid` → `EventDetailPage` — under the existing `AppShellLayout`/`ProtectedRoute`
  subtree (mirror the `/expenses` group).
- `i18n/index.ts` + `useT.ts`: register the new `events` namespace (vi-VN + en-US) in `resources`,
  `NAMESPACES`; add `locales/{vi-VN,en-US}/events.json`; extend `validation.json` with `event.*` keys.

### i18n keys (namespace `events`, vi-VN authoritative + en-US parity)

- `title`, `subtitle`, `add`, `list.*` (headers: name/range/status/expenseCount/createdAt/actions;
  caption; empty.title/body; noMatches.title/body; error.title/retry; view).
- `filter.*` (status label; status.all/open/closed).
- `status.open`, `status.closed`, `closedAt`.
- `detail.*` (back, infoTitle, description, range, status, closedAt, expenseCount, createdAt,
  notFound.title/body, error.title; actions.edit/delete/close/export; closedTitle, closedBody).
- `form.*` (createTitle, editTitle, nameLabel/placeholder, descriptionLabel, startLabel, endLabel,
  submitCreate, submitEdit, cancel).
- `balance.*` (title, caption, member, advanced, owed, balance, totalRow, sumsToZeroHint, ownerRep,
  positiveLabel [được nợ], negativeLabel [đang nợ], zeroLabel, empty).
- `expensesSection.*` (title, assign, name, payer, total, time, remove, empty, closedNote).
- `assign.*` (title, searchPlaceholder, hintInRange, confirm, cancel, empty, outOfRange).
- `expenseEvent.*` (label, none/loose, assign, move, remove, changePlaceholder, outOfRange, closedNote).
- `close.*` (title, body, confirmButton, cancel, toast).
- `delete.*` (title, body [loose-expenses], confirmButton, cancel).
- `limit.*` (title, body for `13001`).
- `export.*` (button, toast.error), `toast.*` (created, updated, closed, deleted, assigned, removed).
- `validation` namespace additions: `event.nameRequired/nameTooLong/descriptionTooLong/startRequired/
  endRequired/rangeInvalid`.
- **[M4-MOD]** `expenses.json`: add `filter.event` + `filter.eventAll` (the new event `Select` labels).

### Accessibility

- Balance table: `<caption>`, numeric cells right-aligned, a footer sum row; balance sign conveyed by a
  label/sign + `Money` (never color alone).
- Status badge color-independent (text + lock icon for closed).
- Close confirm: focus-trapped `Dialog`, an explicit irreversible-action description, primary danger
  button keyboard-reachable.
- Date inputs `<label for>`-associated; range error tied via `aria-describedby`.
- Assign picker: keyboard-operable list; each row labelled with the expense name + time.
- Detail not-found path uses the shared `NotFound` (no existence leak).

### Tests (web-test-engineer — Vitest + RTL, MSW at the client boundary; pinned TZ + vi-VN)

**Schemas** (`schemas.test.ts`): name required/too-long; description too-long; start/end required;
`endDate >= startDate` refinement (equal ok, before → error on `endDate`); exact vi-VN messages.

**Date helper** (`dateRange.test.ts`): `isoToDateInput` round-trips a known ISO in `Asia/Ho_Chi_Minh`;
`dateInputToIso` yields a noon-anchored ISO whose calendar date matches the input (no ±1 drift);
`formatRange` output.

**Hooks** (`useEvents.test.tsx`): key-factory shapes; list sends only defined `closed`; create invalidates
`events` root; update/close/delete invalidate `events` (detail+balance) **and** `expenses` root;
`useExportEvent` → `downloadBlob` with server filename + fallback.

**List** (`eventsPage.test.tsx`): skeleton; empty vs no-matches; error+retry; rows render range/status
badge/expense count; status filter drives the URL + refetch; row link to detail; en-US chrome.

**Detail** (`eventDetailPage.test.tsx`): renders info + balance + expenses; ownership `9000` → shared
NotFound (no leak); non-404 → retry; **open** event shows Edit/Delete/Close enabled; **closed** event
disables Edit/Delete/Close, shows the closed `Alert`, keeps Export enabled and the expenses section
read-only (no assign/remove). Balance for both open and closed (OQ8a).

**Balance table** (`eventBalanceTable.test.tsx`): renders advanced/owed/balance via `Money` (vi-VN);
owner-rep marker + "(đã xóa)"; sign-labelled balance (color-independent); footer sums to zero; empty
`rows` → empty note.

**Create/edit** (`eventFormDialog.test.tsx`): create success → POST body with noon-anchored dates +
toast + close; `13001` → `LimitNotice` (form stays open); edit pre-fills + PUT; `9003` (range excludes
assigned) → form-level message keeps open; `9001` (edit closed) → toast+close; `1001` → field mapping;
`endDate < startDate` blocked client-side.

**Close** (`closeEventDialog.test.tsx`): irreversible copy present; success → toast + status flips
(refetch); terminal `9001` (already closed) → toast+close; transient 500/network → stays open with
inline error (OQ7a).

**Delete** (`deleteEventDialog.test.tsx`): body states expenses become loose; success → toast + navigate
to `/events`; terminal `9000`/`9001` close; transient stays open (OQ7a).

**Assign/remove** (`eventExpensesSection.test.tsx`, `assignExpenseDialog.test.tsx`): assign a loose
in-range expense → PUT `/expenses/:uuid/event` body `{ eventUuid }` + refetch counts/balance; `9002` →
inline out-of-range; remove-from-event → DELETE + refetch; closed event hides assign/remove.

**Expense-side** (extend `expensesPage.test.tsx` + a new `expenseEventControl.test.tsx`): the event
`Select` filter drives the `?event=` URL + `eventUuid` query; `ExpenseEventControl` assign/move (open
events only) + remove; `9001` on a closed source → toast; `9002` → inline.

**i18n** (`eventsI18n.test.ts`): vi-VN↔en-US key-shape parity for `events` + the new `validation.event.*`
and `expenses.filter.event*` keys; no empty leaves; fixed domain terms.

## Impact Analysis

- **APIs/Database/Services:** none — consumes existing stable `api/v1/events` routes (+ balance/export/
  close) and the expense-side assign/remove routes.
- **Frontend:**
  - New feature tree `src/features/events/**` (api, hooks, pages, components, schemas, dateRange, tests).
  - **[M4-MOD]** `features/expenses/`: `api/types.ts` (+`eventUuid`, `AssignEventRequest`),
    `api/expensesApi.ts` (+assign/remove, +`eventUuid` in filterQuery), `hooks/useExpenses.ts`
    (+assign/remove mutations), `components/ExpenseFilterBar.tsx` (+event `Select`),
    `pages/ExpensesPage.tsx` (+`event` URL param), `pages/ExpenseDetailPage.tsx` (+`ExpenseEventControl`),
    new `components/ExpenseEventControl.tsx`.
  - `routes/router.tsx` — `/events`, `/events/:uuid` replace the stub.
  - `i18n/index.ts` + `useT.ts` + new `events.json` (both locales) + `validation.json` + `expenses.json`
    additions.
  - Reuse `src/lib/download/downloadBlob.ts` (M4). **No** new design-system primitive expected (balance
    table + picker use existing `Table`/`Dialog`/`Select`/`Money`/`Badge`); flag if the balance footer or
    picker reveals a genuine primitive gap.
  - **No `src/lib/api/errors.ts` change** (9xxx + 13001 already present).
- **Design system:** a **modest ui-designer pass** — net-new surfaces: the **debt-balance table**
  (advanced/owed/balance semantics, sign-labelled + color-independent balance, sum-to-zero footer,
  owner-rep/deleted markers), the **one-way-close confirm treatment** (emphatic irreversible dialog) +
  the **event status badge**, and the **assign-expense picker** dialog. Everything else (list, detail
  layout, event form dialog, delete dialog, expenses section) reuses M2–M4 primitives/patterns.
- **Documentation:** this planning doc; the roadmap M5 row already present.
- **Downstream:** M6 (Stats) reuses `GET /events/{uuid}/balance` for the per-event lens and the events
  list for the event filter; M7 (Wallet/QR) adds the Premium closed-only per-event **QR** button on this
  event detail (reuses `downloadBlob` + the `12002 EventNotClosedForQr` semantics).

## Decision Log

### Decision

Adopt the plan above: event detail as a route; create/edit as a shared dialog; assign/remove on **both**
the event detail (picker + per-row remove) and the expense detail (`ExpenseEventControl`); a table-only
debt-balance view (sum-to-zero footer, shown for open + closed); noon-anchored date-range submission; a
strong irreversible close confirm; the M4 close-on-error behavior carried forward; and completion of the
M4 event-filter deferral on the expenses list.

### Reason

The backend contract is fully locked (one-way close, closed-event immutability with the settled flag as
the sole per-share exception, delete → loose expenses, within-range assign, balance summing to zero for
open + closed), so the UI's job is faithful mirroring + clear, code-branched messaging. Reusing the M4
primitives and the expense-list/detail seams keeps M5 lean; the only genuinely new visual surface is the
balance table, which warrants a modest design pass. Noon-anchoring the submitted date defends the
calendar-day intent against any timezone-boundary drift at zero cost.

### Alternatives Considered

- Detail as a modal (OQ1b) — rejected; too cramped for balance + expenses.
- Create/edit as pages (OQ2b) — rejected; the form is small, a dialog matches M2/M3.
- Assign/remove on one surface only (OQ3b/c) — rejected; both entry points are in the roadmap and share a
  hook pair.
- Midnight/bare-date range submission (OQ5b/c) — rejected in favor of noon-anchoring for boundary safety.
- A balance bar now (OQ6b) — deferred to M6's dataviz layer.

## Progress Log

### 2026-07-17

- Feature-planner drafted this M5 plan. Required reading completed: `planning/feature-roadmap.md` (M5
  scope + locked roadmap), `FairShareMonWeb/CLAUDE.md` (locked conventions), the backend
  `EventsController.cs` (6 routes + balance + export + qr + close), the `ExpensesController.cs`
  assign/remove-event routes, `Models/Events/**`, `Models/Expenses/AssignEventRequest.cs`,
  `Models/Stats/{EventBalanceResponse,MemberBalanceRow}.cs`, `FairShareMonApi/planning/events.md`
  (16 backend OQs + closed-event/timezone/balance semantics), and the shipped SPA (`api` client +
  `api.blob` + `downloadBlob`, `errors.ts` [9xxx + 13001 already present] + `http-error-handling.ts`,
  `router.tsx`, i18n setup, the M2–M4 features incl. `ExpenseFilter`/`ExpenseFilterBar`/`ExpensesPage`/
  `ExpenseDetailPage` seams and the `eventUuid`/`eventIsClosed` DTO fields).
- Mapped every M5 surface to concrete routes/files/components/hooks/endpoints/schema/i18n keys/tests;
  identified the modest ui-designer surfaces (balance table, close-confirm + status badge, assign
  picker); recorded 10 Open Questions each with a firm recommendation (none CRITICAL; OQ5 flagged for
  timezone care). No `errors.ts` change needed.
- Awaiting the checkpoint (orchestrator auto-accepts recommendations) before implementation.

- **ui-designer modest pass (net-new M5 surfaces).** Delivered the four net-new
  design surfaces as reusable primitive additions + a living-styleguide spec; the
  rest reuses M2–M4 primitives unchanged. `tsc -b`, `pnpm lint`, `pnpm build` all
  clean. Added:
  - **Table summary/total row** — new `<TableFoot>` + a `total` variant on
    `<TableRow>` (`src/components/ui/Table/Table.tsx` + `.module.css`, exported
    from the ui barrel) so the debt-balance table gets its sum-to-zero footer on
    the existing `Table` family (not a new table).
  - **Irreversible-confirm treatment** — a `tone` prop on `DialogContent`
    (`default` | `danger`) that adds a danger top accent + a warning-triangle
    severity glyph (`src/components/ui/Dialog/Dialog.tsx` + `.module.css`;
    `DialogTone` exported). Distinguishes the one-way close from ordinary delete
    confirms (which stay `default`).
  - **M5 showcase** — `src/styles/M5Showcase.tsx` (+ `.module.css`), mounted in
    `StyleGuide.tsx`: reviewable specs for (1) the debt-balance table
    (đã ứng / phải gánh / cân bằng, owner-rep + "(đã xóa)" markers,
    color-independent signed balance via `Money variant="balance"` + a polarity
    word, sum-to-zero `TableFoot`, plus the empty state), (2) the one-way-close
    danger confirm (danger Dialog + warning `Alert` + a deliberate acknowledgment
    checkbox gating the danger button), (3) the open/closed event status `Badge`
    (icon + text), and (4) the assign-expense picker (searchable native-radio
    single-select list + loading/empty states).
  - Documented all of the above in `src/styles/README.md` ("Event & balance
    patterns (M5)").
  - **Contracts for the implementer** (wire data/i18n/hooks; do not fork styles):
    - *Balance table*: `<Table>` → head [Thành viên | Đã ứng (numeric) | Phải
      gánh (numeric) | Cân bằng (numeric)]; body rows use `<TableRow deleted>` +
      `<TableHeaderCell scope="row">` for the member, `<Money>` numeric cells, and
      the balance cell = `<Money variant="balance">` + a polarity label; footer =
      `<TableFoot><TableRow total>` with the API column sums (verbatim, balance =
      0). Empty `rows` → `<TableEmpty colSpan={4}>` + `<EmptyState>`.
    - *Close confirm*: `<DialogContent tone="danger" size="sm">` + warning
      `Alert` + an acknowledgment checkbox controlling the danger button's
      `disabled`; wire the confirm to `useCloseEvent` with OQ7a close-on-error.
    - *Status badge*: `Badge tone="success"` (open) / `Badge tone="neutral"
      icon={<Lock/>}` (closed) — the implementer's `EventStatusBadge` wraps this.
    - *Assign picker*: `<DialogContent size="md">` + a `TextField` search +
      `<fieldset>`/radio list of `useExpensesQuery({ looseOnly, from, to })`
      results; single-select drives the primary button's `disabled`.
  - No new dependency; no `errors.ts` change; no feature data/routing/i18n added
    (implementer's). No new Open Questions — all OQs accepted at recommended
    options and no primitive gap surfaced beyond the two small additive changes
    above.

### 2026-07-17 (web-implementer)

- **OQ1–OQ10 resolved at option a** (auto-accepted); the close-confirm's mandatory
  acknowledgment checkbox kept. Implemented M5 end-to-end per the plan.
- **New feature tree `src/features/events/`:** `api/types.ts` (+ `eventsApi.ts`),
  `hooks/useEvents.ts` (query-key factory + `useEventsQuery`/`useEventQuery`/
  `useEventBalanceQuery` + create/update/delete/close/export mutations with
  cross-cache invalidation into `expensesKeys.all`), `schemas.ts`
  (`eventFormSchema`, name≤200 / desc≤1000 / `endDate >= startDate`), `dateRange.ts`
  (noon-anchored `dateInputToIso`, `isoToDateInput`, `formatRange`), `components/`
  (`EventStatusBadge`, `EventsTable`, `EventFormDialog`, `DeleteEventDialog`,
  `CloseEventDialog` [danger tone + ack checkbox], `EventBalanceTable`,
  `EventExpensesSection`, `AssignExpenseDialog`, `icons`), `pages/`
  (`EventsPage` [list + `?status=` filter], `EventDetailPage` [header + one-way
  close + balance + expenses + edit/delete/export]).
- **[M4-MOD]** expenses: `api/types.ts` (+`eventUuid` on `ExpenseFilter`,
  +`AssignEventRequest`), `api/expensesApi.ts` (+`assignEvent`/`removeEvent`,
  +`eventUuid` in `filterQuery`), `hooks/useExpenses.ts`
  (+`useAssignExpenseEvent`/`useRemoveExpenseEvent`, invalidate expenses + events),
  `components/ExpenseFilterBar.tsx` (+event `Select`, mutually exclusive with
  loose-only), `pages/ExpensesPage.tsx` (+`?event=` URL param → `eventUuid`),
  new `components/ExpenseEventControl.tsx` wired into `ExpenseDetailPage.tsx`
  (replaces the read-only event row).
- **Wiring:** `routes/router.tsx` (`/events` index + `/events/:uuid` replace the
  stub); `i18n/index.ts` + `useT.ts` (registered the `events` namespace); new
  `locales/{vi-VN,en-US}/events.json`; `validation.json` `event.*` keys;
  `expenses.json` `filter.event`/`filter.eventAll` + the `expenseEvent.*` block —
  all vi-VN authoritative + en-US parity. No `errors.ts` change; no new deps.
- **MSW handlers** (`src/test/msw/handlers.ts`): full events CRUD + close +
  balance + CSV export + the expense-side assign/remove routes + `eventUuid`
  filter + `eventName`/`eventIsClosed` linkage on expense responses (13001
  open-event limit + 9000/9001/9002/9003 all demonstrable). No product test suite
  added (web-test-engineer's).
- **Balance-footer clarification (implementation note, not a scope change):** the
  `EventBalanceResponse` DTO carries only `rows` (no totals field). The sum-to-zero
  `TableFoot` sums the server-provided whole-VND row values for advanced/owed via
  exact integer addition (no fractional float math — R3), and renders the balance
  total as the documented sum-to-zero invariant `0` (never client-summed, per the
  plan's "never client-sum the balance").
- **Quality bar:** `tsc -b` clean; `pnpm lint` clean (only pre-existing
  fast-refresh warnings); `pnpm build` succeeds.
- **Verification (LIVE backend on :5200, via the real endpoints the UI consumes):**
  create event (noon-anchored `2026-07-12`/`-18` → backend normalized to
  `00:00:00`/`23:59:59.9999990` at +07:00, correct calendar days, no ±1 drift);
  list all/open/closed; assign in-range OK + out-of-range → **9002**;
  `expenses?eventUuid=` returns event linkage; balance rows (rep + member,
  advanced/owed/balance, **sum = 0**); update range excluding an assigned expense
  → **9003**; CSV export (open + closed) returns `text/csv` + `Content-Disposition`
  filename; unsupported format → 400; remove/re-assign; **one-way close** OK,
  re-close → **9001**, closed detail shows `closedAt`, the expense then reads
  `eventIsClosed=true` (M4 guard lights up); edit/delete/remove on a closed event
  → **9001**; delete open event → its expenses become loose (`eventUuid=null`);
  unknown event → **9000/404**. The Vite dev server boots and serves `/events`.
- **Not exercised live:** the rendered React UI in a browser — no browser-automation
  driver (chromium-cli / Playwright) was available in this environment. The
  production build + `tsc` confirm the UI compiles/wires; the MSW handlers mirror
  the same contract for the browser-mock path (`VITE_ENABLE_MOCKS=true`) and unit
  tests.

### 2026-07-17 (web-test-engineer)

- **M5 test suite added (Vitest + RTL, MSW at the client boundary; pinned TZ
  `Asia/Ho_Chi_Minh` + vi-VN locale, per-test store/session isolation).** 13 new
  files, +90 tests. Full suite **406 → 496 passing**, green on two consecutive
  `pnpm test` runs; `pnpm lint` clean (only pre-existing fast-refresh warnings);
  `tsc -b` clean. No product code changed. Coverage by area:
  - **Schemas** (`schemas.test.ts`): name required/whitespace/at-max/over-max;
    description over-max; start/end required; `endDate >= startDate` (equal ok,
    before → error attached to `endDate`); exact vi-VN messages via the live
    catalog.
  - **Date helper** (`dateRange.test.ts`): `dateInputToIso` noon-anchored
    (12:00 local → `…T05:00:00.000Z` at +07), empty passthrough; `isoToDateInput`
    for offset-aware start/end bounds + invalid input; **round-trip with NO ±1-day
    drift** across month/year/leap-day edges; `formatRange` composition/order.
  - **Hooks** (`useEvents.test.tsx`): `eventsKeys` factory shapes/root-prefixing;
    list sends only a defined `?closed=`; update/close/delete invalidate BOTH the
    `["events"]` root (list+detail+balance) AND `["expenses"]`; create invalidates
    events-only (not expenses); `useExportEvent` → `downloadBlob` with server
    filename + fallback (mocked).
  - **List** (`eventsPage.test.tsx`): skeleton; empty vs no-matches; error+retry;
    row range/status-badge/count; closed badge; name→detail link; status filter
    drives `?status=` URL + `?closed=` refetch (both directions) + clear; en-US
    chrome.
  - **Detail** (`eventDetailPage.test.tsx`): header+info+balance+expenses render;
    ownership `9000` → shared NotFound (no leak); non-404 → retry; open event
    enables Edit/Close/Delete/Export/Assign; closed event hides write controls,
    shows the closed Alert, keeps Export, renders the expenses section read-only,
    and still renders the balance (OQ8a).
  - **Balance table** (`eventBalanceTable.test.tsx`): advanced/owed/balance via
    `Money` (vi-VN grouping, verbatim); owner-rep + `(đã xóa)` markers;
    color-independent signed balance polarity words; **`TableFoot` sum-to-zero**
    (advanced total == owed total, balance total 0 = "đã cân bằng"); empty rows →
    empty note + no footer; load error → retry.
  - **Create/edit** (`eventFormDialog.test.tsx`): create → POST body with
    **noon-anchored** dates (calendar day preserved) + trimmed name + toast +
    close + `onCreated`; `13001` → `LimitNotice` (form stays open); client-side
    `endDate < startDate` blocked (no POST); `1001` → field mapping; edit pre-fills
    + PUT; `9003` → form-level message (stays open); `9001` → toast + close.
  - **Close** (`closeEventDialog.test.tsx`): irreversible copy present; the
    mandatory ack checkbox gates the danger button; success → toast + close;
    terminal `9001` → toast + close; transient 500 → stays open with inline error
    (OQ7a).
  - **Delete** (`deleteEventDialog.test.tsx`): loose-expense copy; success → toast
    + navigate `/events`; terminal `9000`/`9001` close; transient stays open
    (OQ7a).
  - **Assign/remove** (`assignExpenseDialog.test.tsx`,
    `eventExpensesSection.test.tsx`): picker offers loose in-range single-select,
    seeds the query with `looseOnly`+`from`/`to`, assign → PUT `{ eventUuid }` +
    close, `9002` → inline, empty state; section lists in-event expenses with
    per-row remove (DELETE) + toast, assign trigger opens the picker, closed event
    hides assign/remove and shows the read-only note.
  - **Expense-side + M4 seam** (`expenseEventControl.test.tsx`, extended
    `expensesPage.test.tsx`): `ExpenseEventControl` assign (open-events Select) →
    PUT + toast, `9002` inline, `9001` toast, move/remove for an assigned expense,
    closed owning event → read-only; the `ExpenseFilterBar` event `Select` drives
    `?event=` URL + `eventUuid` refetch and enforces event/loose mutual
    exclusivity both ways.
  - **i18n** (`eventsI18n.test.ts`): vi-VN↔en-US key-shape parity for `events`,
    `validation.event.*`, and the new `expenses.filter.event*` /
    `expenses.expenseEvent.*` keys; no empty leaves; fixed domain terms.
- **Extra edge cases beyond the plan's checklist:** `dateInputToIso` empty-string
  passthrough + `isoToDateInput` invalid-input guard; create-dialog `onCreated`
  navigation callback assertion; the ack-checkbox enable/disable transition as its
  own case; both directions of the event/loose mutual-exclusivity seam; and the
  balance load-error retry state.
- **No product bugs found.** The two flagged risk areas held: the balance
  `TableFoot` advanced/owed column sums are equal and the balance total is the
  documented sum-to-zero `0`; and the noon-anchor date round-trip shows no ±1-day
  drift under the pinned +07 timezone (including month/year/leap-day boundaries).
- **Harness note:** the pending-state skeleton `Table` shares the loaded table's
  hidden caption, so `findByRole("table", { name })` can resolve to the skeleton
  node just before it is replaced (then detaches). Detail-page assertions target
  the loaded member `rowheader` (empty in the skeleton) instead — a test-side
  pattern, not a product issue.

## Final Outcome

**Complete.** M5 shipped the Events feature (`src/features/events/`): list (`/events`) with an open/closed URL filter; detail (`/events/:uuid`) with the header + one-way **close** (danger dialog + mandatory ack checkbox), the **debt-balance table** (advanced/owed via `Money`, `TableFoot` sum-to-zero total by exact integer addition + the `0` balance invariant, owner-rep/deleted markers, shown for open AND closed), the event's expenses section, and CSV export (open+closed). Create/edit via a shared `EventFormDialog` (open-only; Zod endDate≥startDate; `13001` limit), `DeleteEventDialog` (expenses→loose), assign/remove expenses on both the event detail (loose+in-range picker, `9002`) and the expense detail (`ExpenseEventControl`), with `9003` range-excludes-assigned handled. Noon-anchored ISO dates (no ±1 drift). Closed the M4 OQ7 deferral: added the event `Select` filter + `?event=` param to the expenses list (mutually exclusive with loose-only). Mutations cross-invalidate `["events"]`+`["expenses"]` so the M4 `eventIsClosed` guard lights up. Design-system additions: `TableFoot`/`TableRow total`, `Dialog tone="danger"`. No new deps, no `errors.ts` change. Verified live on :5200 (drift-free dates, `9001/9002/9003`, sum-to-zero, one-way close, delete→loose, CSV). Tests +90 (suite 406→496); code review **APPROVE, 0 blocking**. Review nit fixed pre-close: corrected a misleading comment in `ExpenseEventControl`. All 10 OQs shipped at option (a).

## Future Improvements

- **Balance bar** (OQ6b) — a light diverging advanced/owed bar on the event detail once the M6 dataviz
  layer + `--fs-viz-*` palette land.
- **Move-from-picker** (OQ4b) — allow the event-detail assign picker to move expenses already in another
  open event (with per-row source-closed handling) if the "add" vs "move" distinction proves confusing.
- **Per-row remove pending state** (M5 review nit) — `EventExpensesSection` disables every row's remove while any one removal is in flight; scope the pending state per row.
- **i18n CSV fallback filename** (M5 review nit) — the `${name || "event"}.csv` fallback embeds a non-i18n literal (only used if the server omits `Content-Disposition`); route through i18n for full copy-through-i18n compliance (also in M4's expense export).
- **Optimistic updates** for assign/remove + close once the write patterns settle (mirrors M4 OQ14b).
- **Bulk assign** — multi-select on the expense list to assign several expenses to an event at once.
- **Proactive open-event-limit affordance** if the tier limit value is ever exposed (OQ9b).
- **Branded date-range picker** replacing the two native `<input type="date">` controls.
- **Retrofit the M2/M3 delete dialogs** to the close-on-error behavior (cross-feature consistency; also
  tracked in M4 Future Improvements).
- E2E (Playwright) coverage of the full ledger loop (add member → add expense → assign to event → close →
  view balance → export) once a browser driver is available.
