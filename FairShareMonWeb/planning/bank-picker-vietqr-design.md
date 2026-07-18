# Design spec — `Combobox` + `BankLogo` (bank-picker / VietQR)

> **Companion to** `bank-picker-vietqr.md` (approved plan). This is the
> visual/interaction spec the web-implementer builds Steps 5 (`Combobox`) and 6
> (`BankLogo`) directly from. It does **not** wire app logic, data, or i18n — it
> defines anatomy, tokens, states, ARIA/keyboard, responsive behavior, and the
> props contract, all expressed in the existing `--fs-*` token system so both
> primitives read as native members of the design system.
>
> **Authored 2026-07-18 by ui-designer.** Grounded in the live primitives:
> `TextField`, `Select`, `TagMultiSelect`, `ColorPicker` (+ their `.module.css`)
> and `styles/tokens.css`. Every value below is a semantic token or a scale step —
> **no raw ramp steps, no ad-hoc px** except where noted (touch-target heights,
> the logo plate).

---

## 0. Shared field-family conventions (copy these verbatim)

Both `TextField`, `Select`, `TagMultiSelect`, and `ColorPicker` share one field
skeleton. The `Combobox` MUST reuse it exactly so it lines up in a form column
with the retained `accountNumber` / `accountHolderName` `TextField`s.

| Part | Rule (identical across the family) |
| --- | --- |
| `.field` | `display:flex; flex-direction:column; gap:var(--fs-space-2)`. For `Combobox` add `position:relative` (the panel is absolutely positioned to it, like `TagMultiSelect`). |
| `.label` | `inline-flex; gap:var(--fs-space-1); font-size:var(--fs-text-sm); font-weight:var(--fs-weight-medium); color:var(--fs-color-text); line-height:var(--fs-leading-snug)`. |
| `.required` | trailing `*`, `color:var(--fs-color-danger-text); font-weight:var(--fs-weight-bold)`, `aria-hidden`. |
| `.labelHidden` | the standard visually-hidden clip block (see `hideLabelVisually`). |
| Control resting | `background:var(--fs-color-surface-sunken); border:1px solid var(--fs-color-border); border-radius:var(--fs-radius-md)`. |
| Control hover | `border-color:var(--fs-color-border-strong)`. |
| Control focus | `border-color:var(--fs-color-focus); box-shadow:var(--fs-shadow-focus)`; `outline:none`. |
| Control invalid | `border-color:var(--fs-color-danger)`; invalid+focus → `box-shadow:0 0 0 3px var(--fs-color-danger-surface)`. |
| Control disabled | `cursor:not-allowed; color:var(--fs-color-text-disabled); background:var(--fs-color-surface)`. |
| Placeholder ink | `var(--fs-color-text-muted)`. |
| `.hint` | `font-size:var(--fs-text-sm); line-height:var(--fs-leading-snug); color:var(--fs-color-text-secondary)`. Rendered only when `hint && !invalid`. |
| `.error` | `display:flex; gap:var(--fs-space-1); font-size:var(--fs-text-sm); line-height:var(--fs-leading-snug); color:var(--fs-color-danger-text); font-weight:var(--fs-weight-medium)`, `role="alert"`. |
| ID wiring | `useId()` → `fieldId`, `labelId`, `hintId`, `errorId`; `describedBy = cx(hint && !invalid ? hintId : undefined, invalid ? errorId : undefined) || undefined`. |

Popover surface (shared by `Select` content + `TagMultiSelect` panel), reused for
the `Combobox` panel:

```
background: var(--fs-color-surface-raised);
border: 1px solid var(--fs-color-border-subtle);
border-radius: var(--fs-radius-md);
box-shadow: var(--fs-shadow-md);
z-index: var(--fs-z-dropdown);
animation: fs-combobox-in var(--fs-duration-fast) var(--fs-ease-decelerate);
```

`@keyframes fs-combobox-in { from { opacity:0; transform:translateY(-2px); } }`
— identical to `fs-select-in` / `fs-tagpanel-in`.

---

## 1. `Combobox` — searchable single-select

### 1.1 Anatomy

The control is **trigger-opens-popover** (like `TagMultiSelect`'s toggle→panel,
NOT an always-inline text input). The search input lives **inside** the popover
and takes focus on open. This keeps the closed control identical in rhythm to a
`Select` trigger while adding search.

