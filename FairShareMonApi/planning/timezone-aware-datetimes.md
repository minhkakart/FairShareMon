# Timezone-aware DateTimes (store UTC, present in the viewer's timezone)

## Objective

Make every `DateTime` the API emits and accepts consistent across any viewer locale while keeping
**UTC as the single storage model**. An instant persisted as `00:00:00Z` must render as
`07:00:00+07:00` to a viewer in UTC+7 — the same absolute moment, projected into the viewer's zone.

The API keeps its own configured default timezone but **prefers the client's timezone supplied
per-request via a header**, falling back to the default when the header is absent or invalid. This is
the classic *store-UTC / present-per-request-zone* pattern. FairShareMon explicitly **retains UTC
storage** and does **not** adopt quick-ordering's `+7`-local storage model.

Concretely:
- Outbound: all response `DateTime`s serialize as ISO-8601 with the viewer-zone offset
  (e.g. `2026-07-14T07:00:00+07:00`).
- Inbound: a datetime **without** an offset is interpreted in the request's timezone, then converted
  to UTC for storage/comparison; a datetime **with** an offset is honored as sent.

This is a cross-cutting foundation change with no new user feature and (as analyzed below) **no schema
migration**.

## Background

Current behavior verified against live code:

- **Clock:** `Utils/AppDateTime.cs` → `Now => DateTime.UtcNow` (UTC; decided M2 OQ10). Kept as-is.
- **DbContext:** `Database/AppDbContext.cs` has no `ConfigureConventions`, no value converters, no
  DateTime configuration; `Database/AppDbContext.partial.cs` is reserved for query filters only.
  `CreatedAt` is app-set (`AppDateTime.Now`, UTC); `UpdatedAt` is DB-generated
  (`ValueGeneratedOnAddOrUpdate` + `HasDefaultValueSql("current_timestamp(6) ON UPDATE
  current_timestamp(6)")`) under the **unpinned** MySQL session timezone — so DB-generated timestamps
  are only UTC if the server session happens to be UTC.
- **Pomelo materialization:** DB `datetime(6)` columns come back as `DateTimeKind.Unspecified`. This
  is the root of the M5 audit representation-drift bug that `AuditSnapshotCanonicalizer.Utc` works
  around by re-labelling the value `Kind.Utc` (`Services/Audit/AuditSnapshots.cs`).
- **Program.cs:** `AddDbContextPool<AppDbContext>(... UseMySql ...)` with **no interceptors**;
  `AddHttpContextAccessor()` **is** registered; `AddControllers(...)` has **no** `AddJsonOptions`
  (default System.Text.Json, so `DateTime` serializes round-trip-ISO with `Kind.Unspecified` → no
  offset today).
- **Export (M8):** `Services/Api/Export/ExportValueFormatter.cs` hardcodes `VietnamOffset = +7`;
  `FormatInstant` shifts UTC→+7, `FormatCalendarDate` takes the UTC date part with no shift (to avoid
  pushing a `23:59:59.999Z` end-of-day boundary into the next calendar day).
- **Event ranges (M6):** `Repositories/EventRepository.cs` `NormalizeStart`/`NormalizeEnd` compute a
  whole-day **UTC** window (`date.Date` .. `date.Date.AddDays(1).AddTicks(-10)`); within-range checks
  live in `ExpenseRepository.IsWithinRange` and `EventRepository.UpdateAsync`. The M6 OQ1 decision
  accepted a "UTC-day boundary" caveat and listed a per-timezone refinement as a Future Improvement —
  this feature is that refinement.
- **Tier window (M10):** `Services/Api/Tiers/TierService.cs` `CurrentMonthUtcWindow()` computes the
  "expenses this month" limit against a fixed `+7` month boundary.
- **Request-context pattern:** `Auth/IContextAuthenticated.cs` is a scoped accessor over
  `IHttpContextAccessor` — the exact pattern to mirror for a request-timezone accessor.
- **Inbound `DateTime` request fields:** `ExpenseTime` (`Models/Expenses/CreateExpenseRequest.cs`,
  `UpdateExpenseRequest.cs`), event `StartDate`/`EndDate` (`Models/Events/CreateEventRequest.cs`,
  `UpdateEventRequest.cs`), stats `From`/`To` (`Models/Stats/StatsRangeRequest.cs`,
  `ByCategoryStatsRequest.cs`), admin `From`/`To` (`Models/Admin/RevenueRequest.cs`,
  `AdminMetricsRequest.cs`). Exploration counts ~30 `DateTime`/`DateTime?` properties across 11
  entities and the response DTOs above.
- **quick-ordering reuse:** `QuickOrdering/Extensions/DateTimeExtension.cs` +
  `QuickOrdering/Configs/Options/AppSettings.cs` show a cross-platform IANA/offset zone resolver
  (`FindTimeZonesByOffset`, `GetPreferredIanaZone`, Windows-vs-Linux id handling) worth reusing for
  parsing the header/default into a `TimeZoneInfo`. We reuse **only** that resolution logic — never its
  `+7`-local `AppDateTime.Now` nor its `SET time_zone='+07:00'` interceptor value.

## Requirements

1. Storage stays UTC; DB columns remain `datetime(6)` holding UTC instants; the offset is
   presentation-only (locked, see Assumptions).
2. A per-request timezone header accepts **either** an IANA zone id (preferred, DST-aware) **or** a
   numeric UTC offset; missing/invalid → fall back to a configured app-default zone (locked).
3. Responses emit ISO-8601 in the viewer's zone **with offset** (locked).
4. Inbound naive datetimes are interpreted in the request's timezone, then converted to UTC;
   offset-bearing input is honored as sent (locked).
5. DB-generated `UpdatedAt` must be true UTC regardless of the server's OS/session timezone.
6. Materialized `DateTime`s must carry `Kind.Utc` so downstream conversions are correct and the M5
   audit no-op detection stays robust without the `AuditSnapshotCanonicalizer.Utc` workaround.
7. M8 export must format instants/calendar dates in the resolved request zone (default when
   headerless) instead of the hardcoded `+7`.
8. The change must not break the `[ResponseWrapped]` / `ApiResult<T>` envelope, and must not touch the
   locked `Controllers/AppController.cs`.
9. All new user-facing messages (if any) in Vietnamese.

## Open Questions

> **All resolved at the 2026-07-14 checkpoint — every one to the recommended option (a).** Retained
> below with the original options for traceability; consolidated in the Decision Log. Nothing here
> reopened a locked decision.

### OQ1 — Header name, config key, and invalid-header handling

> ~~**OQ1**~~ → **Answered 2026-07-14 (option a):** single `X-Time-Zone` header accepting an IANA id
> (preferred) OR a numeric UTC offset; config `App:DefaultTimeZone` (default `Asia/Ho_Chi_Minh`);
> invalid/missing → **silent fallback** to the default (no 400, no new error code).

What is the request header name, the config key/default for the fallback zone, and what happens on an
unparseable/unknown value?

