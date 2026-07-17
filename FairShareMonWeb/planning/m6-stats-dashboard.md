# M6 — Stats & Home dashboard

## Objective

Deliver milestone **M6** of the FairShareMonWeb roadmap: the **statistics surface** (`/stats`, replacing
the stub) and the **data-rich home dashboard** (enriching the M1 `DashboardPage`). This is the frontend's
**first data-visualization milestone** — the first consumer of the reserved `--fs-viz-*` palette and the
`dataviz` skill.

Concretely:

- A **Stats page** with a date-range control driving two read endpoints: `GET /stats/overview` (KPI tiles:
  total spending + expense count) and `GET /stats/by-category` (a category breakdown **chart** — sorted
  total DESC, per-category color via `CategoryMarker` — plus an accessible **data-table alternative**).
- A **rich home dashboard** — overview KPI tiles (this-month range), a compact category breakdown, recent
  activity (recent expenses linking into M4/M5), and quick actions — the "data-rich home" OQ4 deferred to
  M6.

All on the locked foundation stack, honoring the LOCKED API-contract conventions and the §4 business
rules relevant to stats.

## Background

- **Roadmap position (`planning/feature-roadmap.md`, M6).** M6 follows M4 (expenses populate the numbers)
  and M3 (category colors), and depends on M5 for the event lens. It is marked **dataviz: yes** and
  **ui-designer: yes**. OQ4 (resolved 2026-07-17) placed the minimal home in M1 and the **rich** home
  here.
- **The backend is feature-complete and stable** (`StatsController.cs`, read 2026-07-17):
  - `GET api/v1/stats/overview` — `[FromQuery] StatsRangeRequest { from?, to? }` →
    `OverviewStatsResponse { from, to, totalSpending: decimal, expenseCount: int }`. Both bounds optional
    (omit = all-time), inclusive `[from,to]`, raw-UTC compare; `from > to` → **400 / `1001`** validation.
    Totals span the whole ledger (loose + event expenses). Empty range → zeros.
  - `GET api/v1/stats/by-category` — `[FromQuery] ByCategoryStatsRequest { from?, to?, eventUuid? }` →
    `ByCategoryStatsResponse { eventUuid?, from?, to?, rows: CategoryStatRow[] }`. **Time-range XOR
    event** — sending both → **400 / `1001`**. `eventUuid` miss → **404 / `9000`** (resource-owned).
    Each `CategoryStatRow { categoryUuid, categoryName, color (#RRGGBB), icon?, isDeleted, total: decimal,
    expenseCount }`. Only categories with ≥1 in-scope expense appear; **soft-deleted categories with
    history are included** (`isDeleted: true`, §4.7); **sorted total DESC → count DESC → name**. Empty
    scope → empty `rows`. Semantics are locked in `FairShareMonApi/planning/debt-balance-and-stats.md`
    (OQ6–OQ9, OQ13–OQ15).
  - The **per-event debt balance** (`GET /events/{uuid}/balance`) is already consumed in M5
    (`EventBalanceTable`) — M6 does **not** rebuild it.
- **The foundation + M1–M5 are shipped and reusable** (`CLAUDE.md`, `src/styles/README.md`, current
  `src/`):
  - Design-system primitives (`@/components/ui`): `Card`/`CardBody`, `PageHeader`/`Stack`,
    `DescriptionList`, `Money` (VND, `variant="balance"` signed), `Select` (Radix; empty-value forbidden —
    use an `"all"` sentinel), `Table` family (incl. `TableFoot`/`TableRow total`), `Badge`,
    **`CategoryMarker`** (color swatch + emoji + name; color never the sole signal — already earmarked
    "reusable by chart legends (M6)"), `Skeleton`, `EmptyState`, `ErrorState`, `Alert`, `Button`.
  - The **reserved, validated `--fs-viz-*` palette** (`tokens.css` lines ~188–220 light, ~385–414 dark):
    chart chrome (`--fs-viz-surface/grid/axis/ink/ink-muted`), **8-slot categorical** (`--fs-viz-cat-1..8`
    — assign in order, never cycled; a 9th series folds to "Other"), sequential (`--fs-viz-seq-*`),
    diverging (`--fs-viz-div-*`). **Light-mode relief rule:** cat slots 3/4/5 sit <3:1 on white — charts
    using them **must** ship direct value labels or a table view. **No chart components exist yet.**
  - Data layer: per-feature `api/` + `hooks/` on TanStack Query v5 + the centralized `api` client
    (`api.get<T>(path, { query })` — drops `undefined`/`null`, unwraps `ApiResult<T>`, injects
    `Authorization`/`X-Time-Zone`/`Accept-Language`, handles `401 → refresh`); `ApiError` carries the
    numeric `code`. Query params via `Record<string, QueryValue>`.
  - Datetime helpers: `dateBoundToIso(date, end)` (`src/features/expenses/dateTime.ts`) turns a date-only
    `YYYY-MM-DD` into an inclusive ISO bound in the viewer's local zone (the timezone-aware pattern to
    reuse); `formatMoneyVnd`/`formatDateTime` in `src/i18n/format.ts`.
  - i18n: feature namespaces registered in `src/i18n/index.ts` (currently `common, auth, errors,
    validation, settings, members, categories, tags, expenses, events`); typed `useT()`.
  - The M1 `DashboardPage` currently renders welcome + role-filtered quick-link cards (via `useNavEntries`)
    and does **no** data fetching — M6 enriches it.
  - `/stats` is a `StubPage` in `src/routes/router.tsx` (`path: "stats"`); the nav entry already exists
    (M1 `navConfig`).
- **§4 rules M6 must honor:** R1 privacy (ownership miss = 404, never leak — only relevant if the event
  lens is exposed); R3 money exact (render API `decimal` verbatim via `Money`, never float-math a money
  figure); §4.7 deleted categories still shown in historical stats (`isDeleted` → `(đã xóa)` treatment);
  a11y color-independence (labels + values, not color alone) + the light-mode relief rule.