```
┌─ .field (position:relative) ──────────────────────────────────┐
│  Ngân hàng *                                    ← .label       │
│  ┌─ .trigger (button, closed) ─────────────────────────────┐  │
│  │ [logo] Techcombank                                  ⌄   │  │  h 2.5rem
│  │        Ngân hàng TMCP Kỹ Thương … · BIN 970407          │  │  (selected =
│  └──────────────────────────────────────────────────────────┘  │   rich 2-line)
│  ┌─ .panel (absolute, opens below) ─────────────────────────┐  │
│  │ ┌─ .search (role=combobox input) ───────────────── 🔍 ┐ │  │  pinned,
│  │ │ tìm ngân hàng…                                       │ │  │  not scrolled
│  │ └──────────────────────────────────────────────────────┘ │  │
│  │ ┌─ .listbox (role=listbox, scrolls) ───────────────────┐ │  │
│  │ │ [logo] Vietcombank                                   │ │  │  ← .option
│  │ │        NH TMCP Ngoại thương VN · BIN 970436          │ │  │    (active =
│  │ │ [logo] Techcombank                            ✓      │ │  │     primary-
│  │ │        NH TMCP Kỹ Thương VN · BIN 970407             │ │  │     subtle bg;
│  │ │ …                                                    │ │  │     selected = ✓)
│  │ └──────────────────────────────────────────────────────┘ │  │
│  └──────────────────────────────────────────────────────────┘  │
│  Chọn ngân hàng phát hành tài khoản.            ← .hint / .error │
└────────────────────────────────────────────────────────────────┘
```

**Closed trigger content** mirrors `Select`'s renderOption→trigger behavior: when
a value is selected, render the selected option's `renderOption(option)` node in
the trigger; otherwise render the `placeholder` in muted ink. Because the bank
row is two lines, the trigger grows to fit — see height rules below.

### 1.2 Layout + tokens

**Trigger** (extends `Select .trigger`):

| Property | Value |
| --- | --- |
| layout | `display:flex; align-items:center; gap:var(--fs-space-2); width:100%; min-width:0; text-align:start; cursor:pointer` |
| height | `min-height:2.5rem` (NOT fixed `height` — the rich 2-line selected content may exceed one line); `padding-block:var(--fs-space-2)` |
| padding-inline | `var(--fs-space-3)` |
| surface/border/radius | field-family resting values (§0) |
| font | `font:inherit; font-size:var(--fs-text-md)` |
| value slot `.value` | `flex:1; min-width:0; overflow:hidden` — wraps the `renderOption` node; truncation handled inside the row (§1.6) |
| chevron `.chevron` | `flex:none; color:var(--fs-color-text-secondary)`; svg `1.1rem`; reuse `Select`'s `ChevronIcon`. Rotate 180° when open (optional, `transform` under motion guard). |

**Panel** (`.panel`, hand-rolled like `TagMultiSelect`):

| Property | Value |
| --- | --- |
| position | `absolute; top:calc(100% + var(--fs-space-1)); inset-inline:0` (matches trigger/field width — this is how it stays inside a 360px viewport) |
| surface | shared popover surface (§0) |
| padding | `var(--fs-space-1)` |
| layout | `display:flex; flex-direction:column; gap:var(--fs-space-1)` — search pinned on top, listbox below |
| max-height | the **panel** is not scrolled; the listbox inside is (search stays pinned) |

**Search input** (`.search`, pinned; visually a compact `TextField.input` on the
sunken surface so it reads as a field within the raised panel):

| Property | Value |
| --- | --- |
| height | `2.25rem` (`@media (pointer:coarse)` → `2.75rem`) |
| surface | `background:var(--fs-color-surface-sunken); border:1px solid var(--fs-color-border); border-radius:var(--fs-radius-sm)` |
| padding | `padding-inline:var(--fs-space-3)`; if a leading search glyph is shown, `padding-inline-start:var(--fs-space-8)` with the icon absolutely placed in `var(--fs-color-text-muted)` |
| focus | `border-color:var(--fs-color-focus); box-shadow:var(--fs-shadow-focus); outline:none` |
| placeholder | `var(--fs-color-text-muted)` (from `searchPlaceholder`) |
| font | `font-size:var(--fs-text-md)` |

**Listbox** (`.listbox`, scroll region):

