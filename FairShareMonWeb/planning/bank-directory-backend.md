# Bank directory sourced from the backend `/v1/banks` endpoint

> **Status: approach APPROVED — ready for implementation.** This doc formalizes an
> already-approved plan (the deferred "backend OQ4b" from
> `planning/bank-picker-vietqr.md`). It replaces the SPA's direct third-party
> VietQR fetch with a call to our own `GET /api/v1/banks` through the centralized
> client. There are no open design questions (see Open Questions / Decision Log).

## Objective

Stop the SPA from talking to `https://vietqr.vn` directly. The backend now owns the
bank directory behind a provider abstraction and exposes it at
`GET /api/v1/banks` (authenticated, standard `ApiResult<T>` envelope, logo URLs
built server-side). The wallet bank picker + accounts table must consume **our**
endpoint via `src/lib/api/client.ts` (auth, refresh, `X-Time-Zone`,
`Accept-Language`, envelope unwrap all handled centrally) instead of the raw
cross-origin `fetch` + committed snapshot + offline-fallback machinery introduced
in `bank-picker-vietqr.md`.

This is a **frontend-only** change. No backend change (the endpoint is
feature-complete). No change to the wallet mutation contract
(`{ bankBin, bankName, accountNumber, accountHolderName }` is unchanged) and **no
change to the QR flow** (QR PNGs still come from `/v1/expenses/{uuid}/qr` and
`/v1/events/{uuid}/qr` via `api.blob`).

## Background

Grounded in the live code (read 2026-07-19):

### What exists today (the VietQR client-side path — to be removed/replaced)

- `src/features/wallet/api/vietqrDirectoryApi.ts` — the sanctioned raw-`fetch`
  exception. Exports `interface VietqrBank { bin, code, name, shortName, imageId }`,
  `const vietqrDirectoryApi = { list(signal?): Promise<VietqrBank[]> }` (hits
  `${env.vietqrBaseUrl}/api/vietqr/banks`, tolerates raw array or `{ data }`,
  normalizes + drops non-6-digit `caiValue`), and
  `function bankLogoUrl(imageId): string` (builds
  `${env.vietqrBaseUrl}/api/vietqr/images/{imageId}`).
- `src/features/wallet/data/vietqrBanks.ts` — `export const VIETQR_BANKS_SNAPSHOT:
  VietqrBank[]` (58 banks captured 2026-07-18) — the instant seed AND the
  offline/CORS fallback.
- `src/features/wallet/hooks/useVietqrBanks.ts` — `useVietqrBanks()` (TanStack
  Query, key `["vietqr-banks"]`, `initialData: VIETQR_BANKS_SNAPSHOT`,
  `initialDataUpdatedAt: 0`, `staleTime` 24h, `gcTime` 7d, `retry: 1`; queryFn
  catches fetch failure → snapshot, rethrows on abort) and `useBankByBin(bin)`
  (selector over the same cached query).
- `src/features/wallet/components/BankLogo.tsx` — `BankLogoProps { imageId?, alt,
  name?, size?, className? }`; renders `<img src={bankLogoUrl(imageId)}>` with a
  lazy load + initials/glyph fallback on `onError` or missing `imageId`.
- `src/features/wallet/components/bankOptions.tsx` —
  `dedupeBanksByBin(banks: VietqrBank[])`, `buildBankOptions(banks: VietqrBank[]):
  ComboboxOption<VietqrBank>[]` (value=bin, label=shortName, keywords=[name,bin,code],
  meta=bank), `makeRenderBankOption(t)` (two-line row using `<BankLogo
  imageId={meta?.imageId} …>`).
- `src/features/wallet/components/BankAccountFormDialog.tsx` — Controller-wrapped
  `Combobox<VietqrBank>` bound to `bankBin`; imports `type { VietqrBank }` and
  `useVietqrBanks`; builds options from `banks.data`; `loading` = `banks.isFetching`;
  synthetic option for unknown/legacy BIN on edit; on select sets `bankName` =
  `shortName` (D3). `1001`/`13003`/`12000` handling.
- `src/features/wallet/components/BankAccountsTable.tsx` — imports `useVietqrBanks`
  + `dedupeBanksByBin`; builds a `bankByBin` map; per row derives `bank?.imageId`
  + `bank?.shortName ?? account.bankName`; passes `imageId` into `<BankLogo>`.
- `src/config/env.ts` — `env.vietqrBaseUrl` (`VITE_VIETQR_BASE_URL ??
  "https://vietqr.vn"`, no throw). `src/vite-env.d.ts` — `readonly
  VITE_VIETQR_BASE_URL?: string`. (Neither `.env.example` nor `.env.development`
  currently declares `VITE_VIETQR_BASE_URL` — nothing to strip there.)
