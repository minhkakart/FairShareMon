# Per-member QR endpoint (one own QR per still-owing member)

## Objective

Add two new **JSON** QR endpoints that return, for each still-owing member on an expense
(phiếu) or a closed event (đợt), that member's **OWN single VietQR** rendered server-side
(the existing `RenderSingle`, NOT a composite) as a base64 **data URL**. The frontend feeds
each `Image` straight into an `<img>`/carousel and shows one QR per member instead of the
single composite PNG it renders today.

- `GET api/v1/expenses/{uuid}/qr/members?bankAccountUuid={uuid?}`
- `GET api/v1/events/{uuid}/qr/members?bankAccountUuid={uuid?}`

Both return `ApiResult<IReadOnlyList<MemberQrResponse>>`. Nothing is persisted.

## Background

- `WalletQrService` (`Services/Api/Wallet/WalletQrService.cs`) already exposes
  `GenerateExpenseQrAsync` and `GenerateEventQrAsync`, each returning a single `QrImageResult`
  (one **composite** PNG built via `IQrImageService.RenderComposite(items, header)`; `items`
  is one `QrCompositeItem{Label, Payload}` per billed member). These are consumed by
  `ExpensesController.GetQrAsync` / `EventsController.GetQrAsync` (`GET …/qr`), which return
  the composite PNG via `File(...)` (M8 file-response bypass, unwrapped).
- **Expense billing filter (verified in code):** `expense.Shares.Where(s => !s.IsSettled &&
  s.Amount > 0m && s.Member.Uuid != expense.Payer.Uuid)`. Empty → `NoOutstandingDebtForQr`
  (12003). Description per QR = `"{expense.Name} - {member.Name}"`.
- **Event billing filter (verified in code):** the M7 balance
  (`statsService.GetEventBalanceAsync`) — `balance.Rows.Where(r => r.Outstanding > 0m)`,
  amount = `row.Outstanding`, member = `row.MemberName`/`row.MemberUuid`. The event path is
  **closed-only**: `!balance.IsClosed` → `EventNotClosedForQr` (12002) BEFORE billing
  (§4.4/§5). Empty owing set → `NoOutstandingDebtForQr` (12003).
- Both operations: Premium-gated first (`tierService.EnsurePremiumFeature(MessageKeys.Feature.Qr)`
  → 403 `PremiumFeatureRequired` 13003); destination resolved via `ResolveDestinationAsync`
  (default account, or `bankAccountUuid` override — miss → `BankAccountNotFound` 12000; none →
  `NoBankAccountForQr` 12001); resource-owned 404 (`ExpenseNotFound` 6000 / `EventNotFound`
  9000). Nothing persisted (OQ17).
- Per-member payload is built via `qrContentResolver.Resolve().BuildContentAsync(new
  QrContentRequest(bankBin, accountNumber, holderName, amount, description), ct)`.
  `RenderSingle(payload, header)` renders ONE QR PNG with a header band on top (no label under
  the QR — the member name must live in the header). `BuildHeaderAsync(account, title, amount,
  ct)` builds the `QrHeader` (it already supports a non-null `amount` → renders an amount row;
  the composite paths pass `amount: null`). `FormatMoney(amount)` formats VND (e.g. `500.000đ`).
- This work replaces the client's use of the composite image with a per-member list; the
  composite `…/qr` endpoints stay in place (see Decision 3 / Open Question 1).

## Requirements

- Two new endpoints returning `ApiResult<IReadOnlyList<MemberQrResponse>>` (wrapped JSON, NOT
  `File`), one QR PNG per still-owing member as a data URL.
- Same billing filters as today — expense: unsettled, non-zero, non-payer shares; event:
  `Outstanding > 0` on the closed-event M7 balance.
- Same error/gating contract as the composite path: Premium gate (13003) first; destination
  resolution (12001 none / 12000 override-miss); resource-owned 404 (6000 expense / 9000
  event); empty billed set → 12003. Event path additionally: open event → 12002 (closed-only,
  §4.4/§5 — see the note in Open Questions / Decision Log).
- Each member's QR is rendered SERVER-SIDE with `RenderSingle` (one QR + header band), encoded
  to a data URL `data:image/png;base64,<...>`.