- **(a) [recommended]** A single header `X-Time-Zone` that accepts **either** an IANA id
  (`Asia/Ho_Chi_Minh`) **or** an offset (`+07:00`, `+7`); config key `App:DefaultTimeZone` with
  default `Asia/Ho_Chi_Minh`; an invalid/unknown value **silently falls back to the default** (never
  fails the request).
  - Trade-off: most forgiving, zero client friction, matches quick-ordering's resolver; downside is a
    typo'd zone silently renders in the default zone rather than surfacing an error.
- **(b)** Same header/config, but an invalid value returns **400** (validation error 1001) so clients
  learn about typos.
  - Trade-off: stricter contract; but a bad/absent header on every legacy client would break requests,
    and timezone is a presentation concern, not data validity.
- **(c)** Two separate headers (`X-IANA-TimeZone` and `X-UTC-Offset`).
  - Trade-off: unambiguous parsing; but more surface area and client complexity for no real gain over
    "accept both in one header".

New app config block also needs deciding: introduce an `App` section (`App:DefaultTimeZone`) in
`appsettings.json`. Recommended alongside (a).

### OQ2 — Output mechanism: global converter over `DateTime` vs switch DTOs to `DateTimeOffset`

> ~~**OQ2**~~ → **Answered 2026-07-14 (option a):** keep response DTOs as `DateTime` + a global
> `JsonConverter<DateTime>`/`<DateTime?>` emitting ISO-8601 with the viewer-zone offset; do NOT switch
> DTOs to `DateTimeOffset`.

- **(a) [recommended]** Keep response DTO properties as `DateTime` and register a global
  `JsonConverter<DateTime>` (+ nullable) that treats the value as UTC, converts to the request zone,
  and writes ISO-8601 **with offset**. Least churn — no DTO edits, no AutoMapper profile changes.
  - Trade-off: the type stays `DateTime` while the wire format carries an offset — slightly surprising
    to a code reader, but centralized and consistent.
- **(b)** Change every response DTO `DateTime` to `DateTimeOffset` and project the offset in each
  mapping.
  - Trade-off: the type honestly reflects the offset; but touches ~dozen DTOs + AutoMapper profiles and
    each place that constructs the offset, far more churn and error surface.

### OQ3 — M6 event whole-day ranges under a per-request timezone

