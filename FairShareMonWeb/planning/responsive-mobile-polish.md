# Responsive / Mobile-First Polish — cycle 1

## Objective

Run the **first UX/UI improvement cycle** now that the E2E safety net is live
(`planning/e2e-testing.md` — the M2/M4/M5 ledger loop is regression-protected).
The theme of this cycle is **responsive / mobile polish**: audit the SPA at
narrow viewports, fix what genuinely breaks or reads poorly on a phone, and
**standardize the breakpoint discipline** so future work reflows consistently
instead of inventing a new media-query threshold per component.

Concretely this cycle delivers:

1. A **documented, standardized breakpoint ladder** (reusing the existing
   `--fs-*` token language) that consolidates today's ad-hoc thresholds and gives
   the design system + features one vocabulary to reflow against.
2. **Targeted responsive fixes** on the highest-breakage surfaces — starting with
   the **app-shell header** (the clearest mobile break) and the **dense ledger
   screens** (expenses list + filter bar, event detail + balance/expenses).
3. A **touch-target + a11y pass** so the reflow does not regress focus,
   `prefers-reduced-motion`, or hit-target comfort on touch.
4. A **responsive-verification method** wired into the quality bar so "done" is
   provable, not eyeballed.

This is deliberately **cut to one cycle**. The audit below shows the app was
built mobile-first by the ui-designer and is *already largely responsive*; the
work is polish + standardization + closing specific gaps, **not** a from-scratch
responsive rebuild. Lower-traffic/lower-density areas (admin suite deep reflow,
stats-chart deep polish, wallet/QR, settings) are explicitly deferred to a
follow-up cycle — see the cut line in OQ2.

## Background

### The locked substrate (do not fork)

- **Styling system** (`FairShareMonWeb/CLAUDE.md`, `src/styles/README.md`): CSS
  Modules + CSS-custom-property tokens (`--fs-*`) + Radix primitives. Components
  consume **semantic** tokens (`--fs-color-*`, `--fs-space-*`, …), never raw
  ramps. **Never fork a parallel style system.** `tokens.css` + `global.css` are
  owned by the **ui-designer**; feature CSS modules compose primitives.
- **A11y baseline** (`src/styles/README.md` §Accessibility, CLAUDE.md): global
  `:focus-visible` ring (2px jade), status/finance meaning is icon+text/sign
  never color-alone, semantic landmarks + skip link in `AppShell`, global
  `prefers-reduced-motion` neutralizer in `global.css` (lines 136–145), long
  Vietnamese text gets `overflow-wrap`/generous line-heights.
- **Regression net**: `e2e/ledger-loop.spec.ts` (Playwright, MSW-mocked, vi-VN +
  Asia/Ho_Chi_Minh pinned) covers login → member → expense+shares → event →
  assign → close → balance=0. It selects **role/label-first** with a few curated
  `data-testid`s (`event-balance-row`, `balance-amount`, `event-balance-total` on
  `EventBalanceTable.tsx`). **Restructuring markup on covered screens can shift
  those selectors** — a load-bearing constraint for this cycle.

### Current responsive state — the audit (with file refs)

**There is no breakpoint system.** `tokens.css` defines layout tokens
(`--fs-layout-max: 72rem`, `--fs-layout-narrow: 26rem`, `--fs-header-h`,
`--fs-sidebar-w`) but **no breakpoint tokens**. Media queries are hand-written
per component at inconsistent thresholds (grep of `src/**`):

| Threshold | Where |
|-----------|-------|
| `max-width: 30rem` | `Dialog.module.css:123` (footer stacks), `Form.module.css:34` (actions stack) |
| `min-width: 32rem` | `Layout.module.css:91` (DescriptionList → two-col) |
| `max-width: 32rem` | `AssignExpenseDialog.module.css:107`, `M5Showcase.module.css:251` |
| `max-width: 34rem` | `AuditTimeline.module.css:132`, `M4Showcase.module.css:382` |
| `max-width: 40rem` | `ShareEditor.module.css:93` (row grid → stacked areas), `M4Showcase.module.css:161` |
| `max-width: 60rem` | `dashboard.module.css:40` (homeGrid → 1col), `M6Showcase.module.css:271` |
| `min-width: 64rem` | `AppShell.module.css:101` (nav ↔ hamburger swap) |

Six distinct thresholds, mixing `min-` and `max-` conventions. Note CSS custom
properties **cannot** be used inside a media-query *condition* natively, so
"tokenizing" breakpoints means a documented named ladder (+ optionally a
build-time `@custom-media` via PostCSS) — this is a genuine stack question (OQ1).

**App-shell header — the clearest mobile break** (`AppShell.tsx` +
`AppShell.module.css` + `AppShellLayout.tsx`). `.headerInner` (lines 35–43) is a
**fixed-height** (`--fs-header-h` = 3.5rem) flex row, `gap: --fs-space-4`, holding
brand + hamburger + inline-nav + `.actions`. `.actions` (lines 133–139) is
`margin-left:auto; flex:none;` and **does not wrap or collapse**. In
`AppShellLayout.tsx` it renders **four controls**: `LanguageToggle` (segmented,
2 segments) + `ThemeToggle` (segmented, 3 segments) + an account `Button` (ghost,
username) + a logout `Button` (secondary). On a 360–390px phone: brand + hamburger
+ two segmented pill controls + two text buttons overflow the fixed-height row
horizontally. **This is the single highest-impact defect** and it is on every
authenticated screen. No responsive handling exists for the header actions today.

**Tables — horizontal-scroll only** (`Table.module.css:9–16`). `.scroll` wraps
every table in `overflow-x:auto` so a wide table never forces the *page* to scroll
sideways — good — but on a phone the user must scroll the table box sideways and
**loses the row-header (member/expense name) context** while doing so. The
densest offenders:

- **Expenses list** (`ExpensesTable.tsx:40–49`): **8 columns** — name · payer ·
  category · total · time · settled · event · actions. On a phone this is a long
  sideways scroll with no card affordance.
- **Admin user table** (`admin.module.css` §3; `features/admin/**`): sortable
  multi-column table + filter bar + pagination.
- **Event balance table** (`EventBalanceTable.module.css`; covered by E2E):
  member · đã ứng · phải gánh · cân bằng (+ sum-to-zero footer) — 4 numeric-heavy
  columns.

Whether to keep horizontal-scroll (cheap, pure CSS, zero selector risk) or add a
**card-stack reflow** for the worst tables (better UX, restructures markup, E2E
selector risk) is a real decision — OQ3.

**Touch targets.** `Button.sm` = **2rem/32px** height (`Button.module.css:60–64`),
used for table row-action icon buttons. Base controls/`TextField` = 2.5rem/40px.
Segmented control segments = **1.85rem/~30px** height, `min-width:2rem`
(`Controls.module.css:11–17`). All clear WCAG 2.5.8 (AA, 24px) but sit below the
44px mobile comfort target (WCAG 2.5.5 AAA); the 32px row-action icon buttons are
the tightest for touch. A polish item, not a hard failure — OQ4.

