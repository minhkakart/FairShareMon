# Expense QR per-member (composite, like the event QR)

## Objective

Change the expense (phiếu) QR from a single whole-expense VietQR to a **per-member
composite**: one VietQR per member who still owes on that expense, gathered into a
single labelled PNG — identical in shape to the already-shipped event (đợt) QR.

## Background

- `WalletQrService.GenerateExpenseQrAsync` today builds one payload for the whole
  expense (`amount = expense.Total`) and renders it with `RenderSingle`. It also
  supports a `?format=payload` mode returning the raw VietQR string
  (`ExpenseQrResult` / `ExpensesController.GetQrAsync`).
- `WalletQrService.GenerateEventQrAsync` already does the per-member composite:
  it bills every balance row with `Outstanding > 0`, one `QrCompositeItem` per member
  (amount = debt, description = `"{Event} - {Member}"`, label = `"{Member}: {money}"`),
  a header with no amount, then `RenderComposite`. Empty → `NoOutstandingDebtForQr`
  (12003). This work reuses that exact machinery for the expense.
- The expense detail DTO already embeds everything needed inline: `ExpenseResponse.Shares[]`
  (each `ShareResponse` has `Member`, `Amount`, `IsSettled`) and `ExpenseResponse.Payer`.
  This mirrors the settled-per-member feature ([settled-per-member.md]) — a share
  marked `IsSettled` disappears from the next QR.

## Requirements

- Bill shares where `!IsSettled && Amount > 0 && Member.Uuid != Payer.Uuid`.
- No billable share → throw `NoOutstandingDebtForQr` (12003).
- Composite header carries no amount (per-member amounts sit under each QR).
- Description per QR = `"{expense.Name} - {member.Name}"`; label = `"{member.Name}: {money}"`.
- Remove the `?format=payload` expense mode entirely; the endpoint returns the PNG only.
- Reuse existing `ResolveDestinationAsync`, `BuildHeaderAsync`, `FormatMoney`,
  `QrCompositeItem`, `RenderComposite`. Premium gate + destination resolution unchanged.
- Keep the ownership/tier/no-account error contract (6000 / 12000 / 12001 / 13003).

## Open Questions

Resolved with the user before implementation:
- **Billing filter** → unsettled, non-zero, non-payer shares.
- **`?format=payload`** → removed (event QR has no payload mode; web never used it).
- **Spec** → update `The-ideal.md` §3.10 + summary (this doc records the change).

## Assumptions

- `expensesService.GetAsync` returns `ExpenseResponse` with `Shares` fully populated
  (confirmed — the web expense detail page renders shares from the same payload).
- Excluding the payer's own share is correct: the payer paid the total and does not
  transfer to themselves; 0đ owner-representative shares are excluded by `Amount > 0`.

## Implementation Plan

1. `Services/Api/Wallet/WalletQrService.cs`: change `IWalletQrService.GenerateExpenseQrAsync`
   + impl to return `Task<QrImageResult>`, drop the `string? format` param, remove
   `PayloadFormat`/`IsPayloadFormat`, and replace the single-payload block with the
   per-member loop (mirror `GenerateEventQrAsync`). Update the interface XML-doc.
2. `Controllers/ExpensesController.cs` `GetQrAsync`: drop `format` param + `IsPayload`
   branch, `[Produces("image/png")]`, rewrite the Vietnamese Swagger summary to the
   per-owing-member composite (mirror the event QR action), document 12003.
3. `Models/Wallet/QrImageResult.cs`: delete the now-unused `ExpenseQrResult` class.
4. `The-ideal.md` §3.10 "QR cho phiếu" + line-161 summary: per-member composite.
5. Tests — see below.
6. Frontend display touch-ups (tracked in `FairShareMonWeb/planning/`).

## Impact Analysis

- APIs: `GET /v1/expenses/{uuid}/qr` — behavior change (per-member PNG), `format`
  query param removed. Same status codes; adds 12003 (nobody owes / all settled).
- Services: `WalletQrService.GenerateExpenseQrAsync` (MEDIUM risk per gitnexus —
  callers = controller + 12 unit tests). `ExpenseQrResult` deleted (LOW, 0 upstream).
- Database / Infrastructure: none.
- UI: composite frame ratio + drop the single amount row (display only).
- Documentation: `The-ideal.md`, this planning doc.

## Progress Log

### 2026-07-21

* Planning doc created; open questions resolved with user (filter / payload / spec).
* Implemented `GenerateExpenseQrAsync` per-member loop (return `QrImageResult`, no `format`),
  updated `ExpensesController.GetQrAsync` + Swagger, deleted `ExpenseQrResult`.
* Updated `The-ideal.md` §3.10 + summary. Frontend: expense QR frame → composite ratio,
  dropped the single-amount row + `amount` prop, kind-aware 12003 copy, new alt text.
* Tests: rewrote `WalletQrServiceTests` expense cases (composite / exclude settled·zero·payer /
  12003 / no-amount header), fixed the two endpoint fixtures that now need a billable non-payer
  share, added expense 12003 endpoint + frontend tests, removed the payload-mode tests.
* Verified: `dotnet build` clean; 21 `WalletQrServiceTests` green; endpoint tests compile
  (skip without DB). Frontend `tsc -b` clean, lint clean, 249 wallet/expenses tests green.

## Final Outcome

The expense QR now renders one VietQR per still-owing member (unsettled, non-zero, non-payer
share; amount = that share) composited into one labelled PNG — the same shape as the event QR —
returning `NoOutstandingDebtForQr` (12003) when every share is settled/payer/zero. The
`?format=payload` mode was removed and `ExpenseQrResult` deleted. Docs and tests are in sync.
Endpoint integration tests were only compiled (no MariaDB/Redis in this environment) — they run
under CI with a live DB.
