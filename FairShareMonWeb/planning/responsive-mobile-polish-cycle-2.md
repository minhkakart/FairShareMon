# Responsive / Mobile-First Polish — cycle 2

## Objective

Run **cycle 2** of the responsive/mobile-polish effort. Cycle 1 (`planning/responsive-mobile-polish.md`,
committed `fff2450`) shipped the **substrate** — a documented breakpoint ladder,
the app-shell header fix, an **additive** `Table stackOnMobile` card-stack variant,
coarse-pointer 44px touch targets on the primitives, and a mobile-viewport E2E
project — and covered the two universal + highest-traffic surfaces (header +
ledger loop). It **deliberately deferred** the lower-traffic, chart-heavy, and
narrower surfaces to a follow-up (cycle-1 OQ2). **This is that follow-up.** It
reuses the cycle-1 substrate verbatim and applies it to the deferred surfaces —
it does **not** reinvent the ladder, the `Table` variant, or the touch-target
mechanism.

Concretely this cycle delivers (subject to the scope-split decision in OQ1):

1. **Admin suite** responsive pass — the 8-column user table (sortable / paged /
   filtered), the metrics + revenue dashboards, and the sensitive-action dialogs —
   reflowed for phones.
2. **Stats + home dashboard charts** phone-width tuning — the real shared chart
   primitives (`RankedBarChart`, `TimeSeriesBarChart`, `KpiTile`/`KpiRow` under
   `src/components/ui/charts/`) and their feature compositions, applying the
   `dataviz` skill.
3. **Wallet / QR** — the bank-accounts table on phones + the QR dialog.
4. **members / categories / tags / settings** fine-tuning.
5. The **repo-wide breakpoint-ladder sweep** — consolidate the remaining ad-hoc
   thresholds cycle 1 left untouched to the `sm 30 / md 48 / lg 64rem` ladder.

Like cycle 1, this is **polish + standardization**, not a rebuild: the audit
below shows most of these surfaces already reflow acceptably (grids collapse via
`auto-fit`, filter bars `flex-wrap`, dialogs stack). The concentrated defects are
(a) wide tables that only horizontal-scroll on phones and lose row-header context,
(b) the **time-series chart** whose axis labels collide when columns get narrow,
(c) a handful of **bespoke sub-44px touch targets** that never inherited the
cycle-1 coarse-pointer sizing, and (d) the leftover off-ladder media queries.

## Background

### The locked cycle-1 substrate (REUSE — do not fork or redefine)

Everything below is already shipped and reviewed in cycle 1. Cycle 2 **applies**
it; it authors no new mechanism.

- **Breakpoint ladder** — `sm 30rem / md 48rem / lg 64rem`, mobile-first
  `min-width`, documented as a named comment block in `src/styles/tokens.css`
  (lines ~331–340) and a table in `src/styles/README.md` (§Responsive /
  breakpoints, lines 419–448). Authored as raw `@media (min-width: …rem)`; **no
  build tooling**. The one sanctioned `max-width` is the AppShell header-actions
  hide (`@media (max-width: 63.99rem)`). Cycle 2 reflows every touched file
  against these three values and consolidates strays — it does **not** add a stop.
- **`Table` `stackOnMobile`** — the **additive, opt-in** card-stack variant on
  `src/components/ui/Table/Table.tsx` + `Table.module.css` (README §Opt-in mobile
  table card-stack, lines 458–467). Below `sm` each body row reflows into a
  labeled card via the **`data-label` convention** (pass i18n strings on each
  `<TableCell>`; the `scope="row"` header becomes the card title; a cell with no
  `data-label` — e.g. the actions cell — shows no label). Default is unchanged
  horizontal-scroll. `ExpensesTable` is the proven reference adopter
  (`src/features/expenses/components/ExpensesTable.tsx`). **`Table` has a
  CRITICAL blast radius (34 direct callers / 16 flows — cycle-1 D8);** adopting
  the variant is safe only because it is per-consumer additive — a table changes
  **only** when it opts in.
- **Coarse-pointer 44px sizing** — already on `Button size="sm"`
  (`Button.module.css:83` `@media (pointer: coarse)`) and the segmented
  `ThemeToggle`/`LanguageToggle` (`Controls.module.css:56`). Inherited
  automatically wherever those primitives render — **no re-work**. The gap
  (below) is **bespoke controls that are NOT built on those primitives**.
- **A11y baseline + dataviz** — `FairShareMonWeb/CLAUDE.md` (§Accessibility,
  §Styling → charts use `--fs-viz-*` + the `dataviz` skill) and README §Data-viz
  (lines 204–256) + §Shared chart primitives (303–340). The **light-mode relief
  rule** (README lines 216–219): viz slots 3/4/5 sit < 3:1 on white, so any chart
  using them **must** ship direct value labels or a paired table.
- **E2E net** — cycle 1 added a `mobile` Playwright project (Pixel 5, vi-VN +
  Asia/Ho_Chi_Minh) re-running `e2e/ledger-loop.spec.ts` + a new
  `e2e/header-responsive.spec.ts`, and a viewport-agnostic `gotoNav()` helper in
  `e2e/fixtures/session.ts`. The net covers **only** the ledger loop + header.
  **Admin, stats, and wallet are NOT E2E-covered** (OQ4).

### Current responsive state — the cycle-2 audit (with file refs)

Read at narrow viewports (320–430px). Findings per area:

**Admin suite (`src/features/admin/**`).**

- **User table — `AdminUserTable.tsx`** (`COLUMN_COUNT = 8`: username · tier ·
  role · status · createdAt · grantCount · lastGrantAt · actions). Plain `Table`
  (no `stackOnMobile`) → on a phone it is a long sideways scroll and loses the
  username row-header context. **This is the widest admin table and the primary
  admin reflow defect** (OQ2). Sortable headers are `<button className=sortButton>`
  (`admin.module.css:255–280`, `padding: 0`, inline glyph) — a **bespoke tap
  target well under 44px** that does NOT inherit the cycle-1 coarse-pointer sizing
  (it is not a `Button size="sm"`).
- **Filter bar — `admin.module.css:236–249`** (`.filterBar` `flex-wrap`,
  `.filterField` `flex: 1 1 12rem`, `.filterSearch` `flex: 2 1 16rem`). Wraps into
  stacked rows on narrow — functional, just tall. No media query; no change needed
  beyond verifying the tier/status/role `Select`s + username `TextField` stack
  full-width comfortably.
- **Dashboards — `AdminDashboardPage.tsx` / `AdminRevenuePage.tsx`.** The
  `.dashGrid` (`admin.module.css:119–123`) is `auto-fit minmax(20rem, 1fr)` →
  collapses to one column below ~20rem-per-cell (good on phones). `KpiRow` is
  `auto-fit minmax(13.5rem, 1fr)` (`charts.module.css:25–29`) → one column on a
  phone (good). The distribution `RankedBarChart`s each pair with a table — the
  **charts** themselves are the concern (see Charts below). The console shell
  `.tabs` (`admin.module.css:73–77`) is `overflow-x: auto` (3 tabs fit at 320px);
  `.consoleBody`/`.consoleHeader` use `--fs-space-5` padding — a candidate for a
  phone-tightening tidy, not a break.
