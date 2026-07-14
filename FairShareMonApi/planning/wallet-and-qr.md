# Wallet + QR (Milestone 9: Ví & QR chuyển khoản)

The owner's **wallet** — a CRUD list of receiving **bank accounts** with exactly one default (atomic
swap, mirroring the M4 default-category invariant) — plus **on-demand VietQR generation** for a whole
expense (§3.5) and for a closed event (§3.10/§5): one QR per still-owing member, composited into a
single shareable image. This is the project's **first new runtime NuGet dependency** (a QR-rendering
library), so its license and cross-platform behaviour are first-class decisions here.

## Objective

Implement `The-ideal.md` §3.10 (Ví & QR chuyển khoản) on top of the shipped
Auth + Members + Categories + Tags + Expenses/Shares/Audit + Events + Stats + Export stack:

- **Wallet / bank accounts (§3.10):** the owner manages a list of their bank accounts — add / edit /
  delete — with **exactly one default** account. The default is the receive destination for QR
  generation (another account can be chosen at generation time). Resource-owned (§4.1).
- **Expense QR (§3.5 / §3.10 / §5 lock):** the owner actively generates, on demand, a **single VietQR
  for a whole expense**, amount = the expense total (= `SUM(shares)`, reused from M5), destination = the
  owner's default (or a chosen) bank account. Resource-owned.
- **Event QR (§3.10 / §4.4 / §5 lock):** available **only after the event is closed** (data frozen).
  Produce **one VietQR per still-owing member** (negative per-event balance — reused from M7), amount =
  exactly that member's debt, all gathered into **one image** labelled with each person's name + amount,
  to share with the group. Resource-owned; reject an open event.
- **VietQR standard + rendering + composition:** the central technical work — the EMVCo/VietQR payload
  string (TLV + CRC-16/CCITT), a QR-image renderer (new dependency), and the multi-QR composite for the
  event image.
- Cross-cutting: resource-owned 404-never-403 (§4.1); money is `decimal`, non-negative (§4.3); the M8
  `FileContentResult` bypass of `[ResponseWrapped]` (an image is not an `ObjectResult`, so it streams
  unwrapped) with **no edit to the LOCKED `AppController`**; Vietnamese user-facing messages.

This milestone owns a new **`bank_accounts`** table + CRUD, a **VietQR payload builder**, a **QR image
service** (single QR + event composite), and two **QR image routes** on the existing expense/event
controllers. It reuses M5 `IExpensesService.GetAsync` (expense total), M7
`IStatsService.GetEventBalanceAsync` (still-owing members), M6 event closed-state, the M4
default-swap-in-one-transaction pattern, and the M8 file-response bypass.

## Background

- **QR generation in the sibling `quick-ordering` (the strongest dependency signal):**
  - `QuickOrdering/Extensions/QrCodeExtensions.cs` uses **QRCoder** — `new QRCodeGenerator()` →
    `CreateQrCode(content, ECCLevel.Q)` → `new PngByteQRCode(qrCodeData).GetGraphic(pixelsPerModule)`
    to produce **PNG bytes with NO `System.Drawing` dependency** (the `PngByteQRCode` renderer is
    pure-managed and cross-platform — safe on non-Windows/Docker).
  - `QuickOrdering.csproj` pins **`QRCoder` Version `1.4.3`**. QRCoder is **MIT-licensed** (all
    versions), so it carries no split-/commercial-license trap (unlike the AutoMapper 14 situation
    CLAUDE.md warns about).
  - quick-ordering's QR encodes an **ordering URL** (a plain string), **not** a VietQR payload — there
    is **no EMVCo/VietQR payload builder** and **no multi-QR composite** to reuse. Its `VNPay.NetCore`
    usage (`Services/Payments/Providers/VnPayPaymentProvider.cs`) is a **redirect-URL payment gateway**
    (builds a signed `vnp_*` query string), unrelated to QR image generation — not reusable here.
  - **Net:** the reusable pattern from the sibling is precisely "string → PNG via QRCoder
    `PngByteQRCode`, MIT, no System.Drawing". The VietQR payload and the event composite are new work.
- **The M8 file-response bypass is directly reusable** (`planning/export-csv.md`, shipped, verified in
  the live `ExpensesController.ExportAsync`): a controller derived from `AppController` returns
  `File(bytes, contentType, fileName)` (a `FileContentResult`); `ResponseWrappedAttribute` only rewraps
  `ObjectResult`, so the file streams out **unwrapped**, while a thrown `ErrorException` (resource-owned
  miss) is still wrapped into `ApiResult` by the error filter/middleware. **No `AppController` edit.**
  A QR image (PNG/SVG bytes) rides the exact same seam.
- **M7 balance is directly reusable** (`planning/debt-balance-and-stats.md`, shipped):
  `IStatsService.GetEventBalanceAsync(userUuid, eventUuid, ct)` → `EventBalanceResponse`
  { `EventUuid`, `EventName`, `IsClosed`, `Rows: IReadOnlyList<MemberBalanceRow>` }. Each
  `MemberBalanceRow` carries `MemberUuid`, `MemberName` (denormalized so a deleted member still
  displays), `IsOwnerRepresentative`, `IsDeleted`, `Advanced`, `Owed`, `Balance`. A **negative
  `Balance` = the member still owes** (`§3.7`); its magnitude is exactly the amount for that member's
  QR. It also resolves the event resource-owned 404 (`EventNotFound` 9000) and exposes `IsClosed`
  (event-QR gate). **Reuse — never recompute.**
- **M5 expense total is directly reusable:** `IExpensesService.GetAsync(userUuid, uuid, ct)` →
  `ExpenseResponse` whose `Total` is the derived `SUM(shares)` (`decimal(18,2)`, non-negative); it
  throws `ExpenseNotFound` (6000) on an ownership miss. `Name` is available for the QR memo.
- **The M4 default-invariant pattern is the blueprint for the default bank account** (verified in the
  live `CategoryRepository.SetDefaultAsync` + `CategoriesService`): the atomic swap loads the target
  (active, owned; `NoCommit()`/miss if absent), clears `IsDefault` on the current default, sets it on
  the target — all in **one `ExecuteTransactionAsync`**; a dedicated `PUT /{uuid}/default` route drives
  it; the normal update never touches `IsDefault`. The wallet mirrors this exactly (see OQ6 for the
  differences: a wallet may legitimately have zero accounts, and its default may be deletable).
- **Conventions confirmed from the live code (identical M2–M8):** partial POCO entity
  `Database/Entities/<Name>.cs` + `Database/Entities/Partials/<Name>.cs` (ctor sets `Uuid =
  Uuid.NewV7()`, `CreatedAt = AppDateTime.Now`; static `ConfigureModel(ModelBuilder)`); `IEntity`
  (`ulong Id`, `string Uuid` max 64 unique, `CreatedAt`, `UpdatedAt` `ValueGeneratedOnAddOrUpdate` +
  `current_timestamp(6) ON UPDATE …`); snake_case columns; FK cascade to `users`; repositories =
  interface + sealed impl in one file, `[ScopedService(typeof(IX))]`, extend `BaseRepository`, reads via
  `ExecuteQueryAsync`, writes via `ExecuteTransactionAsync` + `TransactionContext.NoCommit()`;
  `ResolveUserIdAsync` resolves owner id; services `[ScopedService(typeof(IX))]` primary ctors,
  Vietnamese messages, map a resource-owned miss to `ErrorException`; controllers derive from
  `AppController` (LOCKED), routes `api/v{version:apiVersion}/[controller]`, `[ResponseWrapped]` →
  `ApiResult<T>`, `AuthenticatedUser.Id` = current user UUID, Vietnamese `[SwaggerOperation]`/
  `[SwaggerResponse]`; FluentValidation auto-registered by `AddValidatorsFromAssembly`; `ErrorCodes`
  one 1xxx-block per feature — next free block is **12xxx** (10xxx Stats reserved, 11xxx Export
  reserved). Money is `decimal`, never float; DB CHECK for non-negative amounts.
- The dev DB holds no real product data beyond disposable smoke rows.

## Requirements

From `The-ideal.md` §3.5 (expense total), §3.7 (per-event balance, negative = owing), §3.10 (wallet +
QR), §3.11/§5 (Premium "mở rộng" grouping), and cross-cutting §4.1/§4.3/§4.4/§4.9:

**Wallet / bank accounts (§3.10):**
- Add / edit / delete a bank account owned by the current user; resource-owned (miss → 404, never 403).
- **Exactly one default** among a user's accounts (atomic swap on set-default); the default is the QR
  receive destination unless another is chosen at generation time.
- The fields must carry what VietQR needs to name the destination: a bank identifier (NAPAS BIN), an
  account number, an account holder name, plus the default flag (exact fields/validation = OQ4/OQ5).

**Expense QR (§3.5 / §3.10 / §5):**
- On-demand, one VietQR for a whole expense; amount = the expense total (reused from M5); destination =
  the default account (or a `?bankAccountUuid=` override — OQ8). Resource-owned (miss → 6000/404).
- No accounts / no default → a clear error (OQ6/OQ11), never a broken QR.

**Event QR (§3.10 / §4.4 / §5):**
- **Closed-events only** — reject an open event with a clear error (§4.4). Resource-owned (miss →
  9000/404).
- One VietQR per **still-owing** member (negative M7 balance), amount = exactly `|Balance|`; composited
  into **one image** labelled with each person's name + amount. Nobody owes → error/empty (OQ13).
- Destination = the owner's default account (or override — OQ8).

**VietQR / rendering:**
- The QR payload is the Vietnamese bank-transfer standard **VietQR** (EMVCo TLV + CRC-16/CCITT over
  NAPAS BIN + account, service code account-transfer, VND, amount, memo) — OQ1.
- QR module rendering + the event composite require a rendering approach that is **license-safe** and
  **cross-platform (no `System.Drawing.Common` on Linux)** — OQ2/OQ3.

**Cross-cutting:**
- Resource-owned (§4.1); money `decimal`, non-negative (§4.3); closed-only event write-freeze (§4.4 —
  QR is a read, allowed on closed events, the ONLY QR the spec gates to closed). Reads/QR are not
  limit-gated (§4.9); tier gating of the whole wallet/QR group is M10 (OQ14). Schema via EF migration
  only; Vietnamese messages; the new 12xxx error block (OQ12).

## Open Questions

