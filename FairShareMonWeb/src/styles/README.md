# FairShareMon Design System

The visual substrate for FairShareMonWeb. Built from scratch (no Figma) on the
locked OQ5 stack: **CSS Modules + CSS-custom-property tokens + Radix primitives**.
One palette, one type scale, one spacing system across every screen.

- **Tokens:** `src/styles/tokens.css`
- **Base/reset:** `src/styles/global.css` (imports tokens; import once from the app entry)
- **Primitives:** `src/components/ui/*` — import from `src/components/ui` (the barrel)
- **Living style guide:** `src/styles/StyleGuide.tsx` (mounted in `App.tsx` for review)

## Design language

"Sổ ghi nợ chi tiêu" — a group expense ledger. The identity is **trustworthy and
precise** (immutable audit, closed events, exact money) yet **warm and social**
(friends splitting trips & meals). The brand hue is **jade / teal-green** — it
reads as "money / tiền" without literal grass green, and deliberately avoids the
generic AI purple-gradient look. Neutrals are a cool gray with a faint jade bias
(chosen, not defaulted). Gold is reserved for Premium.

## Token naming convention

```
Primitive (raw ramp step)   --fs-<ramp>-<step>     --fs-jade-600, --fs-gray-100
Semantic (role)             --fs-<group>-<role>    --fs-color-primary, --fs-color-danger-text
Scales                      --fs-space-3, --fs-radius-md, --fs-text-lg, --fs-duration-fast, --fs-z-modal
```

**Components consume SEMANTIC tokens (and scales), never raw ramp steps.** Raw
ramps are the palette; semantic tokens are the light/dark contract. Change a role
once in `tokens.css` and every surface moves.

Token groups: `--fs-color-*` (surfaces, text, border, brand, status, finance,
premium), `--fs-viz-*` (chart palette), `--fs-space-*`, `--fs-radius-*`,
`--fs-text-* / --fs-leading-* / --fs-weight-* / --fs-tracking-*`, `--fs-shadow-*`,
`--fs-duration-* / --fs-ease-*`, `--fs-z-*`, `--fs-layout-* / --fs-header-h`.

## Theme contract — what the implementer must wire

Light is the default. OS dark applies automatically **unless** the viewer forced a
theme. The viewer's toggle stamps an attribute on the root element and **always
wins in both directions**:

```
<html data-theme="light">   → force light (beats OS dark)
<html data-theme="dark">    → force dark  (beats OS light)
<html>  (no attribute)      → follow the OS (prefers-color-scheme)
```

The CSS resolves all three cases (see `tokens.css`):

```css
:root {
  /* light tokens */
}
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) {
    /* dark tokens (OS) */
  }
}
:root[data-theme="dark"] {
  /* dark tokens (explicit — wins) */
}
```

**Implementer responsibility (React state — NOT owned by the design layer):**

1. A `ThemeProvider`/`useTheme` holding `"light" | "dark" | "system"`, persisted
   (`localStorage`). On change: if `system`, `removeAttribute("data-theme")` on
   `document.documentElement`; else `setAttribute("data-theme", value)`.
2. Read the persisted value on boot **before paint** (a tiny inline script in
   `index.html` or a synchronous read in `main.tsx`) to avoid a flash.
3. Render `<ThemeToggle value onChange labels groupLabel />` from the ui barrel —
   it is presentational and controlled; it does not touch the DOM itself.

No token references a hardcoded theme color in a component; theming is entirely
token-swap, so nothing else needs per-theme code.

## Locale contract

`<LanguageToggle value onChange labels groupLabel />` is presentational. The
implementer's `LocaleProvider` owns `"vi-VN" | "en-US"` and on change must:
`i18next.changeLanguage`, update the API client's `Accept-Language`, set
`<html lang>`, and persist. The toggle only renders and reports intent.

## Money & finance display