- **Sensitive-action dialogs — `components/users/*Dialog.tsx`.** Built on the
  `Dialog` primitive (already responsive: `width: calc(100vw - 2*space-4)`,
  scrolls, footer stacks at 30rem). `.genRow` (`admin.module.css:295–303`,
  generate-password field + Regenerate button, `align-items: flex-end`),
  `.secretRow` / `.actionBar` all `flex-wrap`. The reset-password **secret panel**
  (`.secretValue`, `user-select: all`, mono) reads fine on a phone. Low breakage;
  verify the `.genRow` doesn't clip the Regenerate button at 320px.
- **Detail page — `AdminUserDetailPage.tsx`.** `DescriptionList` stacks below
  32rem (primitive) + a `GrantHistoryTable` (plain `Table`, horizontal-scroll) —
  same wide-table question as the user table but narrower (OQ2 secondary).

**Charts (`src/components/ui/charts/**` — the REAL primitives).**
Cycle-1 notes referenced `RankedBarChart`/`TimeSeriesBarChart`/`KpiRow`; verified
here — the actual files are `RankedBarChart.tsx`, `TimeSeriesBarChart.tsx`,
`KpiTile.tsx` (exporting `KpiTile`/`KpiValue`/`KpiRow`) + `charts.module.css`,
re-exported via `charts/index.ts` and `@/components/ui`. These are **hand-rolled
CSS/flex charts, not SVG** — there is **no fixed pixel width and no `viewBox`**;
they size fluidly off flexbox. `charts.module.css` has **no breakpoint at all**
(only a `prefers-reduced-motion` block at line 202).

- **`KpiTile` / `KpiRow`** — `.kpiRow` is `auto-fit minmax(13.5rem, 1fr)` → one
  column on a phone. Value line is `tabular-nums`, `overflow-wrap: anywhere`.
  **Reads fine on phones; no change.**
- **`RankedBarChart`** — a vertical stack of rows; each `.barHeader` is a
  `space-between` flex of `.barLabel` (min-width 0, `flex-wrap: wrap`) + `.barValue`
  (`white-space: nowrap`, tabular). Long labels wrap; the value stays put. **Reads
  acceptably on phones**; a long category/username label beside a long `<Money>`
  can crowd but does not overflow the page. Light touch (verify wrap comfort).
- **`TimeSeriesBarChart` — the real chart defect.** `.tsPlot` (`charts.module.css:150–157`)
  is a flex row of `.tsCol` (`flex: 1 1 0`), fixed `height: 12rem`, `gap: space-2`;
  `.tsBar` caps at `max-width: 2.75rem`; `.tsAxisLabel` is `flex: 1 1 0` with
  `overflow-wrap: anywhere`. With many buckets (12 months, or the **day** bucket
  toggle → up to ~31 columns) on a 360px viewport each column is ~10–24px wide, so
  the **axis labels (`07/2026`, `16/07`) collide, wrap to 2–3 lines, or become
  unreadable**, and the `.tsCap` value labels (when `showValues`) overlap. The
  data channel is safe (every chart pairs with an accessible table) and the
  **relief rule is already satisfied** (single sequential hue clears 3:1; ranked
  bars carry direct labels) — so this is a **legibility/density** problem, not a
  contrast one. Resolving it is a distinct dataviz discipline (OQ3), owned by the
  **ui-designer**.
- **Home dashboard (`src/features/dashboard/**`).** `dashboard.module.css:35–44`
  `.homeGrid` collapses to one column at **`max-width: 60rem`** — an **off-ladder
  threshold** (sweep target; and authored `max-width`, against the mobile-first
  convention). Otherwise the recent-expenses list and quick-actions reflow fine.

**Wallet / QR (`src/features/wallet/**`).**

- **`BankAccountsTable.tsx`** — 5 columns (bank+BIN · account#+reveal · holder ·
  default · actions[Premium only]), plain `Table` (horizontal-scroll) → sideways
  scroll on phones, loses the bank-name row-header context. **Direct parallel to
  the cycle-1 `ExpensesTable` case → the natural `stackOnMobile` adopter.** The
  per-row **reveal toggle** (`BankAccountsTable.module.css:33–51`, `.revealBtn`
  `1.75rem`/28px, bespoke — **not** a `Button size="sm"`) is a **sub-44px touch
  target that does NOT inherit the cycle-1 coarse-pointer sizing** (a11y gap).
- **`QrDialog` (`QrDialog.tsx` + `.module.css`)** — inside the responsive `Dialog`.
  `.qrFrame` is `max-width: 15rem` (expense square) / `17rem` (event 3:4) and
  scales down — fine on phones, and correctly stays light in both themes to scan.
  The `.accountCard` is a `grid-template-columns: auto 1fr` (term | value) — the
  term column is `white-space: nowrap`; on a 320px viewport a long Vietnamese term
  beside a long holder name/number could get tight (candidate to stack at the
  smallest width). Footer (Download + Copy details + Close) stacks via the Dialog's
  30rem footer rule. Low breakage; verify the account block at 320px.

**members / categories / tags / settings.**

- **Settings (`SettingsPage.tsx`)** — a `PageHeader` + `Stack` of `Card`s
  (`ProfileCard`/`TierStatusPanel`/`PreferencesCard`/`SecurityCard`). Already fully
  responsive (PageHeader wraps actions, DescriptionList stacks). **Verify only.**
