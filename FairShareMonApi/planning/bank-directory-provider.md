# Bank Directory Provider (server-side bank list behind a provider abstraction)

Move the VietQR bank directory into the backend behind a provider abstraction and expose our own
contract `GET /api/v1/banks`, so the FairShareMonWeb SPA no longer talks to `vietqr.vn` directly. Also
make the QR *payload content* pluggable: a `Local` provider (default, byte-identical to today's
hand-rolled `IVietQrPayloadBuilder`) plus an optional `VietQr` provider that calls
`https://vietqr.vn/api/vietqr/generate`. QR *image rendering* (QRCoder / SkiaSharp) is unchanged — only
the TLV content source becomes swappable.

## Objective

Today the SPA fetches the bank list straight from `https://vietqr.vn/api/vietqr/banks` (the single
sanctioned raw-`fetch` exception in `FairShareMonWeb/src/features/wallet/api/vietqrDirectoryApi.ts`),
keeps a committed 58-bank snapshot (`FairShareMonWeb/src/features/wallet/data/vietqrBanks.ts`) as an
offline/CORS fallback, and builds logo URLs from `imageId` client-side.

Move all of this to the backend behind a provider abstraction (VietQR is one provider) and expose OUR
OWN contract:

- **`GET /api/v1/banks`** returns `{ bin, code, name, shortName, logoUrl }[]` — a fully-built `logoUrl`
  (the browser fetches the image directly from VietQR); `imageId` never leaves the backend.
- The bank directory is fetched server-side (cached), with the committed 58-bank snapshot ported to the
  backend as a static fallback so the endpoint never fails.
- The VietQR *payload* becomes provider-selectable: `Local` (default, over the existing
  `IVietQrPayloadBuilder`) or `VietQr` (`/api/vietqr/generate`), chosen by config `Banks:QrProvider`.
  The VietQr provider always falls back to the local builder on any failure, so QR generation never
  breaks.

This is the repo's **first outbound HTTP call** and its **first use of `IMemoryCache`**.

## Background

- **Milestone 9 (`planning/wallet-and-qr.md`, shipped, reviewed, closed) is the foundation.** It built
  the wallet (`bank_accounts` CRUD) and on-demand VietQR generation:
  `Services/Api/Wallet/VietQrPayloadBuilder.cs` (`IVietQrPayloadBuilder.Build(bankBin, accountNumber,
  amount, addInfo)` — hand-rolled EMVCo TLV + CRC-16/CCITT-FALSE, fully unit-tested), `QrImageService.cs`
  (QRCoder single PNG + SkiaSharp composite), and `WalletQrService.cs` (`IWalletQrService`) which today
  injects `IVietQrPayloadBuilder` and calls `payloadBuilder.Build(...)` in both `GenerateExpenseQrAsync`
  (once) and `GenerateEventQrAsync` (a synchronous `Select` over the still-owing rows).
