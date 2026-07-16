# Localization subsystem — localize runtime user-facing messages (i18n)

## Objective

Localize the ~175 runtime user-facing messages the API returns — error messages, FluentValidation
`.WithMessage` texts, `ApiResult.SuccessMessage` texts, and the 3 timezone-converter parse errors — so
each response comes back in the caller's requested language. The current strings are all inline
Vietnamese literals; this feature moves them into `.resx` resources resolved per request via ASP.NET
`IStringLocalizer`, driven by the standard `Accept-Language` header (plus a `?culture=` query
override). Vietnamese remains the neutral/fallback content, so existing Vietnamese-asserting behavior
is preserved by default while English becomes available for `en-US` callers.

This is **API-message i18n only**. Nothing new is stored in the database, no DB-content localization is
attempted, and Swagger annotations (dev-facing) stay Vietnamese. This is a cross-cutting foundation
change with **no schema migration** and **no new NuGet package**.

## Background

Current behavior verified against live code:

- **A resx scaffold exists but is broken (blocker).** The user added
  `Localization/StringResources/StringResources.vi-VN.resx` + `.en-US.resx` (both effectively EMPTY —
  only the resx header, zero `<data>` entries) plus their `.Designer.cs`. There is a **three-way path
  mismatch** that would throw `MissingManifestResourceException` at runtime:
  1. Files physically live in `FairShareMonApi/Localization/StringResources/`.
  2. `FairShareMonApi.csproj` `EmbeddedResource Update=` and `Compile Update=` paths point at
     `Localization\Resources\StringResources.*` (a folder that does not exist).
  3. The `.Designer.cs` `ResourceManager` base name is
     `"FairShareMonApi.Localization.Resources.StringResources.vi-VN"` and the namespace is
     `FairShareMonApi.Localization.Resources`; the Designer classes are `internal`
     (`StringResources_vi_VN`).
  There is **no neutral `StringResources.resx`** (no culture suffix) and **no `NeutralResourcesLanguage`
  attribute**. `AddLocalization` / `UseRequestLocalization` are **not** wired in `Program.cs`.
  Nothing in the codebase references these resources yet.
- **Error messages** are inline literals passed to `new ErrorException(code, "…", httpStatus?)`.
  Verified counts: **36 `new ErrorException(...)` call sites across 10 files** — `AuthService` (9),
  `ExpensesService` (9), `WalletQrService` (4), `TierService` (4), `AdminUserService` (3),
  `SharesService` (2), `EventsService` (2), `MembersService` (1), `CategoriesService` (1), and **1 in
  the LOCKED `Controllers/AppController.cs`** (the `Unauthorized` "Phiên đăng nhập không hợp lệ hoặc đã
  hết hạn." guard). `ErrorCodes.cs` holds only int constants (no messages). Some messages are
  **interpolated**, e.g. `TierService` embeds the limit numbers
  (`$"Tài khoản Free chỉ được tạo tối đa {_maxMembers} thành viên…"`).
- **Two envelope hardcoded strings** live in `Attributes/MvcFilters/ErrorHandlerFilter.cs`:
  `ValidationMessage = "Dữ liệu gửi lên không hợp lệ."` and the per-field fallback
  `"Giá trị không hợp lệ."`; `Middlewares/ErrorHandlerMiddleware.cs` has the generic 500 fallback
  `"Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau."`.
- **FluentValidation** — **98 `.WithMessage(...)` across 28 validator classes** under `Validators/**`.
  Some are interpolated with the validator's `const` limits (e.g.
  `$"Tên thành viên không được vượt quá {NameMaxLength} ký tự."`). Validation is **manual only**:
  services inject `IValidator<T>` and validate explicitly; a thrown `ValidationException` is caught by
  `ErrorHandlerFilter` and shaped into `error.fields` (field name camelCased → `string[]`).
- **`ApiResult.SuccessMessage(string)`** — **19 controller call sites across 9 controllers**
  (`ExpensesController` 4, `AdminController` 4, `CategoriesController` 2, `BankAccountsController` 2,
  `AuthController` 2, `EventsController` 2, `TagsController` 1, `MembersController` 1,
  `HealthController` 1). `SuccessMessage` wraps `Data = new { Message = message }`.
- **Tz-converter** — `Serialization/UtcAwareDateTimeConverter.cs`
  (`RequestDateTimeSerializer.Read`) throws 3 Vietnamese `JsonException` messages from inside
  System.Text.Json: `"Giá trị ngày giờ phải là chuỗi ISO-8601."`, `"Giá trị ngày giờ không hợp lệ."`,
  `$"Giá trị ngày giờ không hợp lệ: {raw}"`. The converter is a singleton that already resolves
  per-request state via `IHttpContextAccessor` (the exact hook a localizer would reuse).
- **Request-context pattern to mirror.** `Auth/IContextAuthenticated.cs` and `Auth/IRequestTimeZone.cs`
  are scoped accessors over `IHttpContextAccessor`; `Middlewares/RequestTimeZoneMiddleware.cs` resolves
  the `X-Time-Zone` header once into `HttpContext.Items`. `Program.cs` registers
  `AddHttpContextAccessor()` (L40), adds JSON converters via `AddControllers().AddJsonOptions(...)`
  (L31–38), and the pipeline (L139–147) runs `UseRouting → RequestTimeZoneMiddleware →
  ErrorHandlerMiddleware → UseAuthentication → UseAuthorization → MapControllers`.
- **Tests pinning exact Vietnamese strings.** Verified ~86 assertions across 12 test files touch
  message text: validator `.WithErrorMessage(...)` (AuthValidatorsTests 14, BankAccountValidatorsTests
  12, EventValidatorsTests 12, ExpenseValidatorsTests 18, CategoriesValidatorsTests 10,
  MembersValidatorsTests 5, StatsValidatorsTests 5, TagsValidatorsTests 5), service `.Message`
  (TierServiceTests 2), and envelope/`ApiResult` assertions (ApiResultTests 1). Add endpoint-level
  envelope assertions in integration tests. (The task brief estimated ~91 across 16 files; verified
  live counts are used here.)
- **Config precedent.** `appsettings.json` has an `"App"` section (`App:DefaultTimeZone`).
- **Reference (portable pieces only).** `quick-ordering` `Extensions/WebBuilderExtensions.cs`
  `UseCustomLocalization` builds a `RequestLocalizationOptions` with
  `QueryStringRequestCultureProvider` then `AcceptLanguageHeaderRequestCultureProvider`,
  `DefaultRequestCulture`, `SupportedCultures`/`SupportedUICultures`; `Program.cs` calls
  `.AddLocalization(options => options.ResourcesPath = "")`; its `LanguageResource.cs` is an empty
  public marker class and `LanguageResource.vi-vn.resx` sits next to it — but there is **no neutral
  resx**, so quick-ordering's message localization is scaffolded and unused (it localizes DB content
  instead, which does not apply to FairShareMon's user-authored data). Its `Services/Localization/*`
  (DB-content, `LocalizationValueConverter`, `LocalizedField`) is **out of scope**.

## Requirements

- Move all ~175 runtime user-facing strings into `.resx` under a structured key scheme, resolved per
  request via `IStringLocalizer`.
- Fix the resx layout/namespace/csproj blocker so a `StringResources` accessor resolves cleanly with a
  real resource read (no `MissingManifestResourceException`) and a clean `dotnet build`.
- Add the **neutral** `StringResources.resx` populated with the current Vietnamese strings verbatim
  (this is the fallback), and keep `StringResources.en-US.resx` populated with English translations.
- Wire `AddLocalization` + `UseRequestLocalization` (QueryString + AcceptLanguage providers,
  `DefaultRequestCulture = vi-VN`, `SupportedCultures`/`SupportedUICultures = { vi-VN, en-US }`) so
  `CurrentUICulture` is set per request and `IStringLocalizer` resolves accordingly.
- Localize errors at the envelope boundary, FluentValidation `.WithMessage`, `SuccessMessage`, the
  envelope hardcoded strings, and (decision-dependent) the 3 tz-converter messages.
- Preserve interpolation: interpolated messages become resource templates with `{0}`, `{1}` placeholders
  resolved via `string.Format`/`IStringLocalizer[key, args]`.
- Default culture (vi-VN) output must stay byte-for-byte identical to today's Vietnamese, so existing
  Vietnamese assertions keep passing under the default culture.
- Migrate/adapt the ~86 message-asserting tests; add `en-US` culture tests; pin `CurrentUICulture` in
  unit tests.
- No DB migration, no new NuGet package.

