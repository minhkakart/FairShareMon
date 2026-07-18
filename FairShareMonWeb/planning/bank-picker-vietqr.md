# Bank picker sourced from the VietQR directory

> **Status: decisions resolved with the user; ready for implementation.** This doc
> formalizes an already-approved plan. The four design questions are recorded as
> **Resolved** (see Decision Log) and must not be reopened. Full approved detail:
> `C:\Users\nguye\.claude\plans\peaceful-nibbling-moore.md`.

## Objective

Replace the error-prone free-text **`bankName`** + **`bankBin`** (6-digit NAPAS BIN)
text inputs in the add/edit bank-account form with **one** searchable, logo-rich
**bank picker** sourced from the VietQR directory. After the change the user only
types **account number** + **account holder name**; the app fills `bankBin` +
`bankName` from the picked bank. Bank logos + short names also render in the
accounts table, re-derived from the stored BIN.

This is a **frontend-only** change. The backend is unchanged: `bank_accounts`
persists only `bankBin` (varchar(6), `^\d{6}$`, = VietQR `caiValue`), free-text
`bankName` (varchar(100)), `accountNumber` (varchar(19), `^\d{6,19}$`), and
`accountHolderName` (varchar(100)). There is **no** logo/shortName/code column and
**no** bank-directory endpoint — the backend was explicitly designed for a
client-side picker (backend planning OQ4a). The submitted request body shape is
**unchanged**: `{ bankBin, bankName, accountNumber, accountHolderName }` for both
create and update.

## Background

Grounded in the live SPA code (read 2026-07-18):

- **Form** — `src/features/wallet/components/BankAccountFormDialog.tsx`: RHF + Zod
  (`bankAccountFormSchema`), four `TextField`s, `FIELD_NAMES = [bankName, bankBin,
  accountNumber, accountHolderName]`. On submit it maps `1001` field errors via
  `applyFieldErrors`, renders an inline `<UpgradePrompt variant="cta">` for the
  stale-tier Premium gate `13003` (form stays open), and toasts+closes on a stale
  `12000` `BankAccountNotFound`. Create + edit are both Premium mutations. Edit
  pre-fills from `account: BankAccountResponse` in a `reset()` on open.
- **Schema** — `src/features/wallet/schemas.ts`: `bankAccountFormSchema(t)` mirrors
  the backend validators; exports `BANK_BIN_PATTERN = /^\d{6}$/` etc. The server
  stays authoritative (a `1001` `error.fields.*` still surfaces on the field).
- **Table** — `src/features/wallet/components/BankAccountsTable.tsx`: renders
  `bankName` + `BIN {{bin}}`, masked/revealable account number, holder, default
  badge, and (Premium only) the set-default / edit / delete actions. Uses
  `<Table stackOnMobile>` (the recent responsive card-stack).
- **Hooks/API** — `src/features/wallet/hooks/useBankAccounts.ts`
  (`useBankAccountsQuery`, create/update/set-default/delete mutations, query-key
  factory `bankAccountsKeys`); `src/features/wallet/api/bankAccountsApi.ts` on the
  centralized `client.ts`; DTOs in `src/features/wallet/api/types.ts`.
- **Design-system primitives** (`src/components/ui/`): `Select` (Radix, uses a
  `renderOption` slot that Radix mirrors selected→trigger), `TagMultiSelect`
  (hand-rolled open/close + outside-pointerdown + Escape via `rootRef`/`useEffect`),
  `ColorPicker` (roving-tabindex keyboard nav: Arrow/Home/End). No searchable
  combobox and no image-with-fallback/avatar primitive exists yet. `pickerOptions.tsx`
  (`src/features/expenses/components/`) is the established `build*Options` +
  `makeRender*Option` pattern for option lists.
- **Env** — `src/config/env.ts` + `src/vite-env.d.ts` expose typed
  `import.meta.env` (`apiBaseUrl`, `enableMocks`, `isDev`).
- **Centralized client** — `src/lib/api/client.ts` is FairShareMonApi-only: it
  injects `Authorization: Bearer <access>`, `X-Time-Zone`, `Accept-Language`, and
  unwraps the `ApiResult<T>` envelope. Sending any of those to a third party (or
  demanding the app envelope from it) is wrong — hence the sanctioned exception
  below.
- **MSW** — `src/test/msw/handlers.ts` uses `onUnhandledRequest: "error"`; every
  URL the code hits under test must have a handler. It already seeds bank accounts
  with BINs `970436` (Vietcombank) + `970407` (Techcombank) and has a `pngResponse()`
  1×1 PNG helper (used by the QR blob path).
- **Prod deployment** — a static SPA (no Node/proxy). There is no server-side
  proxy to launder the VietQR fetch; the live JSON fetch depends on VietQR CORS.

### VietQR contract (as provided; treated as external/unversioned)

`GET https://vietqr.vn/api/vietqr/banks` → an array of
`{ id, bankCode, bankName, bankShortName, imageId, status, caiValue, unlinkedType }`.

- `caiValue` = the 6-digit NAPAS BIN = our `bankBin`.
- Logo image: `https://vietqr.vn/api/vietqr/images/{imageId}` — rendered in an
  `<img>`. Cross-origin **image display** needs no CORS; only the **JSON fetch**
  does (hence the snapshot fallback).
- The fetcher must tolerate **both** a raw array and a `{ data: [...] }` wrapper,
  and **drop** any entry without a valid 6-digit `caiValue`.

