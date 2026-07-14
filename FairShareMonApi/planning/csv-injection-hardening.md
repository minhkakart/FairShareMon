# CSV Injection Hardening (Milestone 8 follow-up)

## Objective

Neutralize **CSV/Excel formula injection** in the M8 CSV export, hardening the shipped hand-rolled CSV
writer against fields that begin with a spreadsheet formula trigger (`=`, `+`, `-`, `@`, TAB, CR). This is a
focused security chore requested by the user at the 2026-07-14 checkpoint, to be done **before Milestone 9**.

Hard constraint carried from M8 OQ5 and the code-review note: the guard must protect **TEXT fields only**;
**numeric money cells must stay byte-identical** — a negative money value such as `-500000.00` must NOT be
mangled, because M8 OQ5 locked raw re-importable invariant decimals (`800000.00` / `-500.00`).

## Background

- M8 (Export CSV) shipped, reviewed **APPROVE — 0 blocking**, suite at **724/724**. Its code-review recorded
  one informational note (note 1) and a Future Improvements entry: `CsvWriter.EscapeField` does **not**
  neutralize formula-leading fields; event CSVs are meant to be **shared with the group**, so a hardening
  should prepend a guard char to formula-leading **text** fields only, **never** numeric money (prefixing
  `-500.00` would break OQ5 raw-decimal re-importability). This chore executes exactly that note.
- **Live code, grounded:**
  - `Services/Api/Export/Formatters/CsvWriter.cs` — `EscapeField(string?)` is the single choke point every
    cell passes through (`FormatRow` → `EscapeField`). Today it only applies RFC-4180 quoting: quote iff the
    field contains `,` / `"` / CR / LF, doubling embedded `"`. No formula neutralization.
  - `Services/Api/Export/Formatters/CsvExportFormatter.cs` — builds all rows (section name, header
    `label,value` rows, column headers, data rows) through `CsvWriter.FormatRow`, then UTF-8-with-BOM. Every
    cell — text and money alike — flows through `EscapeField`.
  - `Services/Api/Export/ExportService.cs` — builds the string-only `ExportDocument`. Money is stringified
    via `ExportValueFormatter.FormatMoney` (`0.00`, `800000.00`, `-500000.00`); dates via `FormatInstant`
    (`dd/MM/yyyy HH:mm`, +7) and `FormatCalendarDate` (`dd/MM/yyyy`, no shift). Text cells are member/category
    names (with the `(đã xóa)` suffix for soft-deleted), notes/merged notes, event name, expense
    name/description, tags.
  - `Models/Export/ExportDocument.cs` — the IR is **string-only** (`ExportSection.HeaderFields`,
    `ColumnHeaders`, `Rows` are all strings). There is currently **no way to distinguish "text" from
    "already-formatted number"** at the formatter layer — this is the core design tension.
  - The **Section 2 (Cân bằng nợ)** balance table legitimately contains negative money (`Balance =
    Advanced − Owed`), e.g. `-500000.00`. Existing test `ExportContentTests` asserts `Cường -500000.00` and
    `Bình 800000.00,500000.00,300000.00`. **Any naive "prefix all leading `-`" guard would break these
    green tests and corrupt money** — which is exactly why the guard must be numeric-safe.
- Money is `decimal`, non-negative at the share/DB-CHECK level (§4.3), but derived balance columns can be
  negative — so "money can start with `-`" is real, not hypothetical.

## Requirements

- Neutralize Excel/Sheets formula injection on exported CSV **text** cells: a cell whose first character is a
  formula trigger must not be interpreted as a formula when the file is opened in Excel/Google Sheets/
  LibreOffice.
- **Never alter numeric money cells** — `-500000.00`, `0.00`, `800000.00`, `266.67` must render byte-identical
  to today (OQ5 re-importability preserved).
