# Tiers (Premium/Free) — Milestone 10

Free-tier **usage-limit enforcement** (create-only, count-based), **Premium feature-gating** of the
"mở rộng" group (activating the M9 `WalletQrService` seam + the wallet CRUD), and **tier propagation to
the request principal**, on top of the shipped Auth → Members → Categories/Tags →
Expenses/Shares/Audit → Events → Stats → Export → Wallet/QR stack. The `users.tier` column + the
`UserTiers` constants already exist (M2), so this milestone adds behaviour, not the tier column.

## Objective

Implement `The-ideal.md` §3.11 (Hạng người dùng Premium/Free) and §4 rule 9:

- **Free is the default; Free = basic features WITH usage limits; Premium = all features (incl. the
  "mở rộng" group) + NO limits.**
- **Hitting a limit only blocks creating NEW data, with a clear Vietnamese message — it never
  locks / hides / deletes existing data, even after a downgrade** (§3.11 last bullet + §4 rule 9). The
  guards are therefore **create-only and count-based**, so existing data is structurally never affected.
- **Premium feature-gating:** the "mở rộng" group (wallet & QR now; non-CSV export formats when they
  exist later) is available only to Premium; a Free user hitting it gets a clear, distinct rejection.
- **Tier → principal:** `AuthenticatedUser` carries the current `Tier` so the create-guards and the
  feature-gate can read it without an extra DB read per call.
- **Config-driven limit numbers** (`Tiers:Free:` in appsettings) so the numbers are tunable without a
  code change / redeploy-to-change-constants.

### Scope framing (confirmed at the roadmap level — NOT reopened here)

Per `planning/agent-dev-team.md` roadmap item 10 (refined 2026-07-14) + the 2026-07-14 scope-expansion
decision:

- **M10 = Free-tier limit enforcement + Premium feature-gating + tier→principal propagation.**
- **M10 does NOT build any payment / upgrade endpoint.** The paid-upgrade path is delivered by **M11's
  manual admin-grant** (an admin flips `users.tier` → Premium). **Self-serve VNPay is a documented
  future seam, not built here.**
- This doc therefore contains **no** pricing, checkout, gateway, or self-upgrade design.

## Background

Grounded in the live code (read 2026-07-14):

- **The tier column already exists (M2).** `Database/Entities/User.cs` has `string Tier`;
  `Database/Entities/Partials/User.cs` maps it `tier` `varchar(16)` `HasDefaultValue(UserTiers.Free)`
  and the ctor sets `Tier = UserTiers.Free`. `Constants/UserTiers.cs` = `Free = "FREE"`,
  `Premium = "PREMIUM"`. **No migration is needed for the tier column** (see Impact Analysis — a
  migration is needed only if the Premium-expiry OQ chooses a `premium_expires_at` column).
- **The principal today carries only `Id` + `Username`.** `Auth/AuthenticatedUser.cs` has `Id`
  (user UUID) + `Username`, with `ToClaims()` / `FromPrincipal(...)`. It is materialized by
  `Auth/TokenValidator.cs` from a `TokenWhitelistEntry`. `Auth/Abstractions/ITokenWhitelistStore.cs`'s
  `TokenWhitelistEntry(UserId, ExpiresAt, Username, TokenType, PairUuid)` is built either from Redis
  (cache) or from `AuthTokenRepository.GetByHashWithUserAsync` (DB fallback), whose `AuthTokenLookup`
  projection already joins `token.User` (so `token.User.Tier` is a one-line projection add). Controllers
  read the principal via `AppController.AuthenticatedUser` → `IContextAuthenticated` →
  `AuthenticatedUser.FromPrincipal`.
- **The three create paths that get limit guards** (all `[ScopedService]`, primary-ctor services):
  - `Services/Api/Members/MembersService.CreateAsync` — the API create path. **The owner-rep is
    bootstrapped via a different path** (`IRegistrationBootstrapStep` / `memberRepository.CreateAsync`
    directly, and the `EnsureOwnerRepresentativeForAllAsync` backfill), **never** through
    `MembersService.CreateAsync` — so it is **automatically exempt** from the guard.
  - `Services/Api/Events/EventsService.CreateAsync` — limit = **OPEN events** (count `is_closed = false`;
    closed events never count, so a user can always create after closing).
  - `Services/Api/Expenses/ExpensesService.CreateAsync` — limit = **expenses per calendar month**
    (count by `expense_time` in the month window).
- **DB-side count pattern already established (M7):** `Repositories/StatsRepository.cs` uses
  `.CountAsync(ct)` / `.SumAsync(...)` pushed into MariaDB over resource-owned `IQueryable`s. The new
  count repo methods mirror this exactly (no `Include`-then-count-in-memory; the M6 review flagged the
  `ListByUserAsync` count-via-`Include`).
- **The M9 tier-gate seam is ready to activate (OQ14, shipped).**
  `Services/Api/Wallet/WalletQrService.cs` was deliberately left ungated with a single entry point per
  operation (`GenerateExpenseQrAsync`, `GenerateEventQrAsync`); its XML doc says "No tier gate at M9 -
  this service is the single seam a later tier mechanism can gate (OQ14)." The wallet CRUD lives in
  `Services/Api/Wallet/BankAccountsService.cs`.
- **Config-read precedent:** `Auth/TokenService.cs` reads `configuration.GetValue("Auth:AccessTokenLifetime", default)`
  from the `Auth:` section of `appsettings.json`. There is currently **no strongly-typed options class**
  in this repo (the sibling `quick-ordering` uses both `IOptions<T>` and a DiDecoration `[Option]`
  attribute).
- **Error-code blocks:** `Constants/ErrorCodes.cs` uses 1xxx–12xxx (12xxx = Wallet/QR).
  `Exception/ErrorException.cs` maps each code → HTTP status in `GetDefaultHttpStatus`. **13xxx is the
  next free block** for Tiers (M11 Admin will claim a later block; per the roadmap M11 text it referenced
  "13xxx Admin" — that reference predates this doc claiming 13xxx for Tiers; the orchestrator should note
  M11 will take the next free block after Tiers).
- Expenses are **hard-deleted** (M5), so an expenses-per-month count is just live rows in the window.
  Members are **soft-deleted** (a deleted member frees a slot). Events are **hard-deleted** and only
  while OPEN.

## Requirements

From `The-ideal.md` §3.11, §4 rule 9, §5, and the roadmap M10 refinement:

- **R1 — Tier on the principal:** `AuthenticatedUser` gains `Tier` (a `UserTiers` value), populated on
  the auth path, exposed via `ToClaims()` / `FromPrincipal(...)`. A missing/unknown tier claim resolves
  to `FREE` (fail-safe).
- **R2 — Free create-limits (create-only, count-based, Premium bypasses):**
  - `MembersService.CreateAsync` — reject the (N+1)-th member for a Free user.
  - `EventsService.CreateAsync` — reject the (N+1)-th **open** event for a Free user.
  - `ExpensesService.CreateAsync` — reject the (N+1)-th expense **in the current calendar month** for a
    Free user.
  - Each backed by a **new DB-side count repo method**. Premium bypasses every limit.
  - **The owner-rep bootstrap create path is exempt** (it never calls the guarded service method).
- **R3 — §4.9 guarantee (must hold + be tested):** limits **only** block create. Reads, edits (rename /
  update / set-default / assign-event / settled), and deletes of **existing** data are **always allowed**,
  including when a user is over-limit (e.g. after a Premium→Free downgrade). No existing row is ever
  locked, hidden, or deleted by tier logic.
- **R4 — Premium feature-gating (OQ5b read-vs-mutation split):** gate the "mở rộng" group behind
  Premium — the wallet **mutations** (`BankAccountsService` create / update / delete / set-default) +
  both QR operations (`WalletQrService`); wallet **reads** (`BankAccountsService` list / get) stay open
  to Free. Leave a documented hook for non-CSV export formats when they exist (CSV stays Free). A Free
  user hitting a gated feature gets a clear, distinct rejection (403 `13003`).
- **R5 — Config-driven limits:** a `Tiers:Free:` appsettings section (`MaxMembers`, `MaxOpenEvents`,
  `MaxExpensesPerMonth`) read the same way as `Auth:`.
- **R6 — Cross-cutting:** Vietnamese user-facing messages naming the limit hit + that Premium removes it;
  new 13xxx error block; resource-owned 404-never-403 unchanged; no schema change unless the expiry OQ
  adds a column.