> **All 17 answered by the user at the 2026-07-14 checkpoint — 16 accepted at the recommended option (a);
> OQ3 the user chose option (b)** (a raster SkiaSharp PNG composite for the event image, over the no-dep
> SVG recommendation, because a PNG is universally previewable in group chats — §3.10 "gửi vào nhóm
> chat"). The annotated questions below carry the binding answers inline; the full options/trade-offs are
> preserved beneath each for the record and mirrored in the Decision Log. **No open questions remain —
> implementation can start.** The Implementation Plan, dependency section, error-code table, endpoint
> table, and test list below are synced to these answers. Decisions locked in §5 (QR: expense = manual
> single QR = total; event = one QR per owing member after close, one image) and in prior planning docs
> (domain terms; total = derived SUM(shares); balance per-event only, negative = owing; resource-owned
> 404-never-403; `AppController` LOCKED; `FileContentResult` bypass) were NOT reopened.

**OQ1 — QR standard + VietQR payload: hand-roll the EMVCo string, or add a payload library?**
The Vietnamese bank-transfer standard is **VietQR** (EMVCo-compliant TLV, NAPAS GUID `A000000727`,
acquirer BIN + consumer account, service code `QRIBFTTA` = transfer-to-account, currency `704` VND,
amount, memo, terminated by a **CRC-16/CCITT-FALSE** (poly `0x1021`, init `0xFFFF`) over the payload
including the `6304` tag). Data needed per QR: acquirer **bank BIN**, **account number**, **amount**,
**addInfo/description** (transfer memo).
> ~~**OQ1**~~ → **Answered 2026-07-14 (option a):** hand-roll a `VietQrPayloadBuilder` (EMVCo TLV:
> 00/01, 38 = NAPAS GUID `A000000727` + BIN + account + service `QRIBFTTA`, 53 = `704` VND, 54 = amount,
> 62-08 = memo, 63 = CRC-16/CCITT-FALSE). No payload library. Unit-test against a known-good VietQR sample.
- **(a) [recommended] Hand-roll a small `VietQrPayloadBuilder`** (build the TLV fields + compute
  CRC-16/CCITT-FALSE; ~100 well-specified, fully unit-testable lines). Trade-off: a little code + a CRC
  routine to test, but the format is precisely specified, keeps the dependency surface to just the image
  renderer (OQ2), and is exactly the minimal-dependency ethos M8 applied to CSV (hand-rolled rather than
  CsvHelper). Correctness is provable against a known VietQR sample string.
- **(b)** Add a VietQR-specific NuGet (e.g. a community `VietQr`/`QrPay` package). Trade-off: less code,
  but these packages are small/thinly-maintained, need a license + version review, and add a second new
  dependency for a format we can generate in ~100 tested lines.
- **(c)** Call an external VietQR image service (e.g. `img.vietqr.io`). Trade-off: zero local rendering,
  but adds a runtime HTTP dependency (network + latency + availability + possible API key), leaks the
  owner's account/amount to a third party, and fails offline — inconsistent with a self-contained API.

**OQ2 — QR module rendering library + LICENSE (the big one).**
Rendering the QR matrix to bytes cannot reasonably be hand-rolled (QR encoding + Reed-Solomon + PNG
codec is thousands of lines). Candidates:
> ~~**OQ2**~~ → **Answered 2026-07-14 (option a, version 1.6.0):** add **`QRCoder` 1.6.0 (MIT)** — the
> project's FIRST new runtime dependency; use `PngByteQRCode` for the single-expense PNG (no
> `System.Drawing`).
- **(a) [recommended] `QRCoder` (MIT), using `PngByteQRCode` — the sibling-proven choice.** MIT across
  all versions (no split-/commercial trap), `PngByteQRCode` emits PNG bytes with **no `System.Drawing`**
  (cross-platform/Docker-safe), and `SvgQRCode` emits an SVG string (also System.Drawing-free) — the
  latter matters for the event composite (OQ3b). **Exact package + version: `QRCoder` — recommend
  `1.6.0` (latest MIT release) OR `1.4.3` to match quick-ordering byte-for-byte** (identical API); the
  version choice is a sub-decision (recommend `1.6.0` for currency; `1.4.3` if strict sibling parity is
  preferred). Trade-off: one new runtime dependency — but MIT, tiny, pure-managed, and already validated
  in the sibling.
- **(b)** `Net.Codecrete.QrCodeGenerator` (MIT). Trade-off: also MIT and clean, but not the
  sibling-proven choice and only produces an SVG/matrix (PNG needs your own codec) — QRCoder is a closer
  fit and already vetted here.
- **(c)** Hand-roll the QR matrix + PNG encoder. Trade-off: zero dependency, but impractical
  (thousands of lines, high bug risk) — not justified when an MIT library exists.

**OQ3 — Event composite image: approach + output format (the second license-sensitive decision).**
The event QR must combine N per-member QRs + name/amount **text labels** into **one image**. Text
rendering + image layout is what forces the choice (the expense QR is a single QR and needs no
composition — QRCoder `PngByteQRCode` alone suffices there).
> ~~**OQ3**~~ → **Answered 2026-07-14 (option b — USER OVERRODE the SVG recommendation):** the event
> composite is a **raster PNG** built with **SkiaSharp** (MIT binding / BSD-3 native) — add `SkiaSharp` +
> `SkiaSharp.NativeAssets.Linux.NoDependencies` (cross-platform / Docker headless) — composing each
> member's QR + name + amount label into one shareable PNG (matches §3.10 "gửi vào nhóm chat"). This is a
> **SECOND** new dependency. **Requires a Vietnamese-capable font:** bundle a small **SIL-OFL** font (e.g.
> Be Vietnam Pro, or a Noto Sans subset) as a project Content/EmbeddedResource, loaded via SkiaSharp; if
> committing a font binary is blocked, the implementer may fall back to a system typeface via
> `SKFontManager.Default.MatchCharacter`/`MatchFamily` for dev and MUST flag the portability gap. Record
> the font choice + license in the dependency section.
- **(a) [recommended] Hand-composed SVG using QRCoder's `SvgQRCode` — NO raster dependency beyond
  QRCoder.** Build one SVG document that vertically stacks each member's QR (inline SVG from
  `SvgQRCode`, or a base64 `PngByteQRCode` in an `<image>`) with an `<text>` label (name + formatted
  amount) beside/under each; serve `image/svg+xml`. Trade-off: adds **only** the OQ2 dependency (no
  raster/font library, best honours the minimal-dependency ethos), and Vietnamese text renders via the
  viewer's fonts. **But** SVG previews inline in browsers and many chat apps yet a few chat clients show
  it as a file attachment rather than an inline image, and turning it into a raster PNG later needs a
  separate rasteriser. Because the spec's intent is "share one image to the group chat", confirm SVG is
  acceptable vs a raster PNG (b).
- **(b)** Add **`SkiaSharp` (MIT binding; native Skia is BSD-3-Clause)** and compose a **PNG** (draw
  each QR bitmap + text on an `SKCanvas`). Trade-off: a true raster PNG, universally previewable in every
  chat app, and license-safe (MIT/BSD, no split-license trap) — **but** it adds a native-asset
  dependency (needs `SkiaSharp.NativeAssets.Linux.NoDependencies` for Docker) **and a bundled
  Vietnamese-capable TTF** (e.g. an SIL-OFL font, since a headless container has no fonts for the
  diacritics), making it the heaviest option (larger than much of the current stack).
- **(c)** Add **`SixLabors.ImageSharp` (+ `ImageSharp.Drawing` + `Fonts`)** and compose a PNG.
  Trade-off: capable and pure-managed, **but the Six Labors Split License is commercial beyond revenue
  thresholds — a genuine license trap of exactly the kind CLAUDE.md warns about (AutoMapper 14).**
  **Not recommended.**

**OQ4 — Bank reference data: a banks table/enum, or store the BIN on the account?**
VietQR needs the acquirer **NAPAS BIN** (6 digits). Either the system curates a bank list (name ↔ BIN)
or the account stores the BIN directly.
> ~~**OQ4**~~ → **Answered 2026-07-14 (option a):** store `bank_bin` + `bank_name` directly on the
> account; NO separate banks reference table.
- **(a) [recommended] Store the BIN (+ a display bank name) directly on the account; no banks table.**
  The client already renders a bank picker (it maps BIN ↔ name/logo); the server just persists what it's
  given and validates the BIN format (6 digits). Trade-off: the server can't verify the BIN maps to a
  real bank, and two accounts might store slightly different display names for the same BIN — acceptable,
  and it avoids shipping/maintaining a ~50-row NAPAS reference table that would need a seed + migrations
  to stay current. Consistent with M4's "the server doesn't enumerate the icon catalog" call.
- **(b)** Ship a curated `banks` reference table (BIN, short name, full name, maybe logo key), seeded via
  a bootstrap/backfill, and have the account FK to it. Trade-off: server-side validation + a canonical
  name, but data-heavy, needs upkeep as NAPAS membership changes, and adds a second table + seed this
  milestone.

**OQ5 — Bank account fields + validation.**
Assuming OQ4a (BIN on the account), pin the columns + rules:
> ~~**OQ5**~~ → **Answered 2026-07-14 (option a):** `bank_bin` `^\d{6}$`; `bank_name` required max 100;
> `account_number` `^\d{6,19}$`; `account_holder_name` required max 100; `is_default`.
- **(a) [recommended]** `bank_bin` (required, exactly 6 digits, regex `^\d{6}$`, max len 6);
  `bank_name` (required display name, max 100); `account_number` (required, digits only, regex
  `^\d{6,19}$` — NAPAS account numbers are numeric, typically 6–19 digits, max len 19);
  `account_holder_name` (required, max 100 — used as the composite label and some bank apps show it);
  `is_default` (bool). Trade-off: the numeric account-number rule rejects a few banks that historically
  used alphanumerics, but the vast majority are numeric and this catches typos; the length window is
  generous. Confirm the account-number rule.
- **(b)** Looser rules (account number any non-empty string up to 30; holder name optional). Trade-off:
  accepts every bank, but lets malformed accounts through to a QR that a bank app rejects at scan time.

**OQ6 — Default-account invariant: auto-assign, deletability, zero-accounts.**
Mirrors M4 but a wallet differs (zero accounts is a valid state; unlike the always-one default
category).
> ~~**OQ6**~~ → **Answered 2026-07-14 (option a):** first account auto-becomes default; dedicated
> `PUT /{uuid}/default` atomic swap (mirror M4); deleting the default promotes another (most-recent) to
> default; deleting the last leaves an empty wallet.
- **(a) [recommended]** Exactly one default **whenever ≥1 account exists**: the **first** account added
  is auto-set default; **set-default** is a dedicated `PUT /bank-accounts/{uuid}/default` doing the
  atomic swap in one `ExecuteTransactionAsync` (clear old, set new — the M4 pattern); the normal update
  never touches `is_default`. **Deleting the default IS allowed** and **promotes** another account (the
  most recently created remaining one) to default; deleting the **last** account leaves the wallet empty
  (zero accounts, no default — a valid state). Trade-off: differs from M4's not-deletable default, but a
  wallet has no "must always exist" requirement — an empty wallet just can't generate a QR until an
  account is added (OQ11).
