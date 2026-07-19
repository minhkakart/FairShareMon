# Dashboard "Recent events" card

A new card on the home dashboard listing the caller's recent events — event name,
date range, total advanced, and open/closed status — ordered open-first then
closed, most-recently-updated within each group, top N with a "view all" link.

## Objective

Add a `RecentEventsCard` to `DashboardPage` that surfaces the caller's most
relevant events at a glance. Each row shows:

- **Event name** (links to `/events/:uuid`).
- **Time** — the event date **range** (`startDate` – `endDate`).
- **Total advanced** — event-level VND sum of all expense amounts (`totalAdvanced`).
- **Status** — open/closed badge (`EventStatusBadge`).

Ordering is computed **client-side** after fetching the full events list: OPEN
events first, then CLOSED; within each group by `updatedAt` DESC; sliced to the
top N (default 5). A header "view all" link routes to `/events`.

## Background

- The dashboard home (M6, see `planning/m6-stats-dashboard.md`) is composed in
  `src/features/dashboard/pages/DashboardPage.tsx`: a `PageHeader`, a
  `DashboardOverview` KPI row, a two-column `.homeGrid`
  (`DashboardCategoryBreakdown` + `RecentActivityCard`), then a role-filtered
  quick-links section. Layout styles live in
  `src/features/dashboard/components/dashboard.module.css`.
- The closest structural template is
  `src/features/dashboard/components/RecentActivityCard.tsx`: a `<Card>` with a
  `.cardHeadRow` header (title + `.viewAll` `Link`), then an
  error / pending-skeleton / empty / list branch, with `.recentRow` `Link` rows
  built from a `.recentMain` block plus a right-aligned value. This new card
  mirrors that structure exactly, so it inherits the existing responsive and
  a11y behavior for free (the CSS is already tokens-only and collapses in the
  `.homeGrid`).
- Events data comes from `useEventsQuery(filter)` in
  `src/features/events/hooks/useEvents.ts`, backed by
  `eventsApi.list` → `GET /v1/events` returning `EventSummaryResponse[]`. Passing
  `{}` (no `closed`) returns **all** events (open + closed).
- Reusable presentational pieces already exist:
  - `EventStatusBadge` (`src/features/events/components/EventStatusBadge.tsx`) —
    color-independent open/closed badge (icon + text).
  - `formatRange(startIso, endIso)` (`src/features/events/dateRange.ts`) — the
    localized "start – end" string, viewer-timezone aware.
  - `<Money amount={number} />` (`@/components/ui`) / `formatMoneyVnd`
    (`src/i18n/format.ts`) for VND — render the API value, never re-derive.

### API dependency (in progress in parallel)

The backend is being extended to add two fields to `EventSummaryResponse`
(`FairShareMonApi/.../Models/Events/EventSummaryResponse.cs`), which currently
exposes only `uuid, name, startDate, endDate, isClosed, closedAt, expenseCount,
createdAt`:

- `totalAdvanced: number` — event-level total advanced (sum of all expense
  amounts in the event), VND.
- `updatedAt: string` — ISO-8601 last-updated timestamp. The `Event` entity
  already carries `UpdatedAt` (`ValueGeneratedOnAddOrUpdate`, per
  `FairShareMonApi/planning/events.md`); this exposes it on the summary DTO.

This plan **assumes those two fields ship** on `EventSummaryResponse`. The FE
type `EventSummaryResponse` in `src/features/events/api/types.ts` must be updated
to add them. **If the delivered field names/types differ, adjust the type, the
sort key, and the `<Money>` binding at implementation time** (see Open Questions
OQ-A / OQ-B for the fallback if either field is late).

## Requirements

- R1 — New card component `RecentEventsCard` under
  `src/features/dashboard/components/`, styled by reusing
  `dashboard.module.css` (same classes as `RecentActivityCard`); no parallel
  style system.
- R2 — Data via `useEventsQuery({})` (fetch all, no filter). No new API surface,
  no new hook.