- **Never alter date cells** — `14/07/2026`, `02/03/2026 01:30` (leading digit, inherently safe).
- Preserve every M8-locked behavior: UTF-8 BOM (OQ3), comma + CRLF (OQ4), RFC-4180 quoting (OQ2), money
  format (OQ5), the two date rules (OQ6), the `FileContentResult` bypass (OQ1), resource-owned 404s.
- **No EF migration, no schema change, no new NuGet dependency, no new error code, no new message key** —
  this is a pure formatter change.
- The existing **724 tests stay green**; new tests are added for the guard.

## Open Questions

> **All four answered by the user at the 2026-07-14 checkpoint — every one at the recommended option (a).**
> The binding answer is annotated inline under each question; the full options/trade-offs are preserved for
> the record and mirrored in the Decision Log. No open questions remain — implementation is unblocked. The
> Implementation Plan and test list below are synced to these answers.

**OQ1 — Where the guard lives / how text-vs-numeric is distinguished.**
> ~~**OQ1**~~ → **Answered 2026-07-14 (option a):** guard inside `CsvWriter.EscapeField` (the single choke
> point) with a **numeric-safe heuristic** — neutralize when the field starts with `=`, `@`, TAB (0x09), or
> CR (0x0D) **always**, and when it starts with `+` or `-` **only if the whole field is NOT a parseable
> invariant-culture decimal** (so `-500000.00` / `0.00` / `800000.00` stay raw; `-cmd` / `+cmd` /
> `=SUM(...)` / `@x` are neutralized). Guard runs BEFORE the existing RFC-4180 quoting. No IR change;
> `CsvExportFormatter` / `ExportService` / `ExportValueFormatter` untouched.
- **(a) [recommended] Guard inside `CsvWriter.EscapeField` using a numeric-safe heuristic.** Neutralize a
  field only when it starts with a dangerous char AND is not a valid number: leading `=`, `@`, TAB (0x09),
  CR (0x0D) are **always** guarded; leading `+` / `-` are guarded **only when the whole field is not a
  parseable invariant decimal** (so `-500000.00` / `0.00` / `800000.00` are left raw, while `-cmd` / `+cmd`
  / `=SUM(...)` / `@x` are neutralized). Self-contained (one method), no IR change, and every current AND
  future CSV cell is protected at the single choke point every row already passes through. *Trade-off:* it is
  a heuristic — a **text** value that happens to look like a bare number (e.g. a note `"+84901234567"`)
  would not be guarded; this is harmless (Excel just shows the number, no data exfiltration) and is the
  explicitly accepted cost.
- **(b) Add a typed/text-flagged cell to the `ExportDocument` IR** (mark cells Text vs Raw/Numeric) and guard
  only Text cells in the formatter. Cleanest separation, future-formatter-friendly (the flag benefits an
  Excel/JSON formatter too), and removes the heuristic. *Trade-off:* churn across the IR
  (`ExportSection`/cell type) + every `ExportService` cell-construction site (header fields, share rows,
  summary rows, balance rows), and a bigger diff for a chore; a forgotten flag on a new field silently loses
  protection.
- **(c) Guard at the `ExportService` layer, only on the known text fields** (names/notes/event name/
  description/tags) before they enter the IR. Localized. *Trade-off:* scatters a security concern across many
  call sites; a text field added later can be forgotten, and it is a security-by-omission model.

**OQ2 — Neutralization method (how to defang a dangerous text field).**
> ~~**OQ2**~~ → **Answered 2026-07-14 (option a):** neutralize by **prefixing a single-quote `'`**; the
> neutralized field then passes through the unchanged RFC-4180 quoting (so `=1,2` → `"'=1,2"`).
- **(a) [recommended] Prefix a single-quote `'`.** The classic Excel guard: Excel treats a leading `'` as
  "the rest is literal text" and does not display the quote; the field then passes through normal RFC-4180
  quoting (a neutralized field containing `,`/`"`/newline is still wrapped, e.g. `=1,2` → `"'=1,2"`).
  *Trade-off:* in a non-Excel parser the `'` becomes a visible part of the value — acceptable because the
  guard is text-only (names/notes), not the money that OQ5 requires to re-import cleanly.