## Open Questions

Each option lists trade-offs; **(a) is the recommendation**. **All 10 resolved at the 2026-07-16
checkpoint — see the per-question "Answered" annotations and the consolidated Decision Log.** This
feature is **UNBLOCKED**; the Implementation Plan, files list, and test list below are synced to the
answers.

### OQ1 — Resource key-naming scheme
> **Answered 2026-07-16 (option a).** Semantic namespaced keys; error keys by enum-name
> (`Error.MemberNotFound`), validation `Validation.<Area>.<Rule>`, success `Success.<Action>`.

How should resource keys be structured, and how do error keys map to `ErrorCodes`?

- **(a) Recommended — semantic namespaced keys, error keys by enum-name.**
  `Error.MemberNotFound`, `Validation.Member.NameRequired`, `Validation.Member.NameTooLong`,
  `Success.MemberCreated`, `Envelope.ValidationFailed`, `Envelope.InternalError`,
  `Serialization.DateTimeMustBeString`, etc. Error keys mirror the `ErrorCodes` **constant name**
  (`Error.<ConstantName>`), which reads clearly, is stable, and is trivially derivable at the throw site
  and in a central error→key map. Trade-off: two parallel identifiers (numeric code + key), but the
  numeric code stays the machine contract and the key is human-facing only.
- **(b) Error keys by numeric code** — `Error.3000`. Fully mechanical (a single `Error.{code}` lookup),
  no hand-maintained map. Trade-off: opaque keys, resx is unreadable, easy to mis-enter a number,
  and interpolated errors still need a per-code arg contract.
- **(c) Flat unstructured keys** — `MemberNotFound`, `NameRequired`. Simplest, but collisions are
  likely across 175 keys and there is no grouping for reviewers/translators.

### OQ2 — `ErrorException` shape
> **Answered 2026-07-16 (option a).** `ErrorException` carries a resource key + optional format args;
> resolve at the envelope boundary (`ApiResult.Failure` / `ErrorHandlerFilter` / `ErrorHandlerMiddleware`);
> drop the literal message from all 36 throw sites. Interpolated messages (Tier limits, max-length) use
> `{0}` placeholders + `string.Format` with the args.

`ErrorException(int code, string message, int? httpStatus)` currently carries a resolved message.

- **(a) Recommended — carry a resource key + optional format args; resolve at the envelope.** Change to
  `ErrorException(int code, string messageKey, object[]? args = null, int? httpStatus = null)` (or a
  small `ErrorException.Create(code, key, args)` factory). The message text is **removed from all ~35
  service throw sites**; `ApiResult.Failure(ErrorException)` and the two envelope builders resolve
  `localizer[ex.MessageKey, ex.Args]` using the request's `CurrentUICulture`. This centralizes
  localization at exactly one boundary and keeps throw sites terse. Trade-offs: (i) churn at 35 sites
  (mechanical); (ii) `ErrorException.Message` (base `System.Exception.Message`) now holds the *key*, not
  the localized text — logging in `ErrorHandlerMiddleware` would log the key (acceptable, arguably
  better for log aggregation); (iii) the LOCKED `AppController` throw site (1) cannot be edited without
  explicit per-request permission — see OQ8.
- **(b) Keep the message parameter but pass a key string through it.** Minimal signature change; the
  envelope treats `ex.Message` as a key and localizes it. Trade-off: semantically confusing
  (`Message` that is not a message), and interpolation args have nowhere clean to live.
- **(c) Resolve at the throw site (inject `IStringLocalizer` into every service).** Each service builds
  the localized string before throwing. Trade-off: spreads localization across 10 services, needs the
  localizer injected everywhere, and defeats the single-boundary design.

### OQ3 — FluentValidation localization mechanism
> **Answered 2026-07-16 (option a).** `.WithMessage(x => localizer[key, args])` per rule, injecting
> `IStringLocalizer<StringResources>` into each of the 28 validators.

98 `.WithMessage` across 28 validators; validation is manual (`IValidator<T>`), output flows to
`error.fields`.

- **(a) Recommended — `.WithMessage(x => localizer[key, args])` per rule, with the localizer injected
  into each validator's primary constructor.** Explicit, per-rule, keeps the existing manual-validation
  flow and `error.fields` shape untouched; the localizer is resolved lazily per validation call so
  `CurrentUICulture` is correct. Interpolated limits become args (`localizer["Validation.Member.NameTooLong", NameMaxLength]`).
  Trade-off: touches all 28 validators and adds a ctor dependency to each (mechanical, uniform).
- **(b) A shared `IStringLocalizer`-backed message provider via `ValidatorOptions.Global`
  (`LanguageManager` / `MessageFormatter`).** Central wiring; validators reference message keys through
  a global resolver. Trade-off: FluentValidation's `LanguageManager` is designed for its built-in
  validator messages, not arbitrary app keys; mapping custom keys through it is awkward, and per-rule
  args/placeholders are harder to thread than option (a).
- **(c) Keep `.WithMessage` literals, translate only at a post-processing step.** Rejected — there is no
  clean seam to re-map free-text messages back to keys after the fact.

### OQ4 — Tz-converter (3 messages)
> **Answered 2026-07-16 (option a).** Localize the 3 tz-converter messages via
> `IHttpContextAccessor` → `IStringLocalizerFactory` inside the singleton converter.

The 3 `JsonException` messages are thrown inside System.Text.Json during model binding, before the
service layer.

- **(a) Recommended — localize via `IHttpContextAccessor` → `IStringLocalizerFactory`.** The converter
  is already a singleton holding `IHttpContextAccessor`; resolve `IStringLocalizerFactory` (also
  singleton-safe) and read `CurrentUICulture` (set by `UseRequestLocalization`, which runs before model
  binding). Consistent with every other message; falls back to neutral (Vietnamese) when there is no
  HttpContext, so it never throws. Trade-off: minor plumbing to pass the factory into the converter
  ctor (registered in `Program.cs` `AddJsonOptions`).
- **(b) Leave as-is (Vietnamese only).** These are rare edge parse errors on malformed datetime input.
  Trade-off: 3 strings stay un-localized — a small, defensible carve-out. Simpler; no converter change.

### OQ5 — Resx accessor + final folder/namespace (resolve the blocker)
> **Answered 2026-07-16 (option a).** Public marker type `StringResources` for
> `IStringLocalizer<StringResources>`; **move the resx + Designer files to `Localization/Resources/`** to
> match the existing csproj `Update=` paths + Designer base names (fixes the three-way mismatch). Verify a
> clean build + a real resource read (no `MissingManifestResourceException`).

Two independent choices: the accessor API and how to fix the three-way path mismatch.

- **Accessor — (a) Recommended: a public marker type `StringResources` for `IStringLocalizer<StringResources>`.**
  Add `Localization/StringResources.cs` as `public class StringResources;` (namespace matching the resx
  base name). Consumers inject `IStringLocalizer<StringResources>`. Idiomatic ASP.NET, strongly
  associated with the resx by type name + namespace, testable. Trade-off: the resx base name and the
  marker type's namespace/name must line up exactly.
  - **(b) `IStringLocalizerFactory.Create(baseName, location)`** — no marker type; call
    `factory.Create("StringResources", "FairShareMonApi")`. More flexible but stringly-typed and easier
    to misconfigure.
- **Folder/namespace fix — (a) Recommended: move the resx + Designer files to `Localization/Resources/`
  to match the existing csproj `Update=` paths and the Designer base name/namespace
  (`FairShareMonApi.Localization.Resources`).** Least churn to the (already-present) csproj + Designer;
  the marker type lives in `FairShareMonApi.Localization.Resources`. Delete the redundant separate
  vi-VN satellite (fold it into the neutral resx — see OQ6).
  - **(b) Keep files in `Localization/StringResources/` and rewrite the csproj `Update=` paths +
    Designer base names/namespaces to `Localization.StringResources`.** More edits, but the folder name
    matches the resource "family" name. Either is fine; pick one and make all three sides agree.
  - Note: with `AddLocalization(options => options.ResourcesPath = ...)`, the runtime lookup path must
    also agree with the chosen folder/namespace. The plan verifies a real resource read after the fix.

### OQ6 — Neutral resx vs vi-VN satellite
> **Answered 2026-07-16 (option a).** Add a neutral `StringResources.resx` (current Vietnamese strings
> verbatim) + `[assembly: NeutralResourcesLanguage("vi-VN")]`; keep `StringResources.en-US.resx`; drop the
> redundant separate vi-VN satellite. Make the Designer/accessor public.