> ~~**OQ3**~~ → **Answered 2026-07-14 (option a):** event whole-day ranges are interpreted **in the
> request timezone → UTC bounds** (a +7 client's "Jul 1–3" stores `Jun 30 17:00:00Z ..
> Jul 03 16:59:59.999999Z`). `NormalizeStart/NormalizeEnd` become tz-aware (compute the day boundaries
> in the resolved request zone, then convert to UTC); create/assign-into-event and the
> expense_time-within-range checks all use the resolved zone consistently while comparing raw UTC
> instants. **This supersedes the accepted M6 OQ1 "whole-day-inclusive UTC" caveat** — the M6
> events.md OQ1 note is now refined (see Decision Log + Impact).

Today `NormalizeStart/End` build whole-day bounds in **UTC**; the M6 OQ1 "UTC-day boundary" caveat was
accepted precisely because there was no per-user timezone. This feature introduces one. How should an
event's whole-day range be interpreted now?

- **(a) [recommended]** Interpret a client's whole-day range as whole days **in the request's
  timezone**, then convert to UTC bounds for storage/comparison. So a `2026-07-14` start from a `+7`
  client stores `2026-07-13T17:00:00Z` and end `2026-07-14` stores `2026-07-14T16:59:59.999999Z`;
  `IsWithinRange` continues to compare the UTC `expense_time` against these UTC bounds.
  `NormalizeStart/NormalizeEnd` become timezone-aware (take the request zone), and the within-range
  checks in `ExpenseRepository`/`EventRepository` are unchanged (still raw UTC comparison — the bounds
  already encode the zone). This **supersedes/refines** the M6 OQ1 UTC-day decision, delivering exactly
  the Future Improvement events.md listed.
  - Trade-off: correct for the common single-zone user; but the stored bounds now depend on the zone of
    the request that created/edited the event. A later edit from a different zone re-normalizes the
    bounds to that zone's days (acceptable — the edit is an explicit rewrite). Whole-day CHECK
    `ck_events_date_range` still holds (end >= start). Documented as the resolution of the OQ1 caveat.
- **(b)** Keep event ranges as **UTC** whole-days (status quo), only changing presentation.
  - Trade-off: zero behavioral risk to M6; but keeps the accepted-but-wrong "expense near local
    midnight lands in the adjacent day" caveat and contradicts the whole point of this feature for the
    one place a day boundary actually matters.
- **(c)** Interpret event days always in the **app-default** zone (not the request zone).
  - Trade-off: stable/deterministic regardless of who edits; but a user genuinely in a non-default zone
    sees off-by-a-day ranges. Reasonable only if the product is single-region.

Note: whichever is chosen, the event `StartDate`/`EndDate` are **calendar dates**, so on **output**
they must render as the same calendar day the user picked (see OQ6's calendar-date handling) — the
converter/formatter must not shift a stored `16:59:59.999999Z` end boundary into the next day.

### OQ4 — M10 tier month-window and any policy dates: request zone vs app-default zone

> ~~**OQ4**~~ → **Answered 2026-07-14 (option a):** the tier "expenses this month" window is computed
> in the **app-default zone** (`App:DefaultTimeZone`), NOT the request header — a usage quota is server
> policy and a header-driven window would be gameable across a month boundary. Behavior for the default
> VN config is unchanged, but now config-driven and explicitly policy-scoped.

The Free-tier "N expenses per calendar month" limit (`TierService.CurrentMonthUtcWindow`) currently
uses a fixed `+7` month. Under per-request timezones, whose month defines the window?

- **(a) [recommended]** Use the **app-default** zone for the tier month window (and any other
  server-side *policy* date), **not** the request zone. Rationale: the monthly quota is a server
  policy, and keying it off a client-supplied header is **gameable** — a user could flip `X-Time-Zone`
  around a month boundary to reset their quota. Policy dates must be deterministic and
  attacker-independent.
  - Trade-off: a genuinely non-default-zone user's "month" may differ slightly from their local month
    at the boundary; acceptable for a coarse monthly quota and far safer than a gameable window.
- **(b)** Use the request zone for the month window (consistent with display).
  - Trade-off: matches the user's local month, but is gameable and non-deterministic — rejected for a
    quota.

### OQ5 — Stats / revenue / admin `from`/`to` naive inputs

Confirm these follow the **global inbound rule**: a naive `from`/`to` is interpreted in the request
zone → UTC, an offset-bearing one is honored as sent, then compared against UTC columns.

> ~~**OQ5**~~ → **Answered 2026-07-14 (option a):** stats/overview/by-category/revenue/admin-metrics
> `from`/`to` naive inputs follow the global inbound rule (naive → request zone → UTC); aggregation
> buckets remain UTC-deterministic.

- **(a) [recommended]** Yes — they go through the same inbound `JsonConverter<DateTime?>`, so no
  per-endpoint code and behavior is uniform with expense/event inputs. The existing "inclusive
  `[from,to]`, `from>to` → 1001" validation is unchanged (it runs on the resulting UTC values).
  - Trade-off: a report "from 2026-07-01" now means midnight in the caller's zone, which is what a user
    intends; the only subtlety is that revenue/metrics buckets (`month`/`day`) are still bucketed on
    UTC values server-side — flag whether buckets should also be request-zone-bucketed (likely out of
    scope; recommend leaving bucket math UTC-based for determinism, consistent with OQ4).
- **(b)** Treat admin/revenue `from`/`to` as UTC literally (ignore request zone) since admin dashboards
  are operator tools.
  - Trade-off: simpler mental model for operators; but inconsistent with the global rule and surprising
    if an admin is in a non-UTC zone.

### OQ6 — Serializer converter reaching the scoped request timezone

The System.Text.Json converter registered via `AddJsonOptions` is effectively a **singleton**, but the
resolved `TimeZoneInfo` is **per-request**. How does the converter read it?

> ~~**OQ6**~~ → **Answered 2026-07-14 (option a):** `RequestTimeZoneMiddleware` resolves the zone ONCE
> into `HttpContext.Items`; the singleton STJ converters read it via `IHttpContextAccessor`; a scoped
> `IRequestTimeZone` exposes the same resolved `TimeZoneInfo` to services (export, event/tier logic).
> Composes with the `[ResponseWrapped]`/`ApiResult<T>` envelope; the M8 CSV `FileContentResult` path
> bypasses the JSON converters and applies the resolved zone directly in `ExportValueFormatter`.

- **(a) [recommended]** A lightweight middleware resolves the zone once per request (parse header →
  `TimeZoneInfo`, fallback to default) and stashes it in `HttpContext.Items["RequestTimeZone"]`; the
  converter reads it via an injected `IHttpContextAccessor`. A scoped `IRequestTimeZone` accessor
  (mirroring `IContextAuthenticated`) exposes the same resolved value to services (TierService, export,
  event normalization) reading from the same `HttpContext.Items` slot so header parsing happens once.
  - Trade-off: two entry points (middleware + accessor) but a single resolution path; clean, testable,
    and the converter stays stateless. Falls back to the default zone when there is no `HttpContext`
    (e.g. background services) so serialization never throws.
- **(b)** No middleware; the converter and the scoped accessor both resolve lazily from the header on
  first use and cache on `HttpContext.Items`.
  - Trade-off: one fewer pipeline component; but resolution logic is invoked from two types and the
    ordering (who caches first) is less obvious.

Also confirm within (a): this works with `[ResponseWrapped]`/`ApiResult<T>` (the envelope is itself
serialized by the same options, so nested `DateTime`s convert correctly), and that the M8
`FileContentResult` (CSV) **bypasses** the JSON converter entirely — export must apply the request zone
**directly** via the formatter (CSV is not JSON), see Implementation Plan step 8.

### OQ7 — Migration: confirm NONE required

This is pure runtime behavior (session pinning, value converters applied by convention, JSON
converters, header resolution). No column type/nullability/default changes; `datetime(6)` stays. The
convention-applied `ValueConverter<DateTime,DateTime>` is a no-op on the write side (stores the same
UTC value) and does not alter the model snapshot's column definitions.

> ~~**OQ7**~~ → **Answered 2026-07-14 (option a):** **NO EF migration.** The implementer must verify
> `dotnet ef migrations add` would produce an EMPTY diff (i.e. do not create one) and STOP + report if
> EF unexpectedly wants a schema change.

- **(a) [recommended]** Confirm **no EF migration** is created. Verify after wiring that
  `dotnet ef migrations add` would produce an empty diff; if EF unexpectedly wants a migration (e.g.
  the converter perturbs the snapshot), stop and raise it rather than committing an empty/no-op
  migration.

### OQ8 — Error codes: confirm none needed

With OQ1(a) (invalid header → silent fallback) there is no new failure path, so no new `ErrorCodes`
entry and no new Vietnamese error message. Confirm. (If OQ1(b) is chosen instead, we reuse the existing
validation code 1001 with a Vietnamese message key — see Impact.)

> ~~**OQ8**~~ → **Answered 2026-07-14 (option a):** no new error codes/messages — an invalid/missing tz
> silently falls back to the default.

## Assumptions

**Locked decisions inherited from the task brief (confirmed, do NOT reopen):**

1. **Storage stays UTC** — DB columns remain `datetime(6)` holding absolute UTC instants; the offset is
   presentation-only. `AppDateTime.Now = DateTime.UtcNow` is kept.
2. **Client timezone header accepts BOTH** an IANA zone id (preferred, DST-aware) **or** a numeric UTC
   offset; missing/invalid → configured app-default zone.
3. **Responses = ISO-8601 in the viewer's zone WITH offset** (e.g. `2026-07-14T07:00:00+07:00`).
4. **Inbound datetimes without an offset = interpreted in the request's timezone** then converted to
   UTC; offset-bearing input honored as sent.

**Working assumptions (confirmed at the 2026-07-14 checkpoint):**

- `AppController.cs` is not modified (locked file rule).
- The convention-based value converter on `DateTime` is safe to apply to all mapped `DateTime`
  properties (all hold UTC today); no entity stores a naive local time.
- One-time caveat: pinning the MySQL session to `+00:00` changes how **pre-existing** DB-generated
  `UpdatedAt` rows read relative to **new** ones if the server was previously running an offset session.
  The dev DB is disposable so this is negligible; flag it for any real/staging environment so the
  operator understands the discontinuity (no data migration proposed).

## Implementation Plan

Two layers: **A. Foundation (UTC correctness)** then **B. Presentation (per-request zone)**.

### A. Foundation — UTC correctness

1. **Session-pinning connection interceptor.** Create
   `Database/UtcSessionTimeZoneInterceptor.cs` — a `DbConnectionInterceptor` (mirrors quick-ordering's
   `DbTimeZoneInterceptor` shape) that runs `SET time_zone = '+00:00'` in `ConnectionOpened` /
   `ConnectionOpenedAsync`. Annotate `[SingletonService]`. Wire it in `Program.cs` by adding
   `.AddInterceptors(...)` to the existing `AddDbContextPool<AppDbContext>(...)` call (resolve the
   singleton, or register the interceptor type and add via the options builder's service provider).
   Result: DB-generated `UpdatedAt` (`current_timestamp(6)`) is true UTC regardless of OS/session tz.
   This is the UTC-correct analog of quick-ordering's `+07:00` interceptor — **use `+00:00`, never
   `+07:00`**.

2. **Global read-side Kind=Utc normalization.** Add a `ConfigureConventions` override in
   `Database/AppDbContext.cs` (allowed — it is model configuration, not a query filter, so it does not
   belong in `AppDbContext.partial.cs`). Register a `ValueConverter<DateTime,DateTime>` (read →
   `DateTime.SpecifyKind(v, DateTimeKind.Utc)`, write → pass-through) plus the nullable variant via
   `configurationBuilder.Properties<DateTime>().HaveConversion<...>()` and
   `Properties<DateTime?>()`. Place the converter class in
   `Database/Conversions/UtcDateTimeConverter.cs`. This makes all ~30 mapped `DateTime` properties
   materialize as `Kind.Utc`, fixing the M5 Pomelo-Unspecified root cause globally.

3. **Drop the audit `Utc` workaround.** In `Services/Audit/AuditSnapshots.cs`, remove
   `AuditSnapshotCanonicalizer.Utc(...)` usage in `ExpenseAuditSnapshot.From` (use
   `expense.ExpenseTime` directly, now already `Kind.Utc`) and delete the `Utc` method; **keep**
   `.Money`. The no-op JSON-equality detection in `Services/Audit/AuditLogFactory.cs` still holds
   because both before/after values are now uniformly `Kind.Utc`. (Verify via the existing audit tests.)

### B. Presentation — per-request zone

4. **Timezone resolution helper.** Create `Utils/TimeZoneResolver.cs` — a static/utility that ports the
   quick-ordering approach: `TryResolve(string? headerValue, out TimeZoneInfo)` accepting an IANA id
   (via `TimeZoneInfo.FindSystemTimeZoneById`) OR an offset (`FindTimeZonesByOffset` + preferred-IANA /
   Windows-id handling for cross-platform), returning `false` on unparseable input. Include a
   `ResolveOrDefault(headerValue, defaultZone)` that never throws. Reuse the offset-parsing and
   preferred-IANA logic from `QuickOrdering/Extensions/DateTimeExtension.cs` (copy the logic, adapt
   namespaces; do **not** import quick-ordering). Read `App:DefaultTimeZone` from config.

5. **Request-timezone accessor + middleware.** Create `Auth/IRequestTimeZone.cs` (mirror
   `IContextAuthenticated`): interface `IRequestTimeZone { TimeZoneInfo Zone { get; } }` + a
   `[ScopedService(typeof(IRequestTimeZone))]` implementation over `IHttpContextAccessor` that reads the
   resolved `TimeZoneInfo` from `HttpContext.Items["RequestTimeZone"]`, falling back to the configured
   default when absent (no HttpContext / background thread). Create
   `Middlewares/RequestTimeZoneMiddleware.cs` that parses the `X-Time-Zone` header once via
   `TimeZoneResolver`, stashes the result in `HttpContext.Items["RequestTimeZone"]`. Wire it in
   `Program.cs` after `UseRouting()` (before/around `UseAuthentication()`; it has no dependency on auth).
   (Per OQ6(a).)

6. **Global JSON converters.** Create `Serialization/UtcAwareDateTimeConverter.cs` and
   `UtcAwareNullableDateTimeConverter.cs` (`JsonConverter<DateTime>` / `JsonConverter<DateTime?>`),
   constructed with an injected `IHttpContextAccessor` (or resolving the zone from `HttpContext.Items`):
   - **Write:** treat the value as UTC (`SpecifyKind(Utc)` if Unspecified) → `TimeZoneInfo.ConvertTime`
     to the request zone → emit ISO-8601 **with offset** (`DateTimeOffset` "o"/round-trip format).
   - **Read:** if the token carries an offset → convert to UTC; if naive → interpret in the request zone
     → convert to UTC. Store the resulting value as `Kind.Utc`.
   Register both via `builder.Services.AddControllers(...).AddJsonOptions(o =>
   o.JsonSerializerOptions.Converters.Add(...))` in `Program.cs` (the first `AddJsonOptions` on the
   existing `AddControllers` registration). Confirm they compose with `[ResponseWrapped]`/`ApiResult<T>`
   (nested `DateTime`s serialize through the same options).

7. **Inbound request models — no per-field changes.** The global read converter handles all inbound
   `DateTime`/`DateTime?` fields (expense `ExpenseTime`, event `StartDate`/`EndDate`, stats/admin
   `From`/`To`) uniformly. Verify FluentValidation validators still operate on the resulting UTC values
   (no validator reads `Kind`).

8. **Export switches to the request zone (M8).** Refactor
   `Services/Api/Export/ExportValueFormatter.cs`: replace the hardcoded `VietnamOffset` with a
   `TimeZoneInfo` parameter (or make the type non-static and inject `IRequestTimeZone`). `FormatInstant`
   converts UTC → request zone with `dd/MM/yyyy HH:mm`; `FormatCalendarDate` renders the whole-day
   boundary in the **same zone** the event range was normalized in (per OQ3) so the calendar day matches
   what the user picked and the end-of-day boundary does not roll forward. Update callers
   (`Services/Api/Export/*` document builders) to thread the zone. CSV is a `FileContentResult` — it
   does **not** pass through the JSON converter, so the formatter applies the zone directly.

9. **M6 event-range normalization becomes tz-aware (per OQ3(a)).** In `Repositories/EventRepository.cs`
   make `NormalizeStart`/`NormalizeEnd` take the request zone (inject `IRequestTimeZone` into the
   repository, or pass the zone through `CreateEventData`/`UpdateEventData` from the service layer —
   prefer passing through the data record to keep the repository free of request-scoped concerns).
   Compute the whole-day bounds in the request zone, then convert to UTC. `IsWithinRange`
   (`ExpenseRepository`) stays a raw UTC comparison (bounds already encode the zone).

10. **M10 tier window uses app-default zone (per OQ4(a)).** In
    `Services/Api/Tiers/TierService.cs`, replace `CurrentMonthUtcWindow`'s hardcoded `+7` with the
    **configured app-default** `TimeZoneInfo` (read `App:DefaultTimeZone`, not `IRequestTimeZone`),
    keeping the anti-gaming property. Update the Vietnamese XML doc/comment that currently says "theo
    giờ +7" to reference the configured default zone.

11. **Config.** Add an `App` section to `appsettings.json`:
    `"App": { "DefaultTimeZone": "Asia/Ho_Chi_Minh" }`. (Per OQ1.)

### Vietnamese user-facing message keys

- **None** (D1 + D8): an invalid/missing header falls back silently — no new error code or message.
- Update existing Vietnamese XML-doc comments that mention a fixed "+7" (ExportValueFormatter,
  TierService, event DTOs' `StartDate`/`EndDate`/`ClosedAt` summaries) to describe the new behavior.

### Tests the test-engineer should write

**Unit (pure logic, no DB):**
- `TimeZoneResolver`: IANA id resolves; offset `+07:00` / `+7` / `-05:00` resolve; unknown/garbage →
  fallback; cross-platform (Windows id vs IANA) sanity.
- `UtcAwareDateTimeConverter` write: `00:00:00Z` renders `+07:00` for a `+7` request zone; renders `Z`
  for a UTC zone; nullable null passes through.
- `UtcAwareDateTimeConverter` read: offset-bearing input → correct UTC; naive input → interpreted in
  request zone → UTC; header-missing → default zone.
- `ExportValueFormatter` (currently hardcoded `+7`) becomes **timezone-parameterized**: `FormatInstant`
  and `FormatCalendarDate` under `+7`, UTC, and a non-`+7` zone; assert the calendar-date boundary does
  not roll into the next day.
- `TierService.CurrentMonthUtcWindow` uses the app-default zone (not a request header); boundary cases
  at month start/end.
- Event `NormalizeStart/End` produce UTC bounds for a whole day in the request zone.
- Audit no-op: after dropping `AuditSnapshotCanonicalizer.Utc`, a before/after with equal instants still
  serializes equal → `AuditLogFactory` returns null (no row).

**Integration (real MariaDB, `[SkippableFact]`, per-test transaction rollback):**
- Round-trip: create an expense, read it back; materialized `ExpenseTime` has `Kind.Utc`.
- DB-generated `UpdatedAt` under the pinned `+00:00` session is UTC (update a row, compare to
  `DateTime.UtcNow` within tolerance).
- End-to-end via `WebApplicationFactory`: POST with `X-Time-Zone: Asia/Ho_Chi_Minh` and a naive
  `expenseTime` stores the correct UTC instant; GET the same resource returns the instant with
  `+07:00`; GET with no header returns the default-zone offset; GET with `X-Time-Zone: +00:00` returns
  `Z`/`+00:00`.
- Event created from a `+7` client: a `startDate=2026-07-14` stores `2026-07-13T17:00:00Z`; an expense
  at `2026-07-14T00:30:00+07:00` (= `2026-07-13T17:30Z`) is within range (OQ3 correctness).
- M8 CSV export honors `X-Time-Zone` (instant formatted in that zone; calendar date unshifted).

## Impact Analysis

**APIs:**
- **Client-facing JSON contract change (breaking-ish):** every response `DateTime` now serializes with
  an explicit offset (e.g. `+07:00`) instead of an offset-less local-looking string. Clients that
  string-parsed the old format may need to accept ISO-8601-with-offset. New optional request header
  `X-Time-Zone`. No route/verb/DTO-shape changes (D2). No new error codes/messages (D8) — an invalid
  header silently falls back to `App:DefaultTimeZone` (D1).

**Database:**
- **No migration** (D7). No schema/column/default/index change. Behavior-only: session pinned to
  `+00:00`; read-side value converter is a write-side no-op. The implementer must verify
  `dotnet ef migrations add` yields an EMPTY diff and STOP + report if EF wants a schema change.

**Infrastructure:**
- New `App:DefaultTimeZone` config key in `appsettings.json`. New `DbConnectionInterceptor` on the
  pooled context. New middleware in the pipeline. Cross-platform tz-database availability assumed
  (Linux ICU / Windows registry) — the resolver handles both; falls back to UTC if a zone id is
  unresolvable at all.

**Services:**
- New: `UtcSessionTimeZoneInterceptor`, `UtcDateTimeConverter` (EF), `TimeZoneResolver`,
  `IRequestTimeZone`/`RequestTimeZone`, `RequestTimeZoneMiddleware`, `UtcAwareDateTimeConverter` +
  nullable (STJ).
- Modified: `Program.cs` (interceptor wiring, `AddJsonOptions`, middleware registration),
  `Database/AppDbContext.cs` (`ConfigureConventions`), `Services/Audit/AuditSnapshots.cs` (drop `.Utc`),
  `Services/Api/Export/ExportValueFormatter.cs` (+ export document builders), `Repositories/EventRepository.cs`
  (tz-aware normalization), `Services/Api/Tiers/TierService.cs` (app-default zone),
  `appsettings.json`.
- Untouched: `Controllers/AppController.cs` (locked); no other controller changes needed.

**Documentation:**
- Update Vietnamese XML-doc comments referencing a fixed "+7" (export, tier, event DTOs). Update the M6
  events.md "UTC-day-boundary limitation" note to point at this doc as the resolution, and check off its
  "Per-user timezone" Future Improvement. Swagger descriptions for the `X-Time-Zone` header (optional).

## Decision Log

**Inherited-locked (from the task brief — not reopened):** storage stays UTC (`datetime(6)`,
presentation-only offset); header accepts IANA id OR numeric offset with app-default fallback;
responses are ISO-8601 in the viewer's zone with offset; naive inbound is interpreted in the request
zone → UTC while offset-bearing input is honored as sent.

**Resolved at the 2026-07-14 checkpoint — all to option (a):**

### D1 — OQ1 (header / config / invalid handling)
Single `X-Time-Zone` header (IANA id or numeric offset); config `App:DefaultTimeZone` (default
`Asia/Ho_Chi_Minh`); invalid/missing → silent fallback.
- **Reason:** most forgiving, zero client friction; timezone is presentation, not data validity — a
  bad header must not fail the request. Alternatives: 400 on invalid (b), two headers (c) — rejected.

### D2 — OQ2 (output mechanism)
Keep response DTOs as `DateTime` + global `JsonConverter<DateTime>`/`<DateTime?>` emitting offset-ISO.
- **Reason:** least churn (no DTO/AutoMapper edits), centralized and consistent. Alternative:
  `DateTimeOffset` DTOs (b) — rejected as high churn.

### D3 — OQ3 (event ranges under per-request tz)
Event whole-day ranges interpreted in the request zone → UTC bounds; `NormalizeStart/End` tz-aware.
- **Reason:** correct for the common single-zone user; delivers the M6 Future Improvement.
  **Supersedes/refines the M6 events.md OQ1 "whole-day-inclusive UTC" caveat.** Alternatives:
  keep UTC days (b), always app-default zone (c) — rejected.

### D4 — OQ4 (tier month-window / policy dates)
Tier "expenses this month" window computed in the app-default zone, not the request header.
- **Reason:** a usage quota is server policy; a header-driven window is gameable across a month
  boundary. Alternative: request-zone window (b) — rejected (gameable, non-deterministic).

### D5 — OQ5 (stats/admin `from`/`to`)
Follow the global inbound rule (naive → request zone → UTC); aggregation buckets stay UTC-deterministic.
- **Reason:** uniform with expense/event inputs, no per-endpoint code; deterministic buckets match D4.

### D6 — OQ6 (converter ↔ scoped-tz mechanism)
`RequestTimeZoneMiddleware` resolves once into `HttpContext.Items`; singleton STJ converters read it via
`IHttpContextAccessor`; scoped `IRequestTimeZone` serves services; CSV export applies the zone directly.
- **Reason:** single resolution path, stateless converter, composes with `ApiResult<T>`; CSV
  `FileContentResult` never touches the JSON converters.

### D7 — OQ7 (migration)
No EF migration — verify an empty diff; stop and report if EF wants a schema change.
- **Reason:** pure runtime behavior; the read converter is a write-side no-op, no column/default change.

### D8 — OQ8 (error codes)
No new error codes/messages — invalid tz silently falls back.
- **Reason:** D1 removes the only would-be failure path.

## Progress Log

### 2026-07-14

- Drafted planning doc. Read `CLAUDE.md`, `.agents/rules/rules.md`, `.claude/rules/rule.md`, and
  verified the live code: `AppDateTime`, `Program.cs` (no interceptors, `AddHttpContextAccessor`
  present, no `AddJsonOptions`), `AppDbContext(.partial).cs` (no conventions/converters),
  `AuditSnapshots.cs` (`Canonicalizer.Utc` workaround) + `AuditLogFactory.cs` (JSON-equality no-op),
  `ExportValueFormatter.cs` (hardcoded `+7`), `EventRepository.cs`
  (`NormalizeStart/End` + `ExpenseRepository.IsWithinRange`), `TierService.cs`
  (`CurrentMonthUtcWindow` +7), `IContextAuthenticated.cs` (accessor pattern), inbound request models
  (expense/event/stats/admin), and quick-ordering's `DateTimeExtension.cs` / `AppSettings.cs` /
  `DbTimeZoneInterceptor.cs` resolver.
- Confirmed the M6 events.md OQ1 "UTC-day boundary" accepted limitation and its "Per-user timezone"
  Future Improvement — this feature is that refinement.
- Recorded 4 locked decisions as inherited assumptions; raised OQ1–OQ8 with recommended options.
- Established a two-layer plan (Foundation: interceptor + convention converter + drop `.Utc`;
  Presentation: resolver + accessor + middleware + STJ converters + export/M6/M10 refinements).
  Confirmed no EF migration expected (to be verified as an empty diff).
- **Checkpoint held with the user: all 8 Open Questions resolved to the recommended option (a).**
  Recorded decisions D1–D8 in the Decision Log, annotated each OQ inline, promoted the working
  assumptions to confirmed. Doc is now **unblocked** for implementation. Synced the Implementation
  Plan, files list, and test list to the answers (no substantive changes needed — the plan already
  reflected option (a) throughout; tightened the "no migration must be verified as empty diff" and the
  OQ8 "no new message key" wording). Next: hand to the implementer.

### 2026-07-16 — Implementation (implementer)

Built the full feature to the answered decisions (all option (a)). Files:

**Foundation (UTC correctness):**
- NEW `Database/UtcSessionTimeZoneInterceptor.cs` — `DbConnectionInterceptor`, `[SingletonService]`,
  runs `SET time_zone = '+00:00'` on `ConnectionOpened`/`ConnectionOpenedAsync`. Wired in `Program.cs`
  via the `(serviceProvider, options)` overload of `AddDbContextPool` +
  `.AddInterceptors(sp.GetRequiredService<UtcSessionTimeZoneInterceptor>())`.
- NEW `Database/Conversions/UtcDateTimeConverter.cs` — `ValueConverter<DateTime,DateTime>`
  (read → `SpecifyKind(Utc)`, write → identity). Applied in `AppDbContext.ConfigureConventions`
  (added) via `Properties<DateTime>()` and `Properties<DateTime?>()` — covers all mapped DateTime props.
- MOD `Services/Audit/AuditSnapshots.cs` — removed `AuditSnapshotCanonicalizer.Utc` and its use in
  `ExpenseAuditSnapshot.From` (now uses `expense.ExpenseTime` directly, already `Kind.Utc` via the
  converter); kept `.Money`; updated the class doc. M5 no-op detection now relies on the converter.

**Presentation (per-request zone):**
- NEW `Utils/TimeZoneResolver.cs` — cross-platform IANA-id/offset resolver ported from quick-ordering
  (`FindTimeZonesByOffset`, preferred-IANA list, Windows-vs-Linux handling). `TryResolve`,
  `ResolveOrDefault`, `GetDefaultZone(config)` (reads `App:DefaultTimeZone`, default
  `Asia/Ho_Chi_Minh`, silent fallback), `FromHttpContext(...)` (reads `HttpContext.Items`). Holds the
  `HttpContextItemsKey` constant. Deliberately does NOT use `TimeSpan.TryParse` (quick-ordering bug:
  `"7"` = 7 days) so `+7` correctly means 7 hours.
- NEW `Auth/IRequestTimeZone.cs` (+ `RequestTimeZone`, `[ScopedService]`) — mirrors
  `IContextAuthenticated`; exposes the resolved `TimeZoneInfo` for the request.
- NEW `Middlewares/RequestTimeZoneMiddleware.cs` — parses `X-Time-Zone` once, resolves, stashes into
  `HttpContext.Items`. Registered in `Program.cs` right after `UseRouting()`.
- NEW `Serialization/UtcAwareDateTimeConverter.cs` (+ `UtcAwareNullableDateTimeConverter.cs`) — global
  STJ converters. Write = UTC→request zone, ISO-8601 with offset (`o` on a `DateTimeOffset`). Read =
  offset/Z-bearing → UTC as sent; naive → interpret in request zone → UTC (`Kind.Utc`). Shared logic
  in `RequestDateTimeSerializer`; zone read via `IHttpContextAccessor` (fallback app-default).
  Registered via `AddControllers().AddJsonOptions(...)` (a fresh `HttpContextAccessor()` is used — it is
  a stateless AsyncLocal wrapper, so it reads the same per-request context the middleware populates).
- MOD `appsettings.json` — added `"App": { "DefaultTimeZone": "Asia/Ho_Chi_Minh" }`.

**Refinements:**
- MOD `Repositories/EventRepository.cs` + `Repositories/EventWriteResult.cs` +
  `Services/Api/Events/EventsService.cs` — `CreateEventData`/`UpdateEventData` gained a `TimeZoneInfo
  Zone`; `EventsService` injects `IRequestTimeZone` and passes `Zone`; `NormalizeStart`/`NormalizeEnd`
  now compute whole-day bounds IN THE REQUEST ZONE then convert to UTC (D3). Within-range checks stay
  raw-UTC compares.
- MOD `Services/Api/Tiers/TierService.cs` — `CurrentMonthUtcWindow` now uses
  `TimeZoneResolver.GetDefaultZone(config)` (app-default zone, NOT the request header, D4); removed the
  hardcoded `+7`; updated the Vietnamese doc.
- MOD `Services/Api/Export/ExportValueFormatter.cs` + `Services/Api/Export/ExportService.cs` —
  `FormatInstant`/`FormatCalendarDate` take a `TimeZoneInfo`; `ExportService` injects `IRequestTimeZone`
  and threads `Zone` (build-document methods became instance methods). Replaced the hardcoded `+7`. CSV
  is a `FileContentResult` (bypasses JSON converters), so the zone is applied directly here.

**No migration (D7 verified):** `dotnet ef migrations add TempTzCheck` produced an EMPTY `Up()`/`Down()`
diff (confirmed by inspection); removed it with `dotnet ef migrations remove`; the model snapshot is
git-clean. The converter's write side is an identity no-op, so column definitions are unchanged.

**Build:** `dotnet build FairShareMonApi/FairShareMonApi.csproj` succeeds (0 errors; only the pre-existing
AutoMapper 13.0.1 NU1903 warning). The `FairShareMonApi.Tests` project has 13 pre-existing tests that
break BY DESIGN due to the intended contract changes — left for the test-engineer (listed in Final
Outcome). No test files edited (per protocol).

**Live smoke (real API on :5199 + MariaDB; data cleaned up afterward):**
- Naive `expenseTime=2026-07-14T00:00:00` + `X-Time-Zone: Asia/Ho_Chi_Minh` → stored
  `expense_time = 2026-07-13 17:00:00.000000` (UTC) ✓; GET renders `2026-07-14T00:00:00.0000000+07:00`.
- GET offset rendering: `Asia/Ho_Chi_Minh`, `+07:00`, `+7`, no-header (default) all render `+07:00`;
  `+00:00` → `2026-07-13T17:00:00+00:00`; `America/New_York` → `2026-07-13T13:00:00-04:00` (same
  instant); garbage `Not/AZone` → silent fallback `+07:00` ✓.
- DB-generated `updated_at` under the pinned `+00:00` session read ~UTC-now (07:35 vs UTC 07:36), not
  local +7 (would be 14:35) ✓ (the plain mysql client session is `SYSTEM`, proving the interceptor is
  what makes it UTC).
- Event `startDate=2026-07-14`/`endDate=2026-07-14` from a +7 client → stored bounds
  `2026-07-13 17:00:00` .. `2026-07-14 16:59:59.999999` ✓ (D3); an expense at the local-day edge
  `2026-07-14T00:30:00+07:00` (=17:30Z) assigned to the event is IN RANGE ✓.
- CSV export (`format=csv`, +7): "Khoảng thời gian" = `14/07/2026 - 14/07/2026` (no day-roll);
  "Thời điểm chi" = `14/07/2026 00:00`. Same expense with `+00:00` → `13/07/2026 17:00`; event with
  `America/New_York` → `13/07/2026 - 14/07/2026` (calendar date tracks the viewer zone, D3) ✓.

### 2026-07-16 — Tests (test-engineer)

Wrote/updated the test suite for the feature and ran the full solution against the live MariaDB
(:3306) + Redis (:6379). **Result: 1088 passed / 0 failed / 0 skipped** (up from the 1047 baseline;
DB reachable so nothing skipped). Determinism confirmed by two consecutive full green runs; the DB is
left clean (per-test prefix/rollback sweeps, verified no residual test rows). Production code untouched
apart from the intended feature (test project only).

**Existing tests updated for the contract change (allowed cross-cutting update):**
- `ExportValueFormatterTests` — rewrote for the new `FormatInstant(utc, zone)` / `FormatCalendarDate(utcBoundary, zone)`
  signatures. Re-derived the calendar-date expectations against tz-normalized boundaries (the +7 whole-day
  end `2026-03-03T16:59:59.999999Z` renders `03/03/2026`, no day-roll); added a UTC-zone instant case and a
  same-stored-boundary-renders-different-day-per-zone case to prove the zone parameter drives the rendering.
- `EventRepositoryTests` (+ `CreateData` helper and 3 inline `UpdateEventData`) and
  `ExpenseEventAssignmentTests` (`NewEventAsync`) — pass the new required `TimeZoneInfo Zone`; use
  `TimeZoneInfo.Utc` so the existing UTC-day-boundary assertions hold unchanged (UTC-day normalized in the
  UTC zone == those UTC bounds).
- `EventsServiceTests` — construct `EventsService` with a `TestRequestTimeZone` stub (new ctor arg); added
  `CreateAsync_PassesRequestZoneToRepository` proving the service threads `IRequestTimeZone.Zone` into
  `CreateEventData`.
- `ExportServiceTests` — construct `ExportService` with the `IRequestTimeZone` stub (+7); re-derived the
  `SampleEvent` boundary fixtures to tz-normalized UTC bounds so the +7 calendar range still reads
  `01/03/2026 - 03/03/2026`.
- `ExpenseRepositoryTests.UpdateGeneralInfoAsync_NoChange_WritesNoAuditRow` — replaced the stale
  "FAILING / confirmed production bug" XML-doc: the global read-side `UtcDateTimeConverter` (`Kind.Utc`)
  now makes the DB-read before-snapshot compare equal, so the no-op writes no audit row and the test PASSES.
- `TierServiceTests` — clarified the month-window comment (app-default zone, not a hardcoded +7); added
  `EnsureCanCreateExpense_MonthWindow_UsesAppDefaultZoneFromConfig_NotFixedPlus7` (App:DefaultTimeZone=UTC →
  UTC calendar-month window), proving the anti-gaming property (D4): config-driven, no request-zone input.

**New coverage added:**
- Unit — `TimeZoneResolverTests` (IANA id; `+07:00`/`+7`/`7`/`+05:30`/`-05:00`/`-5`/`+00:00`;
  invalid/blank/out-of-range → fallback; `GetDefaultZone` reads `App:DefaultTimeZone` and silently falls
  back to Asia/Ho_Chi_Minh; asserts OFFSET behavior, cross-platform).
- Unit — `UtcAwareDateTimeConverterTests` (write UTC→request-zone ISO-8601 with offset for +7/UTC/default
  fallback; read offset/Z/naive → UTC with `Kind.Utc`; missing-zone → configured default; nullable null +
  value), driven via a stubbed `IHttpContextAccessor` + `HttpContext.Items`.
- Unit — `UtcDateTimeConverterTests` (read side stamps `Kind.Utc` preserving ticks; write side identity).
- Integration (real MariaDB, skippable) — `TimeZoneDbIntegrationTests`: **session pin** (interceptor →
  `@@session.time_zone='+00:00'` + DB-generated `UpdatedAt` ≈ UTC-now, EF tx rolled back); **read converter**
  (materialized `ExpenseTime`/`CreatedAt`/`UpdatedAt`/event `StartDate`/`EndDate` carry `Kind.Utc`); **M6
  tz-aware ranges** (whole-day +7 range stores `13/07 17:00Z .. 14/07 16:59:59.999999Z`; expense at the
  local-day edge in range, just outside not).
- Endpoint (WebApplicationFactory, real HTTP, skippable) — `TimeZoneEndpointTests`: the same stored instant
  renders per `X-Time-Zone` (Asia/Ho_Chi_Minh, `+07:00`, `+7`, `+00:00`, America/New_York, garbage, none)
  with the same absolute moment, inside the `ApiResult<T>` envelope; a naive `expense_time` under `+07:00`
  round-trips (stored earlier UTC, revealed via `+00:00`); an event created under +7 covers the local day
  and its CSV export (FileContentResult, bypasses the JSON converters) renders calendar dates in the request
  zone and honors a different `X-Time-Zone`.

**Production bugs found:** none — all assertions passed, including the M5 no-op regression the feature was
designed to fix. No production code was modified.

### 2026-07-16 — Code review (APPROVED, 0 blocking — feature closed)

Full review of the feature: **APPROVE, 0 blocking**, 2 informational notes. Final suite **1088/1088**
(0 failed / 0 skipped), **NO migration** confirmed. Verified checks:

- **Singleton STJ converters hold NO per-request state.** They resolve the zone fresh per call via the
  AsyncLocal-backed `IHttpContextAccessor` (reading `HttpContext.Items`), so there is no cross-request
  leakage despite the converters being effectively singletons.
- **Exactly one conversion each way.** Traced a naive `00:00 +7` → `17:00Z` stored → `+07:00` out; no
  double-shift. `EnsureUtc` (SpecifyKind only when `Unspecified`) prevents a second offset application.
- **`UtcDateTimeConverter`** read = `SpecifyKind(Utc)`, write = identity — an empty migration diff, model
  snapshot untouched, and it coexists cleanly with the DB-generated `UpdatedAt`.
- **Session pin** runs in both the sync `ConnectionOpened` and async `ConnectionOpenedAsync` paths.
- **M6** `NormalizeStart/End` are tz-aware; the end bound is next-local-midnight − 1µs converted to UTC
  (no off-by-one); the closed-event guard still runs BEFORE normalization.
- **M10** `CurrentMonthUtcWindow` uses `App:DefaultTimeZone`, not the request zone (anti-gaming holds).
- **M8 export** uses the request zone; CSV (`FileContentResult`) correctly bypasses the JSON converters
  and applies the zone directly in `ExportValueFormatter`.
- **Audit:** `AuditSnapshotCanonicalizer.Utc` removed (`.Money` kept); `AuditLogFactory`'s own serializer
  keeps no-op detection robust now that reads are uniformly `Kind.Utc`.
- **`TimeZoneResolver`:** `+7` → 7 hours (deliberately NOT `TimeSpan.TryParse`, which would read `"7"`
  as 7 days — the quick-ordering bug avoided); invalid/unknown → silent fallback; cross-platform
  IANA/offset handling.

**Informational note 1 (accepted):** inbound malformed-datetime request bodies now surface a Vietnamese
`JsonException` → 400. This is an addition BEYOND D8's header-scoped "no new messages" (D8 concerned the
`X-Time-Zone` header, which still falls back silently). It is acceptable/desirable — it satisfies the
Vietnamese user-facing-message convention for a genuinely malformed payload, and does not introduce a new
error code.

**Informational note 2 (accepted):** `RequestTimeZoneMiddleware` sits before `ErrorHandlerMiddleware` in
the pipeline and relies on `TimeZoneResolver`'s never-throw guarantee (invalid input → fallback), so it
cannot throw an unhandled exception ahead of the error handler. Recorded as a Future Improvement caveat.

## Final Outcome

**Shipped, reviewed, and closed — code review APPROVED (0 blocking, 2 accepted informational notes);
full suite 1088/1088 (0 failed / 0 skipped, from the 1047 baseline); NO EF migration.**

Timezone-aware DateTimes: **store UTC, present per-`X-Time-Zone` with offset.** UTC storage unchanged;
presentation per-request via the `X-Time-Zone` header (IANA id or numeric offset) with silent fallback
to `App:DefaultTimeZone` (`Asia/Ho_Chi_Minh`).
- **Foundation:** a `+00:00` session interceptor (DB-generated `UpdatedAt` is true UTC) + a global
  read-side `Kind.Utc` value converter (`AuditSnapshotCanonicalizer.Utc` dropped, `.Money` kept — the
  M5 audit no-op now relies on the converter and still writes no row).
- **Presentation:** `RequestTimeZoneMiddleware` + scoped `IRequestTimeZone` + global STJ converters that
  emit/accept offset-aware ISO-8601 in the viewer's zone.
- **Refinements:** M6 event ranges resolve whole days in the request zone → UTC bounds (**refines the
  M6 OQ1 whole-day-UTC decision to be timezone-aware**); the M10 tier month window uses the app-default
  zone (anti-gaming); M8 CSV export formats in the request zone (bypassing the JSON converters).

**Client-facing contract change:** every read-path `DateTime` now renders as offset-ISO in the viewer's
zone. No new `ErrorCodes`; inbound malformed-datetime payloads surface a Vietnamese `JsonException`
(400) — an accepted addition beyond D8's header-scoped scope.

**Files created:** `Database/UtcSessionTimeZoneInterceptor.cs`,
`Database/Conversions/UtcDateTimeConverter.cs`, `Utils/TimeZoneResolver.cs`, `Auth/IRequestTimeZone.cs`,
`Middlewares/RequestTimeZoneMiddleware.cs`, `Serialization/UtcAwareDateTimeConverter.cs`,
`Serialization/UtcAwareNullableDateTimeConverter.cs`.
**Files modified:** `Program.cs`, `appsettings.json`, `Database/AppDbContext.cs`,
`Services/Audit/AuditSnapshots.cs`, `Repositories/EventRepository.cs`,
`Repositories/EventWriteResult.cs`, `Services/Api/Events/EventsService.cs`,
`Services/Api/Tiers/TierService.cs`, `Services/Api/Export/ExportValueFormatter.cs`,
`Services/Api/Export/ExportService.cs`.

**Existing tests updated for the contract change (all resolved by the test-engineer, now green):**
`ExportValueFormatterTests` (formatters now take a `TimeZoneInfo`; `FormatCalendarDate` re-derived
against zone-normalized bounds), `EventRepositoryTests` + `ExpenseEventAssignmentTests`
(`CreateEventData`/`UpdateEventData` carry a `TimeZoneInfo Zone`), `EventsServiceTests` +
`ExportServiceTests` (ctors gained `IRequestTimeZone`), `TierServiceTests` (app-default zone), and
`ExpenseRepositoryTests.UpdateGeneralInfoAsync_NoChange_WritesNoAuditRow` (the M5 no-op now PASSES via
the global `Kind.Utc` converter; the stale "confirmed production bug" XML-doc was corrected). New test
files: `TimeZoneResolverTests`, `UtcAwareDateTimeConverterTests`, `UtcDateTimeConverterTests`,
`TimeZoneDbIntegrationTests`, `TimeZoneEndpointTests` (+ `TestRequestTimeZone`, `TestTimeZones` infra).

No new `ErrorCodes` (D8). Inbound malformed-datetime bodies surface a Vietnamese `JsonException` (400),
accepted at review. No deviations from the doc. No new Open Questions.

## Future Improvements

- **DST-at-midnight edge (review note):** `TimeZoneInfo.ConvertTimeToUtc(localMidnight, zone)` throws
  `ArgumentException` if that local midnight falls in a spring-forward gap — affecting
  `NormalizeStart/End` and the inbound naive `Read` path, surfacing as a 500. This is irrelevant for the
  DST-free default `Asia/Ho_Chi_Minh`, but if DST-having viewer zones are later supported, add a
  try/catch fallback (e.g. resolve via `zone.GetUtcOffset(...)` to skip the gap) so it degrades
  gracefully instead of erroring.
- **Middleware ordering caveat (review note):** `RequestTimeZoneMiddleware` sits BEFORE
  `ErrorHandlerMiddleware` and relies on `TimeZoneResolver`'s never-throw guarantee (invalid input →
  fallback). If the resolver is ever changed to throw, either move the middleware after the error
  handler or keep the fallback invariant so it cannot fault ahead of the handler.
- Per-column opt-out for any genuinely calendar-only or naive-local field (none exist today).
- Optional `Vary: X-Time-Zone` / cache-key awareness if response caching is later introduced (offset
  now depends on the header).
- Consider request-zone bucketing for admin revenue/metrics if operators ask for it (currently UTC for
  determinism, OQ5).
- Persist a per-user default timezone (profile setting) as an additional fallback below the header and
  above the app default.