- `src/test/msw/handlers.ts` — two absolute-URL handlers:
  `http.get("https://vietqr.vn/api/vietqr/banks", …)` (raw array: VCB 970436, TCB
  970407, BIDV 970418, MB 970422, plus one invalid `caiValue "12AB"` to exercise
  the drop-filter) and `http.get("https://vietqr.vn/api/vietqr/images/:imageId",
  …)` (reuses `pngResponse()`).
- Tests: `src/features/wallet/vietqrDirectoryApi.test.ts` (6),
  `useVietqrBanks.test.tsx` (6), `bankLogo.test.tsx` (4), `bankOptions.test.tsx`,
  `bankAccountsTable.test.tsx`, `bankAccountFormDialog.test.tsx`,
  `walletPage.test.tsx`, `src/components/ui/Combobox/combobox.test.tsx` (23,
  incl. `normalizeForSearch`), and `e2e/wallet-picker.spec.ts`.

### The LOCKED backend contract (new — source of truth)

`GET /api/v1/banks` — `BanksController` (read 2026-07-19). Authenticated (Bearer,
**not** `[AllowAnonymous]`), **not** Premium-gated (reference data), returns
`ApiResult<List<BankResponse>>`. `BankResponse` (JSON camelCase):

```ts
type BankResponse = {
  bin: string;       // 6-digit NAPAS BIN — persisted as bankBin, used for QR
  code: string;      // short bank code (e.g. "TCB") — search keyword
  name: string;      // full legal name
  shortName: string; // brand short name (e.g. "Techcombank")
  logoUrl: string;   // fully-built logo URL — imageId never leaves the backend
};
```

Example element: `{ "bin": "970425", "code": "ABB", "name": "Ngân hàng TMCP An
Bình", "shortName": "ABBANK", "logoUrl": "https://vietqr.vn/api/vietqr/images/6435c9f3-…" }`.

Semantics that shape this plan:

- **No `imageId`** — `logoUrl` is complete; the client renders it directly.
- **Not Premium-gated** — no `403 13003` on this endpoint.
- **`401`** rides the existing centralized `401 → refresh-once → retry → else
  logout` flow.
- **Never empty** — the backend has a static fallback (`BankDirectoryFallback`),
  so the SPA no longer needs its own snapshot seed or offline fallback.

### Stack (unchanged, locked by `frontend-foundation.md`)

React 19 + Compiler (no manual memo), TanStack Query v5, RHF + Zod, CSS Modules +
tokens + Radix, react-i18next (vi-VN default + en-US), MSW, oxlint + Prettier. The
centralized client `src/lib/api/client.ts` exposes `api.get<T>(path)` /
`api.post` / `api.put` / `api.delete` / `api.blob`; `api.get` returns the unwrapped
`data` (or throws a typed `ApiError`).

## Requirements

- **R1** — The bank directory is fetched from **our** `GET /api/v1/banks` through
  `src/lib/api/client.ts` (auth + refresh + envelope handled). No component/hook
  hits `vietqr.vn` directly anymore.
- **R2** — The internal type becomes `Bank { bin, code, name, shortName, logoUrl }`
  (was `VietqrBank { …, imageId }`). Renamed as a real symbol rename across every
  consumer — verify each site, not find/replace.
- **R3** — `BankLogo` renders `bank.logoUrl` directly; keep the initials/glyph
  fallback on load error or a missing/empty `logoUrl`.
- **R4** — Drop the committed snapshot + the offline/CORS fallback + the
  raw-`fetch` module + the VietQR env plumbing. The backend guarantees a non-empty
  list; the query is a standard TanStack Query with `staleTime` 24h.
- **R5** — The picker + accounts-table behavior is otherwise unchanged: dedupe-by-BIN,
  option building (value=bin, label=shortName, keywords=[name,bin,code]), the
  two-line row, the synthetic unknown/legacy-BIN option on edit, `bankName` =
  picked `shortName` (D3), the `stackOnMobile` card-stack.
- **R6** — The submitted mutation body stays
  `{ bankBin, bankName, accountNumber, accountHolderName }`; `1001` field mapping,
  `13003` inline UpgradePrompt, `12000` toast+close are all preserved.
- **R7** — The QR flow is untouched.
- **R8** — MSW: replace the two absolute `https://vietqr.vn/...` handlers with one
  relative `GET /api/v1/banks` handler returning the `ApiResult<T>` envelope with
  `BankResponse[]` (with `logoUrl` values). `onUnhandledRequest:"error"` requires
  it.
- **R9** — Tests renamed/updated to the new module/hook/type names and the new
  network boundary; i18n parity preserved (no copy keys change in this cycle beyond
  what the removed snapshot forces — see Step 8).
- **R10** — Accessibility + money/time formatting unchanged (no new surfaces).

