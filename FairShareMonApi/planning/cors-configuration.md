# CORS Configuration

## Objective

Add a CORS policy to FairShareMonApi so browser frontends (the Vite `FairShareMonWeb` SPA and any deployed web origin) can call the API. Port the sibling project quick-ordering's CORS helper, but gate the private/localhost/loopback auto-allow to Development only.

## Background

`Program.cs` had **no** CORS configuration (no `AddCors`/`UseCors`). Any cross-origin browser request was blocked, making the backend unusable from a SPA. quick-ordering (`QuickOrdering/Extensions/CorsExtensions.cs`) has a clean, BCL-only helper: a `"DefaultCors"` policy allowing configured origins plus any localhost/private origin, with `AllowAnyHeader/Method` + `AllowCredentials`.

The concern with a straight port: quick-ordering's local-origin auto-allow is **always on**, so in production any private-network origin could make credentialed calls. FairShareMon uses an opaque Bearer token in the `Authorization` header (not cookies), so the practical risk is limited, but the user chose to tighten it.

## Requirements

- Single global CORS policy adapted from quick-ordering.
- Configured origins read from `App:AllowedOrigins` (consistent with the existing `App:` section), honored in every environment.
- Localhost/loopback/private-network auto-allow enabled **only** when `Environment.IsDevelopment()`.
- Keep `AllowAnyHeader()`, `AllowAnyMethod()`, `AllowCredentials()`.
- `UseCors` placed after `UseRouting()` and before authentication/authorization.

## Open Questions

- None outstanding. Production origins are a deploy-time value; `App:AllowedOrigins` ships empty as a placeholder.

## Assumptions

- Bearer-token-in-header auth means credentialed CORS with a reflected origin (`SetIsOriginAllowed` + `AllowCredentials`) is acceptable.
- Empty `App:AllowedOrigins` in dev is fine because localhost is auto-allowed there.

## Implementation Plan

1. New `Extensions/CorsExtensions.cs` ported from quick-ordering; add `DefaultCorsPolicyName`, `AllowedOriginsConfigKey = "App:AllowedOrigins"`, and an `allowLocalOrigins` flag threaded into `IsAllowedOrigin`. Local-origin fallthrough happens only when the flag is true.
2. `Program.cs`: register `AddDefaultCorsPolicy(builder.Configuration, builder.Environment.IsDevelopment())`; add `app.UseCors(CorsExtensions.DefaultCorsPolicyName)` right after `UseRouting()`.
3. Config: add `App:AllowedOrigins: []` to `appsettings.json`; add an `App` section with the same key to `appsettings.production.local.json`.

## Impact Analysis

- **APIs**: no endpoint changes; single global policy applies to all controllers.
- **Config**: new `App:AllowedOrigins` key in `appsettings.json` and `appsettings.production.local.json`.
- **Security**: production no longer auto-trusts private/loopback origins (tighter than quick-ordering); credentialed CORS limited to the configured list in prod, localhost+private in dev.
- **Database / Services / UI**: none. No new package dependencies (BCL `System.Net` only).

## Progress Log

### 2026-07-16
- Created planning doc.
- Added `Extensions/CorsExtensions.cs` (ported + dev-gated local origins).
- Wired registration and `UseCors` into `Program.cs`.
- Added `App:AllowedOrigins` to `appsettings.json` and `appsettings.production.local.json`.
- `dotnet build` succeeded (0 errors).

## Final Outcome

CORS is configured. In Development, localhost/loopback/private origins plus any `App:AllowedOrigins` entry are allowed with credentials. In non-Development, only `App:AllowedOrigins` entries are allowed. This unblocks the frontend build; deploy-time work is limited to populating `App:AllowedOrigins` with the real production web origin(s).

## Future Improvements

- Optional unit tests for `CorsExtensions.IsAllowedOrigin` (configured match, dev-vs-prod local origin, non-http scheme).
- Consider a stricter production policy (specific methods/headers) if the frontend's needs stabilize.