The scaffold has `StringResources.vi-VN.resx` but **no neutral** `StringResources.resx`.

- **(a) Recommended — add a neutral `StringResources.resx` holding the Vietnamese strings verbatim, set
  `[assembly: NeutralResourcesLanguage("vi-VN")]`, and DROP the separate `StringResources.vi-VN.resx`
  (+ its Designer).** With `DefaultRequestCulture = vi-VN` and unknown cultures folding to the neutral
  resource, Vietnamese output is guaranteed for the default and for any unsupported `Accept-Language`.
  Keeping a redundant vi-VN satellite would force the resolver to load a satellite assembly for the
  default culture (fragile, and the current empty satellite is the source of the blocker). Trade-off:
  the current empty vi-VN Designer is removed; the neutral resource is the single source of Vietnamese.
- **(b) Keep vi-VN as a satellite AND add a neutral resx duplicating it.** Redundant maintenance (two
  Vietnamese copies to keep in sync); no benefit given vi-VN is the neutral language.

### OQ7 — Thin `IRequestCulture` scoped accessor?
> **Answered 2026-07-16 (option a).** No custom `IRequestCulture` accessor — rely on `CurrentUICulture`
> set by `UseRequestLocalization`.

Do any services need to format messages outside `CurrentUICulture` (e.g. background threads)?

- **(a) Recommended — no dedicated accessor; rely on `CurrentUICulture`.** `UseRequestLocalization`
  already flows `CurrentUICulture` through the async context, and all message resolution happens on the
  request thread (envelope, validators, converter). No background-thread message localization is
  required. Simplest; mirrors how the framework is meant to work. Trade-off: if a future background job
  needs localized user-facing text, add the accessor then.
  - **(b) Add `Auth/IRequestCulture.cs`** mirroring `IRequestTimeZone` (a scoped accessor reading the
    resolved culture from `HttpContext`). Consistent with the tz pattern, but currently unused surface.

### OQ8 — The LOCKED `AppController` throw site
> **Answered 2026-07-16 (option a) — PERMISSION GRANTED.** The user explicitly authorized a **one-line
> change to the LOCKED `Controllers/AppController.cs`** to localize its single thrown message (the
> anonymous/401 unauthorized guard). Scope is strictly that one line; `AppController` is otherwise still
> locked.

`Controllers/AppController.cs` (LOCKED per convention) contains 1 `new ErrorException(Unauthorized,
"Phiên đăng nhập không hợp lệ hoặc đã hết hạn.")`.

- **(a) Recommended — request explicit per-request permission to edit this one line** so the
  `Unauthorized` message is localized like all the others (swap the literal for the key/args form under
  OQ2). Keeps localization complete.
- **(b) Leave `AppController` untouched; this single message stays Vietnamese-only.** Honors the lock
  with zero risk. Trade-off: 1 of ~175 messages is not localized (a minor, documented carve-out).

### OQ9 — Test-migration approach & pinning `CurrentUICulture`
> **Answered 2026-07-16 (option a).** Tests assert the stable error `Code` + the default-culture (vi-VN)
> message text (mostly unchanged since neutral = VN) + add parallel en-US culture tests; pin
> `CurrentUICulture` via a test helper.

- **(a) Recommended — assert on stable `Code` + the default-culture (vi-VN) message text, and add
  parallel `en-US` culture tests.** Most existing assertions already pin Vietnamese; under the default
  culture they keep passing unchanged. For unit tests that assert messages, wrap the act in a
  `CultureInfo.CurrentUICulture` set (a small `using`/helper that sets `CurrentUICulture` and restores
  it), and add `en-US` variants asserting the English text. Integration tests send
  `Accept-Language: en-US` (or `?culture=en-US`) and assert English envelope text. Trade-off: some test
  churn to introduce the culture-pinning helper.
  - **(b) Assert on keys instead of text.** Decouples tests from wording, but loses coverage that the
    resx actually contains the right string and that resolution works; also requires exposing keys to
    tests. Rejected as the primary approach; a few key-existence tests may complement (a).

### OQ10 — Config source for the default culture
> **Answered 2026-07-16 (option b — config-driven).** Read `App:DefaultCulture` (= `vi-VN`) and the
> supported-cultures list (`App:SupportedCultures = ["vi-VN","en-US"]`) from `appsettings.json`, NOT
> hardcoded in `Program.cs` — aligning with the timezone feature's `App:DefaultTimeZone` config pattern.

- **(a) Recommended — hardcode `DefaultRequestCulture = "vi-VN"` and `SupportedCultures = { vi-VN,
  en-US }` in `Program.cs` (matching the LOCKED decision).** Simplest; the default is locked anyway.
  - **(b) Read `App:DefaultCulture` from `appsettings.json`** (mirroring `App:DefaultTimeZone`), default
    "vi-VN". More configurable; adds a config knob nobody has asked for yet.

### OQ11 — Auth-handler 401/403 messages (discovered during implementation, NOT in the doc's scope list)
> **Raised 2026-07-16 by the implementer; RESOLVED 2026-07-16 (option a) — user approved. Both messages
> localized.**

`Auth/OpaqueTokenAuthenticationHandler.cs` writes two user-facing literals **directly** via
`ApiResult.Failure(code, literal)` (not through `ErrorException`), so they were outside the doc's enumerated
surface (36 `ErrorException` + envelope + validators + success + tz):
- `HandleChallengeAsync` (401): `"Phiên đăng nhập không hợp lệ hoặc đã hết hạn."` (same text as
  `Error.Unauthorized`, which IS localized everywhere else — AppController + AuthService).
- `HandleForbiddenAsync` (403): `"Bạn không có quyền thực hiện thao tác này."` (no key exists yet).

Smoke confirmed these stay Vietnamese even under `Accept-Language: en-US` (the auth handler runs after
`UseAppLocalization`, so `CurrentUICulture` is set and localizing them IS feasible). Left unchanged to honour
"build exactly what the doc specifies".
- **(a) Recommended — localize both** (add `Error.Unauthorized` reuse for the challenge + a new
  `Error.Forbidden` key for the 403; resolve via `IStringLocalizer` from `context.RequestServices` in the
  handler). Makes localization complete and consistent; the same 401 text should not be VN-only here while
  localized on the AppController path.
- **(b) Leave as a documented Vietnamese-only carve-out** (2 of ~177 messages).

### OQ12 — `PremiumFeatureRequired` feature-name argument stays Vietnamese
> **Raised 2026-07-16 by the implementer; RESOLVED 2026-07-16 (option b) — user approved. Feature names are
> now localized keys, so en-US output is fully English.**

`ITierService.EnsurePremiumFeature(string featureNameVi)` is called with Vietnamese feature names
(`"ví ngân hàng"`, `"tạo mã QR"`). The doc's interpolation mechanism (`{0}` + args) was applied, so
`Error.PremiumFeatureRequired` = `"...{0}..."` and the arg is `featureNameVi`. Result: under en-US the
template is English but `{0}` is the Vietnamese feature name (smoke: `The ví ngân hàng feature is available
only to Premium accounts…`). This matches the doc's stated mechanism (args are values), but produces
mixed-language output.
- **(a) Recommended for now — accept it** (a small, documented residue; the doc did not call for localizing
  the feature name itself).
- **(b) Localize the feature name too** — change `EnsurePremiumFeature` to take a feature key
  (`Feature.BankWallet` / `Feature.QrCode`) and resolve it before formatting. Extra keys + a signature change
  beyond the doc.

## Assumptions

**Status:** all assumptions below are **confirmed** as of the 2026-07-16 checkpoint (the 3 inherited-locked
decisions were never reopened; the 10 Open Questions are resolved above).

- **Locked decision 1 (inherited, not reopened):** scope = runtime messages only — errors (~36 throw
  sites), FluentValidation `.WithMessage` (98), `ApiResult.SuccessMessage` (19), the envelope hardcoded
  strings, and the 3 tz-converter parse errors (~175 strings total). Swagger annotations (~254,
  dev-facing) stay Vietnamese and are out of scope. DB-content localization is out.
- **Locked decision 2 (inherited):** neutral/fallback language = Vietnamese. A neutral
  `StringResources.resx` holds the current Vietnamese verbatim + a `StringResources.en-US.resx`
  satellite; `DefaultRequestCulture = vi-VN`; unknown culture → Vietnamese. The redundant separate vi-VN
  satellite is dropped/folded into neutral.