- Nothing persisted; QR generation stays a pure read.
- New DTO `MemberQrResponse { string MemberUuid; string MemberName; decimal Amount; string
  Image; }`.
- The composite and per-member paths MUST compute the SAME billed set (no divergence) — share
  the billing logic.
- Response ordering stable and meaningful (see Decision 5).
- All user-facing text (Swagger summaries, error messages) Vietnamese; reuse the existing 12xxx
  / 13003 / 6000 / 9000 codes and message keys — no new codes, no new migration.

## Open Questions

1. **Remove the composite `…/qr` endpoints + `RenderComposite` once the web fully migrates?**
   The new per-member endpoints supersede the composite PNG for the web, but the composite
   `GET api/v1/expenses/{uuid}/qr` / `GET api/v1/events/{uuid}/qr` remain shipped and functional.
   Options:
   - **(a) Keep both for now (recommended, and the resolved decision for THIS work).** Ship the
     per-member endpoints alongside the composite ones; the web stops calling the composite
     routes but they stay live. Trade-off: two QR surfaces to maintain briefly; zero breakage
     risk for any other consumer.
   - **(b) Remove the composite endpoints + `RenderComposite` + `QrCompositeItem` in this same
     change.** Trade-off: smaller surface, but a breaking removal while the web migration lands,
     and it deletes the only consumer of the SkiaSharp multi-cell composite path.
   **DEFERRED to the user** — not decided in this doc. Recommendation: (a) now, schedule (b) as
   a follow-up once the web is confirmed off the composite routes.

2. **Header composition for each per-member single QR.** `RenderSingle` draws one emphasized
   `QrHeader.Title` plus the destination bank fields (and an optional amount row); there is no
   label under the QR, so the member name and amount must live in the header. Options:
   - **(a) Reuse `QrHeader`/`BuildHeaderAsync` unchanged (recommended):** set `Title =
     "{contextName} - {memberName}"` (contextName = expense name or event name) and pass the
     member's amount so the header renders the localized amount row. No new `QrHeader` field, no
     `IQrImageService` change; both context and member name are visible on the image. Trade-off:
     a longer Title that wraps to `MaxTitleLines` (2) for long names — acceptable, the header
     already word-wraps + ellipsizes.
   - **(b) Add a dedicated member-name field to `QrHeader` and render it distinctly** (e.g.
     under the title). Trade-off: cleaner separation on the image, but changes the `QrHeader`
     record + `RenderSingle`/`BuildHeaderAsync` + `RenderComposite` call sites and their tests.
   - **(c) `Title = memberName` only; drop the context name from the image.** Trade-off:
     simplest title, but the payer loses the expense/event context on the QR image (it still
     lives in the VietQR memo/description, not the visible header).
   **Recommendation: (a).** Please confirm.

