# QR image header — bank info + title

## Objective

Add a text header to both server-rendered bank-transfer QR PNGs (expense QR and
event QR) so a shared image is self-describing. The header shows the destination
bank info (bank name, account holder, account number) plus a title — the expense
name on the expense QR (with the expense total amount), the event name on the
event QR (no amount; per-member amounts remain under each member's QR).

## Background

- Expense QR: `GET /api/v1/expenses/{uuid}/qr` → `WalletQrService.GenerateExpenseQrAsync`
  → `IQrImageService.RenderSingle(payload)`. Today `RenderSingle` returns a bare
  QRCoder `PngByteQRCode` (no canvas, no chrome).
- Event QR: `GET /api/v1/events/{uuid}/qr` (closed-only) → `GenerateEventQrAsync`
  → `RenderComposite(items)`, a SkiaSharp composite of one QR per still-owing
  member with a `"{MemberName}: {amount}đ"` label under each. `RenderComposite`
  internally calls `RenderSingle(item.Payload)` to encode each member's QR.
- Available at generation time (both flows, after `ResolveDestinationAsync`): the
  full `BankAccount` (`BankBin`, `BankName`, `AccountNumber`, `AccountHolderName`),
  plus `ExpenseResponse.Name`/`.Total` (expense) or `EventBalanceResponse.EventName`
  and per-member rows (event).
- Branded bank name: `IBankDirectoryService.ListAsync(ct)` → `BankResponse{Bin,Code,Name,ShortName,LogoUrl}`,
  24h `IMemoryCache` + static fallback (never throws). `VietQrRemoteQrContentProvider`
  already resolves a bank by BIN this way.
- Text-on-image already works: SkiaSharp + the embedded, Vietnamese-capable
  `BeVietnamPro-Regular.ttf` (Regular weight only — no bold variant bundled), via
  `LoadLabelTypeface`/`WrapText`/`Ellipsize` in `QrImageService`.
- Localization: `IStringLocalizer<StringResources>` (culture set per request by
  `UseAppLocalization`); `TierService` precedent injects
  `IStringLocalizer<StringResources>? localizer = null` with a
  `SharedStringLocalizer.Instance` fallback. Keys declared in `MessageKeys.cs`,
  values in `StringResources.resx` (vi) + `StringResources.en-US.resx` (en).

## Requirements

- Header on BOTH QR images: bank name, account holder, account number, and a title.
- Title = expense name (expense QR) / event name (event QR).
- Expense header also shows the amount (expense total); event header has no amount.
- Bank name = branded VietQR-directory ShortName by BIN, fallback to `Name`, then
  to the account's saved `BankName` on a miss. Text only, no logo.
- Field labels localized (vi default + en), drawn in the request's culture.
- `format=payload` path unchanged (no image, no directory/header cost).
- Keep `QrImageService` pure/synchronous — no localization or directory deps in it.

## Open Questions

None blocking. Product decisions (bank-name source, localized labels, expense-only
amount) are settled with the user. Cosmetic choices resolved with defaults:
left-aligned header, title emphasized by size (30f, no bold font bundled),
single-line ellipsized field values, ≤2-line wrapped title, inset gray divider.

## Assumptions

- Events/expenses per user are bounded; the extra `IBankDirectoryService.ListAsync`
  call is served from the shared 24h cache (negligible; also called by the provider).
- No pixel/golden snapshot tests exist for QR (verified) — only PNG-magic-byte,
  non-empty, and relative-size assertions. Changing the single-QR bytes/dimensions
  breaks nothing by snapshot.

## Implementation Plan

1. **`QrImageService.cs` — `QrHeader` record** (next to `QrCompositeItem`):
   `Title`, `BankLabel`+`BankName`, `HolderLabel`+`AccountHolderName`,
   `NumberLabel`+`AccountNumber`, `string? AmountLabel`+`string? AmountText`
   (both null on the event QR). `AmountText` pre-formatted VND via `FormatMoney`.
2. **`QrImageService.cs` — renderer signatures**:
   - Extract the current `RenderSingle` body into `private static byte[] RenderQrPng(string payload)`.
   - `byte[] RenderSingle(string payload, QrHeader header)` — SkiaSharp canvas: header band, then the QR scaled into a `QrSize`(280) square centered in a `CellPadding` cell; width `ImageWidth`(380); no label under the QR.
   - `byte[] RenderComposite(IReadOnlyList<QrCompositeItem> items, QrHeader header)` — header band at top; per-member loop unchanged except `cellTop = headerHeight` start and its inner call switched to `RenderQrPng(item.Payload)`.
3. **`QrImageService.cs` — header layout**: new constants
   (`HeaderPadding=24`, `TitleTextSize=30f`, `TitleLineHeight=40f`, `MaxTitleLines=2`,
   `HeaderFieldTextSize=22f`, `HeaderFieldLineHeight=30f`, `HeaderTitleGap=12f`,
   `HeaderBottomGap=16f`, `DividerThickness=2f`, divider `#E0E0E0`). Left-aligned;
   `maxHeaderTextWidth = ImageWidth - 2*HeaderPadding`. Extend `WrapText` with a
   `maxLines` param (keep the 3-arg overload delegating `MaxLabelLines`) for the
   ≤2-line title. Each field line composed `"{label}: {value}"` then ellipsized as a
   whole. Pre-measure to compute `headerHeight`, draw an inset divider at the band
   bottom, dispose new paints.
4. **`WalletQrService.cs`**: add ctor deps `IBankDirectoryService bankDirectory` and
   `IStringLocalizer<StringResources>? localizer = null` (fallback
   `SharedStringLocalizer.Instance`). Add
   `private async Task<QrHeader> BuildHeaderAsync(BankAccount account, string title, decimal? amount, CancellationToken ct)`
   resolving branded name (ShortName→Name→saved) + localized labels + optional
   amount. Expense: build header AFTER the `IsPayloadFormat` short-circuit →
   `RenderSingle(payload, header)`. Event: `amount: null` →
   `RenderComposite(items, header)`; per-member cells untouched.
5. **`MessageKeys.cs`**: new `Qr.Header` group — `Qr.Header.Bank`,
   `Qr.Header.AccountHolder`, `Qr.Header.AccountNumber`, `Qr.Header.Amount`.
6. **resx (both files)**: vi = Ngân hàng / Chủ tài khoản / Số tài khoản / Số tiền;
   en = Bank / Account holder / Account number / Amount.

## Impact Analysis

- gitnexus impact (2026-07-20): `RenderSingle` = LOW; `RenderComposite` = MEDIUM
  (fan-out is test-only). Sole production callers: `WalletQrService` +
  the two QR endpoints. No unexpected external consumers.
- APIs: response bytes/dimensions of both QR images change; contract (image/png)
  unchanged. `format=payload` unchanged.
- Services: `WalletQrService` gains 2 ctor deps (attribute DI, no `Program.cs`
  change); `QrImageService` stays scoped + synchronous.
- Database/Infrastructure: none. No migration.
- Tests: `WalletQrServiceTests` (ctor + `CapturingQrImageService` signatures + fake
  `IBankDirectoryService`), `QrImageServiceTests` (pass a header), `LocalizationResourceTests`
  (guard the 4 new keys + bump the hard-coded key-count assertion).

## Progress Log

### 2026-07-20
- Explored QR pipeline; confirmed product decisions with the user; ran impact
  analysis; wrote this plan.

### 2026-07-20 (implementation)
- Implemented steps 1–6 exactly as planned. No new open questions; no deviations.
  - `QrImageService.cs`: added the `QrHeader` record; extracted the bare-PNG body
    into `private static byte[] RenderQrPng(string)`; changed public signatures to
    `RenderSingle(string payload, QrHeader header)` and
    `RenderComposite(IReadOnlyList<QrCompositeItem> items, QrHeader header)`;
    composite's per-member encode now calls `RenderQrPng`. Added header layout
    constants + `BuildHeaderLayout`/`DrawHeaderBand`; extended `WrapText` with a
    `maxLines` param (kept the 3-arg overload delegating `MaxLabelLines`); field
    lines composed `"{label}: {value}"` then whole-line ellipsized; title wraps to
    ≤2 lines (30f vs 22f fields); inset `#E0E0E0` divider; new SKPaints disposed.
  - `WalletQrService.cs`: added ctor deps `IBankDirectoryService bankDirectory` +
    `IStringLocalizer<StringResources>? localizer = null` (fallback
    `SharedStringLocalizer.Instance`); added `BuildHeaderAsync` + `ResolveBankNameAsync`
    (ShortName → Name → account.BankName). Expense header built AFTER the payload
    short-circuit (`amount: expense.Total`); event header `amount: null`.
  - `MessageKeys.cs`: added the `Qr.Header` group (Bank / AccountHolder /
    AccountNumber / Amount).
  - resx (vi + en-US): added the 4 `Qr.Header.*` keys.
- Product project builds clean: `dotnet build .\FairShareMonApi\FairShareMonApi.csproj`
  → 0 errors (only the pre-existing pinned-AutoMapper NU1903 warning). Test project
  intentionally left to the test-engineer; its compilation breaks on the changed
  renderer/ctor signatures as expected.

### 2026-07-20 (tests)
- Fixed the 3 test files broken by the renderer/ctor signature changes and added header coverage.
  - `QrImageServiceTests.cs`: added `SampleHeader()` (event-style, no amount) + `SampleHeaderWithAmount()`
    helpers; passed a header into every `RenderSingle`/`RenderComposite` call. New image-sanity assertions
    decode the PNG with `SKBitmap.Decode` and assert `Width == 380` and `Height > QrSize`(280) (header
    always present makes both single + composite strictly taller than a bare QR). Added: expense header
    WITH an amount line is taller than the same header WITHOUT one; composite grows taller with more
    members (decoded height, on top of the existing byte-length guard); Vietnamese header renders without
    throwing. Kept the growth guard + empty-items throw.
  - `WalletQrServiceTests.cs`: updated `CreateService()` for the new `IBankDirectoryService` ctor dep
    (localizer omitted → `SharedStringLocalizer.Instance` fallback); rewrote `CapturingQrImageService` to
    the new 2-arg renderer signatures and to capture the `QrHeader` (SingleHeaders/CompositeHeaders). Added
    a mutable `FakeBankDirectoryService` seeded with BIN 970436 → ShortName "Vietcombank". New tests:
    expense image header carries Title=expense name, branded BankName, holder/number from the account, and
    non-null AmountText == FormatMoney(Total) ("750.000đ"); `format=payload` builds NO header; event image
    header Title=event name with BOTH AmountLabel/AmountText null; branded-name resolution — BIN hit uses
    ShortName over the account's saved BankName, BIN miss falls back to saved BankName, blank ShortName
    falls through to directory Name. All prior payload-TLV/amount/error-code (6000/9000/12000-12003/13003)/
    filename/feature-gate/format=payload assertions preserved.
  - `LocalizationResourceTests.cs`: bumped the key-count anchor 123 → 127 and renamed the test to
    `MessageKeys_CoversAllOneHundredTwentySevenKeys` (verified by counting: 4 `Qr.Header.*` keys added).
    Added `Localizer_QrHeaderLabels_ResolvePerCulture_ViDiffersFromEn` asserting the 4 labels resolve to
    their pinned vi values and distinct en values. The existing
    `EveryMessageKey_ExistsInBothNeutralAndEnglishResx` already guards the new keys in both resx.
- Result: `dotnet test .\FairShareMonApi.sln` → 719 passed, 0 failed, 486 skipped (DB-unreachable
  integration tests skip cleanly, as expected). The 42 tests across the 3 affected (pure-unit) classes all
  pass with 0 skips. No product bugs surfaced; no product code modified.

### 2026-07-20 (WrapText tail-drop fix + regression tests)
- Product fix (by implementer, reviewed here, not modified): `QrImageService.WrapText` no
  longer overrides `remainder = current` when it breaks at the line cap — the final line now
  uses the full unconsumed tail and is ellipsised, so an overflowing title/label shows "…"
  instead of silently dropping the multi-line tail. The band's line count (and thus height)
  stays capped at `MaxTitleLines`(2) for titles / `MaxLabelLines`(3) for labels.
- Added 3 regression tests to `QrImageServiceTests.cs` (pure unit, no DB), mirroring a new
  `TitleLineHeight`(40) constant:
  - `RenderSingle_LongTitle_WrapsToSecondLine_ProducingTallerImage` — a long multi-word
    Vietnamese title renders one `TitleLineHeight` (exactly 40px) taller than a one-line
    title through the same header (same 3 bank fields, no amount), proving the title wrapped
    to a second line.
  - `RenderSingle_TitleExceedingTwoLineCap_CapsHeightAndRendersWithoutThrowing` — an
    over-long title renders without throwing AND its band height equals the exact-2-line
    title's height and sits exactly one `TitleLineHeight` above the one-line baseline,
    proving the 2-line cap holds (tail is not added as extra lines, band never grows past 2).
  - `RenderComposite_LongTitle_WrapsToSecondLine_ProducingTallerImage` — extra coverage: the
    shared header band wraps the title the same way in the composite renderer.
- Kept all existing QR tests unchanged. Coverage limitation (noted in-test): with `WrapText`
  private and no non-pixel seam, the tests assert the 2-line cap via image height, not the
  literal "…" on the last line — there are no pixel/text assertions in this suite.
- Result: `dotnet test .\FairShareMonApi.sln` → 722 passed, 0 failed, 486 skipped
  (DB-unreachable integration tests skip cleanly). +3 vs the prior 719. The composite-label
  tests that share `WrapText` (`RenderComposite_VietnameseLabels_RenderWithoutThrowing`,
  `RenderComposite_MoreMembers_ProducesTallerLargerImage`, dimension guards) all still pass —
  no test asserts exact label text, only render-without-throw + dimensions, so the wrapping
  change is transparent to them. No product bug surfaced; product code not modified by tests.

## Final Outcome

Both server-rendered QR PNGs now carry a left-aligned text header (bank name /
account holder / account number + title; amount on the expense header only),
drawn on a SkiaSharp canvas above the QR(s). Files changed:

- `FairShareMonApi/Services/Api/Wallet/QrImageService.cs` — `QrHeader` record,
  `RenderQrPng` helper, new renderer signatures, header layout/draw, `WrapText`
  `maxLines` overload.
- `FairShareMonApi/Services/Api/Wallet/WalletQrService.cs` — 2 new ctor deps,
  `BuildHeaderAsync`/`ResolveBankNameAsync`, updated both render calls.
- `FairShareMonApi/Constants/MessageKeys.cs` — `Qr.Header` group.
- `FairShareMonApi/Localization/Resources/StringResources.resx` +
  `StringResources.en-US.resx` — 4 new `Qr.Header.*` keys.

`QrImageService` stayed pure/synchronous; no `Program.cs`/DI/migration/schema
changes. Test updates are the test-engineer's follow-up (guard the 4 new keys +
key-count assertion, header-passing renderer tests, fake `IBankDirectoryService`).

## Future Improvements

- Bundle a Bold Be Vietnam Pro face for a stronger title weight.
- Optional bank logo in the header (would require fetching/decoding a remote image
  per render with failure handling).
- Add `updatedAt`-style effective values if detail views later need them.