- **(b) Prefix a TAB (`\t`).** Invisible-ish in Excel and keeps the leading char literal. *Trade-off:* adds
  whitespace to the value, is less universally understood than `'`, and (being CR/TAB-adjacent) is itself on
  some injection char lists — muddier semantics.
- **(c) Wrap in quotes only / different scheme.** *Trade-off:* RFC-4180 quoting alone does NOT stop formula
  injection (Excel strips the quotes and still evaluates), so quoting is insufficient; a distinct prefix is
  required.

**OQ3 — Scope of fields guarded (confirmation).**
> ~~**OQ3**~~ → **Answered 2026-07-14 (option a):** scope = all user-controlled TEXT cells (member names
> incl. the `(đã xóa)` form, category names, notes/merged notes, event name, expense name/description, tags);
> money (numeric heuristic) and dates (leading digit) are naturally excluded; app-generated Vietnamese
> labels start with letters → untouched.
- **(a) [recommended] Guard all user-controlled TEXT cells; exclude money and dates.** With OQ1(a) the guard
  is applied universally at `EscapeField` but is numeric-safe, so it **naturally**: (i) protects member names
  (incl. the `(đã xóa)` suffix form), category names, notes/merged notes, event name, expense
  name/description, and tags; (ii) leaves **money** untouched (numeric heuristic); (iii) leaves **dates**
  untouched (leading digit). App-generated Vietnamese labels/headers (`Tên phiếu`, `Cân bằng nợ`, …) start
  with letters and are also untouched. Confirm this scope. *Trade-off:* none beyond OQ1(a)'s heuristic note.
- **(b) Restrict the guard to an explicit allow-list of text columns.** Only meaningful if OQ1 = (b)/(c);
  redundant under OQ1(a).

**OQ4 — Does the guard generalize to future formatters (Excel/JSON)?**
> ~~**OQ4**~~ → **Answered 2026-07-14 (option a):** CSV-path-only — a future Excel formatter sets real cell
> types (text vs number) via its library; a JSON formatter is immune. No IR-level flag now.
- **(a) [recommended] Keep it CSV-path-only (consistent with OQ1(a)).** The guard lives in `CsvWriter` and
  applies only to CSV. A future **Excel** (`.xlsx`) formatter would set real cell types (text vs number) via
  its library and re-handle injection there; a future **JSON** formatter is immune (no formula
  interpretation). Confirm that CSV-only scope is acceptable now. *Trade-off:* the injection concern is
  re-solved per binary/spreadsheet format later, rather than once at the IR.
- **(b) Solve it once at the IR (ties to OQ1(b)).** The Text/Numeric flag would let every present and future
  formatter guard consistently from one signal. *Trade-off:* only worth the IR churn if OQ1 = (b).

**Cross-cutting confirmations** → **Confirmed 2026-07-14:** NO migration / NO schema change; NO new NuGet
dependency; NO new error code (11xxx stays reserved-only); NO new message key (the guard is silent
formatting, never surfaced to the user); the existing **724 tests stay green**; and the money / date / BOM /
comma+CRLF / RFC-4180 behavior locked in M8 is **unchanged**.

## Assumptions

> **Confirmed by the user at the 2026-07-14 checkpoint** together with the four Open Questions — these are
> now decisions, not vetoable assumptions.

- The dangerous leading-char set is `=`, `+`, `-`, `@`, TAB (0x09), CR (0x0D) — the standard OWASP CSV-
  injection set. A leading LF (0x0A) is already always quoted by the existing RFC-4180 rule and is not part
  of the classic formula-trigger set; it is not separately guarded (fold into the recommendation if the user
  wants it added).
- The numeric-safe test uses `decimal.TryParse` with **invariant culture** and a tight
  `NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint` — matching the exact `FormatMoney`
  shape (`-?\d+\.\d\d`, no grouping, no whitespace) — so only genuine money-shaped fields escape the guard.