## Open Questions

**None open.** The approach is approved. Two edge decisions a reasonable engineer
would raise are recorded as **Resolved** in the Decision Log (D3 — picker
loading/error UX now that the instant snapshot seed is gone; D4 — keep the
defensive client-side `dedupeBanksByBin`). If implementation surfaces anything this
doc does not cover, stop and record it here rather than pick a silent default.

## Assumptions

- The backend `GET /api/v1/banks` is deployed and stable per the locked contract
  (camelCase `BankResponse[]`, `logoUrl` built server-side, never empty, not
  Premium-gated, `401` only). Confirmed against `BanksController.cs` +
  `BankResponse.cs` (read 2026-07-19).
- `logoUrl` values point at publicly-served images that load in an `<img>`
  cross-origin without CORS (same as today — only the JSON fetch needed CORS, and
  that fetch is now same-origin to our API).
- The backend returns a **canonical single entry per BIN** OR may still contain a
  duplicate BIN (as the VietQR source did for 970452). We keep the client-side
  `dedupeBanksByBin` defensively (D4) — it is a no-op when the server already
  dedupes.
- The seeded wallet accounts (BIN `970436` Vietcombank, `970407` Techcombank)
  exist in the backend directory so the pick → store → table re-derive round-trip
  stays demonstrable; the MSW handler seeds both.
- Removing `initialData` means the picker has a genuine first-load state; the
  existing `Combobox` `loading` affordance covers it (D3).
- Locked stack unchanged (`frontend-foundation.md`).

## Implementation Plan

> All paths under `FairShareMonWeb/`. Symbol renames are real renames (run
> `gitnexus_rename` / verify each call site), never find-and-replace. Copy strings
> live in the i18n resources.

### Step 1 — New API module `banksApi.ts` (replaces `vietqrDirectoryApi.ts`)

Create `src/features/wallet/api/banksApi.ts`, delete
`src/features/wallet/api/vietqrDirectoryApi.ts`.

- Export `interface Bank { bin: string; code: string; name: string; shortName:
  string; logoUrl: string; }` (the new internal + wire shape — identical to
  `BankResponse`, so no normalization/mapping layer is needed).
- Export `const banksApi = { list: () => api.get<Bank[]>("/v1/banks") }` — mirrors
  the `bankAccountsApi.ts` shape. The central client handles Bearer, `X-Time-Zone`,
  `Accept-Language`, envelope unwrap → returns `Bank[]`, and refresh/`401`/typed
  `ApiError` on failure.
- **Delete** `bankLogoUrl(imageId)` (server now sends `logoUrl`).
- Drop the raw-`fetch`, the `VietqrRawBank`/`normalize`/`asString` helpers, and the
  "sanctioned exception" doc comment — no longer applicable.

### Step 2 — Query hook `useBanks.ts` (replaces `useVietqrBanks.ts`)

Create `src/features/wallet/hooks/useBanks.ts`, delete
`src/features/wallet/hooks/useVietqrBanks.ts`.

- `export function useBanks()`:
  ```ts
  useQuery({
    queryKey: ["banks"],
    queryFn: () => banksApi.list(),
    staleTime: 24 * 60 * 60 * 1000,
    gcTime: 7 * 24 * 60 * 60 * 1000,
  });
  ```
  Drop `initialData` / `initialDataUpdatedAt` / the try/catch snapshot fallback /
  the abort-rethrow / `retry: 1`-for-snapshot logic — the queryFn is a plain
  passthrough to the central client, which already de-dupes refresh and throws a
  typed error. Rename the query key `["vietqr-banks"]` → `["banks"]`.
- `export function useBankByBin(bin: string | undefined): Bank | undefined` — keep
  the name; selector over the same cached query
  (`useBanks().data?.find(b => b.bin === bin)`; `undefined` when `!bin`).

### Step 3 — Delete the snapshot

Delete `src/features/wallet/data/vietqrBanks.ts` (`VIETQR_BANKS_SNAPSHOT`). If the
`data/` folder is now empty, remove it.

### Step 4 — `BankLogo` uses `logoUrl`

`src/features/wallet/components/BankLogo.tsx`:

- Change prop `imageId?: string` → `logoUrl?: string` (update the JSDoc: falsy →
  fallback tile, no network).
- `const showFallback = !logoUrl || errored;`
- `<img src={logoUrl} …>` directly (drop the `bankLogoUrl(imageId)` import + call).
- Keep the initials/glyph fallback, `loading="lazy"`, `onError`, sizes, and the
  fixed-light logo plate exactly as-is. `BankLogo.module.css` unchanged.

### Step 5 — `bankOptions.tsx` type + logo source swap

`src/features/wallet/components/bankOptions.tsx`:

- `import type { Bank } from "../api/banksApi";` (was `VietqrBank`); rename all
  `VietqrBank` → `Bank` (in `dedupeBanksByBin`, `buildBankOptions`,
  `ComboboxOption<Bank>`, `makeRenderBankOption`).
- In `makeRenderBankOption`, pass `logoUrl={meta?.logoUrl}` to `<BankLogo>` (was
  `imageId={meta?.imageId}`).
- Keep dedupe-by-BIN (D4), value/label/keywords/meta, and the two-line row
  markup + `bankOptions.module.css` unchanged.

### Step 6 — `BankAccountFormDialog.tsx`

- Swap imports: `type { Bank } from "../api/banksApi"` (was `VietqrBank`);
  `useBanks from "../hooks/useBanks"` (was `useVietqrBanks`).
- Rename the local `const banks = useVietqrBanks()` call → `useBanks()`; type the
  `Combobox<Bank>` and `ComboboxOption<Bank>[]`.
- Everything else stays: options from `banks.data`, `loading` = `banks.isFetching`,
  synthetic unknown/legacy-BIN option, on-select `setValue("bankName",
  bank.shortName …)` (D3), `FIELD_NAMES`, and the `1001`/`13003`/`12000` handling.

### Step 7 — `BankAccountsTable.tsx`

- Swap imports: `useBanks` (was `useVietqrBanks`); `dedupeBanksByBin` unchanged.
- Build the `bankByBin` map the same way; per row pass `logoUrl={bank?.logoUrl}`
  into `<BankLogo>` (was `imageId={bank?.imageId}`). `displayName = bank?.shortName
  ?? account.bankName` unchanged. Card-stack / reveal / default / actions / a11y
  labels untouched.

### Step 8 — Env cleanup

- `src/config/env.ts` — remove the `vietqrBaseUrl` field (and its doc comment) from
  the `env` object.
- `src/vite-env.d.ts` — remove `VITE_VIETQR_BASE_URL` from `ImportMetaEnv`.
- `.env.example` / `.env.development` — **no change** (neither declares
  `VITE_VIETQR_BASE_URL` today; verified 2026-07-19). Note this in the Progress Log
  so the reviewer isn't surprised the diff doesn't touch them.
- i18n: **no copy-key changes** in this cycle — `form.bankPicker.*`, `form.logoAlt`,
  `table.bin`, and `validation:bankAccount.selectBank` all stay valid (the picker
  UX is identical). Verify no stray VietQR-specific copy remains.

### Step 9 — MSW handler

`src/test/msw/handlers.ts`:

- **Remove** both `http.get("https://vietqr.vn/api/vietqr/banks", …)` and
  `http.get("https://vietqr.vn/api/vietqr/images/:imageId", …)`.
- **Add** `http.get("*/api/v1/banks", …)` returning `ok([...])` (the shared
  `Envelope` helper) with a small `BankResponse[]`: VCB `970436`, TCB `970407`,
  BIDV `970418`, MB `970422` — each with a **`logoUrl`** (e.g.
  `https://vietqr.vn/api/vietqr/images/<uuid>`) instead of `imageId`. No
  invalid-BIN entry is needed anymore (the client no longer normalizes/drops — the
  backend owns that; the drop-filter test is removed with the module). Match the
  `*` origin-prefix convention used by every other handler.
- The two seeded wallet BINs must be present so the create/edit/table specs
  round-trip. `pngResponse()` stays (still used by the QR `/qr` handlers) — but the
  bank-logo image handler is gone; `<img src={logoUrl}>` loads are not asserted in
  jsdom (BankLogo falls back to initials on error, which specs already assert).

### Step 10 — Tests (rename + rewire)

- Rename `src/features/wallet/vietqrDirectoryApi.test.ts` →
  `banksApi.test.ts`: drop the normalize/drop-filter/wrapper/`bankLogoUrl` cases
  (that logic is gone); assert `banksApi.list()` calls `GET /v1/banks` through the
  client and returns the unwrapped `Bank[]` (envelope handled), and that a failure
  surfaces as a typed `ApiError` (MSW `fail(...)`).
- Rename `src/features/wallet/useVietqrBanks.test.tsx` → `useBanks.test.tsx`:
  `useBanks()` loads the list from the mocked endpoint (assert the mapped
  `Bank[]`); `useBankByBin` selects a seeded BIN and returns `undefined` for an
  unknown/undefined BIN. Drop the snapshot-seed + offline-fallback + abort cases
  (behavior removed).
- `src/features/wallet/bankLogo.test.tsx`: drive the `logoUrl` prop (was
  `imageId`); keep the lazy-`<img>`, `onError` → initials, no-url → initials,
  no-name → glyph cases.
- `src/features/wallet/bankOptions.test.tsx`: `Bank` type; assert options map
  value/label/keywords/meta and dedupe-by-BIN (keeps first).