- `<Money amount={...} />` renders VND. It ships a self-contained vi-VN VND
  fallback formatter, but the implementer **should inject the app's shared
  `formatMoneyVnd`** via the `format` prop so there is one formatter of record.
  Never combine money with float math in the client — pass API-computed values.
- `variant="balance"` shows a **signed** figure: `+` = credit (owed to you /
  _được nhận lại_, jade-green), `−` = debit (you owe / _phải trả_, red), `0` =
  settled (neutral). The **sign glyph is the color-independent cue** — meaning
  never rests on color alone (red-green CVD safe).

## Form-control primitives (pickers)

The input family all share the TextField frame (2.5rem control, sunken surface,
same border/focus ring) so they line up in a form row:

- `<Select>` — single-select on **Radix Select** (keyboard, typeahead, ARIA
  combobox/listbox, portalled positioning). Controlled: `value` +
  `onValueChange`. Pass `options: {value,label,disabled?,meta?}[]` and an optional
  `renderOption(option)` slot rendered inside Radix `ItemText` — so the SELECTED
  option's custom content (a `CategoryMarker`, a member's owner-rep /
  "(đã xóa)" treatment) is mirrored into the trigger automatically. `label`,
  `placeholder`, `hint`, `error`, `required`, `disabled`, `hideLabelVisually`.
  **Radix forbids an empty-string item value** — use a sentinel like `"all"` for
  a "no filter" option, not `""`.
- `<TagMultiSelect>` — multi-select whose `value` is an array of ids. Selected
  tags read as removable chips; a checkbox-list popover (native checkboxes,
  Escape / outside-click to close) toggles membership. `options: {value,label}[]`,
  `onChange`, `label`, `placeholder`, `hint`, `error`, plus localized
  `toggleLabel` / `removeLabel(label)` / `emptyLabel`.
- `<MoneyInput>` — whole-VND numeric field. Accepts integers only (digits are
  stripped, so negatives are impossible), emits a plain `number | null`, shows a
  grouped figure (`1.234.567`) while blurred and raw digits while focused (no
  caret jumps), and pairs with `<Money>` (same vi-VN, 0-decimal rule). Wire to
  RHF via a `Controller` (the value is a number). Inject the app
  `formatMoneyVnd` grouping via `format`.

`hideLabelVisually` (also on `TextField`) keeps the `<label>` for assistive tech
while removing it visually — use it only where a visible column header already
labels the field for sighted users (the share-editor rows).

## Domain visual states

- **Settled (đã trả):** `<Badge tone="settled" icon={<Check/>}>`.
- **Event open / closed (đang mở / đã chốt):** `<Badge tone="success">` /
  `<Badge tone="neutral" icon={<Lock/>}>`. When an event is **closed**, the
  implementer disables every write control (buttons `disabled`, inputs
  `readOnly`) except the settled toggle — the design provides the disabled
  visuals; the enforcement is the implementer's.
- **Premium-gated (code 13003):** `<UpgradePrompt/>` (gold treatment, distinct
  from a generic forbidden error). `variant` selects the mode:
  `"cta"` (default) — the gated feature, pass an `action`; `"info"` — an
  informational panel with **no** navigating action (Premium is granted manually,
  no self-serve purchase); `"active"` — a subtle check-marked confirmation that
  the account already has Premium. Meaning never rests on color — the crown/check
  glyphs + copy carry it.
- **Tier indicator:** `<TierBadge tier freeLabel premiumLabel/>` — display-only.
  Premium wears the gold `Badge` + crown; Free is neutral. Compares `tier`
  case-insensitively; absent/unknown → Free (fail-safe). Labels are passed in
  (localized by the implementer).
- **Free limit reached (codes 13000/13001/13002):** `<LimitNotice/>` (calm,
  neutral — existing data is never touched).

## Event & balance patterns (M5)

The four net-new event surfaces are compositions over existing primitives — see
`src/styles/M5Showcase.tsx` for the reviewable spec (light + dark). Two small,
reusable primitive additions back them:

- **Table summary/total row.** `<TableFoot>` (a `<tfoot>`) holding a
  `<TableRow total>` — a heavier top rule, sunken tint, semibold cells, no
  zebra/hover. Used for the **debt-balance sum-to-zero row**. The balance table
  itself is a plain `Table` composition: member (row header, with the owner-rep
  tag + muted "(đã xóa)" treatment), `Money` **đã ứng** / **phải gánh** numeric
  cells, and a **cân bằng** cell = `<Money variant="balance">` (the +/− sign
  glyph is the color-independent cue) **plus a polarity word** (được nhận lại /
  phải trả / đã cân bằng) so meaning never rests on color. Totals are the
  API-provided column sums, rendered verbatim — **never client-summed**.
- **Irreversible-action confirm.** `<DialogContent tone="danger">` adds a danger
  top accent + a warning-triangle severity glyph, marking a one-way / destructive
  action as visually distinct from an ordinary confirm (an ordinary delete stays
  `tone="default"`). For the **one-way event close**, pair it with a warning
  `Alert` ("Sau khi chốt, đợt bị khóa"), a deliberate acknowledgment checkbox
  that gates the danger button, and a `variant="danger"` primary button labelled
  with the irreversibility ("Chốt đợt — không thể hoàn tác").
- **Event status Badge.** Open → `<Badge tone="success">` + a clock glyph "Đang
  mở"; closed → `<Badge tone="neutral" icon={<Lock/>}>` "Đã chốt". Icon + text,
  never color alone; the closed badge pairs with the implementer disabling every
  write control (except the settled toggle).
- **Assign-expense picker.** A `Dialog` hosting a searchable single-select list
  of eligible (loose, in-range) expenses. Built on a native `<fieldset>` +
  `<input type="radio">` rows (full keyboard + SR support; each row named by
  expense name + date + `Money` total), a bounded scroll panel, and `Skeleton`
  (loading) / `EmptyState` (nothing eligible / no search match) states.

## Toast wiring (Radix)

The `Toast` layer is presentational. The implementer owns the queue:

```tsx
<ToastProvider swipeDirection="right">
  {/* app */}
  {queue.map((t) => (
    <Toast
      key={t.id}
      tone={t.tone}
      title={t.title}
      description={t.description}
      open={t.open}
      onOpenChange={(o) => !o && dismiss(t.id)}
    />
  ))}
  <ToastViewport />
</ToastProvider>
```

Expose a `useToast()` that pushes items (mutation failures, "đã lưu", etc.).

## Data-viz (M6 — first charts; `--fs-viz-*` palette)

`--fs-viz-*` tokens are the validated categorical (8-slot, fixed order),
sequential (blue ramp), diverging (blue↔red, gray midpoint), and chart-chrome
palette for the **Stats** and **Admin** dashboards. Validated with the
`dataviz` skill against the app's chart surfaces (light `#ffffff` / dark
`#151d1b`), both modes (re-run 2026-07-17):

- Categorical: worst adjacent CVD ΔE **9.1 light / 8.4 dark**; normal-vision
  floor **19.6 / 19.3**. Assign slots `--fs-viz-cat-1..8` **in order, never
  cycled**; a 9th series folds to "Other"/a muted neutral/small-multiples.
- **Relief rule (light):** slots 3/4/5 (magenta/yellow/aqua) sit < 3:1 on white
  (measured `#e87ba4` 2.69 · `#eda100` 2.17 · `#1baf7a` 2.82) — charts that use
  them **must** ship direct value labels or a table view.
- Balance charts (polarity) use the diverging tokens; balance **numbers** in
  tables/tiles use `<Money variant="balance">` (sign + color).

**M6 chart specs live in `src/styles/M6Showcase.tsx`** (reviewable in light +
dark via `StyleGuide.tsx`). Per OQ5a these are **feature-local** patterns the
web-implementer rebuilds under `src/features/stats/components/` — they are NOT
extracted to `components/ui/charts` until M8 (Admin) gives a second consumer. The
four M6 surfaces:

- **StatTile / OverviewKpiRow** — label + big value (`Money` for currency, a
  formatted number for a count) + optional sub-label. Tabular numerics so a
  range-driven value never reflows. No computed "average" tile (R3: no float math
  on money). Loading → skeleton tiles; zero range → `0` tiles (valid, not empty).
- **CategoryBarChart** — hand-rolled ranked **horizontal bar** (longest first, API
  order verbatim). The bar **fill** is the only element wearing `--fs-viz-cat-*`
  (slots `1..8` by rank, 9th+ → `--fs-viz-ink-muted`); every label/value/axis
  wears text tokens. **Bar-length normalization = `total / maxTotal`** (the
  longest bar = 100%); the **% share = `total / overview.totalSpending`** (the
  authoritative same-range denominator) — both are display-only ratios off the
  API's integer totals, **no money value is ever client-computed**. Each bar
  carries a direct label (`CategoryMarker` + name + `Money` + %) → relief-rule
  compliant + color-independent. The chart region is `role="img"` with an
  `aria-label` summary; bars are `aria-hidden`. **It pairs with an always-present
  accessible `CategoryStatsTable`** (`<caption>` + Danh mục / Tổng / Số phiếu /
  Tỷ trọng; footer echoes `overview.totalSpending`, never a client sum) — the
  table is the data channel for assistive tech. Deleted categories (§4.7) keep
  their slot with an `(đã xóa)` treatment. `prefers-reduced-motion` disables the
  bar-grow transition.
- **StatsRangeControl** — preset chips (`role="group"`, active chip carries
  `aria-pressed`, state not by color alone) + a Custom two-date mode with an
  inline invalid-range (`from > to`) message.
- **Home composition** — this-month KPI row + a compact top-5 breakdown + a
  recent-expenses card (rows link to detail) + quick actions + the M1 quick links,
  on a responsive grid.

When editing any chart, follow the `dataviz` skill: pick the form, apply the mark
specs (thin marks ≤24px, 4px rounded data-end, ≥2px surface gaps), keep labels in
text tokens, and **re-run `scripts/validate_palette.js` if any `--fs-viz-*` hue
changes**.

## Wallet & QR (M7)

Two net-new surfaces — see `src/styles/M7Showcase.tsx` for the reviewable spec
(light + dark via `StyleGuide.tsx`). Per OQ5a they are **feature-local** patterns
the web-implementer rebuilds under `src/features/wallet/components/` — they are
NOT extracted to `components/ui` until a second consumer appears. Both reuse
existing primitives (Dialog, Table, Select, UpgradePrompt, EmptyState/ErrorState,
Alert, Badge, TierBadge, Money, Skeleton); no new tokens, no new dependency.

- **QrDialog** — the one genuinely new composite. A modal showing a VietQR image
  paired with a human-readable **account block** (bank · account number · holder
  · amount) — the QR is decorative-plus, so the account block is the accessible
  channel AND the source the **Copy details** action copies (holder + number, per
  OQ4a — never the raw VietQR TLV string). Presentational: the implementer passes
  a `state` discriminated union mapped from the query, an `imageUrl` (the object
  URL it creates from the PNG `Blob` and **revokes on unmount / re-fetch**), and
  the account/destination props. States: `loading` (a `Skeleton` sized to the QR
  frame — zero layout shift) · `ready` (`<img>` + account block) · `premiumGate`
  (the body **IS** an informational `UpgradePrompt` — Free proactive OR reactive
  `403 13003`; no navigating action, Premium is a manual grant) · `noAccount`
  (`12001` → `EmptyState` + a link to `/wallet`) · `noDebt` (`12003` → info
  `Alert`) · `notClosed` (`12002` → warning `Alert`, defensive) · `error`
  (`ErrorState` + retry). An optional destination `Select` shows only with **≥2**
  accounts (OQ2a). Footer: **Download** (primary) + **Copy details**; the live
  dialog always adds a **Close**. `kind` sets the QR frame aspect — `expense`
  square (`1/1`), `event` composite taller (`3/4`). **The QR frame stays light in
  both themes** (a fixed white ground + quiet-zone padding) — a QR must present
  dark modules on a light ground to scan, so it does not invert in dark mode; the
  `alt` text names it and points to the account block below.