3. **Response ordering of the member list.** Options:
   - **(a) Preserve the existing billing order (recommended):** the expense path keeps
     `expense.Shares` order (already stable — the order the shares were created/returned); the
     event path keeps `balance.Rows` order (M7's deterministic member ordering). This makes the
     per-member list identical in order to the composite cells today. Trade-off: order depends
     on the upstream DTO ordering rather than an explicit sort — but it is already deterministic
     and matches current UX.
   - **(b) Sort by member name (culture-aware Vietnamese collation).** Trade-off: predictable
     alphabetical order, but diverges from the composite order and needs a collator choice.
   - **(c) Sort by amount descending.** Trade-off: biggest debt first, but arbitrary vs the
     current UX.
   **Recommendation: (a).** Please confirm.

4. **Event endpoint closed-only error (12002) is included but was omitted from the task's error
   list.** The brief's error contract for the new endpoints listed 13003 / 12001 / 12000 /
   6000·9000 / 12003 but did not mention `EventNotClosedForQr` (12002). The event QR is
   closed-only by locked spec (§4.4/§5) and the existing `GenerateEventQrAsync` enforces it, so
   the per-member event endpoint MUST enforce it too for parity. This doc **includes 12002** for
   the event endpoint and its tests. Flagging only because the brief's list omitted it — please
   confirm there is no intent to relax closed-only for the per-member event QR (there should not
   be; recorded as Decision 6).

## Assumptions

- `expensesService.GetAsync` returns `ExpenseResponse` with `Shares` (each `ShareResponse` has
  `Member{Uuid,Name}`, `Amount`, `IsSettled`) and `Payer{Uuid}` fully populated — confirmed by
  the shipped composite path and `expense-qr-per-member.md`.
- `statsService.GetEventBalanceAsync` returns `EventBalanceResponse{EventName, EventUuid,
  IsClosed, Rows}` where each row has `MemberUuid`, `MemberName`, `Outstanding` — confirmed by
  the shipped composite path.
- Expected member counts per expense/event are small (single digits to low tens), so returning
  N base64 PNGs in one JSON payload is acceptable; no pagination (Decision 7).
- `MemberQrResponse.Image` is a `data:image/png;base64,<...>` string built from the
  `RenderSingle` PNG via `Convert.ToBase64String` — the frontend feeds it straight into an
  `<img>`.
- The per-member QR PNG bytes are the same `RenderSingle` output the single-cell header path
  already produces (SkiaSharp, bundled SIL-OFL font) — no new rendering dependency.
- No AutoMapper profile is needed; `MemberQrResponse` is hand-built in the service.

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/`. Vietnamese for all user-facing strings
> and Swagger summaries. No DB change, no migration, no new NuGet dependency.

### Step 1 — New response DTO

`Models/Wallet/MemberQrResponse.cs` — a sealed record/class (mirror the style of
`BankAccountResponse.cs`), Vietnamese XML-doc:
- `string MemberUuid` — UUID of the still-owing member.
- `string MemberName` — denormalized display name.
- `decimal Amount` — that member's outstanding amount (VND).
- `string Image` — `data:image/png;base64,<...>` data URL of the member's single QR PNG.

### Step 2 — Factor the shared billing logic in `WalletQrService`

In `Services/Api/Wallet/WalletQrService.cs`, extract the "who owes what" computation so the
composite and per-member paths cannot diverge:
- A private record `BilledMember(string MemberUuid, string MemberName, decimal Amount, string
  Description)`.
- `private static (string ContextName, IReadOnlyList<BilledMember> Billed) CollectExpenseBillables(ExpenseResponse expense)`
  — applies the expense filter (`!IsSettled && Amount > 0 && Member.Uuid != Payer.Uuid`),
  ContextName = `expense.Name`, Description = `"{expense.Name} - {member.Name}"`, Amount =
  `share.Amount`, order preserved from `expense.Shares`.
- `private static (string ContextName, IReadOnlyList<BilledMember> Billed) CollectEventBillables(EventBalanceResponse balance)`
  — applies `Outstanding > 0`, ContextName = `balance.EventName`, Description =
  `"{balance.EventName} - {row.MemberName}"`, Amount = `row.Outstanding`, order preserved from
  `balance.Rows`.
- Refactor the existing `GenerateExpenseQrAsync` / `GenerateEventQrAsync` to build their
  `QrCompositeItem` list from these helpers (label = `"{MemberName}: {FormatMoney(Amount)}"`) —
  behaviour unchanged, but now sharing the exact billed set with the per-member path.
- Keep the `IsClosed` gate (12002) and both `if (billed.Count == 0) throw 12003` checks in the
  callers (the helpers stay pure; the empty/closed decisions stay in the orchestration methods).

### Step 3 — New service methods + interface

Add to `IWalletQrService` (with Vietnamese XML-doc extending the interface summary):
- `Task<IReadOnlyList<MemberQrResponse>> GenerateExpenseMemberQrsAsync(string userUuid, string
  expenseUuid, string? bankAccountUuid, CancellationToken cancellationToken = default)`
- `Task<IReadOnlyList<MemberQrResponse>> GenerateEventMemberQrsAsync(string userUuid, string
  eventUuid, string? bankAccountUuid, CancellationToken cancellationToken = default)`

`GenerateExpenseMemberQrsAsync` implementation:
1. `tierService.EnsurePremiumFeature(MessageKeys.Feature.Qr)` (403 13003, first).
2. `var account = await ResolveDestinationAsync(userUuid, bankAccountUuid, ct)` (12000 / 12001).
3. `var expense = await expensesService.GetAsync(userUuid, expenseUuid, ct)` (6000 on miss).
4. `var (contextName, billed) = CollectExpenseBillables(expense)`; empty → throw
   `NoOutstandingDebtForQr` (12003).
5. `BuildMemberQrsAsync(account, contextName, billed, ct)` (shared, Step 4) → return the list.

`GenerateEventMemberQrsAsync` implementation:
1. Premium gate (403 13003).
2. `ResolveDestinationAsync` (12000 / 12001).
3. `var balance = await statsService.GetEventBalanceAsync(userUuid, eventUuid, ct)` (9000).
4. `if (!balance.IsClosed) throw EventNotClosedForQr (12002)`.
5. `var (contextName, billed) = CollectEventBillables(balance)`; empty → `NoOutstandingDebtForQr`
   (12003).
6. `BuildMemberQrsAsync(account, contextName, billed, ct)` → return the list.

### Step 4 — Shared per-member render helper

`private async Task<IReadOnlyList<MemberQrResponse>> BuildMemberQrsAsync(BankAccount account,
string contextName, IReadOnlyList<BilledMember> billed, CancellationToken ct)`:
- `var provider = qrContentResolver.Resolve();`
- For each `BilledMember b` (order preserved — Decision 5):
  - `payload = await provider.BuildContentAsync(new QrContentRequest(account.BankBin,
    account.AccountNumber, account.AccountHolderName, b.Amount, b.Description), ct);`
  - `header = await BuildHeaderAsync(account, $"{contextName} - {b.MemberName}", b.Amount, ct);`
    (Open Question 2a — Title carries context + member name; a non-null amount renders the
    amount row in the header.)
  - `var png = qrImageService.RenderSingle(payload, header);`
  - `var image = "data:image/png;base64," + Convert.ToBase64String(png);`
  - add `new MemberQrResponse { MemberUuid = b.MemberUuid, MemberName = b.MemberName, Amount =
    b.Amount, Image = image }`.
- Return the list.

### Step 5 — Controller actions

`Controllers/ExpensesController.cs` — add (uses the already-injected `IWalletQrService`):
- `[HttpGet("{uuid}/qr/members")]` → `GetMemberQrsAsync([FromRoute] string uuid, [FromQuery]
  string? bankAccountUuid, CancellationToken ct)` returning
  `ApiResult<IReadOnlyList<MemberQrResponse>>.Success(await
  walletQrService.GenerateExpenseMemberQrsAsync(AuthenticatedUser.Id, uuid, bankAccountUuid,
  ct))`. Wrapped JSON (NOT `File`).
- Vietnamese `[SwaggerOperation]` mirroring the composite `GetQrAsync` but describing the
  per-member list ("Danh sách mã QR chuyển khoản theo từng thành viên còn nợ trên phiếu … mỗi
  thành viên một ảnh QR riêng dạng data URL …").
- Responses: `[SwaggerResponse(200, …, typeof(ApiResult<IReadOnlyList<MemberQrResponse>>))]`,
  `400` (12001 no account / 12003 nobody owes) as `ApiResult`, `401`, `403` (13003 Premium),
  `404` (6000 expense / 12000 bank account). No `[Produces("image/png")]` — this returns JSON.

`Controllers/EventsController.cs` — add (uses the already-injected `IWalletQrService`):
- `[HttpGet("{uuid}/qr/members")]` → `GetMemberQrsAsync(...)` returning
  `ApiResult<IReadOnlyList<MemberQrResponse>>.Success(await
  walletQrService.GenerateEventMemberQrsAsync(AuthenticatedUser.Id, uuid, bankAccountUuid, ct))`.
- Vietnamese `[SwaggerOperation]` mirroring the composite event `GetQrAsync`, per-member list,
  closed-only.
- Responses: `200` `ApiResult<IReadOnlyList<MemberQrResponse>>`; `400` (12001 / 12002 open event
  / 12003 nobody owes); `401`; `403` (13003); `404` (9000 event / 12000 bank account).

### Step 6 — Documentation

- `The-ideal.md` §3.10 "QR": note that the API also exposes a per-member QR list (each still-owing
  member's own QR) in addition to the composite image — a display/delivery variant, no rule
  change. Keep the §5 lock text intact.
- This planning doc (Progress Log + Final Outcome).
- Frontend switchover is tracked separately under `FairShareMonWeb/planning/`.

### Step 7 — Message keys

No new keys required — reuse `MessageKeys.Error.NoOutstandingDebtForQr` (12003),
`NoBankAccountForQr` (12001), `BankAccountNotFound` (12000), `EventNotClosedForQr` (12002),
`ExpenseNotFound` (6000), `EventNotFound` (9000), and the Premium gate message via
`EnsurePremiumFeature(MessageKeys.Feature.Qr)` (13003). No new success message (these are reads;
the wrapped `ApiResult<T>.Success` carries the list, no user-facing message string needed).

## Impact Analysis

- **APIs:**
  - NEW `GET api/v1/expenses/{uuid}/qr/members?bankAccountUuid={uuid?}` →
    `ApiResult<IReadOnlyList<MemberQrResponse>>`.
  - NEW `GET api/v1/events/{uuid}/qr/members?bankAccountUuid={uuid?}` →
    `ApiResult<IReadOnlyList<MemberQrResponse>>`.
  - Existing composite `GET …/qr` endpoints: UNCHANGED (Decision 3 / OQ1 — removal deferred).
- **Database:** none. No entity, no EF mapping change, no migration.
- **Infrastructure:** none. No new NuGet dependency; reuses the existing SkiaSharp/QRCoder
  render path and bundled font.
- **Services:**
  - `IWalletQrService` / `WalletQrService` — +2 public methods
    (`GenerateExpenseMemberQrsAsync`, `GenerateEventMemberQrsAsync`), +1 shared render helper
    (`BuildMemberQrsAsync`), +2 pure billing helpers (`CollectExpenseBillables`,
    `CollectEventBillables`), +1 private record `BilledMember`; the two existing `Generate*QrAsync`
    methods are refactored to consume the shared helpers (behaviour-preserving — MEDIUM risk:
    they are covered by `WalletQrServiceTests` + `WalletQrEndpointTests`; run those to confirm no
    regression).
  - `IQrImageService` / `QrImageService` — UNCHANGED (reuses `RenderSingle`).
  - No change to `qrContentResolver`, `BankAccountRepository`, `ExpensesService`, `StatsService`,
    `TierService`.
- **Controllers:** `ExpensesController` (+1 action), `EventsController` (+1 action). Both already
  inject `IWalletQrService`; no ctor change. `AppController` untouched (LOCKED).
- **Models:** NEW `Models/Wallet/MemberQrResponse.cs`. No AutoMapper profile (hand-built).
- **UI:** the web swaps the composite `<img>` for a per-member carousel/list fed by
  `MemberQrResponse.Image` data URLs — tracked in `FairShareMonWeb/planning/`.
- **Documentation:** `The-ideal.md` §3.10 note; this doc.
- **Response size:** N base64 PNGs in one JSON envelope; acceptable for the expected small
  member counts — no pagination (Decision 7).

## Decision Log

### Decision 1 — Per-member single QR, rendered server-side with `RenderSingle`
Each still-owing member gets their OWN single QR PNG via the existing `RenderSingle` (one QR +
header band), NOT a composite. Same billing filters as today.
**Reason:** resolved with the user; the web will show one QR per member.
**Alternatives considered:** keep the single composite (rejected — the UX changes to per-member).

### Decision 2 — JSON list of data URLs, not a file
The new endpoints return `ApiResult<IReadOnlyList<MemberQrResponse>>` with `Image` as a
`data:image/png;base64,<...>` data URL, wrapped by `[ResponseWrapped]` (NOT `File`).
**Reason:** resolved with the user; the frontend feeds each data URL straight into an `<img>`.
**Alternatives considered:** N separate file endpoints (rejected — chatty); a zip (rejected — not
consumable by an `<img>`).

### Decision 3 — Keep the composite `…/qr` endpoints for now
The composite `GET …/qr` endpoints and `RenderComposite` stay shipped and unchanged; the web
stops calling them. Their eventual removal is DEFERRED (Open Question 1).
**Reason:** resolved with the user (keep both now); zero breakage risk during the web migration.

### Decision 4 — Same gating/resolution/ownership contract, nothing persisted
Premium gate (13003) first, then destination resolution (12001 none / 12000 override-miss),
resource-owned 404 (6000 / 9000), empty billed set → 12003; QR generation stays a pure read.
**Reason:** resolved with the user; parity with the composite path.

### Decision 5 — Preserve the existing billing order (proposed)
The member list order preserves `expense.Shares` order / `balance.Rows` order (identical to the
composite cells). **Pending user confirmation (Open Question 3).**

### Decision 6 — Event per-member endpoint is closed-only (12002)
The event per-member endpoint enforces closed-only (`EventNotClosedForQr` 12002) exactly like the
composite event QR (§4.4/§5). **Pending user confirmation (Open Question 4).**

### Decision 7 — No pagination
All billed members' QRs are returned in one JSON payload; acceptable for the expected small
counts. **Reason:** simplicity; matches the composite path's single-image assumption.

## Progress Log

### 2026-07-22

* Created planning doc. Read `The-ideal.md` §3.10/§5, the current `WalletQrService`,
  `QrImageService`, `QrImageResult`, both controllers' `GetQrAsync`, `expense-qr-per-member.md`,
  `wallet-and-qr.md`, and the existing `WalletQrServiceTests` / `WalletQrEndpointTests`.
* Recorded the four resolved decisions (per-member single QR, JSON data-URL list, keep composite,
  same contract) as Decisions 1–4; proposed Decisions 5–7.
* Raised four Open Questions: composite-removal deferral, header composition, ordering, and the
  closed-only (12002) note the task brief omitted for the event endpoint.
* **Open Questions RESOLVED by the user (all four):** (1) keep BOTH composite and per-member
  endpoints — composite removal deferred; (2) header 2a — reuse `QrHeader`/`BuildHeaderAsync`
  unchanged, `Title = "{contextName} - {memberName}"`, pass the member amount; (3) ordering 3a —
  preserve existing billing order; (4) event endpoint is closed-only, enforce 12002.
* **Implemented** (2026-07-22):
  - Created `Models/Wallet/MemberQrResponse.cs` — `{ string MemberUuid; string MemberName;
    decimal Amount; string Image; }` (Vietnamese XML-doc), `Image` = `data:image/png;base64,<...>`.
  - Refactored `WalletQrService`: added a private `BilledMember(MemberUuid, MemberName, Amount,
    Description)` record and two pure static helpers `CollectExpenseBillables(ExpenseResponse)`
    and `CollectEventBillables(EventBalanceResponse)`. The existing `GenerateExpenseQrAsync` /
    `GenerateEventQrAsync` now build their `QrCompositeItem` list from these helpers — same
    filters, same order, same descriptions and labels as before (composite output unchanged; the
    empty-set 12003 and closed-only 12002 gates stay in the orchestration methods).
  - Added `GenerateExpenseMemberQrsAsync` / `GenerateEventMemberQrsAsync` to `IWalletQrService` +
    impl, plus the shared `BuildMemberQrsAsync(account, contextName, billed, ct)` helper that per
    billed member builds the payload via `qrContentResolver.Resolve().BuildContentAsync(...)`, the
    header via `BuildHeaderAsync(account, "{contextName} - {memberName}", amount, ct)`, renders via
    `RenderSingle`, and encodes to the `data:image/png;base64,` data URL. Same guard order as the
    composite path (Premium 13003 → destination 12000/12001 → resource 6000/9000 → event
    closed-only 12002 → empty billed set 12003). Nothing persisted.
  - Added `[HttpGet("{uuid}/qr/members")] GetMemberQrsAsync` to `ExpensesController` and
    `EventsController` returning `ApiResult<IReadOnlyList<MemberQrResponse>>.Success(...)` (wrapped
    JSON, `[Produces("application/json")]`), Vietnamese `[SwaggerOperation]` + `[SwaggerResponse]`
    (200 list / 400 / 401 / 403 / 404; event also documents 12002 open-event under 400).
  - Build succeeds (0 errors); `WalletQrServiceTests` — 21/21 pass, confirming the billing refactor
    left composite behavior intact. No migration (nothing persisted); no new NuGet.

* **Tests added by the test-engineer** (2026-07-22):
  - **Unit — `WalletQrServiceTests` (+18 over the shared fakes + `CapturingQrImageService`):**
    - Expense per-member: one entry per billed member in `expense.Shares` order (MemberUuid/Name/Amount
      correct, `Image` starts `data:image/png;base64,`, per-member payload amount asserted via
      `ParseTlv(SinglePayloads)["54"]`, composite path NOT used); excludes settled/zero/payer shares;
      all-cleared → 12003 (nothing rendered); header per member carries `Title = "{expenseName} -
      {memberName}"` + amount row = member amount (via `SingleHeaders`); `Image` base64 decodes to the
      PNG magic `89 50 4E 47`; no account → 12001; override miss → 12000; expense miss → 6000; Free
      caller → 13003 before destination resolution; per-member billed set matches the composite path.
    - Event per-member: one entry per `Outstanding > 0` row in `balance.Rows` order (amount = Outstanding,
      title = "{eventName} - {memberName}"); excludes settled/cleared rows; all-cleared → 12003; open
      event → 12002; event miss → 9000; no account → 12001; override miss → 12000; Free caller → 13003.
    - Extra (beyond the doc's list): explicit shared-billed-set PARITY test asserting the per-member list
      and the composite `items` cover the same members/order for one seeded expense.
  - **Integration — `WalletQrEndpointTests` (+10, `[SkippableFact]`, skip when DB unreachable):**
    - Expense members: 200 wrapped `ApiResult` list (`isSuccess` true, `data` array, each element
      `memberUuid`/`memberName`/`amount`/`image` with `image` a PNG data URL decoding to the PNG magic,
      content-type `application/json` not raw bytes); no-debtor → 400 12003; no account → 400 12001;
      another user's expense → 404 6000; anonymous → 401.
    - Event members: closed-with-debtor → 200 wrapped list; open event → 400 12002; closed-but-nobody-owes
      → 400 12003; no account → 400 12001; another user's event → 404 9000; anonymous → 401.
  - **Premium gate — `TierLimitEndpointTests` (+1, `[SkippableFact]`):** `Free_QrMemberRoutes_Return403Code13003`
    — Free caller → 403 13003 on both `.../qr/members` routes (mirrors `Free_QrRoutes_Return403Code13003`).
  - Results: `dotnet build` 0 errors; `WalletQrServiceTests` 39/39 pass (21 existing + 18 new);
    the endpoint + tier tests SKIP cleanly (no local MariaDB/Redis — compile + run, reported skipped
    not failed). Full suite: 757 passed, 534 skipped (all DB-backed), 0 failed. No product bug found;
    the billing refactor and per-member paths behave exactly as the planning doc specifies. Test project
    only — no production code changed.

## Impact-relevant notes / conflicts found in code

- The task brief's error list for the new endpoints omitted `EventNotClosedForQr` (12002), but
  the shipped `GenerateEventQrAsync` enforces closed-only and §4.4/§5 lock it — the plan
  INCLUDES 12002 for the event endpoint (Open Question 4 / Decision 6). No relaxation intended.
- `RenderSingle` draws NO label under the QR (labels live in the header band), so the member name
  must be carried in `QrHeader.Title` — hence the Title = `"{context} - {member}"` proposal
  (Open Question 2a). The existing expense/event composite headers pass `amount: null`; the
  per-member path passes the member's amount so the header shows it — `BuildHeaderAsync` already
  supports this with no change.
- Everything else in the brief matches the code exactly (billing filters, 12003 on empty,
  destination resolution 12000/12001, 6000/9000 ownership, 13003 Premium gate, on-demand no
  persistence).

## Final Outcome

Shipped (backend, 2026-07-22). Two new wrapped-JSON endpoints return one per-member VietQR (each a
`data:image/png;base64,<...>` data URL) per still-owing member, rendered server-side via the existing
`RenderSingle`:

- `GET api/v1/expenses/{uuid}/qr/members?bankAccountUuid={uuid?}` → `ApiResult<IReadOnlyList<MemberQrResponse>>`
- `GET api/v1/events/{uuid}/qr/members?bankAccountUuid={uuid?}` → `ApiResult<IReadOnlyList<MemberQrResponse>>` (closed-only)

`MemberQrResponse { string MemberUuid; string MemberName; decimal Amount; string Image; }` — the API
camelCases responses, so the client receives `memberUuid`, `memberName`, `amount`, `image`.

Composite `…/qr` endpoints kept unchanged (Decision 3 / OQ1a). The billed set is now shared between the
composite and per-member paths (`CollectExpenseBillables` / `CollectEventBillables` + `BilledMember`), so
the two surfaces cannot diverge. Build: 0 errors. `WalletQrServiceTests`: 21/21 pass (composite behavior
unchanged). No DB migration, no new NuGet. New tests are owned by the test-engineer.

Files: `Models/Wallet/MemberQrResponse.cs` (new); `Services/Api/Wallet/WalletQrService.cs`,
`Controllers/ExpensesController.cs`, `Controllers/EventsController.cs` (edited).

## Future Improvements

- Remove the composite `…/qr` endpoints, `RenderComposite`, and `QrCompositeItem` once the web is
  confirmed off the composite routes (Open Question 1b).
- If member counts ever grow large, consider streaming/paging or returning per-member QR URLs
  instead of inline base64 (data-URL payloads grow ~1.33× the PNG size each).
- Consider a shared `QrShareMode` seam if additional QR delivery shapes are added later.

## Tests (for the test-engineer)

Reuse the shipped harness and fakes; DB/endpoint tests are `[SkippableFact]` under
`[Collection("AuthIntegration")]` against real MariaDB (never EF InMemory).

**Unit — `WalletQrServiceTests` (extend; mirror the existing composite cases over the fakes +
`CapturingQrImageService`):**
- Expense per-member: one `MemberQrResponse` per billed member (unsettled, non-zero, non-payer
  share), each with the correct `MemberUuid`/`MemberName`/`Amount` and an `Image` starting with
  `"data:image/png;base64,"` (base64 body decodes to bytes with the PNG magic `89 50 4E 47`).
- Excluded from the list: settled shares, zero-amount shares, the payer's own share.
- Expense all-cleared / all-settled / only-payer-and-zero → `NoOutstandingDebtForQr` (12003).
- Expense: no account → 12001; owned override miss → 12000; Premium gate fires first (Free
  caller → 13003 before resolution); order preserved = `expense.Shares` order (Decision 5).
- Event per-member: one entry per `Outstanding > 0` row, amount = `row.Outstanding`, data-URL
  image; open event → 12002; nobody owes → 12003; no account → 12001 / override miss → 12000;
  Premium gate → 13003; order preserved = `balance.Rows` order.
- Header carries the member amount (the capturing image service records a `QrHeader` whose
  `AmountText` is non-null and whose `Title` contains the member name) — proves Open Question 2a.
- Shared-billed-set parity: assert the per-member list and the composite `items` (from
  `Generate*QrAsync`) cover the SAME members/amounts for the same seeded input.

**Integration — endpoint tests `WalletQrEndpointTests` (extend; mirror the composite cases):**
- `GET api/v1/expenses/{uuid}/qr/members` and `GET api/v1/events/{uuid}/qr/members` return a
  WRAPPED `ApiResult` envelope (NOT raw bytes) with `isSuccess = true` and a `data` array; each
  element has `memberUuid`, `memberName`, `amount`, and an `image` starting with
  `"data:image/png;base64,"` whose decoded body starts with the PNG magic.
- Expense: 12001 (no bank account), 12003 (nobody owes / all settled), 6000 (foreign/unknown
  expense — note account resolution precedes the resource check, matching the shipped behaviour),
  401 (anonymous), 403 (Free caller — Premium gate).
- Event: 12001, 12002 (open event), 12003 (nobody owes), 9000 (foreign/unknown event), 401, 403.
- Ordering: the returned list order matches the seeded billing order (Decision 5).