- The guard is applied **before** the existing RFC-4180 quote decision, so a neutralized field that also
  contains a delimiter/quote/newline is still quoted correctly.
- The change is contained to the CSV export path; nothing else in the app calls `CsvWriter`.

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/`. Synced to the locked decisions (option (a) for every
> OQ, resolved 2026-07-14).
> **NO migration, NO new entity/DbSet, NO `.csproj` change, NO new error code, NO new message key.**

### Step 1 — Harden `CsvWriter.EscapeField` (the only production change)

`Services/Api/Export/Formatters/CsvWriter.cs`:
- Add a private constant for the guard prefix `'` (OQ2a) and a `static readonly char[]`/set of the
  always-dangerous leading chars `{ '=', '@', '\t', '\r' }`, plus the conditionally-dangerous `{ '+', '-' }`.
- Add a private `NeutralizeFormula(string field)` helper:
  1. Return `field` unchanged if empty.
  2. Let `first = field[0]`.
  3. If `first` is `=`, `@`, TAB, or CR → return `guardPrefix + field`.
  4. If `first` is `+` or `-` → return `field` unchanged when
     `decimal.TryParse(field, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
     CultureInfo.InvariantCulture, out _)` (money-shaped → keep raw); otherwise `guardPrefix + field`.
  5. Otherwise return `field` unchanged.
- In `EscapeField`: call `field = NeutralizeFormula(field ?? string.Empty);` **first**, then run the
  existing unchanged RFC-4180 quote logic on the (possibly prefixed) value. No other method changes;
  `FormatRow` is untouched (it already routes through `EscapeField`).
- Update the class XML-doc to note the numeric-safe formula guard (Vietnamese, matching the existing style).
- `CsvExportFormatter`, `ExportService`, `ExportDocument`, `ExportValueFormatter` are **unchanged**.

### Step 2 — Tests (owned by the test-engineer; extend the M8 unit + integration files)

**Unit — extend `FairShareMonApi.Tests/CsvWriterTests.cs`** (the primary coverage):
- Guarded, always: `EscapeField("=SUM(A1)")` → `'=SUM(A1)`; `"@cmd"` → `'@cmd`; a TAB-leading field →
  `'\t…`; a CR-leading field → prefixed AND quoted (`"'\r…"`, since it now contains CR).
- Guarded, conditional: `"-cmd"` → `'-cmd`; `"+cmd"` → `'+cmd`; `"=1,2"` → `"'=1,2"` (guard + RFC-4180
  quoting interaction).
- **Money preserved (the critical cases):** `"-500000.00"` → `-500000.00` (unchanged); `"0.00"` → `0.00`;
  `"800000.00"` → `800000.00`; `"266.67"` → `266.67`. These prove the negative-money OQ5 constraint.
- **Dates preserved:** `"14/07/2026"` and `"02/03/2026 01:30"` → unchanged (leading digit).
- **Heuristic trade-off documented in a test:** `"+84901234567"` → unchanged (parses as a number — the
  accepted harmless gap).
- Plain text unchanged: `"Bình"`, `"Bình (đã xóa)"` → unchanged; `null` → empty.

**Unit — extend `FairShareMonApi.Tests/CsvExportFormatterTests.cs`:**
- A document whose header-field value is `=HYPERLINK("http://x")` renders neutralized (`'=HYPERLINK…`);
- A data row containing a money cell `-500000.00` renders that cell raw (formatter-level regression that the
  guard does not touch money flowing through `Render`).

**Integration (real MariaDB) — extend `FairShareMonApi.Tests/ExportContentTests.cs`:**
- Seed (over the M3–M7 endpoints) an expense/event where a **member name** or **note** begins with `=`, `@`,
  or `-cmd`; assert the exported CSV neutralizes those text cells (leading `'`) while the **Section 2 balance
  money** (e.g. `-500000.00`) remains raw and the sum-to-zero `Tổng cộng` is intact.