- `src/features/wallet/bankAccountsTable.test.tsx`: `useBanks` mock/data; known BIN
  → re-derived short name, unknown BIN → stored `bankName`.
- `src/features/wallet/bankAccountFormDialog.test.tsx` + `walletPage.test.tsx`:
  rewire to the `/v1/banks` MSW boundary (no `vietqr.vn`); keep picking-a-bank
  submits `{bankBin, bankName=shortName, …}`, synthetic legacy-BIN round-trip,
  `13003`/`12000` paths.
- `src/components/ui/Combobox/combobox.test.tsx`: unchanged (Combobox primitive is
  not touched).
- `e2e/wallet-picker.spec.ts`: update the header comment (directory now served by
  the relative `/v1/banks` MSW handler, not the absolute VietQR handler); selectors
  are role/label-first and unaffected. Keep the create-via-combobox loop.

### API endpoints consumed this cycle

| Screen / hook | Verb + URL | Auth / headers | Response | Notes |
| --- | --- | --- | --- | --- |
| `useBanks` (picker + table) | `GET /api/v1/banks` (via `api.get`) | Bearer + `X-Time-Zone` + `Accept-Language` (central client) | `ApiResult<BankResponse[]>` → unwrapped `Bank[]` | not Premium-gated; `401` → refresh flow; never empty |
| `BankLogo` | `GET {bank.logoUrl}` in `<img>` | none (public image) | PNG/img | `onError` → initials/glyph fallback |
| Form submit (unchanged) | `POST /v1/bank-accounts` · `PUT /v1/bank-accounts/{uuid}` | Bearer (central client) | `ApiResult<BankAccountResponse>` | body `{bankBin,bankName,accountNumber,accountHolderName}`; `1001`→fields, `13003`→UpgradePrompt, `12000`→toast+close |
| QR (unchanged) | `GET /v1/expenses/{uuid}/qr` · `GET /v1/events/{uuid}/qr` | Bearer (`api.blob`) | PNG `Blob` + filename | out of scope |

### Loading / empty / error states

- **First load:** with `initialData` removed, `useBanks().isLoading` is true until
  the first response; the `Combobox` shows its `loading` hint
  (`banks.isFetching` → `form.bankPicker.loading`) and an empty option list. This
  is a brief, one-time state (24h `staleTime` thereafter).
- **Empty search:** `Combobox` shows `emptyLabel` when a query matches nothing
  (unchanged).
- **Query error** (network / non-`401`): `useBanks` throws a typed `ApiError`; the
  picker has no options and stays in a non-`isFetching` state. Per D3 the picker
  shows `emptyLabel` (no options) and the form cannot be completed; the backend's
  own static fallback makes this rare. TanStack Query's default retry/refetch on
  reconnect applies. `401` is invisible to the UI (central refresh).
- **Table:** while banks load or on error, `useBankByBin` returns `undefined` and
  the row falls back to the stored `account.bankName` + BIN (unchanged, already
  handled).
- **Logo:** `onError` → initials/glyph (unchanged).

### Form validation rules (mirror the backend — unchanged)

`bankBin` required (`^\d{6}$`, "select a bank" copy) · `bankName` required ≤100
(set from picked `shortName`) · `accountNumber` `^\d{6,19}$` · `accountHolderName`
required ≤100. Server stays authoritative (`1001` field errors surface on the
field). No schema change this cycle.

### Accessibility

No new surfaces. `Combobox`, `BankLogo` (decorative `alt=""` inside labeled rows),
table a11y labels, `prefers-reduced-motion`, 44px coarse-pointer targets — all
unchanged.

## Impact Analysis

Affected areas:

- **APIs:** consumes our `GET /api/v1/banks` (new, backend-owned). Stops calling
  `https://vietqr.vn/api/vietqr/banks` + `.../images/{imageId}`. Wallet mutation +
  QR contracts unchanged.
- **Deleted source:** `features/wallet/api/vietqrDirectoryApi.ts`,
  `features/wallet/data/vietqrBanks.ts`, `features/wallet/hooks/useVietqrBanks.ts`.
- **New source:** `features/wallet/api/banksApi.ts`,
  `features/wallet/hooks/useBanks.ts`.
- **Changed source:** `features/wallet/components/BankLogo.tsx`,
  `features/wallet/components/bankOptions.tsx`,
  `features/wallet/components/BankAccountFormDialog.tsx`,
  `features/wallet/components/BankAccountsTable.tsx`, `config/env.ts`,
  `vite-env.d.ts`, `test/msw/handlers.ts`.