- **Locked decision 3 (inherited):** culture source = standard `Accept-Language` header plus a
  `?culture=` query override, via ASP.NET `UseRequestLocalization` (sets `CurrentUICulture` per request);
  `SupportedCultures = { vi-VN, en-US }`; fallback default vi-VN.
- `AddLocalization` ships in the shared ASP.NET framework — **no new NuGet package** is added
  (confirmed 2026-07-16). Resx files only.
- **No EF migration** — nothing is stored; `ErrorCodes` numeric values are unchanged and remain the
  machine contract (confirmed 2026-07-16).
- **Default culture + supported cultures are config-driven** (OQ10 = b): `App:DefaultCulture` (`vi-VN`)
  and `App:SupportedCultures` (`["vi-VN","en-US"]`) are added to `appsettings.json`, mirroring
  `App:DefaultTimeZone`.
- **One-line edit to the LOCKED `AppController.cs` is authorized** (OQ8, permission granted 2026-07-16),
  scoped strictly to localizing its single 401/unauthorized thrown message.
- `AddHttpContextAccessor()` is already registered and reused; `UseRequestLocalization` runs before
  model binding (so the tz-converter sees the right `CurrentUICulture`).
- English translations are authored by the implementer (mechanical translation of the Vietnamese); the
  user can refine wording later without code changes (resx edits only).
- The `error.fields` envelope shape and the numeric-code → HTTP-status mapping are unchanged.

## Implementation Plan

### 1. Fix the resx blocker + accessor (foundation)
1. Resolve OQ5/OQ6. Assuming the recommendations: move
   `Localization/StringResources/StringResources.{vi-VN,en-US}.resx` (+ `.Designer.cs`) to
   `Localization/Resources/` so they match the existing csproj `Update=` paths and Designer base
   name/namespace (`FairShareMonApi.Localization.Resources`). Delete the empty
   `StringResources.vi-VN.resx` + `.Designer.cs` (fold Vietnamese into the neutral resx).
2. Add the **neutral** `Localization/Resources/StringResources.resx` (no culture suffix) +
   `StringResources.Designer.cs`, and add the corresponding `EmbeddedResource`/`Compile` entries in
   `FairShareMonApi.csproj` (mirroring the existing en-US entries). Add
   `[assembly: NeutralResourcesLanguage("vi-VN")]` (e.g. in `Program.cs` top or a small
   `AssemblyInfo`-style file).
3. Add the accessor marker type `Localization/Resources/StringResources.cs`
   (`public class StringResources;`) in namespace `FairShareMonApi.Localization.Resources` so
   `IStringLocalizer<StringResources>` binds to the resx family.
4. Verify: `dotnet build .\FairShareMonApi.sln` clean, and a smoke read of one key in each culture
   returns the expected text (no `MissingManifestResourceException`).

### 2. Wire localization in `Program.cs` + `appsettings.json` (config-driven, OQ10 = b)
1. Add to `appsettings.json` `App` section (beside `DefaultTimeZone`):
   `"DefaultCulture": "vi-VN"` and `"SupportedCultures": ["vi-VN", "en-US"]`.
2. `builder.Services.AddLocalization(options => options.ResourcesPath = "Localization/Resources")`
   (path must agree with the folder chosen in step 1; verify against a real read).
3. Build a `RequestLocalizationOptions` from config: read `App:DefaultCulture` (fallback `"vi-VN"`) into
   `DefaultRequestCulture`, and `App:SupportedCultures` (fallback `["vi-VN","en-US"]`) into
   `SupportedCultures`/`SupportedUICultures`; `RequestCultureProviders = [ new
   QueryStringRequestCultureProvider(), new AcceptLanguageHeaderRequestCultureProvider() ]` (query first,
   then header — matching the reference). Extract into an `Extensions/LocalizationExtensions.cs`
   `AddAppLocalization(IConfiguration)` / `UseAppLocalization()` pair (small, mirrors quick-ordering's
   `UseCustomLocalization`, and keeps the config read out of `Program.cs`).
4. Add `app.UseRequestLocalization(...)` (via `UseAppLocalization()`) in the pipeline **immediately after
   `UseRouting()` and next to `RequestTimeZoneMiddleware`** (L139–142 area), before
   `ErrorHandlerMiddleware`, so `CurrentUICulture` is set before any endpoint, filter, validator, or
   converter runs.

### 3. Define the key scheme + populate the resx (VN neutral + en-US)
1. Resolve OQ1. Assuming the recommendation: `Error.<ErrorCodes constant name>`,
   `Validation.<Area>.<Rule>`, `Success.<Action>`, `Envelope.<Case>`, `Serialization.<Case>`.
2. Populate `StringResources.resx` (neutral, Vietnamese verbatim) with every key: ~36 error strings
   (deduped by code where identical), 98 validation strings, 19 success strings, 3 envelope strings, 3
   serialization strings. Interpolated messages become `{0}`/`{1}` templates
   (e.g. `Validation.Member.NameTooLong = "Tên thành viên không được vượt quá {0} ký tự."`,
   `Error.MemberLimitReached = "Tài khoản Free chỉ được tạo tối đa {0} thành viên. Nâng cấp Premium để bỏ giới hạn."`).
3. Populate `StringResources.en-US.resx` with English translations of every key (same placeholders).
4. Optionally add a `Constants/MessageKeys.cs` static class of key constants to avoid stringly-typed
   keys at call sites (recommended for compile-time safety across ~175 keys).

### 4. Error-envelope resolution + `ErrorException` reshape (OQ2)
1. Reshape `Exception/ErrorException.cs` to carry a `MessageKey` (+ `object[]? Args`) instead of a
   resolved message (keep `Code`, `HttpStatus`, and the `GetDefaultHttpStatus` map intact).
2. Update all ~35 service throw sites to pass a key (+ args) instead of a literal:
   `AuthService` (9), `ExpensesService` (9), `WalletQrService` (4), `TierService` (4 — args are the
   limit numbers), `AdminUserService` (3), `SharesService` (2), `EventsService` (2), `MembersService`
   (1), `CategoriesService` (1).
3. Resolve in the envelope: in `ApiResult.Failure(ErrorException)` (or where the envelope is built),
   inject/resolve `IStringLocalizer<StringResources>` and format `localizer[ex.MessageKey, ex.Args]`.
   Because `ApiResult.Failure` is static, prefer resolving the localizer in the two boundary
   builders that already have DI access — `Attributes/MvcFilters/ErrorHandlerFilter.OnException`
   (inject via the filter's ctor) and `Middlewares/ErrorHandlerMiddleware.InvokeAsync` (resolve from
   `context.RequestServices`) — and pass the resolved string into `ApiResult.Failure(code, message,
   …)`. Localize the two envelope literals here too (`Envelope.ValidationFailed`,
   `Envelope.FieldInvalid`, `Envelope.InternalError`).
4. Localize the LOCKED `AppController` throw site (OQ8 permission granted): swap its single
   `Unauthorized` literal for the key/args form (`Error.Unauthorized`). This is the only authorized edit
   to `AppController.cs`; touch no other line.

### 5. Validator localization (OQ3)
1. Inject `IStringLocalizer<StringResources>` into each of the 28 validators' primary constructors.
2. Replace each `.WithMessage("…")` with `.WithMessage(_ => localizer["Validation.<Area>.<Rule>"])`,
   and interpolated ones with `localizer["…", <limitConst>]`. Keep every rule chain and the
   `error.fields` output shape identical.

### 6. `SuccessMessage` keys
1. Resolve the localizer at the 19 controller call sites. Options: inject
   `IStringLocalizer<StringResources>` into each controller and call
   `ApiResult.SuccessMessage(localizer["Success.<Action>"])`, or add an overload/helper on the
   controller base — but `AppController` is LOCKED, so prefer injecting the localizer into each concrete
   controller (9 controllers). Replace each literal with its key.

### 7. Tz-converter (OQ4 = a)
1. Add `IStringLocalizerFactory` to `UtcAwareDateTimeConverter` (and the nullable variant) ctor, create
   the localizer for `StringResources`, and replace the 3 `JsonException` literals with `Serialization.*`
   keys (the `{raw}` one uses `[key, raw]`). Update the `AddJsonOptions` registration in `Program.cs` to
   pass the factory (resolve a `IStringLocalizerFactory` alongside the existing `HttpContextAccessor`
   used there). Falls back to the neutral (Vietnamese) resource when there is no `HttpContext`, so it
   never throws.

### 8. Test migration (OQ9)
1. Add a small test helper to pin `CultureInfo.CurrentUICulture` (set + restore) for unit tests.
2. Keep existing validator/service/envelope assertions asserting the **default-culture (vi-VN)** text
   (unchanged); add parallel `en-US` unit tests for a representative subset of each area.