| Property | Value |
| --- | --- |
| role region | `role="listbox"` |
| max-height | `min(60vh, 18rem)` — internal scroll; on a phone `60vh` caps it well short of the viewport |
| overflow | `overflow-y:auto; overscroll-behavior:contain` |
| padding | `0` (rows carry their own padding); rows separated by nothing (hover/active bg is the separator) |

**Option row** (`.option`, extends `Select .item` + the two-line member/category
row spirit):

| Property | Value |
| --- | --- |
| layout | `display:flex; align-items:center; gap:var(--fs-space-3); min-height:2.25rem` (`coarse` → `2.75rem`); `padding:var(--fs-space-2) var(--fs-space-3)`; `padding-inline-end:var(--fs-space-8)` (room for the ✓) |
| radius | `var(--fs-radius-sm)` |
| logo | `<BankLogo size="md">` (28px), `flex:none` |
| text stack `.optionText` | `display:flex; flex-direction:column; min-width:0; gap:2px` |
| primary `.optionPrimary` | `font-size:var(--fs-text-md); color:var(--fs-color-text); font-weight:var(--fs-weight-regular)` (→ `medium` when selected); `overflow-wrap:anywhere` |
| secondary `.optionSecondary` | `font-size:var(--fs-text-sm); color:var(--fs-color-text-secondary); line-height:var(--fs-leading-snug); overflow-wrap:anywhere` — the full legal name + `BIN {{bin}}`; the BIN digits wear `font-variant-numeric:tabular-nums` |
| check `.optionCheck` | absolute `inset-inline-end:var(--fs-space-2)`, `color:var(--fs-color-primary)`, svg `1rem`; reuse `Select`'s `CheckIcon`; shown only on the selected row |

### 1.3 States

| State | Trigger | Option row |
| --- | --- | --- |
| **default / placeholder** | muted placeholder text, resting border | — |
| **filled** | selected option's `renderOption` node (logo + short name; secondary line optional in trigger — see note) | — |
| **hover** (control) | `border-color:var(--fs-color-border-strong)` | `background:var(--fs-color-surface-hover)` on pointer hover |
| **focus-visible** (control) | `border-color:var(--fs-color-focus); box-shadow:var(--fs-shadow-focus)` | — |
| **open** | same as focus (`[data-state="open"]`) + chevron rotated | — |
| **active-descendant** (keyboard) | — | `background:var(--fs-color-primary-subtle); color:var(--fs-color-text)` — the SAME cue `Select .item[data-highlighted]` uses. Only one option is active at a time (`aria-activedescendant`). Row must `scrollIntoView({block:"nearest"})` when it becomes active. |
| **selected** | filled trigger | `font-weight:var(--fs-weight-medium)` + trailing ✓ (`aria-selected="true"`) |
| **disabled** (option) | — | `color:var(--fs-color-text-disabled); pointer-events:none` (skipped by keyboard nav) |
| **disabled** (control) | field-family disabled (§0); panel cannot open | — |
| **error** | `border-color:var(--fs-color-danger)`; invalid+focus → `box-shadow:0 0 0 3px var(--fs-color-danger-surface)`; `.error` message below with `role="alert"`; `aria-invalid` + `aria-describedby` on the trigger | — |
| **loading** | trigger unaffected (snapshot seeds the list instantly — see plan R3). A subtle hint row sits at the **top of the listbox** or beside the search: a small spinner + `loading` copy in `var(--fs-color-text-secondary)`, `aria-live="polite"`. Never blocks opening; never empties. | — |
| **empty (no match)** | — | single `.empty` row: `padding:var(--fs-space-3); text-align:center; font-size:var(--fs-text-sm); color:var(--fs-color-text-secondary)`, shows `emptyLabel`. Not focusable, not an `option`. |

> **Trigger secondary line note:** to keep the closed control from growing too
> tall, the trigger MAY render only the primary (short name) beside the logo and
> drop the secondary line, while option rows always show both. The `renderOption`
> the plan supplies renders both; the trigger can clamp the secondary line with
> `-webkit-line-clamp:1` or the caller can pass the same node and let the
> `min-height` grow. **Recommendation:** show logo + short name only in the
> trigger (one line, clean), full two-line row in the list. Flagged as OQ-D1.

### 1.4 ARIA + keyboard

