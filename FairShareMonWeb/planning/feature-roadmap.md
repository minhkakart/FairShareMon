# Frontend Feature Roadmap (FairShareMonWeb)

## Objective

Sequence the FairShareMonWeb SPA feature milestones that remain after the foundation cycle, so the
team delivers the **full product UI over the feature-complete API** cycle-by-cycle. This is a
strategic roadmap (the frontend mirror of the backend team's `FairShareMonApi/planning/agent-dev-team.md`
M1–M11), **not** a single-feature plan. Each roadmap item = one team cycle (plan → design →
implement → test → review) followed by a user checkpoint; each gets its own dedicated planning doc
under `FairShareMonWeb/planning/` when its cycle starts. Ordering is dependency-driven; open,
preference-dependent sequencing/scoping calls are surfaced as Open Questions for the checkpoint.

## Background

- **The backend is feature-complete and CORS-ready.** Every controller exists and is stable
  (`Auth`, `Members`, `Categories`, `Tags`, `Expenses` + shares/export/QR/history sub-routes,
  `Events` + balance/export/QR/close, `Stats`, `BankAccounts`, `Admin`, `Health`), versioned under
  `api/v1/[controller]`. The API-contract conventions (Bearer + refresh, `X-Time-Zone`,
  `Accept-Language`, `ApiResult<T>` envelope, numeric `error.code`, VND money, vi-VN/en-US,
  ownership-404, closed-event immutability, Premium/Free gating, admin privacy boundary) are LOCKED
  and embedded in `FairShareMonWeb/CLAUDE.md`.
- **The product spec** is `FairShareMonApi/The-ideal.md` (§3 features, §4 mandatory business rules).
  The full product surface: accounts/session (§3.1 — done), members (§3.2), categories (§3.3) & tags
  (§3.4), expenses & shares (§3.5), events (§3.6), debt balance (§3.7), audit log (§3.8), stats
  (§3.9), wallet & QR (§3.10), tiers Premium/Free (§3.11), plus the user-added admin suite.
- **The mandatory §4 rules the UI must honor everywhere:** R1 absolute privacy (ownership miss = 404,
  never leak existence); R2 same-owner link integrity (mirror in form option-lists); R3 money exact
  (render API values, never float-math); R4 closed events immutable (disable every write control
  except the settled toggle); R5 atomic expense+shares (single submit); R6 exactly-one default
  category (never deletable); R7 soft-delete keeps history (deleted members/categories/tags stay
  visible in historical data, unselectable for new); R8 no deleted resource on new data; R9 tier
  limits block only create, never touch existing data.
- **Same orchestration protocol + per-feature user checkpoint** as the backend team; the five
  frontend role agents (`web-feature-planner`, `ui-designer`, `web-implementer`,
  `web-test-engineer`, `web-code-reviewer`) run each cycle.

## Done / foundation (baseline — NOT roadmap work)

Already shipped and closed (do not re-plan these):

- **Foundation cycle** (`planning/frontend-foundation.md`) — all cross-cutting plumbing: the
  centralized typed API client (envelope unwrap; `Authorization` + `X-Time-Zone` + `Accept-Language`
  injection; de-duped `401 → refresh-once → retry → else clear + redirect`; blob path for CSV/QR;
  `ErrorCodes` mirror + `classifyError`/`resolveErrorMessage`/`applyFieldErrors`); Zustand vanilla
  session store (access in memory / refresh in `localStorage` / boot rehydrate, readable outside
  React); TanStack Query data layer; React Router v7 with `ProtectedRoute` / `AdminRoute` /
  `PublicOnlyRoute` guards + `AppShellLayout` + shared `NotFound`/`Forbidden`; react-i18next
  (vi-VN default + en-US) + `formatMoneyVnd`/`formatDateTime`/`getTimeZone`; theme (`[data-theme]`
  light/dark/system, no-flash); RHF + Zod with backend-mirrored validators; MSW test harness
  (Vitest + RTL).
- **The design system** (`src/components/ui/*`, `src/styles/tokens.css`) — jade/teal semantic tokens
  (light + dark, AA-verified), primitives (Button, TextField, Form/Field, Card, Badge, Money, Alert,
  Skeleton, EmptyState, ErrorState, `UpgradePrompt` + `LimitNotice` tier affordances, Dialog + Toast,
  AppShell, ThemeToggle, LanguageToggle), and a **reserved, validated `--fs-viz-*` dataviz palette**
  (blue-anchored categorical/sequential/diverging) for the future Stats + Admin dashboards. No chart
  components exist yet.