## Requirements

- **R1** — The form asks for a bank via **one** searchable picker (filter-as-you-type
  over name / short name / BIN / code), not free-text `bankName` + `bankBin`.
- **R2** — On pick, set `bankBin` = the bank's 6-digit BIN **and** `bankName` = the
  bank's **short name** (e.g. "Techcombank"); the submitted body stays
  `{ bankBin, bankName, accountNumber, accountHolderName }`.
- **R3** — The directory is fetched **live** from VietQR via TanStack Query, seeded
  instantly by a **committed static snapshot** that is also the offline/CORS
  fallback; the picker never empties.
- **R4** — **Picker-only, no manual escape hatch.** Unknown/legacy BINs (not in the
  directory) still display and pre-select gracefully via a synthetic option
  carrying the stored `bankName` + BIN (no logo).
- **R5** — The accounts table shows the bank **logo** + **short name** (re-derived
  from the stored BIN), falling back to the stored `bankName` when the BIN is
  unknown; the existing `stackOnMobile` card-stack + columns are preserved.
- **R6** — No new dependency. The combobox is hand-rolled as a new
  `src/components/ui/` primitive, reusing the `Select` / `TagMultiSelect` /
  `ColorPicker` patterns.
- **R7** — The VietQR fetch is a **dedicated raw-`fetch` module**, never
  `client.ts`. Documented as the one sanctioned exception to the "no scattered
  fetch" rule.
- **R8** — All user-facing copy through i18n (vi-VN default + en-US parity). Money
  and time formatting untouched.
- **R9** — Full combobox accessibility (ARIA + keyboard) and the responsive
  baselines (44px coarse-pointer targets, `prefers-reduced-motion`).
- **R10** — Server stays authoritative: the Zod schema keeps the server-mirrored
  `bankBin`/`bankName` rules; a `1001` `error.fields.*` still surfaces on the field.

## Open Questions

**None open.** The four design questions were resolved with the user before this
doc was written and are recorded in the Decision Log as Resolved (D1–D4). If
implementation surfaces anything the doc does not cover and a reasonable engineer
would ask, stop and record it here rather than pick a silent default.

## Assumptions

- The backend needs **no** change — confirmed with the user (backend planning
  OQ4a). No new endpoint, DTO, validator, or migration.
- VietQR's `GET /api/vietqr/banks` returns roughly ~60 Vietnamese banks and is the
  same directory the backend author assumed; `caiValue` is a stable 6-digit NAPAS
  BIN. The endpoint is external/unversioned and may change shape (hence tolerate
  both array and `{ data }` wrapper, and drop invalid BINs).
- VietQR image URLs are publicly served and need no auth; `<img>` cross-origin
  loads work without CORS headers.
- The seeded BINs `970436` (Vietcombank) + `970407` (Techcombank) — already present
  in the wallet MSW seed — exist in the VietQR directory, so the round-trip
  (pick → store → table re-derive) is demonstrable end-to-end.
- The snapshot is captured **during implementation** by calling the live endpoint
  once; the Progress Log records the capture date so it can be refreshed later.
- The locked stack (`frontend-foundation.md`) is unchanged: React 19 + Compiler
  (no manual memo), TanStack Query v5, RHF + Zod, CSS Modules + tokens + Radix,
  react-i18next.

## Data contract

### VietQR raw response (external)

```ts
// one entry of the array (or of `{ data: [...] }`)
type VietqrRawBank = {
  id: number;
  bankCode: string;        // e.g. "TCB"  (our `code`)
  bankName: string;        // full legal name
  bankShortName: string;   // e.g. "Techcombank"  (persisted into our bankName)
  imageId: string;         // logo id → https://vietqr.vn/api/vietqr/images/{imageId}
  status: number;
  caiValue: string;        // 6-digit NAPAS BIN = our bankBin
  unlinkedType: string;
};
```

### Normalized internal shape (what the app uses everywhere)

```ts
export type VietqrBank = {
  bin: string;       // caiValue, validated `^\d{6}$`  → our bankBin
  code: string;      // bankCode  → search keyword
  name: string;      // bankName (full)  → search keyword + secondary line
  shortName: string; // bankShortName  → persisted into bankName, shown as primary
  imageId: string;   // logo id
};
```

Normalization drops any entry whose `caiValue` is not exactly 6 digits.

### Bank-account request body (UNCHANGED — the contract we submit)

```ts
// CreateBankAccountRequest === UpdateBankAccountRequest
{ bankBin: string; bankName: string; accountNumber: string; accountHolderName: string }
```

`bankBin` = picked `VietqrBank.bin`; `bankName` = picked `VietqrBank.shortName`
(D3). Account number + holder come from the two retained `TextField`s.

## Implementation Plan

> All paths under `FairShareMonWeb/`. Copy is illustrative — the real strings live
> in the i18n resources. Assumes the resolved decisions D1–D4.

### Step 1 — Env plumbing for the VietQR base URL

1. `src/vite-env.d.ts` — add `readonly VITE_VIETQR_BASE_URL?: string;` to
   `ImportMetaEnv` with a doc comment (external directory origin; defaults to
   `https://vietqr.vn`).
2. `src/config/env.ts` — add `vietqrBaseUrl:
   (import.meta.env.VITE_VIETQR_BASE_URL ?? "https://vietqr.vn").replace(/\/+$/, "")`
   to the `env` object. No throw (unlike `apiBaseUrl`): a missing value falls back
   to the public default.