- **(b)** Do not auto-assign; the user must explicitly set a default; deleting the default is rejected
  until another is elected. Trade-off: more explicit, but a freshly-added single account with no default
  can't be used for QR until an extra call, and delete-then-can't-delete-default is clunky.

**OQ7 — Bank account: soft-delete or hard-delete?**
Unlike members/categories/tags/expenses, a bank account is **not referenced by any stored historical
data** — QR images are generated on demand and never persisted, so a deleted account leaves no dangling
history.
> ~~**OQ7**~~ → **Answered 2026-07-14 (option a):** bank accounts are **hard-deleted** (NOT
> `IEntityDeletable`) — no historical linkage, QR is ephemeral.
- **(a) [recommended] Hard-delete.** No history to preserve (§4.7 is about resources that appear in
  historical ledger data; bank accounts never do), so a plain delete keeps the model simple; already-
  shared QR images are unaffected (the account details are baked into the shared image, not looked up
  again). Trade-off: departs from the codebase's soft-delete default, but for a resource with zero
  historical linkage that default doesn't apply.
- **(b) Soft-delete** (`is_deleted`, reuse `BaseRepository.Query`) for consistency with every other
  entity. Trade-off: uniform with the codebase, but keeps rows that serve no historical purpose and adds
  `includeDeleted` plumbing the wallet doesn't need.

**OQ8 — Expense/Event QR destination selection.**
> ~~**OQ8**~~ → **Answered 2026-07-14 (option a):** destination = the user's default account, with a
> `?bankAccountUuid=` override (must be owned).
- **(a) [recommended]** Default to the owner's default account; allow a `?bankAccountUuid=` query
  override at generation time (validated resource-owned; a foreign/unknown uuid → 12000/404). No
  accounts / no default and no override → `NoBankAccountForQr` (OQ11). Trade-off: one optional param,
  matches §3.10 "có thể chọn tài khoản khác lúc tạo".
- **(b)** Always use the default; no override. Trade-off: simpler, but loses the spec's explicit
  "choose another account at generation time".

**OQ9 — QR memo / addInfo (EMVCo field 62-08) content.**
> ~~**OQ9**~~ → **Answered 2026-07-14 (option a):** expense QR memo = expense name; event per-member memo
> = `"{event} - {member}"`; ASCII-fold diacritics + truncate to ~25 chars.
- **(a) [recommended]** Expense QR memo = the **expense name** (trimmed, ASCII-folded, truncated to a
  safe length e.g. 25 chars — some banks limit the memo); event per-member memo = **`"{event name} -
  {member name}"`** (folded + truncated) so the payer sees what the transfer is for. Trade-off: diacritics
  are folded in the memo for maximum bank-app compatibility (the QR still transfers correctly; the
  composite label keeps full Vietnamese). Confirm folding + the length cap.
- **(b)** No memo (omit field 62). Trade-off: smaller QR, but the payer's bank statement shows no
  context; the spec's UX ("quét là chuyển khoản nhanh") benefits from a memo.
- **(c)** Let the caller pass a custom memo via query. Trade-off: flexible, but adds surface/validation
  and isn't asked for by §3.10.

**OQ10 — QR image response format + content types.**
> ~~**OQ10**~~ → **Answered 2026-07-14 (option a):** expense QR returns a **PNG** (`FileContentResult`, M8
> bypass) by default; `?format=payload` returns the raw VietQR string as JSON. Event QR returns the
> composite **PNG** as a file.
- **(a) [recommended]** **Expense QR:** default a **PNG** (`FileContentResult`, `image/png`, reusing the
  M8 bypass), via QRCoder `PngByteQRCode`; also support `?format=payload` returning the raw VietQR
  string in the normal `ApiResult<string>` envelope (for clients that render their own). **Event QR:**
  the **composite image** per OQ3 — `image/svg+xml` (if OQ3a) or `image/png` (if OQ3b) — always a file;
  `Content-Disposition: attachment; filename="…"` (uuid-/slug-based, mirroring M8 OQ18). Trade-off: two
  formats for the expense QR (image + payload) but each is trivial and self-documenting; the event QR is
  image-only by nature.
- **(b)** Return only the payload string(s) as JSON and let the client render every QR/image. Trade-off:
  no rendering dependency at all (skips OQ2/OQ3), but pushes VietQR encoding, QR rendering, AND the
  labelled composite onto every client — contradicts §3.10's "hệ thống tạo … một ảnh" (the system
  produces the image), and the event composite is exactly the server's job.
- **(c)** Return only images (no payload option). Trade-off: simplest surface, but a client that wants to
  render natively must re-derive the VietQR string.

**OQ11 — No account / no default when generating a QR.**
> ~~**OQ11**~~ → **Answered 2026-07-14 (option a):** no account / no default → `12001 NoBankAccountForQr`
> (400).
- **(a) [recommended]** If the user has **no account** (or no default and no `?bankAccountUuid`
  override), reject with `NoBankAccountForQr` (12001, HTTP 400) and a clear Vietnamese message
  ("Chưa có tài khoản ngân hàng để tạo mã QR."). Trade-off: an explicit, actionable error vs a
  meaningless empty image.
- **(b)** Return 404. Trade-off: conflates "no wallet configured" with "resource not found" — less
  clear.

**OQ12 — Error-code block (12xxx Wallet/QR).**
> ~~**OQ12**~~ → **Answered 2026-07-14 (option a):** 12xxx block — `12000 BankAccountNotFound` (404),
> `12001 NoBankAccountForQr` (400), `12002 EventNotClosedForQr` (400), `12003 NoOutstandingDebtForQr`
> (400); extend `GetDefaultHttpStatus`.
- **(a) [recommended]** Claim the **12xxx** block: `12000 BankAccountNotFound` (404, resource-owned
  miss, never 403); `12001 NoBankAccountForQr` (400, OQ11); `12002 EventNotClosedForQr` (400, event QR
  on an open event — §4.4); `12003 NoOutstandingDebtForQr` (400, event QR when nobody owes — OQ13).
  Extend `ErrorException.GetDefaultHttpStatus`. Resource-owned expense/event misses reuse
  `ExpenseNotFound` (6000) / `EventNotFound` (9000); bad input reuses `ValidationFailed` (1001). Trade-off:
  four new codes, one block per feature — consistent with 3xxx–9xxx.
- **(b)** Fewer codes (fold no-account/not-closed/no-debt into `ValidationFailed` 1001). Trade-off: less
  code, but clients can't distinguish "add a bank account" from "close the event first" from "nobody
  owes" — machine-distinct codes are worth it for these actionable states.

**OQ13 — Event QR when nobody owes (all balances ≥ 0).**
> ~~**OQ13**~~ → **Answered 2026-07-14 (option a):** all balances ≥ 0 (nobody owes) → reject
> `12003 NoOutstandingDebtForQr`.
- **(a) [recommended]** Reject with `NoOutstandingDebtForQr` (12003, HTTP 400,
  "Không có thành viên nào còn nợ trong đợt này."). Trade-off: an actionable message vs an empty/blank
  image; a closed event where everyone netted out (or was fully advanced) has no one to bill.
- **(b)** Return an empty/placeholder image (200). Trade-off: uniform "always an image" response, but a
  blank image to share is confusing.

**OQ14 — Tier gating (§3.11): wallet + QR are the Premium "mở rộng" group, but enforcement is M10.**
> ~~**OQ14**~~ → **Answered 2026-07-14 (option a):** NO tier gate at M9 (enforcement is M10) — leave a
> clean seam.
- **(a) [recommended]** **No tier gate at M9** — build the full wallet + QR as a clean, ungated feature
  and leave an obvious seam (a single service entry point per operation) so M10's tier mechanism can gate
  the whole group in one place. Trade-off: on M9 ship, a Free user can use wallet/QR until M10 lands the
  gate — acceptable because the tier-limit mechanism itself isn't finalised in any planning doc yet, and
  M8 already deferred its Premium-format gating the same way.
- **(b)** Add a Premium check now. Trade-off: pre-wires the Premium path, but there is no tier
  enforcement mechanism to hook into yet, so it would be a bespoke half-implementation to rip out/rework
  at M10.

**OQ15 — Endpoint surface: controller + routes for the wallet CRUD.**
> ~~**OQ15**~~ → **Answered 2026-07-14 (option a):** `BankAccountsController` → `api/v1/bank-accounts`
> (list/get/create/update/`PUT /{uuid}/default`/delete); QR sub-routes `GET /expenses/{uuid}/qr` +
> `GET /events/{uuid}/qr`.
- **(a) [recommended]** A new **`BankAccountsController`** → `api/v1/bank-accounts` for CRUD +
  set-default (REST-resource style, consistent with `categories`/`tags`); the two QR routes as
  sub-routes on the existing controllers: `GET api/v1/expenses/{uuid}/qr` and
  `GET api/v1/events/{uuid}/qr` (mirroring the M8 `…/export` routes). Trade-off: the "wallet" concept
  (CLAUDE.md's named area) is expressed as the `bank-accounts` resource collection; clean and
  consistent.
- **(b)** A `WalletController` with `api/v1/wallet/accounts…` sub-routes. Trade-off: matches CLAUDE.md's
  "Wallet" label literally, but needs explicit sub-route templates and diverges from the
  flat-resource CRUD idiom of categories/tags.

**OQ16 — Bank account uniqueness.**
> ~~**OQ16**~~ → **Answered 2026-07-14 (option a):** no bank-account uniqueness constraint.
- **(a) [recommended]** No uniqueness constraint — a user may add the same (bank, account number) more
  than once (they manage their own list; a duplicate is harmless). Trade-off: possible accidental dupes,
  but no false rejections and no app-level active-uniqueness machinery (which M4 needed only because a
  name collision is confusing; a duplicate account is not).
- **(b)** Enforce unique (bank_bin, account_number) per user. Trade-off: prevents dupes, but adds the
  app-level uniqueness check (no MariaDB filtered index) for little benefit.

**OQ17 — QR caching / persistence.**
> ~~**OQ17**~~ → **Answered 2026-07-14 (option a):** generate QR on demand, do NOT persist images.
- **(a) [recommended]** Generate on demand, **do not persist** QR strings or images (stateless; small
  CPU cost per request). Trade-off: regenerated each call, but generation is fast and avoids storing
  images / cache-invalidation on account or balance changes; matches quick-ordering's on-demand model.
- **(b)** Cache/persist generated images. Trade-off: saves recomputation, but adds storage +
  invalidation (an account edit or the chosen destination changes the QR) for a cheap operation.

## Assumptions

> **Confirmed by the user at the 2026-07-14 checkpoint** together with the 17 Open Questions — these are
> now decisions, not vetoable assumptions. Each is derived from the spec, prior decisions, and the shipped
> code.

- All wallet + QR endpoints are **guarded** (valid access token; anonymous → 401). No public/anonymous
  QR (public share links are §6 future).
- The **owner** is always the current authenticated user; the QR receive account is always one of the
  owner's own accounts.
- Money is `decimal`, non-negative, end to end (§4.3); the VietQR amount is the expense total (M5) or a
  member's `|Balance|` (M7), formatted per EMVCo (integer VND or `.00` per the VietQR amount rule —
  finalised in Step 5).