**Already responsive (verified good — leave alone / light touch):**

- Filter bars use `flex-wrap` with `flex:1 1 <basis>; min-width` — expenses
  (`ExpenseFilterBar.module.css`), admin (`admin.module.css:236–249`). They wrap
  into stacked rows on narrow (functional, just tall).
- Share editor collapses its 4-col grid to stacked grid-areas at 40rem
  (`ShareEditor.module.css:93–116`).
- Dialog is `width: calc(100vw - 2*space-4)`, `max-height` capped, scrolls, footer
  stacks at 30rem (`Dialog.module.css`). Form actions stack at 30rem.
- `DescriptionList` stacks term/value below 32rem (`Layout.module.css:91–105`);
  `PageHeader` wraps actions below the title (`Layout.module.css:26–62`).
- Grids collapse: dashboard `homeGrid` → 1col at 60rem
  (`dashboard.module.css:40`), admin `dashGrid` + quick-links use `auto-fit/auto-fill
  minmax()` (`admin.module.css:119–123`, `dashboard.module.css:113–117`).
- Nav collapses to the Radix drawer below 64rem (`AppShell.module.css:101–108`,
  M1) with focus trap / Escape / reduced-motion honored.
- QR frame is a max-width, aspect-ratio well that scales down
  (`QrDialog.module.css:27–44`).

**Net:** the reflow foundations are solid. The concentrated defects are (a) the
header actions row, (b) wide-table ergonomics on phones, (c) an inconsistent
breakpoint vocabulary, and (d) sub-optimal touch targets on dense controls.

### Design ownership (this is materially a design cycle)

Per CLAUDE.md, `tokens.css` + `global.css` + `src/components/ui/*` are the
**ui-designer's** domain. This cycle therefore leads with the **ui-designer**:
define the breakpoint ladder, decide the mobile-table pattern and header-actions
pattern, and update the affected **primitives** (`Table`, `AppShell`, control
sizing tokens) + the showcase specs. The **web-implementer** then applies the
ladder to feature-local CSS modules and wires any structural change into the
feature components. The **web-test-engineer** adds the responsive verification.
Hand-off points are called out per step in the Implementation Plan.

## Requirements

### Functional — standardization

- **R1** — Establish a single, documented **breakpoint ladder** as the cycle's
  vocabulary (exact values + `min-`/`max-` convention resolved in OQ1), recorded
  in `tokens.css` (as a documented comment block and/or `@custom-media` if a
  PostCSS approach is adopted) and `src/styles/README.md`. Every media query
  touched in this cycle uses a ladder value; no new one-off thresholds are
  introduced.
- **R2** — Consolidate the existing ad-hoc thresholds **on the surfaces this
  cycle touches** to the nearest ladder value (a full repo-wide sweep of
  untouched files is a Future Improvement, not a blocker).

### Functional — the header fix (highest priority)

- **R3** — The app-shell header must not overflow horizontally at **320px** and
  up. The trailing actions (`LanguageToggle`, `ThemeToggle`, account, logout) get
  a mobile treatment (relocate into the drawer, and/or icon-only/condensed) —
  exact pattern per OQ5 — while at/above the nav breakpoint the current inline
  layout is unchanged.
- **R4** — Whatever relocates must stay reachable and labeled: the language/theme
  toggles and logout remain operable on mobile with visible focus, correct
  landmarks, and localized labels (no orphaned controls, no loss of the theme/
  locale affordance on phones).

### Functional — dense ledger screens

- **R5** — Expenses list, event detail (balance + expenses sections), and their
  filter/share surfaces are usable at 320–430px: no page-level horizontal scroll,
  no clipped controls, comfortable tap targets, readable money/dates. The
  mobile-table strategy (horizontal-scroll vs card-stack) is resolved in OQ3 and
  applied at least to the **expenses list** (8-col, the worst case).
- **R6** — Any covered screen keeps the E2E contract intact: the balance table's
  `data-testid`s (`event-balance-row`, `balance-amount`, `event-balance-total`)
  and the role/label selectors the ledger-loop spec relies on must still resolve
  after reflow. Structural changes to covered screens run **impact analysis
  first** (see Impact Analysis).

### Non-functional — a11y (must not regress)

- **R7** — Touch targets: interactive controls in the reflowed surfaces meet the
  agreed minimum (OQ4). `:focus-visible` rings, keyboard nav, and Radix dialog
  focus management are preserved; `prefers-reduced-motion` continues to neutralize
  any new transition.
- **R8** — Status/finance meaning stays icon+text/sign, never color-alone, through
  every reflow; long Vietnamese strings keep `overflow-wrap` and never force
  sideways scroll.

### Non-functional — quality bar + verification

- **R9** — `pnpm lint`, `tsc -b`, `pnpm build`, `pnpm test` (Vitest) stay green.
- **R10** — A **responsive-verification method** is defined and run: at minimum a
  documented manual viewport checklist; and per OQ6, optionally a Playwright
  **mobile-viewport project** exercising the ledger-loop (and/or a header-overflow
  assertion) added to the existing harness.
- **R11** — **Purely additive to behavior**: no API-contract change, no business-
  rule change (closed-event immutability, tier gates, ownership 404s all
  unchanged), no new runtime dependency unless an OQ explicitly approves one
  (PostCSS custom-media tooling in OQ1 is the only candidate, and it is
  build-only).

## Open Questions

> Each has a **Recommended** option. These are the checkpoint decisions.

- **OQ1 — Breakpoint ladder: which values, which convention, and do we adopt
  build tooling to name them?**
  - Today's thresholds cluster around ~30–34rem (phone/large-phone), ~40rem
    (small tablet), ~60–64rem (tablet/desktop).
  - **(a, Recommended)** Adopt a **3-stop mobile-first `min-width` ladder**:
    `sm 30rem (480px)`, `md 48rem (768px)`, `lg 64rem (1024px)` — documented as a
    named comment block in `tokens.css` + `README.md`, authored as raw
    `@media (min-width: 48rem)` in CSS (no new tooling). `lg 64rem` already matches
    the nav-collapse breakpoint, so the shell stays consistent. Reflow-on-touched-
    files consolidates the 32/34/40/60rem one-offs toward these. Trade-off: values
    live in a comment convention, not an enforceable token (CSS can't interpolate a
    custom property into a media condition), so discipline is by review not by the
    compiler.
  - **(b)** Same ladder **but add `postcss-custom-media`** (+ a small PostCSS
    config) so breakpoints are real named tokens (`@media (--fs-bp-md)`) enforced
    at build time. Cleaner and drift-proof, **but it is a new build dependency** —
    per the locked-stack rule that is itself a decision, and it touches the Vite
    build config.
  - **(c)** Keep `max-width` desktop-down authoring to match the majority of
    existing queries. Rejected in the recommendation because the codebase and
    tokens are explicitly mobile-first; mixing conventions is the current mess.
  - *Impact:* the ladder values, the authoring convention, and whether
    `vite.config`/PostCSS gains a dependency.

