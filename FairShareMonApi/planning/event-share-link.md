# Event share link (public, read-only, 1-day TTL)

## Objective

Let a **Premium** owner mint a temporary **public, read-only** link to a **CLOSED** expense event
(The-ideal.md §6 "Chia sẻ đợt (chỉ xem)"). Anyone with the link (no account, no login) opens a
read-only report: each member's aggregated balance, a per-member drill-in to that member's own
per-expense breakdown, and each member's VietQR against a destination bank chosen at
link-creation time. The link auto-expires after 1 day. The owner can view / copy / revoke /
regenerate the active link. The public payload is a **live** read (a closed event's spend figures
are frozen, but the settled/outstanding overlay can still change and must be reflected).

## Background

- **Spec anchors.** §6 lists this as a Premium future feature ("link công khai có bảo vệ để thành
  viên xem báo cáo đợt mà không cần tài khoản"). §3.11 gates the "mở rộng" bucket (incl. "chia sẻ
  đợt") to Premium. §4.4 makes a closed event immutable (read/export/QR only). §4 rule 9 / §3.11:
  tier limits only ever block *creating new* data — never lock/hide existing data, incl. after a
  downgrade.
- **Closed-only QR precedent.** `WalletQrService.GenerateEventQrAsync` /
  `GenerateEventMemberQrsAsync` already enforce closed-only for the event QR
  (`EventNotClosedForQr` 12002), gate Premium first
  (`tierService.EnsurePremiumFeature(MessageKeys.Feature.Qr)` → 403 `PremiumFeatureRequired`
  13003), resolve the destination via `ResolveDestinationAsync` (default account or
  `bankAccountUuid` override; miss → `BankAccountNotFound` 12000, none → `NoBankAccountForQr`
  12001), reuse the M7 balance (`statsService.GetEventBalanceAsync`), select debtors via
  `CollectEventBillables` (`row.Outstanding > 0`), and render per member via `BuildMemberQrsAsync`
  (`RenderSingle` → `data:image/png;base64,` data URL). Nothing is persisted. This feature reuses
  that machinery for the QR path.
- **Auth / cache precedents.** `Database/Entities/AuthToken.cs` (+
  `Partials/AuthToken.cs`) is the model to mirror for the new entity (Id / Uuid unique / UserId FK
  cascade / token column unique / ExpiresAt / RevokedAt? / timestamps + `ValueGeneratedOnAddOrUpdate`
  `UpdatedAt`). `Auth/TokenWhitelistStore.cs` is the Redis cache to mirror: source-of-truth in DB,
  Redis a best-effort cache keyed `auth:token:{hash}` with TTL = remaining lifetime, cache-first +
  DB fallback + self-heal backfill, delete-on-revoke, warn-and-continue on Redis failure.
  `Auth/TokenService.cs` shows CSPRNG raw-token minting (`RandomNumberGenerator.GetBytes(32)` →
  `WebEncoders.Base64UrlEncode`, 43 chars, 256-bit).
- **Anonymous routing.** `AppController` (LOCKED) supplies the versioned route + `[ResponseWrapped]`
  and exposes `AuthenticatedUser` (throws 401 on anonymous). `Program.cs` sets a `FallbackPolicy`
  requiring an authenticated user for every endpoint **unless `[AllowAnonymous]`** (used by
  `HealthController` at the class level and `AuthController` per-method). A new anonymous controller
  therefore only needs `[AllowAnonymous]` and must never touch `AuthenticatedUser`.
- **Verified data-shape facts (checked against live code):**
  - `IStatsService.GetEventBalanceAsync(string userUuid, string eventUuid, CancellationToken)` →
    `EventBalanceResponse { EventUuid, EventName, IsClosed, IReadOnlyList<MemberBalanceRow> Rows,
    decimal TotalOutstanding, int OwingMemberCount, int SettledMemberCount }`. It is resource-owned
    (miss → `EventNotFound` 9000). **It does NOT carry `ClosedAt`.**
  - `MemberBalanceRow { MemberUuid, MemberName, IsOwnerRepresentative, IsDeleted, Advanced, Owed,
    Balance, Outstanding, IsSettled, SettledAt }`.
  - `IExpensesService.ListAsync(string userUuid, ExpenseFilter, CancellationToken)` →
    `IReadOnlyList<ExpenseSummaryResponse>`. **`ExpenseSummaryResponse` has only `ShareCount`, NOT
    a per-share list.** The per-share breakdown (`ShareResponse { Member{Uuid,Name}, Amount, Note,
    IsSettled, SettledAt }`) lives on the full `ExpenseResponse` returned by
    `IExpensesService.GetAsync(userUuid, expenseUuid)` (and its repo `GetByUuidAsync`, which
    `.Include(Shares).ThenInclude(Member)`). `ExpenseFilter` has an `EventUuid` field.
    **This contradicts the task brief's assumption that `ListAsync(...)` populates
    `ExpenseResponse.Shares` — see Open Question 2.**
  - `IEventsService.GetAsync(userUuid, eventUuid)` → `EventResponse { …, IsClosed, ClosedAt, … }`
    (resource-owned, miss → 9000) — the source for `closedAt`.
  - `IWalletQrService` private helpers exist and are reusable: `record BilledMember(MemberUuid,
    MemberName, Amount, Description)`, `CollectEventBillables(EventBalanceResponse)`,
    `BuildMemberQrsAsync(BankAccount account, string contextName, IReadOnlyList<BilledMember>,
    CancellationToken)`. `BuildMemberQrsAsync` takes a `BankAccount` — a **transient**
    (non-persisted) `BankAccount` built from the snapshot works with no DB row.
  - `ITierService.EnsurePremiumFeature(string featureNameKey)` throws
    `PremiumFeatureRequired` 13003 when the caller is Free (localizes the feature name arg).
  - `IBankAccountRepository.GetByUuidAsync(userUuid, uuid)` / `GetDefaultAsync(userUuid)` are the
    resource-owned destination lookups. Bank accounts are **hard-deleted** (not soft-delete) — hence
    the snapshot requirement (Decision 7 below).
  - `BaseRepository` exposes `ExecuteQueryAsync`, `ExecuteTransactionAsync`, `Query<T>(tracking,
    includeDeleted)`; `TransactionContext.NoCommit()` aborts a write.
  - **`ErrorCodes` 15xxx is already RESERVED** by `planning/settled-per-member.md` (block claimed,
    no codes defined yet, "reserved for any future settled-per-member-specific failure state"). The
    next truly free block is **16xxx** — see Open Question 1.
  - CORS is a single global policy applied to every controller (`planning/cors-configuration.md`);
    the anonymous routes need no CORS change.

## Requirements

- Owner (Premium, at creation) can create a public share link for one of their **closed** events.
- Anonymous consumer (no token, no account) can:
  - `GET` the live read-only report: event name, closed-at, per-member balance rows, per-expense
    breakdown (each expense's shares), aggregate outstanding / owing-count / settled-count, and a
    `hasQr` flag.
  - `GET` per-member VietQR images (one data-URL PNG per still-owing member) against the snapshot
    bank.
- The link expires 1 day after creation (config `Share:LinkTtlHours`, default 24).
- Owner can view the active link (to copy), revoke it, and regenerate it. Creating when an
  unexpired non-revoked link already exists reuses it rather than minting a duplicate (Decision 4).
- Premium gate applies **only at creation** (§3.11). The anonymous view is **never** re-gated
  (§4 rule 9 — existing data stays readable even after a downgrade).
- Creation is **closed-only** (§4.4), mirroring the closed-only event QR.
- The public report is **live**: spend figures are frozen (closed event) but the
  settled/outstanding overlay reflects the current state on every read.
- Store the opaque **token value** (unique-indexed), not a hash — the link must be re-displayable
  ("view/copy active link") (Decision 6).
- **Snapshot** the destination bank fields (BankBin / BankName / AccountNumber / AccountHolderName)
  onto the link so the QR is stable if the wallet account is later edited or hard-deleted; keep
  `BankAccountUuid` as a soft reference (Decision 7).
- All user-facing strings (Swagger, messages) in Vietnamese. Money stays `decimal`. Schema change
  via an EF migration. Soft-revoke preserves the row until natural expiry.

## Open Questions

> **RESOLVED 2026-07-24 (orchestrator).** All 8 Open Questions below were resolved by the
> orchestrator; see the **Orchestrator resolutions (2026-07-24)** subsection of the Decision Log for
> the binding decisions. Summary: OQ1 → (a) 16xxx; OQ2 → (a) `ListDetailedByEventAsync`; OQ3 → (b)
> CSPRNG token stored plain; OQ4 → (b) bank optional (`hasQr`); OQ5 → (b) `regenerate` flag on the
> request body; OQ6 → (a) reuse-unchanged, ignore differing bank; OQ7 → (a) fixed TTL; OQ8 → (a)
> `200` `data: null`. Plus: add `hasQr` (bool) to `PublicEventShareResponse`.

1. **ErrorCodes block — 15xxx is already reserved; use 16xxx?** The task brief said to claim a new
   15xxx block and "confirm it is the next free block". It is **not** free: `ErrorCodes.cs` records
   "15xxx - Settled per member (block reserved by planning/settled-per-member.md … reserved for any
   future settled-per-member-specific failure state)". Options:
   - **(a) Claim 16xxx for the share-link block (recommended).** `ShareLinkNotFoundOrExpired = 16000`,
     `EventNotClosedForShare = 16001`. Respects the existing block-reservation convention; zero risk
     of colliding with a future settled-per-member code. Trade-off: differs from the brief's 15xxx.
   - **(b) Take 15xxx as the brief said.** Trade-off: violates the reserved-block note for
     settled-per-member and risks a future collision; the reservation would have to be formally
     released.
   **Recommendation: (a) 16xxx.** The rest of this doc is written against 16xxx; if (b) is chosen,
   swap the two constants into 15xxx.

2. **How does the public report load the per-expense breakdown?** The brief assumed
   `ExpensesService.ListAsync(owner, ExpenseFilter{EventUuid})` "populates `ExpenseResponse.Shares`".
   Verified false: `ListAsync` returns `ExpenseSummaryResponse` (only `ShareCount`, no per-share
   list). To render each expense's shares (member / amount / settled / note) we need full expense
   detail. Options:
   - **(a) New one-query read that returns full expense detail for the event (recommended).** Add
     `IExpensesService.ListDetailedByEventAsync(userUuid, eventUuid, ct)` →
     `IReadOnlyList<ExpenseResponse>` backed by a new
     `IExpenseRepository.ListDetailedByEventAsync` that `.Include(Category).Include(PayerMember)
     .Include(Shares).ThenInclude(Member).Include(ExpenseTags).ThenInclude(Tag).Include(Event)` in a
     single resource-owned query (mirrors `GetByUuidAsync`'s includes, filtered by event). Trade-off:
     one new repo+service method, but no N+1 and correct per-share data.
   - **(b) `ListAsync` for the UUID set, then `GetAsync` per expense.** Trade-off: reuses only
     existing methods but is N+1 (one detail query per expense on every anonymous page load — a
     public, uncached, potentially hot path).
   - **(c) Enrich `ExpenseSummaryResponse` / `ListAsync` to include per-share detail.** Trade-off:
     changes a shared existing response shape used elsewhere (expense list UI) — larger blast radius,
     rejected.
   **Recommendation: (a).** Please confirm (it adds one repo + one service method beyond the brief's
   listed surface).

3. **Token generator — `Uuid.NewV7()` vs CSPRNG.** The brief says mint the token with
   `Uuid.NewV7()`. A UUIDv7 is **time-ordered**: its leading bits encode the creation timestamp, so
   it is partially predictable and mildly enumerable. This is a *public* capability URL (anyone with
   it can read the report). Options:
   - **(a) `Uuid.NewV7()` as the brief specifies.** Simple, consistent with the repo's Uuid helper;
     ~74 bits of randomness after the timestamp. Acceptable for a low-sensitivity, 1-day, read-only
     link (Decision 6 framing). Trade-off: guessable timestamp prefix; not unguessable.
   - **(b) CSPRNG token (recommended for a public capability):** reuse the exact pattern in
     `TokenService.NewRawToken()` — `WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))`
     (43 chars, 256-bit, URL-safe). Still stored as the plain value (Decision 6). Trade-off: a second
     token-minting path (but identical to the shipped one).
   **Recommendation: (b).** Please confirm; the plan below keeps the column at `VARCHAR(64)` so either
   choice fits.

4. **Is the destination bank required at creation, or optional (link without QR)?** The response DTO
   carries a `hasQr` flag, which is only meaningful if a link can exist without a bank. Options:
   - **(a) Bank required (mirror the QR path).** Creation resolves the default/override account and
     fails `NoBankAccountForQr` (12001) / `BankAccountNotFound` (12000) exactly like the QR endpoints;
     `hasQr` is then always true. Trade-off: an owner with no wallet account can't share at all.
   - **(b) Bank optional (recommended, given `hasQr` exists).** If no `bankAccountUuid` override is
     given and there is no default account, the link is created **without** a bank snapshot
     (`hasQr = false`); the public report renders but the QR endpoint returns an empty list. A given
     (or default-resolved) account is snapshotted and `hasQr = true`. An **explicit** override UUID
     that misses still → 12000. Trade-off: the QR path must tolerate a null snapshot.
   **Recommendation: (b).** Please confirm; the entity's bank snapshot columns are modelled nullable
   to support it (make them non-null if (a) is chosen).

5. **"Regenerate" — separate endpoint or DELETE-then-POST?** Decision 4 fixes reuse-on-create and
   revoke, but the verb for *regenerate* (mint a fresh token, invalidating the old) is unspecified.
   Options:
   - **(a) No dedicated verb — client calls `DELETE {uuid}/share` then `POST {uuid}/share`.** Two
     round-trips; simplest surface. Trade-off: a brief window with no active link.
   - **(b) `POST {uuid}/share?regenerate=true` (or a body flag) (recommended).** Without the flag,
     POST reuses the active link (Decision 4); with the flag, POST revokes the active link and mints
     a fresh one atomically. Single round-trip, no gap. Trade-off: one query param on POST.
   - **(c) Dedicated `PUT {uuid}/share/regenerate`.** Explicit but adds a third route.
   **Recommendation: (b).** Please confirm.

6. **Reuse vs a changed bank at creation.** When an active link already exists (Decision 4 = reuse)
   but the new `POST` requests a *different* `bankAccountUuid` than the active link's snapshot, do we
   reuse (ignoring the new bank) or refresh the snapshot? Options:
   - **(a) Reuse unchanged; ignore the new bank choice while a link is active (recommended).** To
     change the destination the owner regenerates (OQ5). Simplest, keeps one token stable. Trade-off:
     the requested bank is silently ignored until regenerate.
   - **(b) If the requested bank differs from the active snapshot, revoke + regenerate implicitly.**
     Trade-off: a create call can silently rotate the token, surprising a client that expected reuse.
   **Recommendation: (a).** Please confirm.

7. **TTL on reuse — fixed or sliding?** When `POST`/`GET` returns an existing active link, does the
   1-day clock stay anchored to the original creation or extend? Options:
   - **(a) Fixed expiry from original creation (recommended).** Matches "auto-expires after 1 day"
     literally; the link's lifetime is deterministic. Trade-off: an owner who wants more time
     regenerates.
   - **(b) Sliding — reuse extends `ExpiresAt` to now + TTL.** Trade-off: a link viewed daily never
     expires; contradicts the plain 1-day intent.
   **Recommendation: (a).** Please confirm.

8. **`GET {uuid}/share` when no active link exists.** Options: (a) return `200` with `data: null`
   (recommended — "no active link" is a normal state, not an error); (b) return `404`
   `ShareLinkNotFoundOrExpired` (16000). **Recommendation: (a).** Please confirm.

## Assumptions

- The 7 pre-resolved decisions in the task brief are locked and are recorded verbatim in the
  Decision Log; they are not reopened here.
- Reusing `Models/Stats/MemberBalanceRow` directly in the public payload (per the brief) is accepted:
  the anonymous viewer sees `Advanced/Owed/Balance/Outstanding/IsSettled/SettledAt/IsOwner…/IsDeleted`.
  This is the group's shared report, so the decomposition is intended to be visible. (Flagging only:
  if a leaner public row is wanted, that is a follow-up — not assumed here.)
- The public QR endpoint returns an **empty list** (not a 12003 error) when nobody owes — a valid
  shared report can legitimately have zero debtors; an error would be confusing to an anonymous
  viewer. (The authed QR endpoints keep throwing 12003; only the share path softens this.)
- Member counts per event are small; returning N base64 PNGs in one JSON payload is acceptable (same
  assumption as `per-member-qr-sharing.md`).
- The Redis cache stores only the **link metadata** (owner UUID, event UUID, bank snapshot, expiry,
  revoked-flag) for fast token→link resolution — never the report payload, which is always recomputed
  live.
- A closed event cannot be reopened or deleted (delete is OPEN-only), so the closed-only invariant
  holds for the lifetime of the link; the QR-for-share re-assertion is a defensive guard.

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/`. Vietnamese for all user-facing strings and
> Swagger. Uses the locked stack: Controllers → Services/Api → Repositories → AppDbContext; EF
> migration; decimal money (none new here); soft-delete/soft-revoke; `Uuid.NewV7()` for the entity
> Uuid; opaque token per OQ3. Written against OQ recommendations (16xxx, detailed read, CSPRNG token,
> bank-optional, POST?regenerate, reuse-unchanged, fixed TTL, GET-null) — adjust if the checkpoint
> chooses otherwise.

### Step 1 — Entity `EventShareLink`

`Database/Entities/EventShareLink.cs` — POCO `partial class EventShareLink : IEntity` modelled on
`AuthToken.cs`, Vietnamese XML-doc:
- `ulong Id`
- `string Uuid` (external ref, unique)
- `ulong UserId` — owner FK → `users.id`, cascade delete
- `ulong EventId` — FK → `events.id`, cascade delete
- `string Token` — the opaque link token (unique index)
- `DateTime ExpiresAt`
- `DateTime? RevokedAt` — null = active (soft-revoke, kept until expiry)
- Bank snapshot (nullable per OQ4b): `string? BankAccountUuid` (soft reference), `string? BankBin`,
  `string? BankName`, `string? AccountNumber`, `string? AccountHolderName`
- `DateTime CreatedAt`, `DateTime UpdatedAt`
- `User User { get; set; } = null!;`  and  `Event Event { get; set; } = null!;` navigations

`Database/Entities/Partials/EventShareLink.cs` — ctor (`Uuid = Uuid.NewV7(); CreatedAt =
AppDateTime.Now;`) + `static ConfigureModel(ModelBuilder)` mirroring `AuthToken.ConfigureModel`:
table `event_share_links`; PK `id`; `uuid` maxlen 64 unique; `user_id` indexed + FK cascade; `event_id`
indexed + FK cascade; `token` maxlen 64 unique; `expires_at`; `revoked_at`; snapshot columns
`bank_account_uuid` (64), `bank_bin` (16), `bank_name` (100), `account_number` (32),
`account_holder_name` (100); `created_at`; `updated_at` `ValueGeneratedOnAddOrUpdate()` with
`current_timestamp(6) ON UPDATE current_timestamp(6)`.

`Database/AppDbContext.cs`: add `public DbSet<EventShareLink> EventShareLinks => Set<EventShareLink>();`
and `EventShareLink.ConfigureModel(modelBuilder);` in `OnModelCreating` (keep the File Ownership Rule
— no query filter needed; this entity is not `IEntityDeletable`).

Migration: `dotnet ef migrations add AddEventShareLink --project .\FairShareMonApi\FairShareMonApi.csproj`
then review + `database update`. No CHECK constraint (no money column).

### Step 2 — Repository `EventShareLinkRepository`

`Repositories/EventShareLinkRepository.cs` — `interface IEventShareLinkRepository : IBaseRepository`
+ `[ScopedService(typeof(IEventShareLinkRepository))] sealed class EventShareLinkRepository(AppDbContext)
: BaseRepository(dbContext)`. Methods:
- `Task<EventShareLink> CreateAsync(string userUuid, string eventUuid, string token, DateTime
  expiresAt, string? bankAccountUuid, string? bankBin, string? bankName, string? accountNumber,
  string? accountHolderName, CancellationToken)` — resolves owner `user_id` and `event_id` (scoped by
  `userUuid`) inside `ExecuteTransactionAsync`; inserts the row; `NoCommit()` + null-ish guard if the
  event is not owned (caller has already validated via the service, so this is defensive).
- `Task<EventShareLink?> GetActiveByEventAsync(string userUuid, string eventUuid, CancellationToken)`
  — `Query()` where `User.Uuid == userUuid && Event.Uuid == eventUuid && RevokedAt == null &&
  ExpiresAt > AppDateTime.Now`, most-recent first; null when none.
- `Task<bool> RevokeActiveByEventAsync(string userUuid, string eventUuid, CancellationToken)` —
  tracked update inside `ExecuteTransactionAsync`: set `RevokedAt = AppDateTime.Now` on the active
  owned link; returns whether one was revoked (also returns its token so the service can evict the
  cache — return `(bool revoked, string? token)` or a small result record).
- `Task<EventShareLink?> GetByTokenAsync(string token, CancellationToken)` — **anonymous, NOT
  user-scoped**: `Query().Include(l => l.User).Include(l => l.Event).FirstOrDefault(l => l.Token ==
  token)`. Returns the row regardless of expiry/revoke; the service decides validity so it can
  distinguish "unknown" from "expired/revoked" if needed (both map to 16000).

### Step 3 — Redis cache `EventShareLinkCache`

`Services/Api/Share/EventShareLinkCache.cs` — mirror `Auth/TokenWhitelistStore.cs`:
`[SingletonService]`-free (scoped, like the store), primary ctor
`(IEventShareLinkRepository repo, IConnectionMultiplexer redis, ILogger<EventShareLinkCache> logger)`.
- Key prefix `share:event:` → `CacheKey(token)`.
- A serializable `EventShareLinkEntry` record { OwnerUserUuid, EventUuid, ExpiresAt, BankAccountUuid?,
  BankBin?, BankName?, AccountNumber?, AccountHolderName? } (revoked links are simply absent from the
  cache — a revoke deletes the key).
- `LookupAsync(token)` — cache-first; on miss, `repo.GetByTokenAsync`; if the row is null / revoked /
  expired → return null (do not cache); else build the entry, backfill Redis with TTL = `ExpiresAt -
  now`, return it. Warn-and-continue on Redis failure (best-effort), matching the store.
- `AddAsync(token, entry)` — best-effort cache write with TTL = remaining lifetime (the DB row is
  written by the repository in the create transaction first).
- `RemoveAsync(token)` — best-effort delete (called after a revoke commits).

### Step 4 — Service `EventShareService`

`Services/Api/Share/EventShareService.cs` — interface + impl in one file (repo convention).
`[ScopedService(typeof(IEventShareService))] sealed class EventShareService(
  IEventShareLinkRepository shareLinkRepository, EventShareLinkCache shareLinkCache,
  IStatsService statsService, IEventsService eventsService, IExpensesService expensesService,
  IBankAccountRepository bankAccountRepository, IWalletQrService walletQrService,
  ITierService tierService, IConfiguration configuration) : IEventShareService`.
- TTL: `configuration.GetValue("Share:LinkTtlHours", 24)` → `TimeSpan.FromHours(...)`.

Methods:
- `Task<ShareLinkResponse> CreateAsync(string userUuid, string eventUuid, CreateShareLinkRequest
  request, CancellationToken)`:
  1. `tierService.EnsurePremiumFeature(MessageKeys.Feature.Share)` (403 13003, first — Premium at
     creation, §3.11).
  2. `var balance = await statsService.GetEventBalanceAsync(userUuid, eventUuid, ct)` (owner-scoped;
     miss → `EventNotFound` 9000).
  3. `if (!balance.IsClosed) throw ErrorException(EventNotClosedForShare 16001,
     MessageKeys.Error.EventNotClosedForShare)` (closed-only, §4.4).
  4. Regenerate handling (OQ5b): if `request.Regenerate`, revoke the active link first (Step below).
     Else reuse: `var active = await shareLinkRepository.GetActiveByEventAsync(userUuid, eventUuid,
     ct)`; if non-null return `MapToResponse(active)` (reuse-unchanged, OQ6a; fixed TTL, OQ7a).
  5. Resolve + snapshot bank (OQ4b): if `request.BankAccountUuid` given →
     `bankAccountRepository.GetByUuidAsync(userUuid, uuid, ct)` (miss → `BankAccountNotFound` 12000);
     else `GetDefaultAsync(userUuid, ct)` (may be null → no snapshot, `hasQr = false`). Snapshot
     `BankBin/BankName/AccountNumber/AccountHolderName` + keep `BankAccountUuid`.
  6. Mint token per OQ3 (CSPRNG recommended). `expiresAt = AppDateTime.Now + ttl`.
  7. `var link = await shareLinkRepository.CreateAsync(userUuid, eventUuid, token, expiresAt,
     …snapshot…, ct)`. **After** the transaction: `await shareLinkCache.AddAsync(token, entry)`.
  8. Return `MapToResponse(link)`.
- `Task<ShareLinkResponse?> GetActiveAsync(string userUuid, string eventUuid, CancellationToken)` —
  validate ownership via `statsService.GetEventBalanceAsync` (or `eventsService.GetAsync`) → 9000 on
  miss; then `GetActiveByEventAsync`; return `MapToResponse` or null (OQ8a).
- `Task RevokeAsync(string userUuid, string eventUuid, CancellationToken)` — ownership check (9000);
  `shareLinkRepository.RevokeActiveByEventAsync`; **after** commit, if a token was revoked,
  `await shareLinkCache.RemoveAsync(token)`. Idempotent (no active link = no-op success).
- `Task<PublicEventShareResponse> GetPublicAsync(string token, CancellationToken)` **[anonymous]**:
  1. `var entry = await shareLinkCache.LookupAsync(token, ct)` — null → `ErrorException(
     ShareLinkNotFoundOrExpired 16000, MessageKeys.Error.ShareLinkNotFoundOrExpired)`.
  2. Live read using the **owner** UUID from the entry:
     - `var evt = await eventsService.GetAsync(entry.OwnerUserUuid, entry.EventUuid, ct)` → name +
       `ClosedAt`.
     - `var balance = await statsService.GetEventBalanceAsync(entry.OwnerUserUuid, entry.EventUuid,
       ct)` → `Rows`, `TotalOutstanding`, `OwingMemberCount`, `SettledMemberCount`.
     - `var expenses = await expensesService.ListDetailedByEventAsync(entry.OwnerUserUuid,
       entry.EventUuid, ct)` (OQ2a) → map each to `PublicExpense` + `PublicShare[]`.
  3. Build `PublicEventShareResponse { EventName = evt.Name, ClosedAt = evt.ClosedAt, Rows =
     balance.Rows, Expenses = …, TotalOutstanding, OwingMemberCount, SettledMemberCount, HasQr =
     entry.BankBin is not null }`.
- `Task<IReadOnlyList<MemberQrResponse>> GetPublicMemberQrsAsync(string token, CancellationToken)`
  **[anonymous]**:
  1. `LookupAsync(token)` → 16000 on miss.
  2. If `entry.BankBin is null` (no snapshot, OQ4b) → return `[]` (empty list, `hasQr = false`).
  3. `return await walletQrService.GenerateEventMemberQrsForShareAsync(entry.OwnerUserUuid,
     entry.EventUuid, new BankSnapshot(entry.BankBin, entry.BankName, entry.AccountNumber,
     entry.AccountHolderName), ct)`.

`MapToResponse` builds `ShareLinkResponse` (token, expiresAt, createdAt, revoked=false, bank display
fields, `HasQr`).

### Step 5 — `WalletQrService.GenerateEventMemberQrsForShareAsync`

Add to `IWalletQrService` + impl (Vietnamese XML-doc). Existing methods unchanged.
`Task<IReadOnlyList<MemberQrResponse>> GenerateEventMemberQrsForShareAsync(string ownerUserUuid,
string eventUuid, BankSnapshot bankSnapshot, CancellationToken ct)`:
- **No Premium gate** (§4 rule 9 — the anonymous view is never re-gated).
- `var balance = await statsService.GetEventBalanceAsync(ownerUserUuid, eventUuid, ct)`.
- Re-assert closed-only (defensive): `if (!balance.IsClosed) throw EventNotClosedForShare 16001`.
- Build a **transient** `BankAccount { BankBin = snapshot.BankBin, BankName = snapshot.BankName,
  AccountNumber = snapshot.AccountNumber, AccountHolderName = snapshot.AccountHolderName }` (not added
  to the context, never persisted).
- `var (contextName, billed) = CollectEventBillables(balance)` (reuse the existing private helper).
- Empty debtors → return `[]` (share path softens 12003 — Assumptions).
- `return await BuildMemberQrsAsync(account, contextName, billed, ct)` (reuse the existing private
  helper).
- `BankSnapshot` is a small public record (in `Services/Api/Wallet` or `Models/Wallet`) `{ string
  BankBin, string BankName, string AccountNumber, string AccountHolderName }`.

### Step 6 — Expenses detailed-by-event read (OQ2a)

- `IExpenseRepository.ListDetailedByEventAsync(string userUuid, string eventUuid, CancellationToken)`
  → `IReadOnlyList<Expense>` — resource-owned `Query()` filtered by `Event.Uuid == eventUuid &&
  User.Uuid == userUuid`, with the same `.Include(...)` graph as `GetByUuidAsync` (Category, PayerMember,
  Shares.ThenInclude(Member), ExpenseTags.ThenInclude(Tag), Event), ordered `ExpenseTime` DESC.
- `IExpensesService.ListDetailedByEventAsync(userUuid, eventUuid, ct)` →
  `IReadOnlyList<ExpenseResponse>` via the existing AutoMapper `Expense → ExpenseResponse` map.

### Step 7 — Controllers

`Controllers/EventsController.cs` (authed; inject `IEventShareService shareService` — allowed, this
controller is not `AppController` itself):
- `[HttpPost("{uuid}/share")]` `CreateShareAsync([FromRoute] string uuid, [FromBody]
  CreateShareLinkRequest request, [FromQuery] bool regenerate, CancellationToken)` (regenerate per
  OQ5b — or move the flag into the body) →
  `ApiResult<ShareLinkResponse>.Success(await shareService.CreateAsync(AuthenticatedUser.Id, uuid,
  request, ct))`. Swagger 200 / 400 (12001 no bank / 12000 override miss / 16001 open event) / 401 /
  403 (13003 Premium) / 404 (9000 event).
- `[HttpGet("{uuid}/share")]` `GetShareAsync(...)` →
  `ApiResult<ShareLinkResponse?>.Success(await shareService.GetActiveAsync(AuthenticatedUser.Id, uuid,
  ct))`. Swagger 200 (data null when none, OQ8a) / 401 / 404 (9000).
- `[HttpDelete("{uuid}/share")]` `RevokeShareAsync(...)` → `await shareService.RevokeAsync(...)`;
  `ApiResult.SuccessMessage(localizer[MessageKeys.Success.ShareLinkRevoked].Value)`. Swagger 200 /
  401 / 404 (9000).

`Controllers/PublicSharesController.cs` — **NEW**, `[AllowAnonymous]`, explicit route override
`[Route("api/v{version:apiVersion}/public/shares")]` on the class (still derives `AppController` for
the `[ResponseWrapped]` envelope + versioning). **Never reads `AuthenticatedUser`.** Injects
`IEventShareService shareService`:
- `[HttpGet("{token}")]` `GetPublicAsync([FromRoute] string token, CancellationToken)` →
  `ApiResult<PublicEventShareResponse>.Success(await shareService.GetPublicAsync(token, ct))`. Swagger
  200 / 404 (16000 unknown/expired/revoked). No 401 (anonymous).
- `[HttpGet("{token}/qr/members")]` `GetPublicMemberQrsAsync([FromRoute] string token,
  CancellationToken)` → `ApiResult<IReadOnlyList<MemberQrResponse>>.Success(await
  shareService.GetPublicMemberQrsAsync(token, ct))`. Swagger 200 (may be empty) / 404 (16000).

`AppController` remains untouched (LOCKED).

### Step 8 — Models, validator, DTOs

`Models/Share/CreateShareLinkRequest.cs` — `{ string? BankAccountUuid }` (+ `bool Regenerate` if the
flag lives in the body per OQ5). Vietnamese XML-doc.
`Models/Share/ShareLinkResponse.cs` — `{ string Token; DateTime ExpiresAt; DateTime CreatedAt; bool
HasQr; string? BankName; string? AccountNumber; string? AccountHolderName }` (the frontend builds the
public URL from `Token`).
`Models/Share/PublicEventShareResponse.cs` — `{ string EventName; DateTime? ClosedAt;
IReadOnlyList<MemberBalanceRow> Rows; IReadOnlyList<PublicExpense> Expenses; decimal TotalOutstanding;
int OwingMemberCount; int SettledMemberCount; bool HasQr }` (reuses `Models/Stats/MemberBalanceRow`).
`Models/Share/PublicExpense.cs` — `{ string Uuid; string Name; string PayerMemberUuid; string
PayerName; DateTime ExpenseTime; decimal Total; IReadOnlyList<PublicShare> Shares }`.
`Models/Share/PublicShare.cs` — `{ string MemberUuid; string MemberName; decimal Amount; bool
IsSettled; string? Note }`.
Reuses `Models/Wallet/MemberQrResponse` for the QR list.

`Validators/Share/CreateShareLinkRequestValidator.cs` — FluentValidation
`AbstractValidator<CreateShareLinkRequest>`; `BankAccountUuid` optional (when present, non-empty /
max length). Minimal — the real bank checks happen in the service (12000).

### Step 9 — Error codes, message keys, localization, config

`Constants/ErrorCodes.cs` — new **16xxx** block (OQ1a):
`ShareLinkNotFoundOrExpired = 16000` (HTTP 404), `EventNotClosedForShare = 16001` (HTTP 400).
`Constants/MessageKeys.cs`:
- `Error.ShareLinkNotFoundOrExpired = "Error.ShareLinkNotFoundOrExpired"`,
  `Error.EventNotClosedForShare = "Error.EventNotClosedForShare"`.
- `Feature.Share = "Feature.Share"`.
- `Success.ShareLinkRevoked = "Success.ShareLinkRevoked"`.
Localization resx (vi-VN + en-US, `Localization/Resources`): add the four keys. Suggested vi:
`Error.ShareLinkNotFoundOrExpired` = "Liên kết chia sẻ không tồn tại hoặc đã hết hạn.";
`Error.EventNotClosedForShare` = "Chỉ có thể chia sẻ đợt đã chốt.";
`Feature.Share` = "chia sẻ đợt (chỉ xem)"; `Success.ShareLinkRevoked` = "Đã thu hồi liên kết chia sẻ."
`appsettings.json` — add `"Share": { "LinkTtlHours": 24 }`.

### Step 10 — HTTP status mapping

Confirm the `ErrorException` → HTTP mapping (`ErrorHandlerMiddleware`) maps `ShareLinkNotFoundOrExpired`
to 404 and `EventNotClosedForShare` to 400 the same way the 12xxx/9xxx codes are mapped (verify the
middleware's code→status table and add entries if it is an explicit switch rather than range-based).

### Step 11 — Documentation

Update `The-ideal.md` §6 note (feature now partially shipped, backend) — keep §5 locks intact. Keep
this planning doc synchronized (Progress Log + Final Outcome).

## Impact Analysis

- **APIs:**
  - NEW `POST api/v1/events/{uuid}/share` (+`?regenerate`) → `ApiResult<ShareLinkResponse>`
    (Premium-gated, closed-only).
  - NEW `GET api/v1/events/{uuid}/share` → `ApiResult<ShareLinkResponse?>`.
  - NEW `DELETE api/v1/events/{uuid}/share` → `ApiResult` (revoke).
  - NEW `GET api/v1/public/shares/{token}` **[AllowAnonymous]** → `ApiResult<PublicEventShareResponse>`.
  - NEW `GET api/v1/public/shares/{token}/qr/members` **[AllowAnonymous]** →
    `ApiResult<IReadOnlyList<MemberQrResponse>>`.
- **Database:** NEW table `event_share_links` (entity `EventShareLink`), migration `AddEventShareLink`.
  FKs `user_id`/`event_id` cascade; unique `uuid`, unique `token`. No money column, no CHECK. No change
  to existing tables.
- **Infrastructure:** Redis key namespace `share:event:{token}` (new `EventShareLinkCache`). No new
  NuGet. CORS unchanged (global policy already covers the new routes) — **confirmed, no change needed**.
- **Services:**
  - NEW `Services/Api/Share/EventShareService.cs` (`IEventShareService`) + `EventShareLinkCache.cs`.
  - `IWalletQrService`/`WalletQrService`: +1 public method `GenerateEventMemberQrsForShareAsync` (+ a
    `BankSnapshot` record); existing methods and private helpers (`CollectEventBillables`,
    `BuildMemberQrsAsync`, `BilledMember`) reused unchanged.
  - `IExpensesService`/`ExpensesService`: +1 method `ListDetailedByEventAsync` (OQ2a).
  - `IExpenseRepository`/`ExpenseRepository`: +1 method `ListDetailedByEventAsync`.
  - NEW `IEventShareLinkRepository`/`EventShareLinkRepository`.
  - No change to `TierService`, `StatsService`, `EventsService`, `BankAccountRepository`,
    `QrImageService`, `qrContentResolver`.
- **Controllers:** `EventsController` (+3 actions, +1 injected service); NEW `PublicSharesController`
  (`[AllowAnonymous]`). `AppController` untouched (LOCKED).
- **Models:** NEW `Models/Share/*` (5 DTOs) + `Validators/Share/CreateShareLinkRequestValidator`; reuse
  `MemberBalanceRow`, `MemberQrResponse`.
- **Documentation:** `The-ideal.md` §6 note; this doc.

## Decision Log

### Decision 1 — Scope: EVENT ONLY (closed events)
Share links cover closed events only; expense-level sharing is a later cycle.
**Reason:** resolved with the user. **Alternatives considered:** also sharing single expenses (deferred).

### Decision 2 — Row drill-in = the member's OWN breakdown
Expanding a member row shows that member's own per-expense breakdown (expenses they have a share in,
amount per expense, plus what they advanced as payer).
**Reason:** resolved with the user.

### Decision 3 — LIVE read
The public payload reflects current state: the closed event's spend figures are frozen, but the
per-member settled/outstanding overlay is recomputed on every read.
**Reason:** resolved with the user. **Alternatives considered:** a frozen snapshot at link creation
(rejected — settled changes must show).

### Decision 4 — Owner controls: revoke + regenerate + view; reuse an unexpired link
The owner can view/copy the active link, revoke it, and regenerate it; creating while an unexpired,
non-revoked link exists reuses that link instead of minting a duplicate.
**Reason:** resolved with the user. (Regenerate verb and reuse-vs-bank-change refined in OQ5/OQ6.)

### Decision 5 — Premium-gated at creation; anonymous view never re-gated
Creation requires Premium (§3.11) and a closed event (§4.4, mirroring the closed-only event QR code
12002). The anonymous view/QR is never re-gated, incl. after a downgrade (§4 rule 9).
**Reason:** resolved with the user + spec.

### Decision 6 — Store the token VALUE, not a hash
The token is stored as its plain value (unique-indexed) so the link is re-displayable ("view/copy the
active link"). It is a low-sensitivity, read-only, 1-day capability.
**Reason:** resolved with the user. **Alternatives considered:** hash-only + show-once (rejected — the
owner must be able to re-copy the link).

### Decision 7 — Snapshot the bank fields onto the link
BankBin/BankName/AccountNumber/AccountHolderName are copied onto the link at creation; BankAccountUuid
is kept as a soft reference. The QR stays stable if the wallet account is later edited or hard-deleted.
**Reason:** resolved with the user (bank accounts are hard-deleted — no live FK to depend on).

### Decision 8 — ErrorCodes 16xxx (proposed)
Claim a new **16xxx** block for share-link codes because 15xxx is already reserved by
settled-per-member. **Pending user confirmation (Open Question 1).**

### Decision 9 — Detailed-by-event read for the breakdown (proposed)
Add `ListDetailedByEventAsync` (repo + service) rather than N+1 `GetAsync` calls or reshaping the
existing summary. **Pending user confirmation (Open Question 2).**

### Decision 10 — CSPRNG token (proposed)
Mint the token via the shipped CSPRNG pattern rather than `Uuid.NewV7()`, for unguessability of a
public capability URL. **Pending user confirmation (Open Question 3).**

### Orchestrator resolutions (2026-07-24)

The orchestrator resolved all 8 Open Questions. These are binding and were implemented verbatim:

1. **OQ1 → (a) 16xxx block.** 15xxx stays reserved by settled-per-member (no renumbering of existing
   codes). New codes: `ShareLinkNotFoundOrExpired = 16000` (HTTP 404),
   `EventNotClosedForShare = 16001` (HTTP 400). Adds `MessageKeys.Error.*`,
   `MessageKeys.Feature.Share`, `MessageKeys.Success.ShareLinkRevoked`, and vi/en resx entries.
2. **OQ2 → (a).** New `IExpensesService.ListDetailedByEventAsync(userUuid, eventUuid, ct)` →
   `IReadOnlyList<ExpenseResponse>` backed by `IExpenseRepository.ListDetailedByEventAsync`, one
   resource-owned query Including Category + PayerMember + Shares.ThenInclude(Member) +
   ExpenseTags.ThenInclude(Tag) + Event (mirrors `GetByUuidAsync`, filtered by event, ordered
   `ExpenseTime` DESC). No N+1.
3. **OQ3 → (b) CSPRNG.** Token minted via the exact `TokenService.NewRawToken()` pattern
   (`WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))`, 256-bit URL-safe). Stored as
   the PLAIN value (re-displayable), unique-indexed; column `VARCHAR(64)`.
4. **OQ4 → (b) bank optional.** If a `bankAccountUuid` override is given, resolve it (miss →
   `BankAccountNotFound` 12000); else use the default account if one exists; if NO account at all,
   create the link WITHOUT a bank snapshot (`hasQr = false`). Snapshot columns are NULLABLE. The
   public QR endpoint tolerates a null snapshot → returns an empty list.
   `GenerateEventMemberQrsForShareAsync` handles a null/absent snapshot at the service layer.
5. **OQ5 → (b) regenerate flag.** `POST {uuid}/share` with a `regenerate` flag on
   `CreateShareLinkRequest` (request body). Without it, reuse the active link; with it, revoke the
   active link + mint a fresh one.
6. **OQ6 → (a) reuse-unchanged.** While an active link exists and `regenerate` is false, reuse it
   unchanged and IGNORE a differing `bankAccountUuid` (changing the bank requires regenerate).
7. **OQ7 → (a) fixed TTL.** `ExpiresAt` stays anchored to original creation; reuse/GET never extends
   it.
8. **OQ8 → (a) 200 null.** `GET {uuid}/share` with no active link returns `200`
   `ApiResult<ShareLinkResponse?>.Success(null)` (a normal "not shared yet" state), not a 404.

Plus: add `hasQr` (bool) to `PublicEventShareResponse` reflecting whether a bank snapshot exists.
Premium gate (`tierService.EnsurePremiumFeature(MessageKeys.Feature.Share)`) applies at creation
ONLY (never on the public read/QR). Closed-only enforced at creation via the M7 balance `IsClosed`
(else 16001). `appsettings.json`: `Share:LinkTtlHours = 24`. EF migration `AddEventShareLink`.

## Progress Log

### 2026-07-24

- Created planning doc. Read The-ideal.md §3.10/§3.11/§4/§5/§6; CLAUDE.md, AGENTS.md,
  .agents/rules/rules.md, .claude/rules/rule.md; existing planning docs (per-member-qr-sharing,
  cors-configuration).
- Verified against live code: `AuthToken`(+Partial), `TokenWhitelistStore`, `TokenService`,
  `WalletQrService` (private helpers + gating order), `StatsService`/`EventBalanceResponse`/
  `MemberBalanceRow`, `ExpensesService`/`ExpenseResponse`/`ExpenseSummaryResponse`/`ExpenseFilter`/
  `ExpenseRepository` includes, `EventResponse`/`Event` entity, `BankAccountRepository`/`BankAccount`,
  `TierService.EnsurePremiumFeature`, `BaseRepository`, `AppDbContext`, `AppController`,
  `HealthController`/`AuthController` `[AllowAnonymous]`, `Program.cs` FallbackPolicy, `ErrorCodes`,
  `MessageKeys`.
- Found two brief assumptions that do not match the code and raised them as Open Questions:
  (a) `ExpensesService.ListAsync` returns summaries WITHOUT per-share detail (OQ2); (b) `ErrorCodes`
  15xxx is already reserved by settled-per-member, so 16xxx is the next free block (OQ1). Also raised
  token-generator, bank-required-vs-optional, regenerate-verb, reuse-vs-bank-change, TTL, and
  GET-empty questions (OQ3–OQ8).
- Drafted the full implementation plan, impact analysis, and decision log. Awaiting the checkpoint on
  the Open Questions before implementation.
- Orchestrator resolved all 8 Open Questions (see Decision Log → Orchestrator resolutions
  (2026-07-24)). Began implementation against those resolutions.
- Implemented: entity `EventShareLink` (+ partial + AppDbContext DbSet/ConfigureModel);
  `IEventShareLinkRepository`/`EventShareLinkRepository`; `EventShareLinkCache` (Redis, mirrors
  `TokenWhitelistStore`); `IEventShareService`/`EventShareService`; `WalletQrService.
  GenerateEventMemberQrsForShareAsync` (+ `BankSnapshot` record); `IExpenseRepository`/
  `IExpensesService.ListDetailedByEventAsync`; 5 Share DTOs + `CreateShareLinkRequestValidator`;
  3 new actions on `EventsController` + new `[AllowAnonymous]` `PublicSharesController`; error codes
  16000/16001, message keys, vi/en resx, `Share:LinkTtlHours` config, and the `ErrorException`
  status mapping. EF migration `AddEventShareLink` authored offline.

#### Test results (test-engineer, 2026-07-24)

- Added the full test list from the **Tests** section (73 new tests) across 6 files; the whole suite
  is **1364 passed, 0 failed, 0 skipped** (up from the 1291 baseline). All integration tests RAN
  (real MariaDB + Redis reachable) — nothing skipped. No production code changed; no product bug found.
- **`EventShareServiceTests.cs`** (24, pure unit — fakes for repo/stats/events/expenses/bank-account/
  wallet-QR/tier, the real validator, and a REAL `EventShareLinkCache` wired over the fake repo +
  unreachable Redis so every cache op degrades to the DB fallback). Proves: create ordering (Premium
  13003 FIRST, before event resolution; then closed-only 16001; event-miss 9000); bank snapshot
  (explicit-miss 12000; no default/override ⇒ `HasQr` false; default ⇒ snapshot copied + `ExpiresAt`
  = now+24h fixed TTL); reuse (same token, no new row, unchanged expiry) vs `regenerate` (old
  soft-revoked, fresh token); GetActive (returns active / null when none / 9000 on ownership miss);
  Revoke (sets `RevokedAt` / idempotent / 9000); GetPublic (16000 for unknown/expired/revoked; valid ⇒
  live payload with rows + per-`PublicShare` breakdown + counts + `HasQr`, resolved with the OWNER uuid
  from the token; a re-read reflects a changed settled overlay); GetPublicMemberQrs (no snapshot ⇒
  empty, no wallet-QR call; with snapshot ⇒ delegates with owner uuid + snapshot; unknown ⇒ 16000).
- **`WalletQrServiceTests.cs`** (+6, appended). `GenerateEventMemberQrsForShareAsync`: NO Premium gate
  (renders even when the tier double would 13003); builds the transient `BankAccount` from the snapshot
  (payload `38>01>00` BIN + `01` account number match the snapshot, not a differing DB account); one
  entry per `Outstanding > 0` row in order (parity with `CollectEventBillables`); open event ⇒ 16001
  (defensive); nobody owes ⇒ empty list (softened 12003); image decodes to the PNG magic.
- **`CreateShareLinkRequestValidatorTests.cs`** (6): optional `BankAccountUuid` (null / at-max / with
  `Regenerate` OK; empty and over-max rejected).
- **`EventShareLinkCacheTests.cs`** (9, integration — MariaDB + Redis, mirrors `TokenWhitelistStoreTests`):
  Redis-down DB fallback carrying owner/event/snapshot; null-snapshot row resolves with null bank
  fields; unknown/revoked/expired ⇒ null (never cached); `AddAsync` warn-and-continue on Redis-down;
  DB-fallback backfill (TTL = remaining lifetime, 23–24h); cache-first read after the DB row is deleted;
  `RemoveAsync` evicts the key.
- **`EventShareEndpointTests.cs`** (19, integration — `POST/GET/DELETE {uuid}/share`): 200 with token +
  `hasQr`; Free ⇒ 403 13003 (and gate-before-event-resolution on an unknown event); open ⇒ 400 16001;
  bad bank ⇒ 404 12000; no wallet ⇒ 200 `hasQr=false`; reuse ⇒ same token; `regenerate` ⇒ new token +
  old 404s on the public route; foreign/unknown event ⇒ 404 9000; anonymous ⇒ 401; GET active / 200
  `data:null` when not shared / 9000 foreign / 401; DELETE revokes (public GET ⇒ 404 16000) / idempotent
  / 9000 / 401.
- **`PublicShareEndpointTests.cs`** (9, integration — `[AllowAnonymous]` routes, NO auth header): public
  report 200 (not 401) with rows + per-expense breakdown + `hasQr`; `hasQr=false` when no wallet;
  unknown/revoked ⇒ 404 16000; LIVE overlay (owner marks a member settled ⇒ re-GET reflects the changed
  `outstanding`/`isSettled`/counts); per-member QR list 200 (PNG data URLs), empty when nobody owes or
  no snapshot, unknown ⇒ 404 16000.
- Extra edge cases beyond the listed checklist: create-then-resolve-by-token (proves the row is
  publicly resolvable), null-snapshot cache row resolution, and the explicit gate-before-event-resolution
  ordering test on a non-existent event.

## Final Outcome

(pending)

## Future Improvements

- Abuse controls on the anonymous routes (per-IP / per-token rate limiting) — the public report and
  QR endpoints are unauthenticated; consider a lightweight limiter if they see abuse.
- Optional link analytics (last-viewed timestamp, view count) if owners want visibility.
- Extend sharing to single expenses (Decision 1 deferred scope) and configurable TTL per link.
- A leaner public member row (hide `Advanced/Owed/Balance` internals) if the shared report should
  reveal less than the owner's balance view.
- Opportunistic purge of expired/revoked `event_share_links` rows (mirror `DeleteExpiredAsync` in the
  auth token repo) if the table grows.

## Tests (for the test-engineer)

Reuse the shipped harness; DB/endpoint tests are `[SkippableFact]` against real MariaDB (never EF
InMemory), each inside a rolled-back transaction; lightweight fakes for collaborators.

**Unit — `EventShareServiceTests` (fakes for repo/cache/stats/events/expenses/bankAccount/walletQr/
tier):**
- Create: Free caller → 13003 before any resolution; open event → 16001; event miss → 9000; explicit
  bank override miss → 12000; no default + no override → link created with `HasQr = false` (OQ4b);
  default/override present → snapshot copied (BankBin/Name/Number/Holder), token + `ExpiresAt = now +
  TtlHours`, cache `AddAsync` called after commit.
- Reuse: an active link exists → `CreateAsync` returns the SAME token, no new row (Decision 4, OQ6a),
  same `ExpiresAt` (fixed TTL, OQ7a). `regenerate = true` → old link revoked (cache removed) + fresh
  token minted.
- GetActive: returns the active link; none → null (OQ8a); ownership miss → 9000.
- Revoke: sets `RevokedAt`, evicts cache; idempotent when no active link.
- GetPublic: unknown/expired/revoked token → 16000; valid → live payload with `EventName`, `ClosedAt`,
  `Rows` (from balance), `Expenses` with per-`PublicShare` breakdown (member/amount/settled/note),
  `TotalOutstanding`/`OwingMemberCount`/`SettledMemberCount`, `HasQr` reflecting the snapshot. LIVE:
  after a member is marked settled, a second `GetPublicAsync` reflects the changed overlay.
- GetPublicMemberQrs: no snapshot → empty list; with snapshot → delegates to
  `GenerateEventMemberQrsForShareAsync`; unknown token → 16000.

**Unit — `WalletQrServiceTests` (+):**
- `GenerateEventMemberQrsForShareAsync`: NO Premium gate (a Free/anonymous-owner path still renders);
  builds a transient `BankAccount` from the snapshot (payload `54`/account fields match the snapshot,
  not any DB account); billed set == `CollectEventBillables` (parity with `GenerateEventMemberQrsAsync`
  for the same seeded balance); open event → 16001 (defensive); nobody owes → empty list (share
  softening, not 12003); each `Image` a `data:image/png;base64,` PNG data URL.

**Unit — `EventShareLinkCacheTests`:** add/lookup/remove; TTL = `ExpiresAt - now`; DB fallback on a
cache miss (backfills); revoked/expired row → null (not cached); warn-and-continue on a Redis fault.

**Unit — `CreateShareLinkRequestValidatorTests`:** optional `BankAccountUuid` (null OK; over-max
rejected).

**Integration — `EventShareEndpointTests` (`[SkippableFact]`, real MariaDB):**
- `POST {uuid}/share`: Premium + closed event → 200 with `token`/`expiresAt`/`hasQr`; Free → 403
  13003; open event → 400 16001; explicit bad bank → 404 12000; no wallet account → 200 `hasQr=false`
  (OQ4b); reuse returns the same token; `?regenerate=true` returns a new token and 404s the old one on
  the public route; foreign/unknown event → 404 9000; anonymous → 401.
- `GET {uuid}/share`: returns the active link (200, or `data:null` when none, OQ8a); foreign event →
  404 9000; anonymous → 401.
- `DELETE {uuid}/share`: revokes (subsequent public GET → 404 16000); idempotent; foreign → 404 9000.

**Integration — `PublicShareEndpointTests` (`[AllowAnonymous]`, real MariaDB):**
- `GET public/shares/{token}` with **no auth header** → 200 wrapped `PublicEventShareResponse` (NOT
  401) — proves the anonymous route and that `AuthenticatedUser` is never read; payload has rows +
  per-expense breakdown; `hasQr` correct.
- Unknown / expired / revoked token → 404 16000.
- LIVE overlay: mark a member settled (authed) then re-GET the public report → the member's
  `Outstanding`/`isSettled` reflects the change.
- `GET public/shares/{token}/qr/members` → 200 list of PNG data URLs (one per debtor), empty list when
  nobody owes or `hasQr=false`; unknown token → 404 16000; works with no auth header.

### 2026-07-24 — review closure (orchestrator)

- **Backend code review: clean, no blocking findings.** Applied the two actionable nits:
  - N1 — extracted `IEventShareLinkCache` and register/inject via the interface (matches the
    `ITokenWhitelistStore` DI precedent); `EventShareService` now depends on the interface.
  - N3 — added `.AsSplitQuery()` to `ExpenseRepository.ListDetailedByEventAsync` to avoid a
    Shares×Tags cartesian blow-up on the anonymous public read.
  - N2 (revoked-link cache window if the Redis DELETE fails) left as-is by design — mirrors the
    accepted `TokenWhitelistStore` warn-and-continue tradeoff for a ≤24h read-only capability.
- Migrations applied to the local DB (`AddPerMemberSettlement` + `AddEventShareLink`).
- Final: `dotnet build` 0 errors; full suite **1364 passed, 0 failed, 0 skipped** (integration tests
  ran against the real MariaDB+Redis). The message-key count guard was synced 129 → 133.
