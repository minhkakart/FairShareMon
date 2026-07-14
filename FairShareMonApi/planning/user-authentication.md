# User Authentication (Milestone 2: Auth)

Register / login / refresh / logout / change-password with opaque stateful tokens — the first business feature: `users` + `auth_tokens` entities, the project's **first EF migration**, real `TokenService` / `TokenValidator` / `TokenWhitelistStore` (Redis cache-first, DB fallback), BCrypt password hashing, deletion of the infrastructure auth stubs, and closure of the handoffs left open by `project-initialization.md` (Swagger padlock, `HandleForbidAsync`, first end-to-end coverage of `ValidationException` → 400 and plain-DTO auto-wrapping).

## Objective

Implement `The-ideal.md` §3.1 (Tài khoản & phiên đăng nhập) on top of the approved infrastructure skeleton:

- Đăng ký tài khoản bằng username + mật khẩu (Free tier mặc định — §3.11).
- Đăng nhập / đăng xuất; phiên có thời hạn và **gia hạn được không cần nhập lại mật khẩu** (access + refresh token pair).
- Đổi mật khẩu → **mọi thiết bị đang đăng nhập bị buộc đăng nhập lại** (revoke ALL tokens of the user).
- Token mechanics per CLAUDE.md / rules.md Auth section: opaque stateful tokens, SHA-256 hash whitelisted in `auth_tokens` + Redis (TTL = `expires_at`), raw token returned exactly once, validation cache-first with DB fallback.

## Background

- Milestone 1 (`project-initialization.md`) delivered the full skeleton: `ApiResult` envelope (confirmed `error.fields` contract), `ErrorCodes` 1xxx infrastructure block (feature areas claim their own blocks), locked `AppController` (exposes `AuthenticatedUser` via `IContextAuthenticated`), `BaseRepository` + `ExecuteTransactionAsync`/`ExecuteQueryAsync`, empty `AppDbContext` (no migrations yet — the first migration ships here), `Uuid.NewV7()`, Redis multiplexer wired but unused, and the real-MariaDB test harness.
- Auth exists **at the abstract level**: contracts `ITokenService` (+ `TokenPair`), `ITokenValidator`, `ITokenWhitelistStore` (+ `TokenWhitelistEntry`) in `FairShareMonApi/Auth/Abstractions/`, and `OpaqueTokenAuthenticationHandler` as the default scheme (Bearer extraction → `ITokenValidator`; `HandleChallengeAsync` already writes a wrapped 401). A `FallbackPolicy` requires authentication on everything not `[AllowAnonymous]`.
- Placeholders that this feature **must remove**:
  - `Auth/StubTokenService.cs` and `Auth/StubTokenValidator.cs` — **DELETE the files**, don't just add real implementations. DiDecoration registers with `TryAdd`; a leftover stub registration silently wins over the real one.
  - `Controllers/AuthProbeController.cs` — the temporary Swagger-hidden `[Authorize]` probe; remove once this feature's real guarded endpoints exist (its `HealthEndpointTests` references move to a real guarded endpoint — coordinate with the test-engineer).
- Explicit handoffs from Milestone 1 to this feature: Swagger padlock operation filter (padlock only on guarded endpoints), the `HandleForbidAsync` envelope decision, and first end-to-end coverage of `ValidationException` → 400 with `error.fields` and of `[ResponseWrapped]` plain-DTO auto-wrapping.
- Current contract signatures the implementations must satisfy (or minimally extend — see Decision Log):
  - `ITokenService`: `Task<TokenPair?> IssueAsync(string userId, CancellationToken)`, `Task<TokenPair?> RefreshAsync(string refreshToken, CancellationToken)`, `Task<bool> RevokeAsync(string rawToken, CancellationToken)`, `Task<int> RevokeAllAsync(string userId, CancellationToken)`.
  - `ITokenValidator`: `Task<AuthenticatedUser?> ValidateAsync(string rawToken, CancellationToken)`.
  - `ITokenWhitelistStore`: `AddAsync(string tokenHash, TokenWhitelistEntry entry, CancellationToken)`, `LookupAsync(string tokenHash, CancellationToken)`, `RemoveAsync(string tokenHash, CancellationToken)`; `TokenWhitelistEntry(string UserId, DateTime ExpiresAt)`.
  - `TokenPair(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt)`.
  - `AuthenticatedUser { Id (user **UUID** string), Username }`.

## Requirements

From `The-ideal.md` §3.1 / §3.11 / §4 and CLAUDE.md / rules.md conventions:

- **Register:** username + password → create `users` row, tier **Free** by default (§3.11). Username unique per system. Duplicate → clear Vietnamese error. (Ledger bootstrap — owner-representative member + suggested categories — is deferred to Milestones 3/4 per the approved roadmap; see Decision Log.)
- **Login:** verify BCrypt hash; issue an access + refresh opaque token pair; store **SHA-256 hashes** in `auth_tokens` and Redis (TTL = `expires_at`); return the **raw pair exactly once**. Raw tokens are never persisted anywhere.
- **Per-request validation:** hash the incoming Bearer token, look it up in the whitelist — **Redis first, DB fallback** — check expiry and that it is an **access** token; materialize `AuthenticatedUser` without a mandatory per-request DB hit (cache carries the username).
- **Refresh:** exchange a valid refresh token for a new pair without the password — **full pair rotation** (old refresh + paired access revoked immediately) with **reuse detection**: a revoked refresh token presented again revokes ALL of that user's sessions (OQ4 answer).
- **Logout:** revoke the current session (remove from whitelist — DB + Redis).
- **Change password:** verify the current password, store the new BCrypt hash, and **revoke ALL of the user's tokens** (spec: every logged-in device is forced to re-login).
- **Money-free feature, but the general rules still apply:** resource-owned scoping is trivially satisfied (users only touch their own account via their own token); Vietnamese for every user-facing message and Swagger summary; manual FluentValidation (services inject `IValidator<T>`, failures throw `ValidationException` → 400 with `error.fields`); writes via `ExecuteTransactionAsync` with `TransactionContext.NoCommit()` on business failure; `Async` suffix + `CancellationToken` threaded through.
- **Entities per rules.md conventions:** partial-class POCOs (`Database/Entities/<Name>.cs` + `Database/Entities/Partials/<Name>.cs` with ctor + static `ConfigureModel(ModelBuilder)` invoked from `AppDbContext.OnModelCreating`), `ulong Id` PK, non-nullable `string Uuid` (= `Uuid.NewV7()`, max 64, unique index), `CreatedAt`/`UpdatedAt` (`UpdatedAt` = `ValueGeneratedOnAddOrUpdate()` with `current_timestamp()` default).
- **Schema via EF migration only** — first migration of the project, authored offline through `AppDbContextDesignTimeFactory`, reviewed before `database update`.
- **Cleanup obligations:** delete both stubs, remove `AuthProbeController`, add the Swagger padlock operation filter, decide + implement `HandleForbidAsync`.
- **Error codes:** claim a dedicated block for Auth (2xxx — see Decision Log); never renumber existing codes.

## Open Questions

> **All 10 answered by the user at the 2026-07-13 checkpoint — every recommended option (a) was accepted.** The full options/trade-offs as originally presented are preserved in git history; the struck questions below carry the binding answers, mirrored in the Decision Log.