- **This feature deliberately reverses/extends two M9 open-question outcomes** (annotated in
  `planning/wallet-and-qr.md`): **OQ4** ("BIN on the account, no banks table; the client renders the bank
  picker") — we now serve the directory from the backend (still NO DB banks table); and **OQ1c** ("do NOT
  call an external VietQR image service") — we now offer an OPT-IN `VietQr` payload-content provider that
  calls `/api/vietqr/generate`, always with a local fallback. These reversals were approved by the user;
  they are recorded as Decisions here, not reopened.
- **M10 tier gating (`Services/Api/Tiers/TierService.cs`) is live.** The wallet CRUD
  (`BankAccountsController`) and both QR routes (`WalletQrService`) are Premium-only via
  `tierService.EnsurePremiumFeature(MessageKeys.Feature.Wallet | Feature.Qr)` → 403
  `PremiumFeatureRequired` (13003). Any new endpoint must decide whether it participates in that gate
  (see Open Questions).
- **DI is attribute-driven (DiDecoration).** Services carry `[ScopedService(typeof(IX))]`; multiple
  implementations of one interface need `Multiple = true` or `TryAdd` silently drops later ones. The
  single `RegisterDecorators(...)` scan in `Program.cs` wires them.
- **HTTP/options wiring:** `Program.cs` today wires no `HttpClient` and no `IMemoryCache`. The
  DiDecoration `[HttpClientService]`/`[Option]` scanners (`RegisterHttpClients`/`RegisterOptions`) are
  NOT called in `Program.cs`, so they are not used here — standard .NET
  `AddHttpClient<T>` / `AddMemoryCache()` / `Configure<TOptions>(...)` is the fit (Decision 3).
- **Controllers derive from `AppController` (LOCKED)**, routes `api/v{version:apiVersion}/[controller]`,
  responses auto-wrapped into `ApiResult<T>` via `[ResponseWrapped]`; guarded by the fallback
  authorization policy unless `[AllowAnonymous]`. `BankAccountsController` already sets an explicit
  `[Route("api/v{version:apiVersion}/bank-accounts")]` to force a kebab-case multi-word route — the same
  reason a single-word `banks` route is trivially served by the `[controller]` token, but this feature
  uses an explicit route override for clarity/consistency (`api/v{version:apiVersion}/banks`).
- **Config precedence** (`Program.cs`): `appsettings.json` < `appsettings.{env}.json` <
  `appsettings.{env}.local.json` < env vars < CLI. A new `Banks` section slots into `appsettings.json`
  (+ Development).
- **The committed 58-bank snapshot** (`vietqrBanks.ts`, captured 2026-07-18: 58 kept of 66 returned, 8
  dropped for a non-6-digit `caiValue`) is already normalized to `{ bin, code, name, shortName, imageId
  }` and is the exact shape to port into the backend fallback.
- **Web-side contract to keep in mind (not changed by this doc):** the SPA will later switch its picker
  to call our `GET /api/v1/banks` through `src/lib/api/client.ts` and drop the raw-`fetch` exception and
  the client snapshot — that is a separate FairShareMonWeb planning doc, out of scope here (noted in
  Future Improvements).

## Requirements

- Expose `GET /api/v1/banks` returning `List<BankResponse>` `{ bin, code, name, shortName, logoUrl }`,
  authenticated (no `[AllowAnonymous]`), wrapped in `ApiResult<T>`, Vietnamese Swagger.
- The directory is sourced from a provider abstraction (`IBankDirectoryProvider`); VietQR is one impl.
- The endpoint must never fail: cache the provider result 24h (`IMemoryCache`, key `banks:list`), and
  fall back to a committed static 58-bank snapshot on any provider failure.
- `logoUrl` is fully built server-side (`{ImagePath}/{imageId}`); `imageId` is never returned.
- BINs failing `^\d{6}$` are dropped during normalization (mirroring the client rule).
- QR payload content is provider-selectable via `Banks:QrProvider` (`Local` default, `VietQr` optional):
  - `Local` — byte-identical to today's `IVietQrPayloadBuilder`.
  - `VietQr` — POST `https://vietqr.vn/api/vietqr/generate`, return its `qrCode`; resolve `bankCode` from
    the directory by BIN; on ANY remote failure or unresolved bankCode, fall back to the local builder
    (logged warning). QR never breaks.
- QR *image* rendering stays exactly where it is (`QrImageService`, QRCoder/SkiaSharp) — unchanged.
- No DB schema change (no banks table, no migration); `bank_accounts` still stores BIN + display name.
- Standard .NET HTTP + options + cache wiring (not the DiDecoration `[HttpClientService]`/`[Option]`
  scanners).

## Open Questions

> The approach is user-approved; the resolved points are in the Decision Log, not here. Both remaining
> points were resolved by the user on 2026-07-19 (see resolutions inline).
>
> **RESOLVED 2026-07-19 — OQ-A → (a):** `GET /api/v1/banks` is authenticated-only, NOT Premium-gated.
> `EnsurePremiumFeature` is not called on the banks endpoint/service.
> **RESOLVED 2026-07-19 — OQ-B → (a):** serve the static fallback WITHOUT caching it. Only successful
> provider results are cached (key `banks:list`, 24h TTL); provider failure with a cold cache returns the
> committed static fallback uncached, so the next request retries the provider and self-heals.

**OQ-A — Does `GET /api/v1/banks` participate in the Premium gate?**
The approved spec says the endpoint is "authenticated (no `[AllowAnonymous]`)" and does not mention tier
gating. But the directory only feeds the wallet bank-picker, and the wallet CRUD + QR generation are
Premium-only (M10 `Feature.Wallet`/`Feature.Qr` → 403 `PremiumFeatureRequired` 13003).
- **(a) [recommended] Authenticated-only, NOT Premium-gated.** The directory is read-only reference data,
  not a wallet mutation or a QR generation; reads are never limit/tier-gated in this codebase (§4.9). A
  Free user could browse the picker but still hit 403 the moment they try to save a bank account —
  consistent, and it keeps the endpoint a plain read. Trade-off: a Free client can fetch the list even
  though it can't use the wallet yet.
- **(b) Premium-gate it with `Feature.Wallet`** (call `tierService.EnsurePremiumFeature(...)` in the
  service), so the whole wallet surface — including its picker data — is uniformly Premium. Trade-off:
  the SPA must handle a 403 on directory load (an `<UpgradePrompt>`), and it gates pure reference data,
  which is otherwise never gated here.

**OQ-B — On provider failure, is the static fallback cached, or served uncached (retry next call)?**
Decision 4 fixes the successful result to a 24h `IMemoryCache` entry with a committed static fallback so
the endpoint never fails. It does not fix what happens to the *cache* when the provider throws.
- **(a) [recommended] Serve the fallback WITHOUT caching it** (only cache successful provider results).
  The next request retries the provider, so the list self-heals as soon as VietQR is reachable again.
  Trade-off: every request during an outage does a (failing) provider round-trip; acceptable because the
  provider call is behind a short HTTP timeout and outages are rare.
- **(b) Cache the fallback for a short negative-TTL (e.g. 5 min)** to avoid hammering a down provider,
  accepting up to 5 min of stale-fallback after recovery. Trade-off: fewer failing round-trips, slightly
  slower self-heal, one more tunable.

## Assumptions

- The VietQR generate endpoint is `POST https://vietqr.vn/api/vietqr/generate` accepting a JSON body of
  the shape `{ accountNo, accountName, acqId (BIN) | bankCode, amount, addInfo, format, template }` and
  returning a JSON envelope whose QR string lives at a `qrCode` (or `data.qrCode`) field. The exact
  contract is a third-party detail not in the repo; the `VietQr` provider tolerates both bare and
  `data`-wrapped shapes and treats any parse miss as a failure → local fallback. **If this endpoint
  requires an API key / `x-client-id`+`x-api-key`, the `VietQr` provider will always fall back to Local
  (logged) until credentials are configured — acceptable because `Local` is the default.**
- The bank directory endpoint is `GET https://vietqr.vn/api/vietqr/banks` returning either a bare array
  or `{ data: [...] }`, each entry carrying `caiValue` (BIN), `bankCode`, `bankName`, `bankShortName`,
  `imageId` (matching the web normalizer).
- Config defaults: `Banks:QrProvider = "Local"`; `Banks:VietQr:BaseUrl = "https://vietqr.vn"`;
  `BanksPath = "/api/vietqr/banks"`; `GeneratePath = "/api/vietqr/generate"`;
  `ImagePath = "/api/vietqr/images"` (logoUrl = `{BaseUrl}{ImagePath}/{imageId}`).
- Cache TTL 24h, key `banks:list`.
- The endpoint returns the directory (possibly large: ~58+ entries); no paging is required (matches the
  client, which loads the whole list).
- The port of the 58-bank snapshot is a straight data copy; the array is already normalized and sorted.

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/`. New services use DiDecoration `[ScopedService]`;
> the typed `HttpClient`, `IMemoryCache`, and `BanksOptions` are wired with standard .NET in `Program.cs`
> (Decision 3). No entity, no EF mapping, **no migration** (Decision 5). All user-facing strings +
> Swagger summaries are Vietnamese.

### Step 1 — Options + response model (`Models/Banks/`)

1. `Models/Banks/BanksOptions.cs` — plain class (repo convention):
   - `string QrProvider = "Local"`.
   - `VietQrOptions VietQr` (nested plain class): `string BaseUrl = "https://vietqr.vn"`,
     `string BanksPath = "/api/vietqr/banks"`, `string GeneratePath = "/api/vietqr/generate"`,
     `string ImagePath = "/api/vietqr/images"`.
   - Const `public const string SectionName = "Banks"`.
2. `Models/Banks/BankResponse.cs` — plain class: `string Bin`, `string Code`, `string Name`,
   `string ShortName`, `string LogoUrl`. (No `imageId`.)

### Step 2 — Bank directory provider (`Services/Api/Banks/`)

3. `Services/Api/Banks/BankDirectoryProvider.cs` — interface + record + sealed impl in one file:
   - `public interface IBankDirectoryProvider { Task<IReadOnlyList<ProviderBank>> ListAsync(CancellationToken ct); string BuildLogoUrl(string imageId); }`
   - `public sealed record ProviderBank(string Bin, string Code, string Name, string ShortName, string ImageId);`
   - `[ScopedService(typeof(IBankDirectoryProvider))] public sealed class VietQrBankDirectoryProvider(VietQrApiClient client, IOptions<BanksOptions> options) : IBankDirectoryProvider`
     — `ListAsync` calls `client.ListRawAsync(ct)`, normalizes each raw entry (`caiValue`→Bin,
     `bankCode`→Code, `bankName`→Name, `bankShortName`→ShortName, `imageId`), trims, and **drops any
     entry whose Bin fails `^\d{6}$`** (a compiled `Regex`). `BuildLogoUrl(imageId)` =
     `$"{BaseUrl}{ImagePath}/{imageId}"`.

### Step 3 — Bank directory service (cache + fallback + mapping)

4. `Services/Api/Banks/BankDirectoryFallback.cs` — `internal static class` holding
   `IReadOnlyList<ProviderBank> Snapshot` — the **58-bank snapshot ported verbatim** from
   `FairShareMonWeb/src/features/wallet/data/vietqrBanks.ts` (each `{ bin, code, name, shortName,
   imageId }` → `new ProviderBank(...)`). Include a header comment recording the source + capture date
   (2026-07-18) so it can be re-baked.
5. `Services/Api/Banks/BankDirectoryService.cs` — interface + sealed impl:
   - `public interface IBankDirectoryService { Task<IReadOnlyList<BankResponse>> ListAsync(CancellationToken ct); }`
   - `[ScopedService(typeof(IBankDirectoryService))] public sealed class BankDirectoryService(IBankDirectoryProvider provider, IMemoryCache cache, ILogger<BankDirectoryService> logger) : IBankDirectoryService`:
     - `ListAsync`: `cache.GetOrCreate`/`GetOrCreateAsync` on key `"banks:list"` with 24h absolute TTL,
       storing the mapped `IReadOnlyList<BankResponse>`. Inside the factory, call `provider.ListAsync`,
       map `ProviderBank → BankResponse` via `provider.BuildLogoUrl(imageId)`.
     - On a provider exception: log a warning and return the mapped **static fallback**
       (`BankDirectoryFallback.Snapshot` mapped via `provider.BuildLogoUrl`) — per **OQ-B**, do NOT
       write the fallback to the cache (self-heals next call) [recommendation (a); flip if the user
       chooses (b)].

### Step 4 — QR content providers + resolver (`Services/Api/Banks/`)

6. `Services/Api/Banks/IQrContentProvider.cs` — interface + request record:
   - `public sealed record QrContentRequest(string BankBin, string AccountNumber, string AccountHolderName, decimal Amount, string? AddInfo);`
   - `public interface IQrContentProvider { string Key { get; } Task<string> BuildContentAsync(QrContentRequest req, CancellationToken ct); }`
7. `Services/Api/Banks/LocalQrContentProvider.cs` —
   `[ScopedService(typeof(IQrContentProvider))] { Multiple = true }`, `Key => "local"`; primary ctor
   injects `IVietQrPayloadBuilder`; `BuildContentAsync` returns
   `Task.FromResult(builder.Build(req.BankBin, req.AccountNumber, req.Amount, req.AddInfo))` — byte-
   identical to today. (`AccountHolderName` is unused by the local TLV builder.)
8. `Services/Api/Banks/VietQrRemoteQrContentProvider.cs` —
   `[ScopedService(typeof(IQrContentProvider))] { Multiple = true }`, `Key => "vietqr"`; primary ctor
   injects `VietQrApiClient`, `IBankDirectoryService`, `IVietQrPayloadBuilder`,
   `ILogger<VietQrRemoteQrContentProvider>`. `BuildContentAsync`:
   1. resolve `bankCode` from the directory by BIN (`(await directory.ListAsync(ct)).FirstOrDefault(b =>
      b.Bin == req.BankBin)?.Code`); unresolved → log warning + **fall back** to the local builder.
   2. call `client.GenerateAsync(bankCode/BIN, req.AccountNumber, req.AccountHolderName, req.Amount,
      req.AddInfo, ct)`; on null/exception → log warning + **fall back** to the local builder.
   3. else return the remote `qrCode`.
9. `Services/Api/Banks/QrContentProviderResolver.cs` — interface + sealed impl:
   - `public interface IQrContentProviderResolver { IQrContentProvider Resolve(); }`
   - `[ScopedService(typeof(IQrContentProviderResolver))] public sealed class QrContentProviderResolver(IEnumerable<IQrContentProvider> providers, IOptions<BanksOptions> options) : IQrContentProviderResolver`
     — `Resolve()` matches a provider whose `Key` equals `options.Value.QrProvider`
     (case-insensitive); unknown/missing → the `"local"` provider (never null).

### Step 5 — Typed VietQR HTTP client

10. `Services/Api/Banks/VietQrApiClient.cs` — typed `HttpClient` wrapper (registered by
    `AddHttpClient<VietQrApiClient>()`, NOT DiDecoration). Primary ctor `(HttpClient http,
    IOptions<BanksOptions> options, ILogger<VietQrApiClient> logger)`:
    - `Task<IReadOnlyList<VietQrRawBank>> ListRawAsync(CancellationToken ct)` — GET `{BanksPath}`,
      tolerate a bare array or `{ data: [...] }`, deserialize to raw DTOs; throw on non-success/unreadable
      (the directory service catches → fallback).
    - `Task<string?> GenerateAsync(string bankCodeOrBin, string accountNo, string accountName, decimal amount, string? addInfo, CancellationToken ct)` —
      POST `{GeneratePath}` with the request body; return `qrCode`/`data.qrCode`; return `null` on
      non-success/unreadable (caller falls back).
    - Internal DTOs (`Models/Banks/VietQrRawBank.cs`, `VietQrGenerateRequest.cs`,
      `VietQrGenerateResponse.cs`) — plain classes for (de)serialization; no auth/locale headers are sent
      to the third party (mirrors the web-side "no app headers to VietQR" rule).

### Step 6 — Wire WalletQrService onto the resolver

11. Edit `Services/Api/Wallet/WalletQrService.cs` (impact analysis below): replace the injected
    `IVietQrPayloadBuilder payloadBuilder` with `IQrContentProviderResolver qrContentResolver`. The
    `IWalletQrService` interface, method signatures, and all behavior (resource-owned resolution, 12000/
    12001/12002/12003 codes, tier gate, `format=payload`, composite) are **unchanged**.
    - `GenerateExpenseQrAsync`: `var provider = qrContentResolver.Resolve(); var payload = await
      provider.BuildContentAsync(new QrContentRequest(account.BankBin, account.AccountNumber,
      account.AccountHolderName, expense.Total, expense.Name), cancellationToken);`
    - `GenerateEventQrAsync`: the current synchronous `owing.Select(...)` becomes an **await loop** (or
      `Task`-aware projection) building each `QrCompositeItem` via `await provider.BuildContentAsync(...)`
      (amount `-row.Balance`, addInfo `$"{balance.EventName} - {row.MemberName}"`); the label
      (`FormatMoney`) is untouched.

### Step 7 — Controller

12. `Controllers/BanksController.cs` — derives from `AppController`; explicit
    `[Route("api/v{version:apiVersion}/banks")]` + `[ApiVersion("1.0")]`; primary ctor
    `(IBankDirectoryService bankDirectoryService)`:
    - `[HttpGet] ListAsync(CancellationToken ct)` → `ApiResult<IReadOnlyList<BankResponse>>.Success(await
      bankDirectoryService.ListAsync(ct))`. Authenticated (no `[AllowAnonymous]`).
    - Vietnamese `[SwaggerOperation(Summary = "Danh sách ngân hàng", Description = "Trả về danh mục ngân
      hàng (BIN, mã, tên, tên ngắn, URL logo) để hiển thị bộ chọn ngân hàng.")]`,
      `[SwaggerResponse(200, "Lấy danh sách ngân hàng thành công.", typeof(ApiResult<List<BankResponse>>))]`,
      `[SwaggerResponse(401, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", typeof(ApiResult))]`.
      Add a `403` Swagger response only if OQ-A resolves to (b).

### Step 8 — Program.cs + config wiring

13. Edit `Program.cs` (standard .NET, Decision 3), near the AutoMapper/validators block:
    - `builder.Services.AddMemoryCache();`
    - `builder.Services.Configure<BanksOptions>(builder.Configuration.GetSection(BanksOptions.SectionName));`
    - `builder.Services.AddHttpClient<VietQrApiClient>();` (a sensible timeout, e.g.
      `client.Timeout = TimeSpan.FromSeconds(5)`, so an outage fails fast into the fallback/local path).
14. Edit `appsettings.json` + `appsettings.Development.json`: add the `Banks` section
    (`QrProvider: "Local"` + the `VietQr` sub-object with BaseUrl/BanksPath/GeneratePath/ImagePath).

### Step 9 — Vietnamese message keys

- The banks endpoint **never fails** (static fallback) → **no new `ErrorCodes` and no new
  user-facing error `MessageKeys`** are required. Provider/remote failures are logged (server-side, not
  user-facing).
- User-facing strings are the Vietnamese Swagger summaries above; the list success uses the existing
  `ApiResult<T>.Success(...)` default. If OQ-A = (b), the 403 reuses the existing
  `MessageKeys.Error.PremiumFeatureRequired` + `MessageKeys.Feature.Wallet` (no new keys).

### Step 10 — Tests (owned by the test-engineer; definitive list)

Reuse the shipped harness: `[Collection("AuthIntegration")]`; endpoint tests use the
`ExpenseApiTestBase`/`AuthApiTestBase` families (real MariaDB for auth); DB-dependent tests are
`[SkippableFact]` (skip when MariaDB unreachable), never EF InMemory; unique lowercase username prefix
per class; dispose-time cleanup.

**Unit (no DB):**
- `VietQrBankDirectoryProviderTests` — normalizes raw entries (`caiValue`→Bin etc.), trims, and drops
  entries whose Bin fails `^\d{6}$`; `BuildLogoUrl` = `{BaseUrl}{ImagePath}/{imageId}`. Uses a fake
  `VietQrApiClient` (or a stubbed `HttpMessageHandler`).
- `BankDirectoryServiceTests` — maps `ProviderBank → BankResponse` (logoUrl built, `imageId` absent);
  a second call within TTL is served from cache (provider invoked once); a provider exception returns the
  mapped static fallback (non-empty) and (per OQ-B (a)) does NOT cache it (a subsequent call retries the
  provider). Uses a fake provider + a real `MemoryCache`.
- `QrContentProviderResolverTests` — `Resolve()` returns the `local` provider by default, the `vietqr`
  provider when `Banks:QrProvider = "VietQr"` (case-insensitive), and the `local` provider for an
  unknown key.
- `LocalQrContentProviderTests` — `BuildContentAsync` output is byte-identical to
  `IVietQrPayloadBuilder.Build(...)` for the same inputs.
- `VietQrRemoteQrContentProviderTests` — success path returns the remote `qrCode`; a remote
  failure/exception falls back to the local builder (asserts the payload equals the local builder's
  output); an unresolved bankCode falls back to local; both fallbacks log a warning. Uses a fake
  `VietQrApiClient` + fake `IBankDirectoryService` + real `VietQrPayloadBuilder`.
- `VietQrApiClientTests` — over a stubbed `HttpMessageHandler`: `ListRawAsync` parses a bare array AND a
  `{ data: [...] }` wrapper; throws on non-success; `GenerateAsync` returns `qrCode`/`data.qrCode` and
  returns `null` on non-success/unreadable; **no `Authorization`/`Accept-Language`/`X-Time-Zone` headers
  are sent** to VietQR.
- **Update `WalletQrServiceTests.CreateService`** — swap the `IVietQrPayloadBuilder` argument for an
  `IQrContentProviderResolver` (a fake resolving to a `LocalQrContentProvider` over the real
  `VietQrPayloadBuilder`, preserving the existing byte-for-byte payload assertions). All existing M9
  `WalletQrService` assertions must stay green.

**Integration (real MariaDB — `BanksEndpointTests`, `WebApplicationFactory`):**
- `GET /api/v1/banks` authenticated → 200, `ApiResult<List<BankResponse>>` with a **non-empty** list;
  each item has a fully-formed `logoUrl` (`https://vietqr.vn/api/vietqr/images/...`) and **no `imageId`
  field** in the JSON. Deterministic: the factory overrides `VietQrApiClient`'s `HttpMessageHandler`
  with a stub returning a small fixed directory (never hits real VietQR).
- Provider-down path: with the stub handler throwing/non-200, the endpoint still returns 200 with the
  **static fallback** list (proves "never fails").
- Anonymous → 401.
- If OQ-A = (b): a Free user → 403 (13003); a Premium user → 200.

## Impact Analysis

**APIs:**
- **New endpoint:** `GET api/v1/banks` (`BanksController`) → `ApiResult<List<BankResponse>>`. No change
  to any existing endpoint contract.
- **No change to the QR endpoints' external contract** — `GET /expenses/{uuid}/qr` and
  `GET /events/{uuid}/qr` behave identically (only the internal payload source becomes pluggable, default
  `Local` = today's behavior).

**Database:**
- **None.** No entity, no EF mapping, **no migration** (Decision 5). `bank_accounts` unchanged.

**Infrastructure:**
- **First outbound HTTP call:** `AddHttpClient<VietQrApiClient>()` (typed client, short timeout).
- **First `IMemoryCache` use:** `AddMemoryCache()` (in-process; part of the shared framework — **no new
  NuGet package**).
- New `Banks` config section in `appsettings.json` + `appsettings.Development.json`. No Redis, no
  background workers, no new dependency.

**Services:**
- **New (`Services/Api/Banks/`):** `IBankDirectoryProvider`/`VietQrBankDirectoryProvider` (+ `ProviderBank`),
  `IBankDirectoryService`/`BankDirectoryService`, `BankDirectoryFallback`, `IQrContentProvider`
  (+ `QrContentRequest`), `LocalQrContentProvider`, `VietQrRemoteQrContentProvider`,
  `IQrContentProviderResolver`/`QrContentProviderResolver`, `VietQrApiClient`. New `Models/Banks/*`
  (`BanksOptions`, `BankResponse`, `VietQrRawBank`, `VietQrGenerateRequest`, `VietQrGenerateResponse`).
  New `Controllers/BanksController.cs`.
- **Modified:** `Services/Api/Wallet/WalletQrService.cs` (inject `IQrContentProviderResolver` instead of
  `IVietQrPayloadBuilder`; event loop becomes async) — **MEDIUM risk, interface unchanged** (see below);
  `Program.cs` (memory cache + typed client + options); `appsettings.json` + `appsettings.Development.json`.
- **Left intact / reused:** `VietQrPayloadBuilder` (now reached via `LocalQrContentProvider`),
  `QrImageService`, `BankAccountsService`, `BankAccountRepository`, `TierService` — **LOW risk**.

**Impact analysis already run (blast radius):**
- **`WalletQrService` — MEDIUM risk.** Only the private field/ctor dependency changes; the
  `IWalletQrService` interface is untouched, so `ExpensesController.GetQrAsync` and
  `EventsController` QR route are unaffected. The only test touch-point is
  `WalletQrServiceTests.CreateService` (swap one constructor argument).
- **`VietQrPayloadBuilder` — LOW risk.** Left intact and now invoked through `LocalQrContentProvider`
  (and as the VietQr provider's fallback); its existing unit tests stay green unchanged.

**Documentation:**
- This planning doc; Vietnamese Swagger on `GET /api/v1/banks`; a short annotation added to
  `planning/wallet-and-qr.md` (OQ4 / OQ1c superseded/extended). A follow-up FairShareMonWeb planning doc
  (out of scope) will switch the SPA picker to `GET /api/v1/banks` and retire the raw-`fetch` exception +
  client snapshot.

## Decision Log

> Approved by the user before this doc; recorded as decisions, not reopened.

### Decision 1 — QR payload content is provider-configurable (Local default + optional VietQr)
Ship a `Local` provider (default; a thin adapter over the existing `IVietQrPayloadBuilder`, byte-
identical) and a `VietQr` provider that POSTs to `https://vietqr.vn/api/vietqr/generate` and returns its
`qrCode`. Selected by `Banks:QrProvider` (default `Local`). The VietQr provider resolves `bankCode` from
the directory by BIN and **falls back to the local builder on any remote failure or unresolved bankCode
(logged warning)**, so QR never breaks. **This extends M9 OQ1c** (which rejected calling an external
VietQR service): the external call is now an opt-in, always-fallback provider, not the default.
*Alternatives considered:* keep only the hand-rolled builder (rejected — the user wants VietQR-parity as
an option); make VietQr the default (rejected — self-contained/offline-safe default is Local).

### Decision 2 — Logo images via a direct provider URL
The backend returns a fully-built `logoUrl` (`{BaseUrl}{ImagePath}/{imageId}`); the browser fetches the
image directly from VietQR. `imageId` never leaves the backend.
*Alternatives considered:* proxy the image bytes through our API (rejected — needless bandwidth/latency
for a public CDN asset); return `imageId` and let the client build the URL (rejected — that keeps VietQR
knowledge in the SPA, the thing we are removing).

### Decision 3 — HTTP + config wiring is standard .NET
Use `AddHttpClient<VietQrApiClient>()`, `Configure<BanksOptions>(...)`, `AddMemoryCache()` in
`Program.cs`, NOT the DiDecoration `[HttpClientService]`/`[Option]` scanners (their
`RegisterHttpClients`/`RegisterOptions` are not wired in `Program.cs` today). This is the repo's first
outbound HTTP call and first `IMemoryCache` use.
*Alternatives considered:* enable the DiDecoration scanners (rejected — introduces a second, unused
registration path just for this feature).

### Decision 4 — Caching via `IMemoryCache` + committed static fallback
`IMemoryCache`, key `banks:list`, TTL 24h, with a committed static 58-bank fallback (ported from
`FairShareMonWeb/src/features/wallet/data/vietqrBanks.ts`) so the endpoint never fails.
(The fallback's *cache* behavior on provider failure is OQ-B.)

### Decision 5 — No DB schema change
`bank_accounts` still stores BIN + display name; no banks table, no migration. **This extends M9 OQ4**
(no banks reference table): the directory is served from a provider + in-memory cache + static fallback,
still without a DB table.

### Decision 6 — Provider abstraction shape (`Services/Api/Banks/`)
`IBankDirectoryProvider` (`ListAsync` + `BuildLogoUrl`) with a `ProviderBank(Bin,Code,Name,ShortName,
ImageId)` record; VietQr impl normalizes raw entries and drops non-`^\d{6}$` BINs.
`IQrContentProvider` (`Key` + `BuildContentAsync`) with `LocalQrContentProvider` ("local") and
`VietQrRemoteQrContentProvider` ("vietqr"), both `Multiple = true`; `IQrContentProviderResolver.Resolve()`
picks by `Banks:QrProvider`. `IBankDirectoryService.ListAsync` wraps the provider with cache + fallback
and maps `ProviderBank → BankResponse`. `VietQrApiClient` is the typed HTTP wrapper
(`ListRawAsync`, `GenerateAsync`).

### Inherited decisions (locked upstream — NOT reopened)
QR image rendering stays server-side (QRCoder/SkiaSharp, M9) — only TLV *content* becomes pluggable;
resource-owned 404-never-403; money `decimal`, non-negative; `AppController` LOCKED; domain terms
(wallet/bank account, expense, event); M10 Premium gating of wallet CRUD + QR generation.

## Progress Log

### 2026-07-19

- Started planning the bank-directory-provider feature (approach pre-approved by the user).
- Required reading completed: `planning/wallet-and-qr.md` (M9 — the shipped wallet + QR foundation, all
  17 OQs, Decision Log, Final Outcome); `FairShareMonApi/CLAUDE.md` (stack, architecture,
  attribute-driven DI, migration rule); `.claude/rules/rule.md` (planning template);
  `FairShareMonWeb/CLAUDE.md` (the SPA's raw-`fetch` VietQR exception + binary-response rules).
- Grounded the plan in the live code: `Services/Api/Wallet/WalletQrService.cs` (the exact injection point
  to change — `IVietQrPayloadBuilder` → `IQrContentProviderResolver`; event `Select` → await loop) and
  `VietQrPayloadBuilder.cs` (the `Build` signature the Local provider adapts); `Controllers/
  BankAccountsController.cs` (explicit kebab-case `[Route]` + Vietnamese Swagger pattern for the new
  `BanksController`); `Program.cs` (no `HttpClient`/`IMemoryCache`/`[HttpClientService]` scanner wired
  today → standard .NET wiring is the fit); `Services/Api/Tiers/TierService.cs` (M10 Premium gate — the
  basis for OQ-A); `appsettings.json` (`Banks` section placement); and the web snapshot
  `features/wallet/data/vietqrBanks.ts` + `api/vietqrDirectoryApi.ts` (the 58-bank fallback + the raw-
  entry normalization to port).
- Recorded the six approved decisions + inherited locks; drafted the full Implementation Plan (new
  `Services/Api/Banks/` area, `Models/Banks/`, `BanksController`, `Program.cs`/config edits, the
  `WalletQrService` rewire) and the test list (unit + real-MariaDB endpoint).
- Ran impact analysis: `WalletQrService` MEDIUM (interface unchanged → QR routes unaffected; only
  `WalletQrServiceTests.CreateService` needs a one-arg swap); `VietQrPayloadBuilder` LOW (intact, reached
  via the Local provider; its tests stay green).
- Left two genuinely-unresolved points as Open Questions (OQ-A Premium-gating of `GET /api/v1/banks`;
  OQ-B fallback caching on provider failure), each with options + a recommendation, for the orchestrator
  to bring to the user.
- Added a short annotation to `planning/wallet-and-qr.md` noting this feature reverses/extends its OQ4
  (no server banks endpoint) and OQ1c (don't call VietQR to generate).

### 2026-07-19 (implementation)

- User resolved both open questions: **OQ-A → (a)** (authenticated-only, no Premium gate on
  `GET /api/v1/banks`) and **OQ-B → (a)** (serve the static fallback uncached; cache only successful
  provider results). Implemented exactly to those decisions.
- Created `Models/Banks/`: `BanksOptions.cs` (+ nested `VietQrOptions`), `BankResponse.cs`,
  `VietQrRawBank.cs`, `VietQrGenerateRequest.cs`, `VietQrGenerateResponse.cs`.
- Created `Services/Api/Banks/`: `BankDirectoryProvider.cs` (`IBankDirectoryProvider` + `ProviderBank`
  record + `VietQrBankDirectoryProvider`, `^\d{6}$` via `[GeneratedRegex]`), `BankDirectoryFallback.cs`
  (58-bank snapshot ported verbatim from `vietqrBanks.ts`), `BankDirectoryService.cs`
  (`IBankDirectoryService` + impl: `IMemoryCache` key `banks:list`, 24h TTL, cache-successful-only,
  uncached fallback per OQ-B (a)), `IQrContentProvider.cs` (+ `QrContentRequest`), `LocalQrContentProvider.cs`
  (`Multiple = true`, key `local`), `VietQrRemoteQrContentProvider.cs` (`Multiple = true`, key `vietqr`,
  resolve bankCode by BIN → `/api/vietqr/generate` → fall back to local on any failure/unresolved),
  `QrContentProviderResolver.cs` (`IQrContentProviderResolver` + impl, picks by `Banks:QrProvider`,
  case-insensitive, defaults to `local`), `VietQrApiClient.cs` (typed `HttpClient`: `ListRawAsync`
  tolerates bare-array or `{ data: [...] }` and throws on failure; `GenerateAsync` returns
  `qrCode`/`data.qrCode`, `null` on any failure).
- Created `Controllers/BanksController.cs` (`AppController`, route override `api/v{version:apiVersion}/banks`,
  `GET` → `ApiResult<IReadOnlyList<BankResponse>>`, Vietnamese Swagger, authenticated, no Premium gate).
- Edited `Program.cs`: `AddMemoryCache()`, `Configure<BanksOptions>(...)`, `AddHttpClient<VietQrApiClient>()`
  with a 10s timeout (standard .NET wiring, not the DiDecoration scanners).
- Edited `Services/Api/Wallet/WalletQrService.cs`: swapped `IVietQrPayloadBuilder` for
  `IQrContentProviderResolver`; expense QR awaits `provider.BuildContentAsync(...)`; event QR converted from
  a synchronous `Select` to an `await` loop building `QrCompositeItem`. `IWalletQrService` interface
  unchanged.
- Edited `appsettings.json` + `appsettings.Development.json`: added the `Banks` section (QrProvider `Local`;
  VietQr BaseUrl/BanksPath/GeneratePath/ImagePath).
- Updated `FairShareMonApi.Tests/WalletQrServiceTests.cs` `CreateService` to construct `WalletQrService`
  with a `StubQrContentProviderResolver` returning a `LocalQrContentProvider` over the real
  `VietQrPayloadBuilder` (byte-for-byte payload assertions preserved). Did NOT author the new test files
  (test-engineer owns those).
- `dotnet build .\FairShareMonApi.sln`: **succeeded, 0 errors**. Warnings are all pre-existing and
  unrelated (AutoMapper 13.0.1 pinned-vuln NU1903; one `ExpensesEndpointTests.cs` CS8619 nullability) — no
  new warnings from this feature.
- `dotnet test .\FairShareMonApi.sln`: **654 passed, 0 failed, 472 skipped** (DB-backed tests skip when
  MariaDB is unreachable in this environment). All `WalletQrService` unit tests stayed green.

### 2026-07-19 (tests)

- Test-engineer authored the full test suite for this feature under `FairShareMonApi.Tests/` (test project
  only; no product code touched). New shared harness helpers: `Infrastructure/StubHttpMessageHandler.cs`
  (records requests, replies deterministically or throws — the repo's first outbound-HTTP stub) and
  `Infrastructure/CapturingLogger.cs` (asserts the fallback warnings). New endpoint factories in
  `Infrastructure/BanksWebApplicationFactories.cs` (`BanksStubWebApplicationFactory` — fixed 3-entry
  directory incl. one dropped 5-digit BIN + pinned `Banks` URLs; `BanksProviderDownWebApplicationFactory`
  — stub returns HTTP 500), both overriding `VietQrApiClient`'s primary handler via
  `ConfigureTestServices`/`ConfigurePrimaryHttpMessageHandler` so no test ever hits vietqr.vn.
- Unit tests added (all run, no DB): `VietQrApiClientTests` (bare-array + `{data:[...]}` parse, throws on
  non-success/unexpected shape, `qrCode`/`data.qrCode`, null on any generate failure/transport throw, no
  app auth/locale headers sent); `VietQrBankDirectoryProviderTests` (field normalization + trim, drops
  non-`^\d{6}$` BINs, `BuildLogoUrl` composition); `BankDirectoryServiceTests` (success mapped with built
  logoUrl + cached under `banks:list`; cache hit → provider called once; provider throws on cold cache →
  non-empty static fallback, NOT cached, retried next call = self-heal per OQ-B(a));
  `QrContentProviderResolverTests` (local by default/unset/unknown, vietqr case-insensitively);
  `LocalQrContentProviderTests` (key `local`; byte-identical to `VietQrPayloadBuilder`);
  `VietQrRemoteQrContentProviderTests` (happy path returns remote `qrCode`; remote failure / no-qrCode /
  unresolved bankCode all fall back to the local builder byte-identically + log a warning; unresolved case
  short-circuits with no HTTP call).
- Integration tests added (real MariaDB, `[SkippableFact]`): `BanksEndpointTests` (Free-tier authenticated
  GET → 200, envelope `isSuccess:true`, `data` non-empty camelCase `{bin,code,name,shortName,logoUrl}` with
  a fully-built logoUrl and NO imageId field, 5-digit-BIN entry dropped, VCB logoUrl exact — proves OQ-A(a)
  no Premium gate; anonymous → 401 not 403) and `BanksEndpointFallbackTests` (stub 500 → still 200 with the
  full static fallback, ≥50 banks, each logoUrl `https://vietqr.vn/api/vietqr/images/…`, no imageId).
- `dotnet build`: 0 errors (only the two pre-existing warnings: AutoMapper NU1903, `ExpensesEndpointTests`
  CS8619). `dotnet test`: **690 passed, 0 failed, 475 skipped** (+36 new unit tests pass; +3 new integration
  tests skip cleanly because MariaDB is unreachable in this environment). All prior tests (incl. the
  M9/M10 `WalletQrService`/wallet suites the implementer rewired) stayed green.

### 2026-07-19 (review fixes)

- Code review surfaced one BLOCKING and one MEDIUM correctness issue; both fixed:
  - **BLOCKING** — the `HttpClient.Timeout` (10s) surfaces as `TaskCanceledException`, which *is* an
    `OperationCanceledException`, so the `catch … when (exception is not OperationCanceledException)` filters
    in `BankDirectoryService.ListAsync` and `VietQrApiClient.GenerateAsync` let a *timeout* escape → HTTP 500
    on exactly the slow-provider failure mode the fallback was meant to survive (and it bit the default
    `Local` config's `/api/v1/banks` path). Changed both filters to gate on the **passed** token
    (`when (!cancellationToken.IsCancellationRequested)`): a genuine caller cancellation still rethrows, but a
    timeout is treated as a provider failure → static fallback / local-builder fallback.
  - **MEDIUM** — a successful-but-empty provider response (HTTP 200 `[]`, or schema drift dropping every entry
    at the `^\d{6}$` filter) was mapped to an empty list and cached for 24h, emptying the picker for a day.
    `BankDirectoryService` now treats an empty mapped list as a provider miss: serve the static fallback
    **uncached** (same as the exception branch), so it self-heals on the next call.
  - NIT — `BanksController` `[SwaggerResponse]` type aligned to the actual `ApiResult<IReadOnlyList<BankResponse>>`.
- Re-ran: `dotnet build` 0 errors; `dotnet test` **690 passed, 0 failed, 475 skipped**.

## Final Outcome

Shipped the server-side bank directory behind a provider abstraction and a config-selectable QR content
provider, exactly per the approved plan with OQ-A → (a) and OQ-B → (a).

- **New endpoint** `GET /api/v1/banks` (authenticated, not Premium-gated) → `ApiResult<List<BankResponse>>`
  `{ bin, code, name, shortName, logoUrl }`; `imageId` never leaves the backend; endpoint never fails
  (static 58-bank fallback).
- **Bank directory** sourced via `IBankDirectoryProvider` (VietQR impl, drops non-`^\d{6}$` BINs), wrapped
  by `IBankDirectoryService` with `IMemoryCache` (key `banks:list`, 24h) — successful results cached; on
  provider failure the static fallback is served **uncached** (self-heals next call).
- **QR content** is now provider-selectable via `Banks:QrProvider` (`Local` default = byte-identical to the
  existing `IVietQrPayloadBuilder`; `VietQr` optional = `/api/vietqr/generate` with always-fallback to
  local). `WalletQrService` consumes the resolver; the `IWalletQrService` contract and both QR routes are
  unchanged.
- Standard .NET wiring for `IMemoryCache`, `BanksOptions`, and the typed `VietQrApiClient` (10s timeout).
- **No DB change / no migration.** Build clean (0 errors, no new warnings); full test suite green
  (654 passed / 0 failed / 472 DB-skipped).

## Future Improvements

- **FairShareMonWeb migration (separate doc):** switch the SPA bank picker to `GET /api/v1/banks` via
  `src/lib/api/client.ts`, and retire the raw-`fetch` exception (`vietqrDirectoryApi.ts`) + the client
  snapshot (`vietqrBanks.ts`) once the backend endpoint ships.
- A second directory provider (e.g. a curated NAPAS list or another aggregator) — the
  `IBankDirectoryProvider` seam already isolates it.
- A refresh/admin endpoint to force-evict `banks:list` (cache bust) without a 24h wait.
- Distributed cache (Redis, already wired) for the directory if the API scales to multiple instances and
  a shared 24h cache is preferred over per-instance memory.
- Configurable VietQR generate credentials (`x-client-id`/`x-api-key`) if the official generate API is
  adopted over the public endpoint.