- **OQ2 — Scope cut line: what is in cycle 1 vs deferred?**
  - **(a, Recommended)** **In:** the breakpoint ladder (R1/R2 on touched files);
    the **app-shell header** fix (R3/R4); the **expenses list + filter bar** and
    **event detail (balance + expenses sections)** responsive pass (R5); the
    **touch-target pass** on primitives + those surfaces (R7); the verification
    wiring (R10). **Deferred to cycle 2:** admin suite deep reflow (tables/
    dashboards/dialogs), stats-chart deep responsive polish, wallet/QR, members/
    categories/tags/settings fine-tuning, and the repo-wide breakpoint sweep of
    untouched files. Rationale: the header is universal and the ledger screens are
    the highest-traffic + highest-density + already E2E-covered (safest to change
    under the net). Admin/stats are lower traffic and chart-heavy (a distinct
    concern deserving its own cycle).
  - **(b)** Also pull the **admin user table + stats charts** into cycle 1. More
    coverage in one pass, but ~doubles the surface, adds chart-reflow (a separate
    discipline), and stretches a "polish" cycle into a broad rework.
  - **(c)** Header-only, minimal cycle. Fastest/safest, but leaves the wide-table
    phone ergonomics (the second-most-cited defect) unaddressed.
  - *Impact:* the size of the Implementation Plan and which features the
    web-implementer touches.

- **OQ3 — Mobile wide-table strategy: horizontal-scroll (status quo) or
  card-stack reflow?**
  - **(a, Recommended — hybrid)** Keep the `Table` primitive's `overflow-x` scroll
    as the **default** (zero risk, pure CSS), and add an **opt-in card-stack
    reflow** applied first to the **expenses list** (the 8-col worst case): below
    the `sm` breakpoint each row renders as a labeled stacked card (label:value
    pairs) instead of a sideways-scrolling row. Ship it as a documented `Table`
    capability (a `stackOnMobile`-style variant or a feature-local pattern per the
    showcase convention) so other tables can adopt it later. Trade-off: this
    **restructures the covered-area markup pattern** → run impact analysis; the
    expenses list is *not* directly asserted by the ledger-loop spec (it drives
    create → detail), but the balance table is — so keep the **balance table on
    horizontal-scroll for now** to avoid touching E2E selectors.
  - **(b)** Card-stack **all** dense tables now (expenses, admin, balance). Best
    phone UX, but the balance table is E2E-covered and admin is deferred by OQ2 —
    higher risk and scope.
  - **(c)** Horizontal-scroll everywhere, add only scroll affordances (an edge
    fade / "scroll for more" hint). Cheapest and zero markup risk, but leaves the
    8-col expenses list awkward on phones.
  - *Impact:* whether `Table` gains a reflow variant, and which tables adopt it.

- **OQ4 — Touch-target minimum for this cycle.**
  - **(a, Recommended)** Adopt **44px minimum for primary touch targets** on
    reflowed mobile surfaces (bump `Button.sm` row-actions and segmented-control
    segments to ≥44px *effective* hit area via padding/min-size on touch/coarse
    pointers using `@media (pointer: coarse)`, keeping the compact desktop sizing
    for fine pointers). Meets mobile best practice without fattening the desktop
    UI. Trade-off: a per-pointer media query adds a little CSS to the primitives.
  - **(b)** Hold the current sizes (all already pass WCAG 2.5.8 AA 24px) and only
    fix spacing so adjacent targets don't crowd. Least change; leaves 32px icon
    buttons tight for thumbs.
  - **(c)** Global 44px min for all interactive controls regardless of pointer.
    Simplest rule, but visibly loosens dense desktop tables/toolbars.
  - *Impact:* whether primitive sizing gains coarse-pointer rules and the exact
    minimum.

- **OQ5 — Header actions on mobile: how do the toggles + account + logout adapt?**
  - **(a, Recommended)** **Relocate secondary actions into the existing nav
    drawer** on mobile: below the nav breakpoint the header keeps brand +
    hamburger only, and the drawer footer hosts the language/theme toggles +
    account link + logout (the drawer already has focus trap/Escape/restore). Reuses
    M1 infrastructure, guarantees no header overflow, and groups account/settings
    controls where mobile users expect them. Trade-off: touches `AppShell.tsx`
    structure (new drawer-footer slot) + `AppShellLayout.tsx` (pass the actions to
    it) → impact analysis; drawer content changes but its a11y contract is
    unchanged.
  - **(b)** Keep actions in the header but **condense to icon-only** on mobile
    (globe/theme glyph, avatar, exit icon) with `aria-label`s. Less structural
    change, but four icons + brand + hamburger is still tight at 320px and loses
    the text affordance.
  - **(c)** Let the actions `flex-wrap` onto a second header line on mobile
    (drop the fixed header height). Minimal code, but a two-row header eats scarce
    vertical space on phones and looks unpolished.
  - *Impact:* whether `AppShell` gains a drawer-footer/secondary-actions slot and
    how `AppShellLayout` wires it.

- **OQ6 — Responsive verification: add a mobile-viewport E2E project this cycle,
  or defer?**
  - **(a, Recommended)** Add a **Playwright mobile project** to the existing config
    (`projects: [chromium-desktop, { name: "mobile", use: devices["Pixel 5"] }]`)
    that re-runs the **ledger-loop spec** at a phone viewport **plus** a small new
    `e2e/header-responsive.spec.ts` asserting the header does not overflow (e.g.
    `scrollWidth <= clientWidth`) and the drawer exposes the relocated actions.
    Cheap (reuses the harness + spec + MSW), and it makes the header/ledger reflow
    regression-proof — the exact reason the E2E net was built first. Trade-off:
    ~2× E2E runtime for the mobile project; the ledger spec must be robust to the
    reflow (it is role/label-first).
  - **(b)** Defer viewport E2E to a follow-up; verify this cycle via a documented
    manual viewport checklist only. Faster to land, but the new responsive behavior
    then has no automated guard (regresses silently later).
  - **(c)** Add Playwright screenshot/visual-regression snapshots for the reflowed
    surfaces. Strongest visual guard, but snapshot maintenance is heavy and was
    already flagged as a separate Future Improvement in the E2E doc — premature for
    a polish cycle.
  - *Impact:* the `playwright.config.ts` `projects` array, a possible new
    `header-responsive.spec.ts`, and CI/local runtime.

## Assumptions

- **A1** — The app is genuinely mobile-first already (audit above); this is a
  polish/standardization cycle, so the plan is scoped to specific defects, not a
  rebuild. If the checkpoint reveals a broader expectation, re-scope via OQ2.