## Open Questions

> **All 11 answered by the user at the 2026-07-14 checkpoint** — 10 accepted at the recommended option
> (a); **OQ1 the user chose option (c)** (the more-generous limits) and **OQ5 the user chose option (b)**
> (gate only wallet mutations + QR; wallet reads stay open to Free). The binding answers are annotated
> inline below; the full options/trade-offs are preserved for the record and mirrored in the Decision
> Log. **No open questions remain — implementation can start.** The Implementation Plan, config section,
> error-code table, gating section, and test list below are synced to these answers.

**OQ1 — Concrete Free limit numbers (THE headline question; the spec defers these to this doc).**
> ~~**OQ1**~~ → **Answered 2026-07-14 (option c — USER OVERRODE the (a) recommendation):**
> `MaxMembers = 25`, `MaxOpenEvents = 10`, `MaxExpensesPerMonth = 200` (config-driven, tunable).
The spec §3.11/§5 fixes only the principle ("tối đa N thành viên, M đợt đang mở, K phiếu/tháng") and
leaves the numbers here. All three are config-driven (`Tiers:Free:` — OQ10), so they can change later
without a code change.
- **(a) [recommended]** `MaxMembers = 10`, `MaxOpenEvents = 3`, `MaxExpensesPerMonth = 50`. Rationale:
  a Free household/friend group can run a couple of trips at once and log ~1.5 expenses/day, which is
  comfortably usable while leaving Premium a real reason to exist. Trade-off: any fixed number is a
  product judgement; these are deliberately generous-but-bounded and tunable via config.
- **(b)** Stricter (e.g. `MaxMembers = 5`, `MaxOpenEvents = 1`, `MaxExpensesPerMonth = 20`). Trade-off:
  a stronger upgrade nudge, but risks Free feeling unusable for a normal trip (a 6-person trip couldn't
  be logged at all).
- **(c)** More generous (e.g. `MaxMembers = 25`, `MaxOpenEvents = 10`, `MaxExpensesPerMonth = 200`).
  Trade-off: Free feels unlimited, weakening the Premium value proposition.

**OQ2 — Which resources are limited?**
> ~~**OQ2**~~ → **Answered 2026-07-14 (option a):** limit only the three spec resources (members, open
> events, expenses/month); categories & tags are NOT limited in M10.

The spec's "cơ bản (Free)" group lists members, categories, tags, expenses & shares, settled, events,
balance, stats, audit, CSV export "kèm hạn mức", but the only concrete examples are members / open
events / expenses-per-month. The roadmap M10 scope names exactly those three.
- **(a) [recommended]** Limit **only** the three spec examples (members, open events, expenses/month).
  Categories and tags are **not** limited in M10. Trade-off: fewer guards, matches the roadmap's stated
  scope and the spec's own examples; categories/tags can gain limits later (config seam is already
  generic). A user could create many categories/tags on Free — acceptable, they're low-cost metadata.
- **(b)** Also limit categories and tags (add `MaxCategories`, `MaxTags` + guards in their create
  paths). Trade-off: closes the "unlimited metadata" gap, but adds two more guards + two count methods
  + the default-category invariant interaction (deleting to get under a limit must never touch the
  default-category-always-one rule) for little product value.

**OQ3 — What counts toward the member limit?**
> ~~**OQ3**~~ → **Answered 2026-07-14 (option a):** count ACTIVE (non-soft-deleted) members, owner-rep
> INCLUDED; a soft-delete frees a slot.

Members are soft-deleted; there is exactly one owner-rep.
- **(a) [recommended]** Count **active (non-soft-deleted) members, owner-rep included**. So with
  `MaxMembers = 10` a Free user has the owner-rep + 9 more; soft-deleting a member frees a slot (§4.9:
  the deleted member's history is untouched, it just no longer occupies a live slot). Trade-off:
  "your limit counts everyone currently in your ledger" is the intuitive reading and keeps the count a
  single cheap `WHERE is_deleted = 0` query.
- **(b)** Count active members **excluding** the owner-rep (the owner-rep is "free"). Trade-off: a hair
  more generous, but "N members" then means "N+1 rows", which is less intuitive and needs an extra
  predicate.
- **(c)** Count **all-time** members including soft-deleted. Trade-off: soft-deleting would NOT free a
  slot, which edges toward penalising existing/deleted data — mildly against the §4.9 spirit. Not
  recommended.

**OQ4 — Expenses-per-month: counting basis + month boundary timezone.**
> ~~**OQ4**~~ → **Answered 2026-07-14 (option a):** count by `expense_time`, month defined in local +7
> (matching M8's fixed +7), converted to a UTC `[from, to)` window.

The roadmap says "count `expense_time >= start-of-month`". Two sub-decisions:
- **(a) [recommended]** Count by **`expense_time`** (the user-entered date of the expense), and define
  "the month" in **local +7 (Vietnam) time** to match the app's display timezone (M8 OQ6 uses fixed +7),
  converting the +7 month `[start, nextStart)` window to UTC for the `expense_time` (UTC-stored) query.
  Trade-off: matches the roadmap wording and the user's mental "this month"; but because it counts by
  `expense_time`, a user could log many expenses dated into a future/past month to sidestep the current
  month's quota — accepted as low-risk (they still can't exceed the number *for that month*).
- **(b)** Count by **`created_at`** (actual creation timestamp), +7 month window. Trade-off: measures
  true creation rate and can't be gamed by backdating, but "K phiếu/tháng" then depends on when you
  typed it, not the expense date — surprising when catching up on last month's receipts.
- **(c)** Define the month in **UTC** (no +7 shift). Trade-off: simpler (no offset), but a late-evening
  Vietnam expense near month-end lands in the "wrong" month for the user; inconsistent with M8's +7
  display choice.

**OQ5 — Final membership of the Premium-only "mở rộng" group (what M10 gates now).**
> ~~**OQ5**~~ → **Answered 2026-07-14 (option b — USER OVERRODE the (a) recommendation):** gate only
> wallet **mutations** + QR; **wallet READS stay open to Free.** Concretely: `BankAccountsService`
> create / update / delete / **set-default** → Premium-gated (Free → 403 `13003`);
> `BankAccountsService` list / get → allowed for Free; `WalletQrService` both QR methods (expense QR +
> event QR) → Premium-gated. CSV export stays Free; non-CSV export formats gated when they exist (future
> hook). The read-vs-mutation split is made explicit in the gating section + tests below.
- **(a) [recommended]** Gate **wallet CRUD (`bank-accounts` — all of list/get/create/update/set-default/
  delete) + both QR endpoints (`/expenses/{uuid}/qr`, `/events/{uuid}/qr`)** as Premium now; leave a
  documented (not-yet-reachable) hook so non-CSV export formats become Premium when they're added; **CSV
  export stays Free**. Trade-off: matches §3.11's "mở rộng" list (ví & QR, extra export formats) exactly
  and gates the whole group at its single seams. Question within: should **reading/listing** existing
  bank accounts also be gated (a downgraded user who added accounts on Premium) — recommend **gate the
  whole wallet CRUD including reads** for a clean "this feature is Premium" story, but note this means a
  downgraded user can't view their own previously-added accounts (they aren't deleted — §4.9 is about
  the ledger data; wallet accounts are not ledger history). If the user prefers, reads could stay open
  (see OQ5b).
- **(b)** Gate only the **create/mutate** wallet operations + QR, leaving wallet **reads** open so a
  downgraded user can still see (but not add) accounts. Trade-off: gentler and arguably more §4.9-aligned
  (don't hide existing data), but "wallet is a Premium feature" becomes fuzzy (you can see it but not use
  it). **This interacts with OQ6 (expiry) and R3 — flag explicitly.**
- **(c)** Gate a narrower set now (only QR, not wallet CRUD). Trade-off: lets Free users manage bank
  accounts they can't yet use for QR — inconsistent; not recommended.

**OQ6 — Premium expiry model.**
> ~~**OQ6**~~ → **Answered 2026-07-14 (option a):** indefinite-until-revoked — NO `premium_expires_at`
> column, **NO migration this milestone**; expiry a future seam.
- **(a) [recommended] Indefinite-until-revoked.** Premium stays until an admin (M11) flips it back to
  Free; **no `premium_expires_at` column, no migration, no downgrade job.** Trade-off: simplest, fits
  M11's manual-grant model exactly; time-boxed subscriptions become a future seam (add the column + a
  lazy/scheduled check then). Note the future seam explicitly.