- **Symbol renames (every consumer, verified):**
  - `interface VietqrBank` → `interface Bank` (defined in `banksApi.ts`; consumed
    by `bankOptions.tsx`, `BankAccountFormDialog.tsx`; `BankAccountsTable.tsx` +
    `useBanks.ts` reference `Bank` transitively).
  - `const vietqrDirectoryApi` → `const banksApi` (consumed by `useBanks.ts`).
  - `useVietqrBanks` → `useBanks` (consumed by `BankAccountFormDialog.tsx`,
    `BankAccountsTable.tsx`).
  - `useBankByBin` — name kept.
  - `bankLogoUrl` — **deleted** (was imported by `BankLogo.tsx`).
  - `VIETQR_BANKS_SNAPSHOT` — **deleted** (was imported by `useVietqrBanks.ts`).
  - `env.vietqrBaseUrl` / `VITE_VIETQR_BASE_URL` — **deleted** (was consumed by
    `vietqrDirectoryApi.ts`; grep-verified no other consumer).
  - query key `["vietqr-banks"]` → `["banks"]`.
  - `BankLogo` prop `imageId` → `logoUrl` (call sites: `bankOptions.tsx`,
    `BankAccountsTable.tsx`).
- **Changed tests:** `vietqrDirectoryApi.test.ts`→`banksApi.test.ts`,
  `useVietqrBanks.test.tsx`→`useBanks.test.tsx`, `bankLogo.test.tsx`,
  `bankOptions.test.tsx`, `bankAccountsTable.test.tsx`,
  `bankAccountFormDialog.test.tsx`, `walletPage.test.tsx`,
  `e2e/wallet-picker.spec.ts`. Untouched: `combobox.test.tsx`, `schemas.test.ts`,
  `walletI18n.test.ts`, `qrDialog.test.tsx`.
- **Not affected (QR):** `features/wallet/api/qrApi.ts`,
  `components/QrDialog.tsx`, `qrDialog.test.tsx`, the `/qr` MSW handlers +
  `pngResponse()`, and the `M7Showcase` QR references — the "VietQR" mentions there
  are about the QR image content, not the directory endpoint. Leave them.
- **Business rules:** wallet mutations remain Premium-gated (`13003` →
  UpgradePrompt); resource-owned `12000` → toast+close; server authoritative on
  validation; closed-event immutability + admin scope unaffected. No money/time
  format change.
- **Resilience:** the third-party CORS dependency is eliminated — the directory is
  same-origin to our API and the backend guarantees a non-empty list, so the
  client-side snapshot/fallback is no longer needed.
- **GitNexus mandate (implementer):** run
  `gitnexus_impact({direction:"upstream"})` on `BankLogo`, `bankOptions`
  (`buildBankOptions`/`makeRenderBankOption`), `BankAccountFormDialog`,
  `BankAccountsTable`, and the renamed symbols before editing; report blast radius,
  warn on HIGH/CRITICAL; use `gitnexus_rename` for the symbol renames; run
  `gitnexus_detect_changes()` before commit.
- **Risk:** low. Mechanical rename + a network-boundary swap from a bespoke
  raw-fetch path to the standard client path, deleting complexity (snapshot +
  fallback + env). The only behavior change is the first-load state (D3),
  mitigated by the existing `Combobox` loading affordance.

## Decision Log

### D1 — Consume `/v1/banks` via the centralized client — Resolved (approved)

Route the directory through `src/lib/api/client.ts` (`api.get<Bank[]>("/v1/banks")`)
instead of the raw cross-origin `fetch`. **Rationale:** the endpoint is now our
own; auth/refresh/timezone/locale/envelope are handled centrally, and the
sanctioned "raw-fetch exception" (plan D5 of `bank-picker-vietqr.md`) no longer
applies. No mapping layer — `BankResponse` already matches the internal `Bank`
shape.

### D2 — Drop the snapshot + offline fallback + env plumbing — Resolved (approved)

Delete `vietqrBanks.ts`, `env.vietqrBaseUrl`, `VITE_VIETQR_BASE_URL`, and the
try/catch fallback in the hook. **Rationale:** the backend guarantees a non-empty
list (static fallback server-side), so the client no longer needs its own
resilience layer; keeping it would be dead code + drift risk. `staleTime` 24h is
retained.

### D3 — Picker loading/error UX after removing the instant seed — Resolved (recommendation)

Without `initialData` there is a brief first-load state and a possible error state.
Reuse the existing `Combobox` `loading` affordance (`banks.isFetching` →
`form.bankPicker.loading`) for load, and `emptyLabel` when the list is
empty/errored; the table already falls back to the stored `bankName`. **Rationale:**
no new UI is needed; the state is brief (24h cache) and rare (backend fallback).
A dedicated inline error/retry banner in the picker is deferred (Future
Improvements) unless the orchestrator wants it now.

### D4 — Keep `dedupeBanksByBin` client-side — Resolved