- **A2** — `tokens.css` / `global.css` / `src/components/ui/*` remain the
  ui-designer's domain; primitive changes (breakpoints, `Table` reflow, header,
  control sizing) are authored/owned there, then applied feature-side by the
  web-implementer.
- **A3** — The ledger-loop spec is **role/label-first** and does not assert the
  expenses-*list* markup shape (it drives create → detail), so a card-stack reflow
  of the expenses list does not break it; the **balance table is** asserted (via
  `data-testid`s) so it stays on horizontal-scroll this cycle (OQ3a).
- **A4** — No API/DTO/business-rule change is needed; this is presentation-only.
  Closed-event immutability, tier gates (`13003`/`13000-2`), and ownership 404s are
  untouched.
- **A5** — Target viewport floor is **320px** (small phone) through desktop; the
  existing `--fs-layout-max: 72rem` ceiling is unchanged.
- **A6** — MSW handlers + the dev-server E2E path (`pnpm dev --port 5199`,
  `VITE_ENABLE_MOCKS=true`) are reused unchanged for any new viewport spec (per the
  E2E doc); no mock changes needed.
- **A7** — `@media (pointer: coarse)` is an acceptable, well-supported mechanism to
  scope touch-target sizing to touch devices without bloating desktop (basis for
  OQ4a).

## Implementation Plan

> Files relative to `FairShareMonWeb/`. Steps are ordered design-first
> (ui-designer) → feature application (web-implementer) → verification
> (web-test-engineer). Exact behavior of steps 2/4/5/6 depends on the OQ
> resolutions; the plan states the recommended path and marks the branch points.

### 1. Breakpoint ladder (ui-designer) — R1/R2 · OQ1

- Add a documented **breakpoint ladder** block to `src/styles/tokens.css` (comment
  convention: `sm 30rem / md 48rem / lg 64rem`, mobile-first `min-width`) and a
  "Responsive / breakpoints" section to `src/styles/README.md` stating the values,
  the `min-width` convention, and "reflow against the ladder, never a new one-off."
- If **OQ1b**: add `postcss-custom-media` + a PostCSS config and define
  `@custom-media --fs-bp-{sm,md,lg}`; wire into the Vite build. (Only if approved.)
- No component change in this step — it is the vocabulary the later steps consume.

### 2. App-shell header responsive fix (ui-designer + web-implementer) — R3/R4 · OQ5

- **Impact analysis first** on `AppShell` / `AppShellLayout` (shell renders on every
  authenticated route).
- Recommended (**OQ5a**): extend `AppShell.tsx` with a **secondary-actions slot**
  rendered in the drawer footer on mobile (reuse the Radix drawer already in
  `AppShell.tsx`), and keep header actions inline at ≥`lg`. Update
  `AppShell.module.css` (`.headerInner`/`.actions`) so below `lg` the header is
  brand + hamburger only. Wire `AppShellLayout.tsx` to pass `LanguageToggle`,
  `ThemeToggle`, account link, and logout into the new slot.
- Preserve: skip link, landmarks, focus trap, `aria-expanded/controls`, localized
  labels (`common:nav.*`, `common:theme.*`, `common:locale.*`, `common:logout`,
  `common:account`). Update `M1`/AppShell showcase notes.
- If **OQ5b/c** is chosen instead: condense-to-icons or wrap-second-line variant in
  `AppShell.module.css` only (smaller structural change).

### 3. Mobile-table strategy in the `Table` primitive (ui-designer) — R5 · OQ3

- Recommended (**OQ3a**): add an **opt-in card-stack reflow** to
  `src/components/ui/Table/Table.tsx` + `Table.module.css` (e.g. a `stackOnMobile`
  prop/variant that, below `sm`, renders each row as a labeled stacked card using
  the column headers as inline labels; default stays `overflow-x` scroll). Document
  in the Table showcase (`StyleGuide.tsx` / M-showcase). Keep the API additive and
  backward-compatible so untouched tables are unaffected.
- Balance table stays on **horizontal-scroll** this cycle (E2E-covered — A3/OQ3a).

### 4. Expenses list + filter bar responsive pass (web-implementer) — R5 · OQ2a/OQ3a

- `features/expenses/components/ExpensesTable.tsx` (8 cols): adopt the step-3
  card-stack variant below `sm`; ensure name/total/settled read first in the
  stacked card; keep the `nameLink`, settled badge, and row actions reachable
  (touch sizing per step 7).
- `ExpenseFilterBar.module.css`: consolidate to ladder values; verify the
  ~9-control wrap is comfortable (stack full-width fields at `sm`, keep the clear
  action reachable). No logic change (URL-owned filter state stays as-is).
- `ExpensesPage.module.css` (`.tableWrap`) unchanged unless spacing needs a ladder
  tidy.

### 5. Event detail responsive pass (web-implementer) — R5/R6

- `EventDetailPage.module.css`: `.detailHeader`/`.detailActions` already wrap;
  consolidate any thresholds to the ladder; confirm the title + badges + range +
  action cluster stack cleanly at `sm` and the closed-event disabled controls stay
  visible.
- `EventExpensesSection` + `EventBalanceTable`: verify horizontal-scroll ergonomics
  at phone width; **do not restructure** the balance table markup (E2E). Add a
  scroll affordance only if OQ3c-style hinting is wanted; otherwise leave.
- `ShareEditor.module.css` already reflows at 40rem → retune that threshold to the
  ladder (`sm`/`md`) and confirm the stacked areas + running total read well.

### 6. Touch-target + a11y pass (ui-designer for primitives, web-implementer for features) — R7/R8 · OQ4

- Recommended (**OQ4a**): add `@media (pointer: coarse)` min-size/padding so
  `Button.sm` row-actions and `Controls` segments reach ≥44px effective hit area on
  touch; fine-pointer desktop sizing unchanged.
- Verify `:focus-visible`, keyboard order, Radix dialog/drawer focus management,
  and the global `prefers-reduced-motion` net still hold on every reflowed surface.
- Confirm no status/finance cue became color-only and long Vietnamese names still
  wrap (R8).

### 7. Responsive verification (web-test-engineer) — R10 · OQ6

- Author `e2e/RESPONSIVE-CHECKLIST.md` (or a section in `e2e/README.md`): the manual
  viewport pass (320/375/430/768/1024px) for header, expenses list, event detail.
- Recommended (**OQ6a**): add a **mobile project** to `playwright.config.ts`
  (`devices["Pixel 5"]`) re-running `ledger-loop.spec.ts`, and add
  `e2e/header-responsive.spec.ts` asserting the header does not overflow
  (`scrollWidth <= clientWidth`) at a phone viewport and the drawer exposes the
  relocated toggles/logout. Reuses the MSW dev-server harness unchanged.
- Component tests (Vitest + RTL) for any new `Table` reflow variant / `AppShell`
  slot: assert the card-stack renders labeled pairs, the drawer-footer actions
  render and are labeled, and reduced-motion/focus attributes are present.