1. ~~**Token lifetimes**~~ → **Answered 2026-07-13 (option a):** access **30 minutes**, refresh **30 days**, both configurable via `appsettings.json` (`Auth:AccessTokenLifetime` / `Auth:RefreshTokenLifetime`).

2. ~~**Username policy**~~ → **Answered 2026-07-13 (option a):** 3–32 chars, `a-z 0-9 _ . -` only, stored lowercase, case-insensitive uniqueness.

3. ~~**Password policy**~~ → **Answered 2026-07-13 (option a):** min 8 chars, max **72 bytes** (BCrypt truncation limit — validated), no composition rules. Change-password requires the current password and does **not** reject reusing the same password.

4. ~~**Refresh-token rotation semantics**~~ → **Answered 2026-07-13 (option a):** **full pair rotation** — refresh issues a new pair and immediately revokes the old refresh **and** the old paired access token via `pair_uuid`. **Reuse detection: yes** — presenting a *revoked* refresh token revokes ALL of that user's sessions (in addition to the 401). This answer partially supersedes the earlier hard-delete revocation decision — see the amended Decision Log entry (revocation model).

5. ~~**Register response**~~ → **Answered 2026-07-13 (option a):** register returns the created account info only (`ApiResult<UserResponse>`); no auto-login — the client calls login next.

6. ~~**Concurrent sessions (multi-device)**~~ → **Answered 2026-07-13 (option a):** unlimited concurrent sessions; `RevokeAllAsync` on password change is the kill switch.