- R3 — Client-side ordering: partition into open (`!isClosed`) and closed
  (`isClosed`); within each partition sort by `updatedAt` DESC; concatenate
  open-then-closed; slice to `RECENT_N` (default 5).
- R4 — Each row: name, `formatRange(startDate, endDate)`, `<Money amount={totalAdvanced} />`,
  `<EventStatusBadge isClosed={...} />`. Row is a `Link` to `/events/${uuid}`.
- R5 — Header: title + a `.viewAll` `Link` to `/events`.
- R6 — Four states mirroring `RecentActivityCard`: error (with retry), pending
  (skeleton rows), empty (no events), populated list.
- R7 — Money rendered from the API value via `<Money>`/`formatMoneyVnd`; never
  float math. Dates via the shared `formatRange`/`formatDate` (viewer timezone).
- R8 — All copy through i18n (`useT`); add keys to the `common` `home`
  namespace in **both** `vi-VN` and `en-US` with full parity.
- R9 — Ownership 404 / existence rules: N/A for this read (the list only ever
  returns the caller's own events), but row links land on `/events/:uuid` which
  already applies the shared NotFound handling for `1003`/`9000`.
- R10 — Closed events: read-only display only (no write controls on the card),
  so the immutability rule needs no special handling here beyond the status
  badge.
- R11 — Responsive: inherit `.homeGrid` collapse + `.recentRow` grid behavior;
  verify at phone widths (long Vietnamese event names wrap via
  `overflow-wrap: anywhere`; the money value stays right-aligned and does not
  clip). Adds a 4th datum (range) not present in `RecentActivityCard`; confirm
  the row still reads cleanly at ~360px (see OQ-C on row density).
- R12 — Accessibility: rows are keyboard-focusable `Link`s with visible focus
  (inherited `.recentRow:focus-visible`); status conveyed by badge icon+text,
  not color; money/date are plain text; the "view all" link has a descriptive
  accessible name.

## Open Questions

- **OQ-A — Layout placement (UI/UX decision, needs a call).** Where does the
  card go on `DashboardPage`?
  - *Option 1 (recommended):* Add a **new full-width row** below the existing
    `.homeGrid` (between the grid and the quick-access section). Cleanest: the
    card carries 4 data points per row (name, range, money, status) and reads
    best at full width; it does not compete with the category chart / recent
    expenses for the narrow grid column. Requires no change to the existing
    two-column balance.
  - *Option 2:* Make the home grid a **three-column / two-row** region and drop
    the card as a third grid cell. Denser, but the card is cramped in a
    `1fr`-ish column with 4 columns of row data, and `.homeGrid` is currently a
    fixed two-column template — would need CSS changes and a fresh responsive
    pass.
  - *Option 3:* Replace/merge with `RecentActivityCard` (a tabbed
    "expenses / events" card). Most compact but the biggest scope + a11y change;
    likely out of scope for this feature.
  - Trade-off: Option 1 is lowest-risk and best for the row shape; Options 2/3
    save vertical space at higher CSS/UX cost. **Recommend Option 1.**
- **OQ-B — Behavior if `totalAdvanced` is not yet in the API at build time.**
  Options: (1) block this feature until the field ships; (2) render the column
  conditionally / show a dash when absent. Recommend gating on the field
  shipping (the whole point of the card includes the money value), but confirm.
- **OQ-C — Row density with 4 data points on phone.** The sibling
  `RecentActivityCard` row is `1fr auto` (main block + amount). For events we
  need name + range + money + status. Options: (1) main block = name (line 1) +
  range (line 2 meta) with a right column stacking status over money; (2) status
  badge inline in the meta line, money right-aligned. Recommend Option 2
  (money right-aligned like the sibling card; status badge in the `.recentMeta`
  line next to the range) — but this is a visual call the ui-designer may want
  to weigh in on.
- **OQ-D — Top N value.** Task suggests 5 (matches `RecentActivityCard`'s
  `RECENT_N = 5`). Confirm 5, or a different count.
- **OQ-E — Tie-break / missing `updatedAt`.** When two events share an
  `updatedAt`, or if `updatedAt` is momentarily absent (API lag), what is the
  secondary sort? Recommend fallback to `createdAt` DESC, then `startDate` DESC.
  Confirm.

## Assumptions

- A1 — `EventSummaryResponse` will gain `totalAdvanced: number` and
  `updatedAt: string` (ISO-8601), matching the fields already used elsewhere in
  the FE type conventions. If names differ, adjust.
- A2 — `useEventsQuery({})` returns all events (open + closed); the existing
  backend order (`startDate` DESC then `createdAt` DESC) is irrelevant because
  we re-sort client-side.
- A3 — The dashboard uses the `common` namespace under the `home` key (verified:
  `recentActivity`, `viewAll`, `recentExpensesEmpty` all live there). New keys
  go alongside them.
- A4 — Reusing `stats:states.loadError` / `stats:states.retry` for the error
  branch is acceptable (that is what `RecentActivityCard` and
  `DashboardCategoryBreakdown` already do); no need for events-specific error
  copy on the dashboard.
- A5 — Client-side sort of the full events list is acceptable performance-wise
  (a single user's event count is small; the list is already fully fetched).

## Implementation Plan

1. **Update the FE type** — in `src/features/events/api/types.ts`, add to
   `EventSummaryResponse`:
   ```ts
   /** Event-level total advanced (sum of expense amounts), VND. Rendered verbatim (R3). */
   totalAdvanced: number;
   /** ISO-8601 last-updated timestamp (sort key for the dashboard card). */
   updatedAt: string;
   ```
   Leave `EventResponse` untouched unless the backend also adds them there (not
   required by this feature). If the API omits either field at build time, see
   OQ-A/OQ-B before proceeding.

2. **Create the sort helper** (pure, unit-testable) — add
   `sortEventsForDashboard(events: EventSummaryResponse[]): EventSummaryResponse[]`
   to a small module, e.g. `src/features/dashboard/eventOrdering.ts` (feature-local
   so it can be unit-tested without React):
   - Partition by `isClosed`.
   - Sort each partition by `Date.parse(updatedAt)` DESC, tie-break `createdAt`
     DESC (per OQ-E once confirmed).
   - Return `[...open, ...closed]`.
   Do not slice here; the component slices to `RECENT_N`.

3. **Create the component** — `src/features/dashboard/components/RecentEventsCard.tsx`,
   mirroring `RecentActivityCard.tsx`:
   - `const RECENT_N = 5;` `const NO_FILTER: EventFilter = {};`
   - `const eventsQuery = useEventsQuery(NO_FILTER);`
   - `const recent = sortEventsForDashboard(eventsQuery.data ?? []).slice(0, RECENT_N);`
   - `<Card>` + `.cardHeadRow` header: title `t("common:home.recentEvents")` +
     `.viewAll` `Link` to `/events` (`t("common:home.viewAll")`).
   - **Error branch** — `<ErrorState title={t("stats:states.loadError")}
     description={resolveErrorMessage(eventsQuery.error, t)}
     action={<Button variant="secondary" onClick={() => void eventsQuery.refetch()}>
     {t("stats:states.retry")}</Button>} />`.
   - **Pending branch** — `.recentList` with `RECENT_N` skeleton `.recentRow`s
     (two `Skeleton`s in `.recentMain`, one right-aligned).
   - **Empty branch** — `<EmptyState title={t("common:home.recentEventsEmpty")} />`.
   - **List branch** — `.recentList` of `.recentRow` `Link`s to
     `/events/${event.uuid}`:
     - `.recentMain`: `.recentName` = `event.name`; `.recentMeta` = `.recentDate`
       `formatRange(event.startDate, event.endDate)` + `<EventStatusBadge isClosed={event.isClosed} />`.
     - Right column: `<Money amount={event.totalAdvanced} className={styles.recentAmount} />`.
   - Import `styles from "./dashboard.module.css"`; reuse existing classes. No
     quick-actions row (that is specific to `RecentActivityCard`).
   - Follow the React 19 / React Compiler convention — no manual `useMemo` around
     the sort.

4. **Place the card in `DashboardPage.tsx`** — per OQ-A (recommended Option 1):
   add `<RecentEventsCard />` as a full-width block between `.homeGrid` and the
   `<section>` quick-access block:
   ```tsx
   <div className={styles.homeGrid}>
     <DashboardCategoryBreakdown />
     <RecentActivityCard />
   </div>

   <RecentEventsCard />

   <section> … quick access … </section>
   ```
   Import from `../components/RecentEventsCard`. (If the user picks Option 2/3,
   revise this step + `dashboard.module.css` grid accordingly.)

5. **CSS** — expected to need **no new CSS**: the card reuses `.cardHeadRow`,
   `.cardTitle`, `.viewAll`, `.recentList`, `.recentRow`, `.recentMain`,
   `.recentName`, `.recentMeta`, `.recentDate`, `.recentAmount` from
   `dashboard.module.css`. Only add a class if OQ-C's chosen layout requires it
   (e.g. a status/money stack in the right column) — keep additions
   tokens-only, feature-local.

6. **i18n** — add to the `home` object in **both** locale files, matching the
   existing `recentActivity` / `recentExpensesEmpty` / `viewAll` neighbors:
   - `src/i18n/locales/vi-VN/common.json`:
     - `"recentEvents": "Đợt gần đây"`
     - `"recentEventsEmpty": "Chưa có đợt nào"`
   - `src/i18n/locales/en-US/common.json`:
     - `"recentEvents": "Recent events"`
     - `"recentEventsEmpty": "No events yet"`
   - Reuse existing `home.viewAll` and `stats:states.*`. Keep both locale files
     in strict key parity (the repo has an i18n parity test —
     `src/features/events/eventsI18n.test.ts` style — so any missing key in one
     locale fails CI).

7. **Verify namespaces/keys** — confirm `common:home.viewAll` and
   `stats:states.loadError`/`stats:states.retry` resolve (they already back
   `RecentActivityCard`); confirm `events:status.open`/`closed` resolve via
   `EventStatusBadge` (already used on the events pages).

## Impact Analysis

Affected areas:

- **UI** — `DashboardPage.tsx` (one new child + import); new component
  `RecentEventsCard.tsx`; new pure helper `eventOrdering.ts`. Possible minor CSS
  in `dashboard.module.css` (only if OQ-C requires it).
- **Types** — `src/features/events/api/types.ts` gains two fields on
  `EventSummaryResponse`. Low blast radius: additive, optional-to-consumers.
  Existing consumers (`EventsTable`, events pages) are unaffected (they ignore
  the new fields).
- **API** — no new endpoint; depends on the parallel backend change adding
  `totalAdvanced` + `updatedAt` to the existing `GET /v1/events`
  `EventSummaryResponse`. `ApiResult<T>` envelope + error codes handled by the
  centralized client as usual; the card branches only on
  `isError`/`isPending`/empty.
- **i18n** — 2 new keys × 2 locales in `common.json`.
- **Data-fetching** — reuses the `useEventsQuery({})` cache key
  `["events","list",{}]`. If no other screen fetches the unfiltered list, this
  is a fresh cache entry (one extra request on dashboard mount); if the events
  page already prefetches `{}` it is shared. Acceptable.
- **Tests** — new component test + a helper unit test (below). No changes to
  existing tests expected beyond the i18n parity guard picking up the new keys.
- **Infrastructure / Services / DB** — none (FE only).

## Tests (for the web-test-engineer)

Vitest + RTL, network mocked at the client boundary via MSW, TZ + locale pinned.

1. **`eventOrdering` unit test** — given a mixed fixture (open + closed with
   varied `updatedAt`/`createdAt`): asserts open events precede all closed;
   within each group order is `updatedAt` DESC; tie-break behavior (OQ-E).
2. **`RecentEventsCard` component tests:**
   - **List render** — MSW returns a mix; asserts the top-5 rows in the expected
     order, each showing name, `formatRange` output, formatted VND
     (`formatMoneyVnd`), and the correct status badge label
     (`events:status.open`/`closed`).
   - **Slice** — >5 events → exactly 5 rows rendered.
   - **Row link** — a row links to `/events/:uuid`; "view all" links to
     `/events`.
   - **Empty** — `[]` → `common:home.recentEventsEmpty` empty state.
   - **Pending** — skeleton rows before resolve.
   - **Error** — MSW error → error state with a working retry that refetches.
   - **i18n** — renders under vi-VN (default) and en-US; keys resolve in both.
3. **Interaction** — clicking a row / "view all" navigates to the expected route
   (router-level assertion via `renderWithProviders`).

## Decision Log

### Decision
Compute ordering client-side from a single unfiltered `useEventsQuery({})`.

### Reason
Matches the task directive and the established `RecentActivityCard` pattern
(fetch-all + slice). No backend sort/paging endpoint exists for this composite
order, and per-user event counts are small.

### Alternatives Considered
- A dedicated backend "recent events" endpoint — heavier, out of scope, and the
  data is already available in the list response.
- Two queries (`{closed:false}` + `{closed:true}`) then merge — more requests,
  no benefit over one unfiltered fetch + partition.

### Decision
Add the fields to `EventSummaryResponse` (not a dashboard-only DTO).

### Reason
The backend is extending the shared summary DTO; the FE mirrors backend DTOs
feature-locally. Keeps one source of truth for the events type.

## Progress Log

### 2026-07-19

- Read `DashboardPage`, `RecentActivityCard`, `DashboardCategoryBreakdown`,
  `dashboard.module.css`, events `types.ts` / `useEvents.ts` / `eventsApi.ts` /
  `EventStatusBadge` / `dateRange.ts` / `EventsTable`, `i18n/format.ts`, both
  `common.json` `home` sections, and the backend `EventSummaryResponse.cs`.
- Confirmed: dashboard uses `common:home.*`; `formatRange` + `EventStatusBadge`
  + `<Money>` are reusable; `useEventsQuery({})` returns all events; the backend
  DTO does not yet expose `totalAdvanced`/`updatedAt` (parallel backend work).
- Drafted this plan. Open Questions logged (placement, missing-field fallback,
  row density, top-N, tie-break). No code written.

- Open Questions resolved by the orchestrator: OQ-A = Option 1 (new full-width
  row between `.homeGrid` and the quick-access `<section>`); OQ-B = fields exist
  now, no gating; OQ-C = Option 2 (money right-aligned, status badge in the meta
  line next to the date range); OQ-D = top 5; OQ-E = `updatedAt` DESC, then
  `createdAt` DESC, then `startDate` DESC.
- Implemented the feature:
  - `EventSummaryResponse` (`src/features/events/api/types.ts`) gained
    `totalAdvanced: number` + `updatedAt: string`.
  - Added pure `sortEventsForDashboard` (`src/features/dashboard/eventOrdering.ts`):
    OPEN first then CLOSED, each group `updatedAt` DESC → `createdAt` DESC →
    `startDate` DESC; unparseable/absent timestamps sort last (never float up).
  - Added `RecentEventsCard` (`src/features/dashboard/components/RecentEventsCard.tsx`)
    mirroring `RecentActivityCard` (error / pending-skeleton / empty / list
    branches; `useEventsQuery({})` → sort → slice 5; `<Money>` right-aligned,
    `EventStatusBadge` + `formatRange` in the meta line). No new CSS — reuses
    `dashboard.module.css`.
  - Placed `<RecentEventsCard />` in `DashboardPage.tsx` as a full-width row
    between `.homeGrid` and the quick-access `<section>` (OQ-A Option 1).
  - i18n: `home.recentEvents` / `home.recentEventsEmpty` added to both
    `vi-VN` and `en-US` `common.json` (parity preserved).
  - MSW: `EventRecord` gained `updatedAt` (bumped on create/update/close);
    `eventSummaryResponse` now emits `totalAdvanced` (sum of assigned expense
    totals) + effective `updatedAt` (max of the event's own `updatedAt` and the
    latest assigned-expense `createdAt`). Existing typed fixture `makeSummary`
    in `eventsPage.test.tsx` extended with the two fields to stay type-valid.
- gitnexus impact on `EventSummaryResponse` (upstream): 26 impacted, risk MEDIUM
  by import fan-out (8 direct importers), but `processes_affected: 0` /
  `modules_affected: 0` and every edge is `IMPORTS` of the type — additive fields
  (consumers read a subset), so effectively LOW.
- Verified green: `pnpm lint` (only pre-existing `only-export-components`
  warnings), `tsc -b`, `pnpm build`. Drove the real app under the MSW dev-server
  (Playwright): confirmed the empty state, then created two events via the UI and
  observed both in the card ordered `updatedAt` DESC, each with name, localized
  range, "Đang mở" badge, and right-aligned VND ("0 đ"); zero console errors.

- Web-test-engineer (tests): added `src/features/dashboard/eventOrdering.test.ts`
  (9 pure-unit cases for `sortEventsForDashboard`: open-before-closed grouping,
  no-slice/empty, within-group `updatedAt` DESC, tie-breaks `createdAt` DESC then
  `startDate` DESC, unparseable/absent timestamps sink last — never float to top,
  and input-order stability for fully-equal keys) and
  `src/features/dashboard/recentEventsCard.test.tsx` (8 component cases against MSW:
  loading-skeleton branch, empty state, error + retry-refetch recovery, populated
  top-5 ordered list with name/localized-range/status-badge/right-aligned VND, row
  `Link`s to `/events/:uuid` in the sorted order, the 6th sliced off, cap-at-5, the
  "view all" → `/events` link, and vi-VN/en-US locale + badge parity). All 17 green.
  No MSW change needed here (the events handler already emitted `totalAdvanced` +
  effective `updatedAt`). Full suite `pnpm test` = 861 passed / 102 files; `tsc -b`
  + `pnpm lint` clean (pre-existing `only-export-components` warnings only). No
  product bugs found; `RecentEventsCard`'s `<Money>` uses the component's default
  vi-VN VND formatter (identical Intl config to `formatMoneyVnd`, correct grouping),
  matching the sibling `RecentActivityCard` — not flagged.

## Final Outcome

Shipped. The dashboard home now carries a full-width "Recent events" card
(`RecentEventsCard`) between the two-column region and the quick-access links.
It fetches the unfiltered events list (`useEventsQuery({})`), orders it via the
pure `sortEventsForDashboard` helper (open-first, then closed; `updatedAt` DESC
with `createdAt`/`startDate` tie-breaks), and renders the top 5 rows — each a
`Link` to `/events/:uuid` showing the event name, `formatRange(startDate,
endDate)`, an `EventStatusBadge`, and the event-level total advanced via `<Money>`
(rendered verbatim). Error / pending-skeleton / empty / list branches mirror
`RecentActivityCard`; no new CSS or API surface; two i18n keys added with locale
parity. The FE type and the MSW events mock were extended with `totalAdvanced` +
`updatedAt`. Vitest/RTL specs are left for the web-test-engineer per the plan.

Files added: `src/features/dashboard/eventOrdering.ts`,
`src/features/dashboard/components/RecentEventsCard.tsx`.
Files changed: `src/features/events/api/types.ts`,
`src/features/dashboard/pages/DashboardPage.tsx`,
`src/i18n/locales/vi-VN/common.json`, `src/i18n/locales/en-US/common.json`,
`src/test/msw/handlers.ts`, `src/features/events/eventsPage.test.tsx`.

## Future Improvements

- If per-user event counts grow large, add a lightweight backend "recent events"
  endpoint or server-side ordering to avoid fetching the whole list.
- Consider a shared "recent list card" abstraction if a third variant appears
  (expenses / events / …) to reduce duplication with `RecentActivityCard`.
- Show a subtle relative "updated X ago" hint if product wants recency to be
  visible, not just implied by order.