### 8. Quality bar (all) — R9

- `pnpm lint`, `tsc -b`, `pnpm build`, `pnpm test` green; `pnpm test:e2e` green
  (desktop + mobile projects if OQ6a). Update `CLAUDE.md`/showcase docs where a
  primitive gained a prop or the shell gained a slot.

### Design spec (ui-designer) — handoff to the web-implementer

> Built 2026-07-18. The shared primitives + tokens + showcases are done; the
> feature-side application below is the web-implementer's step. Everything uses
> semantic `--fs-*` tokens; no new dependency; no business-rule change.

**1 — Breakpoint ladder (how to author queries).** Values live as a named comment
block in `src/styles/tokens.css` and a table in `src/styles/README.md`
(§Responsive / breakpoints). Mobile-first `min-width`, three stops only:

```css
@media (min-width: 30rem) { … } /* sm — 480px, large phone */
@media (min-width: 48rem) { … } /* md — 768px, tablet */
@media (min-width: 64rem) { … } /* lg — 1024px, desktop; = nav-collapse stop */
```

Author base styles for the smallest viewport; add capability upward. When you
touch a feature CSS module (e.g. `ExpenseFilterBar.module.css` 40rem,
`ShareEditor.module.css` 40rem, `EventDetailPage.module.css`), retune its stray
threshold to the **nearest ladder stop** — do NOT sweep untouched files. The one
sanctioned `max-width` is the AppShell header-actions hide (already written);
don't introduce new `max-width` queries.

