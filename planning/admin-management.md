# Admin suite — Milestone 11

Admin-only management on top of the shipped Auth → Members → Categories/Tags →
Expenses/Shares/Audit → Events → Stats → Export → Wallet/QR → Tiers (M10) stack: a **role model +
config-seeded admin account**, an **`"Admin"` authorization policy** guarding a new
`AdminController`, **manual tier grant/revoke** backed by an append-only **`tier_grants`** table,
an **admin metrics dashboard**, a **revenue dashboard**, and **account-level user administration**
(list/get/disable/enable/revoke-tokens/reset-password/role/grant/revoke). This is the manual paid-upgrade path
that M10 deferred here (self-serve VNPay stays a documented future seam).

**The hard privacy boundary (§4.1):** admin acts ONLY on **account metadata** (username, tier, role,
status, created_at) and **tier-grant/payment** records. Admin **NEVER** sees any user's personal
ledger data (members, expenses, events, shares, bank accounts). M11 therefore builds **no unscoped
cross-user query into any ledger table** — §4.1 holds even for admins.

## Objective

Deliver, per `planning/agent-dev-team.md` roadmap item 11 (added 2026-07-14) and the 2026-07-14
scope-expansion decision:

- **Role model + seeded admin.** A `users.role` enum (`USER`/`ADMIN`, default `USER`) via EF
  migration; `Role` propagated into `AuthenticatedUser`/claims/the token whitelist entry (mirroring
  the M10 `Tier` pattern, fail-safe → `USER`); an idempotent startup seeder that creates or promotes
  the config-driven admin account.
- **Authorization.** An `"Admin"` policy in `Program.cs`; a new `AdminController : AppController`
  carrying `[Authorize(Policy="Admin")]` at the derived level (`AppController` stays LOCKED);
  non-admins (incl. valid Free/Premium users) → the already-wired `Forbidden 1004`/403.
- **Manual tier grant/revoke.** An append-only `tier_grants` entity + migration; grant flips
  `users.tier` → Premium and records the offline **amount + reference (+ note)**; revoke downgrades
  to Free and records a row; the acting user's cached token state is refreshed so the change takes
  effect promptly. This table is the revenue dashboard's sole data source.
- **Metrics dashboard.** Cross-user counts over `users` only (total, tier distribution, role/status
  counts, signups over time) — mirroring the **M7 Stats triad** (DB-side `GROUP BY`/`COUNT`/`SUM`,
  `From`/`To` range) but **never over ledger tables**.
- **Revenue dashboard.** Grant amounts summed over a date range (bucketed), upgrade count, and
  references — over `tier_grants` only.
- **Account-level user administration.** List/get account metadata + grant history; disable/enable
  (reversible — no hard delete, OQ1 = (b)); revoke tokens; reset password; promote/demote role; tier
  grant/revoke. Adds an account-status column; a disabled user's tokens are revoked and they can no
  longer authenticate.

## Background

Grounded in the live code (read 2026-07-14):

- **Users are NOT soft-deletable today.** `Database/Entities/User.cs` is `IEntity` (not
  `IEntityDeletable`): `Id`, `Uuid`, `Username`, `PasswordHash`, `Tier`, `CreatedAt`, `UpdatedAt`.
  `Partials/User.cs` maps `tier` `varchar(16)` `HasDefaultValue(FREE)`; the ctor sets `Tier = FREE`.
  There is **no `role` and no status column** — M11 adds both. Making users soft-deletable would
  ripple `IEntityDeletable`'s query filter into every existing user-scoped query — a large blast
  radius, hence the delete OQ (OQ1).
- **Tier→principal is the pattern to mirror (M10, `planning/tiers-premium-free.md`).** M10 added
  `Tier` as a **trailing positional record parameter defaulting to `FREE`** on
  `Auth/Abstractions/ITokenWhitelistStore.TokenWhitelistEntry` and
  `Repositories/AuthTokenRepository.AuthTokenLookup` (so pre-M10 cached/projected rows deserialize as
  FREE — fail-safe), a private `"tier"` claim on `Auth/AuthenticatedUser` (`ToClaims`/`FromPrincipal`,
  absent → FREE), `token.User.Tier` projected in `GetByHashWithUserAsync`, carried on the login-issue
  cache write (`TokenService.IssueAsync(userId, username, tier, ...)` ← `AuthService.LoginAsync`
  passes `user.Tier`), the **DB-fallback read** (`TokenWhitelistStore.LookupAsync`), and refresh
  (`TokenService.RefreshAsync` passes `lookup.Tier`, which is read **live** from `users.tier`), and
  set on the validated principal (`Auth/TokenValidator`). **M11 adds `Role` at every one of those
  hops the same way.** M10 noted a **key freshness fact: `RefreshAsync` reads the live tier**, so a
  refresh busts staleness immediately; an access token keeps its cached tier for ≤ its TTL (~30 min).
- **`Constants/UserTiers.cs`** = `Free="FREE"`, `Premium="PREMIUM"` — M11 adds a **`UserRoles`
  sibling** (`User="USER"`, `Admin="ADMIN"`) and a status constant set (OQ2).
- **Token revoke paths already exist.** `TokenService.RevokeAllAsync(userId)` (the password-change
  kill switch) hard-deletes every `auth_tokens` row for a user AND deletes each Redis cache key — the
  exact primitive admin "revoke tokens" / "disable" needs. `AuthService.ChangePasswordAsync` shows
  the post-commit revoke pattern. There is currently **no** "bust the cache but keep the sessions"
  primitive (relevant to OQ3).
- **Authorization is wired for policies.** `Program.cs` `AddAuthorization` sets only a `FallbackPolicy`
  (`RequireAuthenticatedUser`). M11 adds a named `"Admin"` policy here.
  `OpaqueTokenAuthenticationHandler.HandleForbiddenAsync` already emits **403 + `Forbidden 1004`** in
  the `ApiResult` envelope for a policy failure — so a non-admin hitting an admin route gets exactly
  that with no new code. `AppController` (LOCKED) exposes `AuthenticatedUser`; policy attributes go on
  the derived controller (as `AuthController` does).
- **The M7 Stats triad is the dashboard pattern (`planning/debt-balance-and-stats.md`).**
  `Repositories/StatsRepository.cs` pushes `GroupBy(...).Select(g => g.Sum/Count)` into MariaDB via
  `ExecuteQueryAsync`, with optional `[from,to]` ranges. M11's dashboards mirror this shape **but
  cross-user over `users` / `tier_grants` only** (M7 is per-user resource-owned; M11 admin dashboards
  are deliberately unscoped-by-user, which is safe **only because they touch no ledger table**).
- **`UserRepository` has no list-all-users method** — every method is username/uuid-keyed. M11 adds a
  paged, account-metadata-only listing (OQ7).
- **Seeder seam options.** `IRegistrationBootstrapStep` runs **per registration** (owner-rep,
  suggested categories) — wrong shape for a singleton admin. The right precedent is a
  `[BackgroundService]` startup worker like `HostedServices/OwnerRepresentativeBackfillHostedService`
  (own DI scope, idempotent, never crashes boot, retries next boot) — M11's admin seeder mirrors it.