3. Add **real-MariaDB integration** endpoint tests (per the testing rules — SkippableFact + per-test
   transaction rollback) that send `Accept-Language: en-US` and `?culture=en-US` and assert the English
   envelope `error.message` / `error.fields` / success `message`; and a vi-VN default test confirming
   Vietnamese. Include an unknown-culture test (`Accept-Language: fr-FR`) asserting the Vietnamese
   fallback.
4. Add a resx-integrity unit test: every key referenced by `MessageKeys`/the error→key map exists in
   both the neutral and en-US resx (guards against missing translations).

### Vietnamese user-facing message keys (samples; full set authored in step 3)
- `Envelope.ValidationFailed` = "Dữ liệu gửi lên không hợp lệ."
- `Envelope.FieldInvalid` = "Giá trị không hợp lệ."
- `Envelope.InternalError` = "Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau."
- `Error.Unauthorized` = "Phiên đăng nhập không hợp lệ hoặc đã hết hạn."
- `Error.MemberNotFound`, `Error.CategoryNameDuplicate`, `Error.EventClosed`, `Error.PremiumFeatureRequired`, … (one per distinct message; ~55 total incl. deduped)
- `Error.MemberLimitReached` = "Tài khoản Free chỉ được tạo tối đa {0} thành viên. Nâng cấp Premium để bỏ giới hạn." (+ OpenEvent / MonthlyExpense analogues)
- `Validation.Member.NameRequired` = "Tên thành viên không được để trống."
- `Validation.Member.NameTooLong` = "Tên thành viên không được vượt quá {0} ký tự."
- `Success.MemberCreated`, `Success.ExpenseDeleted`, `Success.EventClosed`, … (19 total)
- `Serialization.DateTimeMustBeString` = "Giá trị ngày giờ phải là chuỗi ISO-8601."
- `Serialization.DateTimeInvalid` = "Giá trị ngày giờ không hợp lệ."
- `Serialization.DateTimeInvalidWithValue` = "Giá trị ngày giờ không hợp lệ: {0}"

### Tests the test-engineer should write
Required coverage (per the checkpoint): an error / a validation / a success message returns **English**
under `Accept-Language: en-US` **and** `?culture=en-US`, and **Vietnamese** by default / unknown culture;
`error.fields` messages localize; the **interpolated Tier/limit and max-length** messages format
correctly in **both** cultures; the **`AppController` 401** message localizes; existing vi-VN assertions
still pass under the default culture; **no `MissingManifestResourceException`**.

- **Unit (pure logic, `CurrentUICulture` pinned via a test helper that sets + restores it):**
  representative validator tests per area asserting both vi-VN (default) and en-US `.WithErrorMessage`,
  including a **max-length** rule to verify `{0}` interpolation in both cultures; `TierService` limit-
  message tests (member / open-event / monthly-expense) asserting the `{0}` limit number renders in both
  cultures; a resx-integrity test (every key in `MessageKeys` / the error→key map exists in **both** the
  neutral and en-US resx); an `ErrorException`→envelope resolution test (key + args → localized text); a
  smoke test that reads one key per culture with no `MissingManifestResourceException`.
- **Integration (real MariaDB, SkippableFact, per-test transaction rollback):** endpoint calls exercising
  each culture source — `Accept-Language: en-US`, `?culture=en-US`, `Accept-Language: vi-VN` (default,
  header omitted), and an unknown culture (`Accept-Language: fr-FR` → Vietnamese fallback) — asserting
  `error.code` unchanged and `error.message` / `error.fields[*]` / success `message` localized. Cover at
  least: one validation-failure path (`error.fields`, both cultures), one business `ErrorException` path
  (incl. an **interpolated Tier-limit** error in both cultures), one **success** path, and the
  **anonymous/401 `AppController`** path (localized in both cultures).

## Impact Analysis

- **APIs:** No route, verb, DTO, status-code, or `error.code` changes. **Behavior change (client-visible):
  `error.message`, `error.fields[*]`, and success `message` now vary by `Accept-Language` / `?culture=`.**
  Default and unknown cultures return Vietnamese (unchanged from today). English becomes available for
  `en-US`.
- **Database:** None. No migration, no schema/entity/data change. `ErrorCodes` numeric values unchanged.
- **Infrastructure:** `Program.cs` gains `AddLocalization` + `UseRequestLocalization` (pipeline order:
  right after `UseRouting`, beside `RequestTimeZoneMiddleware`, before `ErrorHandlerMiddleware`), wired
  via a new `Extensions/LocalizationExtensions.cs`. `appsettings.json` gains `App:DefaultCulture` and
  `App:SupportedCultures` (config-driven, OQ10 = b). **No new NuGet package** (`AddLocalization` is
  in-framework; resx only). **No EF migration.** `AddHttpContextAccessor` reused.
- **Services:** ~35 `ErrorException` throw sites across 9 services reshaped to keys+args; 28 validators
  gain an `IStringLocalizer` dependency and key-based `.WithMessage`; 9 controllers gain an
  `IStringLocalizer` for `SuccessMessage` keys; `UtcAwareDateTimeConverter` (+nullable) gains a
  localizer. Envelope builders (`ErrorHandlerFilter`, `ErrorHandlerMiddleware`) resolve the localizer.
  **LOCKED `AppController`** — 1 authorized one-line edit (OQ8 permission granted) to localize its 401
  message; no other line touched, otherwise still locked.
- **Resources/files:** ~175 strings moved into `StringResources.resx` (neutral/VN) +
  `StringResources.en-US.resx`; resx layout/namespace/csproj blocker fixed; new marker type
  `StringResources.cs`; optional `MessageKeys.cs` + `Extensions/LocalizationExtensions.cs`; broken empty
  vi-VN satellite deleted.
- **Tests:** ~86 message assertions across 12 files adapted (default-culture text preserved), plus new
  en-US unit tests, a resx-integrity test, and real-MariaDB integration culture tests. `CurrentUICulture`
  pinned in message-asserting unit tests.
- **Documentation:** This planning doc; Swagger annotations stay Vietnamese (out of scope). Consider a
  short note that clients may send `Accept-Language`.

## Decision Log

All resolved at the 2026-07-16 checkpoint. The 3 inherited-locked decisions (scope = runtime messages
only; neutral/fallback = Vietnamese; culture source = `Accept-Language` + `?culture=` via
`UseRequestLocalization`) were confirmed and not reopened.

- **D1 (OQ1 = a):** semantic namespaced keys — `Error.<EnumName>`, `Validation.<Area>.<Rule>`,
  `Success.<Action>`, `Envelope.<Case>`, `Serialization.<Case>`. Reason: readable, stable, and
  derivable at throw sites; numeric `ErrorCodes` stays the machine contract, the key is human-facing.
- **D2 (OQ2 = a):** `ErrorException` carries a resource key + optional args; the message literal is
  dropped from all 36 throw sites and resolved once at the envelope boundary. Reason: single
  localization seam, terse throw sites, clean interpolation via `{0}` + `string.Format`.
- **D3 (OQ3 = a):** FluentValidation localizes per rule with `.WithMessage(x => localizer[key, args])`,
  injecting `IStringLocalizer<StringResources>` into each of the 28 validators. Reason: preserves the
  manual-validation flow and `error.fields` shape; `CurrentUICulture` resolved lazily per call.
- **D4 (OQ4 = a):** the 3 tz-converter messages localize via `IHttpContextAccessor` →
  `IStringLocalizerFactory` inside the singleton converter. Reason: consistency with all other messages;
  falls back to neutral Vietnamese without throwing.
- **D5 (OQ5 = a):** public marker `StringResources` for `IStringLocalizer<StringResources>`; resx +
  Designer files moved to `Localization/Resources/` to match the existing csproj `Update=` paths +
  Designer base names. Reason: fixes the three-way mismatch with least churn; verified by a clean build +
  real resource read.
- **D6 (OQ6 = a):** add a neutral `StringResources.resx` (Vietnamese verbatim) +
  `[assembly: NeutralResourcesLanguage("vi-VN")]`; keep `en-US`; drop the redundant vi-VN satellite;
  make Designer/accessor public. Reason: neutral = Vietnamese guarantees default/unknown output without a
  satellite load, and removes the empty-satellite blocker.
- **D7 (OQ7 = a):** no custom `IRequestCulture` accessor — rely on `CurrentUICulture` from
  `UseRequestLocalization`. Reason: all message resolution is on the request thread; no unused surface.
