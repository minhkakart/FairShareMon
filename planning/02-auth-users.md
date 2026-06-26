# Phase 2 — Authentication & Users

## Objective
Implement user accounts and the opaque stateful token auth from `The-ideal.md` §3: register/login/refresh/logout, SHA-256-hashed token whitelist in `auth_tokens` + Memcached, the authentication middleware, and the Resource-Owned current-user accessor.

## Background
Auth is stateful opaque tokens (not JWT). The whitelist is checked on every request (cache → DB fallback). Register must eventually seed the `is_owner` member (phase 3) and default categories (phase 4) — deferred to minor phases below.

## Requirements
- Tokens are random, returned once; only `SHA-256(token)` is persisted.
- Access + refresh pair; refresh rotates the access token.
- Logout removes the token from whitelist (DB + cache); password change revokes all of the user's tokens.
- Every authenticated request resolves an `AuthenticatedUser` carrying `Id`/`Uuid`.

## Dependencies
Phase 1 (ApiResult, BaseRepository, ICache, AppController, ErrorException).

---

## Stage 2.1 — Schema: users & auth_tokens
1. Append DDL to `database-migration.sql`:
   - `users` (`id` bigint PK, `uuid`, `username` unique, `password_hash`, `created_at`, `updated_at`).
   - `auth_tokens` (`id`, `uuid`, `user_id` FK, `token_hash` indexed, `type` enum `ACCESS|REFRESH`, `expires_at`, `created_at`).
2. Entities: `Database/Entities/User.cs` + `Partials/User.cs` (ctor + `ConfigureModel`); same for `AuthToken`.
3. Register both `ConfigureModel` calls in `AppDbContext.OnModelCreating`.

**Acceptance:** DDL appended; entities map cleanly; build green.

---

## Stage 2.2 — Password hashing
1. `Auth/IPasswordHasher.cs` (+ impl) using BCrypt: `HashPassword`, `VerifyPassword`. `[SingletonService(typeof(IPasswordHasher))]`.

**Acceptance:** hash/verify round-trips in a unit test.

---

## Stage 2.3 — Token service
1. `Auth/ITokenService.cs` (+ impl):
   - `IssuePair(userId)` → generate two opaque tokens (256-bit base64url), compute `SHA-256` hashes, persist `auth_tokens` rows with TTLs (access short, refresh long), push hashes to Memcached with TTL = expiry, return raw tokens once.
   - `ValidateAsync(rawToken)` → hash, check cache then DB; return owning `userId` or null.
   - `RevokeAsync(rawToken)` and `RevokeAllForUser(userId)` (DB + cache).
2. `Models/Dtos/AuthTokenDto.cs` (accessToken, refreshToken, expiresAt).

**Acceptance:** issue → validate → revoke flow works against test DB + fake cache.

---

## Stage 2.4 — Authentication middleware/handler
1. `Auth/Authentication/OpaqueTokenAuthHandler.cs` (or middleware): read `Authorization: Bearer <token>`, call `ITokenService.ValidateAsync`, build a `ClaimsPrincipal` with user id/uuid.
2. `Auth/AuthenticatedUser.cs` + `IContextAuthenticated`; `HttpContext.User.GetAuthenticatedUser()` extension.
3. Wire scheme + `[Authorize]` default policy in `Program.cs`.

**Acceptance:** request with a valid token resolves `AuthenticatedUser`; invalid/expired → 401 wrapped failure.

---

## Stage 2.5 — Resource-Owned plumbing
1. Establish the pattern: services scope every owned query by `AuthenticatedUser.Id`; helper/extension to apply `WHERE user_id = :id`.
2. Document the 404-not-403 rule for owned-resource lookups.

**Acceptance:** a sample owned query returns 404 for another user's row.

---

## Stage 2.6 — Auth endpoints
1. `Services/Api/Auth/AuthService.cs` (interface + impl): `Register`, `Login`, `Refresh`, `Logout`.
   - `Register`: validate unique username, hash password, create `user` in a transaction. *(Owner-member + default-category seeding deferred — see Minor phases.)*
   - `Login`: verify password, issue token pair.
   - `Refresh`: validate refresh token, rotate access token.
   - `Logout`: revoke current token.
2. `Controllers/Common/AuthController.cs`: `POST register|login|refresh|logout`, Swagger annotations, Vietnamese summaries.
3. `Models/Requests/`: `RegisterRequest`, `LoginRequest`, `RefreshTokenRequest`.

**Acceptance:** full register → login → refresh → logout cycle via Swagger.

---

## Stage 2.7 — Account password change (revoke-all)
1. `Services/Api/Accounts/AccountService.cs` `ChangePassword`: verify current, set new hash, `RevokeAllForUser`. `AccountsController` `PUT /api/accounts/password`.

**Acceptance:** after change, old tokens fail on next request.

---

## Stage 2.8 — Tests
1. xUnit: register-duplicate-username, login-wrong-password, refresh-rotation, logout-revoke, password-change-revokes-all. Assert both `ApiResult` and persisted rows.

---

## Minor phase 2.A — Revisit `Register`: seed owner member (after Phase 3)
> Triggered once `members` exists. **Change to `AuthService.Register`.**
1. Inside the register transaction, create one `members` row with `is_owner = true`, `name` = user's display name.
2. Add a test asserting the owner member is created exactly once.

## Minor phase 2.B — Revisit `Register`: seed default categories (after Phase 4)
> Triggered once `categories` exists. **Change to `AuthService.Register`.**
1. Inside the register transaction, insert default categories (Ăn uống, Đi lại, Khách sạn, Mua sắm, Khác) with one `is_default = true`.
2. Test asserting exactly one default category after register.

---

## Impact Analysis
- **APIs:** `/api/auth/*`, `/api/accounts/password`.
- **Database:** `users`, `auth_tokens` (+ later: owner member, default categories).
- **Infrastructure:** auth scheme, Memcached token whitelist.
- **Services:** `AuthService`, `AccountService`, `TokenService`, `PasswordHasher`.

## Open questions / Assumptions
- Access/refresh TTLs (assume 1h / 30d — confirm).
- Token transport: `Authorization: Bearer` header (assumed).
- Single active refresh token per device vs unlimited (assume unlimited; rotation optional).

## Progress log
- (pending)

## Final outcome
- (to be completed)