- **(b)** Add a `premium_expires_at` column + lazy downgrade (on read, if expired → treat as Free) and/or
  a scheduled `BackgroundService` sweep. Trade-off: supports real subscription periods now, **but forces
  a migration this milestone** and a downgrade mechanism for a paid-upgrade path that doesn't exist until
  M11's manual grant — premature. Not recommended for M10.

**OQ7 — Error codes + HTTP for limit-breach vs feature-gate (13xxx block contents).**
> ~~**OQ7**~~ → **Answered 2026-07-14 (option a):** granular 13xxx block — `13000 MemberLimitReached`
> (400), `13001 OpenEventLimitReached` (400), `13002 MonthlyExpenseLimitReached` (400),
> `13003 PremiumFeatureRequired` (403, distinct from generic `Forbidden 1004` so clients show an
> upsell); extend `GetDefaultHttpStatus`. Messages = clear Vietnamese naming the limit + that Premium
> removes it.
- **(a) [recommended] Distinct, granular codes** (consistent with the codebase's per-state codes, e.g.
  12xxx): `13000 MemberLimitReached` (400), `13001 OpenEventLimitReached` (400),
  `13002 MonthlyExpenseLimitReached` (400), `13003 PremiumFeatureRequired` (403). Limit breaches are
  business-rule rejections → **400** (consistent with every other business-rule 400 in the codebase:
  3001, 4002, 9001…). The premium-feature-gate is authorization-shaped → **403** with a **distinct**
  code (not the generic `Forbidden 1004`) so the client can show an "upgrade to Premium" upsell rather
  than a generic "no permission". Extend `ErrorException.GetDefaultHttpStatus` accordingly. Trade-off:
  four new codes, but each is machine-distinct and drives a specific client action.
- **(b)** A **single** `13000 TierLimitReached` (400) whose message names the limit, plus reuse
  `Forbidden 1004` (403) for the feature-gate. Trade-off: fewer codes, but the client can't
  programmatically tell which limit was hit, and the upsell can't be distinguished from a real
  permission error.
- **(c)** Use **402 Payment Required** for the feature-gate and/or limit breach. Trade-off: semantically
  "pay to proceed", but the codebase has no 402 precedent, self-serve payment doesn't exist (M11 manual
  grant), and 402 is under-supported by clients — not recommended.

**OQ8 — How does the current tier reach the request / principal?**
> ~~**OQ8**~~ → **Answered 2026-07-14 (option a):** add `Tier` to `TokenWhitelistEntry` +
> `AuthenticatedUser` (+ `ToClaims`/`FromPrincipal`, default FREE when the claim is absent), populated by
> `TokenValidator` from the joined `token.User.Tier` — no extra per-request DB read. **Staleness
> contract (the seam M11 will use):** an M11 admin grant reflects on the next token issuance/refresh or
> within ≤ the access-token TTL (~30 min); M11 may bust the cached whitelist entry for instant effect.
- **(a) [recommended] Add `Tier` to `TokenWhitelistEntry` + `AuthenticatedUser` (populated on the auth
  path).** `AuthTokenRepository.GetByHashWithUserAsync` already joins the user, so add `token.User.Tier`
  to the projection; `TokenValidator` sets `AuthenticatedUser.Tier`; add a tier claim to
  `ToClaims`/`FromPrincipal`. The guard reads it from `IContextAuthenticated` — **no extra DB read per
  request**. Trade-off / **staleness note (M11 interaction):** the tier is captured into the Redis
  whitelist entry and cached for up to the access-token TTL (30 min); on a cache miss the DB fallback
  re-reads the live `users.tier`. So an M11 admin grant (Free→Premium) reflects on the next token
  issuance/refresh or within ≤ one access-TTL (cache expiry) — **acceptable for an upgrade** (the user
  waits ≤ 30 min or re-logs in). If instant reflection is required, M11 can bust the user's cached token
  entries on grant (a documented small extension), or fall back to OQ8b.
- **(b)** Per-request DB lookup of `users.tier` (no principal change; the guard reads
  `IUserRepository.GetTierAsync(userUuid)`). Trade-off: **always fresh** (an admin grant applies on the
  very next request), but adds a DB read on every guarded create (and, if the feature-gate needs it, on
  gated reads) — defeating the token cache's purpose for this field. The freshness only matters for the
  M11 grant edge; (a) plus optional cache-bust covers it more cheaply.
- **(c)** Lazy DB read only inside the guard service (no principal change, read only on create/gated
  ops). Trade-off: fresh + cheap-ish (only on the guarded operations, not every request), but the
  roadmap explicitly asks for `Tier` on `AuthenticatedUser`; (a) satisfies that and keeps reads off the
  hot path.

**OQ9 — Where does the guard/gate logic live?**
> ~~**OQ9**~~ → **Answered 2026-07-14 (option a):** one shared `Services/Api/Tiers/TierService.cs`
> (`ITierService`, `[ScopedService]`) centralizes the limit checks + feature gate + messages; the three
> create-services and the two wallet services call into it. No DI cycle (TierService depends on the count
> repos + `IContextAuthenticated` + config, not on the feature services).
- **(a) [recommended] One shared `ITierService` (`Services/Api/Tiers/TierService.cs`)** exposing
  `EnsureCanCreateMemberAsync` / `EnsureCanCreateOpenEventAsync` / `EnsureCanCreateExpenseAsync` +
  `EnsurePremium(feature)`; it reads the current tier (OQ8) + the config limits + the count repo
  methods, and throws the 13xxx `ErrorException`s. The three create services + `BankAccountsService` +
  `WalletQrService` each make a one-line call. Trade-off: centralizes all tier logic in one testable
  place (mirrors the "single seam" ethos), and Premium-bypass / message wording live once. No DI cycle
  (the guard depends on repositories, not on the calling services).
- **(b)** Inline the checks in each service. Trade-off: fewer types, but scatters the tier rules,
  duplicates the Premium-bypass + message wording, and is harder to unit-test in isolation.

**OQ10 — Config shape for the limit numbers.**
> ~~**OQ10**~~ → **Answered 2026-07-14 (option a):** config via the existing `Auth:`-style raw
> `GetValue("Tiers:Free:MaxMembers", ...)` pattern (no new options-class pattern).
- **(a) [recommended] Mirror the existing `Auth:` pattern** — raw `configuration.GetValue("Tiers:Free:MaxMembers", 10)`
  etc., read once in `TierService` (with sensible in-code defaults). Trade-off: consistent with the only
  config-read precedent in this repo (`TokenService`), zero new plumbing; less "typed" than an options
  class.
- **(b)** Introduce the project's **first strongly-typed options class** (`Configs/TierLimitOptions.cs`
  bound from `Tiers:Free`, injected via `IOptions<TierLimitOptions>`, optionally the DiDecoration
  `[Option]` attribute used in quick-ordering). Trade-off: cleaner/typed and validate-able, but
  introduces a new pattern this repo hasn't used — a small architectural decision worth the user's call.

**OQ11 — Confirm the §4.9 downgrade / over-limit behaviour (nothing changes for existing data).**
> ~~**OQ11**~~ → **Answered 2026-07-14 (option a):** confirmed + explicitly tested — a downgraded /
> over-limit Free user can still read / edit / delete ALL existing data; only new creates are blocked;
> nothing is locked / hidden / deleted.
- **(a) [recommended] Confirm + test explicitly:** when a Premium user is downgraded to Free while over
  a limit (e.g. 40 members, 15 open events, 300 expenses this month — all over 25 / 10 / 200), **all
  reads, edits, and deletes of existing data continue to work**; only **new** creates are blocked until
  they're back under the limit.
  No row is locked/hidden/deleted by tier logic. This is the §3.11/§4.9 guarantee and gets dedicated
  tests. Trade-off: none — it's the spec. (The only nuance is OQ5's wallet-read gating, which is about a
  Premium *feature*, not ledger data — flagged there.)
- **(b)** Any behaviour that touches existing data on downgrade. **Rejected by the spec** — listed only
  for completeness.

## Assumptions

> **Confirmed by the user at the 2026-07-14 checkpoint** together with the 11 Open Questions — these are
> now decisions, not vetoable assumptions. Each is derived from the spec, prior decisions, and the
> shipped code.

- All guarded endpoints are already authenticated (anonymous → 401 upstream); the guard only runs for a
  known user.