- **members / categories / tags tables** (`MembersTable`, `CategoriesTable`,
  `TagsTable`) — plain `Table` (horizontal-scroll), **fewer columns** than the
  admin/expenses/wallet cases; grep found **no off-ladder media queries** in these
  features. Fine-tuning = verify wrap + decide whether the denser of them warrant
  `stackOnMobile` (folds into OQ2's "which tables adopt" answer) or just scroll.

**Repo-wide breakpoint-ladder sweep — the remaining off-ladder thresholds.**
Grep of `src/**` for `@media` (excluding `prefers-*`, `pointer: coarse`, and the
already-consolidated files) leaves these strays for cycle 2:

| Threshold | File:line | Note |
|-----------|-----------|------|
| `max-width: 34rem` | `features/expenses/components/AuditTimeline.module.css:132` | → nearest stop; mobile-first re-author |
| `max-width: 32rem` | `features/events/components/AssignExpenseDialog.module.css:107` | → `sm 30rem` |
| `max-width: 60rem` | `features/dashboard/components/dashboard.module.css:40` | `.homeGrid`; → `md`/`lg`, mobile-first |
| `min-width: 32rem` | `components/ui/Layout/Layout.module.css:91` | **primitive** — DescriptionList two-col; → `sm`/`md` (ui-designer) |
| `max-width: 30rem` | `components/ui/Dialog/Dialog.module.css:123`, `components/ui/Form/Form.module.css:34` | value already on-ladder but `max-width`; low-priority convention tidy (ui-designer) |
| `max-width: 32rem` | `styles/M5Showcase.module.css:251` | showcase |
| `max-width: 40rem` | `styles/M4Showcase.module.css:161` | showcase |
| `max-width: 34rem` | `styles/M4Showcase.module.css:382` | showcase |
| `max-width: 60rem` | `styles/M6Showcase.module.css:271` | showcase |

Already on the ladder (leave): `ExpenseFilterBar.module.css:14` (min 30),
`ShareEditor.module.css:112` (min 48), `Table.module.css:282` (min 30),
`AppShell.module.css:107/153` (min 64 / max 63.99).

**MSW coverage (for the E2E OQ).** `src/test/msw/handlers.ts` already handles the
admin, stats, bank-accounts, and QR routes (20 matches) — so a mobile E2E spec for
any of these areas is feasible on the existing harness with **no mock changes**
(OQ4).

**Net:** the reflow foundations from cycle 1 hold. The concentrated cycle-2 work is
(a) adopting `stackOnMobile` on the widest tables (admin users, wallet, maybe
grant-history), (b) the **time-series chart** density fix (the one genuinely new
design problem), (c) closing the **bespoke sub-44px touch targets**
(`.revealBtn`, admin `.sortButton`), and (d) the off-ladder sweep. Everything else
is verify-and-tidy.

### Design ownership (again a design-led cycle, but lighter than cycle 1)

Per CLAUDE.md, `tokens.css` / `global.css` / `src/components/ui/*` (including
`components/ui/charts/*` and the `Table` primitive) are the **ui-designer's**
domain. Cycle 2 leads with the **ui-designer** for the genuinely new design work —
the **chart phone-density pattern** (`TimeSeriesBarChart` + `charts.module.css`,
applying the `dataviz` skill) and any primitive touch-ups (`Layout` DescriptionList
threshold, bespoke touch-target guidance). The **web-implementer** then applies the
ladder + adopts `stackOnMobile` feature-side (admin/wallet tables, i18n
`data-label`s) and does the off-ladder sweep. The **web-test-engineer** verifies +
adds any new mobile E2E per OQ4. Hand-off points are called out per step.

## Requirements

### Functional — standardization

- **R1** — Complete the **repo-wide breakpoint sweep**: every stray threshold in
  the table above is consolidated to the nearest ladder stop and re-authored
  mobile-first `min-width` (except the sanctioned AppShell `max-width`). No new
  one-off threshold is introduced anywhere this cycle touches.

### Functional — admin suite

- **R2** — The admin **user table** (`AdminUserTable`, 8 cols) is usable at
  320–430px with no page-level horizontal scroll and no loss of the username
  row-header context — via the mobile-table strategy resolved in OQ2 (recommended:
  adopt the additive `stackOnMobile`, mirroring `ExpensesTable`).
- **R3** — The admin **dashboards** (metrics + revenue) reflow cleanly on phones:
  KPI row and dash grid stack (already do — verify), the charts are legible per
  R6, and each chart's **paired accessible table** remains present and correct.
- **R4** — The **sensitive-action dialogs** (enable/disable/revoke/set-role/
  reset-password/tier grant-revoke) remain fully operable on phones — the danger
  accent, consequence `Alert`, acknowledgment gating, the one-time secret panel
  (`user-select: all` + copy + "closing destroys this"), and `showClose={false}`
  behavior all preserved through reflow. The admin privacy boundary (metadata /
  tier-grant / revenue only — never ledger data) is untouched.

### Functional — charts (stats + home)

- **R5** — `KpiTile`/`KpiRow` and `RankedBarChart` are verified legible at
  320–430px (they largely are — light touch).
- **R6** — `TimeSeriesBarChart` is legible on phones with dense buckets: axis and
  cap labels no longer collide/overlap, via the approach resolved in OQ3. The
  chart continues to satisfy the **light-mode relief rule** (direct labels / paired
  table), keeps `role="img"` + summarizing `ariaLabel` + `aria-hidden` marks, and
  honors `prefers-reduced-motion`. The `dataviz` skill is applied.

### Functional — wallet / QR

- **R7** — `BankAccountsTable` is usable at 320–430px (mobile-table strategy per
  OQ2; recommended: adopt `stackOnMobile`), and the QR dialog's image well +
  account block + footer read cleanly on phones (account block stacks if needed).

### Functional — members / categories / tags / settings

- **R8** — These surfaces are verified at 320–430px and any dense table adopts the
  strategy chosen in OQ2; settings is verify-only.

### Non-functional — a11y (must not regress)

- **R9** — The **bespoke sub-44px touch targets** surfaced by the audit
  (`BankAccountsTable` `.revealBtn` 28px, admin `.sortButton`) reach a ≥44px
  effective hit area on coarse pointers — reusing the cycle-1 mechanism (either
  rebuild on `Button size="sm"`, or add an equivalent `@media (pointer: coarse)`
  rule against tokens, per README lines 449–456). Fine-pointer desktop sizing is
  unchanged. Resolution of "rebuild vs local rule" is OQ5.
- **R10** — Status/finance/sort meaning stays **icon+text/sign**, never
  color-alone, through every reflow (sort glyph + `aria-sort`, default-marker
  star+text, tier/role/status badges, balance sign). Long Vietnamese strings keep
  `overflow-wrap` and never force page sideways scroll. `:focus-visible`, keyboard
  nav, and Radix dialog focus management are preserved.

### Non-functional — quality bar + verification

- **R11** — `pnpm lint`, `tsc -b`, `pnpm build`, `pnpm test` (Vitest) stay green;
  `pnpm test:e2e` (desktop + mobile projects) stays green.
- **R12** — Verification: extend the manual viewport checklist to the cycle-2
  surfaces; and per OQ4, optionally add targeted **admin/stats/wallet mobile E2E
  specs** reusing the existing harness + MSW handlers.
- **R13** — **Purely additive / presentation-only**: no API-contract change, no
  business-rule change (closed-event immutability, tier gates `13000-3`/`14001-2`,
  ownership 404s / `1003` / `14000` all unchanged), no new runtime dependency. Any
  `Table` adoption is per-consumer additive (the CRITICAL `Table` default is not
  touched — cycle-1 D8 constraint carries forward).

## Open Questions

> Each has a **Recommended** option. These are the checkpoint decisions.

- **OQ1 — Scope: one cycle 2, or split into sub-cycles?** This is a broad surface
  (admin + charts + wallet + 4 smaller features + the sweep) spanning two distinct
  disciplines (table-reflow mechanics vs chart dataviz).
  - **(a, Recommended) Split into `2a` and `2b`.** **`2a` = tables + sweep:** admin
    user table + grant-history + wallet `BankAccountsTable` adopt `stackOnMobile`;
    members/categories/tags/settings verify + tidy; the full off-ladder sweep; the
    bespoke touch-target fixes (R9); admin/wallet mobile E2E if OQ4 approves. This
    reuses the proven cycle-1 `ExpensesTable` pattern with **near-zero design
    novelty** — mostly web-implementer application + sweep. **`2b` = charts:** the
    `TimeSeriesBarChart` phone-density pattern + stats/home chart tuning, applying
    the `dataviz` skill. Chart reflow is a **distinct design discipline** that
    deserves its own focused ui-designer pass and its own review, and it is the
    only part with real unknowns (OQ3). Rationale: keeps each sub-cycle single-
    discipline, lets `2a` ship fast on established rails while `2b` gets proper
    design attention, and isolates the chart risk.
  - **(b) One cycle 2, sequenced (tables → charts → sweep) internally.** Fewer
    checkpoints/docs; but couples a fast mechanical pass to a slower design
    exploration and makes the review a mixed bag.
  - **(c) Split differently — admin as its own cycle, everything else together.**
    Admin is the largest single surface; but its table work is the *same* pattern
    as wallet's, so splitting them apart duplicates the table-reflow context.
  - *Impact:* the number of planning docs / checkpoints and how the
    web-implementer's work is sequenced. (This doc is written to serve either as
    the single cycle-2 plan or as the `2a` plan with `2b` carved out — the
    Implementation Plan marks the `2a`/`2b` boundary.)