**2 — `Table` opt-in card-stack (additive; how `ExpensesTable` adopts it).** The
`Table` primitive now takes `stackOnMobile`. Below `sm` each body row becomes a
labeled card; at/above `sm` it is the normal scrolling table. Default unchanged —
only the table you opt in is affected. The label for each value cell comes from a
`data-label` on that `<TableCell>` (the `data-label` convention; `data-*` as a
prop already typechecks — cf. `EventBalanceTable`'s `data-testid`). The
`scope="row"` header is the card title (no label); a cell without `data-label`
(the actions cell) shows no label.

For `features/expenses/components/ExpensesTable.tsx` (8 cols → keep the name as
the card title, drop the rest to labeled lines):

- Add `stackOnMobile` to the `<Table>`.
- Keep the name cell as `<TableHeaderCell scope="row">` (card title) — no label.
- Add `data-label={t(...)}` to each value `<TableCell>`, reusing the SAME i18n
  keys already used for the column headers so the label text matches exactly:
  payer → `data-label={t("expenses:list.payer")}`,
  category → `t("expenses:list.category")`,
  total → `t("expenses:list.total")` (keep `numeric`),
  time → `t("expenses:list.time")`,
  settled → `t("expenses:list.settled")`,
  event → `t("expenses:list.event")`.
- Leave the trailing `<TableCell actions>` with NO `data-label` (it drops to its
  own full-width line, left-aligned, in the card).
- Do NOT add `stackOnMobile` to `EventBalanceTable` — it stays on horizontal
  scroll this cycle (E2E-covered; its `data-testid`s and markup are untouched).
- Reviewable reference render: StyleGuide → "Responsive: thang breakpoint + bảng
  dồn thẻ trên di động".

**3 — `AppShell` `secondaryActions` slot (how `AppShellLayout` wires it).**
`AppShell` now takes `secondaryActions` (rendered pinned to the mobile drawer
footer, inside the Radix focus trap). Below `lg` the header shows brand +
hamburger only (the inline `actions` are CSS-hidden, fixing the 320–390px
overflow at its root); at/above `lg` `actions` sit inline unchanged. In
`src/routes/AppShellLayout.tsx`:

- Keep passing the four controls to `actions` (desktop header) exactly as today.
- ALSO pass them to `secondaryActions` for the mobile drawer footer. Build them
  as their own elements for that slot and pass the account + logout `Button`s
  with **`fullWidth`** (the footer stacks its children full-width; segmented
  toggles keep their intrinsic width). Same localized labels (`common:theme.*`,
  `common:locale.*`, `common:account`, `common:logout`); same `onChange`/`onClick`
  handlers. The account link must remain a real `<a>` (`Button asChild` + router
  `Link`) so the drawer auto-closes on its activation; logout stays a `<button>`
  and the toggles stay `<button>`s (they mutate in place, drawer stays open).
- No a11y wiring needed beyond that — the footer inherits the drawer's focus
  trap / Escape / focus-restore. Reviewable reference: StyleGuide AppShell
  (open the mobile menu to see the footer).

**4 — Coarse-pointer touch targets (already available; no caller change).**
`Button size="sm"` and the segmented `ThemeToggle`/`LanguageToggle` grow to a
≥44px effective hit area under `@media (pointer: coarse)` only; fine-pointer
desktop keeps the compact sizing. This is automatic wherever they already render
(table row-actions, header/drawer toggles) — the web-implementer does not opt in.
If a feature has its own bespoke small tap target, wrap it in a `Button size="sm"`
or add an equivalent `@media (pointer: coarse) { min-height: 2.75rem }` rule
against tokens rather than inventing a new size.

## Impact Analysis

- **Mostly low-risk, pure CSS/token/layout** — steps 1, 4 (filter-bar spacing), 5
  (threshold retune), and much of 6 touch only CSS modules + `tokens.css` comments;
  no logic, no markup restructure, no selector impact.
- **Higher-risk, structural — run `gitnexus_impact` before editing:**
  - **`AppShell.tsx` + `AppShellLayout.tsx`** (step 2, OQ5a): the shell wraps every
    authenticated route. Adding a drawer-footer actions slot changes shell
    structure. Blast radius = all routes; risk that focus-trap/landmark/label
    contracts regress. Mitigation: additive slot, preserve Radix wiring, cover with
    the new header-responsive spec.
  - **`Table.tsx` + `Table.module.css`** (step 3, OQ3a): a reflow variant is a
    primitive change consumed by every table. Mitigation: opt-in prop (default
    unchanged), so only `ExpensesTable` adopts it this cycle; every other table is
    byte-for-byte the same render path.
  - **`ExpensesTable.tsx`** (step 4): markup within the card-stack variant changes.
    **Not** asserted by the ledger-loop spec (create → detail path), so E2E-safe;
    still verify its own Vitest spec (`expensesPage.test.tsx`) after.
- **E2E-covered surfaces held stable:** `EventBalanceTable.tsx` keeps its markup +
  the three `data-testid`s (no restructure); the ledger-loop spec is role/label-
  first and continues to pass (re-run desktop + mobile projects to prove it).
- **No API / business-rule impact** (R11): presentation-only. Tier gates, closed-
  event immutability, ownership 404s, money/time formatting all unchanged.
- **Dependency impact:** none, unless **OQ1b** (add `postcss-custom-media`, a
  build-only dev dep) or **OQ6a** (mobile project reuses the already-approved
  `@playwright/test`, no new dep) are chosen. OQ1b is the only new-dependency
  decision and is flagged as such.
- **Bundle impact:** negligible (CSS deltas + optional additive component props).

## Decision Log

| # | Decision | Status | Rationale |
|---|----------|--------|-----------|
| D1 | This cycle is **responsive/mobile polish**, sequenced first after the E2E net | Resolved (brief) | The E2E doc names this the first UX cycle; the ledger loop is now regression-protected so restyling is safe. |
| D2 | **Audit finding:** the SPA is already largely mobile-first; work is polish + standardization + targeted fixes, not a rebuild | Resolved (planner, evidenced) | Media-query + primitive audit above (file refs). Sets the scope philosophy. |
| D3 | ui-designer leads (breakpoints, `Table`/`AppShell`/sizing primitives + showcases); web-implementer applies feature-side; web-test-engineer verifies | Resolved (planner) | `tokens.css`/`global.css`/`components/ui` are the ui-designer's domain per CLAUDE.md. |
| D4 | **No API/business-rule change**; presentation-only | Resolved (planner) | Nothing in the audit requires contract changes; keeps blast radius to CSS/layout. |
| D5 | Balance table stays **horizontal-scroll** this cycle (E2E-covered), reflow experiments target the non-asserted expenses list first | Resolved (planner) | Protects the ledger-loop selectors while still fixing the worst wide-table case. |
| D6 | Breakpoint ladder values/convention/tooling (OQ1); scope cut line (OQ2); mobile-table strategy (OQ3); touch-target minimum (OQ4); header-actions pattern (OQ5); viewport-E2E now vs later (OQ6) | **Resolved (user, 2026-07-18)** | Checkpoint held; all six at the recommended option (see D7). |
| D7 | **OQ1=a** 3-stop mobile-first ladder (sm 30rem / md 48rem / lg 64rem) as a documented comment convention, **no new build dep**. **OQ2=a** scope = breakpoint ladder + app-shell header + expenses list/filter bar + event detail (balance+expenses) + touch-target pass + verification; **defer** admin/stats/wallet/settings + repo-wide sweep to cycle 2. **OQ3=a** hybrid: `Table` keeps horizontal-scroll DEFAULT + adds an **opt-in** card-stack variant, applied first to `ExpensesTable`; `EventBalanceTable` stays on scroll. **OQ4=a** 44px touch targets via `@media (pointer: coarse)` only (compact desktop preserved). **OQ5=a** relocate header secondary actions into the nav-drawer footer on mobile. **OQ6=a** add a Playwright mobile-viewport project (re-runs the ledger-loop spec) + a new `e2e/header-responsive.spec.ts`. | **Resolved (user, 2026-07-18)** | User accepted every recommendation. |
| D8 | **Impact analysis (mandated pre-edit):** `AppShell` = **LOW** (2 callers: `AppShellLayout`, `StyleGuide`); `ExpensesTable` = **LOW** (1 caller: `ExpensesPage`); **`Table` = CRITICAL** (34 direct callers / 16 flows — every table in the app). **Mitigation (load-bearing):** the OQ3=a change to `Table` is **purely additive** — a new opt-in variant/prop; the horizontal-scroll default and existing markup are unchanged, so the 34 consumers are unaffected unless they opt in. Only `ExpensesTable` opts in this cycle; the E2E-covered `EventBalanceTable` explicitly does not. | **Resolved (user-acknowledged) — proceed additive-only** | Satisfies the "warn on HIGH/CRITICAL before editing" rule; the additive approach keeps the effective blast radius LOW. |

## Progress Log

- **2026-07-18** — Drafted the plan. Audited the current responsive state:
  `src/styles/{tokens,global,README}.css/md` (no breakpoint tokens; six ad-hoc
  media-query thresholds: 30/32/34/40/60/64rem), `AppShell.{tsx,module.css}` +
  `AppShellLayout.tsx` (fixed-height header, non-wrapping four-control actions row →
  **overflows on phones**, the top defect), `Table.module.css` (horizontal-scroll
  only; wide tables lose row-header context on mobile — expenses list is **8
  columns** per `ExpensesTable.tsx`), `Button`/`Controls` CSS (32px/30px touch
  targets below the 44px mobile comfort target), and confirmed the **already-
  responsive** surfaces (filter bars flex-wrap, `ShareEditor` grid collapse at
  40rem, `Dialog`/`Form` footer stacking at 30rem, `DescriptionList` at 32rem,
  dashboard/admin grids `auto-fit/minmax`, nav drawer at 64rem, QR aspect-ratio
  well). Cross-checked the E2E contract (`e2e/ledger-loop.spec.ts` role/label-first
  + `EventBalanceTable` `data-testid`s) to bound restructuring risk. Six Open
  Questions recorded with recommendations. **Status: awaiting checkpoint on
  OQ1–OQ6.**
- **2026-07-18 (checkpoint + impact analysis)** — User resolved all six OQs at the
  recommended option (D7). Ran `gitnexus_impact` (upstream) on the structural edit
  targets per the pre-edit mandate: `AppShell` LOW (2 callers), `ExpensesTable` LOW
  (1 caller), **`Table` CRITICAL (34 callers / 16 flows)**. Surfaced the CRITICAL
  `Table` risk to the user and confirmed the OQ3=a change is **additive/opt-in only**
  (scroll default + existing markup preserved → consumers unaffected unless opted in;
  only `ExpensesTable` opts in, `EventBalanceTable` stays on scroll), keeping the
  effective blast radius LOW (D8). **No open questions remain — cleared for the
  design-led build:** ui-designer (breakpoint ladder + additive `Table` variant +
  `AppShell` drawer-footer slot + coarse-pointer sizing + showcases) → web-implementer
  (feature-CSS application + `ExpensesTable` opt-in) → web-test-engineer (verify +
  mobile-viewport E2E project + header spec) → web-code-reviewer.

- **2026-07-18 (ui-designer — DESIGN build, steps 1/2(primitive)/3/6(primitive))** —
  Built the design-system layer for the cycle (feature-side application handed to
  the web-implementer via the new "Design spec (ui-designer)" subsection above):
  - **Breakpoint ladder (R1/R2, OQ1a):** named comment block in
    `src/styles/tokens.css` (sm 30rem / md 48rem / lg 64rem, mobile-first
    `min-width`, no build tooling) + a "Responsive / breakpoints" section in
    `src/styles/README.md` with the ladder table, the coarse-pointer note, the
    Table card-stack note, and the AppShell drawer-footer note.
  - **`Table` opt-in card-stack (R5, OQ3a — additive):** new `stackOnMobile` prop
    on `src/components/ui/Table/Table.tsx` (+ `Table.module.css`). Below `sm` each
    body row reflows into a labeled card via the `data-label` convention; at/above
    `sm` a `@media (min-width: 30rem)` block restores the real table model.
    Default render is byte-for-byte unchanged (D8 constraint honored) — no `.stack`
    class unless opted in.
  - **`AppShell` drawer-footer slot (R3/R4, OQ5a):** new `secondaryActions` prop on
    `src/components/ui/AppShell/AppShell.tsx` rendered pinned to the drawer footer
    (inside the Radix focus trap; closes on link activation only, so toggles stay
    open). `AppShell.module.css`: header `.actions` hidden below `lg` (via a
    `max-width: 63.99rem` companion — deliberately max-width so jsdom keeps the
    base `display:flex` and the existing `appShellLayout.test.tsx` role queries for
    account/logout stay green), hamburger pinned right (`margin-left:auto`), brand
    `min-width:0` — root-cause fix for the 320–390px overflow.
  - **Coarse-pointer touch targets (R7, OQ4a):** `@media (pointer: coarse)` rules
    on `Button.sm` (`Button.module.css`) and the segmented control
    (`Controls.module.css`) → ≥44px effective hit area on touch only; fine-pointer
    desktop sizing untouched.
  - **Showcases:** `src/styles/StyleGuide.tsx` gains a "Responsive" section
    (breakpoint ladder note + a live `stackOnMobile` expenses table with
    `data-label`s + coarse-pointer note) and wires the AppShell `secondaryActions`
    drawer footer so all four patterns are visually verifiable in light + dark.
  - **Verification:** node/pnpm are NOT installed in this environment (only
    orphaned corepack/npm shims on PATH; no `node.exe` found anywhere), so
    `pnpm lint` / `tsc -b` / `pnpm build` / `pnpm test` could not be executed here
    — flagged for the orchestrator to run in a node-equipped environment. Static
    verification done instead: changes are additive and isolated (no snapshot
    files; no test references the new APIs; `Table`/`AppShell` default render
    paths unchanged); `data-*`-as-prop typechecks (proven by `EventBalanceTable`);
    oxlint has no `jsx-a11y` plugin and the lint script is plain `oxlint`
    (warnings don't fail); jsdom ignores `@media` blocks, so the `max-width`
    actions-hide keeps the shell tests green.
  - **Deviation logged:** the header-actions hide is authored as one `max-width`
    query (paired with the `lg` stop) rather than a mobile-first base
    `display:none` + `min-width` reveal — required so the pre-existing shell unit
    tests keep finding the header actions in jsdom (documented in `tokens.css`,
    `README.md`, `AppShell.module.css`).
  - **Handoff:** web-implementer applies the ladder to feature CSS modules, opts
    `ExpensesTable` into `stackOnMobile` with the i18n `data-label`s, and wires
    `AppShellLayout` to pass the controls into `secondaryActions` (see the Design
    spec subsection). Then web-test-engineer verifies + adds the mobile-viewport
    E2E project (OQ6a).
- **2026-07-18 (design-phase verification — orchestrator)** — Ran the quality bar
  the designer couldn't (node/pnpm are fnm-managed and were missing from the
  subagent shell; fixed at the harness level — see the node-PATH memory). All
  green against the design changes: `pnpm lint` exit 0 (pre-existing
  `only-export-components` warnings only), `tsc -b` exit 0, `pnpm test`
  **777/777 passed** (the additive `Table`/`AppShell` changes broke no unit test),
  `pnpm build` succeeded (5.5s; only the pre-existing >500kB chunk advisory).
  Design phase confirmed done; proceeding to the web-implementer.

- **2026-07-18 (web-implementer — FEATURE-SIDE application, steps 2(wiring)/4/5)** —
  Applied the ui-designer's design spec feature-side (presentation-only; no API/
  business-rule/data-logic change):
  - **`ExpensesTable` opt-in card-stack (R5, OQ3a):** added `stackOnMobile` to the
    `<Table>` in `src/features/expenses/components/ExpensesTable.tsx`; the expense
    name stays the `<TableHeaderCell scope="row">` card title (label-less), and each
    value `<TableCell>` gained `data-label={t("expenses:list.<col>")}` reusing the
    SAME header i18n keys (payer/category/total [kept `numeric`]/time/settled/event).
    The trailing `<TableCell actions>` stays label-less. `EventBalanceTable`
    untouched (stays horizontal-scroll, E2E-covered).
  - **`AppShell secondaryActions` wiring (R3/R4, OQ5a):** `src/routes/AppShellLayout.tsx`
    still passes the four controls to the desktop `actions` unchanged, and ALSO passes
    them to the new `secondaryActions` drawer-footer slot. In the footer the account +
    logout `Button`s get `fullWidth`; account stays `Button asChild` + router `Link`
    (real `<a>` → drawer auto-closes on nav); logout + both toggles stay `<button>`s;
    same localized labels + handlers.
  - **Breakpoint-ladder pass on touched feature CSS (R1/R2):**
    - `ShareEditor.module.css`: retuned the stray `@media (max-width: 40rem)` to the
      mobile-first `md` stop — base is now the stacked grid-areas layout, and
      `@media (min-width: 48rem)` restores the single 4-column row
      (`"member amount note remove"`). No new `max-width` query introduced.
    - `ExpenseFilterBar.module.css`: made fields full-width stacked at the mobile
      base (`flex: 1 1 100%`) and restored the `9rem`/`12rem` flex bases at
      `@media (min-width: 30rem)` (`sm`); clear action stays reachable.
    - `EventDetailPage.module.css` / `ExpensesPage.module.css`: reviewed — already
      reflow via `flex-wrap`/block with NO stray thresholds, so no change was needed
      (header title/badges/range/actions stack cleanly; balance section left on
      horizontal-scroll per D5).
  - **Coarse-pointer touch targets (R7):** inherited automatically from the
    primitives (`Button.sm`, segmented toggles) — no caller opt-in, per the design
    spec section 4.
  - **Verification (all green, run with the fnm node PATH prefix):** `pnpm lint`
    exit 0 (only pre-existing `only-export-components` + one e2e-fixture unused-import
    warning; none from touched files); `pnpm exec tsc -b` exit 0; `pnpm test`
    **777/777 passed** (93 files — no unit test changed or broke); `pnpm build`
    succeeded (5.42s; only the pre-existing >500kB chunk advisory); `pnpm test:e2e`
    **1 passed** — the ledger-loop spec stayed green (13.4s) after the `ExpensesTable`
    reflow + `AppShellLayout` change.
  - **Deviations:** none beyond the design-phase-logged AppShell `max-width`
    companion (already documented). The mobile-viewport Playwright project +
    `e2e/header-responsive.spec.ts` (OQ6a, step 7) are the web-test-engineer's, not
    this feature-side step.