- **D8 (OQ8 = a, PERMISSION GRANTED):** one-line edit to the LOCKED `Controllers/AppController.cs`
  authorized to localize its single 401/unauthorized thrown message. Reason: makes localization complete;
  scope strictly that one line — `AppController` otherwise remains locked.
- **D9 (OQ9 = a):** tests assert stable `Code` + default-culture (vi-VN) text (mostly unchanged) + add
  parallel en-US tests; `CurrentUICulture` pinned via a test helper. Reason: preserves existing coverage,
  verifies real resolution in both cultures.
- **D10 (OQ10 = b, config-driven):** `App:DefaultCulture` (`vi-VN`) + `App:SupportedCultures`
  (`["vi-VN","en-US"]`) read from `appsettings.json`, not hardcoded. Reason: aligns with the timezone
  feature's `App:DefaultTimeZone` config pattern; adding languages needs no code change.
- **Confirmed:** NO EF migration, NO new NuGet (`AddLocalization` is in the shared framework; resx only).

## Progress Log

### 2026-07-16
- Drafted the planning doc. Verified against live code: the resx three-way blocker
  (files in `Localization/StringResources/` vs csproj/Designer `Localization/Resources/`, no neutral
  resx, `internal` Designer classes, `MissingManifestResourceException` risk); message-production
  surface counts — **36 `new ErrorException` across 10 files** (incl. 1 in LOCKED `AppController`),
  **98 `.WithMessage` across 28 validators**, **19 `SuccessMessage` across 9 controllers**, **3
  tz-converter `JsonException`s**, plus the 3 envelope hardcoded strings; **~86 message assertions
  across 12 test files**. Confirmed `AddHttpContextAccessor` already registered, `AddJsonOptions`
  converter registration site, the `UseRouting → RequestTimeZoneMiddleware → ErrorHandlerMiddleware`
  pipeline order, and the quick-ordering reference (`UseCustomLocalization`, `AddLocalization`, empty
  public marker + resx family — no neutral resx). Confirmed **no DB migration** and **no new NuGet**
  required. Recorded 10 Open Questions with recommendations; Decision Log and Final Outcome left
  pending for the checkpoint.