- **Wallet list** — the bank-account `Table`: bank name + **BIN** (mono), a
  **masked account number** (`•••• 1234`) with a per-row **reveal toggle**
  (`aria-pressed`, eye / eye-off glyph, labelled "Hiện/Ẩn số tài khoản …" — the
  state is not color-alone; reveal is a Free-safe read of the user's own number),
  holder, and a **default marker** (`Badge tone="settled"` + star glyph + text —
  icon+text, never color alone). Account & BIN numbers wear `--fs-font-mono` +
  `tabular-nums` (a bank number is a code; mono also keeps the reveal toggle from
  shifting when masked ↔ full). The **Free/Premium split** (OQ1a hybrid, proactive
  by session tier): a **Premium** user sees the "Thêm tài khoản" action + a per-row
  actions column (**Set default** on non-default rows, **Edit**, **Delete**); a
  **Free** user sees an informational `UpgradePrompt` banner above a **read-only**
  table (no actions column). Empty states double as the tier explainer — Free =
  "Ví là tính năng Premium", Premium = "add your first account". A stale-tier
  `403 13003` on any mutation is caught reactively and rendered as the same
  `UpgradePrompt` (the server is authoritative).

## App shell & page layout

- **Responsive nav (`AppShell`):** mobile-first. Below **64rem** the inline nav
  is hidden and a labeled hamburger opens a Radix-Dialog-backed slide-in drawer
  holding the **same** `nav` nodes (supply them once via the `nav` prop);
  at/above 64rem the inline nav shows and the button is hidden. Radix gives the
  focus trap, Escape-to-close, focus restore, and the trigger's
  `aria-expanded`/`aria-controls`; the drawer closes when any nav entry is
  activated (pointer or keyboard). Optional props: `mobileMenuLabel`,
  `mobileMenuCloseLabel`, `navLabel` (all localized by the implementer). The
  slide honors `prefers-reduced-motion`.
- **Page scaffolding:** `<PageHeader title description actions/>` gives every
  routed page one `<h1>` hierarchy (title/description wrap; actions drop below on
  narrow viewports). `<Stack gap>` is the vertical-rhythm column (token gaps, no
  collapsing margins) — e.g. the settings page is a `PageHeader` + a `Stack` of
  `Card`s. `<DescriptionList>` / `<DescriptionRow term>` is the semantic `dl`
  for read-only detail rows (profile fields, event details): term beside value
  on wide viewports, stacked when narrow; a value may be a `Skeleton`, `Badge`,
  or text.

## Accessibility baseline

- Global visible focus ring (`:focus-visible`, 2px jade, 2px offset) — components
  may reshape it but never remove it.
- Status/finance meaning is always **icon + text** (or sign), never color alone.
- Semantic landmarks in `AppShell` (`header`/`nav`/`main`) + a skip link.
- Fields wire `<label for>`, `aria-invalid`, `aria-describedby` → hint & error.
- Dialog/Toast inherit Radix focus management, Escape, and ARIA.
- `prefers-reduced-motion` neutralizes animations globally (and per component).
- Vietnamese runs ~30% longer and stacks diacritics: generous line-heights, no
  tight tracking on body, `text-wrap` on headings/paragraphs, `overflow-wrap` so
  long member names/URLs never force horizontal scroll.

## Typography

System-first stack for zero-dependency, excellent Vietnamese coverage (Segoe UI
on Windows). To adopt a brand webfont later (e.g. **Be Vietnam Pro**), prepend it
to `--fs-font-sans` in `tokens.css` — every surface follows because it reads that
one token. No other change needed.
