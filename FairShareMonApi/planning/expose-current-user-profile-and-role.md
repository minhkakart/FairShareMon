# Expose current-user profile (incl. role) to the authenticated client

Surface the authenticated user's identity/authorization to the client after login and after a
boot-time refresh rehydrate: add `role` to the client-facing user DTO and give the SPA a reliable
way to fetch the current user's profile (username, tier, role). **API surface only — no schema
change.**

## Objective

Close the backend gap the FairShareMonWeb foundation cycle hit while wiring the SPA's auth + routing
(`FairShareMonWeb/planning/frontend-foundation.md`, the `AdminRoute` seam + the reload
account-label nit). The frontend has **no way to obtain the authenticated user's identity/authorization
after login**:

1. **`UserResponse` exposes no `role`.** M11 added `users.role` (`USER`/`ADMIN`) and it rides the
   principal/token/claims, but the client-facing `UserResponse` is only `{ uuid, username, tier,
   createdAt }`. The SPA's `AdminRoute` guard (`user?.role === "ADMIN"`) can therefore never admit an
   admin — it is currently a fail-safe deny-all seam awaiting a backend role source.
2. **No current-user endpoint / no user payload on login or refresh.** Login and refresh return only a
   `TokenPairResponse`. There is no `GET /auth/me`. After a page reload the SPA rehydrates the session
   via `/auth/refresh` (which returns tokens only), so it cannot repopulate the user's identity
   (username, tier, role) — the account UI falls back to a generic label and the admin gate stays
   closed.

Deliver a reliable way to fetch the authenticated user's profile **including role** (and username,
tier), usable both right after login and after a boot-time refresh rehydrate.

## Background

Grounded in the live code (read 2026-07-16):