- **2026-07-18 (web-test-engineer — TEST phase, step 7 / OQ6a)** — Added the
  mobile-viewport E2E verification so the responsive header + ledger reflow are
  regression-proof. Test-only + harness changes (no product code touched):
  - **`playwright.config.ts`:** added the `mobile` project
    (`...devices["Pixel 5"]`, 393px) alongside `chromium`, re-pinning
    `locale: "vi-VN"` + `timezoneId: "Asia/Ho_Chi_Minh"` on it (the Pixel 5
    preset supplies viewport/UA/isMobile/hasTouch but NOT locale/tz — merged, not
    dropped). The `mobile` project re-runs `ledger-loop.spec.ts` at a phone
    viewport (exercising the `ExpensesTable` card-stack reflow + the drawer-driven
    nav) and runs the new header spec. `chromium` keeps running the ledger loop
    and now carries `testIgnore: /header-responsive\.spec\.ts$/` so the phone-only
    header spec never runs at a desktop viewport (declarative viewport pin, no
    per-spec `test.use`). Documented in `e2e/README.md`.
  - **`e2e/header-responsive.spec.ts` (new, 4 tests, phone-only):**
    `AppShellHeader_PhoneViewport_DoesNotOverflowHorizontally` (header
    `scrollWidth <= clientWidth` AND `document.documentElement` no horizontal
    scroll — the top defect the cycle fixed);
    `AppShellHeader_BelowNavBreakpoint_ShowsBrandAndHamburgerOnly` (brand +
    hamburger visible; the inline logout button + both segmented toggles are
    `toBeHidden` below `lg`); `NavDrawer_Opened_ExposesRelocatedSecondaryActionsInFooter`
    (opening the hamburger exposes the language + theme toggles, the `/settings`
    account link, and logout in the drawer footer, all role/label-scoped to the
    dialog — plus an a11y smoke that the toggles' radio options carry accessible
    names); `NavDrawerAccountLink_Activated_ClosesDrawer` (activating the real
    router `<a>` navigates to `/settings` and the drawer auto-dismisses). Uses the
    `appPage` fixture (logged in as `demo`) + the vi-VN `copy` fixture; no
    mid-flow `page.reload()` (OQ3a).
  - **Shared-fixture harness fix (required for the ledger loop to run on a phone
    — the ledger spec's SELECTORS and ASSERTIONS are unchanged):** the header's
    inline `<nav>` is `display:none` below `lg` (correct responsive behavior), so
    the desktop-only `navLink()` finds nothing in the a11y tree on Pixel 5 — the
    unmodified ledger loop failed at login and every nav click. Added a
    viewport-agnostic `gotoNav(page, name)` to `fixtures/session.ts` (opens the
    hamburger drawer first below `lg`, then clicks the SAME nav-link selector; the
    drawer auto-closes on activation), made `login()`'s readiness assertion
    viewport-aware (hamburger below `lg`, unchanged inline-nav assertion above),
    and switched the ledger spec's 3 navigation calls from `navLink(...).click()`
    to `gotoNav(...)`. Only the navigation *mechanism* changed — no `getByRole`/
    `getByLabel`/`getByTestId` query and no assertion (incl. the balance
    `data-testid`s + sum-to-zero) was weakened. Also removed a latent unused
    `navLink` top-level import in `fixtures/test.ts` that the e2e typecheck
    surfaced.
  - **Verification (run with the fnm node PATH prefix):**
    - `pnpm test:e2e` — **6 passed** (run twice, stable, ~19-20s each).
      Per-project: `chromium` = 1 (ledger-loop); `mobile` = 5 (ledger-loop +
      4 header-responsive).
    - `pnpm lint` — exit 0 (only the pre-existing `only-export-components`
      warnings; no warning from any touched file).
    - `pnpm exec tsc -p e2e/tsconfig.json --noEmit` — **exit 0** (after clearing
      the latent unused import above).
    - `pnpm test` (Vitest) — **777/777 passed**, 93 files (unit suite untouched).
  - **No product bug found.** The mobile ledger-loop failure was a test-harness
    gap (desktop-only nav helper), not an app defect — the app's drawer nav is the
    responsive behavior being verified. Coverage gaps: none for this cycle's
    scope; visual-regression + `axe` a11y in E2E remain the documented Future
    Improvement (OQ6c), not this cycle.

## Final Outcome

**Delivered (2026-07-18).** Cycle 1 of the UX/UI effort — responsive/mobile
polish — is complete and reviewed **APPROVE, 0 blocking**. Shipped: a documented
mobile-first breakpoint ladder (sm 30rem / md 48rem / lg 64rem, comment
convention, no new dep); the app-shell header overflow fixed at its root (below
`lg` the header shows brand + hamburger only, and the language/theme toggles +
account link + logout relocate into the nav-drawer footer inside the existing
focus trap); the expenses list opts into an **additive** `Table stackOnMobile`
card-stack variant (i18n `data-label`s reusing the column-header keys), with the
CRITICAL-blast-radius `Table` default and its 33 other consumers left byte-for-byte
unchanged (D8), and `EventBalanceTable` kept on horizontal-scroll to protect the
E2E selectors; the expenses filter bar + share editor retuned to the ladder;
coarse-pointer 44px touch targets on the primitives (fine-pointer desktop
unchanged). Verification: `pnpm lint` clean, `tsc -b` 0, `pnpm test` **777/777**,
`pnpm build` 0, and `pnpm test:e2e` **6 passed** across a `chromium` (desktop
ledger-loop) and a new `mobile` Pixel 5 project (ledger-loop + 4 new
`header-responsive` assertions) — the reflow is now regression-proof. Deferred to
cycle 2 (per OQ2): admin/stats/wallet/members/categories/settings + the repo-wide
ladder sweep. Presentation-only — no API/DTO/business-rule change.

## Future Improvements

- **Repo-wide breakpoint sweep** — after the ladder is set, consolidate the
  remaining untouched one-off thresholds (32/34/40/60rem in
  `AuditTimeline`, `AssignExpenseDialog`, the `M4/M5/M6` showcases) to the ladder.
- **Cycle 2 responsive polish** — admin suite (user table + dashboards + sensitive
  dialogs) card-stack/reflow, stats-chart responsive tuning
  (`RankedBarChart`/`TimeSeriesBarChart`/`KpiRow` at phone widths), wallet/QR,
  members/categories/tags/settings fine-tuning.
- **Card-stack all dense tables** (OQ3b) once the expenses-list pattern is proven
  and the admin tables come under E2E coverage.
- **Visual-regression + `axe` a11y in E2E** (OQ6c) — Playwright screenshot
  snapshots + `@axe-core/playwright` to guard the reflow, aligning with the E2E
  doc's own Future Improvements.
- **Container queries** — where a component reflows based on its own container (e.g.
  cards in a grid) rather than the viewport, adopt `@container` once the ladder
  proves insufficient for component-local reflow.
- **Enforceable breakpoint tokens** (OQ1b) if the comment-convention ladder drifts
  in practice.