## Requirements

- **Stats page (`/stats`)** replaces the stub:
  - A **date-range control** driving both queries with the **same** range.
  - **Overview KPI tiles** from `GET /stats/overview`: **total spending** (via `Money`) and **expense
    count**. Render only the two API values — do **not** compute an "average per expense" (that would be
    float math on money; R3).
  - **Category breakdown** from `GET /stats/by-category`: a chart (per-category `color` + `CategoryMarker`,
    rows in the API's DESC order rendered verbatim — no client re-sort) **and** an accessible data table.
  - Deleted categories shown with an `(đã xóa)` treatment (§4.7); zero-expense categories never appear
    (backend already omits them).
- **Home dashboard** enriches `DashboardPage`: overview KPI tiles for a default recent range, a compact
  category breakdown, a recent-activity list (recent expenses linking into detail), quick actions, and the
  existing role-filtered quick links. Reuses the stats hooks + M4 expense hooks.
- **Charts** are built with the `dataviz` skill on the `--fs-viz-*` palette (categorical slots in order),
  with **direct value labels** and a **table fallback** (honoring the relief rule), and are
  **color-independent** (name + value carry meaning, sign/marker never color alone).
- Every screen handles **loading / empty / error** states; **money** via `Money` (verbatim API values);
  **date ranges timezone-aware** (`X-Time-Zone` sent by the client; local-day bounds → ISO).
- **vi-VN-first** copy with **en-US** parity in a new `stats` namespace (+ home additions in `common`).
- **Error handling** on the `ApiResult<T>` envelope: branch on the numeric `code` (`1001` bad range,
  `9000` event miss, `13xxx` not applicable — reads impose no tier limit), never message text; render the
  backend's localized `error.message` verbatim where surfaced.
- **No new runtime dependency without an Open Question** (charting library is an OQ — see OQ1).

## Open Questions

> **Resolved 2026-07-17 — all at the recommended option (a).** OQ1a (hand-rolled SVG/CSS charts, no new
> dependency) · OQ2a (horizontal ranked bar) · OQ3a (time-range lens only) · OQ4a (preset chips + custom,
> default "This month") · OQ5a (charts feature-local in `features/stats/`) · OQ6a (recent-expenses card) ·
> OQ7a (bar fill uses the systematic `--fs-viz-cat-*` slot by rank; the `CategoryMarker` keeps the
> category's own color). Implemented as specified below.

> Each carries a one-line trade-off and a **Recommended** option. The orchestrator auto-accepts the
> recommended option; none is flagged CRITICAL (every one has a safe, reversible default — see OQ1 note).

### OQ1 — Chart rendering approach / new dependency

- **(a) Recommended — hand-rolled SVG/CSS charts on `--fs-viz-*`, NO new dependency.** M6's charts are a
  horizontal **bar** breakdown (+ optional donut) — simple marks that render cleanly as CSS/inline-SVG
  bars with direct labels. Keeps the foundation's locked, zero-chart-dep stack; full control of the
  relief rule, a11y names, and token theming; nothing to tree-shake or CVD-audit beyond the palette
  already validated. Trade-off: we own axis/scale/label math (small for a bar breakdown) instead of a
  library.
- (b) Add a charting library (e.g. Recharts/visx/Chart.js). Trade-off: faster complex charts, but a **new
  runtime dependency the foundation didn't approve** (CLAUDE.md: "Adding a dependency the foundation
  didn't approve is an Open Question"), bundle weight, its own theming/CVD/a11y reconciliation with
  `--fs-viz-*`, and React 19 + React Compiler compatibility risk. **A charting library is the only choice
  here that needs explicit user sign-off before adoption** — (a) requires none.

### OQ2 — By-category chart form

- **(a) Recommended — horizontal bar chart (ranked, one bar per category, longest first).** Matches the
  API's total-DESC ordering, scales to many categories, gives each bar room for a direct `CategoryMarker`
  + name + `Money` value label (satisfies the relief rule and color-independence), and is the most
  accessible/legible form. Trade-off: less "at-a-glance proportion" than a pie.
- (b) Donut/pie (proportion-first) with a legend. Trade-off: reads share-of-whole nicely, but the light
  relief rule bites hardest (cat slots 3/4/5 on white), needs a legend + direct labels anyway, and
  degrades past ~6 slices — more work for a first dataviz milestone. Could be added later as a toggle.

### OQ3 — Event-scoped by-category lens on the Stats page in M6

- **(a) Recommended — time-range only on the Stats page in M6; defer the event-scoped by-category chart.**
  The API supports `?eventUuid` (XOR range), but the per-event picture already lives on the M5 event
  detail (balance table + the event's expense list). Restricting M6's Stats page to the time-range lens
  keeps one clean scope, avoids the range↔event mode toggle + `9000`/`1001` handling, and keeps the
  by-category total reconcilable with `overview.totalSpending` (same range). Trade-off: the roadmap
  mentioned an event lens — deferring it to a future improvement (it is purely additive: a scope-mode
  toggle + an event `Select` reusing `useEventsQuery`).
- (b) Expose a scope-mode toggle (time-range XOR single event) on the by-category breakdown in M6.
  Trade-off: fuller feature now, but adds mutual-exclusion UX, the `9000` not-found + `1001` both-scopes
  branches, and an event picker — more surface in the first dataviz cycle.

### OQ4 — Date-range control UX + default range

- **(a) Recommended — preset chips (This month · Last 30 days · This year · All time) + a Custom range
  (two date inputs); default "This month".** Fast for the common cases, one custom escape hatch, a sane
  non-empty default so the page loads with data, and it maps directly onto the optional inclusive `from`/
  `to` (All time = both omitted). The home dashboard KPI tiles use the same **This month** default.
  Trade-off: presets are opinionated; power users lean on Custom.
- (b) Raw two date inputs only (no presets), default all-time. Trade-off: simplest to build, but every
  visit needs manual date entry and all-time can be a heavy first load.

### OQ5 — Chart component placement

- **(a) Recommended — feature-local in `src/features/stats/components/` for M6.** The roadmap's Future
  Improvement explicitly extracts a shared `src/components/ui/charts` module **only once both dashboard
  milestones (M6, M8) exist**, to avoid premature abstraction. Build the bar chart + KPI tile feature-
  local now; M8 (Admin) revisits extraction with two real consumers. Trade-off: a light duplication risk
  if M8 needs the exact same bar chart — accepted, and resolved by the planned M8 extraction.
- (b) Build the chart primitives straight into `src/components/ui/charts` now. Trade-off: reusable
  immediately, but abstracts from a single consumer (M6) before M8's needs are known — the anti-pattern
  the roadmap called out.

### OQ6 — Home "recent activity" composition

- **(a) Recommended — a "Recent expenses" card (top 5 from `GET /expenses`, `expenseTime` DESC verbatim,
  sliced client-side) linking each row into its expense detail, plus quick-action buttons (Add expense /
  New event) and the existing role-filtered quick links.** Reuses `useExpensesQuery({})`; expenses are the
  ledger's pulse and already carry payer/category/total for a rich row. Trade-off: two list slices (the
  expense list isn't server-paginated) — 5 rows is cheap.
- (b) Also add a "Recent events" card. Trade-off: more context, but doubles the home queries and the
  events list is already one click away via the compact category breakdown + nav.

## Assumptions

- No backend change — M6 consumes the stable `GET /stats/overview` + `GET /stats/by-category` (and, only
  if OQ3b, `?eventUuid`); no new endpoint or DTO field is needed.
- Reads impose **no tier gate** (§4.9 / backend Assumptions: limits block only create) — M6 shows no
  `LimitNotice`/`UpgradePrompt`.
- The by-category rows already arrive sorted (total DESC → count DESC → name) and filtered (≥1 expense,
  deleted-with-history included) — the client renders them **verbatim** (no re-sort, no re-filter).
- The date-range control operates in the viewer's local zone (matching the browser default `X-Time-Zone`);
  local-day bounds are converted to inclusive ISO exactly like the M4/M5 filters (`dateBoundToIso`).
- Bar-length ratios and any "% share" figure are **display-only ratios** derived from the API's integer
  totals (denominator = the authoritative `overview.totalSpending` for the same range); **no displayed
  money value is ever client-computed** — every money figure comes from the API via `Money` (R3).
- The `ui-designer` runs this cycle and loads the `dataviz` skill before designing any chart; the
  `web-implementer` also loads `dataviz` before building.
- Charts are feature-local for M6 (OQ5a); a shared `ui/charts` extraction is an M8 concern.

## Implementation Plan

> Paths under `FairShareMonWeb/src/`. New feature tree `features/stats/` mirrors the M2–M5 shape
> (`api/`, `hooks/`, `pages/`, `components/`, `schemas.ts`). Concrete names below reflect the
> **recommended** option for every OQ; if the user overrides an OQ the affected step is revised.

### Step 1 — Feature types + API layer

- `features/stats/api/types.ts` — mirror `Models/Stats/**`:
  - `OverviewStatsResponse { from: string | null; to: string | null; totalSpending: number; expenseCount: number }`
  - `StatsRangeRequest { from?: string; to?: string }`
  - `CategoryStatRow { categoryUuid: string; categoryName: string; color: string; icon?: string | null; isDeleted: boolean; total: number; expenseCount: number }`
  - `ByCategoryStatsResponse { eventUuid: string | null; from: string | null; to: string | null; rows: CategoryStatRow[] }`
  - `ByCategoryStatsRequest { from?: string; to?: string; eventUuid?: string }`
  - Money fields typed `number` (API-computed; UI renders, never derives).
- `features/stats/api/statsApi.ts` — on the centralized client (only defined query keys are sent):
  - `overview: (range: StatsRangeRequest) => api.get<OverviewStatsResponse>("/v1/stats/overview", { query: { from: range.from, to: range.to } })`
  - `byCategory: (req: ByCategoryStatsRequest) => api.get<ByCategoryStatsResponse>("/v1/stats/by-category", { query: { from: req.from, to: req.to, eventUuid: req.eventUuid } })`
  - Doc comment: authenticated + resource-owned; `1001` bad range, `9000` event miss (event mode only).

### Step 2 — Query hooks

- `features/stats/hooks/useStats.ts`:
  - `statsKeys = { all: ["stats"], overview: (range) => ["stats","overview",range], byCategory: (req) => ["stats","by-category",req] }`.
  - `useOverviewQuery(range: StatsRangeRequest)` → `useQuery({ queryKey: statsKeys.overview(range), queryFn: () => statsApi.overview(range) })`.
  - `useByCategoryQuery(req: ByCategoryStatsRequest)` → likewise.
  - Read-only feature — no mutations, no cache invalidation beyond React Query defaults.

### Step 3 — Date-range control + range helpers

- `features/stats/dateRange.ts` — a `RangePreset = "thisMonth" | "last30Days" | "thisYear" | "allTime" | "custom"`; `presetToRange(preset): { from?: string; to?: string }` computing inclusive local-day bounds → ISO (reusing the `dateBoundToIso` approach from `features/expenses/dateTime.ts`; extract a shared `dateBoundToIso` into a common util if cleaner, else import). `allTime` → `{}` (both omitted). Timezone-aware by construction.
- `features/stats/schemas.ts` — a Zod `customRangeSchema` mirroring the backend validator: when both `from` and `to` are set, `from <= to` (message via `validation` namespace). Used to block an invalid Custom range client-side before it reaches the API.
- `features/stats/components/StatsRangeControl.tsx` — preset chips (segmented buttons) + a Custom mode revealing two date `TextField`s (`type="date"`); controlled `value: { preset, from, to }` + `onChange`. Shows an inline invalid-range message (from > to). Accessible: `role="group"` + `aria-label`, pressed state on the active preset.

### Step 4 — Overview KPI tiles

- `features/stats/components/KpiTile.tsx` — a presentational `Card`-based tile: `label`, a large `value` slot (a `Money` for currency or a formatted count), optional `hint`. Feature-local (OQ5a).
- `features/stats/components/OverviewKpiRow.tsx` — a responsive grid of `KpiTile`s from an `OverviewStatsResponse`:
  - **Total spending** → `<Money amount={data.totalSpending} />`.
  - **Expense count** → localized count.
  - Loading → `Skeleton` tiles; error → compact `ErrorState`; zero range → tiles show `0` (valid, not empty). **No computed average tile** (R3).

### Step 5 — Category breakdown (chart + accessible table)

- `features/stats/components/CategoryBarChart.tsx` — hand-rolled horizontal bar chart (OQ1a/OQ2a):
  - One row per `CategoryStatRow` in API order; bar width = `row.total / maxTotal` (display-only ratio).
  - Bar fill assigns `--fs-viz-cat-1..8` **in order**; a 9th+ row folds to a muted "Other"/repeat-safe slot per the palette rule (or an `--fs-viz-ink-muted` neutral) — decided with the `dataviz` skill.
  - Each bar carries a **direct label**: `CategoryMarker` (color + icon + name) + the `Money` value (relief-rule compliance + color-independence). Deleted category → `(đã xóa)` suffix/`isDeleted` styling.
  - The chart root is `role="img"` with an `aria-label` summarizing the breakdown (top category + count); the bars themselves are decorative (`aria-hidden`) because the **table** below carries the data for AT.
  - Chart chrome uses `--fs-viz-surface/grid/axis/ink`; honors `prefers-reduced-motion`.
- `features/stats/components/CategoryStatsTable.tsx` — the **accessible data-table alternative**, always rendered (not just a toggle): `Table` with a `<caption>`; columns **Danh mục** (row header = `CategoryMarker` + name, `(đã xóa)` if deleted), **Tổng** (`<Money>`), **Số phiếu** (count), **Tỷ trọng** (% share = `row.total / overview.totalSpending`, display-only ratio). Optional `TableFoot` echoing the authoritative `overview.totalSpending` (never client-summed).
- `features/stats/components/CategoryBreakdown.tsx` — composes the chart + table from a `ByCategoryStatsResponse` (+ the overview total for the denominator/footer); states: loading → `Skeleton`; empty `rows` → `EmptyState` ("Chưa có chi tiêu trong khoảng này"); error → `ErrorState` (retry).

### Step 6 — Stats page + route

- `features/stats/pages/StatsPage.tsx`:
  - `PageHeader` (title/subtitle) + `Stack`.
  - Owns the range state (`StatsRangeControl`); derives the `StatsRangeRequest`.
  - Feeds `useOverviewQuery(range)` → `OverviewKpiRow`, and `useByCategoryQuery({ ...range })` → `CategoryBreakdown` (passing `overview.totalSpending` for the share denominator).
  - `1001` (bad range) is normally prevented by the control; if it still returns, surface `error.message` inline on the range control. `9000` not applicable (time-range mode). Other errors → `ErrorState`.
- `routes/router.tsx` — replace the `path: "stats"` `StubPage` with `<StatsPage />` (lazy-import optional, consistent with siblings).

### Step 7 — Enrich the home dashboard

- `features/dashboard/components/DashboardOverview.tsx` — reuses `useOverviewQuery(presetToRange("thisMonth"))` → `OverviewKpiRow` (labelled "Tháng này").
- `features/dashboard/components/DashboardCategoryBreakdown.tsx` — reuses `useByCategoryQuery(thisMonth)` → a **compact** `CategoryBreakdown` (chart only, or top-5 bars) with a "Xem tất cả" link to `/stats`.
- `features/dashboard/components/RecentActivityCard.tsx` (OQ6a) — `useExpensesQuery({})` sliced to the top 5 (`expenseTime` DESC verbatim); each row = name + `CategoryMarker` + `Money` total + date, linking to the expense detail; `EmptyState` when none; a "Xem tất cả" link to `/expenses`; quick-action buttons (Add expense → `/expenses/new`, New event → `/events`).
- `features/dashboard/pages/DashboardPage.tsx` — recompose: keep the welcome `PageHeader`; add `DashboardOverview`, then a two-column region with `DashboardCategoryBreakdown` + `RecentActivityCard`, then the existing quick-link cards (`useNavEntries`). All new data via the shared stats/expense hooks — no new API surface.

### Step 8 — i18n

- New namespace **`stats`** — `src/i18n/locales/{vi-VN,en-US}/stats.json`; register both in `src/i18n/index.ts` (import + `NAMESPACES`). Representative keys:
  - `page.title` ("Thống kê" / "Statistics"), `page.subtitle`
  - `range.label`, `range.preset.thisMonth|last30Days|thisYear|allTime|custom`, `range.from`, `range.to`, `range.invalid` (from > to)
  - `kpi.totalSpending`, `kpi.expenseCount`, `kpi.expenseCountValue` (pluralized count)
  - `byCategory.title`, `byCategory.chartLabel` (aria summary, interpolated), `byCategory.empty`, `byCategory.error`, `byCategory.deleted` ("(đã xóa)")
  - `byCategory.table.caption`, `table.category`, `table.total`, `table.count`, `table.share`, `table.totalRow`
  - `states.loadError`, `states.retry`
- Home additions in **`common`** (`common:home.*`): `overviewTitle` ("Tháng này"), `categoryBreakdown`, `recentActivity`, `recentExpensesEmpty`, `viewAll`, `quickActions`, `addExpense`, `newEvent`.
- All domain terms fixed (expense = phiếu chi tiêu, category = danh mục, event = đợt). No hardcoded strings.

### Step 9 — MSW handlers + tests

- Extend `src/test/msw/handlers.ts` with `GET /v1/stats/overview` + `GET /v1/stats/by-category` fixtures (a normal set, an empty set, a deleted-category-included set, and a `1001` bad-range case), wrapped in `ApiResult`. Tests per Step 10.

### Step 10 — Tests (web-test-engineer; Vitest + RTL at the MSW boundary, pinned TZ + locale)

- **`statsApi` / `useStats`** — `from`/`to`/`eventUuid` sent only when defined; envelope unwrapped to `data`; `useOverviewQuery`/`useByCategoryQuery` cache keys stable per range.
- **`dateRange` / `schemas`** — each preset yields the expected inclusive ISO bounds under a pinned TZ; `allTime` → `{}`; the custom-range Zod schema rejects `from > to` with the localized message.
- **`OverviewKpiRow`** — total spending rendered via `<Money>` with the **exact API number** (assert the grouped VND string AND that no average/derived money figure appears — **no-float-math assertion**); count rendered; loading → skeletons; error → error state; zero range → `0` tiles (not empty state).
- **`CategoryBarChart`** — bars render in API order (no re-sort); fills use `--fs-viz-cat-*` in order; each bar has a direct `CategoryMarker` + name + `Money` value label; the chart root exposes `role="img"` + an `aria-label`; deleted category shows `(đã xóa)`; **not-color-alone** (name + value present without relying on fill).
- **`CategoryStatsTable`** (the **accessible table-alternative assertion**) — a `<table>` with a caption + the four column headers is present; each row's total is the raw API number via `<Money>` (**no client-summed money**); the optional total row echoes `overview.totalSpending`, never a client sum; deleted category flagged.
- **`StatsPage`** — loading → skeletons; success → KPI row + chart + table; changing a preset/custom range refetches with new `from`/`to`; empty scope → `EmptyState` in the breakdown while KPI tiles show zeros; a `1001` response surfaces the range-control inline message; a generic error → `ErrorState`.
- **`DashboardPage`** (enriched) — renders the this-month KPI row, the compact top category breakdown, and the recent-expenses list (rows link to `/expenses/:uuid`); empty ledger → empty states; quick actions/links present; still role-filters the admin quick link.
- **`statsI18n.test.ts`** — vi-VN and en-US `stats` namespaces have identical key sets (mirror `expensesI18n.test.ts`); home additions parity in `common`.

## Impact Analysis

- **APIs:** none new/changed — consumes stable `GET /stats/overview` + `GET /stats/by-category` (and
  `?eventUuid` only under OQ3b). Reuses M4 `GET /expenses` for recent activity.
- **Database / Infrastructure / Services:** none (frontend planning artifact; read-only screens).
- **Frontend:**
  - **New:** `features/stats/` (`api/{types,statsApi}.ts`, `hooks/useStats.ts`, `pages/StatsPage.tsx`,
    `components/{StatsRangeControl,KpiTile,OverviewKpiRow,CategoryBarChart,CategoryStatsTable,CategoryBreakdown}.tsx`,
    `dateRange.ts`, `schemas.ts`); `features/dashboard/components/{DashboardOverview,DashboardCategoryBreakdown,RecentActivityCard}.tsx`;
    `i18n/locales/{vi-VN,en-US}/stats.json`.
  - **Modified:** `routes/router.tsx` (stub → `StatsPage`); `features/dashboard/pages/DashboardPage.tsx`
    (rich composition); `i18n/index.ts` (register `stats`); `i18n/locales/*/common.json` (home keys);
    `src/test/msw/handlers.ts` (stats fixtures). Possibly a shared `dateBoundToIso` util extraction.
- **Design system:** **first exercise of the reserved `--fs-viz-*` palette.** New feature-local chart
  components (OQ5a); no new `@/components/ui` primitive is expected (charts stay feature-local until the
  M8 extraction), though `CategoryMarker` is reused as the chart legend/label. The `ui-designer` produces
  the KPI tile, bar-chart, and home-composition specs (see Design note).
- **Documentation:** this doc; feature i18n; no `CLAUDE.md` change (Stats already noted as a future viz
  consumer).
- **Downstream:** M8 (Admin) reuses the KPI/bar-chart patterns and is the trigger to extract a shared
  `src/components/ui/charts` module (roadmap Future Improvement).

## Decision Log

### Decision

Draft M6 as: a `/stats` page (date-range → overview KPIs + by-category **bar** breakdown with an
always-present accessible table) plus a rich home dashboard, built with **hand-rolled SVG/CSS charts on
`--fs-viz-*` (no new dependency)**, time-range lens only, charts feature-local, all on the locked
foundation.

### Reason

The by-category and overview data are simple (a ranked list + two scalars) and render cleanly as CSS/SVG
bars + tiles, so a charting library's cost (a foundation-unapproved dependency, bundle weight, and its own
CVD/a11y/theming reconciliation) is unjustified. The palette + relief rule + `CategoryMarker` +
`Table`/`Money`/state primitives already exist, so M6 is composition, not new infrastructure. Keeping the
Stats page to the time-range lens and charts feature-local matches the roadmap's "keep M6 focused" intent
and its deferred shared-`ui/charts` extraction to M8.

### Alternatives Considered

- A charting library (OQ1b) — deferred; the only choice needing user sign-off, and unnecessary for a bar
  breakdown.
- A donut/pie as the primary form (OQ2b) — deferred; the light relief rule + legibility past ~6 slices
  make a ranked bar the stronger first dataviz form.
- An event-scoped by-category lens in M6 (OQ3b) — deferred to a future improvement; the event picture
  already lives on the M5 event detail.
- Building chart primitives into `@/components/ui/charts` now (OQ5b) — deferred to M8 per the roadmap.

## Progress Log

### 2026-07-17

- Feature-planner drafted this M6 plan. Required reading completed: `planning/feature-roadmap.md` (M6
  scope + the resolved OQ4 rich-home placement), `CLAUDE.md` + `src/styles/README.md` (locked stack,
  `--fs-viz-*` palette, relief rule, `CategoryMarker`, `Money`, `Table`, states), the backend
  `StatsController.cs` + `Models/Stats/**` DTOs, and `FairShareMonApi/planning/debt-balance-and-stats.md`
  (overview + by-category semantics, time-range XOR event, deleted-inclusion, DB-side aggregation, money
  exactness). Grounded the plan in the current `src/` tree (design-system barrel, `--fs-viz-*` tokens, the
  M4 expense hooks/types, the M5 events hooks/`EventBalanceTable`, the `dateBoundToIso` helper, the M1
  `DashboardPage`, the `/stats` stub, and the i18n namespace registration).
- Produced the Implementation Plan (routes, files, components, hooks, endpoints + DTO shapes, chart
  types + no-dep rendering approach, i18n keys, a11y requirements, and the test list incl. the
  accessible-table-alternative + no-float-math assertions), Impact Analysis, Decision Log, and six Open
  Questions each with a recommendation.
- Awaiting the user checkpoint to resolve the Open Questions (the orchestrator auto-accepts the
  recommended option) before the ui-designer → implementer → test-engineer → reviewer cycle proceeds.

### 2026-07-17 — ui-designer (design pass)

- Loaded the `dataviz` skill first (mandatory), then **re-ran `scripts/validate_palette.js`**
  against `--fs-viz-cat-*` for both modes. Confirmed the documented relief rule verbatim:
  light categorical PASSes lightness/chroma/CVD (worst adjacent ΔE 9.1) with a **contrast
  WARN — slots 3/4/5 sit < 3:1 on white (`#e87ba4` 2.69 · `#eda100` 2.17 · `#1baf7a` 2.82)
  → direct labels or a table view required**; dark PASSes all six checks (worst adjacent
  ΔE 8.4, all ≥ 3:1). No palette change — used the existing validated `--fs-viz-*`.
- Added the M6 design showcase (reviewable in light + dark via `StyleGuide.tsx`):
  - `src/styles/M6Showcase.tsx` + `src/styles/M6Showcase.module.css` — the four surfaces:
    **StatTile/OverviewKpiRow**, **CategoryBarChart** (+ the paired **CategoryStatsTable**
    and its states: many-categories fold-to-neutral, single, empty), **StatsRangeControl**
    (preset chips + custom two-date, inline invalid-range), and the **rich-home composition**
    (this-month KPI row + compact top-5 breakdown + recent-expenses card + quick actions +
    M1 quick links).
  - Mounted `<M6Showcase />` in `src/styles/StyleGuide.tsx` after M5.
  - Updated the `src/styles/README.md` data-viz section from "reserved — no charts yet" to
    the M6 chart spec (bar-normalization, the chart↔table pairing, relief-rule note, the
    OQ5a feature-local placement, and the "re-run the validator on any hue change" rule).
- dataviz compliance: **only the bar fill wears `--fs-viz-cat-*`** (slots 1..8 by rank, 9th+
  → `--fs-viz-ink-muted`); labels/values/axis wear text tokens. Marks: bar 12px (≤ 24px cap),
  4px rounded data-end square at the baseline, recessive track, ≥ 2px surface gap between
  rows, `prefers-reduced-motion` disables the grow transition. **Bar length = total/maxTotal**;
  **% share = total/overview.totalSpending** — both display-only ratios; no money is
  client-computed (rendered via `Money`, R3). Deleted categories keep their slot with an
  `(đã xóa)` treatment (§4.7).
- `tsc -b`, `pnpm lint`, and `pnpm build` all clean. No new dependency (OQ1a honored). No
  feature data/hooks/routing/i18n built — that is the web-implementer's scope.
- One non-blocking decision for the implementer/user recorded below (OQ7).

### OQ7 — Bar fill color vs. the category's own identity color (design pass)

- **(a) Recommended — bar fill uses the systematic `--fs-viz-cat-*` slot by rank; the
  `CategoryMarker` swatch beside the name keeps the category's OWN color.** This honors the
  M6 mandate (use the CVD-validated reserved palette) and the relief rule; the category's
  arbitrary user-chosen color is not CVD-safe and could be invisible on the chart surface.
  Because identity rests on the direct label (marker + name + value), not the fill, the two
  colors coexisting on a row is safe and legible. Trade-off: a category's swatch color and
  its bar color differ (e.g. an orange "Ăn uống" swatch above a blue bar) — intentional, the
  bar color is the chart's ranking encoding, not the category's brand.
- (b) Paint each bar with the category's own `color`. Trade-off: swatch and bar match, but
  the palette is un-validated (CVD clashes, low-contrast bars on white) and the whole point
  of the reserved `--fs-viz-*` palette is lost. Not recommended. *(Purely visual, reversible;
  implemented as (a) — flag only if the user prefers the category-color bars.)*

### 2026-07-17 — web-implementer (feature build)

- Loaded the `dataviz` skill first (mandatory), then consumed the ui-designer's locked M6 specs from
  `src/styles/M6Showcase.tsx` + `.module.css` verbatim (no restyle). Marked OQ1–OQ7 Resolved at option (a).
- Built the new `features/stats/` tree:
  - `api/types.ts` (mirrors `Models/Stats/**`), `api/statsApi.ts` (`overview`, `byCategory` on the
    centralized client), `hooks/useStats.ts` (`statsKeys` + `useOverviewQuery`/`useByCategoryQuery`, each
    with an `enabled` flag), `dateRange.ts` (`RangePreset`/`RangeValue`, `presetToRequest` → inclusive ISO
    via the reused `dateBoundToIso`; all-time → `{}`; `isCustomRangeInvalid`), `schemas.ts`
    (`customRangeSchema`, message via `validation:stats.rangeInvalid`).
  - `components/`: `StatTile`, `OverviewKpiRow` (Money + count, loading/error/zero states, no computed
    average), `CategoryBarChart` (hand-rolled SVG/CSS bars — fill = `--fs-viz-cat-1..8` by rank, 9th+ →
    `--fs-viz-ink-muted`; length = total/maxTotal; % = total/overviewTotal; `role="img"` + aria-label,
    bars `aria-hidden`; `(đã xóa)` on deleted), `CategoryStatsTable` (the always-present accessible
    channel — caption + 4 cols; footer echoes `overview.totalSpending` + `expenseCount` verbatim, never
    summed), `CategoryBreakdown` (chart + table + loading/empty/error), `StatsRangeControl` (preset chips +
    custom two-date, `aria-pressed`, inline invalid-range guard). Shared `stats.module.css` ported from the
    showcase.
  - `pages/StatsPage.tsx` (owns the range state; drives both queries; `1001` surfaced on the control and
    prevented client-side by disabling the query while a custom range is inverted; other errors →
    panel ErrorState). Route `/stats` stub → `StatsPage` in `router.tsx`.
- Enriched the home: `features/dashboard/components/` `DashboardOverview`, `DashboardCategoryBreakdown`
  (compact top-5 chart, shares the this-month caches), `RecentActivityCard` (top-5 of `useExpensesQuery({})`
  DESC verbatim → expense detail links + quick actions), `dashboard.module.css`; recomposed `DashboardPage`
  (welcome + overview + two-col breakdown/recent + role-filtered quick links).
- i18n: new `stats` namespace (vi-VN + en-US), registered in `i18n/index.ts` + `useT.ts`; `common:home.*`
  additions; `validation:stats.rangeInvalid` — both locales, identical key sets.
- MSW: added `GET /v1/stats/overview` + `/v1/stats/by-category` handlers (range filter, deleted-category
  inclusion, total-DESC sort, `1001` bad-range + range↔event conflict, `9000` event miss) so the app runs
  against mocks.
- Verified live against the backend on **:5200** (curl): registered a user, created 3 expenses across 2
  categories; `overview` → `{totalSpending, expenseCount}`, `by-category` → rows total-DESC with
  `color`/`icon`/`isDeleted`, category totals reconcile to `overview.totalSpending`; `from>to` → 400/`1001`.
  Confirmed the response field names match `api/types.ts` exactly. Additionally drove the real `StatsPage`
  and `DashboardPage` through the API client + MSW in jsdom (temporary throwaway spec, since removed):
  KPI money/count, chart `role=img`, the accessible table (3 data rows + footer echo, 80%/20%/100% shares,
  `(đã xóa)`), and the home KPIs + recent-expense detail links all rendered.
- Quality: `pnpm lint` clean (only the 6 pre-existing fast-refresh warnings), `tsc -b` clean, `pnpm build`
  succeeds, full `pnpm test` green. **No new runtime dependency** (OQ1a honored).
- Deviation recorded: the M1 `dashboardPage.test.tsx` asserted the pre-enrichment layout ("no charts / no
  fetch", quick-link titles as `<h2>`); the plan-mandated enrichment makes those assertions obsolete, so I
  updated that existing test (seeded a mock-valid session; assert links + this-month overview) to keep the
  suite green. Fuller data-panel coverage remains the web-test-engineer's Step 10.
- `CategoryStatsTable` takes an optional `overviewCount` (in addition to the `{rows, overviewTotal}` in the
  design handoff) so the footer can echo the authoritative `overview.expenseCount` verbatim rather than a
  client sum — additive, honors R3.

### 2026-07-17 — web-test-engineer (test pass)

- Wrote the M6 Step-10 test suite (Vitest + RTL at the MSW boundary; pinned TZ
  `Asia/Ho_Chi_Minh` + pinned locale/clock where preset math is involved; per-test
  store isolation via `server.use` overrides + fresh QueryClient). Exercised the
  real hooks/components/pages — mocked only the network. New/updated files under
  `src/features/stats/` and `src/features/dashboard/`:
  - `dateRange.test.ts` — every preset → the exact inclusive ISO bounds under the
    pinned TZ **and** a pinned wall clock (thisMonth/last30Days/thisYear/custom);
    all-time → `{}`; `DEFAULT_RANGE = thisMonth`; `thisMonthRequest` matches the
    preset (shared caches); `isCustomRangeInvalid` from>to guard incl. equal-bound
    and non-custom cases.
  - `schemas.test.ts` — `customRangeSchema` accepts from≤to / equal / empty bounds,
    rejects from>to with the issue on `to`, and emits the localized vi-VN **and**
    en-US `validation:stats.rangeInvalid` copy.
  - `useStats.test.tsx` — `statsKeys` shape under the `["stats"]` root; overview/
    by-category send `from`/`to`/`eventUuid` only when defined (all-time omits both);
    envelope unwrapped to `data`; `enabled=false` fires no request (two-probe proof);
    equal-valued range instances dedupe to one call (stable cache key).
  - `overviewKpiRow.test.tsx` — total via `<Money>` = exact API VND (vi-VN grouping)
    with the **no-derived-average / no-float-math** assertion; grouped count; zero
    range → `0` tiles (not empty); loading → skeleton tiles keeping labels; error →
    compact ErrorState + retry.
  - `categoryBarChart.test.tsx` — rows render in API order verbatim (no re-sort);
    region `role="img"` with a summarizing accessible name, bars `aria-hidden`;
    direct label per bar (marker + name + `<Money>` + %); bar length = total/maxTotal;
    fills = `--fs-viz-cat-1..8` by rank with the 9th folding to `--fs-viz-ink-muted`;
    deleted → `(đã xóa)`; not-color-alone.
  - `categoryStatsTable.test.tsx` — named table + four column headers; per-row totals
    are the raw API numbers; **footer echoes `overview.totalSpending`/`expenseCount`
    verbatim, NOT a client sum** (fixture makes overview totals differ from the row
    sums so an accidental sum is caught); `—` when no overview count; deleted flagged.
  - `categoryBreakdown.test.tsx` — loading skeletons (no chart/table), empty →
    EmptyState, error → ErrorState + retry, success → both chart + table.
  - `statsRangeControl.test.tsx` — labeled `role="group"` of chips; default thisMonth
    is the only `aria-pressed`; selecting moves the pressed state; custom reveals two
    date inputs; inverted custom → inline invalid message + `aria-invalid` on `to`;
    server `1001` message surfaces inline; chips keyboard-reachable with names.
  - `statsPage.test.tsx` — success → KPI + chart + table (overview total appears in
    the KPI tile AND the footer echo); loading → skeletons; changing a preset
    refetches with new bounds (All time drops from/to); empty scope → breakdown
    EmptyState while KPI tiles show zeros; `1001` → inline range-control message;
    generic error → panel ErrorStates; an inverted custom range is blocked
    client-side (the inverted bounds never reach the API).
  - `statsI18n.test.ts` — `stats` vi/en key-shape parity + no empty leaf; `common:home.*`
    parity incl. the M6 additions; `validation:stats.rangeInvalid` parity; fixed
    domain-term guards.
  - `dashboardPage.test.tsx` (enriched beyond the implementer's minimal update) —
    this-month KPI money/count + "Xem thống kê" → `/stats`; compact breakdown shows
    top-5 bars (6th sliced) + "Xem tất cả" → `/stats`; recent-expenses top-5
    `expenseTime`-DESC **verbatim**, each row → `/expenses/:uuid` (6th sliced);
    quick actions → `/expenses/new` + `/events`; empty ledger → both panel empty
    states with quick actions still present; recent-activity error → ErrorState + retry.
- Extra edge cases beyond the plan's list (noted): the `enabled=false` no-request
  proof and the cache-key dedupe (useStats); equal-bound + non-custom guard cases
  (dateRange); the `—` no-count footer placeholder; the client-side inverted-range
  "never reaches the API" URL assertion (statsPage); `aria-invalid` on the `to` field.
- Full suite: **570 passed / 0 failed (66 files)**, up from the 496/56 baseline (+74
  tests). Green on two consecutive `pnpm test` runs (deterministic). `pnpm lint`
  clean (only the 6 pre-existing fast-refresh warnings) and `tsc -b` clean. No product
  code touched; no bugs found — the footer-echo-not-sum invariant and the preset date
  math under the pinned TZ both hold. No coverage gaps outstanding for the M6 scope.

## Final Outcome

**Complete.** M6 delivered the frontend's first data-viz milestone with **no new dependency** (hand-rolled SVG/CSS). `src/features/stats/`: a Stats page (`/stats`) with a preset+custom `StatsRangeControl` (default This month) driving `GET /stats/overview` (KPI tiles via `Money` + `formatCount`, no average) and `GET /stats/by-category` (a horizontal `CategoryBarChart` on `--fs-viz-cat-*` with direct labels + the always-present accessible `CategoryStatsTable` whose footer echoes the overview totals verbatim, never client-summed). Enriched the home `DashboardPage`: this-month KPIs, a compact category breakdown → `/stats`, a top-5 recent-expenses card, and quick actions. Money is R3-safe throughout (bar length/% are display-only integer ratios). Charts are `role="img"` + paired data table; deleted categories keep their slot with `(đã xóa)`; vi-VN + en-US. Time-range lens only (event lens deferred, OQ3a). Verified live on :5200 (overview/by-category reconcile, `1001` range guard). Tests +74 (suite 496→570); code review **APPROVE, 0 blocking**. Review nit fixed pre-close: removed the redundant second invalid-range alert (single field-level message now; two locked tests updated to match). Three cosmetic nits logged below. All 7 OQs shipped at recommended.

## Future Improvements

- Add the **event-scoped by-category lens** (`?eventUuid`, XOR range) as an additive scope toggle on the
  Stats page (OQ3b), reusing `useEventsQuery` for the picker.
- **Inject the shared `formatMoneyVnd` into `<Money>`** on the stats/dashboard usages (M6 review nit) to match the "one formatter of record" convention used by expenses/events (output is currently byte-identical via the fallback).
- **Remove or wire `customRangeSchema`** (M6 review nit): the live guard is `isCustomRangeInvalid`; the Zod schema is only referenced by its own test — drop it or route validation through it.
- **Neutral placeholder (not skeletons) while a custom range is invalid** (M6 review nit): with the queries disabled, the KPI/breakdown panels currently show loading skeletons; render an idle placeholder instead.
- Add a **donut/pie toggle** for the by-category breakdown (OQ2b) once the bar form is validated.
- Extract a shared **`src/components/ui/charts`** module once M8 (Admin) lands, de-duplicating the
  KPI-tile + bar-chart scaffolding (roadmap Future Improvement).
- A **spend-over-time** line/area chart if the backend later adds a time-bucketed stats endpoint (not in
  the current contract — would be an Open Question in its own cycle, not a silent addition).
- Server-side pagination/limit on `GET /expenses` to replace the client-side top-5 slice for recent
  activity, if list sizes grow.