- **OQ2 — Mobile-table strategy for the admin suite (esp. the widest 8-col
  `AdminUserTable`): adopt `stackOnMobile` or stay horizontal-scroll?**
  - **(a, Recommended) Adopt `stackOnMobile`** on `AdminUserTable` (username =
    `scope="row"` card title; tier/role/status/createdAt/grantCount/lastGrantAt get
    `data-label` from the existing `admin:users.columns.*` header keys; the
    view-action cell stays label-less), and likewise on `BankAccountsTable`
    (bank+BIN = card title; account#/holder/default get `data-label`s) and the
    `GrantHistoryTable`. This directly mirrors the reviewed cycle-1 `ExpensesTable`
    adoption, gives phones a readable card per row, and is **per-consumer additive**
    so the CRITICAL `Table` default and its other 33 consumers are untouched
    (cycle-1 D8). Trade-off: the 8-col user table is dense as a card — but a card
    with 7 labeled lines is far more readable than a 7-column sideways scroll, and
    the label reuse keeps i18n cost near-zero.
  - **(b) Horizontal-scroll only for admin (add a scroll affordance / edge fade).**
    Cheapest, zero markup change, zero selector risk. But it leaves the widest
    table in the app awkward on phones — the exact defect this cycle exists to fix.
  - **(c) `stackOnMobile` on wallet + grant-history, but scroll-only on the 8-col
    user table** (too dense to card-stack well). A middle ground; but inconsistent,
    and the user table is the one admins most need on a phone.
  - *Impact:* which admin/wallet tables gain `data-label`s and opt into the variant.

- **OQ3 — `TimeSeriesBarChart` phone-density approach (the real chart work).**
  The chart is fluid flex with no min column width, so many buckets crush the
  columns and collide the labels on phones.
  - **(a, Recommended) Give columns a min width and horizontally scroll the plot
    below the density threshold**, complemented by axis-label thinning for the
    densest (day) buckets. Set `.tsCol` a `min-width` (e.g. ~2.5–3rem) and wrap
    `.tsPlot` (+ the aligned `.tsAxis`) in an `overflow-x: auto` scroller so each
    bucket keeps a legible width and its label stays readable; for the day bucket,
    show every Nth axis label (or rely on `showValues={false}`, which the chart
    already supports). This matches the app's established "scroll the data box, not
    the page" idiom (the `Table` scroll wrapper), keeps every bucket, and the
    paired accessible table remains the full data channel for assistive tech.
    Trade-off: the axis + plot must scroll **together** (two synced scrollers or one
    shared wrapper) so labels stay aligned to columns — a small `charts.module.css`
    +`TimeSeriesBarChart.tsx` structural tweak; additive, chart-local.
  - **(b) Keep it fluid, just thin the labels** (render every Nth `periodLabel`,
    drop caps when narrow). No scroll, no structural change; but sub-24px columns
    are still visually cramped and hard to read/tap-inspect on a phone.
  - **(c) Reduce the bucket count on phones** (e.g. cap to last N buckets below
    `sm`). Simplest visual; but it **hides data** on phones (the table still shows
    all, but the chart lies by omission) — weakest for a finance/audit product.
  - *Impact:* the `TimeSeriesBarChart` markup + `charts.module.css`; owned by the
    ui-designer with the `dataviz` skill (thin marks, text-token labels, re-run
    `scripts/validate_palette.js` only if a `--fs-viz-*` hue changes — it won't).

- **OQ4 — Add admin/stats/wallet mobile E2E this cycle, or defer?** The cycle-1
  net covers only the ledger loop + header; these areas are unguarded, so
  restructuring their markup is **lower selector-risk (nothing asserts it) but also
  unguarded against silent regression**. MSW already covers all four route groups.
  - **(a, Recommended) Add targeted mobile E2E for the surfaces this cycle
    restructures markup on** — i.e. small phone-viewport specs for the admin user
    table (card-stack renders labeled rows; still navigable to detail) and the
    wallet table (card-stack + reveal toggle), on the existing `mobile` Playwright
    project, reusing the harness + MSW handlers unchanged. Skip E2E for
    verify-only surfaces (settings) and cover the charts with component tests (RTL)
    rather than E2E (the chart density is better asserted in a unit test of
    scroll/label behavior). Rationale: guards exactly the markup we restructure,
    for little cost, matching the cycle-1 philosophy of not restructuring covered
    markup unguarded.
  - **(b) Defer all new E2E; verify via the manual checklist + component tests
    only.** Faster to land; but the new admin/wallet card-stack markup then has no
    automated guard.
  - **(c) Add broad admin+stats+wallet mobile E2E flows** (dashboards, dialogs,
    QR). Strongest coverage; but it is a large test-authoring effort disproportionate
    to a polish cycle and overlaps the "admin/stats/wallet lack E2E entirely"
    problem, which is really its own future initiative.
  - *Impact:* whether `e2e/` gains admin/wallet mobile specs and the `mobile`
    project's `testMatch`/runtime.

- **OQ5 — Bespoke sub-44px touch targets (`.revealBtn` 28px, admin `.sortButton`):
  rebuild on the primitive, or add a local coarse-pointer rule?**
  - **(a, Recommended) Add a local `@media (pointer: coarse)` min-size rule against
    tokens** to each bespoke control (per the sanctioned README guidance, lines
    449–456), keeping their compact fine-pointer desktop sizing. Lowest-risk: it
    does not change these controls' distinctive presentation (the reveal eye-toggle
    is a deliberately small inline affordance beside a mono number; the sort button
    is an inline header glyph) and needs no markup change. Trade-off: two small
    feature-local media queries (acceptable — they mirror the primitive's own rule).
  - **(b) Rebuild them on `Button size="sm"`** so they inherit the coarse-pointer
    sizing for free. Fewer bespoke rules long-term; but it restyles two controls
    with specific layouts (a bordered square toggle, an inline sort header) and
    risks visual/spacing regressions for a cosmetic gain.
  - **(c) Leave them** — both already clear WCAG 2.5.8 AA (24px). Least work; but it
    leaves the exact "tight for thumbs" defect cycle 1's OQ4 chose to fix on the
    primitives, now inconsistent on the bespoke ones.
  - *Impact:* whether `BankAccountsTable.module.css` / `admin.module.css` gain
    coarse-pointer rules, or the two controls are refactored onto `Button`.

- **OQ6 — `AdminUserTable` sort controls are hidden on phones under
  `stackOnMobile` (raised during the 2a build).** The sortable column headers
  (username/tier/status/createdAt) live in the `<thead>`, which the card-stack
  variant sets `display:none` below `sm` (by design — the card-stack hides the
  column-header row). So on a phone the admin user list card-stacks nicely but is
  **no longer sortable** (filter + pagination remain, since they live outside the
  table). This is inherent to the chosen OQ2a pattern and was not called out in
  the OQ2a trade-off text. **Not blocking** — 2a shipped as the plan directed
  (the pattern's header-hiding is documented in `Table.tsx`), but flagged for the
  planner/reviewer to accept or address.
  - **(a, Recommended) Accept** — sort is a secondary need on a phone; the filter
    bar (tier/status/role/search) and pagination remain fully operable and cover
    the common "find this user" case. No further work.
  - **(b) Expose a mobile sort control outside the header** — e.g. a small
    sort-by `Select` in the filter bar rendered only below `sm`, driving the same
    URL `sort`/`dir` state. A follow-up (fits a later cycle, not 2a), and needs a
    ui-designer pattern so it is not bespoke.
  - **(c) Leave `AdminUserTable` on horizontal-scroll (OQ2b) instead of
    card-stack** — keeps the headers/sort visible on a phone via sideways scroll,
    but reintroduces the exact wide-table defect this cycle set out to fix, and
    contradicts the resolved OQ2a. Not recommended.
  - *Impact:* whether a mobile-only sort affordance is added later; no change to
    the 2a deliverable.
  - **Resolved 2026-07-18 — (a) Accept.** User accepted the trade-off at the 2a
    commit checkpoint; sort stays desktop-only for now. The mobile sort control
    (b) and the `GrantHistoryTable` card-title parity nit (reviewer nit 1) are
    **backlogged to Future Improvements**, not 2a.

## Assumptions

- **A1** — Cycle 1 is committed (`fff2450`) and its substrate — the ladder,
  `Table stackOnMobile`, coarse-pointer sizing, the `mobile` Playwright project +
  `gotoNav()` — is present and unchanged. Cycle 2 consumes it as-is.
- **A2** — This remains a **polish/standardization** cycle: most cycle-2 surfaces
  already reflow acceptably (audit above); the plan targets specific defects, not a
  rebuild.
- **A3** — `tokens.css` / `global.css` / `src/components/ui/*` (incl.
  `components/ui/charts/*` and `Table`) stay the ui-designer's domain; primitive/
  chart changes are authored there, applied feature-side by the web-implementer.
- **A4** — No API/DTO/business-rule change is needed (presentation-only). Admin
  privacy boundary, tier gates, closed-event immutability, ownership 404s, and
  money/time formatting are untouched.
- **A5** — Admin/stats/wallet are **not** currently E2E-covered, so restructuring
  their markup does not risk the existing ledger-loop/header specs. The charts are
  **CSS/flex, not SVG** — the chart fix is a flex/overflow tweak, not a viewBox
  rewrite.
- **A6** — MSW handlers already cover admin/stats/bank-accounts/QR; any new mobile
  E2E reuses them + the `pnpm dev --port 5199` / `VITE_ENABLE_MOCKS=true` path
  unchanged.
- **A7** — Target viewport floor is **320px** through desktop; `--fs-layout-max:
  72rem` ceiling unchanged.
- **A8** — Per CLAUDE.md, **`gitnexus_impact` must be run before editing any
  symbol.** The known CRITICAL target is `Table` (34 callers); the implementer runs
  impact on `AdminUserTable`, `BankAccountsTable`, `GrantHistoryTable`,
  `TimeSeriesBarChart`, and `charts.module.css` consumers before editing, and the
  additive-only approach keeps the effective blast radius LOW (as in cycle-1 D8).

## Implementation Plan

> Files relative to `FairShareMonWeb/`. Ordered design-first (ui-designer) →
> feature application (web-implementer) → verification (web-test-engineer). The
> `2a`/`2b` split (OQ1a) is marked; if OQ1b (one cycle) is chosen, run all steps in
> the `2a → 2b` order within a single cycle. Exact behavior of the OQ-dependent
> steps states the recommended path and marks the branch points.

### — Sub-cycle 2a: tables + touch targets + sweep —

#### 2a.1 Repo-wide breakpoint sweep (web-implementer; ui-designer for primitives) — R1

- Re-author each stray to mobile-first `min-width` at the nearest ladder stop:
  - `features/expenses/components/AuditTimeline.module.css:132` (34rem →
    `min-width: 30rem`/`48rem` mobile-first).
  - `features/events/components/AssignExpenseDialog.module.css:107` (32rem → `sm`).
  - `features/dashboard/components/dashboard.module.css:40` (`.homeGrid` 60rem →
    `md`/`lg` mobile-first — base one-column, `min-width` restores two-column).
  - `styles/M4Showcase.module.css:161,382` · `M5Showcase.module.css:251` ·
    `M6Showcase.module.css:271` (showcase strays → nearest stop).
- **ui-designer** handles the primitive strays: `components/ui/Layout/Layout.module.css:91`
  (DescriptionList `min-width: 32rem` → `sm`/`md`); optionally tidy
  `Dialog.module.css:123` / `Form.module.css:34` (`max-width: 30rem`) — value is
  already `sm`, so this is a low-priority convention tidy (may defer).
- No behavior change — pure threshold consolidation. Update README's "consolidate
  strays" note to mark the sweep complete.

#### 2a.2 Admin user table + grant-history reflow (web-implementer) — R2 · OQ2

- **Impact analysis first** on `AdminUserTable` / `GrantHistoryTable` (and confirm
  the `Table` CRITICAL rating from cycle-1 D8 still holds).
- Recommended (**OQ2a**): add `stackOnMobile` to the `<Table>` in
  `features/admin/components/users/AdminUserTable.tsx`. Username stays
  `<TableHeaderCell scope="row">` (card title, label-less); add
  `data-label={t("admin:users.columns.<col>")}` to the tier/role/status/createdAt/
  grantCount(keep `numeric`)/lastGrantAt cells reusing the **existing header keys**;
  the trailing view-action cell stays label-less. Skeleton + `TableEmpty` states
  render inside the same table — verify they read in card mode.
- Apply the same to `GrantHistoryTable` (`components/users/GrantHistoryTable.tsx`).
- No logic change — sort/pagination/filter state is unchanged (URL/query owned).

#### 2a.3 Wallet table reflow (web-implementer) — R7 · OQ2

- Add `stackOnMobile` to `features/wallet/components/BankAccountsTable.tsx`:
  bank+BIN block = `scope="row"` card title; add `data-label` (from
  `wallet:table.*` header keys) to account#/holder/default cells; the Premium
  actions cell stays label-less. Keep the reveal toggle inline with the (now
  card-line) account number.
- Verify the `QrDialog` account block at 320px (`QrDialog.module.css` `.accountCard`
  `auto 1fr`) — if the `nowrap` term crowds the value at the floor, add a base
  single-column stack that becomes two-column at `sm` (ladder). Chart-frame + footer
  need no change (already scale/stack).

#### 2a.4 members / categories / tags / settings (web-implementer) — R8 · OQ2

- Verify each at 320–430px. Settings is verify-only. For members/categories/tags
  tables: adopt `stackOnMobile` **only** where OQ2's answer says a denser table
  warrants it (default recommendation: adopt where a table has ≥5 meaningful data
  columns, else leave on scroll); reuse header i18n keys for `data-label`s.

#### 2a.5 Bespoke touch targets (ui-designer guidance; web-implementer applies) — R9 · OQ5

- Recommended (**OQ5a**): add a `@media (pointer: coarse)` min-size rule to
  `BankAccountsTable.module.css` `.revealBtn` (→ ≥44px effective) and to
  `admin.module.css` `.sortButton` (padded hit area ≥44px on coarse), keeping the
  compact fine-pointer sizing. If OQ5b is chosen, rebuild both on `Button size="sm"`
  instead (inherits the primitive rule).
- Verify no status/sort/finance cue became color-only through any reflow (R10).

#### 2a.6 Verification — 2a (web-test-engineer) — R11/R12 · OQ4

- Extend the manual viewport checklist (`e2e/RESPONSIVE-CHECKLIST.md` or
  `e2e/README.md`) with the admin table/dialogs, wallet table/QR, and the smaller
  features at 320/375/430px.
- Recommended (**OQ4a**): add small **mobile-project** E2E specs for the
  restructured markup — `e2e/admin-users-responsive.spec.ts` (card-stack rows are
  labeled; navigable to detail) and `e2e/wallet-responsive.spec.ts` (card-stack +
  reveal toggle), reusing the harness + MSW. Requires an admin-session fixture (a
  logged-in ADMIN) — extend `e2e/fixtures/` if one does not exist.
- Component tests (Vitest + RTL): assert `AdminUserTable`/`BankAccountsTable`
  render their `data-label`s; assert the bespoke controls carry their coarse
  sizing rule / a11y attributes.

### — Sub-cycle 2b: charts —

#### 2b.1 `TimeSeriesBarChart` phone-density pattern (ui-designer, `dataviz` skill) — R6 · OQ3

- **Impact analysis first** on `TimeSeriesBarChart` + `charts.module.css` consumers
  (admin `SignupsPanel`/revenue chart, stats/home).
- Recommended (**OQ3a**): in `components/ui/charts/TimeSeriesBarChart.tsx` +
  `charts.module.css`, give `.tsCol` a `min-width` and wrap the `.tsPlot` + aligned
  `.tsAxis` in a shared `overflow-x: auto` scroller so columns keep a legible width
  and axis labels stay aligned + readable when there are many buckets; add an axis-
  label thinning path for dense (day) buckets (or lean on the existing
  `showValues={false}`). Apply the `dataviz` mark specs (thin marks ≤24px — already
  ≤2.75rem, 4px rounded data-end, ≥2px gaps, recessive baseline/grid), keep all
  labels in text tokens, preserve `role="img"` + `ariaLabel` + `aria-hidden` marks
  + reduced-motion. No `--fs-viz-*` hue changes → no `validate_palette.js` re-run.
- Update `M8Showcase.tsx` (and/or `M6Showcase.tsx`) so the dense-bucket behavior is
  reviewable in light + dark.

#### 2b.2 `RankedBarChart` + `KpiRow`/`KpiTile` phone verification (ui-designer) — R5

- Verify `.barHeader` wrap comfort at 320px (long label + `<Money>` value); tune
  gap/wrap only if a real crowd is found. `KpiRow`/`KpiTile` are already
  single-column on phones — verify only.

#### 2b.3 Stats + home chart applications (web-implementer) — R5/R6

- Confirm the stats `CategoryBreakdown`/`CategoryStatsTable`, the home
  `dashboard` composition (post-`.homeGrid` sweep from 2a.1), and the admin
  `SignupsPanel`/`RevenueChart` all consume the updated chart primitives correctly
  at phone width; each chart's paired accessible table stays present.

#### 2b.4 Verification — 2b (web-test-engineer) — R11/R12 · OQ4

- Component tests (Vitest + RTL) for `TimeSeriesBarChart`: assert the scroller/
  min-width + label-thinning behavior and that `role="img"`/`ariaLabel`/paired-table
  invariants hold. (Per OQ4a, chart density is asserted in unit tests rather than
  E2E.)
- Add the chart surfaces to the manual viewport checklist.

### Quality bar (all) — R11

- `pnpm lint`, `tsc -b`, `pnpm build`, `pnpm test`, `pnpm test:e2e` (desktop +
  mobile) green. Update README/showcase docs where a chart primitive changed or a
  table adopted `stackOnMobile`.

## Impact Analysis

- **Mostly low-risk, pure CSS/token/layout** — the sweep (2a.1), touch-target rules
  (2a.5), QR account-block tidy (2a.3), and chart verifications (2b.2) touch only
  CSS modules; no logic, no selector impact.
- **Higher-risk / run `gitnexus_impact` before editing:**
  - **`Table.tsx`** — CRITICAL (34 callers / 16 flows, cycle-1 D8). Cycle 2 does
    **not** touch the primitive; it only makes more consumers **opt in** to the
    existing `stackOnMobile`. Each adoption changes only that consumer's render.
  - **`AdminUserTable.tsx` / `BankAccountsTable.tsx` / `GrantHistoryTable.tsx`**
    (2a.2/2a.3) — markup within the card-stack variant changes. **Not** E2E-covered
    today (A5) → no existing spec breaks; guard with new specs (OQ4a) + component
    tests. Run impact to confirm each has a small caller set (each is a single-page
    consumer).
  - **`TimeSeriesBarChart.tsx` + `charts.module.css`** (2b.1) — a shared chart
    primitive consumed by admin (signups + revenue) and any stats/home time series.
    Additive/chart-local (a scroller + min-width + label thinning); the props API
    and the `role="img"`/paired-table contract are unchanged. Run impact to
    enumerate consumers and re-verify each after.
- **E2E-covered surfaces held stable:** cycle 2 touches **none** of the
  ledger-loop/header markup — `EventBalanceTable`, `ExpensesTable`, and `AppShell`
  are untouched, so the existing `chromium` + `mobile` specs keep passing (re-run to
  prove). Any new admin/wallet specs are additive.
- **No API / business-rule impact** (R13): presentation-only. Admin privacy
  boundary, tier gates, closed-event immutability, ownership 404s, money/time
  formatting all unchanged.
- **Dependency impact:** none. No new runtime or build dependency (the chart fix is
  CSS/flex; new E2E reuses the approved `@playwright/test` + MSW).
- **Bundle impact:** negligible (CSS deltas + `data-label` attributes + a possible
  admin-session E2E fixture).

## Decision Log

| # | Decision | Status | Rationale |
|---|----------|--------|-----------|
| D1 | Cycle 2 is the **deferred scope from cycle-1 OQ2** — admin, charts, wallet/QR, members/categories/tags/settings, and the repo-wide ladder sweep | Resolved (brief) | Named explicitly as the follow-up in cycle-1 OQ2a + Final Outcome + Future Improvements. |
| D2 | **Reuse the cycle-1 substrate verbatim** — ladder, additive `Table stackOnMobile` (+ `data-label` convention), coarse-pointer 44px on primitives, the `mobile` Playwright project | Resolved (planner, evidenced) | The substrate is committed (`fff2450`); README §Responsive documents it. Cycle 2 applies, never re-authors. |
| D3 | **Audit finding:** the deferred surfaces mostly reflow already; the concentrated defects are wide admin/wallet tables (scroll-only), `TimeSeriesBarChart` label collision, bespoke sub-44px controls (`.revealBtn`/`.sortButton`), and the off-ladder strays | Resolved (planner, evidenced) | Per-area audit above with file refs (`AdminUserTable` 8-col, `charts.module.css` no breakpoint, `BankAccountsTable` 5-col + 28px reveal, grep of off-ladder `@media`). |
| D4 | The **real charts** are `RankedBarChart`/`TimeSeriesBarChart`/`KpiTile`(+`KpiRow`) under `src/components/ui/charts/` — **CSS/flex, not SVG** | Resolved (planner, verified) | Confirmed the actual file names/paths (cycle-1 notes were right on names); the fix is a flex/overflow tweak, not a viewBox rewrite. |
| D5 | **No API/business-rule change**; presentation-only; admin privacy boundary preserved | Resolved (planner) | Nothing in the audit needs a contract change; keeps blast radius to CSS/layout/markup. |
| D6 | ui-designer leads the chart density pattern (`dataviz` skill) + primitive strays; web-implementer applies the ladder + `stackOnMobile` adoptions + sweep; web-test-engineer verifies (+ any new mobile E2E) | Resolved (planner) | Matches cycle-1 ownership + CLAUDE.md domains. |
| D7 | Scope split (OQ1); admin mobile-table strategy (OQ2); `TimeSeriesBarChart` density approach (OQ3); admin/stats/wallet mobile E2E now vs defer (OQ4); bespoke touch-target treatment (OQ5) | **Resolved (user, 2026-07-18)** | Checkpoint held; all at the recommended option (see D8). |
| D8 | **OQ1=a** split into **2a** (admin/wallet tables adopt `stackOnMobile` + repo-wide ladder sweep + bespoke touch-target fixes + admin/wallet mobile E2E) and **2b** (charts — `TimeSeriesBarChart` density + stats/home tuning, `dataviz`). **OQ2=a** adopt `stackOnMobile` on `AdminUserTable`/`BankAccountsTable`/`GrantHistoryTable` (per-consumer additive; `data-label` from the `*.columns.*` header keys). **OQ3=a** (2b) min column width + scroll the plot+axis together below the density threshold, with day-bucket label thinning. **OQ4=a** add targeted phone-viewport E2E for the admin user table + wallet table on the existing `mobile` project; charts get RTL component tests. **OQ5=a** local `@media (pointer: coarse)` min-size rules on `.revealBtn` + admin `.sortButton` (keep compact fine-pointer desktop). | **Resolved (user, 2026-07-18)** | Every OQ at the recommended option. **2a is the active sub-cycle; 2b deferred to its own cycle.** |

## Progress Log

- **2026-07-18** — Drafted the cycle-2 plan. Audited the deferred surfaces at
  narrow viewports: **admin** (`AdminUserTable.tsx` = 8-col plain `Table`,
  horizontal-scroll on phones + a bespoke `.sortButton`; `admin.module.css`
  filter bar/dash grid/console/dialogs — grids collapse via `auto-fit`, dialogs
  inherit the responsive `Dialog`; sensitive dialogs OK); **charts**
  (`src/components/ui/charts/*` — verified the real names/paths; CSS/flex not SVG;
  `charts.module.css` has NO breakpoint; `KpiRow`/`RankedBarChart` read fine, but
  `TimeSeriesBarChart` collides axis/cap labels when buckets are dense on phones —
  the one real chart defect); **wallet** (`BankAccountsTable.tsx` = 5-col plain
  `Table`, horizontal-scroll + a 28px bespoke `.revealBtn`; `QrDialog` scales fine,
  account block a minor 320px tidy); **members/categories/tags/settings** (settings
  verify-only; the smaller tables lower-priority); and the **off-ladder sweep**
  (grepped `@media`: strays remain in `AuditTimeline` 34, `AssignExpenseDialog` 32,
  `dashboard` 60, `Layout` 32 [primitive], `Dialog`/`Form` 30 [max-width], and the
  `M4/M5/M6` showcases). Confirmed MSW covers admin/stats/bank-accounts/QR (20
  handler matches) so mobile E2E is feasible unchanged, and that the cycle-1 E2E
  net covers only the ledger loop + header (admin/stats/wallet unguarded). Five
  Open Questions recorded with recommendations. **Status: awaiting checkpoint on
  OQ1–OQ5.**
- **2026-07-18 (checkpoint — OQs resolved)** — User accepted every recommendation
  (D8): **split into 2a + 2b** (OQ1=a); admin/wallet tables adopt `stackOnMobile`
  (OQ2=a); `TimeSeriesBarChart` = min-width + scroll-the-plot, in **2b** (OQ3=a);
  add targeted admin/wallet mobile E2E (OQ4=a); local coarse-pointer rules on the
  bespoke controls (OQ5=a). **2a cleared for the build** — no ui-designer novelty
  (reuses the committed cycle-1 `stackOnMobile` pattern), so: web-implementer
  (adopt `stackOnMobile` on `AdminUserTable`/`BankAccountsTable`/`GrantHistoryTable`
  with i18n `data-label`s; repo-wide off-ladder sweep; coarse-pointer rules on
  `.revealBtn`/`.sortButton`) → web-test-engineer (admin + wallet mobile E2E specs)
  → web-code-reviewer. **2b (charts)** starts its own design-led cycle afterward.

- **2026-07-18 (2a build — web-implementer)** — Implemented sub-cycle **2a**
  (tables + sweep + touch targets); **2b (charts) untouched**. `gitnexus_impact`
  (upstream) on the three tables all returned **LOW** risk with a single
  page-consumer each (`AdminUserTable`→`AdminUsersPage`, `BankAccountsTable`→
  `WalletPage`, `GrantHistoryTable`→`AdminUserDetailPage`), confirming A8; the
  `Table` primitive was **not** touched (additive per-consumer only).
  - **2a.1 off-ladder sweep (value-alignment, kept existing `max-width`
    direction per the conservative directive):** `AuditTimeline.module.css`
    34→30, `AssignExpenseDialog.module.css` 32→30, `dashboard.module.css`
    `.homeGrid` 60→64, `M4Showcase` 40→48 & 34→30, `M5Showcase` 32→30,
    `M6Showcase` 60→64. A repo grep confirms every remaining `min/max-width` rem
    query now lands on a ladder value (30/48/64) except the sanctioned AppShell
    `63.99` and the **primitive** strays left to the ui-designer per 2a.1
    (`Layout` 32 `min-width`; `Dialog`/`Form` 30 `max-width` — value already on
    ladder). README §Responsive "consolidate strays" note updated to mark the
    sweep complete. *Deviation from 2a.1's "mobile-first re-author" phrasing:*
    kept each query's pre-existing `max-width` layout-collapse direction (only
    the threshold value moved) — this follows the orchestrator's explicit
    conservative "value alignment; keep existing direction" instruction and
    avoids regression risk on real feature surfaces; no query introduces a new
    off-ladder value.
  - **2a.2/2a.3 `stackOnMobile` adoption (OQ2a):** added `stackOnMobile` to
    `AdminUserTable`, `GrantHistoryTable`, and `BankAccountsTable` with i18n
    `data-label`s from the existing `*.columns.*` / `wallet:table.*` /
    `admin:detail.grantHistory.*` header keys. Admin-users: username =
    `scope="row"` card title; tier/role/status/createdAt/grantCount(numeric)/
    lastGrantAt labeled; view-action label-less. Wallet: bank+BIN = card title;
    account#/holder/default labeled; reveal toggle kept inline; Premium actions
    label-less. Grant-history has no natural row-header, so all 7 value cells
    carry `data-label` (no card title) — reads as a fully-labeled card. Verified
    by running the app (admin `admin`/Premium): each reflows to labeled cards at
    360px and returns to the full table at 900px.
  - **Refinement (necessary, recorded):** added `captionHidden` to
    `AdminUserTable` + `BankAccountsTable`. Both had a **visible** `<caption>`
    that, in card mode, shrink-to-fit-wrapped one word per line (a defect the
    reference adopter `ExpensesTable` avoids via `captionHidden`). Both surfaces
    carry a visible heading (wallet `PageHeader`; admin console `<h1>` + active
    "Người dùng" tab), so hiding the caption keeps the accessible name and is the
    design-system-sanctioned use of `captionHidden`. `GrantHistoryTable` already
    had it.
  - **2a.4 members/categories/tags/settings:** verify-only, no restructure.
    These are fewer-column plain `Table`s; the `Table` `.scroll` wrapper
    (`overflow-x: auto`) keeps any overflow inside the table box so the **page**
    never scrolls sideways, and the audit found no off-ladder query in them — no
    code change needed.
  - **2a.5 bespoke touch targets (OQ5a):** added `@media (pointer: coarse)`
    min-size rules — `.revealBtn` (`BankAccountsTable.module.css`) grows 1.75rem→
    2.75rem square; admin `.sortButton` (`admin.module.css`) gets
    `min-height: 2.75rem` — both ≥44px effective on touch only; fine-pointer
    desktop sizing unchanged. No markup change; sort glyph/`aria-sort`,
    default-marker star+text, and badge status cues remain color-independent
    through the reflow (R10).
  - **Test touched (permitted):** `adminUsersPage.test.tsx` sort test now queries
    the header sort button with `{ hidden: true }` — under `stackOnMobile` the
    column-header row is `display:none` at base and jsdom never applies the
    `min-width: 30rem` restore, so the (still-functional) sort control is
    technically hidden there. Logic/control unchanged.
  - **Verification (PATH-prefixed):** `pnpm lint` clean (6 pre-existing
    fast-refresh warnings, none in touched files); `pnpm exec tsc -b` exit 0;
    `pnpm build` OK; `pnpm test` **777 passed / 93 files**; `pnpm test:e2e`
    **6 passed** (chromium + mobile ledger-loop + header-responsive) — unaffected,
    as 2a touches only non-ledger screens.
  - **Open Question raised — see OQ6 below.**
  **Status: 2a implementation complete + verified; awaiting web-test-engineer
  (admin/wallet mobile E2E, OQ4) → web-code-reviewer.**

- **2026-07-18 (2a TEST — web-test-engineer, OQ4a)** — Added the targeted
  phone-viewport E2E guarding the two tables 2a restructured; **no product code
  touched** (test harness + specs only), **no chart tests** (2b). Two new specs
  on the existing `mobile` Playwright project (Pixel 5, 393px < `sm` 30rem →
  card-stack active), reusing the harness + MSW handlers unchanged and honoring
  OQ3a isolation (client-side nav via `gotoNav`, no mid-flow `page.reload()`).
  - **`e2e/admin-users-responsive.spec.ts`** — logs in as the seed ADMIN
    `admin`, navigates primary-nav → /admin → the console Users tab, and proves
    `AdminUserTable`'s `stackOnMobile`: (1) `AdminUserTable_PhoneViewport_`
    `RendersRowsAsLabeledCards` — the `nguyen.van.a` row (newest → row 1 under
    default createdAt desc) renders as a card with the username `rowheader`
    title, every value cell carries its `admin:users.columns.*` `data-label`,
    the tier label is actually drawn (computed `::before` content), the
    tier/role/status icon+text badges show, `columnheader` count is 0 (card mode
    active — the OQ6 header-hiding), and the page does not scroll sideways;
    (2) `AdminUserCard_PhoneViewport_StillNavigatesToUserDetail` — the per-card
    "Xem …" action still routes to `/admin/users/uuid-nguyen-a` and the detail
    metadata renders.
  - **`e2e/wallet-responsive.spec.ts`** — logs in as `admin` (seeded PREMIUM
    with Vietcombank default + Techcombank), navigates primary-nav → /wallet, and
    proves `BankAccountsTable`'s `stackOnMobile`: (1) `BankAccountsTable_`
    `PhoneViewport_RendersAccountsAsLabeledCards` — the Vietcombank card
    (bank+BIN `rowheader` title, `wallet:table.*` `data-label`s, holder label
    rendered via `::before`, holder value + default badge, `columnheader` 0, no
    sideways scroll); (2) `BankAccountRevealToggle_PhoneViewport_IsPresent`
    `AndOperable` — the `aria-pressed` reveal (eye) toggle starts collapsed
    (`•••• 4567`), is reachable by accessible name, and on activation flips to
    the "hide" affordance (`aria-pressed=true`) and reveals the grouped number
    (`0071 0012 3456 7`).
  - **Harness/config touched (permitted):** `e2e/fixtures/copy.ts` gains the
    `admin` + `wallet` vi-VN namespaces (selector source of truth); reused the
    existing `login(page, { username })` (no new variant needed) + `gotoNav()`.
    **Placement** = the phone-only `-responsive.spec.ts` convention: the
    `chromium` project's `testIgnore` widened from `/header-responsive\.spec\.ts$/`
    to `/-responsive\.spec\.ts$/` (one declarative place; desktop never runs the
    phone specs). `e2e/README.md` updated (projects table + phone-only list +
    convention).
  - **Verification (PATH-prefixed, exact outputs):** `pnpm test:e2e` **10 passed**
    (run twice, no flakiness) — per-project: **chromium 1** (ledger-loop),
    **mobile 9** (ledger-loop + 4 header-responsive + 2 admin-users-responsive +
    2 wallet-responsive). `pnpm exec tsc -p e2e/tsconfig.json --noEmit` **exit 0**.
    `pnpm lint` **clean** (6 pre-existing fast-refresh warnings, none in touched
    files). `pnpm test` (Vitest) **777 passed / 93 files** (untouched — confirmed).
    No product bug found. **Existing ledger-loop + header specs and their
    selectors were not modified.**
  **Status: 2a TEST complete + all suites green; ready for web-code-reviewer.**

## Final Outcome

**Sub-cycle 2a delivered (2026-07-18); 2b (charts) pending as its own cycle.**
Split confirmed at the checkpoint (OQ1=a). **2a** — reviewed **APPROVE, 0
blocking** — shipped: `AdminUserTable`, `GrantHistoryTable`, and
`BankAccountsTable` adopt the additive `Table stackOnMobile` card-stack (i18n
`data-label`s from the existing column-header keys; `Table` primitive + its 33
other consumers byte-for-byte unchanged); the repo-wide off-ladder sweep (feature
+ showcase files now on 30/48/64rem; the one remaining `Layout.module.css` stray
is the ui-designer's primitive carve-out); local `@media (pointer: coarse)` 44px
rules on the two bespoke controls (`.revealBtn`, admin `.sortButton`); and two new
phone-viewport E2E specs (`admin-users-responsive`, `wallet-responsive`) on the
`mobile` project. Verification: lint clean, `tsc -b` 0, Vitest **777/777**, build
0, **E2E 10 passed** (chromium 1 + mobile 9), live-app check at 360px. OQ6 (admin
sort hidden on phone) **accepted** — no change; the mobile sort control + the
`GrantHistoryTable` card-title parity are backlogged below. Presentation-only — no
API/DTO/business-rule change; admin privacy boundary intact. **2b (charts —
`TimeSeriesBarChart` density + stats/home tuning, OQ3=a) begins next as a
design-led `dataviz` cycle.**

## Future Improvements

- **Mobile sort affordance for `AdminUserTable`** (OQ6b) — a below-`sm` sort-by
  `Select` in the filter bar driving the same URL `sort`/`dir` state, so the
  card-stacked admin list is sortable on a phone. Needs a ui-designer pattern.
- **`GrantHistoryTable` card title** (reviewer nit) — promote a column (e.g. the
  timestamp or action) to `scope="row"` so its phone cards get a per-card anchor,
  matching the admin-users + wallet cards.
- **Container queries** for chart + card reflow — where a chart/card should reflow
  on its own container width rather than the viewport (e.g. a `RankedBarChart`
  inside a narrow dashboard panel vs full-width), adopt `@container` once the
  viewport ladder proves insufficient.
- **Broad admin/stats/wallet E2E coverage** (beyond the targeted mobile specs) — a
  proper functional E2E suite for the admin console, stats, and wallet flows, which
  these areas lack entirely today; a separate initiative, not a polish cycle.
- **Visual-regression + `axe` a11y in E2E** (carried from cycle-1 OQ6c) —
  Playwright screenshot snapshots + `@axe-core/playwright` to guard the reflow,
  now covering the cycle-2 surfaces too.
- **Enforceable breakpoint tokens** (cycle-1 OQ1b) if the comment-convention ladder
  drifts in practice — add `postcss-custom-media` (a build-only dep, an Open
  Question when raised).
- **Extract a chart scroll/label-density helper** if more time-series surfaces
  appear, so the OQ3 pattern is not re-implemented per consumer.