ARIA-1.2 "combobox with listbox popup", focus retained on the search input and
the active option tracked with `aria-activedescendant` (NOT roving tabindex — see
note). The trigger is the collapsed affordance.

| Element | ARIA |
| --- | --- |
| `.label` | `id={labelId}` |
| `.trigger` (button) | `id={fieldId}`; `aria-haspopup="listbox"`; `aria-expanded={open}`; `aria-controls={listboxId}`; `aria-labelledby={`${labelId} ${fieldId}`}` (label + rendered selected text announce together); `aria-describedby={describedBy}`; `aria-invalid={invalid || undefined}`. Selected text must be real text inside the trigger (the `renderOption` node contains the short name) so the accessible name resolves. |
| `.search` (input) | `role="combobox"`; `aria-expanded="true"` (it only exists while open); `aria-controls={listboxId}`; `aria-autocomplete="list"`; `aria-activedescendant={activeOptionId || undefined}`; `aria-labelledby={labelId}` (or `aria-label` = `searchPlaceholder`); `type="text"`, `autocomplete="off"`, `spellcheck={false}`. |
| `.listbox` | `role="listbox"`; `id={listboxId}`; `aria-label` = the field label (so the listbox is named even though it's separate from the input). |
| `.option` | `role="option"`; `id={`${fieldId}-opt-${index}`}`; `aria-selected={value === option.value}`; `aria-disabled` when disabled. Active state is visual + `aria-activedescendant` on the input — do NOT put `tabindex` on options. |
| `.empty` | plain `<p>`, `role` none, not in the listbox's option set (or `aria-live="polite"` region announcing "no results"). |

**Keyboard** (borrow the index math from `ColorPicker.selectAt`, but implement as
active-descendant so focus never leaves the search input):

| Key | Context | Behavior |
| --- | --- | --- |
| `Enter` / `Space` / `ArrowDown` / click | trigger, closed | open the panel; focus the search input; active = the currently-selected option (or first enabled). `Space` opens only when the trigger is focused (not while typing). |
| type any char | search input | filter the list (§1.5); reset active to the first match; keep it visible. |
| `ArrowDown` | open | active → next enabled option (wraps like `ColorPicker`); `scrollIntoView`. |
| `ArrowUp` | open | active → previous enabled option (wraps). |
| `Home` / `End` | open | active → first / last enabled option. |
| `Enter` | open, active set | select active option → `onValueChange(option.value)`; close; return focus to the trigger. |
| `Escape` | open | close without changing value; return focus to the trigger. (Matches `TagMultiSelect`.) |
| `Tab` | open | close the panel (commit nothing) and let focus move naturally; do not trap focus. |
| outside `pointerdown` | open | close (matches `TagMultiSelect`'s `rootRef` + `document` listener; guard with `rootRef.current?.contains`). |

> **Roving vs active-descendant:** the plan cites `ColorPicker`'s "roving
> keyboard nav" as the reference. Reuse its **index arithmetic** (next/prev/home/
> end with wrap, skipping disabled), but the **focus model must be
> active-descendant** — focus stays on the `role="combobox"` input while
> `aria-activedescendant` points at the highlighted `role="option"`. Roving
> tabindex (moving DOM focus onto options) is wrong for a combobox because the
> user must keep typing into the search field. This is the one deliberate
> divergence from a literal `ColorPicker` copy.

**Motion:** the `fs-combobox-in` open animation and any chevron rotation are
wrapped in `@media (prefers-reduced-motion: reduce) { animation:none;
transition:none; }`. The `scrollIntoView` on active change should pass
`behavior:"auto"` (never smooth) so reduced-motion users aren't scrolled
kinetically.

### 1.5 Filtering rule (case- + diacritic-insensitive, Vietnamese-aware)

Match the query against `label` + every entry in `keywords` (the plan passes
`keywords = [name, bin, code]`). Normalize both sides before comparing.
Substring (`includes`) match, not fuzzy.

```ts
// Normalize for search: lowercase, strip combining diacritics, fold đ→d.
// NOTE: NFD does NOT decompose "đ"/"Đ" (they are distinct Vietnamese letters,
// not base+combining), so an explicit fold is required — otherwise typing
// "dong a" would never match "Đông Á" (EAB / DongA Bank).
const normalizeForSearch = (s: string): string =>
  s
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "") // strip combining marks (á→a, ệ→e, …)
    .toLowerCase()
    .replace(/đ/g, "d");             // fold đ→d AFTER lowercasing

const matches = (query: string, option: ComboboxOption): boolean => {
  const q = normalizeForSearch(query.trim());
  if (q === "") return true;
  const haystacks = [option.label, ...(option.keywords ?? [])];
  return haystacks.some((h) => normalizeForSearch(h).includes(q));
};
```

Worked examples that MUST all match **Techcombank** (`shortName:"Techcombank"`,
`name:"Ngân hàng TMCP Kỹ Thương Việt Nam"`, `code:"TCB"`, `bin:"970407"`):

| Typed | Matches via |
| --- | --- |
| `techcom` | label `techcombank` includes `techcom` |
| `ky thuong` | keyword name → `ky thuong` (from `Kỹ Thương`) |
| `kỹ thương` | same after both sides normalize |
| `970407` | keyword BIN |
| `tcb` | keyword code |
| `dong a` (for EAB) | name/short name folds `Đông Á` → `dong a` |

Digits-only queries (`9704…`) naturally target the BIN keyword; no special-casing
needed.

### 1.6 Responsive

- **Coarse pointer** (`@media (pointer: coarse)`): trigger `min-height`, search
  input height, and option-row `min-height` all bump to **`2.75rem` (44px)** — the
  cycle's coarse-target baseline. The chevron and check hit-areas inherit the row.
- **Small viewport (360px):** the panel is `inset-inline:0` on `.field`, so it is
  exactly as wide as the trigger — it can never overflow a 360px screen. It stays
  a **popover** (no full-screen sheet) per the plan. The listbox `max-height` uses
  `min(60vh, 18rem)` so on a short phone it caps at 60% of the viewport and scrolls
  internally, with the search input pinned above the scroll region.
- **Long Vietnamese names:** primary + secondary lines use `overflow-wrap:anywhere`
  and generous `--fs-leading-snug`; rows grow vertically rather than truncate the
  legal name mid-word. The trigger's single line (recommendation §1.3) truncates
  with ellipsis only for the closed state.
- **No new breakpoint:** only `@media (pointer:coarse)` and the intrinsic
  `min()`/`vh` cap are used — no new min-width stop is introduced (ladder stays
  sm 30 / md 48 / lg 64rem).

### 1.7 Props contract

Mirrors `SelectProps` so it drops into the same form conventions, plus the four
search/async props from the plan. Generic over `Meta`.

```ts
export type ComboboxOption<Meta = unknown> = {
  value: string;
  label: string;              // accessible name + trigger fallback + search target
  keywords?: string[];        // extra search text (full name, BIN, code)
  meta?: Meta;                // per-option data the renderOption slot reads
  disabled?: boolean;
};

export type ComboboxProps<Meta = unknown> = {
  value: string | undefined;                 // controlled; undefined → placeholder
  onValueChange: (value: string) => void;    // emits chosen option's value
  options: ComboboxOption<Meta>[];
  label: ReactNode;                           // required (a11y)
  placeholder?: string;                       // trigger, empty state (muted)
  searchPlaceholder?: string;                 // search input placeholder when open
  emptyLabel?: string;                        // "no matching bank"
  loading?: boolean;                          // subtle hint; never blocks/empties
  hint?: ReactNode;
  error?: ReactNode;                          // invalid styling + aria-describedby
  required?: boolean;
  disabled?: boolean;
  hideLabelVisually?: boolean;
  name?: string;                              // RHF/uncontrolled interop
  id?: string;
  className?: string;
  renderOption?: (option: ComboboxOption<Meta>) => ReactNode; // list + trigger
  ref?: Ref<HTMLButtonElement>;               // forwarded to the trigger button
};
```

Notes:
- `renderOption` drives **both** the list row and the filled trigger (Select
  parity). When absent, fall back to `option.label`.
- `ref` targets the **trigger button** (the focusable collapsed control) — the
  form's `autoFocus` and RHF focus-on-error land there. (`Select` forwards to its
  trigger too.)