- **`Models/Auth/UserResponse.cs`** = `{ string Uuid, string Username, string Tier, DateTime
  CreatedAt }`. `Tier` **is already present** (the frontend's report is correct that the shape is
  `{ uuid, username, tier, createdAt }`); only `Role` is missing. No password/hash is ever exposed
  (documented invariant in the DTO's XML doc).
- **`Mappings/AuthProfile.cs`** declares `CreateMap<User, UserResponse>()` (member-name convention).
  The `User` entity (`Database/Entities/User.cs`, M11) already has a non-null `Role` property
  (`UserRoles.User`/`UserRoles.Admin`, DB default `USER`) and a `Status` property. Adding `Role` to
  `UserResponse` maps automatically with no profile change; adding a member that has no matching
  source would break AutoMapper's assertion, but `Role` matches.
- **`Auth/AuthenticatedUser.cs`** already carries `Id` (user UUID), `Username`, `Tier`, and `Role`,
  materialized from the token whitelist entry / claims with **fail-safe defaults** (`Tier` → `FREE`,
  `Role` → `USER` when absent/unknown — never `ADMIN`). It does **not** carry `CreatedAt`.
- **`Controllers/AppController.cs` (LOCKED)** exposes `protected AuthenticatedUser AuthenticatedUser`,
  which throws a 401 `ErrorException(ErrorCodes.Unauthorized, ...)` on an anonymous request. All
  controllers read the caller identity through it.
- **`Repositories/UserRepository.cs`** exposes `GetByUuidAsync(uuid)` returning the full `User` — the
  existing primitive for a DB-backed current-user read (used today by `ChangePasswordAsync`).
- **Auth freshness facts (from `planning/user-authentication.md` + `planning/admin-management.md`
  M10/M11):** `Tier` and `Role` ride the token whitelist entry and are set on the principal without a
  per-request DB hit. An **access token keeps its cached tier/role for ≤ its TTL (~30 min)**;
  `RefreshAsync` reads the **live** `users.tier`/`users.role` (so a refresh busts staleness
  immediately); M11's grant/revoke/role-change calls `RefreshCachedStateAsync`, which deletes the
  user's Redis token keys so the **next request re-reads live tier/role from the DB fallback**. Net:
  a value read from `AuthenticatedUser` is live after a cache-bust or refresh, but a value read from
  an unrefreshed, un-busted access token can lag up to the access TTL.
- **Anonymous handling is already wired.** Everything not `[AllowAnonymous]` is covered by the
  `FallbackPolicy` (`RequireAuthenticatedUser`); an anonymous request is rejected by
  `OpaqueTokenAuthenticationHandler.HandleChallengeAsync`, which writes a **wrapped 401**
  (`ErrorCodes.Unauthorized = 1002`). No new error code is needed for the new endpoint.
- **Privacy boundary validated.** `The-ideal.md` §4 rule 1 (line 140): "Riêng tư tuyệt đối: mỗi người
  dùng chỉ nhìn thấy và thao tác được dữ liệu của chính mình" — privacy is about a user seeing only
  **their own** data and not others'. M11 §4.1's admin boundary is that admins must not see **other
  users'** ledger data. A user learning **their own** role is their own account metadata — it is
  squarely within "their own data" and violates no boundary. This reading is validated against both
  the spec and the M11 doc.
- **Frontend trace.** `FairShareMonWeb/CLAUDE.md` records the "Auth-guard seam (flagged)": the
  `AdminRoute` reads `user?.role === "ADMIN"` which is always undefined today → fails safe (denies);
  `frontend-foundation.md` Assumptions flag "pending confirmation the login/refresh/user payload
  carries a role field". This doc closes that seam from the backend side.

## Requirements

- **R1 — Expose `role`.** Add `Role` to the client-facing user DTO so the SPA can read the caller's
  authorization. `Tier` is already exposed (confirm, no change). Value is a string (`USER`/`ADMIN`)
  consistent with the existing `Tier` string representation.
- **R2 — Current-user retrieval.** Provide a way for the client to obtain the current user's profile
  (uuid, username, tier, role) both right after login and after a boot-time `/auth/refresh`
  rehydrate.
- **R3 — Authorization + scoping.** The retrieval path is authenticated (the caller's own account
  only — trivially resource-owned via the caller's token); anonymous → the already-wired **401
  `1002`** wrapped envelope. No cross-user access, no new error code.
- **R4 — Envelope consistency.** Response is the standard `ApiResult<UserResponse>` envelope; the
  endpoint derives from `AppController` (LOCKED, untouched) and carries Vietnamese Swagger annotations.
- **R5 — No schema change.** `role`/`tier`/`status` columns already exist (M10/M11) — this is DTO +
  endpoint surface only. **No EF migration.** State this explicitly in Impact Analysis.
- **R6 — Fail-safe role.** However the profile is sourced, an unknown/absent role must resolve to
  `USER`, never `ADMIN` (preserve the M11 fail-safe invariant).

## Open Questions

> All options list trade-offs; the **Recommended** option is marked. These are the design decisions
> for the checkpoint — the orchestrator brings them to the user. Nothing below is silently defaulted.

### OQ1 — Add `role` to `UserResponse` (and confirm `tier`)? — Resolved 2026-07-16: (a)

The minimal, load-bearing change. `role` is what the `AdminRoute` guard needs; `tier` is already
present.

- **(a) Recommended — add `string Role` to `UserResponse` (maps automatically from `User.Role`); keep
  `Tier` as-is.** One field on one DTO; `AuthProfile` unchanged (member-name match). The DTO is used
  by `register` (`ApiResult<UserResponse>`) and by whatever retrieval path OQ2 picks, so `role`
  becomes available everywhere `UserResponse` is returned — including the register response (harmless;
  a freshly registered user is always `USER`). Trade-off: `register` now also returns `role` (benign
  extra field; not a breaking change — additive).
- (b) Introduce a **separate** current-user DTO (e.g. `CurrentUserResponse { uuid, username, tier,
  role }`) and leave `UserResponse` (register) unchanged. Keeps `role` off the register response.
  Trade-off: a second near-identical DTO + a second mapping to maintain, for the sake of hiding a
  field that is trivially `USER` at register time. More surface, little gain.

### OQ2 — How does the client obtain the current user? — Resolved 2026-07-16: (a)

- **(a) Recommended — add `GET /auth/me` returning the current-user profile
  (`ApiResult<UserResponse>`).** Pairs cleanly with the SPA's boot rehydrate: after `/auth/refresh`
  succeeds on reload, the client calls `/auth/me` once to repopulate identity; after login the client
  calls it once too. Keeps the login/refresh token contract **unchanged** (the frontend already coded
  `ApiResult<TokenPairResponse>` for both). Trade-off: one extra round-trip after login and after a
  boot refresh (both infrequent, once per session/boot — negligible).
- (b) Include a **user payload in the login + refresh responses** (e.g. change login/refresh to return
  `{ user: UserResponse, tokens: TokenPairResponse }`). No extra round-trip; identity arrives with the
  tokens. Trade-off: **breaking change** to two contracts the frontend-foundation already implemented
  against (`ApiResult<TokenPairResponse>`), couples the token pair to profile data, and `/auth/refresh`
  is anonymous and returns a plain DTO auto-wrapped by `[ResponseWrapped]` — reshaping it touches that
  handoff. Higher blast radius for a marginal round-trip saving.
- (c) **Both** — add `GET /auth/me` **and** embed the user in login/refresh. Maximum convenience (no
  round-trip on login, plus a canonical refetch endpoint). Trade-off: the union of (b)'s breaking
  contract change and (a)'s new endpoint; duplicated profile-shaping in two code paths to keep in sync.

> Recommendation rationale: `GET /auth/me` (a) is the standard, lowest-risk shape; it leaves the
> login/refresh contracts the SPA already built against untouched, and because `/auth/refresh` reads
> live tier/role, a `/auth/me` call right after a boot refresh is always fresh.

### OQ3 — Data source for the current-user profile (only if OQ2 = a or c) — Resolved 2026-07-16: (a)

`GET /auth/me` could be built two ways:

- **(a) Recommended — live DB read via `UserRepository.GetByUuidAsync(AuthenticatedUser.Id)` → map to
  `UserResponse`.** Always returns live `tier`/`role`/`createdAt`; `UserResponse` needs `CreatedAt`,
  which the token/principal does **not** carry, so a DB read is required anyway to populate it fully.
  `/auth/me` is called at most once per login and once per boot — a single indexed lookup is
  negligible. Trade-off: one DB read per call (acceptable at this frequency; not on a hot path).
- (b) Build `UserResponse` from `AuthenticatedUser` (token/claims) — **no DB hit**. Trade-off: cannot
  populate `CreatedAt` (absent from the principal) without adding it to the token entry (schema of the
  cache entry + claims churn, and it would then be a fixed snapshot); and tier/role can lag up to the
  access-TTL on an unrefreshed, un-busted token. Freshness is weaker for no meaningful win given the
  low call frequency.

> If OQ2 = (b) only (payload on login/refresh, no `/auth/me`), this question still applies to how the
> embedded `user` is built — recommend the same live DB read at login (the user row is already loaded
> in `LoginAsync`) and at refresh (a `GetByUuidAsync` on the rotated user).

### OQ4 — Route name / placement for the current-user endpoint (only if OQ2 = a or c) — Resolved 2026-07-16: (a)

- **(a) Recommended — `GET api/v1/auth/me` on the existing `AuthController`.** Co-located with the
  other auth/session endpoints; conventional "me" naming the SPA expects; `[controller]=Auth` yields
  the `api/v1/auth` prefix for free. Trade-off: none material.
- (b) `GET api/v1/auth/current-user` (spelled-out variant). More explicit, less conventional.
- (c) A `UsersController` with `GET api/v1/users/me`. Cleaner if a broader user-profile surface is
  planned later. Trade-off: a whole new controller for one endpoint now; the identity/session concern
  lives with auth today. Overkill unless a users area is imminent.

### OQ5 — Also expose account `status` (`ACTIVE`/`DISABLED`) on the DTO? — Resolved 2026-07-16: (a)

The frontend only asked for `role` (+ username, tier). `status` was added by M11.

- **(a) Recommended — do NOT expose `status` on `UserResponse` now.** A `DISABLED` user cannot
  authenticate at all (login is rejected with `14003` and all tokens are revoked on disable), so any
  caller holding a valid token is necessarily `ACTIVE` — a self-`status` field would always read
  `ACTIVE` and carry no information for the owning user. Keep the DTO minimal. Trade-off: none for the
  current need; additive later if a use case appears.
- (b) Expose `status` too (future-proofing / symmetry with the admin metadata model). Trade-off: a
  field that is invariably `ACTIVE` for the authenticated self — noise now.

## Assumptions

- The SPA's session model (frontend-foundation OQ3, locked): access token in memory, refresh token in
  `localStorage`, rehydrate on boot via `/auth/refresh`. A current-user fetch after login and after
  the boot refresh fits this exactly (`GET /auth/me` recommendation is built around it).
- Exposing the caller's **own** role/tier to the caller does not breach any privacy rule (validated
  against `The-ideal.md` §4 rule 1 and the M11 §4.1 admin boundary — both concern **other** users'
  data). If the user disagrees, OQ1 becomes contentious; flagged for the checkpoint.
- No new user-facing error message is required: the endpoint's only failure mode is anonymous → the
  existing 401 `1002` wrapped envelope from the auth handler. Success returns data (no message key).
- `AppController` stays LOCKED and untouched; the new endpoint is an ordinary action on
  `AuthController` reading `AuthenticatedUser`.
- No new NuGet dependency; no config change.

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/`. Concrete files below assume the **recommended**
> option for each OQ (OQ1a + OQ2a + OQ3a + OQ4a + OQ5a); re-sync after the checkpoint if the user
> chooses otherwise. All user-facing strings + Swagger summaries are Vietnamese.

### Step 1 — Add `role` to the client DTO (R1, OQ1a)

1. `Models/Auth/UserResponse.cs` — add `public string Role { get; set; } = string.Empty;` (after
   `Tier`). Update the XML doc to note it now carries the caller's role.
2. `Mappings/AuthProfile.cs` — **no change** (member-name match; `User.Role` → `UserResponse.Role`
   maps automatically). Confirm AutoMapper's configuration assertion still passes (the test suite's
   mapping-config test, if present, covers this).

### Step 2 — Current-user retrieval service method (R2, R3, OQ2a/OQ3a)

`Services/Api/Auth/AuthService.cs` (`IAuthService` + impl) — add:

- `Task<UserResponse> GetCurrentUserAsync(string userUuid, CancellationToken cancellationToken =
  default)`:
  - `var user = await userRepository.GetByUuidAsync(userUuid, cancellationToken) ?? throw new
    ErrorException(ErrorCodes.Unauthorized, MessageKeys.Error.Unauthorized);` (a valid token whose
    user row vanished is treated as unauthenticated — mirrors `ChangePasswordAsync`).
  - `return mapper.Map<UserResponse>(user);` — live `tier`/`role`/`createdAt`, `role` fail-safe is the
    persisted column value (never null; DB default `USER`).

### Step 3 — Endpoint (R3, R4, OQ2a/OQ4a)

`Controllers/AuthController.cs` — add a guarded action (no `[AllowAnonymous]`, so the FallbackPolicy
requires auth; anonymous → wired 401 `1002`):

| Verb + Route | Auth | Request → Response | Notes |
|---|---|---|---|
| `GET api/v1/auth/me` | **guarded** | (none) → `ApiResult<UserResponse>` | returns the caller's profile incl. `role`; reads `AuthenticatedUser.Id` |

```
[HttpGet("me")]
[SwaggerOperation(Summary = "Thông tin tài khoản hiện tại",
    Description = "Trả về hồ sơ của người dùng đang đăng nhập (uuid, tên đăng nhập, hạng, vai trò).")]
[SwaggerResponse(StatusCodes.Status200OK, "Lấy thông tin tài khoản thành công.", typeof(ApiResult<UserResponse>))]
[SwaggerResponse(StatusCodes.Status401Unauthorized, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]
public async Task<IActionResult> GetCurrentUserAsync(CancellationToken cancellationToken) =>
    ApiResult<UserResponse>.Success(await authService.GetCurrentUserAsync(AuthenticatedUser.Id, cancellationToken));
```

The Swagger padlock appears automatically (the `AuthorizeOperationFilter` marks every
non-`[AllowAnonymous]` operation).

### Step 4 — Message keys (R4)

**None required.** The endpoint returns data on success (no message) and reuses the existing
`Error.Unauthorized` (1002) for the anonymous path. No `Constants/MessageKeys.cs` or resx additions.
(Swagger annotation strings are inline Vietnamese, not localized resources — consistent with the other
`AuthController` actions.)

### Step 5 — Tests (owned by the test-engineer; definitive list)

Reuse the shipped harness (`[Collection("AuthIntegration")]`, the `Auth*TestBase` families, unique
lowercase username prefix per class, dispose-time cascade cleanup, `[SkippableFact]` for DB-dependent
tests — never EF InMemory), consistent with `planning/user-authentication.md` Step 9 and
`planning/admin-management.md` Step 14.

**Unit (no DB):**
- **AutoMapper mapping:** `User` → `UserResponse` maps `Role` (and `Tier`, `Uuid`, `Username`,
  `CreatedAt`); AutoMapper configuration assertion (`AssertConfigurationIsValid`) still passes with the
  new member (no unmapped `UserResponse` member).
- **Serialization:** `UserResponse` serializes `role` as a camelCase JSON key (consistent with the
  envelope's camelCase contract the SPA branches on).

**Integration (real MariaDB, WebApplicationFactory endpoint):**
- `GET /auth/me` with a valid Bearer for a **normal** user → 200 wrapped envelope, `data` =
  `{ uuid, username, tier, role: "USER", createdAt }` matching the seeded user; no password/hash field
  present.
- `GET /auth/me` with a valid Bearer for an **ADMIN** user (seed by setting `users.role = ADMIN` or via
  the M11 seeder) → `role: "ADMIN"`.
- `GET /auth/me` **anonymous** (no `Authorization` header) → **401 wrapped envelope, code 1002** (the
  FallbackPolicy / `HandleChallengeAsync` path — asserts the endpoint is guarded).
- `GET /auth/me` with a **revoked/expired** access token → 401 `1002` (token no longer valid).
- **Freshness (fail-safe + live-read):** after an admin grant/role change bumps a user's `tier`/`role`
  and busts the cache (M11 `RefreshCachedStateAsync`), a subsequent `/auth/me` reflects the **live**
  values (validates OQ3a's DB-read source; also that an absent/unknown role never yields `ADMIN`).
- `register` response (`ApiResult<UserResponse>`) now includes `role: "USER"` (additive-field
  regression guard — confirms the new field appears and defaults correctly for a fresh account).

### Step 6 — Verification

- `dotnet build` clean; `dotnet test` green.
- Live smoke: login as a seeded ADMIN → `GET /auth/me` with the Bearer → `role: "ADMIN"`, correct
  `tier`/`username`/`uuid`/`createdAt`; login as a normal user → `role: "USER"`; no Bearer → 401
  `1002` wrapped envelope.
- Update this doc's Progress Log + Final Outcome.

## Impact Analysis

- **APIs:** one new endpoint `GET api/v1/auth/me` (guarded) → `ApiResult<UserResponse>`;
  `UserResponse` gains a `role` field (additive — also appears on the existing `register` response, a
  non-breaking addition). Login/refresh contracts **unchanged** (recommended path). Swagger gains the
  `/auth/me` operation with a padlock.
- **Database:** **NONE.** `role`, `tier`, and `status` columns already exist (M10/M11). **No EF
  migration, no data migration.** This is DTO + endpoint surface only.
- **Infrastructure:** none. No config, no new package, no Redis/DB wiring change.
- **Services:** `AuthService`/`IAuthService` gain `GetCurrentUserAsync` (thin: one `GetByUuidAsync` +
  an AutoMapper map). `AuthController` gains one action. `AppController`, `ApiResult`, the auth
  pipeline, and `AuthProfile` are untouched.
- **UI:** unblocks the FairShareMonWeb `AdminRoute` guard (`role` now available) and the boot-rehydrate
  account label (`/auth/me` repopulates identity). No backend UI.
- **Documentation:** this doc; `UserResponse` XML doc updated; the frontend can drop the "Auth-guard
  seam (flagged)" caveat in `FairShareMonWeb/CLAUDE.md` once shipped (frontend-owned follow-up).

## Decision Log

### Decision
This work was **surfaced by the frontend-foundation cycle** (`FairShareMonWeb/planning/frontend-foundation.md`
— the `AdminRoute` deny-all seam and the reload account-label nit; echoed in `FairShareMonWeb/CLAUDE.md`
"Auth-guard seam (flagged)"). The trace is preserved here so the causal chain is recorded.

### Reason
The frontend built `AdminRoute` as a fail-safe deny-all awaiting a backend role source, and flagged in
its Assumptions that the login/refresh/user payload carries no role. This backend gap (no `role` on the
DTO, no current-user endpoint) is the blocker; it is a backend concern and owned here, not in the SPA.

### Decision
No EF migration is part of this work.

### Reason
`users.role`, `users.tier`, and `users.status` were shipped by M10/M11 migrations already applied. This
change only projects an existing column into a DTO and adds a read endpoint — pure API surface. Asserted
explicitly to prevent an implementer from authoring a spurious migration.

### Decision
Exposing the caller's own `role` (and existing `tier`) to the caller does not breach the privacy
boundary.

### Reason
`The-ideal.md` §4 rule 1 scopes privacy to a user seeing only **their own** data; M11 §4.1 scopes the
admin boundary to admins not seeing **other** users' ledger data. Self-role is the caller's own account
metadata — inside "their own data", outside both boundaries. (Flagged as an Assumption for user
confirmation at the checkpoint; if the user objects, OQ1 reopens.)

### Decision (checkpoint 2026-07-16)
All 5 Open Questions resolved at the **Recommended** option: OQ1(a) add `Role` to `UserResponse`
(auto-maps via the existing `CreateMap<User, UserResponse>()`, `Tier` unchanged); OQ2(a) add a guarded
`GET api/v1/auth/me` returning `ApiResult<UserResponse>`, login/refresh `TokenPairResponse` contracts
untouched; OQ3(a) build the profile from a live `UserRepository.GetByUuidAsync(AuthenticatedUser.Id)`
read; OQ4(a) route on the existing `AuthController`; OQ5(a) do NOT expose `status`.

### Reason
Lowest-risk, additive surface that closes the frontend `AdminRoute` seam without breaking the token
contracts the SPA already built against; a live DB read is required regardless because `CreatedAt` is
not carried on the principal/token.

## Progress Log

### 2026-07-16

- Feature-planner: required reading completed — `The-ideal.md` §3.1 + §4 (rule 1, line 140),
  `CLAUDE.md`, `.claude/rules/rule.md`, `planning/user-authentication.md` (token/tier/role principal
  hops, freshness facts), `planning/admin-management.md` (M11 role model, §4.1 admin boundary,
  `RefreshCachedStateAsync`), and the live code: `Controllers/AuthController.cs`,
  `Controllers/AppController.cs` (LOCKED), `Models/Auth/UserResponse.cs` + `TokenPairResponse.cs`,
  `Mappings/AuthProfile.cs`, `Auth/AuthenticatedUser.cs`, `Services/Api/Auth/AuthService.cs`,
  `Repositories/UserRepository.cs` (`GetByUuidAsync`), `Constants/MessageKeys.cs` + `ErrorCodes.cs`;
  plus the trigger `FairShareMonWeb/planning/frontend-foundation.md` + `FairShareMonWeb/CLAUDE.md`
  ("Auth-guard seam (flagged)").
- Confirmed: `UserResponse` already carries `tier`; only `role` is missing. `User` entity already has
  `Role` (+ `Status`); `AuthProfile`'s `CreateMap<User, UserResponse>()` auto-maps a new `Role`
  member. `AuthenticatedUser` carries `Role` (fail-safe `USER`) but not `CreatedAt`. Anonymous is
  already 401 `1002` via the FallbackPolicy. No migration needed.
- Drafted this plan: add `role` to `UserResponse`; add `GET api/v1/auth/me` (guarded) →
  `ApiResult<UserResponse>` built from a live `GetByUuidAsync` read.
- **5 Open Questions raised** (add `role` to `UserResponse`; how the client gets the current user —
  `/auth/me` vs login/refresh payload vs both; profile data source — live DB read vs principal;
  route name/placement; whether to also expose `status`), each with options, trade-offs, and a
  recommendation. Awaiting user answers at the checkpoint before implementation starts.
- **Checkpoint answered — all 5 OQs at Recommended (a).** Implemented:
  - `Models/Auth/UserResponse.cs` — added `public string Role { get; set; } = string.Empty;` (after
    `Tier`) with a Vietnamese XML doc. `Tier` unchanged.
  - `Mappings/AuthProfile.cs` — **no change**; confirmed `User.Role` → `UserResponse.Role` auto-maps by
    member name (build + full test suite, incl. AutoMapper config-assertion tests, pass).
  - `Services/Api/Auth/AuthService.cs` — added `GetCurrentUserAsync(string userUuid, CancellationToken)`
    to `IAuthService` + impl: live `GetByUuidAsync` read → `mapper.Map<UserResponse>`; a vanished user
    row throws `ErrorException(ErrorCodes.Unauthorized, MessageKeys.Error.Unauthorized)` (mirrors
    `ChangePasswordAsync`).
  - `Controllers/AuthController.cs` — added guarded `GET api/v1/auth/me` (no `[AllowAnonymous]`) →
    `ApiResult<UserResponse>.Success(...)`, Vietnamese Swagger annotations, reads `AuthenticatedUser.Id`.
  - No EF migration (role/tier columns already exist from M10/M11); no message-key/resx additions;
    `AppController` untouched.
  - `dotnet build` clean; `dotnet test` green — 1116 passed, 0 failed, 0 skipped (MariaDB reachable).
  - Feature tests deferred to the test-engineer per the plan.
- **Test-engineer: feature test coverage added (Step 5 complete).** Two new files, all tests green
  against the real MariaDB (reachable):
  - `FairShareMonApi.Tests/UserResponseMappingTests.cs` (pure unit, no DB): AutoMapper
    `AssertConfigurationIsValid` still passes with the new `Role` member; `User -> UserResponse` maps
    `Role` (theory: USER + ADMIN) alongside `Uuid`/`Username`/`Tier`/`CreatedAt`; `UserResponse`
    serializes `role`/`tier`/`username`/`uuid`/`createdAt` as camelCase keys with no `password`/
    `passwordHash` key.
  - `FairShareMonApi.Tests/AuthMeEndpointTests.cs` (integration, `WebApplicationFactory<Program>`,
    real MariaDB/Redis, skippable, extends the M11 `AdminEndpointTestBase` for register/login/role-flip
    helpers): `GET /auth/me` valid token -> 200 own profile with `role: USER` (uuid/username/tier/role/
    createdAt correct, no secret field); valid ADMIN token -> `role: ADMIN`; anonymous -> 401 `1002`;
    revoked (post-logout) token -> 401 `1002`; **live-read freshness** - a direct DB tier+role change
    is reflected on the SAME unrefreshed/un-busted token (proves OQ3a's live `GetByUuidAsync` source
    without depending on Redis cache-bust); and the additive-field guard that the `register` response
    now carries `role: USER`.
  - Extra coverage beyond the doc's list: an explicit "before change" assertion inside the freshness
    test (token starts FREE/USER) so the live-read delta is unambiguous; USER+ADMIN parametrization of
    the mapping test.
  - Deviation note: the freshness test uses a direct DB change rather than the M11 grant + cache-bust
    path. Because `/auth/me` reads the DB live on every call, this is a stronger and Redis-independent
    proof of the live-read source; the M11 grant + cache-bust behaviour itself is already covered by
    `AdminEndpointTests`.
  - Full suite: **1126 passed, 0 failed, 0 skipped** (`dotnet test .\FairShareMonApi.sln`, MariaDB
    reachable). No production bug surfaced.

## Final Outcome

**Complete.** `Role` added to `UserResponse` (additive, auto-mapped from `User.Role` — no `AuthProfile`
change) and a new guarded `GET api/v1/auth/me` → `ApiResult<UserResponse>` (`{ uuid, username, tier,
role, createdAt }`) built from a live `GetByUuidAsync` read via `AuthService.GetCurrentUserAsync`.
Login/refresh token contracts untouched; anonymous → the wired 401/`1002`; a missing user row → the
same 401/`1002` (mirrors `ChangePasswordAsync`). **No EF migration** (role/tier columns pre-exist from
M10/M11), no new error code, no new message key, AutoMapper still pinned `13.0.1`. Tests: +10 (4 unit
mapping/serialization + 6 real-MariaDB endpoint), full suite **1126/1126** (0 failed, 0 skipped). Code
review **APPROVE, 0 blocking, 0 nits**. This unblocks the frontend `AdminRoute` role source and the
boot-rehydrate current-user display flagged in `FairShareMonWeb/planning/frontend-foundation.md` +
`FairShareMonWeb/CLAUDE.md` — the frontend can now call `/auth/me` after login and after a boot refresh
to populate `role`/identity.

## Future Improvements

- A broader `UsersController` profile surface (e.g. update display preferences) if user-profile
  features grow beyond identity — would make `GET /users/me` the natural home and could absorb
  `/auth/me`.
- Embed the user payload in login/refresh later (OQ2 b/c) if a round-trip on login/boot ever proves
  costly — additive and can be layered on top of `/auth/me`.
- Expose `status` on the DTO if a self-service "your account is limited" surface is ever built (OQ5 b).
- Frontend follow-up (SPA-owned): remove the "Auth-guard seam (flagged)" caveat from
  `FairShareMonWeb/CLAUDE.md` and wire `AdminRoute` to the now-available `role`, plus repopulate the
  account label from `/auth/me` on boot rehydrate.