### Step 2 — The sanctioned raw-fetch directory module (R3, R7)

`src/features/wallet/api/vietqrDirectoryApi.ts` (new). Heavily commented as the
**one** exception to the "always use `client.ts`" rule (a third party must not
receive our Bearer/timezone/locale headers and does not speak `ApiResult<T>`).

- `list(signal?): Promise<VietqrBank[]>`:
  - `fetch(\`${env.vietqrBaseUrl}/api/vietqr/banks\`, { signal, headers: { Accept: "application/json" } })`
    — no auth, no app headers.
  - On non-ok → throw (the hook catches and falls back to the snapshot).
  - Parse JSON, accept **either** a raw array **or** `{ data: [...] }`.
  - Map `VietqrRawBank → VietqrBank`, `filter` to valid 6-digit `caiValue`.
- `logoUrl(imageId): string` → `\`${env.vietqrBaseUrl}/api/vietqr/images/${imageId}\``.

### Step 3 — Committed snapshot (R3 seed + fallback)

`src/features/wallet/data/vietqrBanks.ts` (new). `export const VIETQR_BANKS_SNAPSHOT:
VietqrBank[]` — the full ~60-bank list in the normalized shape, **generated during
implementation** by calling the live endpoint once and baking the result. Include
the seeded BINs `970436` (Vietcombank) + `970407` (Techcombank). A header comment
records the capture date + source URL so it can be refreshed. This is the instant
seed AND the offline/CORS fallback (the resilience story for the static-SPA prod).

### Step 4 — The directory query hook (R3, R4)

`src/features/wallet/hooks/useVietqrBanks.ts` (new):

- `useVietqrBanks()`:
  ```ts
  useQuery({
    queryKey: ["vietqr-banks"],
    queryFn: async ({ signal }) => {
      try {
        const banks = await vietqrDirectoryApi.list(signal);
        return banks.length > 0 ? banks : VIETQR_BANKS_SNAPSHOT;
      } catch {
        return VIETQR_BANKS_SNAPSHOT; // never empty the picker (CORS/offline)
      }
    },
    initialData: VIETQR_BANKS_SNAPSHOT,
    initialDataUpdatedAt: 0,   // treated as stale → one background refresh
    staleTime: 24 * 60 * 60 * 1000,
    gcTime: 24 * 60 * 60 * 1000,
    retry: 1,
  });
  ```
- `useBankByBin(bin: string | undefined): VietqrBank | undefined` — a selector over
  the same query data (`useVietqrBanks().data?.find(b => b.bin === bin)`), used by
  the table to re-derive logo + short name without another fetch.

### Step 5 — The `Combobox` primitive (R6, R9)

`src/components/ui/Combobox/Combobox.tsx` + `Combobox.module.css`; export from
`src/components/ui/index.ts` (`Combobox`, `ComboboxProps`, `ComboboxOption`).

Generic `Combobox<Meta>` mirroring `Select`'s prop shape so it drops into the same
form conventions:

```ts
export type ComboboxOption<Meta = unknown> = {
  value: string;
  label: string;
  keywords?: string[];   // extra searchable text (full name, BIN, code)
  meta?: Meta;
  disabled?: boolean;
};
export type ComboboxProps<Meta = unknown> = {
  value: string | undefined;
  onValueChange: (value: string) => void;
  options: ComboboxOption<Meta>[];
  label: ReactNode;
  placeholder?: string;
  searchPlaceholder?: string;   // input placeholder when open
  emptyLabel?: string;          // "no matches" copy
  loading?: boolean;            // shows a spinner/hint (background refresh)
  hint?: ReactNode;
  error?: ReactNode;
  required?: boolean;
  disabled?: boolean;
  hideLabelVisually?: boolean;
  name?: string;
  id?: string;
  className?: string;
  renderOption?: (option: ComboboxOption<Meta>) => ReactNode;
  ref?: Ref<HTMLInputElement>;
};
```

Behavior — **reuse, don't reinvent**:

- Trigger/selected-mirroring + token styling copied from `Select` (render the
  selected option's `renderOption` content in the trigger).
- Open/close + outside-`pointerdown` + `Escape` from `TagMultiSelect`
  (`rootRef` + `useEffect`).
- Roving keyboard nav from `ColorPicker`.
- Filter **case- and diacritic-insensitively** (normalize via
  `String.prototype.normalize("NFD")` + strip combining marks) over `label` +
  `keywords`.

ARIA / keyboard (reviewer will check):

- `role="combobox"` input with `aria-expanded`, `aria-controls`,
  `aria-autocomplete="list"`, `aria-activedescendant`; popover `role="listbox"`,
  rows `role="option"` with per-row ids + `aria-selected`.
- Keyboard: ArrowUp/Down move the active option, Enter selects, Escape closes,
  Home/End jump; typing filters.
- Label wiring (`aria-labelledby`) + `aria-invalid` + `aria-describedby` exactly
  like `Select`/`TextField`.
- `prefers-reduced-motion`; 44px min coarse-pointer targets.

### Step 6 — `BankLogo` (R5)

`src/features/wallet/components/BankLogo.tsx` (new). `<img>` on
`vietqrDirectoryApi.logoUrl(imageId)` with `loading="lazy"`, an i18n `alt`, and an
`onError` fallback to a neutral glyph / bank initials (no shared avatar primitive
exists). Small, presentational; used in both the combobox rows and the table.
When there is no `imageId` (synthetic unknown-BIN option) it renders the fallback
directly.

### Step 7 — Option builders + renderer (match `pickerOptions.tsx`)

Add a small helper (co-located with the form, e.g.
`src/features/wallet/components/bankOptions.tsx`) in the established
`build*Options` + `makeRender*Option` spirit:

- `buildBankOptions(banks: VietqrBank[]): ComboboxOption<VietqrBank>[]` — `value =
  bin`, `label = shortName`, `keywords = [name, bin, code]`, `meta = bank`.
- `makeRenderBankOption(t)` → a row: `<BankLogo>` + short name (primary) + full
  name / BIN (secondary line).

### Step 8 — Form rework (R1, R2, R4, R10)

`src/features/wallet/components/BankAccountFormDialog.tsx`:

- Replace the two `TextField`s (`bankName`, `bankBin`) with **one**
  `<Controller name="bankBin">`-wrapped `Combobox` (options from
  `buildBankOptions(useVietqrBanks().data)`, `loading` = query `isFetching`,
  `renderOption` from `makeRenderBankOption`).
- On select: `field.onChange(bank.bin)` **and** `setValue("bankName",
  bank.shortName, { shouldValidate: true })` — keeps the hidden `bankName` in the
  form state so the submitted body is unchanged. Keep `accountNumber` +
  `accountHolderName` `TextField`s as-is.
- `FIELD_NAMES` stays `[bankName, bankBin, accountNumber, accountHolderName]` so
  server `1001` field errors still map (a server `bankName`/`bankBin` error surfaces
  on the combobox via the `bankBin` field error slot; `bankName` errors, if any,
  surface form-level through the existing fallback).
- **Edit prefill (R4):** on open, match `account.bankBin` to a directory entry. If
  present → preselect. If **absent** (legacy/edited BIN) → inject a **synthetic**
  `ComboboxOption` `{ value: account.bankBin, label: account.bankName, keywords:
  [account.bankBin], meta: undefined }` prepended to the options so the stored value
  displays (no logo) and nothing is lost.
- `autoFocus` moves from the old bank-name field to the combobox trigger.

### Step 9 — Schema copy tweak (R10)

`src/features/wallet/schemas.ts`: keep all server-mirrored rules (the combobox
guarantees a valid 6-digit BIN + non-empty short name, but the server stays
authoritative). Change the `bankBin` "required" message to a "select a bank"
phrasing (new validation key, Step 11). `bankName` rules unchanged (now populated
by the picker, still validated).

### Step 10 — Table display (R5)

`src/features/wallet/components/BankAccountsTable.tsx`: in the bank cell, render
`<BankLogo imageId={useBankByBin(account.bankBin)?.imageId}>` + the re-derived
`shortName` (`useBankByBin(account.bankBin)?.shortName ?? account.bankName`), then
the `BIN {{bin}}` secondary line. Additive display change; keep the masked-number
reveal, default badge, actions, and `stackOnMobile` card-stack untouched. (Derive
in the row via the selector; React Compiler handles memoization.)

### Step 11 — i18n (R8)

`src/i18n/locales/{vi-VN,en-US}/wallet.json`:

- Add a `form.bankPicker` block: `label`, `placeholder` (trigger), `searchPlaceholder`,
  `emptyLabel` ("Không tìm thấy ngân hàng" / "No matching bank"), `loading`.
- Add `logoAlt` (e.g. "Logo {{bank}}" / "{{bank}} logo") for `BankLogo`.
- Remove the now-unused `form.binLabel` / `form.binPlaceholder` / `form.binHint`
  and `form.bankNameLabel` / `form.bankNamePlaceholder` (or repurpose them into the
  picker block). Keep `table.bin`.

`src/i18n/locales/{vi-VN,en-US}/validation.json`: adjust
`bankAccount.binRequired` → a "Vui lòng chọn ngân hàng." / "Please select a bank."
message (the field is now a picker, not a typed code). Keep `binPattern` (server-
mirrored). **vi-VN + en-US parity for every added/changed key.**

### Step 12 — MSW handlers (test + dev mock-mode)

`src/test/msw/handlers.ts` — add **absolute-URL** handlers (the test server uses
`onUnhandledRequest: "error"`, and dev mock-mode should work offline):

- `http.get("https://vietqr.vn/api/vietqr/banks", …)` → a **raw array** (NOT the
  app envelope) of a few `VietqrRawBank`s, including the seeded BINs `970436`
  (Vietcombank) + `970407` (Techcombank), plus one entry with an invalid `caiValue`
  to exercise the drop filter.
- `http.get("https://vietqr.vn/api/vietqr/images/:imageId", …)` → reuse the existing
  `pngResponse()` 1×1 PNG.

### API endpoints consumed this cycle

| Screen / hook | Verb + URL | Auth / headers | Response | Notes |
| --- | --- | --- | --- | --- |
| `useVietqrBanks` (picker + table) | `GET https://vietqr.vn/api/vietqr/banks` | **none** (raw fetch, no app headers) | raw array (or `{data:[]}`) of `VietqrRawBank` | not `ApiResult<T>`; failure → snapshot fallback |
| `BankLogo` | `GET https://vietqr.vn/api/vietqr/images/{imageId}` | none | PNG in `<img>` | `onError` → glyph fallback |
| Form submit (unchanged) | `POST /api/v1/wallet/bank-accounts` · `PUT /api/v1/wallet/bank-accounts/{uuid}` | Bearer (via `client.ts`) | `ApiResult<BankAccountResponse>` | body `{bankBin,bankName,accountNumber,accountHolderName}`; `1001`→fields, `13003`→UpgradePrompt, `12000`→toast+close |

### Loading / empty / error states

- **Picker:** the snapshot is `initialData`, so the list is populated on first
  paint (no spinner-blocked open). A background refresh sets `loading` (subtle
  hint), never empties. A failed live fetch is silent — the snapshot stays.
- **Empty search:** the combobox shows `emptyLabel` when no option matches the
  query.
- **Logo:** `onError` → neutral fallback (never a broken-image icon).
- **Form errors:** unchanged — `1001` field mapping, `13003` inline UpgradePrompt,
  `12000` toast+close, else form-level error via `resolveErrorMessage`.

### Form validation rules (mirror the backend)

- `bankBin` — required ("select a bank") + `^\d{6}$`; guaranteed by the picker but
  validated for the synthetic/edge path. Server authoritative.
- `bankName` — required, ≤100; now set from `shortName` on pick.
- `accountNumber` — required, `^\d{6,19}$` (unchanged `TextField`).
- `accountHolderName` — required, ≤100 (unchanged `TextField`).

### Accessibility

- Combobox: full `combobox`/`listbox`/`option` ARIA, `aria-activedescendant`,
  Arrow/Enter/Escape/Home/End, label + `aria-invalid`/`aria-describedby` parity with
  `Select`/`TextField`, `prefers-reduced-motion`, 44px coarse-pointer targets.
- `BankLogo`: meaningful `alt` (or `alt=""` + adjacent text — decide so the row
  isn't double-announced; the option row already carries the bank name, so the logo
  can be decorative `alt=""` inside the row and labeled only when standalone).
- Long Vietnamese bank names: generous line-height + `overflow-wrap`.

### Tests the web-test-engineer should write (Vitest + RTL + MSW; deterministic, pinned TZ + locale)

- **`vietqrDirectoryApi.list`** — maps a raw array → `VietqrBank[]`; tolerates the
  `{ data: [...] }` wrapper; **drops** entries with an invalid `caiValue`; throws on
  a non-ok response.
- **`useVietqrBanks`** — returns the snapshot as `initialData` before any fetch;
  the live success replaces it (mapped); a **fetch failure** (MSW error) falls back
  to the snapshot so `data` is never empty; `useBankByBin` finds a seeded BIN and
  returns `undefined` for an unknown one.
- **`Combobox`** — filters case/diacritic-insensitively over label + keywords;
  keyboard nav (ArrowDown/Up moves active, Enter selects, Escape closes, Home/End);
  outside-click closes; `emptyLabel` shows on no match; `loading` state; ARIA roles
  (`combobox`/`listbox`/`option`, `aria-expanded`, `aria-activedescendant`,
  `aria-invalid` when errored); selection emits the chosen `value`.
- **`BankLogo`** — renders the img at the derived URL; `onError` swaps to the
  fallback glyph; renders the fallback directly when `imageId` is absent.
- **`BankAccountFormDialog`** — selecting a bank sets `bankBin` **and** `bankName`
  (short name), and the **submitted body is unchanged** (`{bankBin, bankName,
  accountNumber, accountHolderName}`); typing "techcom"/"vcb" filters to the right
  bank; edit prefill preselects by BIN; an **unknown/legacy BIN** injects a
  synthetic option that displays + preselects (no logo) and still submits; the
  `13003` inline UpgradePrompt and `12000` toast+close paths still work.
- **`BankAccountsTable`** — shows the logo + re-derived short name for a known BIN;
  falls back to the stored `bankName` for an unknown BIN; the `stackOnMobile` +
  reveal + default + actions behavior is unchanged.
- **Schema** — `bankAccountFormSchema` still enforces the four server-mirrored
  rules; the bank "required" message is the new "select a bank" copy.
- **i18n parity** — every new/changed key exists in **both** `vi-VN` and `en-US`
  for `wallet.json` + `validation.json`.
- **E2E (optional)** — extend `e2e/wallet-responsive.spec.ts` / the ledger-loop to
  pick a bank via the combobox (MSW-mocked, vi-VN + Asia/Ho_Chi_Minh); decide during
  the test step.

## Impact Analysis

- **APIs:** no backend change. Consumes two external VietQR GETs (raw); the wallet
  mutation contracts are untouched (same body shape).
- **New source:** `features/wallet/api/vietqrDirectoryApi.ts`,
  `features/wallet/data/vietqrBanks.ts`, `features/wallet/hooks/useVietqrBanks.ts`,
  `components/ui/Combobox/{Combobox.tsx,Combobox.module.css}`,
  `features/wallet/components/BankLogo.tsx`,
  `features/wallet/components/bankOptions.tsx`.
- **Changed source:** `config/env.ts`, `vite-env.d.ts`, `components/ui/index.ts`,
  `features/wallet/components/BankAccountFormDialog.tsx`,
  `features/wallet/components/BankAccountsTable.tsx`, `features/wallet/schemas.ts`,
  `i18n/locales/{vi-VN,en-US}/wallet.json`,
  `i18n/locales/{vi-VN,en-US}/validation.json`, `test/msw/handlers.ts`.
- **Business rules:** wallet mutations remain Premium-gated (`13003` →
  UpgradePrompt); resource-owned `12000` → toast+close; server stays authoritative
  on validation. Closed-event immutability + admin rules are unaffected (wallet
  scope). No money/time formatting change.
- **Resilience:** prod is a static SPA with no proxy — the live JSON fetch depends
  on VietQR CORS; the committed snapshot is the fallback so the picker always works.
  Logo `<img>` loads need no CORS.
- **GitNexus mandate (implementer):** run `gitnexus_impact({direction:"upstream"})`
  on `BankAccountFormDialog`, `BankAccountsTable`, and `bankAccountFormSchema`
  before editing; report the blast radius and warn on HIGH/CRITICAL; run
  `gitnexus_detect_changes()` before commit.
- **Risk:** low–moderate. Moderate only in that the `Combobox` is a new
  hand-rolled a11y-critical primitive; mitigated by copying three proven primitives
  and a dedicated test suite.

## Decision Log

### D1 — Data source: live VietQR fetch + committed snapshot seed/fallback — **Resolved (with the user)**

Fetch `https://vietqr.vn/api/vietqr/banks` live via TanStack Query, seeded instantly
by a committed static snapshot that doubles as the offline/CORS fallback.
**Rationale:** always-fresh directory when reachable; instant, resilient picker in
the static-SPA prod (no proxy) even when VietQR CORS/network fails. Rejected
alternatives: snapshot-only (goes stale, no new banks) and live-only (empty picker
on CORS/offline — unacceptable).

### D2 — Picker UX: hand-rolled searchable `Combobox`, no new dependency — **Resolved (with the user)**

Build `src/components/ui/Combobox/` from scratch, reusing `Select`'s
renderOption→trigger mirroring, `TagMultiSelect`'s open/close + outside-click +
Escape, and `ColorPicker`'s roving keyboard nav. **Rationale:** the foundation
locked the dependency set; adding a combobox library would be an Open Question. The
three existing primitives already cover the hard parts. Trade-off: we own the
combobox a11y — mitigated by tests + reviewer a11y pass.

### D3 — Stored name: persist `bankShortName` into `bankName` — **Resolved (with the user)**

Write the VietQR `bankShortName` (e.g. "Techcombank") into the unchanged `bankName`
field. **Rationale:** short names are what users recognize and what fits the table /
QR surfaces; the backend column is free-text so no contract change.

### D4 — No manual escape hatch; graceful unknown/legacy BINs — **Resolved (with the user)**

The picker is the only input path. Unknown/legacy BINs (not in the directory) still
display + pre-select via a synthetic option carrying the stored `bankName` + BIN
(no logo). **Rationale:** eliminates the typo-misroute failure mode while never
losing an already-stored account's data on edit.

### D5 — Dedicated raw-fetch module (not `client.ts`) — **Resolved (derived from CLAUDE.md)**

The VietQR fetch lives in `vietqrDirectoryApi.ts`, never the centralized client.
**Rationale:** `client.ts` is FairShareMonApi-only — it injects Bearer/timezone/
locale headers and demands the `ApiResult<T>` envelope; sending those to a third
party or expecting the envelope back is wrong. Documented in-module as the single
sanctioned exception.

## Progress Log

### 2026-07-18

- Feature-planner: required reading completed — the approved plan
  (`peaceful-nibbling-moore.md`); `FairShareMonWeb/CLAUDE.md` (locked stack, the
  `client.ts` "no scattered fetch" rule, money/time/i18n/a11y baselines); the live
  wallet feature (`BankAccountFormDialog.tsx`, `BankAccountsTable.tsx`,
  `schemas.ts`, `hooks/useBankAccounts.ts`, `api/types.ts`); the design-system
  primitives to mirror (`Select.tsx`, `TagMultiSelect.tsx`, `ColorPicker.tsx`,
  `components/ui/index.ts`); the option-builder pattern
  (`expenses/components/pickerOptions.tsx`); `config/env.ts` + `vite-env.d.ts`; the
  wallet + validation i18n resources (both locales); and the MSW harness
  (`handlers.ts` — `onUnhandledRequest:"error"`, seeded BINs 970436/970407,
  `pngResponse()`).
- Confirmed the backend needs no change (body shape `{bankBin, bankName,
  accountNumber, accountHolderName}` unchanged; validators `bankBin ^\d{6}$`, names
  ≤100).
- Authored this plan: env `vietqrBaseUrl`; raw-fetch `vietqrDirectoryApi`; committed
  `vietqrBanks` snapshot; `useVietqrBanks` + `useBankByBin`; `Combobox` primitive;
  `BankLogo`; form rework (Controller-wrapped combobox bound to `bankBin`, sets
  `bankName`=shortName, synthetic-option edit prefill); schema copy tweak; table
  logo + short-name re-derivation; i18n keys (both locales); MSW absolute-URL
  handlers.
- **Open Questions: none** — the four design decisions (D1–D4) were resolved with
  the user before drafting and are recorded as Resolved. Ready for the ui-designer /
  web-implementer.
- ui-designer: authored the visual/interaction spec for the two new design-system
  pieces — `planning/bank-picker-vietqr-design.md`. Covers the `Combobox`
  primitive (anatomy, token map reusing the `Select`/`TagMultiSelect` field family
  + shared popover surface, full state matrix, the ARIA-1.2 combobox-with-listbox
  pattern using `aria-activedescendant` with a keyboard table, the case/diacritic-
  insensitive + `đ→d` fold filtering rule, 360px/44px-coarse responsive behavior,
  and the `ComboboxProps<Meta>` contract mirroring `Select`) and `BankLogo`
  (sizes, fixed-light logo plate + theme-aware initials fallback, alt-text
  guidance, props). No new `--fs-*` tokens needed. Three presentation Open
  Questions recorded for the orchestrator to confirm: OQ-D1 trigger richness
  (one-line vs two-line), OQ-D2 logo plate background (fixed `#ffffff` vs
  theme-aware — recommend fixed), OQ-D3 loading-affordance placement.

### 2026-07-18 — web-implementer (feature built)

- **Snapshot source (Step 3): FULL live capture.** Fetched
  `https://vietqr.vn/api/vietqr/banks` once on 2026-07-18 (reachable from the
  shell); 66 entries returned, **58 kept** after normalize (8 dropped for a
  non-6-digit `caiValue`), sorted by short name, baked into
  `features/wallet/data/vietqrBanks.ts` with a capture-date + source header.
  Includes both seeded BINs (970436 Vietcombank, 970407 Techcombank). Note: the
  live directory lists two entries under caiValue 970452 (both KienLongBank) —
  `buildBankOptions` dedupes by BIN so the picker/keys stay unique.
- **Files created:** `config/env.ts` (+`vietqrBaseUrl`), `vite-env.d.ts`
  (+`VITE_VIETQR_BASE_URL`), `features/wallet/api/vietqrDirectoryApi.ts` (raw-fetch
  module + `bankLogoUrl`), `features/wallet/data/vietqrBanks.ts` (snapshot),
  `features/wallet/hooks/useVietqrBanks.ts` (`useVietqrBanks` + `useBankByBin`),
  `components/ui/Combobox/{Combobox.tsx,Combobox.module.css}` (+ export from
  `components/ui/index.ts`), `features/wallet/components/BankLogo.tsx`
  (+`BankLogo.module.css`), `features/wallet/components/bankOptions.tsx`
  (+`bankOptions.module.css`).
- **Files changed:** `features/wallet/components/BankAccountFormDialog.tsx`
  (Controller-wrapped `Combobox` bound to `bankBin`; on select sets `bankBin` +
  `bankName`=shortName; synthetic-option edit prefill for legacy/unknown BINs;
  1001/13003/12000 handling preserved), `features/wallet/components/BankAccountsTable.tsx`
  (+ `BankLogo` + short-name re-derived via the directory query; card-stack /
  columns / actions untouched) + its `.module.css`, `features/wallet/schemas.ts`
  (`bankBin` required message → `selectBank`), `i18n/locales/{vi-VN,en-US}/wallet.json`
  (added `form.bankPicker.*` + `form.logoAlt`; removed the old bankName/BIN field
  copy), `i18n/locales/{vi-VN,en-US}/validation.json` (`binRequired`→`selectBank`),
  `test/msw/handlers.ts` (absolute-URL VietQR banks (raw array, incl. an invalid
  BIN to exercise the drop filter) + images handlers).
- **Existing tests updated** (broken only by the intentional form redesign, not
  new feature tests): `schemas.test.ts` + `walletI18n.test.ts` (key rename),
  `bankAccountFormDialog.test.tsx` + `walletPage.test.tsx` (drive the combobox
  instead of the removed text fields; the "bad BIN typed" case is now impossible
  via the picker → repurposed to a bad-account-number client-validation case).
- **Decisions built to (from the orchestrator/ui-designer):** OQ-D1 one-line
  trigger / two-line rows (via a `data-combobox-value` slot hook + wallet CSS),
  OQ-D2 fixed `#ffffff` logo plate + theme-aware border, OQ-D3 in-listbox
  `aria-live` loading hint; `aria-activedescendant` focus model; case/diacritic +
  `đ→d` insensitive filtering.
- **Verification:** `pnpm exec tsc -b` clean; `pnpm lint` clean (one
  only-export-components *warning* on `Combobox` co-exporting `normalizeForSearch`
  — matches the existing ColorPicker/IconPicker pattern); `pnpm build` succeeds;
  `pnpm test` **783 passed (93 files)**. Ran the real app (Vite dev + MSW mock
  mode, vi-VN) via a throwaway Playwright drive: opened the wallet, opened the Add
  dialog, filtered the picker ("mbbank"), selected MBBank, filled account# +
  holder, submitted → success toast + new "MBBank" row; seeded rows show the
  BankLogo plate (initials fallback when the cross-origin logo can't load in the
  sandbox — the designed `onError` degradation). Screenshot confirmed the layout.

### 2026-07-18 — web-test-engineer (new coverage added)

- Added **47 new tests** for the bank-picker feature; full suite now **830 passed
  (99 files)**, up from the 783 baseline. `pnpm lint` clean (only pre-existing
  `only-export-components` warnings, incl. the sanctioned `Combobox`↔
  `normalizeForSearch` co-export), `tsc -b` clean.
- **New test files:**
  - `src/components/ui/Combobox/combobox.test.tsx` (23) — `Combobox` primitive
    (open on click/ArrowDown/Enter, search focus, filter by label + diacritic/đ→d
    keyword, empty-label, aria-activedescendant ArrowUp/Down + Home/End + Enter
    select, click select, Escape + outside-pointerdown close, selected mirrored
    into trigger, aria-selected, disabled-blocks-open, error→aria-invalid/
    aria-describedby + role=alert, hint→aria-describedby, loading string in the
    aria-live hint, roles present) **and** `normalizeForSearch` unit cases
    (case-insensitive, strip Vietnamese diacritics, đ→d fold, digits pass through,
    substring-match enablement).
  - `src/features/wallet/vietqrDirectoryApi.test.ts` (6) — normalizes a raw array +
    maps every field; unwraps `{data:[…]}`; drops non-6-digit `caiValue`; throws on
    non-ok + unreadable shape; `bankLogoUrl` builds the image URL (MSW at boundary).
  - `src/features/wallet/useVietqrBanks.test.tsx` (6) — live success replaces the
    snapshot with the mapped list; fetch error → snapshot fallback (never empty);
    empty live list keeps the snapshot; `useBankByBin` selects a seeded BIN and
    returns undefined for unknown/undefined.
  - `src/features/wallet/bankLogo.test.tsx` (4) — lazy `<img>` at the derived URL;
    `onError` → initials fallback; no `imageId` → initials tile directly; no name →
    glyph.
  - `src/features/wallet/bankAccountsTable.test.tsx` (2) — known BIN shows the
    re-derived short name (over a stale stored name); unknown BIN falls back to the
    stored `bankName`.
  - `src/features/wallet/bankOptions.test.tsx` (2, extra) — `buildBankOptions`
    dedupes duplicate BINs (keeps first) + maps value/label/keywords/meta.
- **Extended existing files:** `bankAccountFormDialog.test.tsx` (+2 — picking a bank
  submits `bankBin` + `bankName`=shortName in the unchanged body, asserted via the
  MSW POST body; an unknown/legacy BIN pre-selects a synthetic option in the trigger
  and round-trips the stored BIN + name in the PUT body); `walletI18n.test.ts` (+2 —
  `form.bankPicker.*` + `form.logoAlt` present/non-empty in both locales;
  `validation:bankAccount.selectBank` present in both).
- **No product defects found.** One test-harness note: computing accessible names
  over the fully-open 58-item directory listbox mid-render trips a
  dom-accessibility-api/jsdom `getElementById`-on-detached-node quirk (NOT a product
  bug — the `Combobox` primitive specs drive the same ARIA cleanly on a small list);
  the synthetic-option form spec therefore asserts display via the trigger + the
  submitted body rather than enumerating the giant listbox.

### 2026-07-18 — web-implementer (review fix pass)

Applied the 4 code-review fixes (no blocking issues found):

1. **Combobox `Tab` now closes the panel** (`components/ui/Combobox/Combobox.tsx`):
   added a `case "Tab"` in `onSearchKeyDown` that `setOpen(false)` WITHOUT
   `preventDefault` (focus still advances), plus an `onBlur` (`focusout`) handler
   on the field root that closes when focus leaves the whole control (covers
   Shift+Tab). Prevents the absolutely-positioned panel overlapping the field
   below. New test `Combobox_TabOutOfSearch_ClosesPanelAndAdvancesFocus`.
2. **Table aria-labels use the re-derived `displayName`**
   (`BankAccountsTable.tsx`): the reveal/hide + set-default/edit/delete
   `aria-label`s now pass `{ bank: displayName }` (was `account.bankName`) so SR
   text matches the visible cell when the stored name is stale.
3. **Empty-results row announced** (`Combobox.tsx`): the `emptyLabel` `<li>` now
   carries `aria-live="polite"` (design §1.4) so "no matching bank" is spoken when
   a query filters everything out. (Kept a single text node — an initial
   duplicate-hidden-announcer attempt double-rendered the text and broke an
   existing test; reverted to the reviewer's literal ask.)
4. **AbortError no longer swallowed into the snapshot**
   (`hooks/useVietqrBanks.ts`): the `signal` was already wired into
   `vietqrDirectoryApi.list(signal)` → `fetch`; the queryFn catch now rethrows
   when `signal?.aborted` or the error is a `DOMException` `AbortError` (so a
   cancelled refetch is treated as a cancellation, not a snapshot "success" that
   could clobber fresher live data), and only falls back to the snapshot on a real
   fetch failure. New API test `VietqrDirectoryApi_AbortedSignal_RejectsWithAbortError`
   proves the signal reaches `fetch` (a React-Query-cancellation hook test would
   be non-deterministic in jsdom).

Left `wallet:form.logoAlt` in place (intentionally future-facing per the design
contract).

**Verification (fnm PATH prefix):** `pnpm exec tsc -b` clean; `pnpm lint` clean
(only the pre-existing `only-export-components` warnings, incl. Combobox's
`normalizeForSearch` co-export — matches ColorPicker/IconPicker); `pnpm test`
**832 passed (99 files)**.

## Final Outcome

(pending)

## Future Improvements

- **Backend bank-directory endpoint** (deferred backend OQ4b): serve the directory
  (with logos) from FairShareMonApi so the SPA drops the third-party dependency and
  CORS risk entirely; the client would then use `client.ts` normally.
- **Bundle logo images** for full offline logo rendering (today only the JSON has a
  snapshot fallback; logos still need network).
- **Snapshot refresh cadence** — a small script / CI step to regenerate
  `vietqrBanks.ts` from the live endpoint so the committed seed doesn't drift.
- **Retrofit other `Select`s** to the searchable `Combobox` where long option lists
  would benefit (payer/category pickers) — out of scope here.
- **Visual-regression snapshots** for the combobox + logo rows once a VR harness
  exists.