- Empty `options` with `loading` → show the loading hint; empty `options` without
  loading → the panel opens to the `.empty` row.

### 1.8 CSS module class inventory (structure sketch, not final CSS)

```
.field .label .labelHidden .required
.trigger .triggerInvalid .value .chevron
.panel
.search .searchIcon
.listbox
.option .optionActive .optionSelected .optionText .optionPrimary .optionSecondary .optionCheck
.empty .loading
.hint .error
```

Reuse the `Select`/`TagMultiSelect` tokens listed in §0–§1.2 for each; do not
introduce new colors, radii, or shadows.

---

## 2. `BankLogo` — external logo `<img>` with initials fallback

`src/features/wallet/components/BankLogo.tsx`. A small presentational `<img>` in a
rounded plate that lazy-loads `vietqrDirectoryApi.logoUrl(imageId)` and, on error
(or when `imageId` is absent — the synthetic unknown-BIN option), swaps to a
neutral initials tile. Used in `Combobox` option rows and the accounts table.

### 2.1 Anatomy

```
 loaded:              fallback (onError / no imageId):
 ┌────────┐           ┌────────┐
 │ [logo] │           │   TC   │   ← initials, or a bank glyph when no name
 └────────┘           └────────┘
 rounded plate,       same plate, neutral fill + centered initials
 hairline border,     (our UI ink — theme-aware)
 object-fit:contain
```