- Confirm the pre-existing balance assertions (`Cường -500000.00`, `Bình 800000.00,500000.00,300000.00`)
  still pass unchanged — the regression guarantee.

**Regression:** run the full suite; all pre-existing **724** must stay green (the change is additive/
numeric-safe). Report the new total.

### Step 3 — Wrap-up

- Update this doc's Progress Log + Final Outcome; fill the Decision Log with the checkpoint answers.
- Confirm in the doc: no migration, no dependency, no new error code/message key; suite green.
- Move the M8 "CSV formula-injection hardening" Future Improvements bullet to "done, see
  `csv-injection-hardening.md`" (doc-only cross-link; no code change to `export-csv.md` behavior).

## Impact Analysis

- **APIs:** none. Same routes (`GET api/v1/expenses/{uuid}/export`, `GET api/v1/events/{uuid}/export`), same
  DTOs, same `FileContentResult` bypass, same content type / disposition. Only the bytes of text cells
  beginning with a formula trigger change.
- **Database:** none. No schema change, **no EF migration**, no new query.
- **Infrastructure:** none. No Redis, no background worker, **no new NuGet package** (`.csproj` unchanged;
  AutoMapper stays 13.0.1 pinned).
- **Services:** one production file touched — `Services/Api/Export/Formatters/CsvWriter.cs` (add
  `NeutralizeFormula` + wire into `EscapeField`). `CsvExportFormatter`, `ExportService`, `ExportDocument`,
  `ExportValueFormatter` unchanged. (Under OQ1(b) the surface would widen to `ExportDocument` + every
  `ExportService` cell site — noted for the checkpoint.)
- **Error codes / messages:** none new. 11xxx Export block stays reserved-only; no new message key (silent
  formatting).
- **Documentation:** this planning doc; a one-line cross-link update in `export-csv.md` Future Improvements.

## Decision Log

> **Resolved at the 2026-07-14 user checkpoint — all 4 Open Questions accepted at the recommended option
> (a).** One line per decision (binding decision + reason); the full options/trade-offs are preserved inline
> under each OQ above.

1. **OQ1 — Guard in `CsvWriter.EscapeField` with a numeric-safe heuristic (a):** `=`/`@`/TAB/CR always
   guarded; `+`/`-` guarded only when the whole field is NOT a parseable invariant decimal; runs before the
   RFC-4180 quoting; no IR change. *Reason:* the single choke point protects every current and future CSV
   cell in one method, while the numeric heuristic keeps money (incl. negative balances) byte-identical.
2. **OQ2 — Neutralize by prefixing a single-quote `'` (a):** the guarded field then flows through the
   unchanged quoting (`=1,2` → `"'=1,2"`). *Reason:* the classic, widely-recognized Excel guard; text-only
   so it does not affect OQ5 money re-importability.
3. **OQ3 — Scope = all user-controlled TEXT cells; money + dates excluded (a):** the numeric heuristic
   excludes money, leading digits exclude dates, and letter-leading app labels are untouched. *Reason:*
   protects every user text field (names/notes/event name/description/tags) without a per-field allow-list.
4. **OQ4 — CSV-path-only (a):** no IR-level Text/Numeric flag now; a future Excel formatter sets real cell
   types, JSON is immune. *Reason:* minimal surface for a chore; the IR flag (OQ1b) is deferred to whenever
   Excel/JSON formatters land.
5. **Cross-cutting (confirmed):** NO migration / schema change / new NuGet dependency / new error code (11xxx
   stays reserved-only) / new message key; the existing **724 tests stay green**; M8-locked
   money/date/BOM/comma+CRLF/RFC-4180 behavior unchanged.