Retain the defensive dedupe-by-BIN in `bankOptions.tsx` +
`BankAccountsTable.tsx`. **Rationale:** if the backend ever returns a duplicate BIN
(the VietQR source did for 970452), the picker keys + table lookup stay unique and
consistent; it is a no-op when the server already dedupes. Cheap safety, no
behavior change.

## Progress Log

### 2026-07-19

- Feature-planner: required reading done — the LOCKED backend contract
  (`FairShareMonApi/Controllers/BanksController.cs`,
  `Models/Banks/BankResponse.cs`: authenticated, `ApiResult<List<BankResponse>>`,
  camelCase `{bin,code,name,shortName,logoUrl}`, not Premium-gated, never empty);
  the prior plan (`planning/bank-picker-vietqr.md`, incl. its deferred backend
  OQ4b + Future Improvements); `FairShareMonWeb/CLAUDE.md` (central-client rule,
  the raw-fetch exception being retired); and every live consumer —
  `api/vietqrDirectoryApi.ts` (`VietqrBank`, `vietqrDirectoryApi`, `bankLogoUrl`),
  `data/vietqrBanks.ts` (`VIETQR_BANKS_SNAPSHOT`), `hooks/useVietqrBanks.ts`
  (`useVietqrBanks`, `useBankByBin`, key `["vietqr-banks"]`),
  `components/BankLogo.tsx` (prop `imageId`), `components/bankOptions.tsx`,
  `components/BankAccountFormDialog.tsx`, `components/BankAccountsTable.tsx`,
  `config/env.ts` (`vietqrBaseUrl`), `vite-env.d.ts` (`VITE_VIETQR_BASE_URL`),
  `test/msw/handlers.ts` (the two absolute VietQR handlers), and the test/e2e
  files. Confirmed via grep that `env.vietqrBaseUrl` has exactly one consumer
  (`vietqrDirectoryApi.ts`) and that `.env.example` / `.env.development` do **not**
  declare `VITE_VIETQR_BASE_URL` (so env cleanup touches only `env.ts` +
  `vite-env.d.ts`). Confirmed the QR path (`qrApi.ts`, `QrDialog.tsx`, `/qr`
  handlers, `M7Showcase`) is unrelated to the directory endpoint and stays.
- Authored this plan: `banksApi.ts` (via central client) replacing
  `vietqrDirectoryApi.ts`; `Bank` type replacing `VietqrBank` (`logoUrl` not
  `imageId`); `useBanks` (standard TanStack Query, key `["banks"]`, 24h
  `staleTime`, no snapshot/fallback) replacing `useVietqrBanks`; `BankLogo`
  `logoUrl` prop; `bankOptions`/form/table swaps; env + snapshot deletion; the
  relative `/v1/banks` MSW envelope handler; and the test/e2e renames.
- **Open Questions: none** — approach approved; D3 (picker loading/error UX) and
  D4 (keep client dedupe) recorded as Resolved. Ready for the web-implementer.

### 2026-07-19 — web-implementer (implementation complete)

Implemented all 10 steps exactly as specified; no deviations, no new Open
Questions.

- **Step 1** — Created `src/features/wallet/api/banksApi.ts` (`interface Bank`
  `{bin,code,name,shortName,logoUrl}` + `banksApi.list = () =>
  api.get<Bank[]>("/v1/banks")`). Deleted `api/vietqrDirectoryApi.ts` (raw fetch,
  `VietqrRawBank`/`normalize`/`asString`, `bankLogoUrl`, "sanctioned exception"
  comment all gone).
- **Step 2** — Created `hooks/useBanks.ts` (`useBanks` — key `["banks"]`, 24h
  `staleTime`, 7d `gcTime`, no `initialData`/snapshot/fallback/abort-rethrow;
  `useBankByBin` kept). Deleted `hooks/useVietqrBanks.ts`.
- **Step 3** — Deleted `data/vietqrBanks.ts` (`VIETQR_BANKS_SNAPSHOT`); the now-
  empty `features/wallet/data/` folder was removed.
- **Step 4** — `BankLogo` prop `imageId` → `logoUrl` (rendered directly as
  `src`; initials/glyph fallback on error/missing kept; `BankLogo.module.css`
  untouched).
- **Steps 5-7** — `bankOptions.tsx`, `BankAccountFormDialog.tsx`,
  `BankAccountsTable.tsx`: `VietqrBank`→`Bank`, `useVietqrBanks`→`useBanks`,
  `imageId`→`logoUrl`. Dedupe-by-BIN (D4), option value/label/keywords/meta,
  synthetic legacy-BIN option, `bankName=shortName` (D3), `loading =
  banks.isFetching`, and the `1001`/`13003`/`12000` handling all unchanged.