- **Auth vertical slice** (`src/features/auth/*`) — login, register, logout, change-password,
  session rehydrate/refresh, protected redirect, error-code rendering, locale + theme toggles.
- **`/auth/me` wiring** (`planning/wire-current-user-profile.md`) — `useCurrentUserQuery` fetches
  `/auth/me` after login and boot-refresh, syncs `{ uuid, username, tier, role }` into the session
  store; `AdminRoute` is **activated off `role === "ADMIN"`** (fail-safe deny).
- **Stub routes** already registered in `src/routes/router.tsx` for `/members`, `/categories`,
  `/tags`, `/expenses`, `/events`, `/stats`, `/wallet`, plus `/dashboard`,
  `/settings/change-password`, and the `/admin` sub-tree — each a `StubPage` this roadmap replaces.

So: auth, session, routing + guards, i18n, theme, forms, the design system, and the dataviz palette
are DONE. The roadmap below covers only the remaining **feature screens**.

## Requirements

- Every milestone maps to real, stable API routes (listed per item) — no backend change is assumed;
  if a screen appears to need one, that is an Open Question in that milestone's own cycle, not here.
- Every milestone honors the §4 rules relevant to it (called out per item).
- Reuse the foundation: the one API client, TanStack Query hooks per feature
  (`src/features/<area>/api` + `hooks`), the `src/components/ui/*` primitives, the shared formatters,
  and i18n namespaces — never a parallel system.
- vi-VN-first copy with en-US parity; fixed domain terms (expense = phiếu chi tiêu, share = phần
  gánh, event = đợt, wallet = ví, settled = đã trả, Premium/Free — never voucher/record/batch).
- The `ui-designer` runs whenever a milestone introduces materially new visual surfaces; the
  `dataviz` skill applies to the two dashboard milestones (Stats, Admin).
- Each milestone is right-sized to one cycle; the core (Expenses & Shares) is the largest and may be
  split at its own planning checkpoint if it exceeds one cycle.

## Milestones (dependency-ordered)

> Sizes are rough (S ≈ small/one focused surface, M ≈ several screens + forms, L ≈ the largest
> surfaces). "UI-designer: yes" = materially new visual surfaces needing design. "dataviz: yes" =
> the `dataviz` skill applies. Each milestone consumes the LOCKED envelope/error conventions; only
> milestone-specific codes are listed.

### M1 — App shell, navigation & account settings — size S/M

- **Scope:** flesh out the empty `AppShellLayout` into the real authenticated shell — primary
  navigation to every feature area (members, categories/tags, expenses, events, stats, wallet, and
  the admin entry when `role === "ADMIN"`), responsive/mobile-first layout, the account/settings
  surface (show username, **tier badge**, role; link to the existing change-password page; locale +
  theme toggles relocated into settings/header), and a minimal **home** landing (welcome + quick
  links; the rich summary lands in M6/Stats). Establishes the navigation-registration pattern each
  later milestone extends.
- **API consumed:** none new — reads the session `user` (`/auth/me` already wired). No writes.
- **Depends on:** foundation only.
- **Business rules:** admin nav item gated on `role === "ADMIN"` (fail-safe hide); tier badge reads
  session tier (display only — the gating affordances come with the gated features).
- **ui-designer:** yes (shell, nav, home, settings). **dataviz:** no.

### M2 — Members — size S/M

- **Scope:** members list (owner-representative first, then A→Z; an `includeDeleted` toggle to reveal
  soft-deleted members for stats/export context), add member, rename member (incl. the owner-rep),
  soft-delete member with confirm. Empty/loading/error states.
- **API consumed:** `GET /members?includeDeleted=`, `GET /members/{uuid}`, `POST /members`
  (`CreateMemberRequest`), `PUT /members/{uuid}` (`UpdateMemberRequest`), `DELETE /members/{uuid}`.
  Responses `MemberResponse`.
- **Depends on:** M1.
- **Business rules:** R1 ownership 404 → `NotFound`; R7 soft-delete keeps history (deleted members
  stay listed under the toggle, hidden from default lists); owner-rep is non-deletable (backend
  `3xxx` 400 → inline message + no delete control on the owner-rep); R9 Free member-limit → `13000`
  400 → `<LimitNotice>` (no self-serve upgrade — informational).
- **ui-designer:** yes (list + member form/dialog). **dataviz:** no.
- **Rationale for placing early:** expenses reference members (payer + shares); members must be
  manageable first.

### M3 — Categories & Tags — size M