- **Error blocks:** `Constants/ErrorCodes.cs` runs 1xxx–13xxx (13xxx = Tiers, M10).
  `Exception/ErrorException.GetDefaultHttpStatus` maps each code → HTTP. **14xxx is the next free
  block** for Admin (the roadmap's earlier loose "13xxx Admin" is superseded — M10 took 13xxx).
- **Append-only payment-table shape to adapt (sibling `quick-ordering`).**
  `Database/Entities/PaymentResultRecord.cs` + `Services/Payments/PaymentRecordPersistence.cs` show an
  append-only money record (amount + currency + reference + metadata, `Add` + `SaveChanges`, no
  update). M11 adapts the **shape** (swap order→user/tier-grant, `Guid.CreateVersion7()`→`Uuid.NewV7()`,
  integer money→`decimal`+CHECK) — the VNPay gateway is **not** ported (deferred).
- **Money + CHECK precedent:** `shares.amount` is `decimal(18,2)` with
  `ck_shares_amount_non_negative` (`amount >= 0`) via `table.HasCheckConstraint(...)`;
  `Partials/BankAccount.cs` shows the standard entity mapping (snake_case columns, `user_id` FK,
  `Uuid.NewV7()` ctor, `updated_at` `ON UPDATE current_timestamp(6)`).
- **Config precedent:** raw `configuration.GetValue("Section:Key", default)` (`Auth:`, `Tiers:Free:`).
  M11 adds an `Admin:` section (`Admin:Username`, `Admin:Password`).

## Requirements

From the roadmap M11 scope, the 2026-07-14 locked decisions, and `The-ideal.md` §3.11 + §4.1:

- **R1 — Role model.** `users.role` `varchar` enum (`USER`/`ADMIN`), default `USER`, via EF migration;
  `UserRoles` constants; `Role` on `User` entity + mapping.
- **R2 — Role→principal (mirror M10 tier, fail-safe USER).** `Role` on `TokenWhitelistEntry` +
  `AuthTokenLookup` (trailing default `USER`), projected from `token.User.Role`, carried on
  login-issue / DB-fallback / refresh, set on the validated `AuthenticatedUser` + a `"role"` claim;
  absent/unknown → `USER` (never `ADMIN`).
- **R3 — Seeded admin.** Idempotent startup seeder (config `Admin:Username`/`Admin:Password`):
  creates the account (Free tier, ADMIN role) if absent, or promotes it to ADMIN if it exists; no-op
  when config is absent; never logs the password.
- **R4 — Admin policy + controller.** `"Admin"` policy in `Program.cs` (requires the `role` claim ==
  `ADMIN`); `AdminController` under `api/v1/admin/...` with `[Authorize(Policy="Admin")]`; non-admins
  (incl. Free/Premium) → 403 `Forbidden 1004` (already wired).
- **R5 — `tier_grants` table (append-only).** New `IEntity`, snake_case: `user_id`, `tier`, `action`
  (GRANT/REVOKE), `amount` `decimal(18,2)` + CHECK `>= 0`, `currency`, `reference`, `note`,
  `granted_by_user_id`, + denormalized username snapshots (immutable trail consistent with
  `audit_logs`, OQ5), timestamps; index for the revenue range query.
- **R6 — Manual tier grant/revoke.** Grant flips `users.tier` → Premium + writes a GRANT row; revoke
  → Free + writes a REVOKE row; both refresh the target's cached token state so the change is prompt
  (OQ3).
- **R7 — Metrics dashboard.** Cross-user over `users` only: total users, tier distribution, role
  distribution, active/disabled counts, signups over time. **NO ledger aggregate of any kind** (OQ6).
- **R8 — Revenue dashboard.** Over `tier_grants` only: SUM of GRANT amounts over a `[from,to]` range,
  bucketed (day/month), upgrade count, references.
- **R9 — User administration (account-level only).** List (paged, filter/sort) + get (metadata +
  grant history); disable/enable (reversible — **no hard delete**, OQ1 = (b)); revoke tokens; reset
  password; promote/demote role; tier grant/revoke. Adds a status column; disable revokes all tokens +
  blocks future authentication; re-enable restores login.
- **R10 — Privacy boundary (§4.1, non-negotiable).** No admin endpoint returns any user's members,
  expenses, events, shares, or bank accounts — and **no anonymous/aggregate ledger figure** either
  (OQ6). No cross-user query touches a ledger table. This is asserted by dedicated tests.
- **R11 — Cross-cutting.** Vietnamese user-facing messages; new 14xxx error block +
  `GetDefaultHttpStatus`; `AppController` untouched; money `decimal`+CHECK; DiDecoration
  `[ScopedService]`; EF migration for the schema changes.

## Open Questions

> **All 15 answered by the user at the 2026-07-14 checkpoint** — 14 accepted at the recommended option
> (a); **OQ1 the user chose option (b)** (disable-only, NO hard delete — user override). The binding
> answers are annotated inline below; the full options/trade-offs are preserved for the record and
> mirrored in the Decision Log. **No open questions remain — implementation can start.** The
> Implementation Plan, endpoint table, migration, error-code table, and test list below are synced to
> these answers.

**OQ1 — User delete semantics (highest-impact).** Users are `IEntity`, not `IEntityDeletable` today.
> ~~**OQ1**~~ → **Answered 2026-07-14 (option b — USER OVERRODE the (a) recommendation):** disable/
> suspend ONLY — **NO hard delete**. Accounts are only ever disabled/enabled (reversible). Users remain
> non-`IEntityDeletable`; **no cascade-delete path is built** and the `DELETE /admin/users/{uuid}`
> endpoint is removed from the plan. The `tier_grants` no-cascade design (OQ5) stays (append-only trail
> consistent with `audit_logs`), but its rationale is now "consistency with the immutable audit trail",
> not "survives a user hard-delete".
- **(a) Disable/suspend as the default control + a separate, explicit hard-delete that
  cascades to ALL of that user's data.** Day-to-day "removal" is the reversible **disable** (OQ2);
  a genuine hard-delete is a distinct, clearly-irreversible endpoint that removes the `users` row and
  cascades via the existing FKs (members, categories, tags, events, expenses, shares, bank_accounts,
  auth_tokens — all `user_id` FK `OnDelete(Cascade)`); `audit_logs` have **no** user FK (orphaned by
  design, §3.8 immutability) and `tier_grants` deliberately keep no cascade FK (OQ5) so **revenue
  history survives the delete**. Trade-off: hard-delete is destructive and irreversible, but it is
  opt-in and separated from the everyday disable; **no new `IEntityDeletable` on users**, so zero
  ripple into existing user-scoped queries. Guarded by the self/last-admin rules (OQ10).
- **(b) Disable/suspend only — no hard-delete at all.** Simplest and safest (nothing is ever
  destroyed), but there is then no way to honour a real "delete my account / GDPR erase" request from
  the admin side; data accumulates forever.
- **(c) Soft-delete users (add `is_deleted` + `IEntityDeletable` to `users`).** Reversible and
  history-preserving, **but** `BaseRepository.Query<User>` would then filter deleted users by default,
  rippling into auth (login/token lookups), every `user_id` join, and the M10 count queries — a large,
  risky blast radius for a feature whose "removal" need is already met by disable (OQ2). Not
  recommended.

**OQ2 — Account-status model.**
> ~~**OQ2**~~ → **Answered 2026-07-14 (option a):** a `users.status` `varchar(16)` enum column
> (`ACTIVE`/`DISABLED`, default `ACTIVE`) via the migration; **disable** = set `DISABLED` +
> `RevokeAllAsync` (immediate token kill) + block login (14003); **enable** restores login.
- **(a) A `status` `varchar(16)` column (`ACTIVE`/`DISABLED`) with a `UserStatuses`
  constant set, default `ACTIVE`** (mirrors the `tier` string-enum pattern; extensible to `SUSPENDED`
  later). **Disable** = set `status=DISABLED` + `RevokeAllAsync` (instant, permanent logout) + block
  future logins (`AuthService.LoginAsync` rejects `DISABLED` with 14003 before issuing tokens).
  **Enable** = set `status=ACTIVE` (login works again; no tokens auto-restored — the user re-logs in).
  Trade-off: a string column is one extra `varchar`; no per-request status check is needed because
  disable already revokes all tokens, so a `DISABLED` user simply cannot hold a valid token.
- **(b) A `bool is_active` column** (true default). Simpler/smaller, but not extensible to more states
  and less self-documenting than the string enum the codebase already favours for `tier`.
- **(c) Also add a per-request token-validation status check** (reject a `DISABLED` user even if a
  token slips through). Trade-off: defense-in-depth, but redundant given disable revokes all tokens,
  and it adds a hot-path branch / a `status` field on the token entry for no real gain. Not
  recommended unless the user wants belt-and-suspenders.

**OQ3 — Role propagation + grant/disable freshness.** `Role` rides the token entry like `Tier`. How
promptly must a tier grant / role change take effect?
> ~~**OQ3**~~ → **Answered 2026-07-14 (option a):** add a targeted Redis cache-bust primitive for
> grant/revoke/role-change (the new tier/role applies on the user's next request, no forced logout);
> disable + reset-password use the existing `RevokeAllAsync` (immediate access cut).
- **(a) Add a targeted "cache-bust" primitive for grant/revoke + role change; use the
  existing `RevokeAllAsync` kill-switch for disable/delete.** A new
  `TokenService`/`AuthTokenRepository` method deletes only the user's **Redis** token cache keys (not
  the `auth_tokens` rows), so the next request falls through to the DB-fallback read and picks up the
  **live** `users.tier`/`users.role` immediately **without logging the user out**. Disable/delete keep
  using `RevokeAllAsync` (hard-delete rows + cache → instant, permanent lockout — mandatory for
  disable). Trade-off: one small new primitive, but a paid upgrade / a promotion reflects on the very
  next request while sessions stay alive.
- **(b) Rely purely on the ≤access-TTL / refresh path (M10 behaviour) for tier + role; only disable
  revokes tokens.** No new primitive; a grant/promotion reflects on the next refresh or within ≤ one
  access-TTL (~30 min). Trade-off: simplest, but an admin promoting a user to ADMIN (or upgrading to
  Premium) sees up to a 30-min lag unless the user refreshes/re-logs in.
- **(c) `RevokeAllAsync` on every grant/revoke/role change too (force re-login on any change).**
  Always fresh and trivial to reason about, but logs the user out of every device on a mere upgrade —
  poor UX for a positive action. Not recommended.

**OQ4 — Grant/revoke endpoint shape + fields.**
> ~~**OQ4**~~ → **Answered 2026-07-14 (option a):** two endpoints —
> `POST /admin/users/{uuid}/tier/grant` (amount, currency?, reference?, note?) flips tier → Premium +
> writes a GRANT row + busts cache; `POST /admin/users/{uuid}/tier/revoke` (note?) → Free + writes a
> REVOKE row + busts cache.
- **(a) Two endpoints:** `POST /admin/users/{uuid}/tier/grant` (body: `amount`,
  `currency?`, `reference?`, `note?`) sets `tier=PREMIUM` + writes a `GRANT` row;
  `POST /admin/users/{uuid}/tier/revoke` (body: `note?`) sets `tier=FREE` + writes a `REVOKE` row
  (amount 0). Trade-off: two routes, but each carries only its own fields (grant needs payment data,
  revoke does not) and the verbs read clearly; a re-grant of an already-Premium user still records a
  new payment row (a renewal).
- **(b) One `PUT /admin/users/{uuid}/tier` (body: `tier`, `amount`, `currency?`, `reference?`,
  `note?`).** Fewer routes, but conflates two different actions, makes `amount` awkward on a downgrade,
  and needs branchy validation ("amount only when tier=PREMIUM"). Not recommended.

**OQ5 — `tier_grants` table design.**
> ~~**OQ5**~~ → **Answered 2026-07-14 (option a):** append-only, plain `user_id`/`granted_by_user_id`
> columns (NO cascade FK, like `audit_logs`) + denormalized username snapshots, `amount` `decimal(18,2)`
> + CHECK `>= 0`, `currency` default VND, `action` GRANT/REVOKE discriminator, indexes on `created_at`
> and `(user_id, created_at)`. (The no-FK rationale is now "consistency with the immutable audit trail"
> — OQ1 = (b) means there is no hard-delete to survive.)
- **(a) Append-only, no cascade FK to `users`.** Columns: `id`, `uuid` (unique),
  `user_id` (**plain indexed column, no navigation FK** — mirrors `audit_logs`'s no-FK design; keeps
  the grant trail immutable and self-contained), `user_username` (denormalized snapshot),
  `tier` (`varchar(16)`), `action` (`varchar(16)` GRANT/REVOKE), `amount` `decimal(18,2)` +
  CHECK `ck_tier_grants_amount_non_negative` (`amount >= 0`), `currency` `varchar(3)` default `VND`,
  `reference` `varchar(255)?`, `note` `varchar(500)?`, `granted_by_user_id` (plain column) +
  `granted_by_username` (snapshot), `created_at`, `updated_at`; index on `(created_at)` (revenue range
  scan) and `(user_id, created_at)` (grant history). Never updated after insert. Trade-off: the
  no-FK + denormalized snapshots trade referential strictness for revenue durability across deletes
  and privacy-safe display (no join back into `users` needed to render history) — the same trade the
  audit log already makes.
- **(b) Real cascade FK to `users`.** Cleaner referential integrity, but a hard-delete (OQ1a) would
  cascade grants away, **destroying revenue history** — contradicts R8. Rejected.
- **(c) Mutable "current grant" row per user (upsert).** Smaller table, but loses the payment/audit
  trail and the revenue dashboard's history. Rejected.

**OQ6 — Metrics dashboard content + the privacy line.**
> ~~**OQ6**~~ → **Answered 2026-07-14 (option a):** metrics = account-metadata figures ONLY
> (user/tier/role/status counts + signup buckets); **zero ledger aggregates, not even anonymous** —
> honours the privacy boundary (§4.1/R10).
- **(a) Account-metadata figures ONLY, zero ledger aggregates.** `totalUsers`, tier
  distribution (`GroupBy(u=>u.Tier).Count`), role distribution, `activeUsers`/`disabledUsers`, and
  signups-over-time (`GroupBy` on `created_at` bucket, optional `[from,to]`). **No** total-expenses,
  total-events, total-members, or any other ledger figure — not even anonymous system-wide sums —
  honouring §4.1/R10. Trade-off: the admin dashboard can't show "system activity", but that is exactly
  the locked privacy boundary; if a purely operational, ledger-free metric is wanted later it is
  additive.
- **(b) Also include anonymous system-wide ledger counts** (e.g. total expenses, total events). More
  "admin insight", but it builds unscoped cross-user queries into ledger tables — the precise thing
  the locked boundary forbids. **Rejected unless the user explicitly overrides R10.**

**OQ7 — User-listing surface.**
> ~~**OQ7**~~ → **Answered 2026-07-14 (option a):** `GET /admin/users` = account metadata only, paged +
> filter (tier/status/role/username search) + sort; grant-count/last-grant allowed; NO ledger counts.
- **(a) Account metadata only, paged + filterable + sortable.** Row fields: `uuid`,
  `username`, `tier`, `role`, `status`, `createdAt`, and grant-derived `grantCount` / `lastGrantAt`
  (from `tier_grants`, not ledger). **No** members/expenses/events counts — even anonymous per-user
  ledger counts are off-limits (R10). Query params: `?tier=&status=&role=&search=` (username contains,
  case-insensitive), `?page=&pageSize=` (default 1 / 20, capped e.g. 100), `?sort=` (createdAt DESC
  default; username / tier / status). Trade-off: no ledger context per user, but that is the boundary;
  pagination keeps the list bounded for large user bases.
- **(b) Add per-user ledger counts** (member/expense/event counts). Richer admin view, but requires
  cross-user counts over ledger tables — forbidden by R10. Rejected.
- **(c) Unpaged full list.** Simpler, but unbounded as the user base grows. Not recommended.

**OQ8 — Admin password-reset (no email system exists).**
> ~~**OQ8**~~ → **Answered 2026-07-14 (option a):** include `POST /admin/users/{uuid}/reset-password` —
> admin-set temp password (BCrypt-hashed + stored, `RevokeAllAsync`, returned ONCE in the response,
> NEVER logged).
- **(a) Include an admin-set-temp-password endpoint**
  (`POST /admin/users/{uuid}/reset-password`, body: `newPassword`), which BCrypt-hashes the new
  password, stores it, and `RevokeAllAsync` the user (forcing re-login) — mirroring
  `ChangePasswordAsync`'s post-update kill-switch. Never logs the password. Trade-off: an admin can
  reset any user's password (a real support need with no email/reset flow), but it is a powerful
  capability — guarded by the admin policy, audited via a grant-style trail (OQ12) or at least logged;
  the temp password is returned once to the admin to relay out-of-band. Guarded by OQ10 (self/other-
  admin rules).
- **(b) Drop admin password-reset from M11.** Smaller surface / less risk, but leaves no recovery path
  for a locked-out user until an email/reset feature exists. The user may prefer this if password
  handling by an admin is undesirable.

**OQ9 — Seeded-admin credentials + multiple admins.**
> ~~**OQ9**~~ → **Answered 2026-07-14 (option a):** config-seeded admin (`Admin:Username`/`Admin:Password`),
> idempotent create-or-promote at startup (reuse the `OwnerRepresentativeBackfillHostedService` shape),
> no-op when config absent, password never logged; + `POST /admin/users/{uuid}/role` to promote/demote
> others.
- **(a) Config-driven (`Admin:Username` + `Admin:Password`), idempotent seeder,
  create-or-promote, safe when absent; plus an admin role endpoint to promote/demote others.** On boot
  the seeder: if the config username is missing → create it (Free tier, ADMIN role, BCrypt-hashed
  password); if it exists → ensure `role=ADMIN` (promote). If either config value is absent →
  **no-op** (no admin seeded — a safe default, logged as a warning). Never log the password.
  Additionally, `POST /admin/users/{uuid}/role` (body: `role`) lets an existing admin promote/demote
  others (subject to OQ10). Trade-off: password in config/env is standard for a bootstrap credential
  (rotate via config), and the role endpoint means the seeded admin isn't the only possible admin.
- **(b) Seeder only, no role endpoint** (admins can only ever be config-seeded). Simpler and tighter,
  but the only admin(s) must be listed in config; you can't promote a trusted user at runtime.
- **(c) Also re-set the password on every boot to match config.** Keeps config authoritative, but a
  runtime password change would be silently reverted on the next boot — surprising. Recommend
  create-or-promote-**without** overwriting an existing password (a). 

**OQ10 — Admin acting on itself / other admins (lockout safety).**
> ~~**OQ10**~~ → **Answered 2026-07-14 (option a):** guard the actions that **remove admin access or
> credentials** — self AND any-admin-target — plus the last-admin case. `14001` (self) + `14002`
> (any admin target / would leave zero admins). **Guarded (privilege-affecting) actions:** disable,
> revoke-tokens, reset-password, demote (ADMIN→USER). **NOT guarded:** promotion (USER→ADMIN, only adds
> an admin) and **tier grant/revoke** — tier (Premium/Free) is orthogonal to admin privilege (role), so
> revoking an admin's Premium can never cause a lockout; tier grant/revoke is allowed on any target
> including an admin. (With OQ1 = (b) there is no hard-delete.)
> **Doc correction (2026-07-14 code review):** an earlier draft of this annotation listed
> tier-"downgrade" among the guarded actions — that was a documentation error, not the shipped behavior.
> The reviewed code correctly guards only the privilege/credential paths; tier-revoke on an admin is
> allowed. This edit aligns the doc with the reviewed code (no behavior change).
- **(a) Guard both self and last-admin.** An admin **cannot** disable /
  demote / revoke-tokens / reset-password **itself** (→ 14001), and **cannot** disable / demote /
  reset-password / revoke-tokens **another ADMIN** (→ 14002) — admin-on-admin destructive actions are
  blocked so no admin can knock out a peer, and the system can never reach zero admins. Tier
  grant/revoke and password-reset on a **non-admin** target are always allowed. Trade-off: an admin who
  must be removed has to be demoted by editing config + reseeding or via DB — accepted as the safe
  default (prevents accidental/malicious lockout).
- **(b) Guard only the last remaining admin** (allow admin-on-admin otherwise, incl. self). More
  flexible, but one admin can disable/delete another — a footgun / attack surface among admins.
- **(c) No guards.** Simplest, but a single fat-finger self-disable or a mutual admin-disable can lock
  everyone out. Rejected.

**OQ11 — Endpoint/route structure.**
> ~~**OQ11**~~ → **Answered 2026-07-14 (option a):** one `AdminController` named `Admin` (the
> `[controller]`-derived `api/v1/admin` prefix), logic split across `IAdminUserService` +
> `IAdminDashboardService`.
- **(a) One `AdminController` named `Admin`** so `[controller]` yields the
  `api/v1/admin` prefix for free (no route gymnastics against the LOCKED `AppController` route
  template), with sub-paths: `dashboard`, `revenue`, `users`, `users/{uuid}`,
  `users/{uuid}/disable|enable|revoke-tokens|reset-password|role`, `users/{uuid}/tier/grant|revoke`,
  and `DELETE users/{uuid}`. `[Authorize(Policy="Admin")]` sits once on the controller. Business logic
  splits across cohesive services (`IAdminUserService`, `IAdminDashboardService`) — the controller
  stays thin. Trade-off: one controller class is a bit large, but the service split keeps logic
  cohesive and the routing falls out of `[controller]=Admin` cleanly.
- **(b) Split controllers** (`AdminDashboardController`, `AdminUsersController`). More cohesive files,
  but `[controller]` would produce `api/v1/AdminDashboard` / `api/v1/AdminUsers` unless each declares
  an explicit `[Route("api/v{version:apiVersion}/admin/...")]`, and the `[Authorize(Policy="Admin")]`
  attribute must be repeated. Acceptable if the user prefers smaller controllers.

**OQ12 — Are admin actions audited?** (M5 `audit_logs` is ledger-only, §3.8.)
> ~~**OQ12**~~ → **Answered 2026-07-14 (option a):** `tier_grants` is the grant/revoke trail; NLog
> structured logging for the other admin actions (disable/enable/role/reset-password); NO new
> admin-audit table in M11.
- **(a) The `tier_grants` table IS the audit trail for grant/revoke; add lightweight
  structured logging (NLog) for the other admin actions (disable/enable/reset-password/role),
  but no new admin-audit table in M11.** Grants/revokes are already a durable, queryable record;
  logging the rest is cheap and reversible-decision-friendly. Trade-off: disable/reset aren't
  in a queryable DB trail, only logs — acceptable for M11; a dedicated `admin_audit_logs` table is a
  clean future addition if a queryable admin trail becomes a requirement.
- **(b) A dedicated append-only `admin_audit_logs` table for every admin action.** Complete, queryable
  trail, but a second new table + repository + write on every admin action for M11 — more scope than
  the locked spec asks. Defer to a future improvement unless the user wants it now.

**OQ13 — Migration(s): one or two.**
> ~~**OQ13**~~ → **Answered 2026-07-14 (option a):** ONE migration `AddAdminRoleStatusAndTierGrants`
> (users ALTER `role` + `status`; new `tier_grants` table).
- **(a) One migration `AddAdminRoleStatusAndTierGrants`** covering the `users` ALTER
  (`role` + `status` columns) **and** the new `tier_grants` table (with its CHECK + indexes) — they
  ship together for M11, so one reviewable atomic migration. Trade-off: a slightly larger single
  migration, but M11's schema is one cohesive unit.
- **(b) Two migrations** (`AddUserRoleAndStatus`, then `AddTierGrants`). Finer-grained history, but two
  round-trips for one milestone with no independent value.

**OQ14 — Revenue dashboard buckets + what counts as revenue.**
> ~~**OQ14**~~ → **Answered 2026-07-14 (option a):** revenue = SUM(amount) over GRANT rows only,
> inclusive UTC `[from,to]` (both optional = all-time), bucketed monthly (default) / daily; per-bucket +
> grand totals + references.
- **(a) Revenue = SUM(`amount`) over `action=GRANT` rows only, in an inclusive UTC
  `[from,to]` (both optional = all-time, mirroring M7 OQ7), bucketed by `month` (default) or `day`
  (`?bucket=`); response carries per-bucket `{ periodLabel, total, grantCount }`, a grand `totalRevenue`
  + `grantCount`, and an optional flat `references` list.** REVOKE rows (amount 0) never count as
  revenue. Trade-off: matches "revenue = money taken via upgrade grants"; comps (amount 0 grants) show
  as grants with 0 revenue — correct.
- **(b) Net revenue (subtract something on revoke).** A revoke is not a refund (no money moves back in
  this manual model), so netting would misstate revenue. Rejected.
- **(c) No bucketing (single total over the range).** Simpler, but a revenue dashboard usually wants a
  trend; bucketing is cheap with the M7 `GroupBy` pattern. Not recommended.

**OQ15 — Grant field constraints (amount required? currency default?).**
> ~~**OQ15**~~ → **Answered 2026-07-14 (option a):** grant `amount` required `>= 0` (0 = comp),
> `currency` optional default VND, reference/note optional.
- **(a) `amount` required on a grant, `>= 0` (0 allowed for comps/free grants);
  `currency` optional, default `VND`; `reference` and `note` optional.** Trade-off: forcing an amount
  (even 0) keeps the revenue source complete and explicit; VND default matches the sibling
  (`MoneyCurrencies.VND`) and the single-currency spec. `reference` optional because an offline
  transfer may lack one.
- **(b) `amount` optional (null = comp).** Slightly friendlier, but a nullable money column complicates
  the revenue SUM and the CHECK; an explicit 0 is clearer. Not recommended.

## Assumptions

> **Confirmed by the user at the 2026-07-14 checkpoint** together with the 15 Open Questions — these
> are now decisions, not vetoable assumptions. Each is derived from the spec, the M10 doc, and the
> shipped code.

- All admin endpoints are authenticated **and** admin-authorized; anonymous → 401 (upstream),
  authenticated-non-admin → 403 `Forbidden 1004` (already wired via `HandleForbiddenAsync`).
- The seeded admin is a normal `users` row with `role=ADMIN` (and Free tier); it is subject to the
  same auth stack. It never receives any special ledger access — only the admin endpoints.
- Admin dashboards are intentionally **not** resource-owned-by-user (they are cross-user by design) —
  this is safe **only** because they touch `users` / `tier_grants`, never a ledger table (R10).
- Money is `decimal(18,2)` with a DB CHECK `>= 0`; single currency (VND) default (§4.3).
- No new NuGet dependency; `AppController` untouched; no VNPay/gateway code (future seam).
- The `users.tier` column already exists (M10); M11 only writes it via grant/revoke and reads it in
  the dashboard — it adds `role` + `status` columns and the `tier_grants` table.

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/`. New services/repos use DiDecoration
> `[ScopedService]`; hosted seeder uses `[BackgroundService]`. All user-facing strings + Swagger
> summaries are Vietnamese. Concrete file names below assume the **recommended option (a)** for each
> OQ; the plan is re-synced after the checkpoint.

### Step 1 — Role + status on the user entity (R1, OQ2) + constants

1. `Constants/UserRoles.cs` (new) — `public const string User = "USER"; public const string Admin =
   "ADMIN";` (+ XML doc).
2. `Constants/UserStatuses.cs` (new) — `public const string Active = "ACTIVE"; public const string
   Disabled = "DISABLED";`.
3. `Database/Entities/User.cs` — add `public string Role { get; set; }` and
   `public string Status { get; set; }`.
4. `Database/Entities/Partials/User.cs` — ctor sets `Role = UserRoles.User; Status =
   UserStatuses.Active;`; map `role` `varchar(16)` `HasDefaultValue(UserRoles.User)` and `status`
   `varchar(16)` `HasDefaultValue(UserStatuses.Active)` (+ an index on `role` and/or `status` if the
   dashboard/list filters warrant — decide during impl; low cardinality).

### Step 2 — `tier_grants` entity + mapping (R5, OQ5/OQ15) + migration (OQ13)

1. `Database/Entities/TierGrant.cs` (new POCO, `IEntity`): `Id`, `Uuid`, `UserId` (ulong, plain),
   `UserUsername`, `Tier`, `Action`, `Amount` (decimal), `Currency`, `Reference?`, `Note?`,
   `GrantedByUserId` (ulong, plain), `GrantedByUsername`, `CreatedAt`, `UpdatedAt`. Add a
   `TierGrantActions` constant set (`Grant="GRANT"`, `Revoke="REVOKE"`) — either in the partial or a
   `Constants/TierGrantActions.cs`.
2. `Database/Entities/Partials/TierGrant.cs` (new): ctor sets `Uuid = Uuid.NewV7()`, `CreatedAt =
   AppDateTime.Now`; `ConfigureModel` maps table `tier_grants` with snake_case columns,
   `amount` `decimal(18,2)` + `table.HasCheckConstraint("ck_tier_grants_amount_non_negative",
   "amount >= 0")`, `currency` default `VND`, **no navigation FK** to `users` (plain `user_id` /
   `granted_by_user_id` columns, like `audit_logs`), unique index on `uuid`, index on `created_at`
   and `(user_id, created_at)`.
3. `Database/AppDbContext.cs` — add `public DbSet<TierGrant> TierGrants => Set<TierGrant>();` and
   `TierGrant.ConfigureModel(modelBuilder);` in `OnModelCreating`.
4. Migration **`AddAdminRoleStatusAndTierGrants`** (one migration, OQ13a): `users` ALTER (`role`,
   `status`) + `create table tier_grants` (+ CHECK + indexes). Author offline via the design-time
   factory; apply to the dev DB during Implement per the roadmap.

### Step 3 — Role→principal (R2, mirror M10 tier exactly)

1. `Auth/Abstractions/ITokenWhitelistStore.cs` — add trailing `string Role = UserRoles.User` to the
   `TokenWhitelistEntry` positional record (after `Tier`).
2. `Repositories/AuthTokenRepository.cs` — add trailing `string Role = UserRoles.User` to
   `AuthTokenLookup`; project `token.User.Role` in `GetByHashWithUserAsync`.
3. `Auth/TokenWhitelistStore.cs` (`LookupAsync`) — pass `row.Role` into the `TokenWhitelistEntry`.
4. `Auth/TokenService.cs` — add a `string role = UserRoles.User` parameter to `IssueAsync` (both
   `TryCacheAsync` entries carry it); `RefreshAsync` passes `lookup.Role` (live-read, like tier).
5. `Services/Api/Auth/AuthService.cs` (`LoginAsync`) — pass `user.Role` into `IssueAsync`.
6. `Auth/TokenValidator.cs` — set `Role = string.IsNullOrEmpty(entry.Role) ? UserRoles.User :
   entry.Role` on the returned `AuthenticatedUser`.
7. `Auth/AuthenticatedUser.cs` — add `public string Role { get; init; } = UserRoles.User;`, a private
   `"role"` claim in `ToClaims()`, and read it in `FromPrincipal(...)` (absent → `USER`, fail-safe).

### Step 4 — Admin policy (R4)

`Program.cs` `AddAuthorization` — add a named policy alongside the existing fallback:
`options.AddPolicy("Admin", policy => policy.RequireAuthenticatedUser().RequireClaim("role",
UserRoles.Admin));`. A non-admin fails the policy → `HandleForbiddenAsync` emits 403 `Forbidden 1004`
(no new code). Define the policy name as a constant (e.g. `Constants/AuthorizationPolicies.Admin`).

### Step 5 — Seeded-admin startup worker (R3, OQ9)

`HostedServices/AdminSeederHostedService.cs` (new, `[BackgroundService]`, mirrors
`OwnerRepresentativeBackfillHostedService`): own DI scope; read `Admin:Username`/`Admin:Password`; if
either absent → log a warning + no-op; else resolve the user by username — if absent, create it
(BCrypt hash via `IPasswordHasher`, Free tier, `role=ADMIN`, `status=ACTIVE`) through
`IUserRepository.CreateAsync`; if present, promote to `role=ADMIN` if not already (a new
`IUserRepository.SetRoleAsync` / reuse an update path). Never log the password; wrap in try/catch so a
DB outage never crashes boot (retries next boot).

### Step 6 — Repositories (account metadata + grants + dashboards; no ledger tables)

1. `Repositories/UserRepository.cs` — add:
   - `Task<(IReadOnlyList<AdminUserRow> Rows, int Total)> ListForAdminAsync(AdminUserQuery query, CancellationToken)`
     — paged, filter (tier/status/role/username contains), sort; projects account metadata ONLY.
   - `Task<AdminUserDetail?> GetForAdminAsync(string uuid, CancellationToken)` — account metadata only.
   - `Task<bool> SetTierAsync(string uuid, string tier, CancellationToken)`,
     `SetStatusAsync(...)`, `SetRoleAsync(...)`, `SetPasswordHashAsync(...)` — targeted
     `ExecuteUpdate`-style writes.
   - `Task<int> CountByRoleAsync(string role, CancellationToken)` — for the last-admin guard (OQ10).
   - **No hard-delete method** (OQ1 = (b): disable-only; users stay non-`IEntityDeletable`).
   - counting helpers for the dashboard (or via a dedicated repo, below).
2. `Repositories/TierGrantRepository.cs` (new, `ITierGrantRepository`, `[ScopedService]`, extends
   `BaseRepository`): `AddAsync(TierGrant)` (append-only), `ListByUserAsync(userId)` (grant history),
   and revenue aggregation `GetRevenueAsync(from, to, bucket)` (DB-side `GroupBy`
   `created_at` bucket + `SUM(amount)` over `action=GRANT`, mirroring `StatsRepository`).
3. `Repositories/AdminDashboardRepository.cs` (new) OR methods on `UserRepository`: cross-user counts
   over `users` only — `CountAsync`, tier distribution (`GroupBy(u=>u.Tier)`), role/status counts,
   signups bucketed (`GroupBy` `created_at`). **No ledger table is queried.**
4. `Repositories/AuthTokenRepository.cs` — add a **cache-bust-only** helper (OQ3a):
   `Task<IReadOnlyList<string>> GetActiveHashesByUserAsync(string userUuid, CancellationToken)` so the
   token service can delete just the Redis keys (keeping the rows). (Disable + reset-password keep using
   the existing `DeleteAllByUserIdAsync` via `RevokeAllAsync`.)

### Step 7 — Token freshness primitive (OQ3a)

`Auth/TokenService.cs` — add `Task RefreshCachedStateAsync(string userUuid, CancellationToken)` that
loads the user's active token hashes (Step 6.4) and `TryDeleteCachedAsync` each Redis key (no DB
change), so the next request re-reads live tier/role from the DB fallback. Called after a committed
grant/revoke/role-change. Disable + reset-password call the existing `RevokeAllAsync` (immediate cut).

### Step 8 — Admin services (R6/R7/R8/R9, OQ4/OQ6/OQ7/OQ10/OQ12/OQ14)

1. `Services/Api/Admin/AdminUserService.cs` (`IAdminUserService`, `[ScopedService]`, primary ctor):
   - `ListAsync(AdminUserListRequest)` / `GetAsync(uuid)` (metadata + grant history via
     `ITierGrantRepository`).
   - `GrantTierAsync(actingAdmin, uuid, GrantTierRequest)` — validate; resolve target (miss → 14000);
     in one `ExecuteTransactionAsync`: set `tier=PREMIUM` + insert a `GRANT` `TierGrant` (with
     `granted_by` = acting admin, denormalized usernames); **post-commit** call
     `RefreshCachedStateAsync`.
   - `RevokeTierAsync(...)` — set `tier=FREE` + insert a `REVOKE` row (amount 0); post-commit refresh.
   - `DisableAsync` / `EnableAsync` — set status; on disable, post-commit `RevokeAllAsync`
     (self/other-admin guard OQ10 → 14001/14002). No hard-delete (OQ1 = (b)).
   - `RevokeTokensAsync` — `RevokeAllAsync` (self/other-admin guard).
   - `ResetPasswordAsync` (OQ8a) — hash + store + `RevokeAllAsync`; return the temp password once (never
     logged); self/other-admin guard.
   - `SetRoleAsync` (OQ9a) — promote/demote; self/other-admin + last-admin guard.
   - Guards centralised: `EnsureNotSelfDestructive` (14001), `EnsureTargetNotAdminForDestructive`
     (14002 for acting on another admin) + `EnsureNotLastAdmin` (14002 for a demote/disable that would
     leave zero admins).
2. `Services/Api/Admin/AdminDashboardService.cs` (`IAdminDashboardService`) — `GetMetricsAsync(range)`
   (OQ6a, `users` only) + `GetRevenueAsync(RevenueRequest)` (OQ14a, `tier_grants` only).

### Step 9 — DTOs + validators + mapping

`Models/Admin/`: `AdminUserListRequest` (`[FromQuery]` tier/status/role/search/page/pageSize/sort),
`AdminUserRow`, `AdminUserDetailResponse` (metadata + `TierGrantRow[]`), `GrantTierRequest`
(amount/currency?/reference?/note?), `RevokeTierRequest` (note?), `ResetPasswordRequest`,
`SetRoleRequest`, `AdminMetricsResponse` (totals + distributions + signup buckets), `RevenueRequest`
(`[FromQuery]` from/to/bucket), `RevenueResponse` (buckets + grand totals + references),
`TierGrantRow`, `PagedResult<T>` (if none exists — check `Models/`). `Validators/Admin/`:
`GrantTierRequestValidator` (amount `>= 0`; currency length/whitelist; note/reference max lengths),
`RevenueRequestValidator` (from ≤ to; bucket in {day,month}), `AdminUserListRequestValidator`
(pageSize cap; sort whitelist), `ResetPasswordRequestValidator` (reuse the password rules).
`Mappings/AdminProfile.cs` — `User`→`AdminUserRow`/detail, `TierGrant`→`TierGrantRow`.

### Step 10 — `AdminController` (R4, OQ11a)

`Controllers/AdminController.cs` (new, `: AppController`, `[Authorize(Policy="Admin")]`), thin,
Vietnamese Swagger, `actingAdmin = AuthenticatedUser`:

| Verb + Route | Request → Response |
|---|---|
| `GET api/v1/admin/dashboard` | `[FromQuery] range?` → `ApiResult<AdminMetricsResponse>` |
| `GET api/v1/admin/revenue` | `[FromQuery] RevenueRequest` → `ApiResult<RevenueResponse>` |
| `GET api/v1/admin/users` | `[FromQuery] AdminUserListRequest` → `ApiResult<PagedResult<AdminUserRow>>` |
| `GET api/v1/admin/users/{uuid}` | route → `ApiResult<AdminUserDetailResponse>` (miss → 14000) |
| `POST api/v1/admin/users/{uuid}/tier/grant` | `GrantTierRequest` → `ApiResult<TierGrantRow>` |
| `POST api/v1/admin/users/{uuid}/tier/revoke` | `RevokeTierRequest` → `ApiResult<TierGrantRow>` |
| `POST api/v1/admin/users/{uuid}/disable` | route → `ApiResult` (message) |
| `POST api/v1/admin/users/{uuid}/enable` | route → `ApiResult` (message) |
| `POST api/v1/admin/users/{uuid}/revoke-tokens` | route → `ApiResult` (message) |
| `POST api/v1/admin/users/{uuid}/reset-password` | `ResetPasswordRequest` → `ApiResult<...>` (OQ8; temp password returned once) |
| `POST api/v1/admin/users/{uuid}/role` | `SetRoleRequest` → `ApiResult` (OQ9) |

> **No `DELETE /admin/users/{uuid}`** (OQ1 = (b): accounts are only disabled/enabled — reversible;
> no hard-delete path is built).

### Step 11 — Login block for disabled users (R9, OQ2)

`Services/Api/Auth/AuthService.cs` (`LoginAsync`) — after the credential check, if
`user.Status == UserStatuses.Disabled` throw `ErrorException(ErrorCodes.AccountDisabled, ...)` (14003)
**before** issuing tokens. (Disable already revoked existing tokens.)

### Step 12 — Error codes + messages (R11)

Append the **14xxx Admin** block to `Constants/ErrorCodes.cs` and extend
`ErrorException.GetDefaultHttpStatus`:

| Code | Name | HTTP | Message (Vietnamese) |
|---|---|---|---|
| `14000` | `AdminUserNotFound` | 404 | "Không tìm thấy người dùng." |
| `14001` | `AdminCannotTargetSelf` | 400 | "Bạn không thể thực hiện thao tác này với chính tài khoản admin của mình." |
| `14002` | `AdminCannotTargetAdmin` | 400 | "Không thể vô hiệu hóa/hạ quyền một tài khoản admin khác, hoặc thao tác này sẽ khiến hệ thống không còn admin nào." |
| `14003` | `AccountDisabled` | 403 | "Tài khoản đã bị vô hiệu hóa. Vui lòng liên hệ quản trị viên." |

(Grant amount/currency/sort/bucket bad input → `ValidationFailed` 1001 with `error.fields`.)

### Step 13 — Config

`appsettings.json` — add an `Admin` section:
```json
"Admin": { "Username": "admin", "Password": "" }
```
(Empty/absent password → seeder no-ops per OQ9a; real credentials supplied via env/secret in each
environment — never commit a real password.)

### Step 14 — Tests (owned by the test-engineer; definitive list)

Reuse the shipped harness: `[Collection("AuthIntegration")]`; DB/endpoint tests use the existing
`*DbTestBase`/`*ApiTestBase` families, a unique lowercase username prefix per class, dispose-time
cascade cleanup; DB-dependent tests `[SkippableFact]` (skip when MariaDB unreachable), never EF
InMemory. Seed an admin test user by setting `users.role = ADMIN` directly (or via the seeder).

**Unit (no DB):**
- `AuthenticatedUser` round-trips `Role` via `ToClaims`/`FromPrincipal`; absent `role` claim → `USER`
  (fail-safe); unknown value → treated as non-admin.
- `TokenWhitelistEntry`/`AuthTokenLookup` default `Role=USER` when the trailing arg is omitted
  (back-compat for pre-M11 cached/projected rows).
- `AdminUserService` guards (fake repos): self disable / demote / revoke-tokens / reset-password →
  14001; disable / demote / reset-password / revoke-tokens against another admin → 14002; a demote/
  disable that would leave zero admins → 14002 (last-admin); grant/revoke/reset on a non-admin target
  allowed.
- Validators: grant amount `< 0` → 1001; bad `bucket`/`sort` → 1001; `from > to` → 1001.
- `AdminDashboardService`/revenue mapping: buckets + totals; REVOKE rows excluded from revenue.

**Integration (real MariaDB):**
- `UserRepository.ListForAdminAsync` — paging, tier/status/role filters, username search, sort;
  returns account metadata only.
- `TierGrantRepository` — append-only insert; `GetRevenueAsync` sums only `GRANT` amounts, buckets by
  month/day, user-agnostic; grant history by user.
- Dashboard counts — tier/role/status distributions + signup buckets over `users` only.
- Seeder — creates the admin when absent (ADMIN role, Free tier, BCrypt hash, status ACTIVE);
  promotes an existing user to ADMIN without overwriting its password; no-op when config absent;
  idempotent across two runs.
- Grant flips `users.tier` → PREMIUM **and** writes a GRANT row; revoke → FREE + a REVOKE row.
- `UserRepository.CountByRoleAsync("ADMIN")` — feeds the last-admin guard.
- Disable sets `status=DISABLED` and revokes the user's tokens; enable sets `status=ACTIVE`. (No
  hard-delete path — OQ1 = (b).)

**Endpoint (WebApplicationFactory):**
- **Admin policy 403 for non-admins on EVERY `/admin/...` route** — a valid Free user AND a valid
  Premium user both get 403 `Forbidden 1004`; anonymous → 401.
- Seeded/ADMIN user can call every admin route (200).
- Grant flips tier + writes a GRANT row + (Redis available) the target's **next request reflects
  PREMIUM without a re-login** (cache-bust, OQ3a); revoke downgrades + writes a REVOKE row + reflects
  FREE.
- Disable sets `status=DISABLED`, revokes the target's tokens (existing tokens now 401) AND blocks
  login (14003); enable restores login.
- Reset-password revokes the target's tokens AND returns the temp password once in the response body
  (asserted; and it is never logged).
- Role promote (USER→ADMIN) then demote (ADMIN→USER), subject to the last-admin guard.
- Seeded-admin: create-or-promote idempotency (two runs) + no-op when config is absent.
- Dashboards aggregate correctly over `users`/`tier_grants` — tier distribution, role/status counts,
  signup buckets, revenue SUM over GRANT rows (REVOKE excluded), day/month buckets.
- **Privacy-boundary assertion (R10 — the headline safety test):** there is **NO** admin endpoint
  returning another user's members / expenses / events / shares / bank accounts; the user-list/detail
  responses contain **only** account metadata + grant history (assert the response shape has no ledger
  fields, and that no admin route exposes ledger content).
- Self / other-admin / last-admin guards over HTTP (14001/14002).

### Step 15 — Wrap-up

Update the Progress Log + Final Outcome; record the checkpoint answers in the Decision Log; confirm
the migration applied to the dev DB; note that **no cross-user ledger query was added** (R10).

## Impact Analysis

- **APIs:** New `AdminController` (`api/v1/admin/dashboard`, `/revenue`, `/users`, `/users/{uuid}`,
  `/users/{uuid}/tier/grant|revoke`, `/disable|enable|revoke-tokens|reset-password|role`), all
  `[Authorize(Policy="Admin")]`. **No `DELETE` route** (OQ1 = (b): disable-only). `POST /auth/login`
  gains a disabled-account rejection (14003). No change to existing ledger endpoints.
- **Database:** **One EF migration `AddAdminRoleStatusAndTierGrants`** (OQ13a): `users` ALTER (`role`,
  `status` `varchar(16)` defaults) + new `tier_grants` table (money `decimal(18,2)` +
  `ck_tier_grants_amount_non_negative`, no cascade FK — user_id/granted_by plain columns like
  `audit_logs`, indexes on `created_at` and `(user_id, created_at)`). **Users stay
  non-`IEntityDeletable`** (OQ1 = (b)) — no hard-delete path, zero ripple into existing queries. **No
  cross-user ledger query is built.**
- **Infrastructure:** New `Admin:` appsettings section (username/password; password via env/secret).
  New `[BackgroundService]` `AdminSeederHostedService` (picked up by the existing `RegisterDecorators`
  scan — no manual `AddHostedService`). A new Redis cache-bust primitive
  (`TokenService.RefreshCachedStateAsync`) reuses the existing token-cache mechanism (no topology
  change).
- **Services:** New `Services/Api/Admin/AdminUserService.cs`, `AdminDashboardService.cs`;
  `Repositories/TierGrantRepository.cs`, `AdminDashboardRepository.cs` (or methods on
  `UserRepository`); new methods on `UserRepository` + `AuthTokenRepository`. **Modified auth files
  (role propagation, mirror M10):** `Auth/Abstractions/ITokenWhitelistStore.cs`,
  `Repositories/AuthTokenRepository.cs`, `Auth/TokenWhitelistStore.cs`, `Auth/TokenService.cs`,
  `Auth/TokenValidator.cs`, `Auth/AuthenticatedUser.cs`, `Services/Api/Auth/AuthService.cs`. New
  constants (`UserRoles`, `UserStatuses`, `TierGrantActions`, `AuthorizationPolicies`), `Program.cs`
  (Admin policy), `ErrorCodes.cs` + `ErrorException.cs` (14xxx). `AppController` untouched.
- **Documentation:** this doc; the roadmap M11 line closed at wrap-up; the self-serve VNPay future
  seam re-noted.

## Decision Log

### Inherited decisions (locked upstream — NOT reopened)

1. **Admin privilege = `users.role` enum (`USER`/`ADMIN`, default `USER`) + a config-seeded admin
   account** created/promoted idempotently at startup.
2. **An `"Admin"` authorization policy + `AdminController : AppController`
   (`[Authorize(Policy="Admin")]`)**; non-admins → the already-wired `Forbidden 1004`/403;
   `AppController` stays LOCKED (policy attributes at the derived level).
3. **Payment = manual admin-grant ONLY** (no online gateway; self-serve VNPay a documented future
   seam). A grant flips `users.tier` → Premium and records the offline **amount + reference (+ note)**
   in a new **append-only payments/grants table**; revoke downgrades to Free. This table is the
   revenue dashboard's data source.
4. **Admin scope = metrics dashboard + manual tier grant/revoke + revenue dashboard + account-level
   user administration.**
5. **CRITICAL PRIVACY BOUNDARY — admin NEVER sees users' personal ledger data** (members, expenses,
   events, shares, bank accounts). Admin acts only on account metadata + payment/grant records; **no
   unscoped cross-user ledger query is built** — §4.1 holds even for admins.
6. **Error block = 14xxx** (M10 Tiers took 13xxx; the roadmap's earlier "13xxx Admin" is superseded).

### New decisions (resolved at the 2026-07-14 user checkpoint)

> All 15 Open Questions answered — 14 at the recommended option (a); **OQ1 = (b) was a user override.**
> One numbered point per OQ (binding decision + one-line reason); the full options/trade-offs are
> preserved inline under each OQ above.

1. **OQ1 = (b) [override] — disable-only, NO hard delete.** Accounts are only disabled/enabled
   (reversible); users stay non-`IEntityDeletable`; no cascade-delete path is built; the
   `DELETE /admin/users/{uuid}` endpoint is dropped. *Reason:* the user prefers reversible account
   controls; avoids the destructive-irreversible surface and any `IEntityDeletable` query ripple.
2. **OQ2 = (a):** `users.status` enum column (`ACTIVE`/`DISABLED`, default `ACTIVE`); disable = status +
   `RevokeAllAsync` + login block (14003); enable restores login. *Reason:* mirrors the `tier`
   string-enum pattern; disable already revokes tokens so no hot-path status check is needed.
3. **OQ3 = (a):** a targeted Redis cache-bust primitive for grant/revoke/role-change (live tier/role on
   the next request, no forced logout); disable + reset-password use `RevokeAllAsync`. *Reason:* a paid
   upgrade / promotion reflects promptly while sessions stay alive; disable needs an instant cut.
4. **OQ4 = (a):** two endpoints — `.../tier/grant` (amount/currency?/reference?/note?) and
   `.../tier/revoke` (note?); each writes its row + busts cache. *Reason:* clear verbs, per-action
   payloads.
5. **OQ5 = (a):** append-only `tier_grants`, plain `user_id`/`granted_by_user_id` (no cascade FK, like
   `audit_logs`) + username snapshots, `amount` `decimal(18,2)` + CHECK `>= 0`, `currency` VND default,
   GRANT/REVOKE discriminator, indexes `created_at` + `(user_id, created_at)`. *Reason:* immutable
   revenue/audit trail consistent with the audit log; privacy-safe display without joining `users`.
6. **OQ6 = (a):** metrics = account-metadata figures ONLY (user/tier/role/status counts + signup
   buckets); zero ledger aggregates, not even anonymous. *Reason:* honours the §4.1/R10 privacy
   boundary.
7. **OQ7 = (a):** `GET /admin/users` = account metadata only, paged + filter + sort; grant-count/
   last-grant allowed; NO ledger counts. *Reason:* the boundary + bounded lists for scale.
8. **OQ8 = (a):** include `POST /admin/users/{uuid}/reset-password` (hash + store + `RevokeAllAsync`,
   temp password returned once, never logged). *Reason:* a real recovery path with no email/reset flow.
9. **OQ9 = (a):** config-seeded admin (`Admin:Username`/`Admin:Password`), idempotent create-or-promote
   at startup (reuse the `OwnerRepresentativeBackfillHostedService` shape), no-op when absent, password
   never logged; + `POST /admin/users/{uuid}/role` to promote/demote others. *Reason:* standard
   bootstrap-credential pattern + a runtime path to more admins.
10. **OQ10 = (a):** guard the actions that **remove admin access or credentials** — self (14001) and
    any-admin-target / last-admin (14002). **Guarded = disable, revoke-tokens, reset-password, demote;
    NOT guarded = promotion and tier grant/revoke** (tier is orthogonal to admin privilege, so
    revoking an admin's Premium can never cause a lockout — allowed on any target). *Reason:* prevents
    accidental/malicious lockout while keeping non-privilege actions unrestricted; the system can never
    reach zero admins. *Doc correction (2026-07-14 review):* an earlier draft wrongly listed
    tier-downgrade as guarded — corrected to match the reviewed code (no behavior change).
11. **OQ11 = (a):** one `AdminController` named `Admin` (prefix from `[controller]`), logic split across
    `IAdminUserService` + `IAdminDashboardService`. *Reason:* clean `api/v1/admin/...` routing against
    the LOCKED base + cohesive services.
12. **OQ12 = (a):** `tier_grants` is the grant/revoke trail; NLog structured logging for the other
    admin actions; no new admin-audit table in M11. *Reason:* the durable trail already exists for the
    money actions; logging the rest is cheap. A dedicated `admin_audit_logs` is a future improvement.
13. **OQ13 = (a):** ONE migration `AddAdminRoleStatusAndTierGrants`. *Reason:* M11's schema is one
    cohesive unit.
14. **OQ14 = (a):** revenue = SUM(amount) over GRANT rows only, inclusive UTC `[from,to]` (optional =
    all-time), bucketed monthly (default) / daily; per-bucket + grand totals + references. *Reason:*
    "revenue = money taken via upgrade grants"; a trend view via the cheap M7 `GroupBy` pattern.
15. **OQ15 = (a):** grant `amount` required `>= 0` (0 = comp), `currency` optional default VND,
    reference/note optional. *Reason:* keeps the revenue source complete and explicit; VND matches the
    single-currency spec.

### Cross-cutting confirmations

- **Role → principal mirrors the M10 tier pattern exactly:** `Role` added to `AuthenticatedUser` + the
  token whitelist entry + `AuthTokenLookup` + a `"role"` claim + `TokenValidator` + login-issue +
  DB-fallback read + refresh (live-read), **fail-safe `USER`** when absent/unknown (never `ADMIN`).
- **Authorization:** the `"Admin"` policy in `Program.cs` + `[Authorize(Policy="Admin")]` on the
  controller; non-admins (Free AND Premium) → the already-wired `Forbidden 1004`/403. `AppController`
  untouched.
- **ONE migration**, **14xxx** error block (`14000 AdminUserNotFound`, `14001` self/other-admin
  destructive, `14002` last-admin, `14003 AccountDisabled`; grant/list bad input → `1001`).
- **No cross-user ledger query is built** — asserted by the privacy-boundary test.

## Progress Log

### 2026-07-14

- Started planning M11 (Admin suite).
- Read the source of truth: `The-ideal.md` §3.11 (manual upgrade path) + §4.1 (absolute privacy
  boundary); `CLAUDE.md`, `.agents/rules/rules.md`, `.claude/rules/rule.md` (template).
- Read the prior planning docs: `planning/tiers-premium-free.md` (the tier→principal pattern to mirror
  for role→principal; the ≤access-TTL staleness note + `RefreshAsync` reads live tier),
  `planning/debt-balance-and-stats.md` (the M7 DB-side `GROUP BY`/`SUM`/`COUNT` Stats-triad shape for
  the dashboards), and `planning/agent-dev-team.md` roadmap items 10+11 + the M11 deferred OQs.
- Grounded the plan in the live code: `User` + `Partials/User` (no role/status column; users NOT
  `IEntityDeletable`), `Constants/UserTiers`, the full auth path (`AuthenticatedUser`,
  `ITokenWhitelistStore`, `TokenValidator`, `TokenService`, `TokenWhitelistStore`,
  `AuthTokenRepository`, `OpaqueTokenAuthenticationHandler` — 403/1004 already wired,
  `RevokeAllAsync` kill-switch), `Program.cs` (`AddAuthorization` fallback policy),
  `Services/Api/Auth/AuthService` (login + the seeder seam), `UserRepository` (no list-all),
  `StatsRepository`/`StatsController` (dashboard pattern), `HostedServices/
  OwnerRepresentativeBackfillHostedService` (idempotent startup seeder shape), `ErrorCodes` (14xxx
  free), `ErrorException`, `AppController` (LOCKED), `Partials/BankAccount` + `Partials/Share`/`Event`
  (entity mapping + CHECK-constraint pattern), `AppDbContext`, `appsettings.json` (config pattern).
  Read the sibling `quick-ordering` `PaymentResultRecord` (+ partial) + `PaymentRecordPersistence` for
  the append-only money-record shape to adapt (NOT the VNPay gateway).
- Wrote the Requirements, 15 Open Questions (each with options + trade-offs + a recommended option
  (a)), Assumptions, the step-by-step Implementation Plan (role+status entity/migration →
  role→principal + seeded-admin seeder → Admin policy + AdminController → tier_grants entity/migration
  + grant/revoke service with the token-cache refresh → metrics + revenue dashboards over
  users/tier_grants only → user administration → 14xxx error codes → login block → tests incl. the
  privacy-boundary assertion + self/last-admin guards), Impact Analysis (the single migration + the
  modified auth files + the explicit "no ledger cross-user query added"), and this Progress Log.
- Recorded the 6 LOCKED decisions as inherited; left all 15 new OQs OPEN for the checkpoint.

- **All 15 Open Questions answered by the user at the 2026-07-14 checkpoint** — 14 at the recommended
  option (a); **OQ1 = (b)** (disable-only, NO hard delete) was a user override. Annotated each OQ
  inline, filled the Decision Log with one-line reasons, marked the Assumptions confirmed, and synced
  the Implementation Plan, the endpoint table (dropped `DELETE /admin/users/{uuid}`; users stay
  non-`IEntityDeletable`, no cascade-delete path), the single migration `AddAdminRoleStatusAndTierGrants`,
  the 14xxx error-code table (14002 message reworded to drop "delete"), and the test list (removed the
  hard-delete test; added reset-password / role promote-demote / seeder idempotency-and-no-op /
  disable-enable / cache-bust-no-relogin coverage; kept the privacy-boundary assertion and the
  self/other-admin/last-admin guards) to the answers. Confirmed: role→principal mirrors M10 tier
  exactly (fail-safe USER), the Admin policy + `[Authorize(Policy="Admin")]` (non-admins → 1004/403),
  ONE migration, `AppController` untouched, and **no cross-user ledger query is added**. **Doc
  unblocked — implementation can start.**

### 2026-07-14 — Implementation (Milestone 11 built)

Implemented M11 end-to-end per the approved decisions (14×(a), OQ1=(b)). Build clean, `dotnet test`
**931/931** (0 skipped), migration authored **and applied** to the dev DB, live smoke passed.

- **Constants:** `UserRoles` (USER/ADMIN), `UserStatuses` (ACTIVE/DISABLED), `TierGrantActions`
  (GRANT/REVOKE), `DashboardBuckets` (day/month), `AuthorizationPolicies` (Admin + `role` claim type).
- **Schema:** `User` gained `Role` + `Status` (POCO + `Partials/User` ctor defaults + `varchar(16)`
  mappings with defaults + indexes on `role`/`status`). New `TierGrant` entity (POCO + Partials):
  append-only, plain `user_id`/`granted_by_user_id` (no navigation FK, like `audit_logs`) +
  denormalized username snapshots, `amount decimal(18,2)` + CHECK `ck_tier_grants_amount_non_negative`,
  `currency` default VND, indexes on `created_at` and `(user_id, created_at)`, unique `uuid`. Registered
  in `AppDbContext`.
- **Migration:** `20260714152818_AddAdminRoleStatusAndTierGrants` — ONE migration (users ALTER role+status,
  create `tier_grants` + CHECK + indexes). Authored OFFLINE via the design-time factory, reviewed, and
  **applied** (`dotnet ef database update`).
- **Role → principal (mirrors M10 tier exactly, fail-safe USER):** `Role` added to
  `AuthenticatedUser` (property + `role` claim in `ToClaims` + `FromPrincipal` read, absent→USER),
  `TokenWhitelistEntry` (trailing default `UserRoles.User`), `AuthTokenLookup` (trailing default +
  `token.User.Role` projection in `GetByHashWithUserAsync`), `TokenWhitelistStore.LookupAsync` (passes
  `row.Role`), `ITokenService.IssueAsync`/`TokenService` (new `role` param on both cache writes),
  `TokenService.RefreshAsync` (passes `lookup.Role`, live-read from `users`), `TokenValidator` (sets
  Role fail-safe), `AuthService.LoginAsync` (passes `user.Role`). All existing record constructions are
  positional with fewer args, so the trailing default is fully back-compatible (no test-assertion ripple).
- **Cache-bust primitive (OQ3a):** `AuthTokenRepository.GetActiveHashesByUserAsync` (active, non-revoked,
  non-expired hashes) + `ITokenService/TokenService.RefreshCachedStateAsync` — evicts ONLY the user's
  Redis token cache keys (DB rows kept), so the next request re-reads live tier/role via the DB-fallback
  path with NO forced logout. Called post-commit after grant/revoke/role change. Disable + reset-password
  keep using the existing `RevokeAllAsync` kill-switch (immediate cut).
- **Login block:** `AuthService.LoginAsync` rejects a `DISABLED` account with `14003` AFTER the credential
  check (no account-existence leak on a bad password).
- **Errors:** 14xxx block (`14000 AdminUserNotFound`/404, `14001 AdminCannotTargetSelf`/400,
  `14002 AdminCannotTargetAdmin`/400, `14003 AccountDisabled`/403) + `GetDefaultHttpStatus` extended.
- **Repos:** `UserRepository` gained `ListForAdminAsync` (paged/filter/sort, account metadata only),
  `SetTierAsync`/`SetStatusAsync`/`SetRoleAsync`, `CountByRoleAsync` (last-admin guard). New
  `TierGrantRepository` (`AddAsync`, atomic `RecordAsync` = flip tier + append grant in ONE transaction,
  `ListByUserIdAsync`, `GetGrantSummariesAsync`, `GetRevenueAsync` — GRANT-only DB-side GROUP BY/SUM by
  month/day). New `AdminDashboardRepository.GetMetricsAsync` (users-only counts + signup buckets). **No
  cross-user ledger query is built anywhere** — repos touch only `users`, `auth_tokens`, `tier_grants`.
- **Services:** `AdminUserService` (list/get/grant/revoke/disable/enable/revoke-tokens/reset-password/
  role) with the guards (`EnsureDestructiveAllowed`: self→14001, any admin target→14002 which subsumes
  last-admin; demote applies it, promote is unguarded) + post-commit cache-bust/RevokeAllAsync + NLog
  structured logging (never logs passwords). `AdminDashboardService` (metrics + revenue).
- **DTOs/validators/profile:** `Models/Admin/*` (+ generic `Models/PagedResult<T>`), `Validators/Admin/*`
  (grant amount≥0, currency/reference/note lengths, sort/direction/bucket whitelists, from≤to,
  reset-password reuses the register password rules, role∈{USER,ADMIN}), `Mappings/AdminProfile`.
- **Controller/policy/config/seeder:** `AdminController : AppController` with
  `[Authorize(Policy="Admin")]` (thin, Vietnamese Swagger, no DELETE route). `Program.cs` adds the
  `Admin` policy (`RequireClaim("role", ADMIN)`). `appsettings.json` gains an empty `Admin:` section
  (password via env/secret). `AdminSeederHostedService` (`[BackgroundService]`, mirrors the owner-rep
  backfill): create-or-promote idempotently, no-op when config absent, never logs the password.
- **Privacy boundary verification (R10):** asserted in the smoke that the user-list rows, the user-detail
  response, and the dashboard response carry ONLY account-metadata + grant fields (keys checked against a
  ledger-term blocklist — none present); confirmed there is NO admin route returning members/expenses/
  events/shares/bank-accounts. Grep/design confirms no admin repo query touches a ledger table.
- **Cache-bust-no-relogin verification:** with a FREE-era access token held constant (no re-login), the
  Premium wallet gate returned 403/13003 before grant, **200 after grant**, and 403/13003 again after
  revoke — proving the Redis cache-bust makes the live tier apply on the very next request while the
  session stays alive.
- **Smoke results (dev DB + Redis up):** admin login + all `/admin/*` reachable; Free AND Premium
  non-admins → 403/1004 on every admin route; anonymous → 401; grant→PREMIUM+GRANT row, revoke→FREE+REVOKE
  row; disable→existing token 401 + login 14003, enable restores; reset-password returns temp password
  once + old tokens 401 + new password works; role promote (existing token then reaches `/admin/dashboard`
  via cache-bust) + demote; self-disable/self-demote→14001; demote/disable another admin→14002; dashboard
  distributions+signups (totalUsers=5); revenue SUM over GRANT rows; invalid bucket / negative amount →
  1001; unknown user → 14000. Smoke users + tier_grants cleaned up afterward.

**Deviation flagged (one test-file edit):** the plan-mandated new interface method
`IAuthTokenRepository.GetActiveHashesByUserAsync` (Step 6.4/7, the cache-bust primitive) forced
`FakeAuthTokenRepository` in `FairShareMonApi.Tests/TokenServiceTests.cs` to implement it or the test
project would not compile. Added a faithful, behavior-neutral implementation (returns the fake's active
hashes) — no test assertion/logic changed. The `Role` addition itself did NOT ripple any test (all record
constructions are positional and back-compatible). Reported to the orchestrator.

### 2026-07-14 — Test suite (Milestone 11 tested by the test-engineer)

Wrote and ran the full M11 test suite. **`dotnet test .\FairShareMonApi.sln` = 1047 passed / 0 failed /
0 skipped** (was 931 pre-M11 → **+116 new M11 tests**); green and **deterministic across two
consecutive full runs**; DB verified clean afterward (0 test-prefixed users, `tier_grants` total 0, 0
far-future seed rows, 0 orphaned grants); `appsettings.json` `Admin:` left untouched (empty password →
the seeder no-ops on a normal boot, so no admin is seeded outside tests). **No production code was
changed** — only `FairShareMonApi.Tests`. The one pre-existing test-file edit noted at handoff
(`FakeAuthTokenRepository.GetActiveHashesByUserAsync` in `TokenServiceTests.cs`) was verified faithful
(returns the user's non-revoked, non-expired hashes — mirrors the real repository) and kept.

Per-area breakdown of the added tests:

- **Unit — role on the principal (`AuthenticatedUserRoleTests`, 11):** `Role` round-trips through
  `ToClaims`/`FromPrincipal` (with tier), absent/empty `role` claim → USER (fail-safe, never ADMIN), an
  unknown value is carried but is never ADMIN (Admin policy denies), exactly one role claim is emitted,
  and the two positional records (`TokenWhitelistEntry`/`AuthTokenLookup`) default `Role = USER` when the
  trailing arg is omitted (pre-M11 back-compat).
- **Unit — validators (`AdminValidatorsTests`, 34):** grant amount `>= 0` (0 = comp) + currency/
  reference/note max-lengths; revoke note length; revenue/metrics `From <= To` (only when both present) +
  bucket ∈ {day,month}; user-list page/pageSize cap + sort/direction whitelist; role ∈ {USER,ADMIN};
  reset-password reuses the register password policy.
- **Unit — `AdminUserService` guards + effects (`AdminUserServiceTests`, 21):** grant records a GRANT row
  (tier→PREMIUM, granted-by/username snapshots, VND default) + cache-busts + never revokes; revoke records
  a REVOKE row (amount 0, tier→FREE) + cache-busts; disable sets DISABLED + `RevokeAllAsync`; reset-password
  rehashes (never stores plaintext) + returns the temp password once + `RevokeAllAsync`; the OQ10 guards
  (self→14001; any admin target→14002; promotion unguarded, demotion guarded); unknown target→14000;
  bad input→ValidationException. (Fakes for the repos/token-service; real AutoMapper + validators.)
- **Unit — `AdminDashboardService` mapping (`AdminDashboardServiceTests`, 5):** metrics distributions +
  signup buckets and revenue buckets/totals/references map faithfully; `From`/`To`/`Bucket` echo back; the
  requested bucket is passed to the repo; bad range/bucket → ValidationException.
- **Integration — `TierGrantRepository` (`TierGrantRepositoryTests`, 10):** `RecordAsync` flips tier +
  appends the row **atomically** (a forced negative amount trips the real
  `ck_tier_grants_amount_non_negative` CHECK → the whole transaction rolls back, leaving NEITHER tier flip
  NOR row), unknown user → null + nothing written; grant history is per-user newest-first; revenue is a
  DB-side GROUP BY/SUM over **GRANT rows only** (REVOKE excluded), user-agnostic, month/day buckets,
  inclusive `[from,to]`; grant summaries (count + last, GRANT-only). Revenue tests bound their range to a
  far-future window so the global revenue query sees only their own rows.
- **Integration — admin `UserRepository` (`AdminUserRepositoryTests`, 11):** `ListForAdminAsync` paging +
  tier/status/role filters + username search + sort (scoped to the class prefix for determinism; the
  projection is account-metadata-only by type); `SetTier/Status/Role` (+ false on unknown uuid);
  `CountByRoleAsync` (by delta).
- **Integration — `AdminDashboardRepository` (`AdminDashboardRepositoryTests`, 3):** tier/role/status
  distributions + total (by delta) and signup month/day buckets (over an isolated far-future window) —
  over `users` only.
- **Integration — `AdminSeederHostedService` (`AdminSeederHostedServiceTests`, 4):** creates the admin
  when absent (ADMIN/Free/ACTIVE, a BCrypt hash that verifies, never the plaintext), promotes an existing
  account WITHOUT overwriting its password, no-ops when config is absent, idempotent across two runs.
  (`ExecuteAsync` driven directly over a real DI scope via reflection; the configured username carries the
  class prefix so it is swept on dispose.)
- **Endpoint — `AdminEndpointTests` (17, `WebApplicationFactory`, real MariaDB/Redis):** the
  **authorization matrix over EVERY `/admin/*` route** (anonymous→401; FREE **and** PREMIUM non-admins→403
  `Forbidden 1004`; admin allowed); grant→PREMIUM + **cache-bust proven** (the target's pre-existing
  FREE-era token passes the Premium wallet gate on its next request with NO re-login; revoke→FREE closes
  the gate again; GRANT/REVOKE rows asserted); disable→existing token 401 + login blocked + enable
  restores login; reset-password returns the temp password once + old tokens dead + new password logs in;
  role promote reaches `/admin/*` on the pre-existing token via cache-bust; the self/other-admin guards
  (14001/14002); dashboards (distributions + signup buckets; revenue SUM + buckets + references); and the
  **§4.1/R10 PRIVACY-BOUNDARY test** — the target is given distinctive ledger data (a member + a bank
  account with unique marker values) and every admin response (`/admin/users`, `/admin/users/{uuid}`,
  dashboard, revenue) is asserted to contain **neither the ledger marker values NOR any ledger field key**
  (`accountNumber`, `payerMemberId`, `expenseTime`, `isOwnerRepresentative`, `shares`, …), while still
  returning the expected account metadata + grant history.

**Admin-caller + cache-bust approach:** an ADMIN caller is minted by registering a user, flipping
`users.role = ADMIN` directly in the DB, then logging in (the role rides the issued token) — mirroring how
the M10 tests obtain a Premium caller; the default `WebApplicationFactory` is used (no committed
`appsettings` change; no config-seeded admin). Cache-bust is verified end-to-end by holding a
pre-existing token constant across a grant/revoke or promote and observing the tier/role gate flip on the
very next request without a re-login (these tests `SkipIfNoRedis` since the cache-bust needs a live
Redis).

**No production bug found.** Two points where the task brief's phrasing differs from the shipped (and
plan-locked) behavior — the tests assert the actual, correct behavior:
1. **Disabled login returns HTTP 403 (code 14003), not 400.** `ErrorException.GetDefaultHttpStatus` maps
   `AccountDisabled` → 403 (matching the Step-12 error table and the `ErrorCodes` doc-comment); the brief's
   "login → 400 14003" is inaccurate. Test asserts 403 + 14003.
2. **Role demotion of an admin is impossible via the API by design (OQ10a).** Because a demote is a
   destructive action and the guard blocks ANY admin target (14002, subsuming the last-admin case),
   demoting a promoted admin returns 400/14002 — admins are removed via DB/config, as OQ10(a) states. The
   role test therefore proves promotion + cache-bust and that demoting the now-admin target is guarded
   (14002), rather than a full promote→demote round-trip.

**Coverage gaps / notes:** the metrics/revenue **repository** queries are global (cross-user by design over
`users`/`tier_grants`), so distribution counts are asserted by delta and time-bucketed figures over an
isolated far-future window; a purely count-based "last remaining admin can't be demoted/disabled" scenario
is not separately reachable because the stricter admin-on-admin guard (14002) blocks it first — this is the
locked OQ10(a) behavior and is covered by the other-admin guard tests.

### 2026-07-14 (code review — APPROVED, 0 blocking — milestone closed)

Code-reviewer verdict: **APPROVE, 0 blocking** (1 informational + 2 nits). Final suite
**dotnet test = 1047 passed / 0 failed / 0 skipped**, deterministic across runs, DB swept clean,
`Admin:` config left empty (no seeded admin on a normal boot). Verified checks:

- **§4.1 privacy boundary holds (R10).** The admin services/repositories touch ONLY `users`,
  `tier_grants`, and `auth_tokens`; a grep for ledger tables (members/expenses/events/shares/
  bank_accounts) and for `.Include(` across all admin paths returned **0 matches**. The admin DTOs
  carry only account-metadata + grant fields, and `AdminProfile` excludes `PasswordHash`. The
  dashboards/revenue aggregate over `users`/`tier_grants` only — **no ledger aggregate of any kind**.
- **Authorization complete.** The `Admin` policy is `RequireClaim("role","ADMIN")`; the class-level
  `[Authorize(Policy=Admin)]` gates every `AdminController` route; a non-admin (FREE and PREMIUM) → 403
  `Forbidden 1004`, anonymous → 401; exactly one `role` claim is emitted per principal.
- **Role → principal fail-safe.** An absent/unknown role resolves to `USER` at every hop — the Redis
  cache entry, the DB-fallback read, and the refresh live-read — and is **never** `ADMIN`.
- **Lockout guards.** `EnsureDestructiveAllowed` blocks self (14001) and any-admin-target (14002) on
  disable / revoke-tokens / reset-password / demote; **promotion is unguarded**; the zero-admins state
  is unreachable because the any-admin-target guard subsumes the last-admin case.
- **Disable.** Sets `status=DISABLED` + post-commit `RevokeAllAsync`; login rejects a `DISABLED`
  account → 403 `14003`.
- **Grant/revoke + cache-bust.** `RecordAsync` performs the tier-flip + row append **atomically in one
  transaction** (a CHECK violation trips `NoCommit`, rolling back both); the post-commit
  `RefreshCachedStateAsync` evicts ONLY the user's Redis keys → the live tier applies on the next
  request with **no forced logout**; revenue SUMs GRANT rows only; money is `decimal` + DB CHECK.
- **Migration + snapshot in sync** (no `DELETE` route, no cascade FK); conventions clean.

**Two clarified behaviors confirmed correct + intended** (the reviewer flagged the task brief's
phrasing, not the code): (1) a disabled-account login returns **403 / 14003** (per the Step-12 error
table and `ErrorException.GetDefaultHttpStatus`), not 400; (2) **admin demotion is blocked by the 14002
any-admin-target guard** (an admin is removed via DB/config, per OQ10(a)) — the OQ10 doc annotation +
Decision Log were corrected accordingly (documentation fix, no behavior change).

## Final Outcome

**Milestone 11 (Admin suite) is COMPLETE — implemented, built, migrated, tested, and code-reviewed
(APPROVE, 0 blocking).** All decisions (14×(a) + OQ1=(b)) are honored. Shipped:

- **Schema:** `users.role` (USER/ADMIN) + `users.status` (ACTIVE/DISABLED) columns + the append-only
  `tier_grants` table (money `decimal(18,2)` + `ck_tier_grants_amount_non_negative`, no cascade FK,
  denormalized snapshots), all in **ONE** applied migration `AddAdminRoleStatusAndTierGrants`.
- **Role → principal:** rides the token/claim/validator/refresh exactly like the M10 tier, **fail-safe
  USER** (never ADMIN); a targeted Redis **cache-bust primitive** applies a tier/role change on the
  next request with **no forced logout**.
- **Authorization:** an `Admin` policy (`RequireClaim("role","ADMIN")`) + `AdminController :
  AppController` (`[Authorize(Policy="Admin")]`); non-admins → 403 `Forbidden 1004`, anonymous → 401.
  Routes: dashboard, revenue, user list/detail, tier grant/revoke, disable/enable, revoke-tokens,
  reset-password, role — **NO delete** (OQ1=(b)).
- **Config-seeded admin:** `AdminSeederHostedService` create-or-promotes idempotently at startup,
  no-ops when `Admin:` config is absent, never logs the password.
- **Manual tier grant/revoke:** atomic tier-flip + append an immutable payment row recording
  **amount + reference (+ note)** in one transaction; **revenue = SUM of GRANT rows** only.
- **Dashboards:** metrics + revenue aggregate **cross-user over `users`/`tier_grants` ONLY** — no
  ledger aggregate.
- **Account controls:** disable = token-kill (`RevokeAllAsync`) + login block (403/14003), enable
  restores login; reset-password returns the temp password once + kills tokens; role promote/demote.
- **Lockout guards:** self (14001) + any-admin-target/last-admin (14002) on privilege/credential
  actions; tier grant/revoke allowed on any target (orthogonal to privilege).
- **The strict privacy boundary (§4.1/R10) is preserved** — no admin endpoint returns any user's
  personal ledger data and **no cross-user ledger query was added**.

`dotnet build` clean; `dotnet test` **1047/1047** (0 failed, 0 skipped); live smoke + code review
**APPROVE, 0 blocking**. Self-serve VNPay upgrade remains a documented future seam. **This completes the
full M1–M11 roadmap — the app is feature-complete per `The-ideal.md` + the added admin suite.**

## Future Improvements

- **Self-serve VNPay upgrade** (portable from the `quick-ordering` sibling) — the documented online
  payment seam that replaces/augments manual grants; `tier_grants` already models the record side.
- **Premium expiry / time-boxed subscriptions** — the M10 future seam (`premium_expires_at` + a lazy/
  scheduled downgrade); grants could then carry a period.
- **Dedicated `admin_audit_logs` table** (OQ12b) if a queryable trail of all admin actions
  (disable/enable/reset-password/role) becomes a requirement.
- **Hard account deletion / right-to-erasure** (OQ1 = (b) deferred it) — a distinct irreversible
  endpoint cascading to ledger data, if a real GDPR-style erase requirement arrives; `tier_grants`
  already keeps no cascade FK so the revenue trail would survive.
- **Admin role granularity** (e.g. SUPPORT vs SUPERADMIN) beyond the binary USER/ADMIN.
- **Operational, ledger-free system metrics** (e.g. request/error rates) if admin observability is
  wanted without crossing the ledger boundary.
- **Code-review nit — dead `IUserRepository.CountByRoleAsync`** (no caller: the last-admin case is
  subsumed by the 14002 any-admin-target guard). Either remove it or drop the misleading "feeds the
  last-admin guard" comment. Non-blocking, no behavior impact.
- **Code-review nit — unused `TierGrantRepository.AddAsync`** (the live path is the atomic
  `RecordAsync`); it also awkwardly wraps `Task.FromResult` in an async transaction lambda. Remove or
  simplify. Non-blocking, no behavior impact.
