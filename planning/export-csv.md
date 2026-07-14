# Export CSV (Milestone 8: Export phiếu & đợt ra CSV)

Read-only export of a single **expense** (§3.5) and a single **event** (§3.6) to a downloadable file,
starting with **CSV** but behind a **format-extensible abstraction** so Excel/JSON can be added later
without touching the controllers or the data-gathering code (§5 lock: "Export: CSV trước mắt, thiết kế
mở cho định dạng khác."). Pure read + format over the shipped M5 (expenses/shares) and M7
(debt balance) data — no new expenditure data, and (expected) no schema change.

## Objective

Implement `The-ideal.md` §3.5 (Export phiếu) and §3.6 (Export đợt) on top of the shipped
Auth + Members + Categories + Tags + Expenses/Shares/Audit + Events + Stats stack:

- **Expense export (§3.5):** for one owned expense, produce a CSV with the expense's header info plus a
  per-member table (each member's share amount + note — "kèm ghi chú gộp"). Read-only, resource-owned.
- **Event export (§3.6):** for one owned event, produce a CSV with (a) a per-member share summary across
  the whole event AND (b) the **debt-balance table** (đã ứng / phải gánh / cân bằng — §3.7). Read-only,
  resource-owned. The balance is **reused from M7** (`IStatsService.GetEventBalanceAsync`) — never
  recomputed.
- **Format-extensible architecture (§5 lock):** CSV is one implementation behind an abstraction so
  Excel/JSON can be added later WITHOUT editing the controllers/services — this is the milestone's central
  design point. Only CSV is implemented now.
- **Response-wrapping bypass:** an export endpoint must return raw file bytes
  (`Content-Type: text/csv; charset=utf-8`, `Content-Disposition: attachment; filename=…`) despite
  `AppController` + `[ResponseWrapped]` auto-wrapping every response into `ApiResult<T>` — achieved
  WITHOUT editing the LOCKED `AppController` (see OQ1).
- **Resource-owned (§4.1):** export only the caller's own expense/event; a miss → 404 reusing
  `ExpenseNotFound` (6000) / `EventNotFound` (9000), never 403. Soft-deleted members/categories still
  appear in the exported historical data (§4.7).
- **No new tables / no EF migration expected** — export is read + format over M5/M7 rows (confirmed in
  Impact Analysis as an explicit non-change; OQ19).

This milestone owns a neutral **`ExportDocument`** intermediate representation, an **`IExportFormatter`**
abstraction with a single **`CsvExportFormatter`** implementation, an **`IExportService`** that gathers
data (reusing M5/M7) and builds the document, and two new **`export` routes** (placement is OQ8). It reuses
the M5 `expenses`/`shares`, M3 `members`, M4 `categories`, M6 `events`, and M7 balance, plus the
established resource-owned / `ApiResult` / DiDecoration conventions.

## Background

- **Response wrapping (grounded in the live code — the key technical finding):**
  - `Controllers/AppController.cs` is `[ResponseWrapped]` at the class level and is **LOCKED**.
  - `Attributes/ResponseWrappedAttribute.cs` is a `ResultFilterAttribute` whose `OnResultExecuting`
    only acts when `context.Result is ObjectResult` **and** that object's value is not already an
    `ApiResult`, **and** the status is 2xx; otherwise it `return`s and leaves the result untouched.
    A **`FileContentResult`** (returned by `ControllerBase.File(bytes, contentType, fileName)`) is
    **NOT** an `ObjectResult`, so the filter is a no-op on it — the raw file streams out unwrapped. This
    is the intended, edit-free bypass (OQ1a).
  - Error paths still wrap correctly: `Attributes/MvcFilters/ErrorHandlerFilter.OnException` converts a
    thrown `ErrorException` (e.g. `ExpenseNotFound`/`EventNotFound`) into an `ApiResult.Failure` result
    **before** the file result is produced, and `Middlewares/ErrorHandlerMiddleware` also wraps because
    the class-level `[ResponseWrapped]` metadata is present on the endpoint. So the success path returns a
    file and the failure path returns the normal JSON envelope — no `AppController` edit required.
- **M7 balance is directly reusable** (`planning/debt-balance-and-stats.md`, shipped):
  - `IStatsService.GetEventBalanceAsync(userUuid, eventUuid, ct)` returns `EventBalanceResponse`
    { `EventUuid`, `EventName`, `IsClosed`, `Rows: IReadOnlyList<MemberBalanceRow>` }.
  - `MemberBalanceRow` carries `MemberUuid`, `MemberName` (denormalized so deleted members display),
    `IsOwnerRepresentative`, `IsDeleted`, `Advanced`, `Owed`, `Balance` — exactly the debt-balance table
    §3.6 requires, and its **`Owed` column IS the per-member share total across the event** (each member's
    total phải gánh), so the §3.6 "bảng tổng hợp phần gánh của từng thành viên trên toàn đợt" is derivable
    from the same rows (OQ15). The balance already includes the owner-rep at 0đ and soft-deleted members
    with `IsDeleted = true` (§4.7). It resolves the resource-owned 404 (`EventNotFound` 9000) for free.
  - `GetEventBalanceAsync` does NOT expose the event's date range; the event header (name, range, closed)
    needs `IEventsService.GetAsync` if the range is wanted in the CSV (OQ14).
- **M5 expense is directly reusable** (`planning/expenses-shares-audit.md`, shipped):
  - `IExpensesService.GetAsync(userUuid, uuid, ct)` returns `ExpenseResponse` { `Uuid`, `Name`,
    `Description`, `ExpenseTime`, `Total` (derived `SUM(shares)`), `Category` (full, incl. deleted),
    `Payer` (full, incl. deleted), `IsSettled`, `SettledAt`, `Shares: IReadOnlyList<ShareResponse>`,
    `Tags`, `EventUuid`/`EventName`/`EventIsClosed`, `CreatedAt` }. It throws `ExpenseNotFound` (6000) on
    an ownership miss (resource-owned). `ShareResponse` = { `Uuid`, `Member` (full, incl. deleted),
    `Amount` (decimal VND), `Note`, `CreatedAt` }. This is the whole payload the expense CSV needs — no
    new repository method required (OQ11).
  - Money is `decimal(18,2)`, non-negative, never float (§4.3). The expense total is derived, not stored.
  - The owner-rep always has a share (0đ if not entered) so it always appears in the per-member table.
- **Conventions confirmed from the live code (identical M2–M7):** controllers derive from `AppController`,
  thin, Vietnamese `[SwaggerOperation]`/`[SwaggerResponse]`, `AuthenticatedUser.Id` = current user UUID;
  services `[ScopedService(typeof(IX))]` with primary ctors; multiple implementations of one interface use
  `[ScopedService(typeof(IX), Multiple = true)]` (otherwise `TryAdd` drops later ones); `Async` suffix +
  `CancellationToken`; `ErrorCodes` blocks per feature (next free block is **11xxx**; 10xxx is reserved by
  Stats). Reads never open transactions.
- **No existing export/CSV pattern to mirror:** `quick-ordering` has only a JWT `multipart/form-data`
  file-**upload** feature and a static-file-download middleware (`Content-Disposition` on static assets) —
  neither is a controller action that returns generated CSV, and it has no CSV library and no
  `ResponseWrapped` opt-out attribute. M8 designs its export shape from FairShareMon conventions, not from
  a sibling exemplar. No CSV library (CsvHelper etc.) is currently referenced anywhere in the solution.
- The dev DB holds no real product data beyond disposable smoke rows.

## Requirements

From `The-ideal.md` §3.5, §3.6, §3.7 (the embedded balance), §3.9, §5 (export lock, Premium extended
formats), and cross-cutting §4.1 / §4.3 / §4.7 / §4.9:

- **Expense export:** a CSV summarizing each member's share amount + note for one owned expense, with the
  expense header info. Read-only; resource-owned (miss → 6000/404).
- **Event export:** a CSV with the per-member share summary across the event **and** the debt-balance
  table (§3.7). Read-only; resource-owned (miss → 9000/404). Balance reused from M7, not recomputed.
- **Format-extensible design (§5):** CSV behind an abstraction; adding Excel/JSON later must not touch the
  controllers or the data-gathering code — only add a new formatter implementation.
- **Correct file download:** raw bytes with `text/csv` content type + `attachment` disposition, bypassing
  `[ResponseWrapped]` without editing `AppController`.
- **Vietnamese-correct CSV in Excel:** encoding/BOM/delimiter chosen so Vietnamese renders correctly when
  opened in Excel (OQ3/OQ4).
- **Soft-delete history (§4.7):** deleted members/categories appear in the exported data with their
  denormalized names; the owner-rep row is present.
- **Money accuracy (§4.3):** amounts are `decimal`, formatted deterministically (OQ5); never float.
- **Privacy / resource-owned (§4.1):** another user's expense/event looks non-existent (404, never 403).
- **Tier (§3.11 / §4.9):** CSV export is a Free/basic feature — not gated at M8; additional formats
  (Excel/JSON) are the Premium "mở rộng" set for later (OQ20). Reads/exports are never limit-gated (§4.9).

## Open Questions

> **All 20 answered by the user at the 2026-07-14 checkpoint — 19 accepted at the recommended option (a);
> OQ6 the user chose option (b)** (local Vietnamese date format, overriding the ISO-UTC recommendation).
> The annotated questions below carry the binding answers inline; the full options/trade-offs are preserved
> beneath each for the record and mirrored in the Decision Log. No open questions remain — implementation
> can start. The Implementation Plan, endpoint table, CSV-format spec, and test list below are synced to
> these answers. Decisions locked in §5 and in prior planning docs (domain terms; total = derived
> SUM(shares); balance per-event only, loose expenses excluded; hard-deleted expenses/shares, soft-deleted
> members/categories; resource-owned 404-never-403; `AppController` LOCKED) were NOT reopened.

**OQ1 — Response-wrapping bypass: how does an export endpoint return raw CSV given the LOCKED
`[ResponseWrapped]` `AppController`?**
> ~~**OQ1**~~ → **Answered 2026-07-14 (option a):** return a `FileContentResult` via
> `File(bytes, "text/csv; charset=utf-8", fileName)` from the `AppController`-derived controller. The
> `[ResponseWrapped]` filter no-ops on non-`ObjectResult` results, so the file streams out unwrapped;
> thrown `ErrorException`s still get wrapped into `ApiResult`. **NO edit to the LOCKED `AppController`.**
- **(a) [recommended] Return a `FileContentResult` via `File(bytes, "text/csv; charset=utf-8", fileName)`
  from an action on a controller that still derives from `AppController`.** `ResponseWrappedAttribute`
  only rewraps `ObjectResult` results (verified in the live code); a `FileContentResult` is not an
  `ObjectResult`, so it passes through completely untouched, while thrown `ErrorException`s (resource-owned
  miss) are still wrapped into `ApiResult` by `ErrorHandlerFilter`/`ErrorHandlerMiddleware` (the endpoint
  keeps the class-level `[ResponseWrapped]` metadata). Trade-off: none significant — no `AppController`
  edit, errors stay in the envelope, success returns a real file; this is exactly what the filter's design
  anticipates.
- **(b)** Add a `[SkipResponseWrap]`-style attribute (and teach the filter/middleware to honor it).
  Trade-off: explicit intent, but it requires editing `ResponseWrappedAttribute` (and arguably the filter
  wiring) for a case (a) already handles for free; more surface, no benefit.
- **(c)** Put the export endpoints on a dedicated controller that does NOT derive from `AppController`
  (no `[ResponseWrapped]`). Trade-off: guarantees no wrapping, but loses `AuthenticatedUser`, the
  versioned route convention, and the error-envelope-on-failure behavior, and diverges from every other
  controller — a last resort only.

**OQ2 — CSV generation: hand-rolled writer vs a library (CsvHelper).**
> ~~**OQ2**~~ → **Answered 2026-07-14 (option a):** a small hand-rolled RFC-4180 CSV writer, **no new
> dependency** — quote a field iff it contains the delimiter, a `"`, CR, or LF; double embedded quotes.
- **(a) [recommended] A small hand-rolled RFC-4180 CSV writer** (a `CsvExportFormatter` that quotes a
  field iff it contains the delimiter, a double-quote, CR, or LF; doubles embedded quotes; joins fields
  with the delimiter and rows with CRLF; prepends a UTF-8 BOM per OQ3). Trade-off: ~40 lines to write and
  unit-test, but honors the minimal-dependency ethos (no new NuGet package to license-review/pin — cf. the
  AutoMapper 13 pin), and the CSV shape here is simple tabular text. Escaping rules are fully specified so
  correctness is testable.
- **(b) Add CsvHelper (or Sylvan.Data.Csv).** Trade-off: battle-tested escaping/culture handling, but adds
  a dependency to a deliberately lean stack, needs a license/version decision, and is overkill for
  fixed-shape tables the app fully controls.

**OQ3 — Encoding + BOM (Vietnamese in Excel).**
> ~~**OQ3**~~ → **Answered 2026-07-14 (option a):** UTF-8 **WITH a BOM** (`EF BB BF`) so Excel renders
> Vietnamese correctly.
- **(a) [recommended] UTF-8 WITH a BOM (`EF BB BF`).** Excel on Windows (esp. VN locale) assumes the
  system ANSI code page for a BOM-less CSV and mangles Vietnamese diacritics; a UTF-8 BOM makes Excel
  detect UTF-8 and render "Đà Lạt", "Bình" correctly on double-click. Trade-off: a few strict UTF-8
  parsers choke on the BOM, but Excel (the primary consumer, §3.5 UC) needs it, and the byte order mark is
  standard for Excel-targeted CSV.
- **(b) UTF-8 without BOM.** Trade-off: cleaner for programmatic parsers, but Excel misreads Vietnamese on
  open — the exact failure the spec's "render correctly" concern calls out.
- **(c) Make the BOM a query/config flag.** Trade-off: flexible, but adds surface for a decision that has a
  clear default for the stated Excel use case.

**OQ4 — CSV dialect: delimiter + line ending.**
> ~~**OQ4**~~ → **Answered 2026-07-14 (option a):** **comma** delimiter + **CRLF** line endings
> (RFC-4180 canonical).
- **(a) [recommended] Comma delimiter, CRLF line endings** (RFC-4180 canonical). Trade-off: universally
  understood; a VN-locale Excel whose list separator is `;` may load a comma CSV into one column on
  double-click (the user then uses Data → From Text, or the BOM+UTF-8 import path). Simple, portable,
  standard.
- **(b) Semicolon delimiter, CRLF.** Trade-off: opens cleanly by double-click on many VN-locale Excel
  installs, but is wrong for comma-locale tools and non-Excel parsers, and diverges from RFC-4180.
- **(c) Make the delimiter a query parameter (default comma).** Trade-off: satisfies both camps, but adds
  API surface and test permutations for a first release; can be added later.

**OQ5 — Money/number formatting for VND amounts.**
> ~~**OQ5**~~ → **Answered 2026-07-14 (option a):** raw **invariant-culture** decimal (`800000.00`), dot
> decimal separator, two decimals, **no thousands grouping**.
- **(a) [recommended] Raw invariant-culture decimal, two decimals, no thousands separator
  (e.g. `800000.00`, `266.67`).** Machine-readable, re-importable, locale-independent, unambiguous, and
  free of a separator that would clash with the delimiter. Trade-off: less "pretty" than `800.000` for a
  human skimming the file, but avoids delimiter collisions and round-trips cleanly (§4.3 exactness).
- **(b) Thousands-separated / VND-formatted (e.g. `800.000` or `800,000 ₫`).** Trade-off: friendlier to
  read, but a `,`/`.` separator collides with the delimiter (forcing quoting), is locale-dependent, and is
  not machine-re-importable.
- **(c) Integer VND (drop the `.00`, e.g. `800000`).** Trade-off: compact and VND has no minor unit in
  practice, but shares are stored `decimal(18,2)` and a split like `266.67` would lose the cents — so this
  only works if no share ever carries a fractional value, which the model does allow.

**OQ6 — Date/datetime format + timezone.**
> ~~**OQ6**~~ → **Answered 2026-07-14 (option b — USER OVERRODE the recommendation):** dates/times are
> shown in **local Vietnamese format**, NOT ISO-UTC. **Two distinct formatting rules — this is where a bug
> would hide, so it is spelled out and tested:**
> - **Instants** (`expense_time`, and any created/settled timestamp if shown) are stored UTC → **convert
>   to UTC+7 (Asia/Ho_Chi_Minh, fixed +7, no DST)** then format **`dd/MM/yyyy HH:mm`**.
> - **The event's calendar date range** (`start_date`/`end_date`) is stored as whole-day **UTC boundaries**
>   (`00:00:00` and `23:59:59.999999`, per M6 OQ1) representing **calendar dates** → display **`dd/MM/yyyy`
>   taken from the STORED UTC date component with NO +7 shift**. A naive +7 shift of `end_date`
>   (`23:59:59.999` UTC) would roll it to the next calendar day and mis-display the range — so the range is
>   date-only-from-UTC, never shifted.
> - **Centralize:** a single instant-formatter (`dd/MM/yyyy HH:mm`, applies +7) and a single date-formatter
>   (`dd/MM/yyyy`, no shift) reused by every current/future formatter (put them with the service's
>   document-building helpers). Add an explicit test that an `end_date` at `23:59:59.999 UTC` displays as
>   its own calendar day, not the next.
- **(a) [recommended] ISO 8601 UTC (`yyyy-MM-ddTHH:mm:ssZ`), matching the UTC storage convention.**
  Deterministic, sortable, unambiguous, and consistent with the M6 "raw-UTC datetime" model (no per-user
  timezone exists in the system). Trade-off: a reader in UTC+7 sees UTC times, not local — acceptable
  given the app stores/compares UTC everywhere and introduces no timezone concept.
- **(b) Local-ish `dd/MM/yyyy HH:mm` (VN-style display).** Trade-off: friendlier for a VN reader, but
  there's no per-user timezone to localize to (it would still be UTC-derived, just relabeled), it's
  ambiguous/unsortable, and diverges from the stored UTC convention.
- **(c) Date-only `yyyy-MM-dd` where a time isn't needed** (e.g. event range) and full ISO where it is
  (expense_time). Trade-off: cleaner per-field, but two formats to specify/test — foldable into (a).

**OQ7 — Export abstraction shape (the central design point).**
> ~~**OQ7**~~ → **Answered 2026-07-14 (option a):** a neutral `ExportDocument` IR (title + ordered
> sections of header-fields / table-rows) built by `IExportService`, rendered by `IExportFormatter` keyed
> by `ExportFormat` (`[ScopedService(typeof(IExportFormatter), Multiple = true)]`); one `CsvExportFormatter`
> now. Adding Excel/JSON later = one new formatter class, no controller/service/DTO change.
- **(a) [recommended] A neutral `ExportDocument` intermediate representation + an `IExportFormatter`
  keyed by format.** `IExportService` gathers data (from M5/M7) and builds a format-agnostic
  `ExportDocument` (a title + ordered `ExportSection`s, each an optional name + header row + data rows of
  strings, incl. a leading label/value "header block" section — OQ12/OQ14). `IExportFormatter`
  { `ExportFormat Format`, `string ContentType`, `string FileExtension`, `byte[] Render(ExportDocument)` }
  has one impl now (`CsvExportFormatter`) registered `[ScopedService(typeof(IExportFormatter),
  Multiple = true)]`. Adding Excel/JSON later = add one formatter class; the data-gathering, the
  controllers, and the DTOs are untouched (§5 lock satisfied). All money/date/number stringification
  happens once, in the service, when building the document (so every future format is consistent).
  Trade-off: one extra IR layer, but it's the cleanest realization of "thiết kế mở cho định dạng khác".
- **(b)** `IExporter` interface per document type + per format (e.g. `IExpenseCsvExporter`,
  `IEventCsvExporter`) that go straight from the domain object to bytes. Trade-off: fewer abstraction
  layers, but adding a format means N new classes (one per document type) and duplicates the
  money/date/label formatting in each — poorer extensibility.
- **(c)** A single `IExportService` with a `switch (format)` producing CSV inline. Trade-off: least code
  now, but the §5 "open design" becomes a switch statement to edit for every new format — violates the
  milestone's central requirement.

**OQ8 — Where the export routes live.**
> ~~**OQ8**~~ → **Answered 2026-07-14 (option a):** export sub-routes on the existing controllers —
> `GET api/v1/expenses/{uuid}/export` and `GET api/v1/events/{uuid}/export`.
- **(a) [recommended] Sub-routes on the existing resource controllers:**
  `GET api/v1/expenses/{uuid}/export` on `ExpensesController` and `GET api/v1/events/{uuid}/export` on
  `EventsController`, both delegating to `IExportService`. Trade-off: consistent with the shipped idiom
  (the balance is already `GET events/{uuid}/balance`, shares are `expenses/{uuid}/shares`); the client
  already holds the uuid; two thin actions. The reusable format machinery lives in `IExportService`, not
  in the controllers.
- **(b)** A dedicated `ExportController`: `GET api/v1/export/expenses/{uuid}`,
  `GET api/v1/export/events/{uuid}`. Trade-off: groups all export in one controller (nice if many export
  types come later), but duplicates the resource path segment and splits an expense's routes across two
  controllers.

**OQ9 — Format-selection mechanism.**
> ~~**OQ9**~~ → **Answered 2026-07-14 (option a):** a `?format=csv` query parameter, defaulting to `csv`
> when omitted.
- **(a) [recommended] A `?format=csv` query parameter, defaulting to `csv` when omitted.** Explicit,
  visible in Swagger, trivially extensible (`?format=xlsx`), and cache/URL-friendly. Trade-off: less
  "RESTful-purist" than content negotiation, but far simpler to implement/test and self-documenting.
- **(b)** HTTP `Accept` header content negotiation (`Accept: text/csv`). Trade-off: idiomatic, but harder
  to trigger from a browser link, invisible in a shared URL, and heavier to wire against `[ResponseWrapped]`.
- **(c)** A file extension in the route (`…/export.csv`). Trade-off: clean URLs, but route-template
  juggling and the default-format case is awkward.

**OQ10 — Unsupported / invalid `format` value.**
> ~~**OQ10**~~ → **Answered 2026-07-14 (option a):** unsupported `format` → **400 `ValidationFailed`
> (1001)**; no new code.
- **(a) [recommended] 400 Bad Request via `ValidationFailed` (1001)** with a Vietnamese message
  (`export.unsupportedFormat`), when `format` is present but not a registered formatter. Trade-off:
  consistent with how the app surfaces bad input (1001 + `error.fields`); simplest for the client.
- **(b)** 415 Unsupported Media Type. Trade-off: HTTP-semantically precise for content negotiation, but
  the app has no 415 precedent and `format` is a query param, not a media type — inconsistent.
- **(c)** A dedicated `ExportFormatUnsupported` code in the new 11xxx block (still HTTP 400). Trade-off: a
  machine-distinct code, but M8 otherwise needs no new codes; reserve 11xxx and decide if this one is
  worth defining (ties to OQ19).

**OQ11 — Data source for the export service: reuse services vs repositories.**
> ~~**OQ11**~~ → **Answered 2026-07-14 (option a):** reuse the application services
> (`IExpensesService.GetAsync`, `IStatsService.GetEventBalanceAsync`, `IEventsService.GetAsync`) — free
> 404 + derived total + M7 balance; no new repository query except a small notes read for merged event
> notes.
- **(a) [recommended] Reuse the existing application services** — `IExpensesService.GetAsync`,
  `IStatsService.GetEventBalanceAsync`, and `IEventsService.GetAsync` (for the event header/range). This
  gets the resource-owned 404 (6000/9000), the derived total, the full-info incl.-deleted mapping, and the
  M7 balance for free — zero duplicated query logic (DRY). Trade-off: `IExportService` depends on other
  services (service-to-service composition), not repositories; acceptable here since it's pure read
  orchestration and avoids re-deriving totals/balance in a second place.
- **(b)** Inject the repositories directly (`IExpenseRepository.GetByUuidAsync`, `IStatsRepository`,
  `IEventRepository`) and map in the export service. Trade-off: keeps services from depending on services,
  but re-does the null→404 mapping and total/balance shaping the services already own — duplication and a
  risk of the export drifting from the API's numbers.

**OQ12 — Expense CSV layout & columns (§3.5).**
> ~~**OQ12**~~ → **Answered 2026-07-14 (option a):** header block (name, description, expense_time
> [+7 `dd/MM/yyyy HH:mm`], payer, category, total, settled) + a per-member share table (member name, share
> amount, note) + a total row.
- **(a) [recommended] A header block (label/value rows) followed by a per-member share table.** Header
  block rows: `Tên phiếu`, `Mô tả`, `Thời điểm chi` (ISO UTC), `Người trả`, `Danh mục`, `Nhãn`
  (comma-joined), `Đợt` (name or "(không thuộc đợt)"), `Đã trả` (Có/Không), `Tổng tiền`. Then a blank
  line, then the table header `Thành viên,Số tiền gánh,Ghi chú` and one row per share
  (`Member.Name`, `Amount`, `Note`), including the owner-rep (even 0đ) and any soft-deleted member (name
  still shown, optionally suffixed "(đã xóa)" — see OQ13's note), sorted by amount DESC then name. A final
  `Tổng cộng` total row (= expense total). Trade-off: mixes a metadata block and a table in one CSV (two
  "shapes"), but it is the natural human-readable report and the `ExportDocument` IR models it as a
  metadata section + a table section cleanly.
- **(b)** Flat table only (one row per share, header fields repeated per row or dropped). Trade-off:
  strictly tabular / easiest to re-import, but loses the readable header presentation §3.5 implies.
- **(c)** Header + table as two separate CSV files zipped. Trade-off: cleanest separation, but adds a zip
  concern and a second content type for a single small expense.

**OQ13 — "ghi chú gộp" (merged notes) semantics.**
> ~~**OQ13**~~ → **Answered 2026-07-14 (option a):** expense export — one note per member; event export —
> join a member's notes across expenses with "; ", each prefixed by the expense name; a deleted member gets
> a "(đã xóa)" suffix on the name.
- **(a) [recommended] For the expense export, one note per member row (a member has exactly one share per
  expense — duplicates are forbidden), so "gộp" here means the note column sits alongside the amount in the
  per-member summary; empty when absent. For the event export, a member may have shares across many
  expenses, so their notes are MERGED into one cell by joining the non-empty notes with "; ", each
  optionally prefixed by the expense name (`"Ăn tối: chia đều; Taxi: về sân bay"`).** Also flag a
  soft-deleted member's name with a "(đã xóa)" suffix (§4.7). Trade-off: the event note cell can get long,
  but it faithfully "gộp"s all of a member's notes; the prefix keeps provenance.
- **(b)** Event export: join notes with "; " WITHOUT the expense-name prefix. Trade-off: shorter cells,
  but loses which expense each note came from.
- **(c)** Event export: omit merged notes entirely (only the per-member owed total). Trade-off: simplest,
  but §3.6 leans on §3.5's "kèm ghi chú gộp" — dropping notes loses information the spec calls for.

**OQ14 — Event CSV layout: sections, header, and whether to list expenses.**
> ~~**OQ14**~~ → **Answered 2026-07-14 (option a):** ONE file — event header (name, date range
> [calendar `dd/MM/yyyy`, no +7], closed status) + Section 1 (per-member share summary) + Section 2 (the
> debt-balance advanced/owed/balance table). **No per-expense list.**
- **(a) [recommended] One CSV with an event header block + two labeled sections.** Header block:
  `Tên đợt`, `Khoảng thời gian` (start–end, requires `IEventsService.GetAsync`), `Trạng thái`
  (Đang mở/Đã chốt). Section 1 "Tổng hợp phần gánh theo thành viên": `Thành viên,Tổng phần gánh,Ghi chú
  gộp` (the per-member owed total from the balance rows + merged notes per OQ13). Section 2 "Cân bằng nợ":
  `Thành viên,Đã ứng,Phải gánh,Cân bằng` (the M7 balance rows verbatim), with a final `Tổng cộng` row
  where Cân bằng must be 0 (sum-to-zero). Both sections include the owner-rep and soft-deleted members
  (§4.7). Do NOT embed the full per-expense list (that's what `GET /expenses?eventUuid=…` is for).
  Trade-off: two sections in one file (multiple shapes), but matches §3.6 exactly ("bảng tổng hợp phần
  gánh … + bảng cân bằng nợ") and is a single convenient download.
- **(b)** Also embed a third "Danh sách phiếu" section (every expense: name, time, payer, category, total,
  settled). Trade-off: a richer standalone report, but §3.6 asks only for the two summary tables and this
  duplicates the expense-list endpoint; heavier and more test surface.
- **(c)** Two separate CSVs (summary + balance) zipped. Trade-off: clean separation / each re-importable,
  but adds zip handling for a single event report.

**OQ15 — Event per-member share summary: source.**
> ~~**OQ15**~~ → **Answered 2026-07-14 (option a):** derive the per-member share summary from the M7
> balance rows' `Owed` column (+ a small notes read for the merged-notes cell); do NOT add a new aggregate.
- **(a) [recommended] Derive it from the M7 balance rows' `Owed` column** (each member's total phải gánh
  across the event) — the balance already sums exactly this over the same share-set, so no second
  aggregation and guaranteed consistency with the balance table in the same file. Merged notes (OQ13) are
  the only thing the balance doesn't carry, so the service additionally fetches the event's shares' notes
  (via the expense list) purely to build the merged-notes cell. Trade-off: one extra read for notes, but
  reuses M7 for all the money and keeps the two tables numerically consistent.
- **(b)** Compute a separate per-member share aggregate in a new export repository method. Trade-off:
  self-contained, but re-implements what M7 already computes and risks divergence from the balance table.

**OQ16 — Event export vs closed state.**
> ~~**OQ16**~~ → **Answered 2026-07-14 (option a):** event export available for **BOTH** open and closed
> events (export is read-only).
- **(a) [recommended] Available for BOTH open and closed events** (export is read-only; §3.6/§4.4 only
  freeze writes, and the M7 balance is already available for both — mirrors that decision). Trade-off:
  none — an open event exports a current snapshot, exactly like viewing its balance.
- **(b)** Closed-only (mirror the M9 QR gate). Trade-off: consistent with QR, but §3.6 places no such gate
  on export and it would block the useful "export the current state mid-trip" case.

**OQ17 — Scope: single-resource export only?**
> ~~**OQ17**~~ → **Answered 2026-07-14 (option a):** single-expense + single-event export only; no
> bulk/filtered-list export at M8.
- **(a) [recommended] Single-expense export + single-event export only** (per §3.5/§3.6). No bulk
  filtered-expense-list export at M8. Trade-off: matches the spec exactly; a "list export" (e.g.
  `GET /expenses/export?filter…`) can be an additive future improvement reusing the same abstraction.
- **(b)** Also add a filtered-list export now. Trade-off: handy, but beyond §3.5/§3.6, and needs its own
  columns/decisions — better once the single-resource shape is settled.

**OQ18 — Filename convention + sanitization.**
> ~~**OQ18**~~ → **Answered 2026-07-14 (option a):** `expense-{uuid}-{yyyyMMdd}.csv` and
> `event-{slug}-{yyyyMMdd}.csv`; the slug is ASCII-folded from the event name with unsafe chars stripped,
> falling back to the uuid when empty.
- **(a) [recommended] `expense-{uuid}-{yyyyMMdd}.csv` and `event-{slug}-{yyyyMMdd}.csv`**, where `{slug}`
  is the event name lowercased, diacritics stripped, non-`[a-z0-9]` runs collapsed to `-`, trimmed, capped
  (e.g. 40 chars), falling back to the uuid when empty. `{yyyyMMdd}` is today's UTC date. The filename is
  set via `Content-Disposition: attachment; filename="…"` (ASCII-safe) plus a `filename*=UTF-8''…`
  variant if the original name is wanted. Trade-off: predictable, filesystem-safe, no header-injection
  risk; the slug loses Vietnamese diacritics in the filename (the CSV content keeps them).
- **(b)** Use the raw name (URL-encoded) in `filename*`. Trade-off: prettier filename, but needs careful
  RFC 5987 encoding and CRLF/quote sanitization to avoid header injection.
- **(c)** Uuid-only filenames (`expense-{uuid}.csv`). Trade-off: simplest/safest, but less friendly in a
  Downloads folder.

**OQ19 — Error-code block for Export.**
> ~~**OQ19**~~ → **Answered 2026-07-14 (option a):** reserve the **11xxx Export** block in `ErrorCodes.cs`
> with a comment; define NO new codes (unsupported format → 1001; resource-owned miss → 6000/9000).
- **(a) [recommended] Reserve the 11xxx Export block in `ErrorCodes.cs` with a comment; define no new
  codes.** Resource-owned misses reuse `ExpenseNotFound` (6000) / `EventNotFound` (9000); an unsupported
  `format` is `ValidationFailed` (1001) (OQ10a). Trade-off: none — continues the one-block-per-feature
  reservation without inventing unused codes.
- **(b)** Define `11000 ExportFormatUnsupported` (HTTP 400) now for OQ10. Trade-off: a machine-distinct
  code for the unsupported-format case; only worth it if the client must branch on it programmatically.

**OQ20 — Premium gating of export (§3.11).**
> ~~**OQ20**~~ → **Answered 2026-07-14 (option a):** no tier gate at M8 (CSV export is a Free feature;
> reads are never gated) — leave a clean seam so a later milestone can gate the Excel/JSON formats.
- **(a) [recommended] No tier gate at M8** — CSV export is the Free/basic feature (§3.11 lists "export
  CSV" under Cơ bản), and reads/exports are never limit-gated (§4.9). Leave a clear extension seam so that
  when Excel/JSON formatters are added they can be gated as the Premium "mở rộng" set. Trade-off: none for
  M8; the gating decision is deferred to whichever milestone adds the extra formats (and to the
  still-open tier-limits planning).
- **(b)** Introduce a tier check now (even though only CSV exists). Trade-off: pre-wires the Premium path,
  but there's nothing to gate yet and the tier-limit mechanism itself isn't finalized in a planning doc.

## Assumptions

> **Confirmed by the user at the 2026-07-14 checkpoint** together with the 20 Open Questions — these are
> now decisions, not vetoable assumptions. Each is derived from the spec, prior decisions, and the shipped
> M5/M7 code.

- All export endpoints are **guarded** (valid access token required); anonymous → 401. No anonymous export
  (public share links are §6 future).
- M8 is **read-only** — it creates/updates/deletes no rows and writes no audit entries.
- **No schema change / NO EF migration** (OQ19; called out in Impact Analysis as an explicit non-change).
  Export is pure read + format over M5/M7 rows.
- The **owner** is always the current authenticated user; sharing/other-actor concerns (§6) are out of
  scope.
- Money and dates are stringified once, in the service, when building the neutral `ExportDocument`, so all
  future formats stay consistent (OQ5/OQ6).
- The `ExpenseResponse` / `EventBalanceResponse` / `EventResponse` shapes already expose everything the
  CSVs need; no new repository query is required (OQ11a) except an optional per-member notes read for the
  event's merged-notes cell (OQ15a).
- CSV is a Free-tier feature; export is not limit-gated (§4.9 / OQ20).

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/`. New services/formatters use DiDecoration
> attributes. All user-facing strings and Swagger summaries are Vietnamese. Concrete names reflect the
> **recommended option (a)** for every Open Question; if the user picks a different option at the
> checkpoint, re-sync the affected steps before coding.
>
> **NO SCHEMA CHANGE / NO EF MIGRATION.** M8 adds no entity, no `DbSet`, no `ConfigureModel`, and runs no
> `dotnet ef migrations add`. Steps: format enum + neutral document IR → formatter abstraction + CSV
> formatter → export service (reuse M5/M7) → controllers/routes → error-code reservation → tests.

### Step 1 — Format enum + neutral document IR + result type

`Models/Export/ExportFormat.cs` — `enum ExportFormat { Csv }` (Excel/Json added later). Include a
`Constants/ExportFormats.cs` or a helper mapping the query string (`"csv"`) ↔ enum, case-insensitive.

`Models/Export/ExportDocument.cs` — the format-agnostic IR:
- `ExportDocument { string Title; IReadOnlyList<ExportSection> Sections; }`.
- `ExportSection { string? Name; IReadOnlyList<KeyValuePair<string,string>>? HeaderFields; IReadOnlyList<string> ColumnHeaders; IReadOnlyList<IReadOnlyList<string>> Rows; }`
  (a section is either a label/value "header block" via `HeaderFields`, or a table via
  `ColumnHeaders` + `Rows`; the CSV formatter renders both, blank-line-separated). Model choice ties to
  OQ12/OQ14.

`Models/Export/ExportedFile.cs` — `ExportedFile { byte[] Content; string ContentType; string FileName; }`
(the service's output; the controller turns it into `File(...)`).

**Centralized value formatting (OQ5 money + OQ6 dates — one place, reused by every current/future
formatter).** Add a small helper alongside the service (e.g. `Services/Api/Export/ExportValueFormatter.cs`
or static helpers on the service) that ALL document-building goes through:
- **Money (OQ5a):** `decimal.ToString("0.00", CultureInfo.InvariantCulture)` — dot decimal, two decimals,
  no thousands grouping (`800000.00`).
- **Instant (OQ6b):** `FormatInstant(DateTime utc)` → convert the stored UTC to **UTC+7
  (Asia/Ho_Chi_Minh, fixed +7, no DST)** — `utc.AddHours(7)` on the UTC value (or a fixed
  `TimeSpan.FromHours(7)` offset; do NOT use a machine-local `TimeZoneInfo`) — then
  `ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)`. Used for `expense_time` and any
  created/settled timestamp that appears.
- **Calendar date (OQ6b):** `FormatCalendarDate(DateTime utcBoundary)` → format the **stored UTC date
  component with NO +7 shift**: `utcBoundary.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)`. Used
  ONLY for the event's `start_date`/`end_date` (whole-day UTC boundaries representing calendar dates, M6
  OQ1). **A naive +7 shift of `end_date` (`23:59:59.999 UTC`) would roll it to the next calendar day — so
  the range is date-only-from-UTC, never shifted.** This split is the single most bug-prone point of M8;
  it is unit-tested (Step 7).

### Step 2 — Formatter abstraction + CSV formatter

`Services/Api/Export/IExportFormatter.cs` —
`interface IExportFormatter { ExportFormat Format { get; } string ContentType { get; } string FileExtension { get; } byte[] Render(ExportDocument document); }`.

`Services/Api/Export/Formatters/CsvExportFormatter.cs` —
`[ScopedService(typeof(IExportFormatter), Multiple = true)]` (Multiple so future formatters coexist —
CLAUDE.md warns a non-Multiple `TryAdd` drops later ones). `Format => ExportFormat.Csv`,
`ContentType => "text/csv; charset=utf-8"`, `FileExtension => "csv"`. `Render`:
- Build text: for each section, emit `HeaderFields` as `label,value` rows, then (if any) a blank line, the
  `ColumnHeaders` row, and each data row; blank line between sections (OQ12/OQ14 layout).
- **RFC-4180 escaping (OQ2a):** quote a field iff it contains `,` (delimiter — OQ4), `"`, CR, or LF;
  double embedded `"`; join fields with the delimiter; terminate every row with CRLF.
- **Encoding (OQ3a):** UTF-8 with a leading BOM (`Encoding.UTF8.GetPreamble()` + `GetBytes(text)`), so
  Excel renders Vietnamese.

### Step 3 — Export service (data gathering, reuse M5/M7)

`Services/Api/Export/IExportService.cs` + `ExportService.cs`
(`[ScopedService(typeof(IExportService))]`, primary ctor injecting `IExpensesService`, `IStatsService`,
`IEventsService`, and `IEnumerable<IExportFormatter>` — OQ11a):
- `Task<ExportedFile> ExportExpenseAsync(string userUuid, string expenseUuid, string? format, CancellationToken)`:
  1. `ResolveFormatter(format)` — default `csv`; unknown → `ErrorException(ValidationFailed, "…")` (OQ10a).
  2. `expense = await expensesService.GetAsync(userUuid, expenseUuid, ct)` (throws `ExpenseNotFound`
     6000 on a miss — resource-owned).
  3. `BuildExpenseDocument(expense)` — the header block + per-member share table (OQ12), stringifying
     money via the money helper (OQ5a) and `expense_time` (and settled timestamp, if shown) via
     `FormatInstant` (+7 `dd/MM/yyyy HH:mm`, OQ6b); owner-rep + soft-deleted members included with names
     (§4.7, OQ13).
  4. `bytes = formatter.Render(document)`; build the filename (OQ18);
     return `new ExportedFile(bytes, formatter.ContentType, fileName)`.
- `Task<ExportedFile> ExportEventAsync(string userUuid, string eventUuid, string? format, CancellationToken)`:
  1. `ResolveFormatter(format)`.
  2. `balance = await statsService.GetEventBalanceAsync(userUuid, eventUuid, ct)` (throws `EventNotFound`
     9000 on a miss). `evt = await eventsService.GetAsync(userUuid, eventUuid, ct)` (for the range header).
     For merged notes (OQ13/OQ15a), read the event's shares' notes (e.g. via
     `expensesService.ListAsync` with `EventUuid` filter, or a small dedicated read).
  3. `BuildEventDocument(evt, balance, notes)` — header block (name; **date range via
     `FormatCalendarDate` — `dd/MM/yyyy`, NO +7 shift**, OQ6b/OQ14; closed status) + Section 1 (per-member
     share summary from `balance.Rows[].Owed` + merged notes) + Section 2 (the balance table
     `Advanced/Owed/Balance` + a `Tổng cộng` row) (OQ14); money via the money helper (OQ5a); owner-rep +
     soft-deleted members included (§4.7).
  4. Render + filename + return `ExportedFile`.
- `ResolveFormatter(string? format)` — parse to `ExportFormat` (default Csv); find the matching
  `IExportFormatter` in the injected set; none → `ValidationFailed`.

### Step 4 — Controllers / routes

**[M5-MOD]** `Controllers/ExpensesController.cs` — add (inject `IExportService`):

| Verb + Route | Request → Response | Notes |
|---|---|---|
| `GET api/v1/expenses/{uuid}/export` | `[FromRoute] uuid`, `[FromQuery] string? format` → `FileContentResult` (`text/csv`) | resource-owned (miss → 6000/404); default `format=csv`; unknown format → 400; returns `File(file.Content, file.ContentType, file.FileName)` (unwrapped — OQ1a) |

**[M6-MOD]** `Controllers/EventsController.cs` — add (inject `IExportService`):

| Verb + Route | Request → Response | Notes |
|---|---|---|
| `GET api/v1/events/{uuid}/export` | `[FromRoute] uuid`, `[FromQuery] string? format` → `FileContentResult` (`text/csv`) | resource-owned (miss → 9000/404); OPEN or CLOSED (OQ16); default `format=csv`; unknown → 400 |

Both actions: Vietnamese `[SwaggerOperation]`; `[SwaggerResponse(200, …, typeof(FileContentResult))]` (or
`[Produces("text/csv")]`), `[SwaggerResponse(400, …, typeof(ApiResult))]`,
`[SwaggerResponse(404, …, typeof(ApiResult))]`, `[SwaggerResponse(401, …, typeof(ApiResult))]`;
`userUuid = AuthenticatedUser.Id`. Action bodies stay thin: call `exportService`, return `File(...)`.

### Step 5 — Error codes

**[MOD]** `Constants/ErrorCodes.cs` — reserve the **11xxx Export** block with a comment; define no new
codes (OQ19a). Unsupported `format` reuses `ValidationFailed` (1001); resource-owned misses reuse
`ExpenseNotFound` (6000) / `EventNotFound` (9000). No change to `ErrorException.GetDefaultHttpStatus`.

### Step 6 — Vietnamese message keys / strings

- `export.unsupportedFormat` → "Định dạng xuất không được hỗ trợ." (unknown `format` → 400).
- CSV labels (Vietnamese): `Tên phiếu`, `Mô tả`, `Thời điểm chi`, `Người trả`, `Danh mục`, `Nhãn`, `Đợt`,
  `Đã trả` (Có/Không), `Tổng tiền`, `Thành viên`, `Số tiền gánh`, `Ghi chú`, `Tổng cộng`; event:
  `Tên đợt`, `Khoảng thời gian`, `Trạng thái` (Đang mở/Đã chốt), `Tổng hợp phần gánh theo thành viên`,
  `Tổng phần gánh`, `Ghi chú gộp`, `Cân bằng nợ`, `Đã ứng`, `Phải gánh`, `Cân bằng`; deleted-member/
  category suffix `(đã xóa)`.
- Reuse existing 404 messages: "Không tìm thấy phiếu chi tiêu." / "Không tìm thấy đợt chi tiêu."

### Step 7 — Tests (owned by the test-engineer; definitive list)

Reuse the M5/M6/M7 harness: `[Collection("AuthIntegration")]`; DB/endpoint tests use the
`ExpenseDbTestBase` / `ExpenseApiTestBase` families (own connections / app DI + real HTTP), a unique
lowercase username prefix per class, dispose-time cascade cleanup; all DB-dependent tests are
`[SkippableFact]` (skip when MariaDB is unreachable), never EF InMemory.

**Unit (no DB) — `CsvExportFormatterTests`:**
- Quoting: a field with a comma / a `"` / a newline is wrapped in quotes and embedded quotes are doubled;
  a plain field is not quoted (RFC-4180).
- Line endings are CRLF; sections are blank-line separated; header-block rows render as `label,value`.
- The output starts with the UTF-8 BOM (`EF BB BF`); Vietnamese text round-trips (decode → original).
- `ContentType == "text/csv; charset=utf-8"`, `FileExtension == "csv"`, `Format == Csv`.

**Unit (no DB) — `ExportValueFormatterTests` (the OQ6 bug-prone split — mandatory):**
- **Instant formatting (OQ6b):** a UTC `expense_time` of `2026-03-01T18:30:00Z` renders as
  `02/03/2026 01:30` (+7 applied); a settled/created instant if shown follows the same +7 rule.
- **Calendar-date formatting (OQ6b — the critical case):** an event `end_date` stored as
  `2026-03-03T23:59:59.999999Z` renders as `03/03/2026` (its own calendar day, **NOT** `04/03/2026`); a
  `start_date` of `2026-03-01T00:00:00Z` renders as `01/03/2026`; **no +7 shift is applied to the range.**
- **Money formatting (OQ5a):** `800000m` → `800000.00`, `266.67m` → `266.67`, `0m` → `0.00`
  (invariant culture, dot separator, no grouping).

**Unit (no DB) — `ExportServiceTests`** (fake `IExpensesService`/`IStatsService`/`IEventsService` + the
real or a stub formatter set):
- Unsupported `format` → `ErrorException(ValidationFailed)`; omitted/`"csv"`/`"CSV"` → the CSV formatter.
- Expense document: header-block fields + one table row per share incl. the owner-rep (0đ) and a
  soft-deleted member (name shown, `(đã xóa)`); a `Tổng cộng` row equals the total; `expense_time` shown as
  +7 `dd/MM/yyyy HH:mm` (OQ6b) and money as `0.00`-style (OQ5a).
- Event document: header block; Section 1 per-member owed + merged notes (OQ13 join); Section 2 the M7
  balance rows + a `Tổng cộng` Cân bằng of 0.
- Resource-owned miss: a thrown `ExpenseNotFound`/`EventNotFound` from the underlying service propagates
  (the service does not swallow it).
- Filename shape (OQ18): `expense-{uuid}-{date}.csv`; event slug sanitized (diacritics stripped, unsafe
  chars → `-`), empty name → uuid fallback.

**Integration (real MariaDB) — `ExportServiceTests` (DB) / reuse via the export service over seeded data:**
- Expense export content: seed an expense with several shares (incl. an owner-rep 0đ share and a
  soft-deleted member's share and a soft-deleted category) → the CSV contains every member row with the
  correct amounts/notes and the deleted member/category names (§4.7); total row correct.
- Event export content: seed the §3.7 scenario (Bình +300k, Cường −500k) → Section 2 matches the balance
  and Cân bằng sums to 0; Section 1 per-member owed matches; merged notes gathered across the event's
  expenses (OQ13); open AND closed event both export (OQ16). The event header **date range displays as the
  stored calendar days** (`dd/MM/yyyy`, no +7) — an `end_date` at `23:59:59.999 UTC` shows its own day, and
  an `expense_time` in the header/rows shows the +7 local time (OQ6b).
- Resource-owned: another user's expense/event is not exportable (service maps to 6000/9000).

**Endpoint (WebApplicationFactory) — `ExportEndpointTests`:**
- `GET /expenses/{uuid}/export` and `GET /events/{uuid}/export` return **HTTP 200 with
  `Content-Type: text/csv; charset=utf-8`** and `Content-Disposition: attachment; filename=…`, and the
  body is **raw CSV (NOT an `ApiResult` envelope)** — proves the `[ResponseWrapped]` bypass (OQ1a); the
  body begins with the UTF-8 BOM.
- Resource-owned 404: another user's / unknown uuid → **HTTP 404 wrapped as `ApiResult`** (`error.code`
  6000 / 9000), never 403, never a file.
- Unsupported `format` (e.g. `?format=xml`) → **HTTP 400 wrapped as `ApiResult`** (`ValidationFailed`).
- Anonymous request → **401 wrapped as `ApiResult`**.
- Default (no `format`) → CSV; `?format=csv` (any case) → CSV.

### Step 8 — Wrap-up

- Update this planning doc's Progress Log + Final Outcome; fill the Decision Log with the checkpoint
  answers.
- Confirm in the doc that **no migration** was produced (explicit non-change) and whether any dependency
  was added (expected: none — OQ2a).
- Note the export abstraction seam (`IExportFormatter` + `ExportDocument`) for the M9 QR follow-on and for
  future Excel/JSON formats.

## Impact Analysis

**APIs:**
- **New routes on existing controllers:** `GET api/v1/expenses/{uuid}/export` (`ExpensesController`),
  `GET api/v1/events/{uuid}/export` (`EventsController`) — both return a file, not the JSON envelope, on
  success (OQ1a/OQ8a). No change to existing endpoints.

**Database:**
- **No schema change, no EF migration** (explicit non-change; OQ19). Pure read + format over `expenses`,
  `shares`, `members`, `categories`, `events`, and the M7 balance. Any future export-driven index would be
  a separate follow-up, not part of M8.

**Infrastructure:**
- **None.** No Redis, no background workers, and **NO new NuGet package** — OQ2a (hand-rolled RFC-4180
  writer) is locked, so no CSV library is added.

**Services:**
- **New:** `IExportService`/`ExportService` (`Services/Api/Export/`), the centralized
  `ExportValueFormatter` money/instant/calendar-date helper (OQ5a/OQ6b, `Services/Api/Export/`),
  `IExportFormatter` + `CsvExportFormatter` (`Services/Api/Export/Formatters/`), `Models/Export/*`
  (`ExportFormat`, `ExportDocument`/`ExportSection`, `ExportedFile`).
- **Modified:** `ExpensesController` + `EventsController` (add the `export` route + `IExportService`
  dependency); `ErrorCodes.cs` (reserve the 11xxx Export block, no new codes).
- **Reused (unchanged):** `IExpensesService`, `IStatsService` (M7 balance), `IEventsService`.

**UI:** none (API only).

**Documentation:** this planning doc; Vietnamese Swagger annotations on the two new endpoints. If a
`CLAUDE.md` controller-area list needs an "export" note, that is a doc-only follow-up (no code impact).

## Decision Log

> **Resolved at the 2026-07-14 user checkpoint — 19 of 20 Open Questions accepted at the recommended option
> (a); OQ6 accepted at option (b)** (local Vietnamese date format, overriding the ISO-UTC recommendation).
> One numbered point per OQ (binding decision + one-line reason). The full **Reason** and
> **Alternatives-Considered** for each are the options/trade-offs preserved inline under the matching OQ
> above.

1. **OQ1 — FileContentResult bypass (a):** export actions return `File(bytes, "text/csv; charset=utf-8",
   fileName)`; `[ResponseWrapped]` no-ops on non-`ObjectResult` while `ErrorException`s still wrap. *Reason:*
   clean, edit-free bypass of the LOCKED `AppController` that keeps the error envelope intact.
2. **OQ2 — Hand-rolled RFC-4180 writer (a):** no new dependency; quote on delimiter/quote/newline, double
   embedded quotes. *Reason:* honors the minimal-dependency ethos for a simple fixed-shape table.
3. **OQ3 — UTF-8 with BOM (a).** *Reason:* Excel (the primary consumer) renders Vietnamese correctly only
   with the BOM.
4. **OQ4 — Comma + CRLF (a).** *Reason:* RFC-4180 canonical, portable across tools/parsers.
5. **OQ5 — Raw invariant decimal `0.00` (a).** *Reason:* machine-readable, re-importable, no
   delimiter/locale collision; exact per §4.3.
6. **OQ6 — Local Vietnamese dates (b — USER OVERRODE the ISO-UTC recommendation):** instants →
   +7 `dd/MM/yyyy HH:mm`; the event calendar range → `dd/MM/yyyy` from the stored UTC date with NO +7
   shift; both centralized in one formatter and explicitly tested. *Reason:* user preference for local
   display; the split avoids the `23:59:59.999 UTC` end_date rolling to the next day.
7. **OQ7 — `ExportDocument` IR + `IExportFormatter` keyed by format (a).** *Reason:* the §5-mandated open
   design — new formats add a class, nothing else changes.
8. **OQ8 — Export sub-routes on existing controllers (a).** *Reason:* consistent with the shipped
   `events/{uuid}/balance` and `expenses/{uuid}/shares` idiom.
9. **OQ9 — `?format=csv` query param, default csv (a).** *Reason:* explicit, Swagger-visible, trivially
   extensible.
10. **OQ10 — Unsupported format → 400 `ValidationFailed` 1001 (a).** *Reason:* consistent with how bad
    input is surfaced; no new code needed.
11. **OQ11 — Reuse the application services (a).** *Reason:* free resource-owned 404, derived total, and M7
    balance; no duplicated query logic.
12. **OQ12 — Expense CSV = header block + per-member table + total row (a).** *Reason:* the natural
    human-readable §3.5 report; models cleanly onto the IR.
13. **OQ13 — Merged notes: one per member (expense); join with "; " + expense-name prefix (event);
    "(đã xóa)" suffix (a).** *Reason:* faithfully "gộp"s notes with provenance; §4.7 display for deleted
    members.
14. **OQ14 — Event CSV = one file, header + share-summary section + balance section, no expense list (a).**
    *Reason:* matches §3.6 exactly; the expense list is already its own endpoint.
15. **OQ15 — Per-member summary from the M7 balance `Owed` column (a).** *Reason:* reuses M7, keeps the two
    tables numerically consistent, no second aggregate.
16. **OQ16 — Export for both open and closed events (a).** *Reason:* export is read-only; §3.6/§4.4 only
    freeze writes.
17. **OQ17 — Single-expense + single-event export only (a).** *Reason:* matches §3.5/§3.6; list export is a
    future additive improvement.
18. **OQ18 — `expense-{uuid}-{yyyyMMdd}.csv` / `event-{slug}-{yyyyMMdd}.csv` with a sanitized slug (a).**
    *Reason:* predictable, filesystem-safe, no header-injection risk.
19. **OQ19 — Reserve 11xxx Export, no new codes (a).** *Reason:* reads only; misses reuse 6000/9000,
    unsupported format reuses 1001.
20. **OQ20 — No tier gate at M8 (a).** *Reason:* CSV is a Free feature and reads are never limit-gated
    (§4.9); leave a seam to gate Excel/JSON later.

**Implementation refinement (recorded at code review, 2026-07-14):** the export actions are annotated
`[Produces("text/csv", "application/json")]` (not the plan's suggested `text/csv` only) so the wrapped
error/404/400 `ApiResult` envelope has a matching JSON output formatter and never 406s on the failure path;
the success `FileContentResult` bypasses `Produces` regardless (it is a non-`ObjectResult`). No behavioral
change to the success CSV path.

**Inherited decisions (locked upstream — NOT reopened):** `AppController` + `[ResponseWrapped]` are LOCKED
(bypass without editing them); expense total = derived `SUM(shares)` (M5 OQ1); money `decimal(18,2)`,
non-negative, no float (§4.3); expenses/shares hard-deleted, members/categories soft-deleted (§4.7);
per-event balance semantics + sum-to-zero + `Owed`/`Advanced`/`Balance` rows incl. owner-rep and deleted
members (M7); event whole-day-inclusive **UTC boundary** date range, accepting the UTC-day-boundary
limitation (M6 OQ1) — directly relevant to OQ6's calendar-date rule; resource-owned 404-never-403 with
`ExpenseNotFound` 6000 / `EventNotFound` 9000 (§4.1); domain terms expense/share/event/settled/Premium-Free
(§5).

## Progress Log

### 2026-07-14

- Started planning M8 (Export CSV).
- Read the source of truth: `The-ideal.md` §3.5 (export phiếu), §3.6 (export đợt), §3.7 (the embedded
  balance table), §3.9, §4.1/§4.7/§4.9, and the §5 lock ("Export: CSV trước mắt, thiết kế mở cho định
  dạng khác"); `CLAUDE.md`; `.claude/rules/rule.md` template.
- Read prior planning docs: `debt-balance-and-stats.md` (M7 — the balance to reuse) and
  `expenses-shares-audit.md` / `events.md` context.
- **Grounded the response-wrapping bypass in the live code:** `Attributes/ResponseWrappedAttribute.cs`
  only rewraps `ObjectResult` results, so a `FileContentResult` (`File(...)`) streams out untouched, while
  `Attributes/MvcFilters/ErrorHandlerFilter` + `Middlewares/ErrorHandlerMiddleware` still wrap thrown
  `ErrorException`s — no `AppController` edit needed (OQ1a).
- Confirmed the reusable seams: `IStatsService.GetEventBalanceAsync` → `EventBalanceResponse`
  (advanced/owed/balance incl. owner-rep + deleted members) for the event's balance and per-member owed;
  `IExpensesService.GetAsync` → `ExpenseResponse` (shares + members + total + full incl.-deleted info) for
  the expense export; `IEventsService.GetAsync` for the event range header.
- Confirmed there is **no existing CSV/export library or pattern** in FairShareMon or quick-ordering
  (quick-ordering only has JWT multipart upload + static-file download middleware).
- Confirmed the next free `ErrorCodes` block is **11xxx** (10xxx reserved by Stats).
- Drafted the format-extensible design (`ExportDocument` IR + `IExportFormatter` keyed by format, single
  `CsvExportFormatter`), the two `export` routes, and the full test list.
- Wrote this planning doc with 20 Open Questions (each with options, trade-offs, and a recommended option
  (a)). Awaiting the user checkpoint before any implementation.
- **Checkpoint (2026-07-14):** the user answered all 20 Open Questions — **19 accepted at the recommended
  option (a); OQ6 accepted at option (b)** (local Vietnamese `dd/MM/yyyy HH:mm` / `dd/MM/yyyy` dates instead
  of ISO-UTC). Annotated every OQ inline with its binding answer, moved Assumptions to confirmed, filled the
  Decision Log (20 points + inherited-decisions block), and synced the Implementation Plan / endpoint table
  / CSV-format spec / test list. Spelled out the OQ6 date-formatting split (instants → +7
  `dd/MM/yyyy HH:mm`; event calendar range → `dd/MM/yyyy` from the stored UTC date, NO +7 shift) as a
  centralized `ExportValueFormatter` plus dedicated tests, since it is the milestone's most bug-prone point.
  Confirmed **no schema change / no EF migration and no new NuGet dependency**. Plan is unblocked —
  implementation can start.

### 2026-07-14 (implementation)

- Implemented M8 end-to-end per the locked decisions (19×a, OQ6×b). No schema change, **no EF migration**,
  **no new NuGet dependency** (hand-rolled RFC-4180 writer — OQ2a).
- **New files:**
  - `Models/Export/ExportFormat.cs` — `enum ExportFormat { Csv }` (OQ7).
  - `Models/Export/ExportDocument.cs` — the neutral IR: `ExportDocument { Title, Sections }` +
    `ExportSection { Name?, HeaderFields?, ColumnHeaders?, Rows }` (OQ7/OQ12/OQ14).
  - `Models/Export/ExportedFile.cs` — service output `{ Content, ContentType, FileName }`.
  - `Services/Api/Export/ExportValueFormatter.cs` — the centralized OQ5/OQ6 stringifiers (static, mirroring
    the `AppDateTime`/`Uuid` helper style): `FormatMoney` (`0.00` invariant), `FormatInstant`
    (UTC +7 fixed → `dd/MM/yyyy HH:mm`), `FormatCalendarDate` (`dd/MM/yyyy` from the stored UTC date, **no
    +7 shift**). The +7 offset is a fixed `TimeSpan.FromHours(7)` (never a machine `TimeZoneInfo`).
  - `Services/Api/Export/IExportFormatter.cs` — the format-keyed abstraction.
  - `Services/Api/Export/Formatters/CsvWriter.cs` — RFC-4180 escaping (quote iff field has `,`/`"`/CR/LF;
    double embedded quotes) + CRLF row join (OQ2/OQ4).
  - `Services/Api/Export/Formatters/CsvExportFormatter.cs` —
    `[ScopedService(typeof(IExportFormatter), Multiple = true)]`; renders sections (name row + header-field
    rows + blank-line + table), UTF-8 **with BOM** via `Encoding.UTF8.GetPreamble()` (OQ3);
    `ContentType = "text/csv; charset=utf-8"`, `FileExtension = "csv"`, `Format = Csv`.
  - `Services/Api/Export/ExportService.cs` — `IExportService`/`ExportService`
    (`[ScopedService(typeof(IExportService))]`, primary ctor injecting `IExpensesService`, `IStatsService`,
    `IEventsService`, `IEnumerable<IExportFormatter>` — OQ11). Builds the expense doc (header block +
    per-member share table sorted amount-desc/name + `Tổng cộng` row — OQ12) and the event doc (header +
    Section 1 per-member `Owed` summary + merged notes + Section 2 balance table with a sum-to-zero
    `Tổng cộng` — OQ14/OQ15); merged notes gathered by listing the event's expenses and joining each
    member's non-empty notes with `"; "` prefixed by the expense name (OQ13); deleted member/category →
    `(đã xóa)` suffix (§4.7); `ResolveFormatter` (default csv, unknown → `ValidationFailed` 1001 — OQ10);
    filenames `expense-{uuid}-{yyyyMMdd}.csv` / `event-{slug}-{yyyyMMdd}.csv` with an ASCII-folded slug
    (FormD diacritic strip + `đ`→`d`, non-`[a-z0-9]`→`-`, ≤40 chars, uuid fallback — OQ18).
- **Modified files:**
  - `Controllers/ExpensesController.cs` — injected `IExportService`; added
    `GET api/v1/expenses/{uuid}/export?format=csv` returning `File(...)` (OQ1/OQ8), Vietnamese Swagger,
    `[Produces("text/csv","application/json")]`, 200/400/401/404 responses.
  - `Controllers/EventsController.cs` — injected `IExportService`; added
    `GET api/v1/events/{uuid}/export?format=csv` (same shape; OPEN or CLOSED — OQ16).
  - `Constants/ErrorCodes.cs` — reserved the **11xxx Export** block with a comment; **no new codes** (OQ19).
  - `AppController.cs` was **NOT** touched (LOCKED) — the `File(...)` result is a non-`ObjectResult` so
    `[ResponseWrapped]` no-ops on it (verified live), while thrown `ErrorException`s still wrap.
- **Build/tests:** `dotnet build` clean (only the pre-existing AutoMapper NU1903 + a pre-existing test
  nullability warning). `dotnet test` = **664/664 passed, 0 skipped** (DB reachable). No test changed.
- **Live smoke (real API on :5299, DB+Redis up; smoke data cleaned up after):**
  - Expense export → HTTP 200, `Content-Type: text/csv; charset=utf-8`, `Content-Disposition: attachment;
    filename=expense-{uuid}-20260714.csv`, body starts with BOM `EF BB BF`, CRLF present, header block +
    per-member table + `Tổng cộng` row, all UNwrapped (not an `ApiResult`). Vietnamese rendered
    (`Ăn tối Đà Lạt`, `Ăn uống`).
  - **OQ6 date split verified live:** `expense_time` `2026-03-01T18:30:00Z` rendered `02/03/2026 01:30`
    (+7 applied, instant rolled forward correctly); the event range with `end_date`
    `2026-03-03T23:59:59.999999Z` rendered `01/03/2026 - 03/03/2026` — the end boundary shows its **own**
    calendar day `03/03/2026`, **NOT** `04/03/2026` (no +7 shift on the range). This is the milestone's
    most bug-prone point and it behaves exactly as decided.
  - Deleted member shows `Bình (đã xóa)` in both the expense table and both event sections (§4.7/OQ13).
  - Event export → two labeled sections; Section 2 balance matches M7 and the `Tổng cộng` Cân bằng = `0.00`;
    merged notes joined with expense-name prefix (`Ăn tối Đà Lạt: chủ trả`); filename slug
    `event-chuyen-di-da-lat-20260714.csv` (diacritics folded).
  - Money rendered `500000.00` / `300000.00` / `-200000.00` (invariant `0.00`, dot separator — OQ5).
  - `?format=xlsx` → **400**; another user's expense → **404**; another user's event → **404**; anonymous →
    **401**; **closed** event still exports → **200** (OQ16).
  - Smoke rows (users, members, categories, events, expenses, shares, audit logs) were removed from the dev
    DB afterward (verified 0 residue).

### 2026-07-14 (tests)

- **Test-engineer pass (test project only; NO production code changed).** Added the full M8 test set per the
  Step 7 definitive list plus judged edge cases. Suite went **664 → 724** (`+60` M8 tests); `dotnet test`
  from the repo root = **724 passed / 0 failed / 0 skipped** (MariaDB reachable). Run **twice** with
  identical results (determinism confirmed); the pre-existing 664 all still pass unchanged (M8 is additive —
  no regression). Post-run DB sweep verified **0 residue** in users/members/categories/tags/expenses/
  shares/events/audit_logs and **0 leftover test-prefix users**.
- **New test files (all in `FairShareMonApi.Tests/`):**
  - `CsvWriterTests.cs` (9, unit) — RFC-4180 escaping: a field is quoted iff it holds the comma delimiter /
    a `"` / CR / LF; embedded quotes doubled; plain field unquoted; null → empty; row joins escaped fields
    with the comma; `LineEnding` is CRLF (OQ2/OQ4).
  - `ExportValueFormatterTests.cs` (9, unit) — the OQ6 bug-prone split: `FormatInstant` shifts UTC→+7 and
    rolls `2026-03-01T18:30:00Z` forward to `02/03/2026 01:30` (+ midday / midnight cases);
    `FormatCalendarDate` on `2026-03-03T23:59:59.999999Z` → `03/03/2026` (**no +7, no day-roll**) and on the
    `00:00:00Z` start → same day; `FormatMoney` → invariant `0.00` incl. `-200000.00` and `266.67` (OQ5/OQ6).
  - `CsvExportFormatterTests.cs` (8, unit) — BOM `EF BB BF` prefix; `text/csv; charset=utf-8` / `csv` /
    `Format==Csv`; header-fields render `label,value`; table renders name + column headers + rows; sections
    blank-line separated; every line (incl. last) ends CRLF; in-context quoting; Vietnamese round-trips
    (OQ2/OQ3/OQ7).
  - `ExportServiceTests.cs` (19, unit) — over fake `IExpensesService`/`IStatsService`/`IEventsService` + a
    capturing `IExportFormatter` asserting the neutral `ExportDocument`: format resolution
    (null/`csv`/`CSV`/padded → CSV; `xlsx`/`json` → `ValidationFailed` 1001 before any render); expense doc
    (header block with +7 instant + `0.00` money + `(không thuộc đợt)` fallback; per-member table sorted
    amount-desc incl. owner-rep 0đ and a deleted member `(đã xóa)`; `Tổng cộng` = total); event doc (header
    calendar range no-shift + open/closed status; Section 1 owed + merged notes; Section 2 balance with
    sum-to-zero `Tổng cộng`; deleted suffix in both sections); merged-notes join with the expense-name
    prefix; `ExpenseNotFound`/`EventNotFound` propagation; filename shape + slug (diacritics folded, empty
    name → uuid fallback) (OQ10/OQ11/OQ12/OQ13/OQ14/OQ15/OQ18).
  - `ExportEndpointTests.cs` (12, integration HTTP, skippable) — success path: 200 + `text/csv; charset=utf-8`
    + `Content-Disposition: attachment; filename=…` + body starting with the BOM and **not** parsing as an
    `ApiResult` envelope (the `[ResponseWrapped]` bypass, OQ1); default and `?format=CSV` both CSV; closed
    event → 200 (OQ16); wrapped-envelope edges: unsupported format → 400 (1001), another-user's / unknown
    expense → 404 (6000), event → 404 (9000) never 403, anonymous → 401.
  - `ExportContentTests.cs` (3, integration HTTP, skippable) — seeds over the M3–M7 endpoints then decodes
    the exported CSV over the real DB: expense shows the per-member share rows (owner-rep `Tôi` 0đ, a
    soft-deleted member `Bình (đã xóa)` + note, a soft-deleted category `(đã xóa)`), the derived total row,
    and the +7 `expense_time` (`18:30Z` → `16/07/2026 01:30`); event shows the calendar range
    `14/07/2026 - 16/07/2026` (**end boundary keeps its own day**), Section 1 owed + merged note
    (`Ăn tối: chia đều`), Section 2 balance matching `GetEventBalanceAsync` (Bình `800000.00,500000.00,
    300000.00`; Cường `-500000.00`) with a sum-to-zero `Tổng cộng`, deleted-member display, and open+closed
    both exporting.
- **Cleanup strategy:** all DB-touching tests are `[SkippableFact]` under `[Collection("AuthIntegration")]`
  on `ExpenseApiTestBase`; unique per-class username prefix + dispose-time cascade sweep (expenses first to
  clear RESTRICT FKs, then events hard-deleted, then `audit_logs` by actor). The closed-event caveat is
  handled correctly — the sweep deletes expenses at the DB level (`ExecuteDeleteAsync`), never via the API,
  so a closed event never blocks teardown.
- **Production bugs found:** **none.** All export behavior (the OQ6 date split, the BOM, the
  `[ResponseWrapped]` bypass, the resource-owned 404s, the sum-to-zero balance, the merged notes, the
  slugged filename) behaves exactly as specified. **No production code was modified.**

### 2026-07-14 (code review — APPROVED, 0 blocking — milestone closed)

- **Verdict: APPROVE — 0 blocking findings, 5 informational notes (no code changes required).** Suite
  **724/724** (passed / 0 failed / 0 skipped), deterministic, dev DB swept clean.
- **Verified checks:**
  - **OQ6 date split correct.** `FormatInstant` is a fixed +7 via `utc.Add(TimeSpan.FromHours(7))` — no
    DST, no `TimeZoneInfo`, no double-convert — and is applied only to `expense.ExpenseTime` (instants);
    `FormatCalendarDate` applies **no shift** and is used only for the event `start`/`end`, so an
    `end_date` of `23:59:59.999999Z` renders its own calendar day with no mix-up between the two rules.
  - **`FileContentResult` bypass confirmed.** `ResponseWrappedAttribute` early-returns on a
    non-`ObjectResult`, so the raw CSV (+ UTF-8 BOM) streams unwrapped, while thrown `ErrorException`s
    still get the `ApiResult` envelope; `AppController` is untouched (LOCKED honored).
  - **Format-extensibility clean.** The `ExportDocument` IR is string-only; `IExportFormatter` is keyed by
    the `ExportFormat` enum and registered `Multiple = true`; the resolver throws `ValidationFailed` (1001)
    **before** any data fetch on an unsupported format.
  - **RFC-4180 correct.** Quote-on-comma/quote/CR/LF, doubled embedded quotes, CRLF row endings, UTF-8 BOM,
    `text/csv; charset=utf-8`.
  - **Resource-owned + M7 reuse.** Section 1 (owed) and Section 2 (balance) both come from the same
    `balance.Rows` — no recompute; misses map to 6000/9000 (never 403).
  - **Content correct.** `FormatMoney` invariant `0.00` incl. negatives; `(đã xóa)` for soft-deleted
    members/categories (§4.7); merged notes joined `"; "` prefixed by the expense name; expense total row;
    event two sections; diacritic-folded filename slug with no `Content-Disposition` injection.
  - **NO migration / NO new dependency** — `.csproj` unchanged, AutoMapper still **13.0.1** pinned.
  - Conventions clean (layering, DiDecoration attributes, `Async`+`CancellationToken`, Vietnamese
    user-facing strings/Swagger).
- **Final state confirmed:** `dotnet test` = **724 passed / 0 failed / 0 skipped**, deterministic (run
  twice), dev DB swept clean; build clean; live smoke + code review = **APPROVE**.
- **Reviewer's 5 informational notes (no change required; recorded for fidelity):**
  1. **CSV formula-injection hardening** — `CsvWriter.EscapeField` does not neutralize fields beginning
     with `=`/`+`/`-`/`@` (Excel formula-injection). Low risk in the current single-tenant-export model (a
     user exports only their own ledger), but event CSVs are meant to be shared with the group → a future
     hardening should prepend a `'`/tab to formula-leading **text** fields only, **never** numeric money
     fields (prefixing `-500.00` would break OQ5 raw-decimal re-importability). Flagged for the user to
     decide, possibly bundled with M10 sharing/tiers. → Future Improvements.
  2. **`GatherMergedNotesAsync` N+1** — one `ListAsync` + a `GetAsync` per event expense; a single bulk
     notes read would remove the O(#expenses) round-trips (mirrors the M7 aggregation-efficiency theme). →
     Future Improvements.
  3. Export actions use `[Produces("text/csv", "application/json")]` (not the plan's suggested `text/csv`
     only) so the wrapped error/404/400 envelope has a matching JSON output formatter (avoids a 406 on the
     error path); the success `FileContentResult` bypasses `Produces` regardless. → Decision Log note.
  4. (informational) The centralized `ExportValueFormatter` cleanly isolates the OQ5/OQ6 rules — a good seam
     for future formats; no action.
  5. (informational) The `ExportDocument` IR + `Multiple = true` formatter registration is the exact
     §5-mandated open design; adding Excel/JSON is a single formatter class; no action.
- Milestone 8 is **closed and ready to commit.**

## Final Outcome

Milestone 8 (Export CSV) is **complete, reviewed (APPROVE — 0 blocking), and ready to commit.** Both
endpoints ship — `GET api/v1/expenses/{uuid}/export?format=csv` (expense export = header block + per-member
share table + total row) and `GET api/v1/events/{uuid}/export?format=csv` (event export = header +
per-member share summary + the M7 debt-balance table, no per-expense list) — returning UTF-8-with-BOM,
RFC-4180, CRLF CSV files (comma delimiter) via a `FileContentResult` that bypasses `[ResponseWrapped]`
without editing the LOCKED `AppController`; resource-owned misses and unsupported formats still return the
wrapped `ApiResult` envelope (404 6000/9000, 400 1001), the actions carrying
`[Produces("text/csv", "application/json")]` so the error path never 406s.

The format-extensible seam is in place: a neutral, string-only `ExportDocument` IR built once by
`IExportService` (money via `FormatMoney`, dates via `FormatInstant`/`FormatCalendarDate`, all centralized
in `ExportValueFormatter`), rendered by `IExportFormatter` keyed on the `ExportFormat` enum and registered
`Multiple = true` behind the shared `CsvWriter`/`CsvExportFormatter` — adding Excel/JSON later is a single
new formatter class with no controller/service/DTO change (and a clean seam to gate those as Premium —
OQ20; no tier gate at M8). Data is reused from the M5/M7 services (derived total, the balance rows for both
event sections — no recompute). The two OQ6 date rules are centralized and verified live and by unit tests
(instant fixed +7 `dd/MM/yyyy HH:mm` vs calendar-date `dd/MM/yyyy` no-shift — an `end_date` at
`23:59:59.999999Z` keeps its own day). Soft-deleted members/categories still display with `(đã xóa)` (§4.7);
merged notes join with the expense-name prefix; filenames use a diacritic-folded slug. The **11xxx Export**
error block is reserved (no new codes defined).

**No schema change, no EF migration, no new NuGet dependency** (`.csproj` unchanged, AutoMapper still
13.0.1 pinned). `dotnet build` clean; `dotnet test` = **724 passed / 0 failed / 0 skipped** (deterministic,
run twice), dev DB swept clean; live smoke + code review = APPROVE. No deviations from the locked decisions;
no new Open Questions. Two informational follow-ups (CSV formula-injection hardening; `GatherMergedNotesAsync`
N+1) are recorded under Future Improvements.

## Future Improvements

- **CSV formula-injection hardening (from code review, 2026-07-14).** `CsvWriter.EscapeField` does not
  neutralize fields that begin with `=`/`+`/`-`/`@` (Excel formula-injection). Low risk in the current
  single-tenant-export model (a user exports only their own ledger data), but **event CSVs are meant to be
  shared with the group**, so a future hardening should prepend a `'`/tab to formula-leading **text** fields
  only — **WITHOUT** touching numeric money fields (prefixing `-500.00` would break OQ5 raw-decimal
  re-importability, so any guard must be text-field-only). Flagged at the M8 checkpoint for the user to
  decide, possibly bundled with M10 sharing/tiers.
- **`GatherMergedNotesAsync` N+1 (from code review, 2026-07-14).** The event export does one `ListAsync` +
  a `GetAsync` per event expense to collect merged notes; a single bulk notes read would remove the
  O(#expenses) round-trips (mirrors the M7 aggregation-efficiency theme).
- **Additional formats (Excel/JSON)** as new `IExportFormatter` implementations behind the same
  `ExportDocument` IR — the §5-mandated extension point; gated as the Premium "mở rộng" set (§3.11/OQ20).
- **Bulk / filtered-list export** (e.g. all expenses matching a filter, or a whole ledger) reusing the
  same abstraction (OQ17b).
- **Streaming large exports** via `FileStreamResult` / `IAsyncEnumerable` if event/expense volumes grow
  (the abstraction can return a stream instead of a `byte[]`).
- **User-selectable CSV dialect** (delimiter, BOM, number/date locale) as query params for non-Excel
  consumers (OQ4c/OQ5b).
- **Localized/decorated filenames** via RFC 5987 `filename*` with the real Vietnamese name (OQ18b).