- **Scope:** categories list (default first, then A→Z; `includeDeleted` toggle), create/edit
  category with **color + icon picker**, set-default action, soft-delete (default not deletable);
  tags list, create/rename, soft-delete, with reactivation-on-name-reuse messaging. Both share the
  reference-data list/form pattern from M2.
- **API consumed:** `GET/POST /categories`, `GET/PUT/DELETE /categories/{uuid}`,
  `PUT /categories/{uuid}/default` (`CreateCategoryRequest`/`UpdateCategoryRequest`,
  `CategoryResponse`); `GET/POST /tags`, `GET/PUT/DELETE /tags/{uuid}`
  (`CreateTagRequest`/`UpdateTagRequest`, `TagResponse`).
- **Depends on:** M1 (and shares the M2 CRUD pattern).
- **Business rules:** R6 exactly-one default category (disable delete on the default; set-default
  swaps atomically server-side); unique active names → `4xxx`/`5xxx` 400 → field-level message;
  create-with-deleted-name reactivates (surface as informational, not an error); R7/R8 soft-delete
  keeps history + unselectable for new. No Free limit on categories/tags.
- **ui-designer:** yes (color/icon picker is the new surface). **dataviz:** no.
- **Rationale:** expenses require a category and optional tags, and the expense-list filters key off
  them — both must exist before the expense forms/filters.

### M4 — Expenses & Shares (the core) — size L

- **Scope:** the ledger's heart. Expense list with the full filter set (event / date range /
  category / tag / settled), expense detail (shares table, tags, category, payer, derived total),
  the **atomic create form** (name, description, expense time, payer defaulting to owner-rep,
  category defaulting to default-category, tag set, and the share editor with the owner-rep 0đ row
  auto-present), edit general info, delete (cascades shares), the **settled toggle**, the share
  sub-CRUD (add/edit/delete a share, member-swap), the per-expense **change-history / audit view**
  (timeline), and **CSV export** (blob download, Free). May be split into "list + detail + create"
  and "edit + shares + history + export" sub-cycles at its own planning checkpoint if it overflows.
- **API consumed:** `GET /expenses` (`ExpenseFilter` → `ExpenseSummaryResponse[]`),
  `GET /expenses/{uuid}` (`ExpenseResponse`), `POST /expenses` (`CreateExpenseRequest`),
  `PUT /expenses/{uuid}` (`UpdateExpenseRequest`), `DELETE /expenses/{uuid}`,
  `PUT /expenses/{uuid}/settled` (`SetSettledRequest`), `POST /expenses/{uuid}/shares`
  (`CreateShareRequest`), `PUT /expenses/{uuid}/shares/{shareUuid}` (`UpdateShareRequest`),
  `DELETE /expenses/{uuid}/shares/{shareUuid}`, `GET /expenses/{uuid}/history`
  (`AuditLogResponse[]`), `GET /expenses/{uuid}/export?format=csv` (blob).