**Inherited decisions (locked upstream — NOT reopened):** M8 **OQ5** — money is raw invariant decimal
(`800000.00`, and negative balances like `-500000.00`), machine-re-importable, so the guard must never touch
a numeric money cell (the load-bearing constraint that dictates OQ1's numeric-safe heuristic); M8 OQ2
(hand-rolled RFC-4180 writer, no library), OQ3 (UTF-8 BOM), OQ4 (comma + CRLF), OQ6 (date split), OQ7
(string-only `ExportDocument` IR + `IExportFormatter`); `AppController` + `[ResponseWrapped]` LOCKED; domain
terms expense/share/event/settled/Premium-Free.

## Progress Log

### 2026-07-14

- Started planning the CSV formula-injection hardening (M8 follow-up, requested at the 2026-07-14 checkpoint
  to land before M9).
- Read the M8 planning doc `export-csv.md` in full (OQ2 hand-rolled RFC-4180 writer, OQ3 UTF-8 BOM, OQ4
  comma+CRLF, OQ5 raw invariant decimal money incl. negatives, OQ6 date split, OQ7 `ExportDocument` IR +
  `IExportFormatter`), including the code-review informational note 1 and the Future Improvements entry that
  scopes this hardening (text-only, never numeric money).
- Read the live code: `CsvWriter.cs` (the `EscapeField` choke point to modify), `CsvExportFormatter.cs`,
  `ExportService.cs`, `ExportDocument.cs`, `ExportValueFormatter.cs`, and the M8 tests `CsvWriterTests.cs`,
  `CsvExportFormatterTests.cs`, `ExportContentTests.cs`. Confirmed the IR is string-only (no text/numeric
  distinction) and that Section 2 balance money can be negative (`-500000.00`), which existing tests assert —
  the exact reason the guard must be numeric-safe.
- Drafted the plan around a numeric-safe guard in `EscapeField` (OQ1a) prefixing `'` (OQ2a), text-only via
  the numeric heuristic (OQ3a), CSV-path-only (OQ4a), with the four decisions surfaced as Open Questions.
- Wrote this planning doc with 4 Open Questions (each options + trade-offs + a recommended option (a)).
  Awaiting the user checkpoint before any implementation.
- **Checkpoint (2026-07-14):** the user answered all 4 Open Questions — **every one accepted at the
  recommended option (a)** (numeric-safe guard in `CsvWriter.EscapeField`; single-quote `'` prefix; text-only
  scope; CSV-path-only). Confirmed the cross-cutting non-changes (no migration / schema / dependency / error
  code / message key; 724 tests stay green; M8-locked behavior unchanged). Annotated every OQ inline with its
  binding answer, moved Assumptions to confirmed, filled the Decision Log (4 decisions + cross-cutting +
  inherited-decisions block referencing the M8 OQ5 money constraint), and synced the Implementation Plan +
  test list. The load-bearing regression — `-500000.00` / `0.00` / `800000.00` money cells left raw while the
  existing M8 balance-table negative-money assertions still pass — is called out explicitly. Plan is
  unblocked — implementation can start.

- **Implemented (2026-07-14):** Hardened `Services/Api/Export/Formatters/CsvWriter.cs` (the ONLY production
  file changed). Added a private `const char FormulaGuardPrefix = '\''` and a private
  `NeutralizeFormula(string field)` helper wired as the first step of `EscapeField` (before the unchanged
  RFC-4180 quoting). Heuristic (OQ1a/OQ2a): empty → unchanged; first char `=`/`@`/TAB(0x09)/CR(0x0D) →
  always prefix `'`; first char `+`/`-` → prefix `'` only when NOT
  `decimal.TryParse(field, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
  CultureInfo.InvariantCulture, out _)` (so `-500000.00`/`0.00`/`800000.00` stay raw; `-cmd`/`+cmd` guarded);
  any other first char → unchanged. Added `using System.Globalization;` and updated the class XML-doc
  (Vietnamese). `CsvExportFormatter`, `ExportService`, `ExportValueFormatter`, and the `ExportDocument` IR are
  untouched. NO migration / schema change / new NuGet dependency / new error code / new message key. Build
  clean; full suite **724/724** green with the M8 `ExportContentTests` negative-money balance assertions
  (`Cường -500000.00`, `Bình 800000.00,500000.00,300000.00`) still passing — money preservation confirmed.

- **Tested (2026-07-14, test-engineer):** Added CSV-injection-hardening coverage across the three M8 test
  files; no production code touched. Full suite **761/761 green** (724 pre-existing unchanged + 37 new
  cases), **0 skipped** (MariaDB + Redis reachable), deterministic across two consecutive runs. Post-run DB
  sweep confirmed clean (0 leftover test users, 0 `=cmd`/`=evil` members).
  - **Unit — `CsvWriterTests.cs` (+18 cases):** always-guarded triggers `=SUM(A1)`→`'=SUM(A1)`,
    `@cmd`→`'@cmd`, leading TAB→`'\tx`, leading CR→prefixed **and** RFC-4180-quoted (`"'\rx"`);
    conditional `+`/`-` text (`-cmd`, `+cmd`, `-1+2)`, `=1+1`, a `=cmd|'/C calc'!A0` DDE payload) guarded;
    guard×RFC-4180 interaction (`=1,2`→`"'=1,2"`, `=a"b`→`"'=a""b"`); **money preserved (load-bearing):**
    `-500000.00`/`0.00`/`800000.00`/`500000.00`/`300000.00`/`-0.01`/`123.45`/`266.67` returned raw;
    dates (`14/07/2026`, `02/03/2026 01:30`) untouched; documented heuristic gap `+84901234567` left raw;
    non-dangerous/Vietnamese text (`Tôi`, `Bình`, `Bình (đã xóa)`, `Ăn tối: chia đều`), empty string, and
    interior-trigger text (`a=b`/`a+b`/`a-b`/`a@b`) unchanged; `FormatRow` neutralizes a formula cell while
    leaving a `-500000.00` cell raw in the same row.
  - **Unit — `CsvExportFormatterTests.cs` (+3 cases):** a header-field value `=cmd` renders neutralized
    (`Tên phiếu,'=cmd`) with BOM + trailing CRLF still present; a data-row cell `@evil` / `=HYPERLINK(...)`
    neutralized through `Render`; a `-500000.00` money cell renders raw (`DoesNotContain "'-500000.00"`).
  - **Integration (real MariaDB) — `ExportContentTests.cs` (+2 cases):** end-to-end expense export with a
    member named `=cmd` and a share note `=HYPERLINK(http://x)` comes back as `'=cmd,500000.00,'=HYPERLINK(...)`
    (name + note guarded, money raw) with no un-guarded formula line and total `500000.00` intact;
    end-to-end event export with a formula member name `=evil` yields Section 2 `'=evil,500000.00,200000.00,300000.00`
    while `Tôi,0.00,300000.00,-300000.00` keeps the negative balance raw (`DoesNotContain "'-300000.00"`)
    and the `Tổng cộng` sum-to-zero holds. Seeded over the M3–M7 endpoints; cleaned via the inherited
    prefix sweep.
  - The pre-existing M8 negative-money balance assertions (`Cường -500000.00`,
    `Bình 800000.00,500000.00,300000.00`) continue to pass unchanged — the money-preservation regression
    guarantee is re-confirmed. No production bug found; `CsvWriter` behaves exactly per the OQ1a/OQ2a spec.

### 2026-07-14 (code review — APPROVED, 0 blocking — chore closed)

- **Verdict: APPROVE — 0 blocking findings, 2 informational notes** (both documented, accepted trade-offs).
  Suite **761/761** (passed / 0 failed / 0 skipped), deterministic, dev DB swept clean.
- **Verified checks:**
  - **Numeric-safe heuristic correct.** `CsvWriter.NeutralizeFormula` guards `=` / `@` / TAB (0x09) / CR
    (0x0D) **always**, and `+` / `-` **only when the whole field is NOT a parseable invariant decimal** — so
    money (`-500000.00` / `0.00` / `800000.00`) stays raw while `-cmd` / `+cmd` / `=SUM(...)` / `@x` are
    neutralized.
  - **Parse predicate is exactly right.** `decimal.TryParse` with
    `NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint` + `CultureInfo.InvariantCulture` accepts
    only `[+-]?digits[.digits]` — no grouping, whitespace, exponent, or currency symbol — so any string that
    takes the "leave raw" branch is provably a plain number and **no formula payload can slip through** the
    numeric branch; and being invariant-culture it is locale-independent (safe even on a comma-decimal
    machine, where a locale-sensitive parse could otherwise misjudge `-500000.00`).
  - **Guard runs before quoting.** The `'` prefix is applied first, then the unchanged RFC-4180 quote logic,
    so a neutralized field that also needs quoting gets both (`=1,2` → `"'=1,2"`).
  - **Scope contained to one file.** Only `Services/Api/Export/Formatters/CsvWriter.cs` changed;
    `CsvExportFormatter` / `ExportService` / `ExportDocument` (IR) / `ExportValueFormatter` / `.csproj` /
    `ErrorCodes.cs` all untouched — **no new dependency, no new error code, no new message key**.
  - **Tests assert real behavior** (always-guarded triggers, guarded `+cmd`/`-cmd`, money preserved,
    guard×quoting interaction, and the pre-existing M8 balance-table negative-money assertions still green).
- **2 informational notes (accepted trade-offs, no change required):**
  1. **Heuristic gap:** a **text** field that is itself a bare number (e.g. a note `"+84901234567"`) is left
     raw — harmless (Excel just shows the number; no exfiltration) and the explicitly accepted OQ1(a) cost.
  2. **First-char-only coverage** matches the OWASP formula-trigger set; leading-whitespace / leading-LF
     cases are safe by design (a leading LF is already always quoted by RFC-4180 and is not a formula
     trigger).
- **Final state confirmed:** `dotnet test` = **761 passed / 0 failed / 0 skipped**, deterministic, dev DB
  swept clean; build clean; code review = **APPROVE**. The chore is **closed and ready to commit.**

## Final Outcome

CSV formula-injection hardening is **complete, reviewed (APPROVE — 0 blocking), and ready to commit.** A
single production file changed — `Services/Api/Export/Formatters/CsvWriter.cs` gained a `NeutralizeFormula`
step wired as the first action of `EscapeField` (the single cell choke point) that prepends a single-quote
`'` to text cells beginning with a spreadsheet formula trigger (`=` / `@` / TAB / CR always; `+` / `-` only
when the field is NOT a parseable invariant decimal), running **before** the unchanged RFC-4180 quoting. The
**numeric-safe heuristic** preserves M8 OQ5's raw re-importable money — including negative balance columns
like `-500000.00` — byte-identical; dates (leading digit) and app-generated Vietnamese labels (leading
letter) are naturally untouched. The guard is **CSV-path-only** (a future Excel formatter will set real cell
types; JSON is immune); the string-only `ExportDocument` IR is unchanged.

**No migration, no schema change, no new NuGet dependency, no new error code (11xxx stays reserved-only), no
new message key.** All M8-locked behavior (UTF-8 BOM, comma + CRLF, RFC-4180 quoting, money/date formatting,
the `FileContentResult` bypass, resource-owned 404s) is unchanged. `dotnet test` = **761 passed / 0 failed /
0 skipped** (deterministic, dev DB swept clean); build clean; code review = **APPROVE — 0 blocking**. Two
informational notes (the accepted bare-number heuristic gap; first-char-only OWASP coverage) are recorded as
documented trade-offs — no follow-up required.

## Future Improvements

- **Typed `ExportDocument` cells (OQ1b) if/when Excel/JSON formatters land** — a Text/Numeric flag would move
  the guard signal into the IR and let every future formatter defend consistently, retiring the CSV-only
  heuristic.
- **Configurable/stricter dangerous-char set** (e.g. optionally include leading LF, or a per-consumer
  "no guard" mode for known machine re-import pipelines) if a non-Excel consumer needs the `'`-free text.
