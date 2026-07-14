# Hosted-service DI registration (BackgroundService base + `[BackgroundService]`)

## Objective

Standardize how startup background workers are written and registered:

1. All hosted services inherit from `Microsoft.Extensions.Hosting.BackgroundService` (override
   `ExecuteAsync`) instead of implementing `IHostedService` directly.
2. They are registered declaratively with DiDecoration's `[BackgroundService]` attribute (picked up by
   the existing `RegisterDecorators(...)` scan in `Program.cs`) instead of manual
   `builder.Services.AddHostedService<T>()` calls.

## Background

- The two startup backfill services shipped in Milestones 3 and 4:
  - `HostedServices/OwnerRepresentativeBackfillHostedService.cs` (M3, `planning/members.md` OQ2).
  - `HostedServices/SuggestedCategoriesBackfillHostedService.cs` (M4, `planning/categories-and-tags.md` OQ3).
- Both implemented `IHostedService` directly (work in `StartAsync`, trivial `StopAsync`) and were
  registered manually in `Program.cs`.
- The rest of the app's DI is attribute-driven via DiDecoration. `RegisterDecorators(builder.Configuration,
  typeof(Program).Assembly)` (`Program.cs`) already calls `RegisterHostedServices` internally, so a
  `[BackgroundService]`-annotated type is registered automatically — no separate call is needed.
- User guidance (2026-07-14): use DiDecoration's `BackgroundServiceAttribute(Type? serviceType = null)`
  to register hosted services; a `[SingletonService]` may be placed alongside it to register the worker
  as a singleton that the hosted-service machinery then resolves. Separately: **all** hosted services
  should inherit from `BackgroundService`.

## Requirements

- Both backfill services inherit from `BackgroundService`, override `protected override Task
  ExecuteAsync(CancellationToken)`, and drop the explicit `StopAsync` (inherited).
- Both carry the bare `[BackgroundService]` attribute (user-chosen form — a dedicated instance created
  via `ActivatorUtilities`; no lifetime attribute).
- Remove both manual `AddHostedService<T>()` registrations and the now-unused
  `using FairShareMonApi.HostedServices;` from `Program.cs`.
- Behavior, DI scope usage, idempotency, and log-and-swallow error handling are otherwise unchanged.
- Convention docs updated so the team follows this pattern for future hosted services.

## Open Questions

None — resolved at the 2026-07-14 checkpoint (see Decision Log).

## Assumptions

- `BackgroundService` / `IHostedService` are available via the Web SDK's implicit usings (they were
  already used un-imported), so no new `using` is required for the base class.
- `[BackgroundService]` (DiDecoration `BackgroundServiceAttribute`) and `: BackgroundService`
  (`Microsoft.Extensions.Hosting.BackgroundService`) coexist without name ambiguity — confirmed by a
  clean build.

## Implementation Plan

1. `HostedServices/OwnerRepresentativeBackfillHostedService.cs` — add `using DiDecoration.Attributes;`
   + `[BackgroundService]`; base `IHostedService` → `BackgroundService`; `StartAsync` → `ExecuteAsync`
   (thread `stoppingToken`); delete `StopAsync`.
2. `HostedServices/SuggestedCategoriesBackfillHostedService.cs` — same base/method conversion (already
   had `[BackgroundService]` + the `using`).
3. `Program.cs` — remove the two `AddHostedService<T>()` calls + their comments and the unused
   `using FairShareMonApi.HostedServices;`.
4. Docs — `CLAUDE.md`, `.agents/rules/rules.md`, `AGENTS.md` DI sections; this planning doc; Progress-Log
   notes in `members.md` + `categories-and-tags.md`.

## Impact Analysis

- **APIs / Database / Services:** none — the backfills perform identical work.
- **Infrastructure / bootstrap:** hosted-service registration moves from manual `AddHostedService` to the
  DiDecoration attribute scan. **Fixes a latent M4 defect:** `SuggestedCategoriesBackfillHostedService`
  was registered twice (it already had `[BackgroundService]` *and* a manual `AddHostedService` line), so
  its work ran twice each boot; it now runs once.
- **Behavior note (accepted):** `IHostedService.StartAsync` was awaited during startup (backfill
  finished before serving traffic); `BackgroundService.ExecuteAsync` runs in the background (the app
  serves immediately while the backfill runs concurrently). Acceptable for idempotent, self-healing,
  log-and-swallow backfills. The try/catch keeps `ExecuteAsync` from throwing, so the .NET 8 default
  `BackgroundServiceExceptionBehavior.StopHost` cannot stop the host.
- **Documentation:** DI conventions in `CLAUDE.md` / `.agents/rules/rules.md` / `AGENTS.md`; this doc;
  cross-references from `members.md` and `categories-and-tags.md`.

## Decision Log

### Decision
Hosted services inherit from `Microsoft.Extensions.Hosting.BackgroundService` and register via bare
`[BackgroundService]`; no manual `AddHostedService`.

### Reason
User guidance (2026-07-14). Matches the app's attribute-driven DI, removes two lines of manual wiring,
and eliminates the M4 double-registration. Bare `[BackgroundService]` (dedicated instance) chosen over
`[SingletonService]` + `[BackgroundService(typeof(self))]` (shared singleton) because these backfills
are pure hosted services that nothing else resolves.

### Alternatives Considered
- Keep `IHostedService` + manual `AddHostedService` (status quo) — inconsistent with the rest of the DI.
- `[SingletonService]` + `[BackgroundService(typeof(self))]` — one shared singleton instance; unnecessary
  here since nothing resolves the worker outside the host.
- `[SingletonService]` + bare `[BackgroundService]` — rejected: `ServiceType == null` makes the hosted
  path create a *second* instance, leaving an unused singleton.

## Progress Log

### 2026-07-14

- Converted both backfill hosted services to `BackgroundService` (`ExecuteAsync`, no `StopAsync`) with
  bare `[BackgroundService]`; removed the two manual `AddHostedService` registrations + the unused
  `using` from `Program.cs`; fixed the M4 double-registration.
- Updated DI conventions in `CLAUDE.md`, `.agents/rules/rules.md`, `AGENTS.md`; cross-referenced from
  `members.md` and `categories-and-tags.md`.
- Verified: `dotnet build` clean (only the pre-existing AutoMapper NU1903 warning); `dotnet test`
  337/337; clean boot with each backfill running exactly once.

## Final Outcome

Both hosted services inherit from `BackgroundService` and are registered solely via `[BackgroundService]`
through the DiDecoration `RegisterDecorators` scan. Manual `AddHostedService` wiring removed; the M4
double-registration is fixed. Build clean, 337/337 tests pass. The pattern is documented as the house
convention for future hosted workers.