- The event QR reuses M7's balance verbatim (negative balance = owing); it does not recompute debt.
- QR generation is a **read** — it creates/updates no rows and writes no audit entries; it is allowed on
  closed events (the only QR the spec gates to closed is the event QR).
- **ONE** new EF migration adds the `bank_accounts` table only (no banks table — OQ4a).
- **TWO** new NuGet dependencies: `QRCoder` 1.6.0 (MIT) + `SkiaSharp` (MIT/BSD-3) with
  `SkiaSharp.NativeAssets.Linux.NoDependencies`, plus a bundled SIL-OFL font — all license-clean vs the
  AutoMapper-14 split-license trap.
- The image response reuses the M8 `FileContentResult` bypass unchanged — **no `AppController` edit**.

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/`. New services/repos use DiDecoration
> `[ScopedService]`. All user-facing strings + Swagger summaries are Vietnamese. Concrete names below are
> **synced to the user's 2026-07-14 checkpoint answers** — option (a) throughout, **except OQ3 = (b)
> SkiaSharp PNG composite**.

### Step 1 — Entity + EF migration

1. `Database/Entities/BankAccount.cs` (POCO, `partial`, `IEntity` **only** — NOT `IEntityDeletable`;
   hard-delete, OQ7a): `ulong Id`, `string Uuid`, `ulong UserId`, `required string BankBin`, `required string BankName`,
   `required string AccountNumber`, `required string AccountHolderName`, `bool IsDefault`,
   `DateTime CreatedAt`, `DateTime UpdatedAt`; nav `User User`.
2. `Database/Entities/Partials/BankAccount.cs`: ctor (`Uuid = Uuid.NewV7()`, `CreatedAt =
   AppDateTime.Now`); length consts (`BankBinMaxLength = 6`, `BankNameMaxLength = 100`,
   `AccountNumberMaxLength = 19`, `AccountHolderNameMaxLength = 100`); static
   `ConfigureModel(ModelBuilder)`:
   - Table `bank_accounts`; `id` PK; `uuid` (max 64, unique index); `user_id` (indexed);
     `bank_bin` (max 6); `bank_name` (max 100); `account_number` (max 19); `account_holder_name`
     (max 100); `is_default` (bool default `false`); `created_at`; `updated_at`
     (`ValueGeneratedOnAddOrUpdate` + `current_timestamp(6) ON UPDATE current_timestamp(6)`).
   - FK `HasOne(User).WithMany().HasForeignKey(UserId).OnDelete(Cascade)` (mirrors `Category`).
3. `Database/AppDbContext.cs`: add `DbSet<BankAccount> BankAccounts => Set<BankAccount>();` and invoke
   `BankAccount.ConfigureModel(modelBuilder)` in `OnModelCreating`. `AppDbContext.partial.cs` untouched
   (bank accounts are hard-deleted, no soft-delete filter).
4. **Migration:** `dotnet ef migrations add AddBankAccounts --project .\FairShareMonApi\FairShareMonApi.csproj`
   (offline via the pinned design-time factory). **Migration name: `AddBankAccounts`.** Review: one
   `bank_accounts` table, utf8mb4/unicode_ci, unique `uuid`, index `user_id`, FK cascade, bool default,
   `updated_at` default. Keep the model snapshot in sync; apply during the Test step.

### Step 2 — New dependencies (TWO — call out exactly)

Add to `FairShareMonApi.csproj` — these are the project's **first new runtime dependencies**, both
license-clean vs the AutoMapper-14 split-license trap:
1. **`<PackageReference Include="QRCoder" Version="1.6.0" />`** (MIT, OQ2a) — `PngByteQRCode` renders the
   single-expense QR to PNG bytes with **no `System.Drawing`** (cross-platform).
2. **`<PackageReference Include="SkiaSharp" Version="…" />`** (MIT binding / BSD-3 native, OQ3b) plus
   **`<PackageReference Include="SkiaSharp.NativeAssets.Linux.NoDependencies" Version="…" />`** (headless
   Docker/Linux rendering) — draws the **event composite PNG** (each member's QR + name + amount label on
   one `SKCanvas`). Pin a stable SkiaSharp version compatible with `net8.0` at implementation time.
3. **A bundled Vietnamese-capable font** for the SkiaSharp labels: commit a small **SIL-OFL** font (e.g.
   **Be Vietnam Pro** or a **Noto Sans** subset) as a project `Content`/`EmbeddedResource` and load it via
   SkiaSharp (`SKTypeface.FromStream`/`FromFile`). **Record the exact font + its OFL license in this
   section when committed.** *Fallback:* if committing a font binary is blocked in the environment, the
   implementer may use a system typeface via `SKFontManager.Default.MatchCharacter`/`MatchFamily` for dev
   and **MUST flag the portability gap** (a headless container may lack Vietnamese glyphs) in the Progress
   Log.
- QRCoder's `SvgQRCode` is **not** used (the SVG-composite path was OQ3a, not chosen).

### Step 3 — Error codes + messages

Append to `Constants/ErrorCodes.cs` (never renumber). **12xxx block = Wallet/QR** (OQ12a):

| Code | Name | HTTP | Message (Vietnamese) |
|---|---|---|---|
| `12000` | `BankAccountNotFound` | 404 | "Không tìm thấy tài khoản ngân hàng." |
| `12001` | `NoBankAccountForQr` | 400 | "Chưa có tài khoản ngân hàng để tạo mã QR." |
| `12002` | `EventNotClosedForQr` | 400 | "Chỉ có thể tạo mã QR cho đợt đã chốt." |
| `12003` | `NoOutstandingDebtForQr` | 400 | "Không có thành viên nào còn nợ trong đợt này." |

- Extend `ErrorException.GetDefaultHttpStatus`: `12000`→404, `12001`/`12002`/`12003`→400.
- Success messages: "Thêm tài khoản ngân hàng thành công." / "Cập nhật tài khoản ngân hàng thành công."
  / "Đã xóa tài khoản ngân hàng." / "Đã đặt tài khoản ngân hàng mặc định."

### Step 4 — Repository (default-swap, resource-owned)

`Repositories/BankAccountRepository.cs` — `IBankAccountRepository : IBaseRepository` + sealed impl
(`[ScopedService(typeof(IBankAccountRepository))]`, extends `BaseRepository`), mirroring
`CategoryRepository`:
- `ListByUserAsync(userUuid, ct)` — scoped; sort default-first then `created_at`.
- `GetByUuidAsync(userUuid, uuid, ct)` — resource-owned; null on miss.
- `GetDefaultAsync(userUuid, ct)` — the user's default account (null if none).
- `CreateAsync(userUuid, bankBin, bankName, accountNumber, holderName, ct)` — resolve user id (unknown →
  signal); insert; **auto-set default when it is the user's first account** (OQ6a), inside one
  `ExecuteTransactionAsync`.
- `UpdateAsync(userUuid, uuid, …fields, ct)` — tracked update scoped to user; **never touches
  `is_default`**; miss → signal.
- `DeleteAsync(userUuid, uuid, ct)` — hard-delete (OQ7a) in one transaction; **if the deleted row was
  the default and other accounts remain, promote the most-recently-created remaining account to default**
  (OQ6a); miss → signal.
- `SetDefaultAsync(userUuid, uuid, ct)` — **atomic swap** in one `ExecuteTransactionAsync` (load target
  owned → miss/`NoCommit()` if absent; clear the current default; set the target) — the M4
  `CategoryRepository.SetDefaultAsync` shape.
- `ResolveUserIdAsync` — reuse the private helper pattern.

### Step 5 — VietQR payload builder + QR image service

`Services/Api/Wallet/VietQrPayloadBuilder.cs` (`[ScopedService]` or a pure static helper) — OQ1a:
- `string Build(string bankBin, string accountNumber, decimal amount, string? addInfo)` — assemble the
  EMVCo TLV: `00`=`01` (format), `01`=`12` (dynamic, has amount), `38`=NAPAS merchant-account block
  (GUID `A000000727` + acquirer BIN + consumer account + service code `QRIBFTTA`), `53`=`704` (VND),
  `54`=amount (VND amount formatting finalised here — integer VND, no decimals, unless a share carries
  cents), `58`=`VN`, `62`=additional-data (memo in sub-tag `08`, folded/truncated per OQ9a), then
  `63`=**CRC-16/CCITT-FALSE** (poly `0x1021`, init `0xFFFF`) computed over the whole string including
  the `6304` tag prefix, emitted as 4 upper-hex chars.
- `ushort Crc16Ccitt(ReadOnlySpan<char> data)` — the CRC routine (unit-tested against a known VietQR
  sample).

`Services/Api/Wallet/QrImageService.cs` — `IQrImageService` + sealed impl (`[ScopedService]`):
- `byte[] RenderSingle(string payload, ...)` → QRCoder `PngByteQRCode` PNG (the single-expense QR, and
  each member's QR bitmap used by the composite) — OQ2a.
- `byte[] RenderComposite(IReadOnlyList<(string label, string payload)> items, ...)` → **OQ3b (SkiaSharp
  PNG):** draw a **single raster PNG** on an `SKCanvas` — vertically stack each member's QR (decode the
  `PngByteQRCode` bytes into an `SKBitmap`, or re-render onto the canvas) with an `SKTextBlob`/`DrawText`
  label of the member name + formatted amount below/beside each QR; encode the surface to PNG
  (`SKImage.Encode(SKEncodedImageFormat.Png)`). Load the bundled **SIL-OFL Vietnamese font** via
  `SKTypeface.FromStream`/`FromFile` (fallback `SKFontManager.Default.MatchCharacter` — flag portability
  per Step 2). Content-Type `image/png`.

`Services/Api/Wallet/WalletQrService.cs` — `IWalletQrService` + sealed impl (`[ScopedService]`, primary
ctor injecting `IBankAccountRepository`, `IExpensesService`, `IStatsService`, `IVietQrPayloadBuilder`,
`IQrImageService`):
- `Task<QrImageResult> GenerateExpenseQrAsync(userUuid, expenseUuid, bankAccountUuid?, format?, ct)`:
  1. resolve the destination account — the override (resource-owned; miss → 12000) or the default; none
     → `NoBankAccountForQr` (12001, OQ11).
  2. `expense = expensesService.GetAsync(userUuid, expenseUuid, ct)` (miss → 6000).
  3. `payload = builder.Build(acct.BankBin, acct.AccountNumber, expense.Total, memo(expense.Name))`.
  4. `format == "payload"` → return the raw string (JSON); else `RenderSingle(payload)` → PNG
     `QrImageResult { Content, ContentType, FileName }` (OQ10a).
- `Task<QrImageResult> GenerateEventQrAsync(userUuid, eventUuid, bankAccountUuid?, ct)`:
  1. resolve the destination account (as above; 12000/12001).
  2. `balance = statsService.GetEventBalanceAsync(userUuid, eventUuid, ct)` (miss → 9000); **if
     `!balance.IsClosed` → `EventNotClosedForQr` (12002)** (§4.4/§5).
  3. owing = `balance.Rows.Where(r => r.Balance < 0)`; **empty → `NoOutstandingDebtForQr` (12003)**
     (OQ13a).
  4. for each owing row build a payload (amount = `-r.Balance`, memo = `"{event} - {member}"`);
     `RenderComposite([(label = "{member}: {amount}", payload)])` → the one image (OQ10a).
- `QrImageResult` = `{ byte[] Content; string ContentType; string FileName; }` (the M8 `ExportedFile`
  shape; the controller turns it into `File(...)`). The payload-only expense case returns the string in
  `ApiResult<string>` instead.

### Step 6 — DTOs + validators + mapping

`Models/Wallet/`:
- `CreateBankAccountRequest { string BankBin; string BankName; string AccountNumber; string AccountHolderName; }`
- `UpdateBankAccountRequest { … same four … }`
- `BankAccountResponse { string Uuid; string BankBin; string BankName; string AccountNumber; string AccountHolderName; bool IsDefault; DateTime CreatedAt; }`

`Validators/Wallet/` (auto-registered; camelCase field keys) — OQ5a:
- `CreateBankAccountRequestValidator` / `UpdateBankAccountRequestValidator`: `BankBin` required + regex
  `^\d{6}$` ("Mã ngân hàng (BIN) phải gồm đúng 6 chữ số."); `BankName` required max 100; `AccountNumber`
  required + regex `^\d{6,19}$` ("Số tài khoản không hợp lệ."); `AccountHolderName` required max 100.

`Mappings/BankAccountProfile.cs` — `CreateMap<BankAccount, BankAccountResponse>()`.

### Step 7 — Services (bank accounts CRUD)

`Services/Api/Wallet/BankAccountsService.cs` — `IBankAccountsService` + sealed impl (`[ScopedService]`,
primary ctor injecting `IBankAccountRepository`, `IMapper`, the two validators):
- `ListAsync` / `GetAsync` (miss → `BankAccountNotFound` 12000).
- `CreateAsync` — validate → create (auto-default first, OQ6a) → map.
- `UpdateAsync` — validate → update (never `is_default`); miss → 12000.
- `SetDefaultAsync` — atomic swap; miss → 12000.
- `DeleteAsync` — delete + promote-another-if-default (OQ6a); miss → 12000.

### Step 8 — Controllers

`Controllers/BankAccountsController.cs` (new; derives from `AppController`; `userUuid =
AuthenticatedUser.Id`; Vietnamese Swagger) — OQ15a:

| Verb + Route | Request → Response | Notes |
|---|---|---|
| `GET api/v1/bank-accounts` | → `ApiResult<IReadOnlyList<BankAccountResponse>>` | default-first |
| `GET api/v1/bank-accounts/{uuid}` | route → `ApiResult<BankAccountResponse>` | miss → 12000 |
| `POST api/v1/bank-accounts` | `CreateBankAccountRequest` → `ApiResult<BankAccountResponse>` | first → auto default |
| `PUT api/v1/bank-accounts/{uuid}` | `UpdateBankAccountRequest` → `ApiResult<BankAccountResponse>` | no `isDefault`; miss → 12000 |
| `PUT api/v1/bank-accounts/{uuid}/default` | route → `ApiResult` success message | atomic swap; miss → 12000 |
| `DELETE api/v1/bank-accounts/{uuid}` | route → `ApiResult` success message | promote-if-default; miss → 12000 |

**[M5-MOD]** `Controllers/ExpensesController.cs` — add (inject `IWalletQrService`):

| Verb + Route | Request → Response | Notes |
|---|---|---|
| `GET api/v1/expenses/{uuid}/qr` | `[FromRoute] uuid`, `[FromQuery] string? bankAccountUuid`, `[FromQuery] string? format` → `FileContentResult` (`image/png`) OR `ApiResult<string>` (`format=payload`) | miss → 6000; no account → 12001; unwrapped image via M8 bypass |

**[M6-MOD]** `Controllers/EventsController.cs` — add (inject `IWalletQrService`):

| Verb + Route | Request → Response | Notes |
|---|---|---|
| `GET api/v1/events/{uuid}/qr` | `[FromRoute] uuid`, `[FromQuery] string? bankAccountUuid` → `FileContentResult` (`image/png`, SkiaSharp composite — OQ3b) | miss → 9000; open → 12002; nobody owes → 12003; no account → 12001 |

Both QR actions: Vietnamese `[SwaggerOperation]`; `[SwaggerResponse(200, …, typeof(FileContentResult))]`
(+ `[Produces(...)]`), `400`/`404`/`401` as `ApiResult`. Bodies stay thin: call `walletQrService`,
return `File(result.Content, result.ContentType, result.FileName)` (unwrapped — the M8 bypass) or the
`ApiResult<string>` payload.

### Step 9 — Tests (owned by the test-engineer; definitive list)

Reuse the shipped harness: `[Collection("AuthIntegration")]`; DB/endpoint tests use the
`ExpenseDbTestBase` / `ExpenseApiTestBase` (or `AuthDbTestBase`/`AuthApiTestBase`) families, a unique
lowercase username prefix per class, dispose-time cascade cleanup; all DB-dependent tests
`[SkippableFact]` (skip when MariaDB unreachable), never EF InMemory.

**Unit (no DB):**
- `VietQrPayloadBuilderTests` — **the correctness centre-piece:** the TLV field ordering/lengths match
  EMVCo; the CRC-16/CCITT-FALSE matches a **known VietQR reference string** (assert the exact `63xx`
  CRC bytes for a fixed BIN/account/amount/memo sample); amount formatting (VND) is correct; memo
  folding/truncation (OQ9a) applied; a zero-amount / no-memo variant still produces a valid CRC.
- `Crc16` routine — matches published CRC-16/CCITT-FALSE test vectors (`"123456789"` → `0x29B1`).
- `BankAccount` validators — BIN non-6-digit rejected; account number out of `^\d{6,19}$` rejected;
  required-field + max-length messages; camelCase field keys.
- `BankAccountsService` (fake repo) — get/update/set-default/delete miss → 12000; create maps; update
  never sets `isDefault`.
- `WalletQrService` (fakes) — expense: no account → 12001, override miss → 12000, else builds payload
  from `expense.Total`; `format=payload` returns the string; event: open event → 12002, nobody owes →
  12003, else one composite over the negative-balance rows with amount = `|Balance|`.
- `QrImageService` — `RenderSingle` returns non-empty bytes starting with the **PNG magic**
  (`89 50 4E 47`); `RenderComposite` returns a **PNG** (PNG magic, OQ3b) that is taller/larger for more
  members and whose pixels/label text include each member's name + amount (assert non-empty distinct
  output per member count; the label text is exercised via the endpoint/integration path).

**Integration (real MariaDB — `BankAccountRepositoryTests`):**
- Create sets uuid/UTC/user_id; **first account is auto-default** (OQ6a); resource-owned — another
  user's account invisible to get/list/update/delete/set-default.
- **Default invariant:** set-default clears exactly the previous default and sets the target atomically
  (never zero-with-accounts, never two); update never changes `is_default`.
- **Delete-of-default promotes another** remaining account (OQ6a); deleting the **last** account leaves
  the wallet empty (no default); a non-default delete leaves the default intact.
- Hard-delete removes the row entirely (OQ7a — `bank_accounts` has no `is_deleted`).

**Integration (real MariaDB — `WalletQrServiceTests` over seeded data):**
- Expense QR: seed an expense (several shares) → payload amount = the expense total; PNG bytes returned;
  `format=payload` returns the VietQR string containing the seeded BIN/account.
- Event QR: seed the §3.7 scenario (Bình +300k, Cường −500k) on a **closed** event → the composite has
  exactly one QR per **negative-balance** member (Cường), amount = 500k, labelled with the member name;
  positive/zero members excluded.
- **Closed-only gate:** the same event while **open** → `EventNotClosedForQr` (12002).
- **Nobody owes:** a closed event where all balances ≥ 0 → `NoOutstandingDebtForQr` (12003).
- **No account:** a user with no bank account requesting either QR → `NoBankAccountForQr` (12001).
- Resource-owned: another user's expense/event → 6000 / 9000 (never leaked).

**Endpoint (WebApplicationFactory — `BankAccountsEndpointTests`, `WalletQrEndpointTests`):**
- Full CRUD + set-default over HTTP wrapped in `ApiResult`; resource-owned 404 (12000, never 403);
  invalid payload → 400 with `error.fields`; anonymous → 401.
- `GET /expenses/{uuid}/qr` → **HTTP 200, `Content-Type: image/png`**, body is **raw image bytes (PNG
  magic), NOT an `ApiResult` envelope** — proves the M8 bypass; `?format=payload` → 200 JSON
  `ApiResult<string>`; no account → 400 (12001); foreign expense → 404 (6000); anonymous → 401.
- `GET /events/{uuid}/qr` on a closed event → **HTTP 200** with the composite **`image/png`** (PNG
  magic, one QR per still-owing member) + `Content-Disposition: attachment`; open event → 400 (12002);
  nobody owes → 400 (12003); foreign event → 404 (9000); anonymous → 401.

### Step 10 — Wrap-up

- `dotnet build` clean; `dotnet test` green (DB tests skip only when MariaDB unreachable); live smoke:
  add bank accounts → default swap → generate an expense QR (scan resolves in a bank app) → close an
  event → generate the event composite. `dotnet ef database update` per protocol.
- Update this doc's Progress Log + Final Outcome; **record the exact new dependencies (QRCoder version +
  SkiaSharp version + the bundled font name + all three licenses)** added.
- Note the QR seam (`VietQrPayloadBuilder` + `IQrImageService`) for future features (reminders §6,
  per-member settled QR §6).

## Impact Analysis

**APIs:**
- **New controller:** `BankAccountsController` — 6 routes under `api/v1/bank-accounts` (list, get,
  create, update, set-default, delete).
- **New routes on existing controllers:** `GET api/v1/expenses/{uuid}/qr` (`ExpensesController`),
  `GET api/v1/events/{uuid}/qr` (`EventsController`) — both return an image (unwrapped) on success, the
  JSON envelope on failure. No change to existing endpoints.

**Database:**
- **ONE new migration `AddBankAccounts`** — table `bank_accounts` **only** (bank_bin, bank_name,
  account_number, account_holder_name, is_default, FK cascade to `users`, unique `uuid`, index `user_id`;
  **no `is_deleted`** — hard-delete per OQ7a). No banks reference table (OQ4a). No data migration.

**Infrastructure:**
- **⚠ FIRST NEW RUNTIME DEPENDENCIES — TWO packages + a bundled font, all license-clean vs the
  AutoMapper-14 split-license trap:**
  1. **`QRCoder` 1.6.0 (MIT)** — `System.Drawing`-free `PngByteQRCode` for the single-expense QR (OQ2a).
  2. **`SkiaSharp` (MIT binding / BSD-3 native) + `SkiaSharp.NativeAssets.Linux.NoDependencies`** — the
     event composite PNG, headless-Docker-safe (OQ3b).
  3. **A bundled SIL-OFL Vietnamese font** (e.g. Be Vietnam Pro / Noto Sans subset) for the composite
     labels — exact font + license recorded at implementation (Step 2); dev fallback = a system typeface
     via SkiaSharp, with the portability gap flagged.
  - `SixLabors.ImageSharp` (OQ3c) was rejected as a Split-License trap. No Redis, no background workers.

**Services:**
- **New:** `IBankAccountsService`/`BankAccountsService`, `IBankAccountRepository`/`BankAccountRepository`,
  `IVietQrPayloadBuilder`/`VietQrPayloadBuilder`, `IQrImageService`/`QrImageService`,
  `IWalletQrService`/`WalletQrService` (`Services/Api/Wallet/`), `Mappings/BankAccountProfile.cs`,
  `Models/Wallet/*`, `Validators/Wallet/*`.
- **Modified:** `ExpensesController` + `EventsController` (add a QR route + `IWalletQrService`);
  `ErrorCodes.cs` + `ErrorException.GetDefaultHttpStatus` (12xxx); `AppDbContext` (`DbSet` +
  `ConfigureModel`); `FairShareMonApi.csproj` (new package). `AppController`, `ApiResult`, middleware,
  `ResponseWrappedAttribute` untouched.

**UI:** none (API only) — the client renders a bank picker (BIN ↔ name) and displays/shares the image.

**Documentation:** this planning doc; Vietnamese Swagger on the new endpoints; `ErrorCodes` XML docs
(12xxx). CLAUDE.md already lists a "Wallet (bank accounts + bank-transfer QR generation)" controller
area — no edit needed.

## Decision Log

> **Resolved at the 2026-07-14 user checkpoint — 16 of 17 Open Questions accepted at the recommended
> option (a); OQ3 the user chose option (b).** One numbered point per OQ (binding decision + one-line
> reason). Full options/trade-offs for each are preserved inline under the matching OQ above.

1. **OQ1 — Hand-rolled VietQR payload (a):** a `VietQrPayloadBuilder` builds the EMVCo TLV (00/01, 38 =
   NAPAS `A000000727` + BIN + account + `QRIBFTTA`, 53 = 704, 54 = amount, 62-08 = memo, 63 =
   CRC-16/CCITT-FALSE); no payload library. *Reason:* precisely specified + fully unit-testable, keeps the
   dependency surface to the image libs only (the M8 hand-rolled-CSV ethos).
2. **OQ2 — QRCoder 1.6.0 (a):** add **`QRCoder` 1.6.0 (MIT)**, `PngByteQRCode` (no `System.Drawing`) —
   the project's first new runtime dependency. *Reason:* MIT (no split-license trap), sibling-proven,
   cross-platform.
3. **OQ3 — SkiaSharp PNG composite (b — user override):** the event composite is a **raster PNG** via
   **`SkiaSharp`** (MIT/BSD-3) + `SkiaSharp.NativeAssets.Linux.NoDependencies` + a bundled SIL-OFL
   Vietnamese font — a **second** new dependency. *Reason:* a PNG previews inline in every group-chat
   client (§3.10 "gửi vào nhóm chat"), which the no-dep SVG could not guarantee; still license-clean.
4. **OQ4 — BIN on the account, no banks table (a).** *Reason:* the client renders the bank picker; avoids
   a data-heavy NAPAS reference table to maintain.
5. **OQ5 — Fields/validation (a):** `bank_bin ^\d{6}$`, `bank_name` req ≤100, `account_number ^\d{6,19}$`,
   `account_holder_name` req ≤100, `is_default`. *Reason:* catches typos while covering the vast majority
   of (numeric) Vietnamese accounts.
6. **OQ6 — Default invariant (a):** first account auto-default; dedicated `PUT /{uuid}/default` atomic
   swap; deleting the default promotes another; deleting the last leaves an empty wallet. *Reason:* mirrors
   the M4 default-category swap, adapted to a wallet that may legitimately be empty.
7. **OQ7 — Hard-delete (a):** bank accounts are hard-deleted (not `IEntityDeletable`). *Reason:* no
   historical linkage (QR is ephemeral, never persisted) — §4.7 does not apply.
8. **OQ8 — Default + `?bankAccountUuid` override (a).** *Reason:* matches §3.10 "có thể chọn tài khoản
   khác lúc tạo".
9. **OQ9 — Memo (a):** expense memo = expense name; event per-member memo = `"{event} - {member}"`;
   ASCII-folded + truncated ~25 chars. *Reason:* max bank-app compatibility; the composite label keeps
   full Vietnamese.
10. **OQ10 — PNG + payload option (a):** expense QR = PNG (M8 bypass) with `?format=payload` → raw VietQR
    string as JSON; event QR = composite PNG file. *Reason:* the system produces the image (§3.10) while
    still offering the raw payload for clients that render their own.
11. **OQ11 — No account → 12001 (a).** *Reason:* an actionable error beats a broken/empty QR.
12. **OQ12 — 12xxx block (a):** `12000 BankAccountNotFound` (404), `12001 NoBankAccountForQr`,
    `12002 EventNotClosedForQr`, `12003 NoOutstandingDebtForQr` (400); extend `GetDefaultHttpStatus`.
    *Reason:* one block per feature; machine-distinct actionable states.
13. **OQ13 — Nobody owes → 12003 (a).** *Reason:* a blank image to share is confusing; a clear message is
    actionable.
14. **OQ14 — No tier gate at M9 (a).** *Reason:* the tier-enforcement mechanism is M10 and not yet
    finalised; leave a clean seam (M8 deferred its gating the same way).
15. **OQ15 — `BankAccountsController` → `api/v1/bank-accounts` + QR sub-routes (a).** *Reason:*
    REST-resource CRUD consistent with categories/tags; QR mirrors the M8 `…/export` sub-routes.
16. **OQ16 — No uniqueness constraint (a).** *Reason:* a duplicate account is harmless; avoids app-level
    active-uniqueness machinery.
17. **OQ17 — On-demand, no persistence (a).** *Reason:* generation is cheap and avoids storage +
    invalidation on account/balance changes.

**Inherited decisions (locked upstream — NOT reopened):** QR expense = manual single QR, amount = total;
QR event = one QR per still-owing member after close, one image (§5). Total = derived `SUM(shares)` (M5);
balance per-event only, negative = owing (§3.7); money `decimal(18,2)`, non-negative, no float (§4.3);
resource-owned 404-never-403, `ExpenseNotFound` 6000 / `EventNotFound` 9000 (§4.1); closed-event
write-freeze, QR read allowed on closed events (§4.4); the M8 `FileContentResult` bypass of
`[ResponseWrapped]` with `AppController` LOCKED; domain terms wallet/bank account/expense/event/settled
(§5).

## Progress Log

### 2026-07-14

- Started planning M9 (Wallet + QR).
- Required reading completed: `The-ideal.md` §3.10 in full + §5 QR lock + §3.5/§3.7/§3.11 + §4.1/§4.3/
  §4.4/§4.9; `CLAUDE.md` (minimal-dependency + license sensitivity, `FileContentResult` bypass,
  `AppController` LOCKED); `.claude/rules/rule.md` (template) + `.agents/rules/rules.md`.
- Read prior planning docs: `debt-balance-and-stats.md` (M7 — reuse `GetEventBalanceAsync`, negative =
  owing), `export-csv.md` (M8 — the verified `FileContentResult` bypass + file-response idiom),
  `categories-and-tags.md`/`members.md` (default-swap invariant, entity/repo/service/validator/
  controller layout, test harness).
- Grounded the plan in the live code: `Category` entity + partial, `CategoryRepository.SetDefaultAsync`
  (atomic default swap blueprint), `StatsService`/`EventBalanceResponse`/`MemberBalanceRow`,
  `ExpensesController.ExportAsync` (`File(...)` unwrapped) + `ExpenseResponse.Total`, `ErrorCodes`
  (next free block 12xxx), `FairShareMonApi.csproj` (no runtime dependency yet).
- **Investigated the sibling `quick-ordering` for the dependency signal:** `QrCodeExtensions.cs` uses
  **QRCoder 1.4.3 (MIT)** `PngByteQRCode.GetGraphic(pixelsPerModule)` — PNG, no `System.Drawing`;
  it encodes an ordering URL (no VietQR payload, no composite); `VnPayPaymentProvider` is a redirect-URL
  gateway (`VNPay.NetCore`), unrelated to QR imaging. Recorded QRCoder as the recommended, license-safe
  renderer.
- Drafted all template sections; 17 Open Questions raised with options + trade-offs + a recommended
  option (a) each — the central ones being OQ1 (hand-rolled VietQR payload), OQ2 (QRCoder MIT), and OQ3
  (event composite: SVG-only vs SkiaSharp vs the ImageSharp split-license trap).
- **User checkpoint (2026-07-14):** all 17 OQs resolved — **16 at option (a); OQ3 at option (b)**
  (SkiaSharp raster PNG composite over the no-dep SVG, for chat-shareability). Confirmed: ONE migration
  `AddBankAccounts` (bank_accounts only, no banks table, no `is_deleted`); **TWO** new NuGet dependencies
  (`QRCoder` 1.6.0 MIT + `SkiaSharp` MIT/BSD-3 with `…NativeAssets.Linux.NoDependencies`) plus a bundled
  SIL-OFL font; event QR is closed-only (`12002`) and reuses `IStatsService.GetEventBalanceAsync` for the
  still-owing (negative-balance) members — not recomputed; image responses reuse the M8
  `FileContentResult` bypass. Annotated every OQ inline, filled the Decision Log, marked the plan
  **unblocked**, and synced the Implementation Plan / dependency section / error-code + endpoint tables /
  test list to the answers. **Implementation can start.**

### 2026-07-14 (implementation)

**Built the full M9 Wallet + QR feature per the approved plan (option (a) throughout, OQ3 = (b)
SkiaSharp). `dotnet build` clean; `dotnet test` 761/761 (0 skipped - MariaDB reachable); live HTTP
smoke all green.**

- **Dependencies added** to `FairShareMonApi.csproj` (first-ever runtime deps; `dotnet restore` clean,
  all MIT/BSD-3 - no split-license trap):
  - `QRCoder` **1.6.0** (MIT) - `PngByteQRCode` for the single-expense QR PNG (no `System.Drawing`;
    uses the net6.0 asset for net8.0).
  - `SkiaSharp` **2.88.9** (MIT binding / BSD-3 native) + `SkiaSharp.NativeAssets.Linux.NoDependencies`
    **2.88.9** - event composite PNG on an `SKCanvas`.
  - **Bundled font:** `Assets/Fonts/BeVietnamPro-Regular.ttf` (**Be Vietnam Pro**, SIL Open Font
    License 1.1 - `Assets/Fonts/OFL.txt`), sourced from the Google Fonts `ofl/bevietnampro` repo and
    committed as an `<EmbeddedResource>`. Loaded via `SKTypeface.FromStream` from the manifest resource;
    a system fallback (`SKFontManager.Default.MatchCharacter('ế')`) remains as a safety net. **No
    portability gap** - the font is embedded in the assembly, so headless Linux/Docker renders
    Vietnamese diacritics without any system font. Verified visually: the composite renders
    "Nguyễn Văn A", "Trần Thị Bích Đào" (wraps to 2 lines), "Lê Cường" with full diacritics.
- **Entity + migration:** `Database/Entities/BankAccount.cs` (+`Partials/BankAccount.cs`), `IEntity`
  only (hard-delete, OQ7); `AppDbContext` `DbSet` + `ConfigureModel` wired. Migration **`AddBankAccounts`**
  (`20260714115422_AddBankAccounts`) authored offline via the design-time factory - one `bank_accounts`
  table (unique `uuid`, index `user_id`, FK cascade to `users`, `is_default` default false,
  `updated_at` computed default, **no `is_deleted`**); reviewed and **applied to the dev DB**
  (`database update` succeeded).
- **Error codes (12xxx):** `12000 BankAccountNotFound` (404), `12001 NoBankAccountForQr`,
  `12002 EventNotClosedForQr`, `12003 NoOutstandingDebtForQr` (400) added to `ErrorCodes` +
  `ErrorException.GetDefaultHttpStatus`.
- **Repository:** `Repositories/BankAccountRepository.cs` - resource-owned; first-account auto-default;
  atomic `SetDefaultAsync` swap; `DeleteAsync` hard-delete that promotes the most-recently-created
  remaining account when the default is removed; all invariant mutations inside one
  `ExecuteTransactionAsync`.
- **VietQR (OQ1):** `Services/Api/Wallet/VietQrPayloadBuilder.cs` - EMVCo TLV
  (`00`=01, `01`=12 dynamic / 11 static, `38`=NAPAS `A000000727`+BIN+account+`QRIBFTTA`, `53`=704,
  `54`=amount `0.##`, `58`=VN, `62`-`08`=ASCII-folded memo truncated to 25 chars, `63`=CRC-16/CCITT-FALSE
  poly 0x1021 init 0xFFFF over the whole string incl. `6304`). **Verification:** CRC test vector
  `"123456789"` -> `0x29B1`; a generated payload's emitted CRC matched an **independently-implemented**
  CRC (bash + C#); the live `?format=payload` response
  `00020101021238540010A00000072701240006970436011012345678900208QRIBFTTA530370454065000005802VN62120808Com trua63047E7E`
  decoded field-by-field to the exact expected TLV and its CRC `7E7E` re-validated. Memo
  "Ăn uống tại Đà Nẵng nhé bạn ơi" folded to "An uong tai Da Nang nhe b" (25, pure ASCII, đ→d).
- **QR images (OQ2/OQ3):** `Services/Api/Wallet/QrImageService.cs` - `RenderSingle` (QRCoder PNG,
  magic `89 50 4E 47` confirmed), `RenderComposite` (SkiaSharp: vertical stack of each member's QR +
  wrapped centred label, embedded font; PNG grew 5.7 KB → 18.6 KB from 1 → 3 members).
- **Orchestration:** `Services/Api/Wallet/WalletQrService.cs` (`IWalletQrService`) - resolves the
  destination (override→12000 / default / none→12001), reuses `IExpensesService.GetAsync` (total) and
  `IStatsService.GetEventBalanceAsync` (closed gate→12002, negative-balance debtors, empty→12003), never
  recomputes debt; the single seam for a future tier gate (OQ14). DTOs (`Models/Wallet/*`), validators
  (`Validators/Wallet/*`, OQ5 regexes), `Mappings/BankAccountProfile.cs`, `BankAccountsService`.
- **Controllers:** new `BankAccountsController` at **`api/v1/bank-accounts`** (explicit
  `[Route(".../bank-accounts")]` on the derived controller because the `[controller]` token would render
  the multi-word name as `BankAccounts`; verified `bank-accounts`→200 while `BankAccounts`/`bankaccounts`
  →404, i.e. the derived route cleanly overrides the inherited one - **`AppController` untouched**).
  Added `GET /expenses/{uuid}/qr` and `GET /events/{uuid}/qr`; both return `File(...)` (the M8
  `FileContentResult` bypass - `image/png`, `Content-Disposition: attachment`), the expense route also
  returning `ApiResult<string>` for `?format=payload`.
- **Live smoke (all green):** create acct1 (auto-default) + acct2; set-default swaps to acct2;
  delete-default acct2 promotes acct1; expense QR → 200 `image/png` (PNG magic) and `?format=payload`
  → valid VietQR string (CRC 7E7E validated); closed-event QR → composite `image/png` with exactly
  one QR for the sole debtor labelled "Cuong: 500.000đ"; open-event → 12002; nobody-owes → 12003;
  no-account → 12001; another user's expense → 6000 and event → 9000 (with the requester holding an
  account, since account resolution precedes the resource check per Step 5); another user's bank
  account (direct GET and `?bankAccountUuid` override) → 12000; anonymous → 401. Smoke bank-account rows
  hard-deleted afterward (both wallets empty); closed events + their expenses are immutable by design
  (§4.4) and were left as disposable dev rows.
- **No deviations from the plan; no new Open Questions.** One implementation detail worth recording:
  the kebab-case route required an explicit `[Route]` on the derived controller (the base `[controller]`
  token cannot produce a hyphen), resolved without touching the locked `AppController`.

### 2026-07-14 (tests)

**Authored + ran the full M9 test suite (test project only; NO production code touched). `dotnet test`
green on the FULL solution: 878 passed / 0 failed / 0 skipped (MariaDB + Redis reachable), up from the
761 baseline — 117 new M9 tests, all additive; the 761 pre-existing tests still pass unchanged.
Determinism confirmed (two consecutive full runs both 878/878, identical). DB verified clean afterward
(0 test-prefixed users, 0 bank_accounts rows).**

New test files under `FairShareMonApi.Tests/` (all follow the shipped harness: `[Collection("AuthIntegration")]`,
`ExpenseApiTestBase`/`AuthDbTestBase`, unique per-class username prefix, dispose-time cleanup, `[SkippableFact]`):

- **Unit (no DB):**
  - `VietQrPayloadBuilderTests` (14) — the correctness centre-piece. CRC-16/CCITT-FALSE against the
    canonical vector `"123456789"` → `0x29B1`; production CRC cross-checked against an INDEPENDENT
    table-driven CRC written in the test (never the production routine checking itself); a full payload
    decoded field-by-field to the expected EMVCo TLV (00=01; 01=12 dynamic / 11 static; 38 = NAPAS
    `A000000727` + BIN + account + `QRIBFTTA`; 53=704; 54=amount; 58=VN; 62-08=memo; 63=CRC); the
    trailing `6304` shown to be included in the CRC input and the emitted CRC re-validated independently;
    a tampered payload fails the independent CRC; amount formatting (no grouping, up to 2 decimals);
    memo ASCII-folds Vietnamese diacritics + đ→d + truncates to 25 chars; static (zero-amount) + no-memo
    variants still valid.
  - `BankAccountValidatorsTests` (28, Create + Update validator classes) — Create + Update validators: `bankBin ^\d{6}$`,
    `accountNumber ^\d{6,19}$`, required `bankName`/`accountHolderName` ≤100, pinned Vietnamese messages.
  - `QrImageServiceTests` (5) — `RenderSingle` PNG magic `89 50 4E 47`; `RenderComposite` PNG magic,
    Vietnamese labels render without throwing (embedded Be Vietnam Pro), taller/larger for more members,
    empty items → throw.
  - `BankAccountsServiceTests` (13, fake repo + real mapper/validators) — get/update/set-default/delete
    miss → 12000; create trims + maps + first-auto-default; update never touches `is_default`; invalid
    input → validation exception before the repo; unknown user → 12000.
  - `WalletQrServiceTests` (11, fakes + real `VietQrPayloadBuilder` + capturing image fake) — expense: no
    account → 12001, override miss → 12000, owned override used, amount = `expense.Total`,
    `format=payload` returns the raw string without rendering, expense miss → 6000; event: open → 12002,
    nobody owes → 12003, closed-with-debtors composes exactly one entry per negative-balance member with
    amount = `|Balance|` and the member name in the label (asserted on the captured composite items, not
    pixels), no account → 12001, event miss → 9000.
- **Integration (real MariaDB, skippable) — `BankAccountRepositoryTests` (18):** create persists
  uuid/UTC/user_id; first account auto-default; second not default (exactly one default); unknown user →
  null; duplicate (bank, account) allowed (OQ16 no uniqueness); resource-owned scoping (foreign account
  invisible on get/list/update/delete/set-default); `GetDefaultAsync` null when empty; list default-first
  scoped to caller; update persists fields but never `is_default`; set-default atomic swap (exactly one
  default, never zero/two); delete-of-default promotes most-recent remaining; non-default delete leaves
  default intact; delete-last empties the wallet; hard-delete removes the row (no `is_deleted`).
- **Endpoint (WebApplicationFactory, real HTTP, skippable):**
  - `BankAccountsEndpointTests` (16) — full CRUD + set-default over the kebab-case route
    `api/v1/bank-accounts` wrapped in `ApiResult`; single-default invariant over HTTP (auto-default,
    atomic swap, delete-promotes, delete-last-empties); resource-owned 404 code 12000 (never 403) on
    get/update/delete/set-default + unknown uuid; validation 400 with camelCase `error.fields`
    (`bankBin`/`accountNumber`); anonymous → 401.
  - `WalletQrEndpointTests` (12) — expense QR → 200 `image/png` with **raw PNG magic bytes, NOT an
    ApiResult envelope** (proves the M8 `FileContentResult` bypass); `?format=payload` → 200 JSON
    `ApiResult<string>` whose CRC re-validates (independent) and whose amount = the expense total;
    no-account → 400 (12001); foreign expense → 404 (6000); foreign `?bankAccountUuid` override → 404
    (12000); anonymous → 401. Event QR on a closed event with a debtor → 200 composite `image/png`
    (PNG magic, `Content-Disposition: attachment`, non-trivial length); open event → 400 (12002); closed
    but nobody owes → 400 (12003); no account → 400 (12001); foreign event → 404 (9000); anonymous → 401.
    Note (matches shipped behaviour, Step 5): destination-account resolution precedes the resource check,
    so foreign-resource tests give the requester their own account.

**VietQR/CRC verification approach:** the production CRC is cross-checked two ways — (1) the canonical
`"123456789"` → `0x29B1` reference vector, and (2) an INDEPENDENT table-driven CRC-16/CCITT-FALSE
implemented inside the tests (a deliberately different algorithm from the production inline bit-loop) that
must agree on every sample and re-validate the `63xx` field of built payloads and the live
`?format=payload` HTTP response. Payloads are decoded with a test-local EMVCo TLV walker and asserted
field-by-field.

**Cleanup / determinism:** each DB/endpoint test class disposes by deleting its username-prefixed users
(cascades to bank_accounts via the user FK) plus an explicit `bank_accounts` sweep by the prefix's users
(defensive), reusing the base `ExpenseApiTestBase`/`AuthDbTestBase` sweeps for users/events/expenses/
audit_logs. Two consecutive full runs produced identical 878/878 results; a post-run DB probe found 0
test-prefixed users and 0 `bank_accounts` rows.

**No production bugs found.** All planning-doc test-list items and invariants are covered; no coverage gaps.
Only additive test files were created — no production code, CLAUDE.md, or config was modified.

### 2026-07-14 (code review — APPROVED, 0 blocking — milestone closed)

**Verdict: APPROVE, 0 blocking (2 informational notes). `dotnet test` = 878 passed / 0 failed / 0
skipped, deterministic (two consecutive identical runs), DB swept clean afterward.** Milestone 9 is
cleared for commit.

Verified checks:
- **Dependencies / licensing clean:** `QRCoder` 1.6.0 (MIT) + `SkiaSharp` 2.88.9 +
  `SkiaSharp.NativeAssets.Linux.NoDependencies` 2.88.9 (MIT binding / BSD-3 native) + **Be Vietnam Pro**
  embedded with `OFL.txt` (SIL-OFL 1.1); AutoMapper still pinned 13.0.1 — **no split-license trap**. The
  font loads via `SKTypeface.FromStream` off the embedded manifest resource — **no `System.Drawing`, no
  system-font dependency, no portability gap** (headless Linux/Docker renders Vietnamese diacritics from
  the embedded TTF).
- **VietQR correct:** EMVCo TLV `00`/`01` (12 dynamic vs 11 static) / `38` (NAPAS `A000000727` + BIN +
  account + `QRIBFTTA`) / `53`=704 / `54`=amount / `58`=VN / `62`-`08`=memo / `63`=CRC; CRC-16/CCITT-FALSE
  (poly `0x1021`, init `0xFFFF`) over the whole string incl. `6304`, matching the `0x29B1` vector; amount
  `0.##` invariant with no grouping; memo NFD-fold + đ-map + ASCII + truncate-25; P2P correctly omits the
  merchant tags `52`/`59`/`60`.
- **Default-account invariant race-free:** auto-default the first account; atomic user-scoped set-default
  swap that is never zero/two; delete-of-default promotes the most-recent remaining; delete-last empties;
  hard-delete (no `is_deleted`); update never touches `is_default`.
- **Resource-owned holds on every path:** `12000` (never 403); `6000`/`9000` for expense/event misses.
- **Event QR closed-only** (`12002`) reusing `GetEventBalanceAsync` (negative balance = still owing, no
  recompute), one QR per debtor at amount = `|balance|`, nobody-owes → `12003`, single composite PNG.
- **QR responses:** PNG via `FileContentResult` (M8 bypass) + `?format=payload` JSON; `AppController`
  untouched. Migration + model snapshot in sync; `12xxx` codes + `GetDefaultHttpStatus`; the tier-gate
  seam is present but not gating (OQ14).

Two informational notes (non-blocking, no action required to ship):
1. **Destination-account resolution runs before the resource-ownership check** — a **benign ordering
   quirk, NOT a privacy leak.** `12001`/`12000` depend solely on the requester's own wallet, so a foreign
   resource and a nonexistent one are indistinguishable to the caller (§4.1 satisfied).
2. **Font-stream lifetime is safe at SkiaSharp 2.88.9** (the typeface is fully materialized before the
   stream is disposed, and cached) — a heads-up to revisit `QrImageService.RenderComposite` (legacy
   `SKPaint.TextSize` / `DrawText(string)` / `MeasureText`) and the typeface load **if SkiaSharp is ever
   bumped to 3.x** (those APIs changed).

## Final Outcome

**Delivered, reviewed, and closed — Milestone 9 (Wallet + QR) is complete and APPROVED (code review
2026-07-14, 0 blocking + 2 informational notes): `dotnet build` clean, `dotnet test` 878/878 (0 failed,
0 skipped, deterministic, DB swept clean), and a full live HTTP smoke passed (VietQR CRC valid, PNG magic
bytes, composite one-QR-per-debtor).** The owner's wallet (`bank_accounts` CRUD with the
exactly-one-default invariant, mirroring M4) and on-demand VietQR generation for a whole expense and for
a closed event (one QR per still-owing member composited into one labelled PNG) are shipped end to end.

Endpoints: `BankAccountsController` → `api/v1/bank-accounts` CRUD + `PUT /{uuid}/default`;
`GET /expenses/{uuid}/qr` (PNG + `?format=payload` JSON) and `GET /events/{uuid}/qr` (closed-only
composite PNG). Services: `VietQrPayloadBuilder` (hand-rolled EMVCo TLV + CRC-16) + `QrImageService`
(QRCoder single QR + SkiaSharp composite) + `WalletQrService` + `BankAccountsService` + repo / DTOs /
validators / `BankAccountProfile`. Reuses the M7 balance (still-owing members, no recompute), the M5
derived expense total, and the M8 `FileContentResult` image bypass. No tier gate (clean seam for M10 —
OQ14); no QR persistence (on-demand).

Files created: `Database/Entities/BankAccount.cs` (+`Partials/BankAccount.cs`);
`Migrations/20260714115422_AddBankAccounts*.cs` (+ snapshot update); `Repositories/BankAccountRepository.cs`;
`Services/Api/Wallet/{VietQrPayloadBuilder,QrImageService,WalletQrService,BankAccountsService}.cs`;
`Models/Wallet/{CreateBankAccountRequest,UpdateBankAccountRequest,BankAccountResponse,QrImageResult}.cs`;
`Validators/Wallet/{Create,Update}BankAccountRequestValidator.cs`; `Mappings/BankAccountProfile.cs`;
`Controllers/BankAccountsController.cs`; `Assets/Fonts/{BeVietnamPro-Regular.ttf,OFL.txt}`.
Files modified: `FairShareMonApi.csproj` (deps + embedded font), `Database/AppDbContext.cs`,
`Constants/ErrorCodes.cs`, `Exception/ErrorException.cs`, `Controllers/ExpensesController.cs`,
`Controllers/EventsController.cs`.

Dependencies added: **QRCoder 1.6.0 (MIT)**, **SkiaSharp 2.88.9 (MIT/BSD-3)** +
**SkiaSharp.NativeAssets.Linux.NoDependencies 2.88.9**, and the embedded **Be Vietnam Pro (SIL-OFL
1.1)** font — all license-clean. Migration **`AddBankAccounts`** applied to the dev DB. QR generation
is stateless (nothing persisted) and ungated (the `IWalletQrService` entry points are the single seam a
later tier mechanism can gate — OQ14).

## Future Improvements

- Offer an SVG variant of the event composite (via QRCoder `SvgQRCode`) alongside the SkiaSharp PNG if a
  vector/scalable share format is later wanted — the `IQrImageService` seam isolates the change.
- A curated `banks` reference table (OQ4b) with server-side BIN validation + canonical names/logos, if
  the client-side bank picker proves insufficient.
- Per-member settled QR + debt reminders (§6) reusing the `VietQrPayloadBuilder` / `IQrImageService`.
- Caching generated QR images keyed by (account, amount, memo) if generation volume ever warrants it.
- Optional custom memo per QR (OQ9c) and non-numeric account-number support for the few banks that use
  alphanumerics (OQ5b), if real usage requires them.