Single square box. State is internal (`useState` `errored`); on `<img onError>`
set `errored=true` and render the fallback instead of the img. When `imageId` is
falsy, render the fallback directly (never attempt the network).

### 2.2 Sizes

| Variant | Box | Used by |
| --- | --- | --- |
| `sm` | `1.5rem` (24px) | dense table cells / `stackOnMobile` card rows |
| `md` (default) | `1.75rem` (28px) | `Combobox` option rows, table (roomy) |
| `lg` | `2.5rem` (40px) | optional — detail/confirmation surfaces (not required this cycle) |

Size is a `size?: "sm" | "md" | "lg"` prop driving a `--bank-logo-size` custom
property on the root; the box is `width:var(--bank-logo-size); height:var(--bank-logo-size); flex:none`.

### 2.3 Plate + image tokens

| Property | Value | Rationale |
| --- | --- | --- |
| shape | `border-radius:var(--fs-radius-sm)` (4px) | logos are square brand marks; a soft radius, not a circle (these are institutions, not people — avoid the avatar/circle read) |
| plate background | **fixed light** `#ffffff` in both themes | bank logos are brand artwork drawn for light backgrounds, often with transparent regions and dark ink; a fixed light plate keeps every logo legible and on-brand in dark mode. Same rationale `ColorPicker` uses literal hex for stored swatch colors. **See OQ-D2.** |
| border | `1px solid var(--fs-color-border-subtle)` (theme-aware) | a hairline scrim so a white logo on a white plate is still delineated from the surface — mirrors the `ColorPicker` swatch `inset 0 0 0 1px` scrim idea |
| padding | `2px` inset (`sm`) / `3px` (`md`+) | keeps the logo from touching the border |
| image | `width:100%; height:100%; object-fit:contain; display:block` | never crop or distort a logo |
| loading | `loading="lazy"` on the `<img>` | off-screen table rows / long lists don't fetch until needed |

### 2.4 Fallback tile

Rendered when the image errors or there is no `imageId`.

| Property | Value |
| --- | --- |
| background | `var(--fs-color-surface-sunken)` (theme-aware — this is our UI, not brand art) |
| border | `1px solid var(--fs-color-border-subtle)` |
| text (initials) | `color:var(--fs-color-text-secondary); font-weight:var(--fs-weight-semibold); line-height:1`; font-size scales with box: `sm`→`var(--fs-text-xs)` (12px), `md`→~13px, `lg`→`var(--fs-text-sm)` |
| glyph (no name) | a generic bank/building glyph in `var(--fs-color-text-muted)` at ~60% of the box, when initials can't be derived |
| shape/size | identical plate to the loaded state (radius, box) so layout never shifts on fallback |

**Initials derivation** (from the passed short name, falling back to the full
name; keep it deterministic and diacritic-preserving for display):

```ts
// Display initials — keep Vietnamese diacritics (this is shown, not searched).
const initialsOf = (name: string): string => {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return ""; // → render the bank glyph
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[1][0]).toUpperCase();
};
// "Techcombank"  → "TE"     "Vietcombank" → "VI"
// "Đông Á"       → "ĐA"     "Bản Việt"    → "BV"
```