### 2026-07-16 — Checkpoint resolved (planner)
- User answered all 10 Open Questions (OQ1–OQ10). Recorded the decisions inline (per-question "Answered
  2026-07-16" annotations) and consolidated them into the Decision Log (D1–D10). Confirmed the 3
  inherited-locked decisions unchanged. Notable: OQ10 chose **config-driven** cultures
  (`App:DefaultCulture` / `App:SupportedCultures` in `appsettings.json`, mirroring `App:DefaultTimeZone`),
  and OQ8 **granted explicit permission** for a one-line edit to the LOCKED `AppController.cs` (localize
  its 401 message only). Synced the Implementation Plan (config step, tz-converter, AppController),
  files/Impact list, and test list to the answers. Assumptions marked confirmed. Feature is
  **UNBLOCKED**; ready for implementation. NO EF migration, NO new NuGet confirmed.

### 2026-07-16 — Implementation (implementer)

Implemented the full subsystem per the Decision Log. NO EF migration, NO new NuGet (`AddLocalization`
is in the shared framework; resx only) — confirmed.

**Resx layout fix + neutral resx + accessor (D5/D6).** Deleted the broken empty scaffold under
`Localization/StringResources/` (untracked). Created the neutral `Localization/Resources/StringResources.resx`
(Vietnamese verbatim) and `StringResources.en-US.resx` (English, authored here) — **120 keys each**. Added
the public marker type `Localization/Resources/StringResources.cs`
(`FairShareMonApi.Localization.Resources.StringResources`) and
`[assembly: NeutralResourcesLanguage("vi-VN")]` (`Localization/AssemblyLocalizationInfo.cs`). Removed the
Designer/`ResXFileCodeGenerator` + `Compile Update` entries from the csproj and rely on the SDK default
`**/*.resx` embedded-resource glob; the manifest name
(`FairShareMonApi.Localization.Resources.StringResources`) equals the marker type's full name.
**Reconciliation of the OQ5 path note:** because the marker type's namespace already matches the resx base
name, `AddLocalization()` is wired with **no `ResourcesPath`** (an empty path); setting
`ResourcesPath = "Localization/Resources"` would have doubled the base name and thrown
`MissingManifestResourceException`. Verified a real resource read in both cultures at runtime (smoke below)
— no `MissingManifestResourceException`. The strongly-typed Designer accessor was intentionally dropped in
favour of the chosen `IStringLocalizer<StringResources>` marker-type accessor (the Designer files were empty
and unused, and a neutral Designer class would collide with the marker type name).

**Culture pipeline + config keys (locked + D10).** `Extensions/LocalizationExtensions.cs` adds
`AddAppLocalization()` (registers `AddLocalization()`) and `UseAppLocalization(IConfiguration)` (builds
`RequestLocalizationOptions`: `QueryStringRequestCultureProvider` then
`AcceptLanguageHeaderRequestCultureProvider`, `DefaultRequestCulture` + supported cultures read from config,
unknown → default). `appsettings.json` `App` section gains `DefaultCulture: "vi-VN"` and
`SupportedCultures: ["vi-VN","en-US"]` (mirrors `App:DefaultTimeZone`). `Program.cs`: `AddAppLocalization()`
by the controller/JSON wiring; `app.UseAppLocalization(app.Configuration)` placed right after `UseRouting`,
beside `RequestTimeZoneMiddleware`, before `ErrorHandlerMiddleware`.

**Key scheme (D1).** `Constants/MessageKeys.cs` (generated) holds all 120 keys as nested constants:
`Error.*` (42), `Envelope.*` (3), `Serialization.*` (3), `Success.*` (19), `Validation.<Area>.*` (53 across
Auth/Member/Category/Tag/Event/Expense/Share/BankAccount/Stats/Admin + a small `Validation.Common.*` group
of 3 for genuinely cross-area messages — range-invalid, amount-negative, note-too-long). Distinct messages
for the same error code get suffixed keys (e.g. `Error.EventClosed` / `EventClosedEdit` / `EventClosedDelete`
/ `EventClosedDetach`; `Error.OwnerRepresentativeShareNotDeletable` /
`OwnerRepresentativeShareMemberNotChangeable`).

**ErrorException reshape + envelope resolution (D2).** `ErrorException` now carries `MessageKey` (+ optional
`object[]? Args`); the base `Exception.Message` holds the key (for logs). **Signature kept the existing
`int? httpStatus` as the 3rd positional parameter and added `object[]? args` as the 4th** (deviation from the
doc's suggested `(code, key, args, httpStatus)` order) so the existing `ErrorException`/`ApiResult` unit tests
(which pass `httpStatus` positionally) keep **compiling**. Resolution happens at the envelope boundary:
`ErrorHandlerFilter` (localizer injected via ctor) and `ErrorHandlerMiddleware` (localizer from
`context.RequestServices`) format `localizer[ex.MessageKey, ex.Args]` via a new
`Extensions/LocalizerExtensions.LocalizeError`. The two envelope literals + the 500 fallback use
`Envelope.ValidationFailed` / `Envelope.FieldInvalid` / `Envelope.InternalError`. **All 52 `ErrorException`
constructions** were re-keyed — not just the 36 `new ErrorException(...)` the doc counted: the doc's grep
missed **16 target-typed `new(ErrorCodes.X, "...")` factory helpers** (e.g. `private static ErrorException
NotFound() => new(...)` in 10 services), which had to be re-keyed too for the reshape to be coherent. The
interpolated Tier limits use `args: [_maxMembers]` etc.; `PremiumFeatureRequired` passes the (Vietnamese)
`featureNameVi` as `{0}`.

**AppController (D8 — one authorized line).** `Controllers/AppController.cs` line 27 now throws
`new ErrorException(ErrorCodes.Unauthorized, MessageKeys.Error.Unauthorized)` — the only line touched.

**Validators (D3).** All 28 validators localized: each gained an **optional** primary-ctor param
`IStringLocalizer<StringResources>? localizer = null` with a `localizer ??= SharedStringLocalizer.Instance`
fallback, and every `.WithMessage(...)` (98 total) became `.WithMessage(_ => localizer[key, args].Value)`.
The optional param + shared fallback is deliberate: in the app the container injects the real localizer;
the ~13 test files that construct validators with `new XValidator()` still **compile** (and localize via
the shared fallback, which resolves the same resx). `Localization/SharedStringLocalizer.cs` builds a
process-wide `IStringLocalizer<StringResources>` on a `ResourceManagerStringLocalizerFactory`
(default options, matching `AddLocalization()`).

**Success (D2/D1).** All 19 `ApiResult.SuccessMessage(...)` call sites across 9 controllers now pass
`localizer[MessageKeys.Success.*].Value`; each controller gained an injected
`IStringLocalizer<StringResources>` (required param; `HealthController` gained a primary ctor). Controllers
are not `new`-constructed in tests, so required injection is safe.

**Tz-converter (D4).** `UtcAwareDateTimeConverter` + `UtcAwareNullableDateTimeConverter` gained an
**optional** `IStringLocalizerFactory? localizerFactory = null` (DI supplies it; unit-test construction falls
back to `SharedStringLocalizer.Instance`), and the shared `RequestDateTimeSerializer.Read` takes an
`IStringLocalizer` and throws the 3 `Serialization.*` keys (the `{raw}` one via `[key, raw]`). `Program.cs`
registers the converters through a DI-aware `AddOptions<Mvc.JsonOptions>().Configure<IStringLocalizerFactory>`
(replacing the inline `AddJsonOptions` lambda), so the singleton converters get the real factory.

**No custom IRequestCulture (D7).** Relied on `CurrentUICulture`.

**Build.** `dotnet build FairShareMonApi.sln` succeeds — 0 errors (the production project has 0 warnings; the
3 warnings are pre-existing `CS8619` nullability in `ExpensesEndpointTests`, unrelated).

**Live smoke (VN default + en-US), app on real MariaDB/Redis:**
- Success (`GET /health`): no header → `Hệ thống hoạt động bình thường.`; `Accept-Language: en-US` **and**
  `?culture=en-US` → `The system is operating normally.`; `Accept-Language: fr-FR` → Vietnamese fallback.
- Validation `error.fields` + interpolation (`POST /auth/register` short username/password): VN →
  `Tên đăng nhập phải có từ 3 đến 32 ký tự.` / `Mật khẩu phải có ít nhất 8 ký tự.`; en-US →
  `Username must be between 3 and 32 characters.` / `Password must be at least 8 characters.` (envelope
  `Dữ liệu gửi lên không hợp lệ.` → `The submitted data is invalid.`).
- Business error (`POST /auth/login` bad creds): VN `Tên đăng nhập hoặc mật khẩu không đúng.` → en-US
  `Incorrect username or password.` (code 2001 unchanged).
- Interpolated business error (`POST /bank-accounts` on a Free account → `PremiumFeatureRequired` 13003):
  VN `Tính năng ví ngân hàng chỉ dành cho tài khoản Premium…` → en-US `The ví ngân hàng feature is available
  only to Premium accounts…` — proves `ErrorException` args flow through the envelope. (The `{0}` feature name
  stays Vietnamese — see Open Questions below.)
- Auth-handler 401 carve-out (invalid token, `Accept-Language: en-US`): returns Vietnamese
  `Phiên đăng nhập không hợp lệ hoặc đã hết hạn.` — see Open Questions.
- No `MissingManifestResourceException` in any culture. Smoke user + rows cleaned up from the DB (0 orphans).

**Two items outside the doc's enumerated scope (flagged, NOT changed):** see the two new Open Questions
appended above.

### 2026-07-16 — OQ11 + OQ12 completed (implementer, user-approved)

Both flagged carve-outs are now localized (still NO migration, NO new NuGet, production code only, no test
edits). **3 keys added** to both resx (neutral vi-VN verbatim + en-US) — now **123 keys each** — and to
`Constants/MessageKeys.cs`:
- `Error.Forbidden` = "Bạn không có quyền thực hiện thao tác này." / "You do not have permission to perform
  this action." (code 1004).
- `Feature.Wallet` = "ví ngân hàng" / "wallet".
- `Feature.Qr` = "tạo mã QR" / "QR code".

**OQ11.** `Auth/OpaqueTokenAuthenticationHandler.cs` — `HandleChallengeAsync` (401) and
`HandleForbiddenAsync` (403) now resolve `IStringLocalizer<StringResources>` from `Context.RequestServices`
(the handler runs after `UseAppLocalization`, so `CurrentUICulture` is set) and use `Error.Unauthorized` /
`Error.Forbidden`. No ctor change (the handler isn't `new`-constructed in tests).

**OQ12.** `ITierService.EnsurePremiumFeature(string featureNameKey)` now takes a **resource key**;
`TierService` gained an optional injected `IStringLocalizer<StringResources>? localizer = null`
(SharedStringLocalizer fallback for the `new TierService(...)` test) and resolves the feature-name key on the
request thread, passing the **localized** feature name as the `{0}` arg. Callers updated:
`BankAccountsService` ×4 → `MessageKeys.Feature.Wallet`; `WalletQrService` ×2 → `MessageKeys.Feature.Qr`.
Result: en-US → "The wallet feature is available only to Premium accounts. Upgrade to use it." (fully
English); vi-VN unchanged.

**Build:** `dotnet build FairShareMonApi.sln` — 0 errors (production 0 warnings). **Smoke (app on real
MariaDB/Redis):**
- 401 invalid token: en-US → `Your session is invalid or has expired.`; no header → Vietnamese.
- 403 non-admin → admin route: en-US → `You do not have permission to perform this action.`; no header →
  `Bạn không có quyền thực hiện thao tác này.`
- 13003 gated wallet (Free acct): en-US → `The wallet feature is available only to Premium accounts. Upgrade
  to use it.` (fully English, no Vietnamese term); no header → `Tính năng ví ngân hàng chỉ dành cho tài khoản
  Premium…`
Smoke user cleaned up (0 orphans). Files changed: `Auth/OpaqueTokenAuthenticationHandler.cs`,
`Services/Api/Tiers/TierService.cs`, `Services/Api/Wallet/BankAccountsService.cs`,
`Services/Api/Wallet/WalletQrService.cs`, `Localization/Resources/StringResources.resx` +
`StringResources.en-US.resx`, `Constants/MessageKeys.cs`. NO test edits (the D9 test churn — now including
new en-US coverage for the auth-handler + a fully-English feature-name assertion — remains the
test-engineer's step).

### 2026-07-16 — Test migration + culture coverage (test-engineer)

Migrated the D9 message-assertion churn and added the new culture / OQ11 / OQ12 / resx-integrity coverage.
**Production code untouched** — all work is in `FairShareMonApi.Tests`. Full suite **GREEN: 1116 passed,
0 failed, 0 skipped** (DB reachable), run **twice, identical** (deterministic). Real MariaDB left clean
afterward (0 users, 0 members/expenses/categories/auth_tokens; prefix-scoped cleanup + tx rollback all ran).

**Migration approach (D9).** Rather than rely on the ambient host culture (the trap that produced the ~93
failures), every locale-sensitive test now pins culture explicitly:
- New helper `FairShareMonApi.Tests/UseCultureAttribute.cs` — an xUnit `BeforeAfterTestAttribute`
  (`[UseCulture("vi-VN")]` / `[UseCulture("en-US")]`) that sets + restores `CurrentCulture`/`CurrentUICulture`
  on the test thread, plus an imperative `CultureScope` `IDisposable` for tests that switch culture within one
  method. The validators resolve via the `SharedStringLocalizer` fallback (they're `new()`-constructed with no
  DI localizer), which honours `CurrentUICulture` — confirmed the pinned culture drives the resolved text.
- Applied `[UseCulture("vi-VN")]` to all **21 message-asserting validator test classes** across the 8
  `*ValidatorsTests` files (Auth/BankAccount/Event/Expense/Categories/Members/Stats/Tags). Their existing
  Vietnamese assertions are unchanged and now pass deterministically under vi-VN.
- `TierServiceTests.LimitMessage_IncludesTheConfiguredNumber` (asserted `ErrorException.Message`, now the KEY)
  replaced by two tests asserting the stable **`Code` + `MessageKey`** and the localized text resolved the way
  the envelope does (`SharedStringLocalizer.Instance.LocalizeError(ex)`) under pinned vi-VN and en-US — proving
  the interpolated `{0}` limit renders in both cultures.

**New unit coverage.**
- `LocalizationResourceTests.cs` — representative keys resolve in vi-VN and en-US; en-US satellite really
  loads (distinct from neutral); unknown culture (fr-FR) → Vietnamese neutral fallback; `{0}` interpolation in
  both cultures; `LocalizeError` (key + args → localized text) in both cultures; a smoke theory reading a key
  per culture with **no `MissingManifestResourceException`**; and the high-value **resx-integrity guard**:
  reflects over every `MessageKeys.*` constant and asserts each is present + non-empty in **both** the neutral
  and the en-US `ResourceSet` (`ResourceManager.GetResourceSet(..., tryParents:false)` so an untranslated en-US
  key can't hide behind the neutral fallback), plus an anchor that the key count is 123.
- `LocalizationValidatorCultureTests.cs` — representative en-US validator assertions (Members required +
  max-length `{0}`; Register required + username range `{0},{1}` + password min-length `{0}`) with a co-located
  vi-VN restatement, proving `.WithMessage` localizes across cultures over the identical code path.

**New integration coverage (real MariaDB, `SkippableFact`, `[Collection("AuthIntegration")]`).**
`LocalizationEndpointTests.cs`:
- Success `message` (`GET /health`) localizes by **every culture source**: no header → vi-VN; `Accept-Language:
  en-US` → English; `?culture=en-US` → English; `Accept-Language: fr-FR` → vi-VN fallback.
- Validation `error.fields[*]` (`POST /auth/register`) — vi-VN default, en-US header, `?culture=` — with
  `error.code` unchanged.
- Business `ErrorException` (`POST /auth/login` bad creds) — vi-VN + en-US, code 2001 unchanged.
- **OQ11** 401 (no token → `GET /members`) and 403 (Free non-admin → `GET /admin/users`) localize vi-VN
  default / en-US header (codes 1001/1004 unchanged).
- **OQ12** the gated wallet **13003** message is fully English under en-US (contains "wallet"/"Premium",
  does **not** contain "ví ngân hàng") and Vietnamese by default.
- `LocalizationTierLimitEndpointTests` (low-limit host) — the interpolated member-limit **13000** message
  renders the `{0}` number ("2") with a Vietnamese template by default and an English template under en-US.

**No production bugs found** — all localized paths behaved as designed; no failing tests left in the tree.

### 2026-07-16 — Code review (APPROVED, 0 blocking — feature closed)

Reviewed the full subsystem end-to-end. **Verdict: APPROVE, 0 blocking**, 2 cosmetic/informational nits.
Suite **1116 passed / 0 failed / 0 skipped** (DB reachable), deterministic across repeated runs, real
MariaDB left clean afterward.

**Verified — no production path silently loses localization:**
- **Validators** are registered via `AddValidatorsFromAssembly`, so the container supplies the real
  open-generic `IStringLocalizer<>`; the optional `= null` + `SharedStringLocalizer` fallback is exercised
  only by `new XValidator()` test construction, and even that fallback honours `CurrentUICulture`
  (resolves the same resx) — no path returns keys or wrong-culture text.
- **`TierService` and all 9 controllers** get the localizer ctor-injected; **`ErrorHandlerFilter`** is a
  global MVC filter with the localizer injected; **`ErrorHandlerMiddleware`** resolves it from
  `context.RequestServices`; the **two tz-converters** get the factory via
  `AddOptions<JsonOptions>().Configure<IStringLocalizerFactory>` (DI-aware, not the inline lambda).
- **Pipeline order:** `UseAppLocalization` runs after `UseRouting` and before the tz/error middleware and
  the auth handler, so `CurrentUICulture` is set before filters, validators, converters, and the
  401/403 auth-handler run.
- **Neutral vi-VN = verbatim:** diffed all 52 error keys + 98 validator messages + 19 success messages —
  **zero drift** from the original Vietnamese literals; interpolated `{0}`/`{1}` arg order correct.
- **Resx wiring coherent:** marker type FQN == resource base name, empty `ResourcesPath`,
  `[assembly: NeutralResourcesLanguage("vi-VN")]`, no orphaned csproj `Update=` entries; both resx are the
  **identical 123 keys**, en-US fully translated (no untranslated keys hiding behind neutral fallback —
  guarded by the resx-integrity test).
- **`ErrorException(code, messageKey, httpStatus?, args?)`** — the stable numeric `Code` and the
  `GetDefaultHttpStatus` map are unchanged (machine contract intact); the parameter-order deviation is
  sound (keeps existing tests compiling).
- **OQ11** auth 401/403 localized via `Context.RequestServices`; **OQ12** feature name resolved per-culture
  (no mixed-language message); **`AppController`** — exactly one line changed.
- **NO EF migration, NO new NuGet** — confirmed.

**Informational nits (non-blocking, not fixed):**
1. `LocalizationExtensions` sets `ApplyCurrentCultureToResponseHeaders = true`, so responses emit a
   `Content-Language` header — harmless and arguably useful, but a minor undocumented addition beyond the
   doc's scope.
2. `ErrorHandlerMiddleware` would return the raw `MessageKey` if the localizer were ever unresolvable on a
   wrapped endpoint (should not occur in production, since the localizer is always DI-registered) — a
   Vietnamese literal fallback there would be marginally more consistent (cosmetic).

## Final Outcome

**Delivered, tested, and reviewed — feature closed (2026-07-16).** All runtime user-facing messages —
errors, FluentValidation `.WithMessage`, `ApiResult.SuccessMessage`, the 3 tz-converter parse errors, and
the auth handler's 401/403 (OQ11) — are localized per request via `IStringLocalizer<StringResources>`,
keyed from `Constants/MessageKeys.cs` (**123 keys**) over a neutral vi-VN resx (Vietnamese verbatim) + an
`en-US` satellite. Per-request culture comes from `Accept-Language` + `?culture=` through
`UseRequestLocalization` (config-driven `App:DefaultCulture` / `App:SupportedCultures`, fallback vi-VN;
unknown culture → Vietnamese). `ErrorException` was reshaped to carry a resource key + optional args
resolved once at the envelope boundary (`ErrorHandlerFilter` / `ErrorHandlerMiddleware`), with the stable
numeric `Code` unchanged. OQ12 makes the Premium feature-name fully per-culture (no mixed-language message).
Swagger annotations were intentionally left Vietnamese (out of scope). The original resx blocker is fixed
(files moved to `Localization/Resources/`, neutral base + public marker type,
`[assembly: NeutralResourcesLanguage("vi-VN")]`, empty `ResourcesPath`).

`dotnet build FairShareMonApi.sln` passes (0 errors, production 0 warnings); the full suite is **1116
passed / 0 failed / 0 skipped** (deterministic, real MariaDB left clean); the feature was live-smoke-verified
in both cultures with no `MissingManifestResourceException`. **NO EF migration and NO new NuGet package.**
Code review: **APPROVE, 0 blocking** (2 cosmetic nits recorded above and in Future Improvements).

Files created: `Localization/Resources/StringResources.resx`, `Localization/Resources/StringResources.en-US.resx`,
`Localization/Resources/StringResources.cs`, `Localization/AssemblyLocalizationInfo.cs`,
`Localization/SharedStringLocalizer.cs`, `Constants/MessageKeys.cs`, `Extensions/LocalizationExtensions.cs`,
`Extensions/LocalizerExtensions.cs`. Files changed: `FairShareMonApi.csproj`, `appsettings.json`, `Program.cs`,
`Exception/ErrorException.cs`, `Attributes/MvcFilters/ErrorHandlerFilter.cs`,
`Middlewares/ErrorHandlerMiddleware.cs`, `Serialization/UtcAwareDateTimeConverter.cs`,
`Serialization/UtcAwareNullableDateTimeConverter.cs`, `Controllers/AppController.cs` (1 line),
9 controllers, 28 validators, 13 services (52 `ErrorException` constructions re-keyed).

## Future Improvements

- Add more languages by dropping in `StringResources.<culture>.resx` (no code change) once translations
  exist.
- Localize Swagger annotations / API docs if a non-Vietnamese developer audience emerges.
- Add an `IRequestCulture` scoped accessor (OQ7b) if background jobs ever need localized user-facing
  text.
- A build-time analyzer / test that flags any new inline user-facing literal that bypasses the resx.
- Externalize translations to a translation-management workflow if the string set grows.
- (Review nit 1) Document — or make configurable — the `ApplyCurrentCultureToResponseHeaders = true`
  setting in `LocalizationExtensions` (emits a `Content-Language` response header).
- (Review nit 2) Have `ErrorHandlerMiddleware` fall back to a Vietnamese literal instead of returning the
  raw `MessageKey` on the (production-impossible) path where the localizer is unresolvable.
- DB-content localization is intentionally **out of scope** — not applicable to FairShareMon's
  user-authored data (members, categories, tags, notes are the owner's own words); revisit only if a
  future feature introduces system-authored, translatable stored content.