7. ~~**Redis outage behavior**~~ → **Answered 2026-07-13 (option a):** DB is the source of truth, Redis is a best-effort cache — lookups fall back to `auth_tokens`, cache writes/deletes warn-and-continue on failure, cache self-heals via lookup backfill. The bounded stale-revocation window (max one access-token TTL when a revocation's Redis delete failed) is **explicitly accepted**.

8. ~~**BCrypt work factor**~~ → **Answered 2026-07-13 (option a):** work factor **11**, configurable via `Auth:BcryptWorkFactor`.

9. ~~**Login brute-force protection**~~ → **Answered 2026-07-13 (option a):** deferred entirely to Future Improvements — nothing ships this milestone.

10. ~~**Timestamp convention (`AppDateTime`)**~~ → **Answered 2026-07-13 (option a):** `AppDateTime.Now` = **UTC** (`DateTime.UtcNow`); DB stores UTC; presentation converts to UTC+7. Binds all future entities and every token-expiry/TTL computation.

## Assumptions

> The previously vetoable assumptions were **confirmed by the user at the 2026-07-13 checkpoint** — they are now decisions, not assumptions.

- `register`, `login`, `refresh` are `[AllowAnonymous]`; `logout` and `change-password` require authentication (they act on the caller's own session/account). No other interpretation seems plausible.
- **Confirmed 2026-07-13:** change-password requires the **current password** in the request (and reusing the same password is allowed — OQ3 answer).
- **Confirmed 2026-07-13:** accounts are username + password only — no email, phone, verification, or password recovery this milestone. Recovery stays under Future Improvements.
- **Confirmed 2026-07-13:** users are **not** soft-deletable (no account-deletion feature in the spec) — `users` does not implement `IEntityDeletable`.
- The raw token format (cryptographically random bytes, Base64Url-encoded) is an internal detail, not a documented API contract — clients must treat tokens as opaque strings.
- **Confirmed 2026-07-13:** users registered before Milestones 3/4 ship will have their owner-representative member and suggested categories **backfilled by those milestones** (each owns a data backfill for pre-existing users) — see Decision Log.
- Redis-dependent integration tests may skip when Redis is unreachable, mirroring the MariaDB skippable-fixture pattern (test-engineer owns the mechanics).
- `dotnet ef database update` for the first migration runs only after code review, per the orchestration protocol; MariaDB must be reachable for that step (authoring is offline).

## Implementation Plan

> File paths are relative to `FairShareMonApi/FairShareMonApi/`. All new services use DiDecoration attributes (`[ScopedService]` unless noted). Interface + implementation live in the same file (rules.md). All user-facing strings in Vietnamese.

### Step 0 — Unblock: stub deletion + package

1. **Delete** `Auth/StubTokenService.cs` and `Auth/StubTokenValidator.cs` (files, not just registrations — DiDecoration `TryAdd` hazard). The build breaks until Step 5 lands; Steps 0–5 are one implementer unit.
2. Add package **`BCrypt.Net-Core`** (latest, 1.6.0) to `FairShareMonApi.csproj` (package name per CLAUDE.md / init-plan handoff).

### Step 1 — Entities

1. `Database/Entities/User.cs` (POCO) + `Database/Entities/Partials/User.cs` (ctor + `ConfigureModel`):
   - `ulong Id`, `string Uuid` (unique, max 64), `string Username` (max 32 per OQ2 — stored lowercase, **unique index**), `string PasswordHash` (max 100 — BCrypt output is 60 chars), `string Tier` (max 16, default `"FREE"`; values `FREE`/`PREMIUM` — constants in `Constants/UserTiers.cs`), `DateTime CreatedAt`, `DateTime UpdatedAt`.
   - Table `users`. Ctor sets `Uuid = Uuid.NewV7()`, `CreatedAt = AppDateTime.Now`, `Tier = UserTiers.Free`.
2. `Database/Entities/AuthToken.cs` + `Database/Entities/Partials/AuthToken.cs`:
   - `ulong Id`, `string Uuid`, `ulong UserId` (FK → `users.id`, cascade delete), `string TokenHash` (SHA-256 hex, fixed 64 chars, **unique index**), `string TokenType` (max 16; `ACCESS`/`REFRESH` — constants in `Constants/TokenTypes.cs`), `string PairUuid` (max 64, indexed — shared by the two rows of one issuance; enables pair/rotation revocation), `DateTime ExpiresAt`, **`DateTime? RevokedAt`** (null = active; set by rotation/logout so a reused revoked refresh token stays attributable for the OQ4 reuse-detection cascade — see the amended revocation-model decision), `DateTime CreatedAt`, `DateTime UpdatedAt`; navigation `User User`.
   - Table `auth_tokens`. Indexes: `token_hash` unique, `user_id`, `pair_uuid`.
3. `Utils/AppDateTime.cs` — `Now` = **`DateTime.UtcNow`** (OQ10 answer: UTC in DB, presentation converts to UTC+7; helper referenced by rules.md but not yet created — first entities need it).
4. `Database/AppDbContext.cs`: add `DbSet<User> Users`, `DbSet<AuthToken> AuthTokens`; invoke `User.ConfigureModel(modelBuilder)` and `AuthToken.ConfigureModel(modelBuilder)`. No query filters needed (`AppDbContext.partial.cs` untouched).

### Step 2 — First EF migration

- `dotnet ef migrations add AddUsersAndAuthTokens --project .\FairShareMonApi\FairShareMonApi.csproj` (offline via the pinned design-time factory). **Migration name: `AddUsersAndAuthTokens`.**
- Review the generated migration (utf8mb4, unique indexes, FK cascade, `UpdatedAt` default `current_timestamp()`); keep the model snapshot in sync. `database update` only after review, per protocol.

### Step 3 — Error codes + Vietnamese messages

Append to `Constants/ErrorCodes.cs` (never renumber):

| Code | Name | HTTP | Message (Vietnamese) |
|---|---|---|---|
| `1004` | `Forbidden` | 403 | "Bạn không có quyền thực hiện thao tác này." |
| `2000` | `UsernameTaken` | 400 | "Tên đăng nhập đã tồn tại." |
| `2001` | `InvalidCredentials` | 401 | "Tên đăng nhập hoặc mật khẩu không đúng." |
| `2002` | `InvalidRefreshToken` | 401 | "Mã gia hạn phiên không hợp lệ hoặc đã hết hạn." |
| `2003` | `CurrentPasswordIncorrect` | 400 | "Mật khẩu hiện tại không đúng." |

- The **2xxx block is claimed for Auth** (see Decision Log). Extend `ErrorException.GetDefaultHttpStatus` with the new codes (`1004`→403, `2000`→400, `2001`→401, `2002`→401, `2003`→400).
- Success messages: đăng ký "Đăng ký tài khoản thành công.", đăng xuất "Đăng xuất thành công.", đổi mật khẩu "Đổi mật khẩu thành công. Vui lòng đăng nhập lại trên mọi thiết bị."

### Step 4 — Repositories

1. `Repositories/UserRepository.cs` — `IUserRepository` + impl (`[ScopedService]`, extends `BaseRepository`): `GetByUsernameAsync`, `GetByUuidAsync`, `ExistsByUsernameAsync`, `CreateAsync` (via `ExecuteTransactionAsync`), `UpdatePasswordAsync`.
2. `Repositories/AuthTokenRepository.cs` — `IAuthTokenRepository` + impl: `AddPairAsync` (two rows, one transaction), `GetByHashWithUserAsync` (join → uuid + username + expiry + type + pair + `RevokedAt` — returns revoked rows too, so the service layer can distinguish "revoked" from "unknown" for reuse detection), `RevokeByPairUuidAsync` (sets `RevokedAt` — rotation/logout), `DeleteAllByUserIdAsync` (hard delete, returns count — password-change kill switch and reuse-detection cascade), opportunistic `DeleteExpiredAsync` (purges rows past `ExpiresAt`, revoked or not).

### Step 5 — Real token infrastructure (replaces the stubs)

1. **Minimal contract extensions** (auth-owned, see Decision Log): `TokenWhitelistEntry` gains `Username`, `TokenType`, `PairUuid`; `ITokenService.IssueAsync` gains the username (signature `IssueAsync(string userId, string username, ...)`) so cache entries can materialize `AuthenticatedUser` without a per-request DB hit. `TokenPair` unchanged. Update XML docs accordingly.
2. `Auth/PasswordHasher.cs` — `IPasswordHasher` + BCrypt impl (`Hash`, `Verify`; work factor from `Auth:BcryptWorkFactor`, default **11** — OQ8 answer).
3. `Auth/TokenHasher.cs` (static) — SHA-256 → lowercase hex; used by service + validator.
4. `Auth/TokenWhitelistStore.cs` — `[ScopedService(typeof(ITokenWhitelistStore))]`, the Redis + DB composite: `AddAsync` = DB rows (via `IAuthTokenRepository`) + Redis `StringSet` (key `auth:token:{hash}`, JSON value, TTL = `ExpiresAt − now`); `LookupAsync` = Redis first → DB fallback excluding revoked rows (backfill Redis with remaining TTL on hit); `RemoveAsync` = DB revoke + Redis `KeyDelete`. Redis failure handling per the OQ7 answer: **DB is the source of truth; every Redis operation is best-effort warn-and-continue** (the bounded stale-revocation window, max one access TTL, is user-accepted).
5. `Auth/TokenService.cs` — `[ScopedService(typeof(ITokenService))]`, implements `ITokenService`: `IssueAsync` generates two 32-byte `RandomNumberGenerator` tokens → Base64Url raw strings, one shared `PairUuid = Uuid.NewV7()`, whitelists both hashes with configured lifetimes (`Auth:AccessTokenLifetime` = 30 min, `Auth:RefreshTokenLifetime` = 30 days — OQ1 answer), returns the raw `TokenPair` once. `RefreshAsync` (OQ4 answer — full pair rotation + reuse detection): look up the hash **including revoked rows**; (i) unknown/expired/wrong-type → null (→ 2002); (ii) found with `RevokedAt` set → **theft signal: `RevokeAllAsync` for that user**, then null (→ 2002); (iii) valid refresh → revoke the old pair via `PairUuid` (+ Redis deletes), issue a new pair. `RevokeAsync` revokes the presented token's **pair** (logout semantics — see Decision Log). `RevokeAllAsync` hard-deletes all of the user's rows + their Redis keys.
6. `Auth/TokenValidator.cs` — `[ScopedService(typeof(ITokenValidator))]`, implements `ITokenValidator`: hash → `ITokenWhitelistStore.LookupAsync` → require type `ACCESS` and `ExpiresAt > AppDateTime.Now` → `AuthenticatedUser { Id = entry.UserId (uuid), Username = entry.Username }`; null otherwise. No DB access of its own beyond the store's fallback.

### Step 6 — DTOs + validators (manual FluentValidation)

1. `Models/Auth/` requests: `RegisterRequest { Username, Password }`, `LoginRequest { Username, Password }`, `RefreshRequest { RefreshToken }`, `ChangePasswordRequest { CurrentPassword, NewPassword }`; responses: `UserResponse { Uuid, Username, Tier, CreatedAt }`, `TokenPairResponse { AccessToken, AccessTokenExpiresAt, RefreshToken, RefreshTokenExpiresAt }` (mapped from `TokenPair` via AutoMapper profile `Mappings/AuthProfile.cs`).
2. `Validators/Auth/RegisterRequestValidator.cs`, `LoginRequestValidator.cs`, `RefreshRequestValidator.cs`, `ChangePasswordRequestValidator.cs` — concrete rules per the OQ2/OQ3 answers: username required, 3–32 chars, regex `^[a-z0-9_.-]+$` after lowercasing; password required, min 8 chars, max 72 **bytes** (UTF-8 byte count, BCrypt truncation limit); new password may equal the current one (no reuse rejection); all messages Vietnamese (e.g. "Tên đăng nhập không được để trống.", "Mật khẩu phải có ít nhất N ký tự."). Registered automatically by the existing `AddValidatorsFromAssembly`; **services** inject `IValidator<T>` and throw `ValidationException` on failure (→ 400 with `error.fields` via `ErrorHandlerFilter`).

### Step 7 — AuthService + AuthController

1. `Services/Api/Auth/AuthService.cs` — `IAuthService` + impl in one file (`[ScopedService]`), primary constructor injecting repos, `IPasswordHasher`, `ITokenService`, validators:
   - `RegisterAsync`: validate → lowercase the username → uniqueness check (+ unique-index race safety inside `ExecuteTransactionAsync`, `NoCommit()` on duplicate) → create `User` (tier FREE) → `UserResponse` only, no auto-login (OQ5 answer).
   - `LoginAsync`: validate → lowercase the username → fetch by username → BCrypt verify (verify against a fixed dummy hash when the user is missing, equalizing timing) → on failure `ErrorException(2001)` → `ITokenService.IssueAsync` → `TokenPairResponse`.
   - `RefreshAsync`: validate → `ITokenService.RefreshAsync` → null ⇒ `ErrorException(2002)`.
   - `LogoutAsync(rawAccessToken)`: `ITokenService.RevokeAsync` (pair revocation); success message even if already revoked (idempotent).
   - `ChangePasswordAsync`: validate → verify current password (`ErrorException(2003)` on mismatch) → update hash in one transaction → **after commit** `ITokenService.RevokeAllAsync(userUuid)` (post-commit side-effect per rules.md).
2. `Controllers/AuthController.cs` (derives from `AppController` — which stays **locked and untouched**):

   | Verb + Route | Auth | Request → Response | Notes |
   |---|---|---|---|
   | `POST api/v1/auth/register` | anonymous | `RegisterRequest` → `ApiResult<UserResponse>` (no auto-login — OQ5) | HTTP 200 envelope |
   | `POST api/v1/auth/login` | anonymous | `LoginRequest` → `ApiResult<TokenPairResponse>` | raw pair returned once |
   | `POST api/v1/auth/refresh` | anonymous | `RefreshRequest` → **plain `TokenPairResponse`** | returned unwrapped on purpose — first real exercise of `[ResponseWrapped]` auto-wrapping (handoff) |
   | `POST api/v1/auth/logout` | **guarded** | (no body) → `ApiResult` success message | revokes the presenting token's pair |
   | `POST api/v1/auth/change-password` | **guarded** | `ChangePasswordRequest` → `ApiResult` success message | revokes ALL user tokens |

   All actions carry Vietnamese `[SwaggerOperation]`/`[SwaggerResponse]` annotations. Logout reads the raw Bearer token from the `Authorization` header (the service needs the raw token to hash).

### Step 8 — Pipeline handoffs

1. `Swagger/AuthorizeOperationFilter.cs` — `IOperationFilter`: padlock (Bearer security requirement) **only** on operations not marked `[AllowAnonymous]` (everything else is guarded by the FallbackPolicy). In `Program.cs`: remove the global `AddSecurityRequirement`, add `options.OperationFilter<AuthorizeOperationFilter>()`. (`Program.cs` is editable; only `AppController.cs` is locked.)
2. `Auth/OpaqueTokenAuthenticationHandler.cs` — add `HandleForbidAsync` override writing `ApiResult.Failure(ErrorCodes.Forbidden, "Bạn không có quyền thực hiện thao tác này.")` (403 in the envelope), symmetric with the existing challenge override. Note: business-level ownership misses still return **404, never 403** — this override only covers genuine policy failures (none exist yet; future Premium gates will).
3. **Delete `Controllers/AuthProbeController.cs`** — logout/change-password are now real guarded endpoints. The test-engineer updates the `HealthEndpointTests` cases that referenced `authprobe` (401-envelope + Swagger-hidden assertions) to target a real guarded auth endpoint.

### Step 9 — Tests (owned by the test-engineer; the definitive list)

**Unit (no DB):**
- `PasswordHasherTests` — hash/verify roundtrip, wrong password fails, two hashes of the same password differ (salt), configured work factor honored, 72-byte-boundary input.
- `TokenHasherTests` — known SHA-256 vector, lowercase-hex 64-char output.
- `TokenValidatorTests` (fake `ITokenWhitelistStore`) — valid access token → `AuthenticatedUser` (uuid + username); unknown hash → null; expired → null; **refresh token presented as access → null**; store entry with wrong type/expiry combinations.
- Validator tests for all four request validators — every rule, Vietnamese message text, field names as they appear in `error.fields`.
- `TokenServiceTests` (fake store/repo) — issue returns distinct raw tokens with correct expiries (30 min / 30 days from config) and one shared pair id; refresh performs **full pair rotation** (old refresh AND old access revoked, new pair valid); **reuse detection: a revoked refresh token presented again triggers revoke-all for the user** and returns null; revoke removes the pair; revoke-all count.

**Integration (real MariaDB, rollback harness — skippable):**
- `UserRepositoryTests` — create (uuid/tier/timestamps set, UTC), unique-username violation surfaces, lowercase-stored username retrievable case-insensitively (OQ2 behavior).
- `AuthTokenRepository` / whitelist DB-fallback tests — `AddPairAsync` writes both rows; `GetByHashWithUserAsync` joins username and returns `RevokedAt`; expired rows excluded from whitelist lookups; `RevokeByPairUuidAsync` marks both rows revoked (rows remain, attributable); `DeleteAllByUserIdAsync` hard-deletes and returns count; `DeleteExpiredAsync` purges past-expiry rows.
- `TokenWhitelistStoreTests` — DB fallback works with Redis unavailable/stubbed (OQ7 warn-and-continue behavior, no exception surfaces); revoked rows never returned by `LookupAsync`; Redis backfill on fallback hit when Redis available (skippable when Redis unreachable).
- `AuthServiceTests` — register persists BCrypt hash (never plaintext, tier FREE, lowercase username); duplicate username → code 2000, nothing persisted (`NoCommit`); login wrong password/unknown user → 2001; refresh with unknown/expired/access-typed token → 2002; **refresh with a revoked refresh token → 2002 AND all of the user's sessions revoked (reuse-detection cascade)**; change-password with same password as current succeeds (allowed per OQ3); change-password wrong current → 2003 and tokens untouched; change-password success → hash updated and **zero** remaining `auth_tokens` rows for the user.

**Endpoint (WebApplicationFactory) — closes the Milestone-1 handoff gaps:**
- `POST /auth/register` with invalid payload → **400 envelope with `error.fields`** (first e2e `ValidationException` coverage), camelCase field keys, Vietnamese messages.
- `POST /auth/refresh` success → response is the **wrapped envelope** although the action returns a plain DTO (first e2e `[ResponseWrapped]` auto-wrap coverage).
- Full happy flow: register → login → call guarded `logout` with Bearer → success envelope; the same token afterwards → **401 wrapped** (replaces the AuthProbe regression tests).
- login with wrong credentials → 401 envelope code 2001; register duplicate → 400 envelope code 2000.
- change-password flow: login on two "devices" → change password → both tokens 401.
- Swagger document: padlock **present** on logout/change-password, **absent** on register/login/refresh/health; `authprobe` no longer in the document.
- Note: endpoint tests hit real MariaDB/Redis through the app — mark DB-dependent ones skippable consistently with the existing harness.

### Step 10 — Wrap-up

- `dotnet build` clean; `dotnet test` green (DB/Redis tests skip only when servers unreachable); live boot smoke: register → login → guarded call → logout via Swagger.
- `dotnet ef database update` after review approval.
- Update this doc (Progress Log, Final Outcome); update `AGENTS.md`/CLAUDE.md only if reality diverged; rules.md's `AppDateTime.Now` reference now resolves to the built UTC helper (OQ10) — no rules.md text change needed unless the orchestrator wants "(UTC)" noted there.

## Impact Analysis

- **APIs:** five new endpoints under `api/v1/auth`; `AuthProbeController` removed; Swagger contract changes (padlock per-operation instead of global). No existing endpoint changes shape.
- **Database:** first migration `AddUsersAndAuthTokens` — tables `users`, `auth_tokens` (FK cascade, unique indexes on `username`, `uuid`, `token_hash`; nullable `revoked_at` for reuse detection). Timestamps stored as UTC (OQ10). No data migration (empty DB).
- **Infrastructure:** Redis becomes load-bearing for the first time (token whitelist cache; best-effort warn-and-continue on outage per OQ7); new package `BCrypt.Net-Core`; new config keys `Auth:AccessTokenLifetime` (30 min), `Auth:RefreshTokenLifetime` (30 days), `Auth:BcryptWorkFactor` (11) in `appsettings.json`.
- **Services:** stubs deleted and replaced by real `TokenService`/`TokenValidator`/`TokenWhitelistStore`; new `AuthService`, `UserRepository`, `AuthTokenRepository`, `PasswordHasher`; auth-owned contracts minimally extended (`TokenWhitelistEntry`, `IssueAsync` signature). `AppController`, `ApiResult`, middleware pipeline untouched except the two planned handoffs (Swagger filter registration in `Program.cs`, `HandleForbidAsync` in the auth handler).
- **UI:** none (API only).
- **Documentation:** this doc; ErrorCodes XML docs; possibly a one-line rules.md sync after OQ10.

## Decision Log

### Decision
Ledger bootstrap on registration (owner-representative member + suggested categories "Ăn uống, Đi lại, Khách sạn, Mua sắm, Khác" with one default — spec §2/§3.1) is **deferred**: Milestone 3 (Members) creates the owner-representative member on register, Milestone 4 (Categories) creates the suggested set. This milestone's register creates **only the `users` row**.

### Reason
The approved roadmap (`planning/agent-dev-team.md`) already assigns "owner-representative member auto-created on register" to Milestone 3 and the default-category invariant to Milestone 4; their entities/tables don't exist yet and pulling their schemas forward would front-load two milestones into this one. Deferral is safe pre-release: no real users exist, and each of those milestones **inherits an explicit backfill obligation** for any user registered before it ships (recorded in Assumptions; must appear in their planning docs).

### Alternatives Considered
- Create `members`/`categories` tables + bootstrap rows now — spec-complete registration immediately, but triples this milestone's schema surface and duplicates design work owned by M3/M4.
- A bootstrap hook interface that later features implement — indirection with no consumer yet.

### Decision
`users.tier` column ships **now** (string, default `FREE`, constants `FREE`/`PREMIUM`); tier *enforcement* stays in Milestone 10.

### Reason
Spec §3.11 fixes that every account has a tier and Free is the registration default — that is registration behavior, owned here. A one-column addition now avoids an extra migration and a backfill later; the column is inert until M10.

### Alternatives Considered
- Defer the column to M10 — cleaner milestone isolation, but contradicts "Free là mặc định khi đăng ký" being part of §3.1 registration and forces a later migration + default backfill.

### Decision
Minimal, auth-owned contract extensions: `TokenWhitelistEntry(UserId, ExpiresAt)` → add `Username`, `TokenType`, `PairUuid`; `ITokenService.IssueAsync(string userId, ...)` → `IssueAsync(string userId, string username, ...)`.

### Reason
`ITokenValidator` must return `AuthenticatedUser { Id, Username }` from a **cache-first** lookup; without the username in the whitelist entry every request needs a DB hit, defeating the cache. `TokenType` is required so refresh tokens can never authenticate as access tokens; `PairUuid` enables pair revocation (logout, rotation). The contracts were authored in Milestone 1 explicitly *for* this feature to complete — extending them here is ownership, not churn; all call sites are in this feature (stubs are deleted).

### Alternatives Considered
- Secondary cache `user:{uuid} → username` — second key to keep coherent, more failure modes.
- DB lookup of the user on every request — negates Redis; rejected.
- `IssueAsync(AuthenticatedUser user, ...)` — couples the token layer to the claims model; two strings are enough.

### Decision *(amended 2026-07-13 — superseded in part by the OQ4 reuse-detection answer)*
**Revocation model:** rotation and logout **soft-revoke** (`revoked_at` set, row retained until natural expiry); password-change `RevokeAllAsync` **hard-deletes** all of the user's rows; expired rows (revoked or not) are purged opportunistically, not by a scheduler. *(Original decision — hard-delete on every revocation — is preserved below for the record.)*

### Reason
The user's OQ4 answer mandates reuse detection: presenting a *revoked* refresh token must revoke ALL of the user's sessions. With pure hard-delete, a revoked hash is indistinguishable from a random invalid token — there is no user to cascade against. Retaining rotated/logged-out rows with `revoked_at` (until their natural expiry) keeps the revoked refresh token attributable. Hard-delete stays correct for `RevokeAllAsync`: after the kill switch every session is already dead, so a later reuse can only yield a plain 401 — no cascade is needed or possible. Whitelist lookups (`ITokenWhitelistStore.LookupAsync`) exclude revoked rows, so "in the whitelist" still means valid; only the refresh path reads revoked rows deliberately. A Redis tombstone was rejected because OQ7 makes Redis best-effort — theft detection must not evaporate on a cache blip; the DB is the source of truth.

### Alternatives Considered
- **Original decision:** hard-delete on every revocation ("presence means valid", no retention duty per spec §3.8, purge-free lookups) — incompatible with the user-mandated reuse-detection cascade; superseded.
- Redis tombstone `auth:revoked:{hash} → user` with TTL — vanishes exactly when Redis is degraded (OQ7 accepts degraded Redis); rejected.
- Soft-revoke everywhere including revoke-all — uniform but keeps rows that can never trigger a meaningful cascade; hard-delete is the truthful kill switch.

### Decision
Logout (`RevokeAsync`) revokes the **whole pair** — the presented access token *and* its paired refresh token, linked via `pair_uuid`.

### Reason
Spec: "đăng xuất" ends the session. Revoking only the access token would leave the refresh token alive, silently resurrectable — not a logout.

### Alternatives Considered
- Revoke only the presented token — violates user expectation; rejected.
- Client sends both tokens to logout — trusts the client to end its own session correctly; unnecessary given pair linkage.

### Decision
Error-code block **2000–2999 is claimed for Auth**; infrastructure additionally gains `Forbidden = 1004` (403), and `HandleForbidAsync` is overridden to emit the wrapped envelope.

### Reason
Milestone 1 reserved 1xxx for infrastructure and told feature areas to claim blocks in their planning docs — auth is the first, taking 2xxx. `Forbidden` is a cross-cutting pipeline status (belongs to 1xxx; appending is allowed, renumbering is not). Overriding `HandleForbidAsync` was an explicit Milestone-1 handoff: without it a policy failure would emit a bare 403 outside the envelope. Ownership misses still use 404 per the Resource-Owned rule; 1004 covers only genuine authorization-policy failures.

### Alternatives Considered
- Map 403 → 404 in the handler — would hide real policy failures (future Premium gates) behind a lie; the 404-not-403 rule is about *resource existence*, not about banning the status entirely.
- Leave `HandleForbidAsync` default — unwrapped empty 403 breaks the "every response is an `ApiResult`" contract.

### Decision
Raw token format: 32 bytes from `RandomNumberGenerator` → Base64Url (43 chars); whitelist key = lowercase-hex SHA-256.

### Reason
256 bits of entropy (unguessable), URL/header-safe, constant length; hex SHA-256 gives a fixed 64-char unique-indexable DB column. Matches the conventions' "random token, store its SHA-256 hash" exactly.

### Alternatives Considered
- Prefixed tokens (`fsm_...`) — nice for secret scanning, cosmetic otherwise; can be added later without contract impact.
- GUID-based tokens — only 122 bits and a recognizable shape; rejected.

### Decision
**User checkpoint 2026-07-13 — all 10 Open Questions resolved; every recommended option (a) accepted:**

1. **Token lifetimes:** access 30 minutes, refresh 30 days; configurable via `Auth:AccessTokenLifetime` / `Auth:RefreshTokenLifetime`. *Reason:* short revocation window with rare re-logins fits mobile ledger usage; config keys allow tuning without redeploy.
2. **Username policy:** 3–32 chars, `a-z 0-9 _ . -` only, stored lowercase, case-insensitive uniqueness. *Reason:* aligns with the DB's `utf8mb4_unicode_ci` collation — no "An" vs "an" surprises, no homoglyph ambiguity.
3. **Password policy:** min 8 chars, max 72 bytes (BCrypt truncation limit — validated), no composition rules; change-password requires the current password and does **not** reject reusing the same password. *Reason:* length beats complexity (NIST guidance); the 72-byte cap is a BCrypt correctness requirement, not a preference.
4. **Refresh rotation:** full pair rotation — new pair issued, old refresh AND paired access revoked immediately via `pair_uuid`. **Reuse detection:** presenting a revoked refresh token revokes ALL of that user's sessions (plus 401). *Reason:* cleanest session-state model; reuse of a rotated token is a theft indicator worth a hard response. *Consequence:* forced the amended revocation model above (`revoked_at` soft-revoke for rotation/logout).
5. **Register response:** created account info only (`ApiResult<UserResponse>`); no auto-login. *Reason:* one way to obtain tokens; keeps the "raw token returned once" story simple.
6. **Concurrent sessions:** unlimited; `RevokeAllAsync` on password change is the kill switch. *Reason:* matches the §3.1 multi-device UC; no eviction logic to invent.
7. **Redis outage:** DB is the source of truth, Redis best-effort cache — fallback lookups, warn-and-continue on cache write/delete failure, self-heal via backfill. The bounded stale-revocation window (max one access TTL) is **explicitly accepted**. *Reason:* a cache outage must not take auth down; the risk window is small and bounded.
8. **BCrypt work factor:** 11, configurable via `Auth:BcryptWorkFactor`. *Reason:* ~100–150 ms per hash balances cracking resistance against login latency.
9. **Brute-force protection:** deferred to Future Improvements — nothing ships this milestone. *Reason:* personal app, small attack surface; keeps the milestone lean.
10. **`AppDateTime.Now` = UTC** (`DateTime.UtcNow`); DB stores UTC; presentation converts to UTC+7. *Reason:* unambiguous expiry/TTL math, timezone-proof; binds all future entities.

Additionally confirmed (previously flagged assumptions): change-password requires the current password; no email/phone/recovery this milestone; users are not soft-deletable; Milestones 3/4 own the member/category bootstrap backfill for early-registered users.

### Reason
User answers at the Milestone-2 planning checkpoint (2026-07-13), brought by the orchestrator per the Clarification-First protocol; recorded verbatim so the implementer needs no other source.

### Alternatives Considered
The full option sets (b)/(c) with trade-offs, as presented to the user, are preserved in the struck Open Questions above and in git history.

## Progress Log

### 2026-07-13

- Feature-planner: required reading completed (`The-ideal.md` §2/§3.1/§3.11/§4, CLAUDE.md, rules.md, `project-initialization.md` final outcome + all 2026-07-13 entries, `agent-dev-team.md`, and the actual code: auth contracts/handler/stubs, `AppController`, `ErrorCodes`, `ErrorException`, `BaseRepository`, `DatabaseExtensions`, `AppDbContext`, `Program.cs`, `AuthProbeController`).
- Drafted this plan: `users` + `auth_tokens` entities, first migration `AddUsersAndAuthTokens`, five endpoints, real token stack (Redis cache-first + DB fallback), BCrypt, stub deletion, AuthProbe removal, Swagger padlock filter, `HandleForbidAsync`, 2xxx error block, full test list.
- **10 Open Questions raised** (token lifetimes, username policy, password policy, refresh rotation, register response, session limits, Redis outage, BCrypt cost, brute-force protection, `AppDateTime` timezone) — awaiting user answers at the checkpoint before implementation starts. Registration-bootstrap scoping, tier column, contract extensions, pair-revocation logout, hard-delete whitelist semantics, error-code block, and forbid handling recorded as decisions (roadmap/spec-derived, not preference guesses).

### 2026-07-13 (checkpoint — all Open Questions answered, plan unblocked)

- **User answered all 10 Open Questions; every recommended option (a) accepted** (see the consolidated Decision Log entry): access 30 min / refresh 30 days (configurable), username 3–32 `a-z0-9_.-` lowercase case-insensitive, password 8 chars–72 bytes with no composition rules (same-password reuse allowed on change), **full pair rotation + reuse-detection revoke-all**, register returns `UserResponse` only, unlimited concurrent sessions, Redis best-effort with DB as source of truth (stale-revocation window accepted), BCrypt work factor 11, brute-force protection deferred, `AppDateTime.Now` = UTC.
- Previously vetoable Assumptions confirmed by the user: current password required on change-password; no email/phone/recovery this milestone; users not soft-deletable; M3/M4 own the bootstrap backfill.
- **Plan amendment forced by the OQ4 reuse-detection answer:** pure hard-delete revocation cannot attribute a reused revoked refresh token, so the revocation model was amended — `auth_tokens` gains nullable `revoked_at`; rotation/logout soft-revoke (row kept until natural expiry), password-change revoke-all still hard-deletes. Entity (Step 1), repository methods (Step 4), `TokenService.RefreshAsync` flow (Step 5), migration content, Impact Analysis, and the test list updated accordingly; superseded decision preserved in the Decision Log.
- Doc synchronized end-to-end with the answers (concrete lifetimes/policies/work factor/UTC in Steps 1–10); Open Questions struck and marked answered. **No open questions remain — implementation can start.**

### 2026-07-13 (implementation — Steps 0–8 complete)

- **Step 0:** deleted `Auth/StubTokenService.cs` + `Auth/StubTokenValidator.cs` (files removed, per the DiDecoration TryAdd hazard); added package **`BCrypt.Net-Core` 1.6.0**.
- **Step 1:** created `Utils/AppDateTime.cs` (`Now` = `DateTime.UtcNow`), `Constants/UserTiers.cs`, `Constants/TokenTypes.cs`, entities `Database/Entities/User.cs` + `Partials/User.cs` and `Database/Entities/AuthToken.cs` + `Partials/AuthToken.cs` (partial-POCO pattern, `ulong Id`, ctor-set `Uuid`/`CreatedAt`, snake_case columns, `updated_at` = `current_timestamp(6) ON UPDATE current_timestamp(6)`); wired both `ConfigureModel` calls + `DbSet`s into `AppDbContext.cs` (`AppDbContext.partial.cs` untouched).
- **Step 2:** authored migration **`AddUsersAndAuthTokens`** offline via the design-time factory (dotnet-ef CLI 10.0.9 against the project's EF Core 8 design package — worked cleanly); reviewed (snake_case, utf8mb4/unicode_ci, unique indexes uuid/username/token_hash, indexes user_id/pair_uuid, FK cascade, tier default FREE, nullable revoked_at) and **applied to the live dev DB** (`database update` → Done).
- **Step 3:** `ErrorCodes` gained `Forbidden = 1004` and the 2xxx auth block (2000–2003); `ErrorException.GetDefaultHttpStatus` extended (403/400/401/401/400).
- **Step 4:** `Repositories/UserRepository.cs` (GetByUsername/GetByUuid/ExistsByUsername/Create with in-transaction re-check + duplicate-key race absorption/UpdatePassword) and `Repositories/AuthTokenRepository.cs` (AddPairAsync atomic two-row insert, GetByHashWithUserAsync incl. revoked, RevokeByPairUuidAsync returning the pair's hashes, DeleteAllByUserIdAsync returning deleted hashes, DeleteExpiredAsync).
- **Step 5:** contracts extended per plan (`TokenWhitelistEntry` + Username/TokenType/PairUuid; `IssueAsync(userId, username, …)`); real `PasswordHasher` (BCrypt, `Auth:BcryptWorkFactor`), static `TokenHasher` (SHA-256 → lowercase hex), `TokenWhitelistStore` (Redis cache-first, DB source of truth, warn-and-continue, backfill self-heal), `TokenService` (32-byte CSPRNG → Base64Url, pair issuance, full pair rotation, reuse-detection revoke-all cascade, opportunistic expired purge at issuance), `TokenValidator` (ACCESS + unexpired only).
- **Step 6:** DTOs under `Models/Auth/`, AutoMapper `Mappings/AuthProfile.cs`, four manual validators under `Validators/Auth/` (username 3–32 `a-zA-Z0-9_.-` stored lowercase; password 8 chars–72 UTF-8 bytes; Vietnamese messages).
- **Step 7:** `Services/Api/Auth/AuthService.cs` (interface + impl, dummy-hash timing equalization on unknown user, post-commit `RevokeAllAsync` after password change) and `Controllers/AuthController.cs` (register/login/refresh anonymous, logout/change-password guarded, refresh returns the plain DTO to exercise `[ResponseWrapped]` auto-wrap, Vietnamese Swagger annotations). `AppController.cs` untouched.
- **Step 8:** `Swagger/AuthorizeOperationFilter.cs` (padlock only on non-`[AllowAnonymous]` operations; global `AddSecurityRequirement` removed from `Program.cs`), `HandleForbiddenAsync` envelope override (1004/403) in `OpaqueTokenAuthenticationHandler`, **deleted `Controllers/AuthProbeController.cs`**; the two `HealthEndpointTests` authprobe cases retargeted minimally to `POST api/v1/auth/logout` (+ swagger test now also asserts `/api/v1/auth/login` present).
- **Implementation notes (minor, recorded deviations):**
  - `IAuthTokenRepository` additionally exposes single-row `AddAsync` and `RevokeByHashAsync` (beyond the planned list) so the fixed single-hash `ITokenWhitelistStore.AddAsync`/`RemoveAsync` contracts could be implemented honestly; issuance itself uses the planned atomic `AddPairAsync`, and `TokenService` performs the best-effort Redis cache writes/deletes directly via shared static helpers on `TokenWhitelistStore` (the plan's "+ Redis deletes").
  - `DeleteAllByUserIdAsync` returns the deleted hashes rather than a bare count (revoke-all must delete the matching Redis keys; count = list length).
  - The planned register success message "Đăng ký tài khoản thành công." cannot ride alongside `data` (the `ApiResult` envelope has no message-with-data shape and is not to be changed); register returns `ApiResult<UserResponse>` per the endpoint table, the message lives in the Swagger annotation. Logout/change-password success messages ship as specified via `SuccessMessage`.
  - Opportunistic expired-row purge call site: start of `TokenService.IssueAsync` (login/refresh).

### 2026-07-13 (verification)

- `dotnet build` clean (only the pre-existing AutoMapper NU1903 advisory warning); `dotnet test` **42/42 passed, 0 skipped**.
- Live boot smoke against MariaDB + Redis (port 5200): register → 200 `UserResponse` (tier FREE, lowercase username); duplicate register → 400 code 2000; invalid payload → 400 code 1001 with camelCase `error.fields` + Vietnamese messages; wrong password → 401 code 2001; login → raw pair (43-char Base64Url, 30 min / 30 days); refresh → new pair in the wrapped envelope (plain-DTO auto-wrap confirmed) and old access 401; **reused revoked refresh → 2002 AND all sessions killed** (new access 401 too — cascade confirmed); change-password wrong current → 2003; correct → success message, second device 401, new-password login works; logout → success, token 401 afterwards; Swagger padlock only on logout/change-password. One smoke user (`smoketest_1783952169`) remains in the dev DB (harmless dev data).

### 2026-07-13 (Step 9 — test suite complete, 145/145 green)

- **Test-engineer: full auth suite written per the Step-9 list; suite grew 42 → 145 tests, `dotnet test` = Failed 0 / Passed 145 / Skipped 0** (MariaDB + Redis live; every DB/Redis-dependent test is `[SkippableFact]` and skips cleanly when the servers are unreachable). Verified deterministic across two consecutive full runs; post-run sweep confirmed **0 leftover test users, 0 leftover `auth_tokens` rows, 0 orphaned `auth:token:*` Redis keys**.
- **Unit (no I/O):** `TokenHasherTests` (5 — FIPS vectors, 64-char lowercase hex, determinism), `PasswordHasherTests` (6 — roundtrip, wrong-password, salt uniqueness, configured/default work factor embedded in the hash, 72-byte boundary), `TokenValidatorTests` (5, fake store — valid ACCESS → `AuthenticatedUser`, lookup by SHA-256 hash never raw, unknown/expired/refresh-as-access → null), `TokenServiceTests` (13, fake repo + deliberately unreachable Redis so every cache op exercises the OQ7 warn-and-continue path — 43-char Base64Url raws, configured 30min/30d + custom lifetimes, hash-only persistence with one shared `pair_uuid`, opportunistic expired purge, full pair rotation, **reuse-detection revoke-all cascade**, pair-scoped `RevokeAsync`, `RevokeAllAsync` count), validator tests (24 across 4 classes — every rule incl. 3–32/charset/case, 8-char min, **72-byte (not char) boundary with multibyte proof**, same-password-reuse allowed on change; Vietnamese messages pinned per the Step-9 list).
- **Integration (real MariaDB):** `UserRepositoryTests` (7 — uuid/FREE/UTC defaults, duplicate → null + nothing persisted, case-insensitive retrieval via unicode_ci, exists/get-by-uuid, password update), `AuthTokenRepositoryTests` (9 — pair insert, unknown-user false, joined lookup **returns revoked rows** (reuse detection needs them), pair soft-revoke keeps rows + preserves first `revoked_at`, revoke-all hard-deletes + returns hashes, expired purge keeps live rows), `TokenWhitelistStoreTests` (6 — Redis-down: DB fallback without throwing, revoked/expired excluded, unknown-user add throws; live-Redis: **DB-fallback hit backfills the cache with remaining TTL**, cache-first hit answers without the DB), `AuthServiceTests` (14, service graph resolved from the app's DI — BCrypt-only persistence, lowercase username, codes 2000/2001/2002/2003 incl. expired-refresh via direct DB mutation, rotation kills the old access, **reused revoked refresh → 2002 + zero rows left + even the rotated session dead**, change-password kill switch + old-password login dies + same-password allowed, logout idempotent).
- **Endpoint (WebApplicationFactory, real HTTP):** `AuthEndpointTests` (14) — closes both Milestone-1 handoffs: register invalid payload → **400 with camelCase `error.fields`** (first e2e ValidationException), refresh returns a plain DTO → **wrapped envelope** (first e2e `[ResponseWrapped]` auto-wrap); plus register response leaks no password material, duplicate → 2000, wrong credentials → 2001, token-pair shape, full register→login→logout flow (token + paired refresh dead afterwards), two-device change-password (both 401, new password logs in), wrong current → 2003, **Swagger padlock present on logout/change-password and absent on register/login/refresh/health**, and the two guarded-401-envelope regression tests **moved out of `HealthEndpointTests` into `AuthEndpointTests`** (auth owns that contract now; HealthEndpointTests keeps health + generic swagger assertions).
- **Isolation strategy (recorded deviation from the rollback harness):** repository/service writes run `ExecuteTransactionAsync`, which begins its own DB transaction and cannot nest inside `IntegrationTestBase`'s per-test transaction — auth integration tests therefore use new bases `Infrastructure/AuthDbTestBase` (own connections) and `Infrastructure/AuthApiTestBase` (app DI/HTTP) with a unique lowercase username prefix per class and guaranteed dispose-time cleanup: best-effort Redis `auth:token:{hash}` deletion, then user deletion by prefix (FK cascade wipes `auth_tokens`). All DB-touching classes share the `[Collection("AuthIntegration")]` to serialize them — `TokenService.IssueAsync`'s global expired purge would otherwise race tests that seed deliberately-expired rows. New `Infrastructure/RedisFixture` mirrors the DB probe (probe once, `SkipIfNoRedis()`, `FSM_TEST_REDIS` override) and `UnreachableRedis` provides the deterministic degraded-Redis multiplexer.
- No production bugs found; production code untouched.

### 2026-07-14 (code review — APPROVED, 0 blocking — + hardening fixes)

- **Code-reviewer verdict: APPROVE, 0 blocking findings.** All 10 OQ decisions + the `revoked_at` amendment verified against the code; all 4 recorded implementation deviations and the test-engineer's isolation deviation confirmed real and justified; no silent deviations. Migration, conventions, DI (stubs gone, one registration per interface), and the 145 tests' assertions spot-checked as genuine. Three low-severity auth-layer hardening advisories + one process note raised; the orchestrator elected to fix all three before closing since later milestones build on this layer.
- **A1 — backfill race (`Auth/TokenWhitelistStore.cs`):** `LookupAsync` now re-reads the row after the self-heal cache write and evicts the just-written entry if the row was revoked/removed in the window, so a stale-valid cache entry can no longer outlive its DB revocation by the token TTL. One extra DB read on the (uncommon) cache-miss path only.
- **A2 — concurrent-refresh double-rotation (`Auth/TokenService.cs` + `Repositories/AuthTokenRepository.cs`):** added `IAuthTokenRepository.TryRevokeActiveByHashAsync` — a single conditional `revoked_at IS NULL` `ExecuteUpdateAsync`. `RefreshAsync` now *claims* the refresh token atomically; losing the claim (already revoked, or a concurrent refresh won) is treated as reuse → revoke-all + reject. This replaces the earlier plain `RevokedAt is not null` pre-check, which two simultaneous refreshes could both pass. Behavior change is intended; all existing outcomes (rotation, reuse cascade, expired/unknown/wrong-type → null) unchanged.
- **A3 — dummy-hash work factor (`Auth/PasswordHasher.cs` + `Services/Api/Auth/AuthService.cs`):** added `IPasswordHasher.CreateDummyHash()`, which hashes with the *configured* `Auth:BcryptWorkFactor` (cached per factor for the process). `AuthService` login now verifies unknown-user attempts against it instead of a static default-11 hash, so raising the work factor can't reopen the login timing oracle.
- Test fake `FakeAuthTokenRepository` (TokenServiceTests) gained the `TryRevokeActiveByHashAsync` implementation (mirrors the conditional update). Build clean; **`dotnet test` = Failed 0 / Passed 145 / Skipped 0** after the fixes.
- **Process note (recorded, resolved in [agent-dev-team.md](agent-dev-team.md) Decision Log):** the migration was applied to the dev DB during Implement, before Review — accepted going forward because the Test step needs live schema and the dev DB is disposable; non-dev environments still apply only after Review.
- **Deferred hardening (not blocking, tracked in Future Improvements):** a deterministic test for the A2 concurrency race (needs real parallelism, not unit-testable) and end-to-end simulation of the A1 window.

## Final Outcome

Milestone 2 implemented end-to-end per the approved plan (all 10 OQ answers + the `revoked_at` amendment honored): `users` + `auth_tokens` via first migration **`AddUsersAndAuthTokens`** (authored offline, reviewed, applied to the live dev DB), real opaque-token stack (BCrypt work factor 11, SHA-256 whitelist, Redis cache-first/DB-fallback with warn-and-continue, full pair rotation + reuse-detection revoke-all), five `api/v1/auth` endpoints with Vietnamese messages/Swagger, stubs and AuthProbe deleted, Swagger per-operation padlock + `HandleForbiddenAsync` envelope handoffs closed, config under `Auth:*`. Full register→login→refresh→rotate→reuse-cascade→change-password→logout flow smoke-verified live. Test-engineer added the 145-test suite (unit + real-MariaDB integration + WebApplicationFactory endpoint), closing both Milestone-1 coverage handoffs. Code-reviewer **APPROVED with 0 blocking findings**; the three low-severity hardening advisories (A1 cache backfill race, A2 concurrent-refresh double-rotation, A3 dummy-hash work factor) were fixed before closing. **Final state: build clean, `dotnet test` = 145 passed / 0 failed / 0 skipped.** Milestone 2 complete.

## Future Improvements

- Password recovery (email/OTP) — requires contact info the spec doesn't collect yet.
- Login brute-force protection / rate limiting — **explicitly deferred by the user (OQ9, 2026-07-13)**.
- Scheduled purge job for expired `auth_tokens` rows (opportunistic purge only for now).
- Session management endpoint ("phiên đang đăng nhập" list + revoke-one-device) — natural extension of the pair model.
- `GET /auth/me` profile endpoint once profile data exists beyond username/tier.
- Token prefix (`fsm_`) for secret-scanning friendliness.
- Deterministic concurrency tests for the A2 refresh-claim race and the A1 backfill window (need real parallelism / mid-test Redis outage injection; the single-threaded outcomes are covered).