- **Step 8** — Removed `env.vietqrBaseUrl` from `config/env.ts` and
  `VITE_VIETQR_BASE_URL` from `vite-env.d.ts`. Confirmed `.env.example` /
  `.env.development` do **not** declare `VITE_VIETQR_BASE_URL` (grep, exit 1) — so
  the diff intentionally does not touch them, exactly as the plan predicted. No
  i18n copy-key changes.
- **Step 9** — `test/msw/handlers.ts`: replaced the two absolute
  `https://vietqr.vn/...` handlers with one relative `http.get("*/api/v1/banks")`
  returning the `ApiResult<T>` envelope (via the shared `ok(...)`) with
  `Bank[]` — VCB 970436, TCB 970407, BIDV 970418, MB 970422, each with a
  `logoUrl` (no `imageId`, no invalid-BIN entry). `pngResponse()` retained for the
  `/qr` handlers.
- **Step 10** — Test renames/rewires: `vietqrDirectoryApi.test.ts` →
  `banksApi.test.ts` (asserts `banksApi.list()` unwraps `Bank[]` over `/v1/banks`
  + a failure surfaces as a typed `ApiError`; normalize/drop-filter/`bankLogoUrl`
  cases dropped). `useVietqrBanks.test.tsx` → `useBanks.test.tsx` (loads the list
  from the mocked endpoint; `useBankByBin` selects a seeded BIN / returns
  `undefined` for unknown+undefined; snapshot-seed/offline-fallback/abort cases
  dropped). `bankLogo.test.tsx` drives `logoUrl`. `bankOptions.test.tsx` uses
  `Bank` + asserts `meta.logoUrl` and dedupe-by-BIN. `bankAccountsTable.test.tsx`
  rewired to the real `useBanks`/`/v1/banks` boundary (known BIN → re-derived
  short name, unknown BIN → stored `bankName`; the snapshot-only DuplicateBin case
  — which needed the deleted `VIETQR_BANKS_SNAPSHOT` + `bankLogoUrl` — was dropped,
  dedupe stays covered by `bankOptions.test.tsx`). `bankAccountFormDialog.test.tsx`
  + `walletPage.test.tsx`: updated the directory-source comments (no `vietqr.vn`);
  the picking/synthetic-BIN/`13003`/`12000` flows are unchanged.
  `e2e/wallet-picker.spec.ts`: header + inline comments updated to the relative
  `/api/v1/banks` handler (selectors unchanged). `combobox.test.tsx` untouched.
- **Quality bar (all green):** `pnpm lint` (oxlint) — no errors (only pre-existing
  fast-refresh warnings in untouched files); `pnpm exec tsc -b` — exit 0;
  `pnpm build` (`tsc -b && vite build`) — built OK (535 modules); `pnpm test`
  (vitest) — **828 passed / 828, 99 files**.
- **Ran the app** (`VITE_ENABLE_MOCKS=true pnpm dev`, driven with Playwright):
  logged in as `admin` (Premium), opened `/wallet`, confirmed the accounts table
  re-derives Vietcombank/Techcombank short names from their BINs and opened the
  bank picker to select MBBank. Network capture: exactly one `200
  GET /api/v1/banks` (same-origin, via the central client) and **zero** direct
  `vietqr.vn` directory calls. Screenshot verified.

## Final Outcome

Shipped 2026-07-19. The SPA no longer fetches `https://vietqr.vn` for the bank
directory — the wallet bank picker + accounts table now consume our own
`GET /api/v1/banks` through the centralized client (`src/lib/api/client.ts`), with
the internal type `Bank { bin, code, name, shortName, logoUrl }` (logo URL built
server-side, rendered directly). The committed snapshot, offline/CORS fallback,
raw-`fetch` module, `bankLogoUrl` builder, and the `vietqrBaseUrl` /
`VITE_VIETQR_BASE_URL` env plumbing were all deleted. MSW now serves the directory
from a relative `/api/v1/banks` envelope handler; the test suite is renamed/rewired
to the new module/hook/type names and the new network boundary. Lint + typecheck +
build + 828 unit tests all green; the flow was verified against the running app. QR
flow untouched. No deviations from the plan; no new Open Questions.

## Future Improvements

- **Inline error + retry affordance in the picker** if `/v1/banks` fails
  (currently degrades to an empty list per D3) — decide with the orchestrator if
  the brief error state proves user-visible.
- **Prefetch `/v1/banks` on app/wallet entry** (or hydrate from the query cache) to
  remove even the brief first-load state now that the snapshot seed is gone.
- **Retire the "sanctioned raw-fetch exception" note** from `CLAUDE.md` / project
  docs once this lands, since the SPA no longer scatters any raw `fetch`.
- **Bundle/precache logos** for offline logo rendering (still network-dependent).