- A user with no/unknown tier claim is treated as **Free** (fail-safe default; matches the DB default).
- The owner-rep member and the suggested-category bootstrap run through their own seams
  (`IRegistrationBootstrapStep` / repository create), **not** the guarded API service methods, so they
  are inherently exempt — no special-casing inside the guard is required.
- QR generation and exports are **reads** and are **not** limit-gated (§4.9); the wallet/QR gate is a
  **Premium feature-gate** (OQ5), not a usage limit.
- No new NuGet dependency. No `AppController` edit. Money/decimal rules are untouched (no money in M10).
- **No EF migration** unless OQ6 chooses (b) `premium_expires_at`.

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/`. New services use DiDecoration `[ScopedService]`.
> All user-facing strings + Swagger summaries are Vietnamese. **Synced to the 2026-07-14 checkpoint
> answers** — option (a) throughout except **OQ1 = (c)** (limits 25 / 10 / 200) and **OQ5 = (b)** (gate
> wallet mutations + QR only; wallet reads stay open to Free). **NO EF migration / NO schema change**
> this milestone (the `tier` column already exists; OQ6 = no expiry column).

### Step 1 — Tier on the principal (OQ8a)

1. `Auth/Abstractions/ITokenWhitelistStore.cs` — add `string Tier` to the `TokenWhitelistEntry` record
   (positional). All constructor sites updated (Step 1.2–1.4).
2. `Repositories/AuthTokenRepository.cs` — add `string Tier` to the `AuthTokenLookup` record and to the
   `GetByHashWithUserAsync` projection (`token.User.Tier`).
3. `Auth/TokenWhitelistStore.cs` (`LookupAsync`) — pass `row.Tier` into the `TokenWhitelistEntry`.
   `Auth/TokenService.cs` (`IssueAsync`) — the two `TryCacheAsync` entries carry the tier; add a `tier`
   parameter to `IssueAsync`/`RefreshAsync` sourced from the login/refresh lookup (the auth service
   already loads the user on login — thread its `Tier` through; on refresh, `AuthTokenLookup.Tier` is
   available). *(Confirm the exact login call site during implementation; the projection add makes the
   value available on every path.)*
4. `Auth/TokenValidator.cs` — set `Tier = entry.Tier` on the returned `AuthenticatedUser`.
5. `Auth/AuthenticatedUser.cs` — add `public required string Tier { get; init; }` (or default `FREE`);
   add a tier claim to `ToClaims()` (a private claim type constant, e.g. `"tier"`); read it in
   `FromPrincipal(...)` defaulting to `UserTiers.Free` when the claim is absent (fail-safe, back-compat
   for tokens issued before this change).

### Step 2 — DB-side count repo methods (mirror StatsRepository, OQ9/count basis OQ3/OQ4)

- `Repositories/MemberRepository.cs` (`IMemberRepository`) — add
  `Task<int> CountActiveByUserAsync(string userUuid, CancellationToken)` → `Query().Where(m => m.User.Uuid == userUuid).CountAsync(ct)`
  (soft-delete filter already excludes deleted; owner-rep included — OQ3a).
- `Repositories/EventRepository.cs` (`IEventRepository`) — add
  `Task<int> CountOpenByUserAsync(string userUuid, CancellationToken)` →
  `Query().Where(e => e.User.Uuid == userUuid && !e.IsClosed).CountAsync(ct)`.
- `Repositories/ExpenseRepository.cs` (`IExpenseRepository`) — add
  `Task<int> CountByUserInRangeAsync(string userUuid, DateTime fromUtcInclusive, DateTime toUtcExclusive, CancellationToken)`
  → `Query().Where(x => x.User.Uuid == userUuid && x.ExpenseTime >= from && x.ExpenseTime < to).CountAsync(ct)`
  (the service computes the +7 calendar-month window and converts to the UTC `[from, to)` bounds — OQ4a).

### Step 3 — `TierService` (the shared guard + gate; OQ9a)

`Services/Api/Tiers/TierService.cs` — `ITierService` + sealed impl (`[ScopedService(typeof(ITierService))]`,
primary ctor injecting `IContextAuthenticated`, `IMemberRepository`, `IEventRepository`,
`IExpenseRepository`, `IConfiguration`):

- Reads config once (OQ10a): `MaxMembers` (default 25), `MaxOpenEvents` (default 10),
  `MaxExpensesPerMonth` (default 200) from `Tiers:Free:` (OQ1c numbers).
- `private bool IsPremium` — `IContextAuthenticated.AuthenticatedUser?.Tier == UserTiers.Premium`
  (null/unknown → Free — fail-safe).
- `Task EnsureCanCreateMemberAsync(string userUuid, CancellationToken)` — if `IsPremium` return; else if
  `await memberRepository.CountActiveByUserAsync(...) >= MaxMembers` throw
  `ErrorException(ErrorCodes.MemberLimitReached, "...")` (message names the limit + Premium — OQ7a).
- `Task EnsureCanCreateOpenEventAsync(...)` — analogous with `CountOpenByUserAsync` / `MaxOpenEvents` /
  `OpenEventLimitReached`.
- `Task EnsureCanCreateExpenseAsync(...)` — compute the current +7 calendar month `[from, to)` in UTC
  (`AppDateTime.Now` + `TimeSpan.FromHours(7)` → month start → back to UTC bounds; reuse the M8 fixed-+7
  convention, no `TimeZoneInfo`), call `CountByUserInRangeAsync`, compare `>= MaxExpensesPerMonth`, throw
  `MonthlyExpenseLimitReached`.
- `void EnsurePremiumFeature(string featureNameVi)` — if not `IsPremium` throw
  `ErrorException(ErrorCodes.PremiumFeatureRequired, $"Tính năng {featureNameVi} chỉ dành cho tài khoản Premium...")`
  (403 — OQ5/OQ7a).

### Step 4 — Wire the limit guards into the three create services (R2, owner-rep exempt)

- `Services/Api/Members/MembersService.cs` — inject `ITierService`; in `CreateAsync` call
  `await tierService.EnsureCanCreateMemberAsync(userUuid, ct)` **before** creating (after validation).
  `EnsureOwnerRepresentativeForAllAsync` and any bootstrap path are untouched (exempt — R2).
- `Services/Api/Events/EventsService.cs` — inject `ITierService`; guard `CreateAsync` with
  `EnsureCanCreateOpenEventAsync`.
- `Services/Api/Expenses/ExpensesService.cs` — inject `ITierService`; guard `CreateAsync` with
  `EnsureCanCreateExpenseAsync`. `UpdateAsync` / `AssignEventAsync` / `SetSettledAsync` / `DeleteAsync`
  are **not** guarded (R3 — edits/deletes always allowed).

### Step 5 — Premium feature-gate (activate the M9 seam; R4 / OQ5b — read-vs-mutation split)

- `Services/Api/Wallet/WalletQrService.cs` — inject `ITierService`; call
  `tierService.EnsurePremiumFeature("tạo mã QR")` at the top of `GenerateExpenseQrAsync` **and**
  `GenerateEventQrAsync` (before resolving the destination account). Both QR operations are
  Premium-gated. This is the seam the M9 XML doc reserved.
- `Services/Api/Wallet/BankAccountsService.cs` — inject `ITierService`; per **OQ5b** gate only the
  **mutating** methods:
  - **Premium-gated** (Free → 403 `13003`): `CreateAsync`, `UpdateAsync`, `DeleteAsync`,
    `SetDefaultAsync` — each calls `EnsurePremiumFeature("ví ngân hàng")` at the top.
  - **Open to Free** (no gate): `ListAsync`, `GetAsync` — a downgraded user can still **read** the bank
    accounts they added on Premium (they just can't add/change/delete or use them for QR while Free).
    This keeps the §4.9 spirit for previously-entered data while still gating the "use the wallet"
    actions.
- **Documented hook (no code yet):** add an XML-doc note in `Services/Api/Export/ExportService.cs` (or
  the format resolver) that when a non-CSV `ExportFormat` is added, its branch must call
  `EnsurePremiumFeature(...)`; CSV stays Free. No behaviour change now (only CSV exists).

### Step 6 — Config

`appsettings.json` — add:
```json
"Tiers": {
  "Free": {
    "MaxMembers": 25,
    "MaxOpenEvents": 10,
    "MaxExpensesPerMonth": 200
  }
}
```
(Numbers = OQ1c; `TierService` supplies the same values as in-code defaults so a missing section still
works.)

### Step 7 — Error codes + messages (OQ7a)

Append to `Constants/ErrorCodes.cs` — **13xxx block = Tiers** (never renumber):

| Code | Name | HTTP | Message (Vietnamese) |
|---|---|---|---|
| `13000` | `MemberLimitReached` | 400 | "Tài khoản Free chỉ được tạo tối đa {N} thành viên. Nâng cấp Premium để bỏ giới hạn." |
| `13001` | `OpenEventLimitReached` | 400 | "Tài khoản Free chỉ được có tối đa {M} đợt đang mở. Chốt bớt đợt hoặc nâng cấp Premium để bỏ giới hạn." |
| `13002` | `MonthlyExpenseLimitReached` | 400 | "Tài khoản Free chỉ được tạo tối đa {K} phiếu chi tiêu mỗi tháng. Nâng cấp Premium để bỏ giới hạn." |
| `13003` | `PremiumFeatureRequired` | 403 | "Tính năng {tên} chỉ dành cho tài khoản Premium. Nâng cấp để sử dụng." |

- Extend `ErrorException.GetDefaultHttpStatus`: `13000`/`13001`/`13002` → 400; `13003` → 403.
- The limit numbers in the messages are interpolated from the config values (so a config change updates
  the message too).
- **M11 heads-up (block ownership):** Tiers claims the **13xxx** block, so **M11 (Admin) must take the
  next free block, 14xxx** — the roadmap's loose "13xxx Admin" reference is superseded. The orchestrator
  should correct the M11 roadmap line accordingly.

### Step 8 — Migration (ONLY if OQ6 = (b))

- **OQ6 = (a) [recommended]: NO migration** (the tier column already exists; nothing else schema-touching).
- **OQ6 = (b) only:** add `DateTime? PremiumExpiresAt` to `User` + `Partials/User.cs` mapping
  (`premium_expires_at` nullable) and a migration **`AddPremiumExpiry`**; the tier read treats an expired
  Premium as Free. (Not planned unless chosen.)

### Step 9 — Tests (owned by the test-engineer; definitive list)

Reuse the shipped harness: `[Collection("AuthIntegration")]`; DB/endpoint tests use the existing
`*DbTestBase` / `*ApiTestBase` families, a unique lowercase username prefix per class, dispose-time
cascade cleanup; DB-dependent tests `[SkippableFact]` (skip when MariaDB unreachable), never EF InMemory.
**A test user's tier is set by seeding `users.tier` directly** (no upgrade endpoint exists).

**Unit (no DB — `TierService` with fake repos + a fake `IContextAuthenticated`):**
- Free at (N-1) members → `EnsureCanCreateMemberAsync` passes; at N → throws `13000`; **Premium at any
  count → passes** (bypass). Same shape for open events (`13001`) and monthly expenses (`13002`).
- `EnsureCanCreateExpenseAsync` computes the +7 calendar-month `[from, to)` window correctly (a
  month-boundary case: an expense at 2026-07-31 23:30 +7 counts in July; 2026-08-01 00:30 +7 counts in
  August).
- `EnsurePremiumFeature` — Free → `13003` (403); Premium → passes.
- Unknown/null tier → treated as Free.
- `AuthenticatedUser` — `ToClaims`/`FromPrincipal` round-trips `Tier`; a principal with no tier claim →
  `Tier == FREE` (back-compat).

**Integration (real MariaDB — count methods):**
- `MemberRepository.CountActiveByUserAsync` counts active members (owner-rep included), excludes
  soft-deleted, is user-scoped (another user's members not counted).
- `EventRepository.CountOpenByUserAsync` counts only `is_closed = false`, user-scoped.
- `ExpenseRepository.CountByUserInRangeAsync` counts only `expense_time` in `[from, to)`, user-scoped;
  an expense dated in a different month is excluded.

**Integration / Endpoint (the behavioural guarantees):**
- **Member limit:** seed a Free user at the limit → `POST /members` → 400 `13000` + the message names the
  limit; the (limit)-th succeeds, the (limit+1)-th fails. **Premium seeded → the same create succeeds**
  (bypass). Owner-rep already exists and does NOT consume an extra call beyond OQ3a's counting.
- **Open-event limit:** Free at `MaxOpenEvents` open events → `POST /events` → 400 `13001`; **closing an
  event frees a slot** and the next create succeeds; Premium bypasses.
- **Monthly-expense limit:** Free at `MaxExpensesPerMonth` this month → `POST /expenses` → 400 `13002`;
  an expense dated last month does not block this month; Premium bypasses.
- **§4.9 guarantee (R3/OQ11a — the headline safety test):** seed a Free user **over every limit** (e.g.
  40 members, 15 open events, 300 expenses this month — all over 25 / 10 / 200) → all of `GET`/list, rename/update, set-settled,
  assign-event, and delete on existing rows **succeed**; only new creates return 13000/13001/13002. No
  row is deleted/hidden by tier logic.
- **Premium feature-gate — read-vs-mutation split (R4/OQ5b):**
  - Free user → **wallet MUTATIONS** `POST /bank-accounts`, `PUT /bank-accounts/{uuid}`,
    `PUT /bank-accounts/{uuid}/default`, `DELETE /bank-accounts/{uuid}` → **403 `13003`**; and both QR
    routes `GET /expenses/{uuid}/qr`, `GET /events/{uuid}/qr` → **403 `13003`**.
  - Free user → **wallet READS** `GET /bank-accounts`, `GET /bank-accounts/{uuid}` → **allowed (200)** —
    a downgraded user can still see accounts they added on Premium.
  - **Premium user → allowed everywhere** (wallet CRUD + QR behave as in M9).
  - CSV export (`/expenses/{uuid}/export`, `/events/{uuid}/export`) stays **Free** (Free user → 200).
- **Owner-rep bootstrap exempt:** registering a brand-new user still auto-creates the owner-rep member
  even though the guarded API path is limited (bootstrap bypasses the guard) — assert it holds even when
  `Tiers:Free:MaxMembers` is overridden to `0` in the test config (registration still yields the
  owner-rep).
- **Config-driven numbers honored:** overriding `Tiers:Free:MaxMembers` in the test host's config
  changes the threshold at which `13000` triggers.

### Step 10 — Wrap-up

Update the Progress Log + Final Outcome; keep this doc synced; the orchestrator records the checkpoint
answers in the Decision Log before implementation begins.

## Impact Analysis

- **APIs:** No new endpoints. Behavioural changes to existing endpoints — `POST /members`,
  `POST /events`, `POST /expenses` may now return 400 (13000/13001/13002) for Free users at a limit; the
  wallet **mutations** (`POST`/`PUT`/`DELETE` on `api/v1/bank-accounts`, incl. `PUT .../default`) and the
  QR routes (`/expenses/{uuid}/qr`, `/events/{uuid}/qr`) may now return 403 (13003) for Free users, while
  wallet **reads** (`GET api/v1/bank-accounts`, `GET .../{uuid}`) stay open to Free (OQ5b). New Swagger
  `[SwaggerResponse]` annotations for these codes on the affected actions.
- **Database:** **No schema change / no EF migration this milestone** (OQ6a + OQ2a locked — the `tier`
  column already exists, no `premium_expires_at`, no category/tag limit columns). A `premium_expires_at`
  migration is deferred to the future expiry seam only.
- **Infrastructure:** New `Tiers:Free:` section in `appsettings.json`. No Redis/DB topology change; the
  tier now rides in the existing Redis token-cache entry (OQ8a) — a serialization-shape change to the
  cached `TokenWhitelistEntry` (old cached entries without a tier field deserialize with a null/default
  tier → treated as Free until refreshed; harmless).
- **Services:** New `Services/Api/Tiers/TierService.cs`. **Modified create-path files (limit guards):**
  `Services/Api/Members/MembersService.cs`, `Services/Api/Events/EventsService.cs`,
  `Services/Api/Expenses/ExpensesService.cs`. **Modified feature-gate files:**
  `Services/Api/Wallet/WalletQrService.cs`, `Services/Api/Wallet/BankAccountsService.cs`. **Auth path:**
  `Auth/AuthenticatedUser.cs`, `Auth/TokenValidator.cs`, `Auth/TokenWhitelistStore.cs`,
  `Auth/TokenService.cs`, `Auth/Abstractions/ITokenWhitelistStore.cs`,
  `Repositories/AuthTokenRepository.cs`. **Count methods:** `Repositories/MemberRepository.cs`,
  `Repositories/EventRepository.cs`, `Repositories/ExpenseRepository.cs`. **Errors:**
  `Constants/ErrorCodes.cs`, `Exception/ErrorException.cs`. `Controllers/*` gain only Swagger
  annotations. `AppController` untouched.
- **Documentation:** this doc; the roadmap `planning/agent-dev-team.md` M10 line closed at wrap-up; a
  future-seam note for self-serve payment (VNPay) and Premium expiry.

## Decision Log

All 11 Open Questions resolved at the **2026-07-14 user checkpoint** (9 at the recommended option (a);
**OQ1 = (c)** and **OQ5 = (b)** were user overrides).

### Decisions

- **OQ1 = (c):** Free limits `MaxMembers = 25`, `MaxOpenEvents = 10`, `MaxExpensesPerMonth = 200`
  (config-driven, tunable). Reason: keep Free comfortably usable for real trips; Premium's value is
  "no limits" + the extended features rather than tight caps.
- **OQ2 = (a):** limit only members / open events / expenses-per-month; categories & tags unlimited in
  M10. Reason: matches the spec's concrete examples and the roadmap scope; metadata is low-cost.
- **OQ3 = (a):** member limit counts active (non-deleted) members incl. owner-rep; soft-delete frees a
  slot. Reason: intuitive "everyone currently in your ledger", single cheap query, honours §4.9.
- **OQ4 = (a):** expenses-per-month counted by `expense_time`, month in local +7 → UTC `[from, to)`.
  Reason: matches the roadmap wording and M8's fixed-+7 display convention; backdating risk accepted.
- **OQ5 = (b) [override]:** gate only wallet mutations (create/update/delete/set-default) + both QR
  methods; wallet reads (list/get) stay open to Free. Reason: a downgraded user can still see accounts
  they entered on Premium (§4.9 spirit) while the "use the wallet" actions remain Premium.
- **OQ6 = (a):** Premium indefinite-until-revoked — no `premium_expires_at`, no migration; expiry a
  future seam. Reason: fits M11's manual-grant model; avoids a premature downgrade mechanism.
- **OQ7 = (a):** granular 13xxx codes — `13000`/`13001`/`13002` (400) + `13003 PremiumFeatureRequired`
  (403, distinct from `Forbidden 1004`). Reason: each state is machine-distinct and drives a specific
  client action (incl. an upsell).
- **OQ8 = (a):** `Tier` on `TokenWhitelistEntry` + `AuthenticatedUser`, from the joined `token.User.Tier`;
  default FREE when the claim is absent. Reason: no extra per-request DB read; the ≤ access-TTL staleness
  is acceptable for upgrades. **M11 seam:** M11 may bust the cached whitelist entry for instant effect.
- **OQ9 = (a):** one shared `ITierService` centralizes limits + gate + messages; no DI cycle. Reason:
  single testable seam, no duplicated Premium-bypass/message logic.
- **OQ10 = (a):** config via the existing `Auth:`-style raw `GetValue("Tiers:Free:...")` pattern.
  Reason: consistent with the repo's only config-read precedent; no new options pattern.
- **OQ11 = (a):** the §4.9 over-limit/downgrade guarantee is confirmed and explicitly tested (existing
  data always readable/editable/deletable; only new creates blocked). Reason: it is the spec.

### Cross-cutting decisions

- **NO EF migration / NO schema change this milestone** (the `tier` column already exists; OQ6a = no
  expiry column; OQ2a = no category/tag limit columns).
- **Tiers claims the 13xxx error block → M11 (Admin) must take 14xxx.** The roadmap's loose "13xxx Admin"
  reference is superseded; the orchestrator should correct the M11 roadmap line.

### Inherited decisions (NOT reopened)

- Domain terms fixed (§5): Premium/Free, member, expense, share, event, wallet/bank account, settled.
- Free is the registration default; §4.9 "limits only block new creates, never touch existing data".
- Payment = **M11 manual admin-grant only**; self-serve VNPay is a documented future seam (roadmap
  2026-07-14).
- Resource-owned 404-never-403 (§4.1); `AppController` LOCKED; DB-side aggregation/count pattern (M7);
  the M9 `WalletQrService` tier-gate seam (OQ14).

## Progress Log

### 2026-07-14

- Drafted the M10 (Tiers Premium/Free) planning doc. Read the spec (§3.11, §4 rule 9, §5), CLAUDE.md,
  the roadmap (items 10 refined + 11), the M9 wallet/QR doc (the tier-gate seam OQ14), and the live code:
  `User`/`Partials/User`, `UserTiers`, the full auth path (`AuthenticatedUser`,
  `OpaqueTokenAuthenticationHandler`, `TokenValidator`, `TokenService`, `TokenWhitelistStore`,
  `ITokenWhitelistStore`, `AuthTokenRepository`, `IContextAuthenticated`, `AppController`), the three
  create services (`MembersService`, `EventsService`, `ExpensesService`) + their repositories, the M7
  `StatsRepository` (DB-side count pattern), `WalletQrService`, `BankAccountsService` (seam),
  `ErrorCodes`, `ErrorException`, `appsettings.json`, and the quick-ordering options precedent.
- Confirmed: the `tier` column already exists → **no migration under the recommended options**; the
  owner-rep bootstrap path bypasses `MembersService.CreateAsync` → **inherently exempt**; the M9
  `WalletQrService` seam is ready to gate; 13xxx is the next free error block.
- Wrote 11 Open Questions (each with options + trade-offs + a recommended option (a)); Implementation
  Plan (tier→principal → count methods → `TierService` → guards in the 3 create services → Premium gate
  activating the M9 seam → config → error codes → migration only-if-expiry → definitive test list);
  Impact Analysis; this Progress Log.
- Decision Log intentionally left pending; OQs open for the user checkpoint.

- **All 11 Open Questions answered by the user at the 2026-07-14 checkpoint** — 9 at the recommended
  option (a); **OQ1 = (c)** (limits 25 / 10 / 200) and **OQ5 = (b)** (gate wallet mutations + QR only,
  wallet reads open to Free) were user overrides. Annotated each OQ inline, filled the Decision Log
  (with the 14xxx-for-M11 note), confirmed the Assumptions, and synced the Implementation Plan
  (`TierService` defaults 25/10/200), the config JSON, the gating step (read-vs-mutation split), and the
  test list to the answers. Confirmed **NO EF migration / NO schema change** this milestone. **Doc
  unblocked — implementation can start.**

- **Implementation completed (2026-07-14).**
  - **Tier → principal (OQ8a).** `Tier` added to `TokenWhitelistEntry` and `AuthTokenLookup` as a
    *trailing positional record parameter defaulting to `UserTiers.Free`* (so entries/rows cached or
    projected before M10 deserialize as FREE — fail-safe — and existing tests that build these records
    positionally keep compiling). `AuthenticatedUser` gained `Tier` (non-`required`, defaults FREE) with
    a private `"tier"` claim written by `ToClaims()` and read by `FromPrincipal(...)` (missing → FREE).
    `AuthTokenRepository.GetByHashWithUserAsync` now projects `token.User.Tier`; `TokenWhitelistStore`
    carries `row.Tier` on the DB-fallback read; `TokenValidator` sets `AuthenticatedUser.Tier` from the
    entry. `ITokenService.IssueAsync` gained a `string tier = UserTiers.Free` parameter (both cache
    writes carry it); `AuthService.LoginAsync` passes `user.Tier`, and `TokenService.RefreshAsync`
    passes `lookup.Tier`. Net effect: tier rides the Redis cache entry on BOTH the login-issue write and
    the validate-read — **no extra per-request DB read**. (M11 seam left intact; not built.)
  - **Count methods (DB-side, mirror `StatsRepository`).** `MemberRepository.CountActiveByUserAsync`
    (`Query()` = non-deleted, owner-rep included), `EventRepository.CountOpenByUserAsync`
    (`!IsClosed`), `ExpenseRepository.CountByUserInRangeAsync(from, to)` (`ExpenseTime >= from &&
    < to`) — all user-scoped `CountAsync`.
  - **`Services/Api/Tiers/TierService.cs`** (`ITierService`, `[ScopedService]`, primary ctor injecting
    `IContextAuthenticated` + the 3 count repos + `IConfiguration`; NOT the feature services → no DI
    cycle). Reads `Tiers:Free:MaxMembers|MaxOpenEvents|MaxExpensesPerMonth` via `GetValue` (defaults
    25/10/200). `IsPremium` = `AuthenticatedUser?.Tier == Premium` (null/unknown → Free). Async ensure
    methods for member/open-event/expense (Premium returns early); synchronous `void
    EnsurePremiumFeature(featureNameVi)` (per Step 3) throwing 13003. **+7 month window:**
    `AppDateTime.Now.Add(FromHours(7))` → first-of-month (Unspecified) → `AddMonths(1)` → each
    `.Subtract(FromHours(7))` back to a UTC `[from, to)` half-open window (fixed offset, no
    `TimeZoneInfo`; matches M8).
  - **Limit guards (create-only).** `EnsureCanCreate*Async` called in `MembersService.CreateAsync`,
    `EventsService.CreateAsync`, `ExpensesService.CreateAsync` AFTER validation, BEFORE the repo insert.
    Edits/deletes/assign/settled untouched. The owner-rep bootstrap path
    (`IRegistrationBootstrapStep` → `memberRepository.CreateAsync`) never calls the guarded service, so
    it is inherently exempt.
  - **Premium gate (OQ5b).** `BankAccountsService` mutations (`Create/Update/Delete/SetDefault`) call
    `EnsurePremiumFeature("ví ngân hàng")`; `List/Get` stay open. `WalletQrService` both QR methods call
    `EnsurePremiumFeature("tạo mã QR")` before resolving the destination. A documented (no-code) hook
    added in `ExportService.ResolveFormatter` for future non-CSV formats (CSV stays Free).
  - **Errors + config.** 13xxx block appended to `ErrorCodes` (13000/13001/13002 → 400, 13003 → 403)
    with `GetDefaultHttpStatus` extended; messages are Vietnamese and interpolate the configured limit.
    `Tiers:Free:` (25/10/200) added to `appsettings.json`.
  - **NO EF migration / NO schema change** — confirmed (`users.tier` already existed; OQ6 = no expiry
    column). No `migrations add` was run.
  - **Build:** `dotnet build FairShareMonApi.csproj` → 0 errors (2 pre-existing AutoMapper-13.0.1
    advisory warnings only). The `.sln` build currently fails ONLY in `FairShareMonApi.Tests` — a
    mechanical ripple from the planned contract changes: the 3 in-file fake repos
    (`FakeMemberRepository`/`FakeEventRepository`/`FakeExpenseRepository`) must implement the 3 new
    count methods, and the 3 `CreateService()` helpers (target-typed `new(...)`) must pass an
    `ITierService` for the new constructor parameter. No behavioural/assertion test is broken by the M10
    logic. Left for the test-engineer per Step 9 (implementer did not edit tests).
  - **Live smoke (real MariaDB + Redis, tier limits overridden via env vars, users prefixed
    `m10smoke`, all data + the flip done via SQL, no upgrade endpoint exists):** two runs, every
    assertion PASS.
    - Run 1 (members=3/open-events=2/expenses-per-month=2): Free create up to the limit succeeds, the
      (N+1)-th returns 400 with 13000/13001/13002 and the right Vietnamese message + interpolated number;
      soft-deleting a member and closing an event each free a slot; a last-month-dated expense does not
      count toward the current month; §4.9 — an over-limit Free user can still rename/edit/set-settled/
      delete existing rows and read lists; Free wallet mutations + both QR routes → 403 13003 while
      `GET /bank-accounts` stays 200. After a DB flip to PREMIUM + re-login all limits and the wallet/QR
      gate lift; after a DB flip back to FREE + re-login the downgraded user can still READ bank accounts
      (200) but mutations + QR return 403 13003 (§4.9-spirit read-vs-mutation split confirmed).
    - Run 2 (`Tiers:Free:MaxMembers=0`): registration still yields exactly one member — the
      owner-representative — proving the bootstrap bypasses the guard, while the guarded API member
      create returns 400 13000 (guard active). Smoke data removed; the 2 pre-existing users untouched;
      `appsettings.json` never edited for the smoke (env-var overrides only).

- **Tests authored + full suite green (2026-07-14, test-engineer).** Added **53 M10 test cases**; the
  whole suite is **931 passed / 0 failed / 0 skipped** (was 878; MariaDB + Redis reachable, nothing
  skipped), and **deterministic** (two consecutive full runs both 931/0/0). **Production code untouched**
  — only `FairShareMonApi.Tests` was modified. No production bug found.
  - **Harness ripple fixed (test project only, per protocol).** Added the 3 new count methods to the
    in-file fakes (`FakeMemberRepository.CountActiveByUserAsync`,
    `FakeEventRepository.CountOpenByUserAsync`, `FakeExpenseRepository.CountByUserInRangeAsync`) and
    threaded a new shared pass-through `Infrastructure/FakeTierService` (no-op by default; per-code throw
    switches) into the constructors of **all five** services whose signatures changed —
    Members/Events/Expenses **and** BankAccounts/WalletQr (the brief named the first three; the two
    wallet services also gained the `ITierService` param and are fixed the same way). Existing
    assertions were not weakened.
  - **Existing wallet/QR endpoint tests adapted (not weakened).** M10 gates wallet mutations + both QR
    ops behind Premium, so a fresh (Free) user can no longer perform them; `WalletQrEndpointTests` and
    `BankAccountsEndpointTests` now register Premium callers via a new
    `ExpenseApiTestBase.CreatePremiumClientAsync()` / `SetUserTierAsync()` helper (register → flip
    `users.tier` in the DB → login so the token carries PREMIUM). Their assertions are unchanged.
  - **Per-area breakdown of the new cases:**
    - *Unit — tier on principal (`AuthenticatedUserTierTests`, 6):* `Tier` round-trips through
      `ToClaims`/`ToPrincipal`/`FromPrincipal`; absent/empty tier claim → FREE (back-compat); default
      property is FREE; no-identifier principal → null.
    - *Unit — `TierService` (`TierServiceTests`, 17):* each `EnsureCanCreate*` passes below the limit and
      throws 13000/13001/13002 exactly AT the limit; PREMIUM bypasses all three (and skips the DB
      count); `EnsurePremiumFeature` → 13003 for Free, passes for Premium; null/unknown tier treated as
      Free (fail-safe); low injected config honoured + in-code default 25 when config absent; the message
      interpolates the configured number + names Premium; the expense guard computes the current +7
      calendar-month `[from, to)` UTC window (local boundaries verified).
    - *Unit — create-guard wiring in the services:* `MembersServiceTests`/`EventsServiceTests`/
      `ExpensesServiceTests` each gained one test proving a guard breach surfaces verbatim as 13000/
      13001/13002 before the repo insert; `BankAccountsServiceTests` +5 (mutations create/update/
      set-default/delete → 13003; reads list/get stay open even when the gate would throw — OQ5b);
      `WalletQrServiceTests` +2 (both QR ops → 13003 before destination resolution).
    - *Integration — count methods (`TierCountRepositoryTests`, 5, real MariaDB, skippable):*
      `CountActiveByUserAsync` counts active incl. owner-rep, excludes soft-deleted, user-scoped, and a
      soft-delete frees a slot; `CountOpenByUserAsync` counts only `is_closed = false`, user-scoped, and
      closing frees a slot; `CountByUserInRangeAsync` counts only `expense_time` in the half-open
      `[from, to)` window (inclusive start, exclusive end, last/next-month excluded), user-scoped.
    - *Endpoint (`TierLimitEndpointTests`, 14 + `OwnerRepExemptionEndpointTests`, 1; real HTTP,
      skippable):* member/open-event/expense limits return 400 13000/13001/13002 (member case also
      asserts the Vietnamese message + interpolated number); closing frees an open-event slot;
      last-month-dated expenses don't consume the current-month quota; Premium bypasses each limit; the
      §4.9 guarantee (a Free user seeded OVER every limit can still list/edit/close/set-settled/delete
      existing rows — only creates blocked); the OQ5b read-vs-mutation split (Free wallet mutations +
      both QR → 403 13003, Free wallet reads → 200, CSV export stays Free 200, Premium allowed
      everywhere incl. a real expense QR PNG); the tier-on-token staleness contract (an already-issued
      Free token stays gated after a DB flip to Premium; a fresh login lifts the gate); and the owner-rep
      exemption (`MaxMembers = 0` still yields the owner-rep on register while `POST /members` → 400
      13000).
  - **Config-override approach:** two `WebApplicationFactory<Program>` subclasses
    (`TierLimitWebApplicationFactory` = 2/2/2, `ZeroMemberLimitWebApplicationFactory` = MaxMembers 0)
    append an in-memory `Tiers:Free:` source so limits are hit with a handful of rows; **committed
    `appsettings.json` stays 25/10/200** (never edited; no env-var overrides used). Cleanup sweeps the
    per-class username prefix (expenses → events → bank_accounts → audit_logs → users); a post-run DB
    check confirms **0 test-prefix users remain** (real DB left clean).

- **Post-review nit (2026-07-14):** M10 passed code review (APPROVE, 0 blocking). Added the promised
  13xxx Swagger `[SwaggerResponse]` annotations for API-doc parity (documentation-only, no behaviour
  change): `POST /members` 400 (13000), `POST /events` 400 (13001), `POST /expenses` 400 (13002) —
  broadened the existing 400 descriptions per the `ExpensesController` enumerate-in-one-response pattern;
  new 403 (13003) on the four `bank-accounts` mutations (create/update/set-default/delete) and both QR
  routes (`GET /expenses/{uuid}/qr`, `GET /events/{uuid}/qr`).

### 2026-07-14 (code review — APPROVED, 0 blocking — milestone closed)

- **Verdict: code-reviewer APPROVE, 0 blocking** (one nit fixed, 2 informational notes). Milestone
  closed and cleared for commit.
- **Verified against the spec + this doc's decisions:**
  - **§4.9 create-only invariant holds.** The tier guards live **only** in the three `CreateAsync`
    methods (`MembersService`/`EventsService`/`ExpensesService`); NO limit sits on any update / assign /
    set-settled / delete / read / close path. An over-limit or downgraded Free user is blocked **only**
    on new creates — every read/edit/delete of existing data still works; nothing is locked/hidden/deleted.
  - **Owner-rep bootstrap exempt.** The guard is only in `MembersService.CreateAsync`; the bootstrap path
    (`IRegistrationBootstrapStep` → `memberRepository.CreateAsync`) bypasses it — `MaxMembers = 0` still
    yields the owner-representative on register while `POST /members` returns 400 13000.
  - **Premium bypasses all three limits and the feature-gate.**
  - **Read-vs-mutation gate (OQ5b) correct.** `BankAccountsService` list/get ungated (Free → 200);
    create/update/delete/set-default → 403 13003 for Free; both `WalletQrService` QR methods → 403 13003
    for Free; CSV export stays Free.
  - **Tier → principal fail-safe.** Tier flows across every hop —
    `AuthTokenLookup` → `TokenWhitelistEntry` → login-issue cache write → **DB-fallback read** →
    `RefreshAsync` → `TokenValidator` → `AuthenticatedUser` + claims — and **every hop defaults to FREE
    when the value is absent/unknown**; an absent/unknown tier is never treated as Premium.
  - **+7 month window correct.** `CurrentMonthUtcWindow` uses a fixed +7 offset (no `TimeZoneInfo`) and
    a half-open `[from, to)` window.
  - **Count methods DB-side + user-scoped.** `CountActiveByUserAsync` (active incl. owner-rep, excl.
    soft-deleted), `CountOpenByUserAsync` (`is_closed = false`), `CountByUserInRangeAsync` (half-open
    range) — all `CountAsync` pushed to MariaDB, scoped by user UUID.
  - **`TierService` is `[ScopedService]` with no DI cycle** (depends on the count repos +
    `IContextAuthenticated` + config, not on the feature services).
  - **Errors + config.** 13000/13001/13002 → 400, 13003 → 403, `GetDefaultHttpStatus` extended;
    `Tiers:Free:` 25/10/200 config-driven. **NO migration / NO schema change** (the `tier` column already
    existed).
- **Nit fixed:** the promised 13xxx Swagger `[SwaggerResponse]` annotations were added to `POST /members`,
  `POST /events`, `POST /expenses` + the four wallet mutations + both QR routes (documentation-only; the
  suite held at 931/931).
- **2 informational notes (non-blocking, accepted):** (1) `RefreshAsync` already reads the **live**
  `users.tier` on rotation, so a token refresh busts the tier-staleness window immediately — a free bonus
  the M11 admin-grant can lean on; (2) the read-vs-mutation gate (OQ5b) was re-confirmed as intentional.
- **Final state:** `dotnet test` = **931 passed / 0 failed / 0 skipped**, deterministic (re-run
  identical), the real DB swept clean of test-prefix rows, and committed `appsettings.json` restored to
  **25 / 10 / 200**. Cleared for the orchestrator's commit.

## Final Outcome

**Milestone 10 (Tiers Premium/Free) COMPLETE — implemented, tested, and code-reviewed (APPROVE, 0
blocking) 2026-07-14; cleared for commit.**

Delivered per the approved doc:
- **Free-tier usage limits — create-only, count-based, §4.9-safe.** Members (25), open events (10),
  expenses per calendar month (200, `expense_time` in a fixed +7 half-open `[from, to)` window), all
  config-driven via `Tiers:Free:` and enforced by `Services/Api/Tiers/TierService.cs` + three DB-side
  count repo methods + one guard call in each of the three `CreateAsync` services. Guards sit **only** on
  create, so existing data is never touched (§3.11/§4.9); the **owner-rep bootstrap is exempt**
  (`MaxMembers = 0` still yields the owner-rep on register). **Premium bypasses every limit.**
- **Premium feature-gate — the M9 seam activated (OQ5b read-vs-mutation split).** Wallet **mutations**
  (create/update/delete/set-default) and **both QR methods** are Premium-gated (Free → 403 13003); wallet
  **reads** (list/get) and **CSV export** stay Free.
- **Tier → principal, fail-safe FREE.** `Tier` rides the token whitelist entry
  (`AuthTokenLookup`/`TokenWhitelistEntry`) onto `AuthenticatedUser` + a private claim, populated on the
  login-issue write, the DB-fallback read, refresh, and validate — **no extra per-request DB read**, and
  **every hop defaults to FREE** when the value is absent (an absent/unknown tier is never Premium).
  `RefreshAsync` reads the live `users.tier`, so a refresh immediately reflects an M11 admin grant.
- **13xxx error block** (13000/13001/13002 → 400, 13003 → 403); Vietnamese messages naming the limit +
  that Premium removes it.
- **NO EF migration / NO schema change** (the `tier` column already existed; OQ6 = no expiry column).

The **paid-upgrade path is deferred to M11's manual admin-grant** (an admin flips `users.tier`); self-serve
VNPay remains a documented future seam. **M11 (Admin) must take the 14xxx error block** (Tiers claimed
13xxx). Build clean; **`dotnet test` = 931 passed / 0 failed / 0 skipped** (deterministic); live smoke +
code review APPROVE.

### Deviations / notes
- `EnsurePremiumFeature` is synchronous `void` per the doc's Step 3 (the orchestrator brief loosely
  called it `EnsurePremiumFeatureAsync`; it does no async work, so the doc's shape was used).
- `Tier` was added to `TokenWhitelistEntry`/`AuthTokenLookup` as a *trailing positional parameter with a
  FREE default* (still positional per Step 1) rather than a plain new positional field, and
  `AuthenticatedUser.Tier` defaults to FREE rather than being `required` — both to keep the fail-safe
  default and to avoid gratuitously breaking existing record/`AuthenticatedUser` construction sites.
- Ripple to flag: `FairShareMonApi.Tests` will not compile until the test-engineer adds the 3 count
  methods to the 3 in-file fakes and threads an `ITierService` into the 3 `CreateService()` helpers.
  These are pure consequences of the planned interface + constructor additions, not logic breaks.

## Future Improvements

- **Self-serve upgrade (VNPay redirect)** — portable from the `quick-ordering` sibling — as an
  alternative to M11's manual admin-grant. Documented seam; not built.
- **Time-boxed Premium (`premium_expires_at`)** — a subscription period with lazy/scheduled downgrade,
  if OQ6 stays at indefinite-until-revoked for M10.
- **Category/Tag limits** — if OQ2 stays at "only the three examples", add `MaxCategories`/`MaxTags`
  later (the config + guard pattern already generalizes).
- **Instant tier-change reflection** — an explicit token-cache bust on an M11 admin grant, if the
  ≤ access-TTL staleness of OQ8a proves too slow in practice.
- **Soft-limit warnings** — a "you're near your Free limit" hint (e.g. at 80%) surfaced to the client
  before the hard 13xxx rejection.