- **Depends on:** M2 (members for payer/shares), M3 (categories/tags for select + filters).
- **Business rules:** R5 atomic create (one submit builds expense + shares); R3 money exact (render
  derived total from API; the share editor sums for display only, never authoritative); R2 same-owner
  links (option lists only show the caller's own active members/categories/tags); R8 deleted
  members/categories/tags are unselectable but shown in historical detail (`(đã xóa)` styling); R1
  ownership 404; R4 closed-event immutability — if the expense belongs to a closed event, disable
  every write control **except the settled toggle** (drive off the expense's event-closed flag /
  `9001` on attempted write); mirror the backend validators (share member uniqueness, non-negative
  amounts) in Zod; Free monthly-expense limit → `13002` 400 → `<LimitNotice>`; `1001` field errors
  mapped onto the create/edit forms.
- **ui-designer:** yes (the most complex forms in the app — share editor, filter bar, audit
  timeline). **dataviz:** no.
- **Rationale:** the product's core; everything upstream (members, categories/tags) exists to feed
  it, and everything downstream (events, balance, stats, QR) consumes its data.

### M5 — Events (lifecycle + closed-event UI) — size M/L

- **Scope:** events list (open/closed filter; sorted by start date), event detail (info + derived
  expense count + the **debt-balance table** from §3.7, and the list of the event's expenses via the
  expense filter), create event, edit info (open only), delete (open only; expenses become loose),
  the **one-way close** action with a strong confirm, assign/remove an expense to/from an event, and
  event **CSV export**. The closed-event read-only treatment is the headline UX: a closed event
  disables all write controls across its expenses except the settled toggle.
- **API consumed:** `GET /events` (`EventFilter` → `EventSummaryResponse[]`), `GET /events/{uuid}`
  (`EventResponse`), `POST /events` (`CreateEventRequest`), `PUT /events/{uuid}`
  (`UpdateEventRequest`), `DELETE /events/{uuid}`, `PUT /events/{uuid}/close`,
  `GET /events/{uuid}/balance` (`EventBalanceResponse`), `GET /events/{uuid}/export?format=csv`
  (blob); plus the M4 expense-side `PUT /expenses/{uuid}/event` (`AssignEventRequest`) and
  `DELETE /expenses/{uuid}/event`.
- **Depends on:** M4 (expenses reference events; balance is computed from the event's shares; the
  assign/remove routes live on the expense).
- **Business rules:** R4 closed events immutable — one-way close (`9001` on re-close/writes),
  disable edit/delete/assign/remove/share-writes when closed, settled toggle the sole exception;
  expense-time-within-range validation (`9002`/`9003`) surfaced on assign + range-edit; R3 balance
  sums to zero (display verbatim from API); R1 ownership 404; R9 Free open-event limit → `13001` 400
  → `<LimitNotice>`.
- **ui-designer:** yes (event detail with balance table + closed-state treatment). **dataviz:** the
  balance is a table, not a chart — `dataviz` not required (a light bar for advanced/owed is
  optional and deferred).
- **Rationale:** events group and lock expenses, so expenses must exist first; balance depends on
  the shares from M4.

### M6 — Stats & Home dashboard — size M

- **Scope:** the statistics surface + the rich **home dashboard**: overview (total spending +
  expense count over an optional date range) and by-category (total + count per category with its
  color, as a pie/bar **chart** plus an accessible data table), filterable by date range or by a
  single event. The home landing from M1 gets its real content here (recent activity + overview
  tiles + a category chart).
- **API consumed:** `GET /stats/overview` (`StatsRangeRequest` → `OverviewStatsResponse`),
  `GET /stats/by-category` (`ByCategoryStatsRequest` → `ByCategoryStatsResponse`); reuses
  `GET /events/{uuid}/balance` for the per-event lens.
- **Depends on:** M4 (expenses populate the numbers) and M3 (category colors); the event lens uses
  M5.
- **Business rules:** R1 privacy; range-XOR-event validation (`400` when both supplied); R3 render
  API money verbatim; charts must honor the light-mode relief rule from the design system (direct
  labels or a table fallback for low-contrast adjacent slots); color-independent (labels + values,
  not color alone) for a11y.
- **ui-designer:** yes. **dataviz:** **yes** — first consumer of the reserved `--fs-viz-*` palette;
  charts built with the `dataviz` skill.
- **Rationale:** stats need populated expenses/events to be meaningful, so it follows the core and
  events.

### M7 — Wallet (bank accounts) & QR — size M

- **Scope:** the wallet — bank accounts list (default first), create/edit account (BIN, bank name,
  account number, holder), set-default, delete (default-promotion handled server-side); the
  per-expense **QR image** (PNG blob display + download/share) on the expense detail; the per-event
  **composite QR image** (closed events only) on the event detail. All Premium-gated.
- **API consumed:** `GET /bank-accounts`, `GET /bank-accounts/{uuid}`, `POST /bank-accounts`
  (`CreateBankAccountRequest`), `PUT /bank-accounts/{uuid}` (`UpdateBankAccountRequest`),
  `PUT /bank-accounts/{uuid}/default`, `DELETE /bank-accounts/{uuid}` (`BankAccountResponse`);
  `GET /expenses/{uuid}/qr` (PNG blob, or `?format=payload` → string); `GET /events/{uuid}/qr`
  (PNG blob, closed only).
- **Depends on:** M4 (expense QR uses the expense total) and M5 (event QR uses the closed-event
  balance; the QR entry points live on the expense/event detail screens).
- **Business rules:** **Premium gating (the OQ5b read-vs-mutation split)** — wallet **reads** (list/
  get) are Free; wallet **mutations** (create/update/set-default/delete) **and both QR generations**
  are Premium → `403 13003` → `<UpgradePrompt>` (mutations show the gate proactively; QR buttons show
  the upgrade affordance for Free users); `12001` no bank account → prompt to add one; `12002` event
  not closed → QR only after close; `12003` no outstanding debt → informational; R1 ownership 404;
  blob/image display via `api.blob(...)`, never the JSON path.
- **ui-designer:** yes (bank-account forms + QR image presentation/share). **dataviz:** no.
- **Rationale:** QR consumes the expense total (M4) and the closed-event balance (M5), so wallet
  follows both; it is the first Premium-gated feature, exercising the upgrade affordances.

> **Tiers / Premium & Free UX — dissolved into the gated features (OQ2 = a).** There is **no
> standalone Tiers milestone.** The tier/Premium affordances are folded into the features that carry
> them: a **tier-status / how-to-upgrade surface in M1** (account settings), the **member-limit
> notice in M2** (`13000`), the **expense-limit notice in M4** (`13002`), the **open-event-limit
> notice in M5** (`13001`), and the **Premium read-vs-mutation gate in M7** (`13003` →
> `<UpgradePrompt>`). **There is no self-serve upgrade endpoint** — tier is a manual admin grant, so
> every upgrade affordance is **informational only** (explains how to upgrade; renders the backend's
> localized message verbatim, branches on the numeric code; R9 = limits block only create, never
> touch existing data). A short **tier-UX consistency pass** (a review sweep, not a milestone) runs
> at the end of the feature work to confirm the badge, limit notices, and upgrade prompts read
> coherently across all gated features. The `<LimitNotice>`/`<UpgradePrompt>` primitives already
> exist from the design system.

### M8 — Admin suite — size L (placement flexible — see OQ3)

- **Scope:** the `role === "ADMIN"`-only admin area (behind the already-wired `AdminRoute`):
  metrics dashboard (total users, tier/role/status distribution, registrations over time), revenue
  dashboard (Premium grant totals by month/day + reference list), user administration (paged/filter/
  sort list; user detail with grant history; tier grant/revoke; disable/enable; revoke-tokens;
  reset-password with the one-time temp password; role promote/demote).
- **API consumed:** `GET /admin/dashboard` (`AdminMetricsRequest` → `AdminMetricsResponse`),
  `GET /admin/revenue` (`RevenueRequest` → `RevenueResponse`), `GET /admin/users`
  (`AdminUserListRequest` → `PagedResult<AdminUserRow>`), `GET /admin/users/{uuid}`
  (`AdminUserDetailResponse`), `POST /admin/users/{uuid}/tier/grant` (`GrantTierRequest`),
  `POST /admin/users/{uuid}/tier/revoke` (`RevokeTierRequest`),
  `POST /admin/users/{uuid}/disable`, `POST /admin/users/{uuid}/enable`,
  `POST /admin/users/{uuid}/revoke-tokens`, `POST /admin/users/{uuid}/reset-password`
  (`ResetPasswordRequest` → `ResetPasswordResponse`), `POST /admin/users/{uuid}/role`
  (`SetRoleRequest`).
- **Depends on:** foundation + the `AdminRoute` guard only — **independent of M2–M8**; can slot
  anywhere after M1.
- **Business rules:** **admin privacy boundary** — the UI shows ONLY account metadata + tier-grant
  data, never any user's ledger; admin-only (non-admins never see the area — 403 `1004`);
  self-target `14001` / admin-target & last-admin `14002` guards → disable/explain the blocked
  actions; disabled-account login `14003`; reset-password temp shown exactly once (copy-to-clipboard,
  never persisted client-side); money (revenue) rendered verbatim.
- **ui-designer:** yes (dashboards + user-admin tables + action dialogs). **dataviz:** **yes** —
  metrics + revenue charts on the `--fs-viz-*` palette.
- **Rationale:** highest-privilege, self-contained, and lower day-to-day user priority — naturally
  last, but independent enough to pull earlier if the operator needs it (OQ3).

## Sequencing rationale

1. **A light shell first (M1)** so there is real navigation to hang every subsequent screen on, and a
   place for the tier badge/settings — but deliberately minimal, deferring the data-rich home to the
   Stats milestone.
2. **Reference data before the core:** members (M2) and categories/tags (M3) are _referenced by_
   expenses (payer, shares, category, tags) and by the expense-list filters, so they must be
   creatable/selectable before the expense forms exist. This mirrors the backend's own M3→M4→M5
   ordering.
3. **Expenses & Shares (M4) as the core** — the largest surface; everything above feeds it and
   everything below consumes it. It also introduces the closed-event-immutability write-guard pattern
   (initially inert until events exist).
4. **Events (M5) after expenses** because events group and lock _existing_ expenses, the assign/remove
   routes live on the expense, and the balance is computed from expense shares.
5. **Stats (M6) after the core + events** so the numbers/charts have populated data; it is the first
   `dataviz` consumer and completes the home dashboard.
6. **Wallet & QR (M7) after expenses + events** because the per-expense QR needs the expense total and
   the per-event QR needs the closed-event balance; it is the first Premium-gated feature.
7. **Tiers/Premium UX is folded into the gated features** (M1 tier surface, M2/M4/M5 limit notices,
   M7 Premium gate) rather than a standalone milestone — a review sweep at the end confirms coherence
   (OQ2 = a).
8. **Admin (M8)** is independent (needs only the guard) and lowest day-to-day priority, so it sits
   last (OQ3 = a).

## Open Questions

> Each option carries a one-line trade-off; the **Recommended** option is marked. These are
> genuinely preference-dependent sequencing/scoping calls for the user at the checkpoint — they do
> not block drafting the individual milestone docs, but they shape the order and granularity.
>
> **All 6 were answered by the user at the 2026-07-17 checkpoint — every one at the recommended
> option (a).** Each is annotated below with its resolution; the binding record is in the Decision
> Log.

### OQ1 — Delivery strategy: complete-each-area vs walking-skeleton

> **Resolved 2026-07-17 — option (a): complete each area fully, in dependency order (M2 → M8).**

- **(a) Recommended — complete each feature area fully within its milestone, in the dependency order
  above (M2 → M8).** Matches the backend team's proven cadence; each checkpoint delivers a finished,
  reviewable area; the foundation already proved the end-to-end path, so a skeleton adds little.
  Trade-off: the first fully usable _ledger loop_ (add member → add expense → close event → see
  balance) only lands after M5.
- (b) Build a thin **walking skeleton** of the core ledger first (minimal members → categories →
  expense-create → event → balance, happy-path only), then iterate depth area-by-area. Trade-off: a
  usable demo sooner, but revisits every screen twice (churn, doubled review surface, harder
  checkpoints).
- (c) Hybrid — do (a) but front-load a minimal expense-create path inside M4's first sub-cycle so the
  core loop is demoable mid-M4. Trade-off: adds a sub-checkpoint inside the largest milestone.

### OQ2 — Is Tiers/Premium UX its own milestone or folded into each gated feature?

> **Resolved 2026-07-17 — option (a): fold into the gated features; the standalone Tiers milestone
> is dissolved** (M1 tier surface, M2/M4/M5 limit notices, M7 Premium gate; end-of-work consistency
> sweep).

- **(a) Recommended — fold the gating affordances into each gated feature as it is built (M2 limit,
  M4 limit, M5 limit, M7 Premium gate), and keep only a tiny "tier status / how to upgrade" surface in
  M1 settings — dissolving the standalone Tiers milestone.** The `<LimitNotice>`/`<UpgradePrompt>` primitives
  already exist; each feature naturally owns its own limit/gate messaging; no self-serve purchase flow
  exists to justify a dedicated milestone. Trade-off: the "coherent tier story" is emergent rather
  than designed in one pass — mitigated by a short consistency review at the end.
- (b) Keep M8 as a dedicated consolidation milestone after the gated features. Trade-off: a real
  checkpoint for tier UX coherence, at the cost of an extra cycle for mostly-already-shipped UI.

### OQ3 — Admin suite (M8): last, or prioritized/parallel?

> **Resolved 2026-07-17 — option (a): keep Admin last (M8).**

- **(a) Recommended — keep Admin last.** It is self-contained (needs only the wired `AdminRoute`),
  highest-privilege, and lowest day-to-day end-user priority; shipping the member-facing product first
  maximizes user-visible value per cycle. Trade-off: operators wait longest for the admin console.
- (b) Pull Admin earlier (e.g. right after M1), since it is independent of M2–M8. Trade-off: delays
  the member-facing ledger that is the product's reason to exist.
- (c) Treat Admin as a "slot-anywhere" milestone the user schedules on demand between other cycles.
  Trade-off: keeps flexibility but leaves the roadmap tail ambiguous.

### OQ4 — Home dashboard timing

> **Resolved 2026-07-17 — option (a): minimal home in M1, rich dashboard in M6.**

- **(a) Recommended — a minimal home in M1 (welcome + quick links), with the data-rich home
  dashboard (overview tiles + category chart + recent activity) built in M6/Stats** once there is data
  and the dataviz layer. Trade-off: the landing page is plain until M6.
- (b) Build the rich home earlier with empty/placeholder states. Trade-off: designs and builds a
  dashboard against data that does not exist yet, risking rework when Stats lands.

### OQ5 — Is the app-shell/navigation its own milestone (M1) or grown incrementally per feature?

> **Resolved 2026-07-17 — option (a): a dedicated light M1 shell/nav/settings milestone.**

- **(a) Recommended — a dedicated light M1 shell/nav/settings milestone** that establishes the
  navigation-registration pattern and the account/tier surface up front. Trade-off: one small cycle
  before feature work starts.
- (b) Skip a shell milestone and grow the nav incrementally as each feature route is added (starting
  with Members). Trade-off: no single owner for the shell/settings/tier-badge surface; the account
  page and navigation pattern get retrofitted.

### OQ6 — Granularity of Categories & Tags (M3)

> **Resolved 2026-07-17 — option (a): one combined M3 covering both Categories and Tags.**

- **(a) Recommended — one combined M3 covering both Categories and Tags** (mirrors the backend's
  combined M4; they share the reference-data CRUD pattern and are individually small). Trade-off: a
  slightly larger single cycle.
- (b) Split into two milestones (Categories, then Tags). Trade-off: two very small cycles / two
  checkpoints for closely-related reference data.

## Assumptions

- No backend change is needed for any milestone; the API is feature-complete and stable. There is
  **no self-serve tier-upgrade endpoint** — upgrading to Premium is a manual admin grant, so the
  member-facing "upgrade" UX is informational only.
- The roadmap order may be revised at any checkpoint as the user reprioritizes; sizes are estimates
  refined in each milestone's own planning doc.
- The core (M4) may be split into sub-cycles at its own planning checkpoint if it exceeds one cycle.
- The `dataviz` skill + the reserved `--fs-viz-*` palette are used only by M6 (Stats) and M8 (Admin);
  the light-mode chart-relief rule from the design system applies.
- Each milestone gets its own template-compliant planning doc under `FairShareMonWeb/planning/` at the
  start of its cycle, with its own Open Questions resolved before implementation.

## Impact Analysis

- **APIs/Database/Services:** none — this is a planning artifact; each milestone consumes existing,
  stable endpoints listed above.
- **Frontend:** each milestone replaces a `StubPage` with a real `src/features/<area>/`
  (`api/`, `hooks/`, `pages/`, `components/`, `schemas.ts`) tree, extends the nav registration, adds
  feature i18n namespaces (vi-VN + en-US), and adds Vitest + RTL component/interaction tests at the
  MSW boundary — all on the locked foundation stack.
- **Design system:** M6 and M8 first exercise the reserved dataviz palette (new chart components in
  `src/components/ui` or a `charts` module — decided in those cycles); other milestones reuse existing
  primitives.
- **Documentation:** this roadmap; one planning doc per milestone as its cycle starts.
- **Downstream:** later milestones depend on earlier ones per the dependency graph; reordering M4/M5
  earlier than their reference data would break the option-lists and filters.

## Decision Log

### Decision

Adopt a dependency-ordered milestone roadmap, mirroring the backend team's roadmap shape and cadence
(one cycle + checkpoint per item).

### Reason

The API is feature-complete and the foundation is shipped, so the remaining work is purely feature
screens. Reference data (members, categories, tags) must precede the expense forms/filters that
consume them; events lock existing expenses; stats/QR consume expense/event data — so the order is
dependency-driven, not preference-driven, except for the scoping/sequencing calls captured as Open
Questions.

### Alternatives Considered

- A walking-skeleton-first strategy (OQ1b) — rejected; risks double-handling every screen.
- Front-loading Admin (OQ3b) — rejected; delays the member-facing product.
- A standalone Tiers milestone (OQ2b) — rejected; folded into the gated features.

### Decision — roadmap locked at the 2026-07-17 checkpoint (all 6 OQs at the recommended option)

The user answered all 6 Open Questions at the 2026-07-17 checkpoint, accepting the recommended option
(a) for every one. The locked 8-milestone roadmap is:

| M   | Milestone                                                                    | dataviz | ui-designer |
| --- | ---------------------------------------------------------------------------- | ------- | ----------- |
| M1  | App shell, navigation & account settings (incl. tier-status/upgrade surface) | no      | yes         |
| M2  | Members (incl. `13000` member-limit notice)                                  | no      | yes         |
| M3  | Categories & Tags (combined — OQ6)                                           | no      | yes         |
| M4  | Expenses & Shares (core; incl. `13002` expense-limit notice)                 | no      | yes         |
| M5  | Events (incl. `13001` open-event-limit notice)                               | no      | yes         |
| M6  | Stats & Home dashboard (rich home — OQ4)                                     | **yes** | yes         |
| M7  | Wallet & QR (incl. `13003` Premium read-vs-mutation gate)                    | no      | yes         |
| M8  | Admin suite (last — OQ3)                                                     | **yes** | yes         |

**Consequence:** the standalone Tiers/Premium milestone is **dissolved** (OQ2 = a); its affordances
are folded into M1/M2/M4/M5/M7, with an end-of-work consistency sweep (a review, not a milestone).
Delivery is complete-each-area in dependency order (OQ1 = a); Categories & Tags stay combined
(OQ6 = a); the app shell is its own dedicated M1 (OQ5 = a). M1 (App shell) is the next cycle to
start on the user's go.

## Progress Log

### 2026-07-17

- Feature-planner drafted this roadmap. Required reading completed: `FairShareMonApi/The-ideal.md`
  (§2 concepts, §3 features, §4 mandatory rules, §5 locked decisions), the backend roadmap
  `FairShareMonApi/planning/agent-dev-team.md` (M1–M11 shape + closed-event/tier/admin semantics),
  every feature controller (`Members`, `Categories`, `Tags`, `Expenses` + shares/export/QR/history,
  `Events` + balance/export/QR/close, `Stats`, `BankAccounts`, `Admin`) to map each milestone to real
  routes, `FairShareMonWeb/CLAUDE.md` (locked conventions), the closed foundation +
  `/auth/me`-wiring planning docs, and the current `src/` tree (design system + primitives + stub
  routes already in place).
- Produced a 9-milestone dependency-ordered roadmap with per-milestone scope, consumed API routes,
  hard dependencies, enforced §4 business rules, ui-designer/dataviz involvement, and rough size;
  plus sequencing rationale and 6 Open Questions (delivery strategy, Tiers-UX fold-in, Admin
  placement, home-dashboard timing, shell-as-milestone, Categories&Tags granularity).
- Awaiting the user checkpoint to confirm ordering + resolve the Open Questions before the first
  feature milestone (M1) starts its own planning cycle.

### 2026-07-17 (checkpoint — roadmap locked)

- Checkpoint held; **the user answered all 6 Open Questions, accepting the recommended option (a) for
  every one.** OQ1 complete-each-area in dependency order · OQ2 fold Tiers into gated features (the
  **standalone Tiers milestone is dissolved**) · OQ3 Admin last · OQ4 minimal home in M1 + rich
  dashboard in M6 · OQ5 dedicated M1 shell · OQ6 combined Categories & Tags.
- Applied the locked answers: the Tiers milestone was dissolved and its affordances folded into
  M1 (tier-status/upgrade surface), M2 (`13000` member limit), M4 (`13002` expense limit),
  M5 (`13001` open-event limit), and M7 (`13003` Premium read-vs-mutation gate), with an
  end-of-work tier-UX consistency sweep (a review, not a milestone; upgrade UX is informational only
  — no self-serve endpoint). The former **M9 Admin** was renumbered to **M8**; all cross-references
  (sequencing rationale, dataviz applies to M6 + M8, designer M1–M8, Future Improvements) updated.
  OQs annotated Resolved inline; Decision Log entry added.
- **No open questions remain.** The roadmap is locked at 8 milestones. Next: **M1 (App shell)** begins
  its own plan → design → implement → test → review cycle on the user's go.

## Final Outcome

**Roadmap locked (2026-07-17).** The FairShareMonWeb feature roadmap is an 8-milestone,
dependency-ordered sequence — M1 App shell/nav/account+settings · M2 Members · M3 Categories & Tags ·
M4 Expenses & Shares · M5 Events · M6 Stats & Home dashboard · M7 Wallet & QR · M8 Admin suite — each
one team cycle plus a user checkpoint, built on the completed foundation. All 6 Open Questions were
resolved at the recommended option (a); the standalone Tiers/Premium milestone was dissolved and its
gating affordances folded into M1/M2/M4/M5/M7 (informational-only upgrade UX; end-of-work consistency
sweep). `dataviz` applies to M6 and M8; the `ui-designer` is materially involved in every milestone.
Each milestone gets its own template-compliant planning doc when its cycle starts. **M1 (App shell)
is the next cycle to start on the user's go.**

## Future Improvements

- Route-level code-splitting / lazy feature bundles as milestones land.
- Optimistic updates + granular cache invalidation for the write-heavy core (Expenses/Shares/Events).
- A shared chart module (`src/components/ui/charts`) extracted once both dashboard milestones
  (M6, M8) exist, to avoid duplicating dataviz scaffolding.
- Reassess whether any milestone reveals a genuine backend gap (e.g. self-serve upgrade, session/
  device list) — such gaps are Open Questions in the relevant milestone's own cycle, not silent
  additions here.
- E2E (Playwright) coverage of the full ledger loop once M2–M5 are shipped.