The fallback fill is intentionally **neutral** (one calm surface tone), NOT a
per-bank hashed color — a bank tile should read as a quiet placeholder, not a
colorful social avatar, and color-hashing would fight the jade/gold system and add
a CVD burden for no information gain.

### 2.5 Alt text

- `alt` is a **required prop**, supplied by the caller from i18n (the plan's
  `logoAlt` = "Logo {{bank}}" / "{{bank}} logo"). `BankLogo` never invents copy.
- **Inside a labeled row** (the `Combobox` option and the table cell already show
  the bank name as text), the caller passes `alt=""` so the logo is decorative and
  the row is not announced twice. The component must forward an empty string
  faithfully (do not substitute a default).
- **Standalone** (a logo shown without adjacent text — not in this cycle, but the
  contract supports it), pass the meaningful `logoAlt` string.
- The fallback tile: initials are `aria-hidden` (the img's `alt` already carries or
  suppresses the accessible name); the tile is never independently announced.

### 2.6 Light / dark

- **Loaded plate:** fixed light `#ffffff` in both themes (OQ-D2) → colored logos
  render true; the theme-aware hairline border keeps it seated on either ground.
  At 24–28px the light chip reads as a small brand token, not a glaring card.
- **Fallback tile:** fully theme-aware (`surface-sunken` + `border-subtle` +
  `text-secondary`), so it recedes into either theme like the rest of the UI.
- No logo state depends on color alone; the border guarantees the plate edge is
  visible on `surface`, `surface-raised` (popover), and `surface-sunken` grounds.

### 2.7 Props contract

```ts
export type BankLogoProps = {
  imageId?: string;                 // VietQR imageId; falsy → fallback tile directly
  alt: string;                      // i18n; "" when a sibling text labels the bank
  name?: string;                    // short/legal name → fallback initials
  size?: "sm" | "md" | "lg";        // default "md" (28px)
  className?: string;
};
```

The component owns only: URL build (via `vietqrDirectoryApi.logoUrl`), the
`errored` state + `onError` handler, initials derivation, and the plate markup.
No data fetching, no i18n lookups (strings arrive as props).

---

## 3. Decisions to confirm with the user (Open Questions for this spec)

These are **presentation** choices the plan did not pin (the plan's D1–D4 cover
data/UX architecture, not these visuals). Recorded here so the orchestrator can
confirm; none blocks implementation if the recommendation stands.

- **OQ-D1 — Trigger richness.** Recommend the closed `Combobox` trigger shows
  **logo + short name on one line** (ellipsis-truncated), while list rows show the
  full two-line content (short name + legal name · BIN). Alternative: show the full
  two-line block in the trigger too (taller control). *Recommendation: one line in
  the trigger.*
- **OQ-D2 — Logo plate background.** Recommend a **fixed light (`#ffffff`) plate in
  both themes** so bank brand artwork stays legible in dark mode (with a
  theme-aware hairline border). Alternative: a theme-aware plate
  (`--fs-color-surface`) that can make dark-inked transparent logos vanish in dark
  mode. *Recommendation: fixed light plate.*
- **OQ-D3 — Loading affordance placement.** Recommend the background-refresh
  `loading` state renders as a **subtle top-of-listbox hint row** (spinner +
  copy, `aria-live="polite"`), since the snapshot means the list is never actually
  empty. Alternative: a spinner in the trigger. *Recommendation: in-listbox hint.*

---

## 4. Handoff notes

- Both primitives consume **only** `--fs-*` semantic tokens/scales (plus the two
  flagged fixed values: coarse-pointer `2.75rem` heights and the `#ffffff` logo
  plate). No new tokens are required in `tokens.css`.
- `Combobox` is exported from `src/components/ui/index.ts`
  (`Combobox`, `ComboboxProps`, `ComboboxOption`) per the plan; `BankLogo` is
  feature-local to `features/wallet/components/`.
- Theming is automatic: everything resolves through the light/dark token contract
  already in `tokens.css` (both the OS-preference and explicit-toggle blocks) — no
  component-level `@media (prefers-color-scheme)` or `[data-theme]` selectors.
- The `renderOption` node for banks (`makeRenderBankOption`, plan Step 7) composes
  `<BankLogo size="md" alt="">` + `.optionPrimary` (short name) + `.optionSecondary`
  (legal name · `BIN {{bin}}`), following the `pickerOptions.tsx`
  `makeRender*Option` pattern.
```
