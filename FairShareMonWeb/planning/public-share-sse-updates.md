# Public share report: live updates via Server-Sent Events (SSE) — frontend

## Objective

Consume the just-shipped anonymous SSE endpoint `GET api/v1/public/shares/{token}/stream`
(`FairShareMonApi/planning/public-share-sse-updates.md`) from the existing `/share/:token`
public report page so a visitor's already-open tab **auto-updates** instead of showing a
stale settlement report until the `15000`ms `staleTime` happens to kick in on refocus. No new
route, no new screen — this is a live-data enhancement layered onto the shipped
`event-share-link.md` feature (`PublicSharePage` + `PublicBalanceTable`).

Concretely: open a native `EventSource` on the report while it is successfully displayed;
on `event: updated`, invalidate the existing TanStack Query caches for the report and the
per-member QR list so they silently refetch; on `event: revoked` / `event: expired`, close the
connection (the server does not suppress `EventSource`'s own auto-reconnect, so the client must
call `.close()` itself) and show a distinct "no longer live" terminal state; clean up the
connection on unmount/token change.

## Background

Verified against the live, already-shipped code (2026-08-26):

- **The backend contract** (`FairShareMonApi/planning/public-share-sse-updates.md`, Final
  Outcome — shipped and reviewed): `GET api/v1/public/shares/{token}/stream`,
  `text/event-stream`, `[AllowAnonymous]`. Pre-write validation reuses the same
  `ShareLinkNotFoundOrExpired` (`16000`) as the plain `GET`, returned as the normal wrapped
  `ApiResult` 404 JSON **before any byte is written** — so a connect-time failure is a regular
  HTTP 404, not something that ever reaches `EventSource`'s streaming state. Once connected:
  - First frame is always `event: connected` (harmless liveness ping — no listener needed).
  - `event: updated` fires whenever the shared event's settled/outstanding overlay changes
    (any of the three settled-toggle mutations, when they land on the event behind this
    token). Every frame carries a **trivial** `data: {}` body (required by the SSE spec for a
    named event to dispatch at all) — **the stream never carries the report payload itself**;
    the client must re-fetch the plain `GET` endpoint(s).
  - `event: revoked` (owner explicitly revoked or regenerated the link) and `event: expired`
    (the link's TTL naturally elapsed, detected by the server's own heartbeat re-check) are
    **terminal and distinct** — the backend deliberately modeled them as two different event
    names specifically so the frontend can show an accurate reason (backend Decision Log #2 /
    OQ2).
  - `: keep-alive` heartbeat comments every `Share:StreamHeartbeatSeconds` (default 20s) — SSE
    comment lines; native `EventSource` ignores them silently, no listener needed.
  - Native `EventSource` **auto-reconnects** on a dropped/errored connection with no server
    support required. A `revoked`/`expired` terminal frame is the server's explicit signal to
    **stop** reconnecting — the server does not close the socket in a way that suppresses
    `EventSource`'s own retry, so the client must call `.close()` itself on either terminal
    event, or the browser will silently reconnect to a token it was just told is dead.
- **Verified: `EventSource` cannot carry any custom header, full stop** — not just
  `Authorization`. This makes the task's framing question ("does `anonymous`/`skipAuthRefresh`
  matter here?") moot in both directions: those options exist on `src/lib/api/client.ts`'s
  `RequestOptions` to suppress a header the client would otherwise inject, but `EventSource` is
  a **separate native browser API** that never goes through `client.ts`/`fetch` at all — there
  is no wrapper to configure. Confirmed this is fine functionally too: the stream carries no
  localized text and no timestamps (trivial `data: {}` only), so the missing
  `Accept-Language`/`X-Time-Zone` headers cost nothing. `withCredentials` stays default `false`
  (no cookies are used anywhere in this app; auth is Bearer-in-header, and this route sends no
  auth at all).
- **Verified in `src/`:** `usePublicShareQuery`/`usePublicShareMemberQrsQuery`
  (`src/features/share/hooks/useShare.ts`) already define the exact query keys
  (`shareKeys.public(token)`, `shareKeys.publicQrs(token)`) this feature needs to invalidate —
  no new query key scheme required. `PublicBalanceTable.tsx`
  (`src/features/share/components/PublicBalanceTable.tsx`) lazily enables the QR query
  (`qrEnabled`, flips true on first QR-button click and **stays true** for the rest of the
  page's life, independent of whether the `QrPreviewDialog` (`previewOpen`) is currently open)
  and drives the lightbox off `members.findIndex(...)`. `PublicSharePage.tsx`
  (`src/features/share/pages/PublicSharePage.tsx`) is the single place that branches
  loading/error/success off `usePublicShareQuery`; the existing `16000` branch renders one
  **identical** "link unavailable" screen for expired/revoked/missing specifically to avoid an
  existence leak for a visitor who does not yet know whether the token was ever real
  (`share:expired.title/body`).
- **Verified: TanStack Query v5's `invalidateQueries` default `refetchType: 'active'` only
  triggers an eager refetch for a query that has at least one **enabled** observer** (a
  `useQuery` mounted with `enabled: false` is not counted as "active", so invalidating it just
  marks it stale — no network call fires until it becomes enabled). This directly answers part
  of the task's "is refetching the QR list on every `updated` wasteful" question: calling
  `invalidateQueries` unconditionally on `shareKeys.publicQrs(token)` costs **nothing** for a
  visitor who has never clicked a QR button (`qrEnabled === false`) — TanStack's own gating
  skips the fetch. The only real cost is the "opened once, dialog now closed" case (`qrEnabled
  === true`, `previewOpen === false`), where the query stays "active" and does refetch quietly
  in the background — see Open Question OQ4.
- **A genuine correctness gap found while verifying `PublicBalanceTable.tsx`:** the lazy QR
  query's result (`members`) drives both the button-enable state (`row.outstanding > 0`,
  read from the separately-refetched **report** rows) and the open lightbox's `startIndex`
  (`members.findIndex(m => m.memberUuid === targetMemberUuid)`). If an `updated` signal arrives
  **while the QR lightbox is open** and the member currently being viewed just got settled (or
  the report/QR set otherwise changed), the refetched `members` array can shrink or reorder
  under the open dialog, and `startIndex` recomputes to `-1 → clamped 0` (a *different* member's
  slide, silently) rather than closing gracefully. This must be handled (Requirements below) —
  it is a direct, unavoidable consequence of adding live refetch to a component that today only
  ever fetches the QR list once per page load.
- **Verified: no existing precedent for `EventSource` anywhere in this codebase** (`grep -r
  EventSource src/` — zero hits) and no test double for it. **Verified MSW (`msw@2.15`) only
  intercepts `fetch`/`XMLHttpRequest`** — it has no interception layer for the native
  `EventSource` transport (a separate browser networking primitive, not built on `fetch`).
  **`jsdom` (the pinned `^29.1.1`) does not implement `EventSource`** — it is on jsdom's
  documented "not implemented" list alongside `WebSocket`. This means: (a) the existing MSW
  handlers for `GET .../shares/{token}` and `.../qr/members` are irrelevant to testing the
  stream itself — they cover the plain-JSON refetches the stream *triggers*, not the stream
  connection; and (b) there is no `EventSource` global in the Vitest/jsdom environment to begin
  with, so a test that calls `new EventSource(...)` today would throw `EventSource is not
  defined`. **This is a genuinely new testing technique this suite has not needed before** —
  flagged in Tests, mirroring the exact same gap the backend planning doc flagged for its own
  integration-test harness (`Infrastructure/SseTestClient.cs`).
- **Verified `vite.config.ts`'s dev proxy** (`server.proxy["/api"] = { target: proxyTarget,
  changeOrigin: true }`) has no custom buffering/timeout override. Vite's dev proxy is Node's
  `http-proxy` under the hood, which streams a proxied HTTP response by default (no config
  needed to avoid buffering) — unlike the production nginx layer, which needed an explicit
  `proxy_buffering off` (the backend doc's Step 8). This should just work for local dev without
  a vite.config.ts change, but is worth a manual smoke check during implementation since this
  repo has never proxied a long-lived streamed response through Vite dev before (see
  Assumptions).
- **Verified `React.StrictMode` is enabled** (`src/main.tsx`) — in dev, effects
  mount→cleanup→mount once. A subscription-style effect (open a connection in the effect body,
  close it in the cleanup) already tolerates this correctly by construction (the cleanup closes
  whichever connection that mount opened before the next one opens) — no special guard needed,
  unlike `useSessionBootstrap`'s module-level guard (which exists because *that* effect performs
  a one-time side effect that must not double-fire, not because it is a per-mount subscription).

## Requirements

- While `PublicSharePage` is displaying a **successful** report for a token (i.e.
  `usePublicShareQuery` is `isSuccess`), open one `EventSource` on
  `GET {apiBaseUrl}/v1/public/shares/{token}/stream` and keep it open for the life of that
  success state.
- On `event: updated`: invalidate both `shareKeys.public(token)` and
  `shareKeys.publicQrs(token)` (TanStack Query's own `enabled`-gated "active" semantics decide
  whether either actually triggers a network refetch — see Background). No manual re-fetch
  call; no polling; no change to `staleTime`.
- On `event: revoked` or `event: expired`: close the `EventSource` (client-initiated — the
  server does not suppress `EventSource`'s auto-reconnect) and switch the page to a **terminal**
  state distinct from the initial-load `16000` "link unavailable" screen — see OQ1 for the exact
  copy/layout, distinct copy per reason is the strong default (unlike the pre-load 404, the
  visitor already knows this report existed, so naming the reason leaks nothing new).
- On unmount, or when `token` changes (route re-entry with a different token), close the
  `EventSource` — no dangling connection.
- Guard the QR-lightbox correctness gap (Background): while `QrPreviewDialog` is open and a
  QR-list refetch (triggered by `updated`) resolves, if the member currently being viewed is no
  longer present in the refreshed member list, close the dialog and show a brief informational
  toast rather than silently jumping to an unrelated member's slide.
- Never attempt to read a numeric error code off the stream's connection failure — `EventSource`
  exposes no response body/status on its native `error` event, so a connect-time or
  reconnect-time failure that isn't one of the two named terminal events is treated as
  inconclusive; fall back to the plain report's existing `staleTime` safety net (OQ2) rather
  than guessing a reason.
- No new runtime dependency (`EventSource` is a browser global); no change to
  `src/lib/api/client.ts`, `src/lib/api/errors.ts`, or the `16xxx` error-code mirror; no new
  route (reuses the shipped `share/:token`).

## Open Questions

> None of the decisions locked in `event-share-link.md` are reopened (ungated route, no-leak
> pre-load `16000` screen, `QrPreviewDialog` reuse, read-only table shape). These are new
> ambiguities specific to consuming the live stream.

**OQ1 — Terminal-state UI/copy for `revoked`/`expired`, and whether it differs from the
existing pre-load "expired" screen.** The pre-load `16000` screen (`share:expired.title/body`)
deliberately uses **identical** copy for expired/revoked/missing to avoid leaking whether a
token ever existed to a visitor who is *probing* an unknown URL. That constraint does not apply
here: a visitor hitting a mid-session terminal frame has, by definition, already been looking at
a real, loaded report — telling them *why* it just stopped updating leaks nothing new, and the
backend went out of its way to model the two reasons distinctly (backend Decision Log #2).
Options:
- **(a) Distinct copy per reason, full-page replace (recommended).** "Chủ đợt đã thu hồi/tạo
  lại liên kết này" vs "Liên kết đã hết hạn (1 ngày)" — reuses the same layout as the pre-load
  screen (an `ErrorState`), just swapped copy and a new `share:stream.*` i18n block. Simplest to
  implement and test; matches the existing full-page-replace pattern the page already has for
  the pre-load case.
- (b) Distinct copy, but as a dismissible-looking banner **over the last-good, now-frozen
  report** (table stays visible, greyed out or with a "no longer live" ribbon) instead of
  replacing the page. Preserves more context for a visitor mid-review, but is a materially
  bigger UI surface (frozen-but-interactive-looking read-only table needs its own treatment) for
  a feature whose core value is "don't make the visitor manually reload" — a full-page swap
  already delivers that.
- (c) Reuse the single generic pre-load `share:expired.title/body` copy for both mid-session
  terminal reasons too (no new i18n keys). Simplest, but throws away the exact distinction the
  backend went out of its way to build, and could read as "am I on the wrong link now?" to a
  visitor who was just actively looking at a real report seconds ago.
Please pick (a)/(b)/(c) (or a variant) before the layout/i18n keys in Step 3 are finalized.

**OQ2 — Keep, widen, or drop the existing `15000`ms `staleTime` on `usePublicShareQuery` now
that a push channel exists?** Options:
- **(a) Leave it exactly as-is (recommended).** `staleTime` only governs whether a
  window-refocus/remount uses cached data without a round trip — it is not a poll, so it costs
  nothing to keep alongside the SSE push. It also remains the correctness safety net for the one
  case the stream *cannot* diagnose on its own: a connect/reconnect failure whose native `error`
  event carries no code (see Requirements) — if `EventSource` silently gives up after a network
  blip that happened to coincide with the link actually expiring, the next natural refocus/retry
  still eventually surfaces the truth via the plain `GET`'s own `16000`.
- (b) Increase `staleTime` materially (e.g. to a few minutes) on the theory that SSE now owns
  freshness. Trade-off: makes the app **more** fragile in exactly the failure mode (a).describes
  — if the stream quietly died (e.g. a backgrounded mobile tab throttling the connection) with no
  clean terminal frame, the page would show stale data for longer before any mechanism notices.
Please confirm (a) or a specific alternative value.

**OQ3 — `EventSource` testing technique for Vitest/jsdom.** Verified (Background): MSW cannot
intercept native `EventSource`, and jsdom does not implement it at all — so there is no existing
harness this reuses as-is. Options:
- **(a) A hand-rolled `FakeEventSource` test double, installed via `vi.stubGlobal("EventSource",
  ...)` (recommended).** A small class implementing just the surface the hook uses
  (`addEventListener`/`removeEventListener`/`close()`/`readyState`/the three `CONNECTING`/`OPEN`/
  `CLOSED` constants), plus a way for the test to grab the live instance (e.g. a
  module-level registry keyed by URL, or a constructor spy) and manually dispatch a named
  `MessageEvent` to simulate a server frame. Zero new dependency; deterministic; mirrors how
  `src/test/setup.ts` already polyfills other jsdom-missing browser APIs
  (`hasPointerCapture`, `ResizeObserver`, `URL.createObjectURL`) by patching the global directly.
- (b) Install a real polyfill package (e.g. `event-source-polyfill`) purely for tests, and mock
  *its* transport instead. Extra dependency for a capability the app itself doesn't need
  (production `EventSource` support is universal in target browsers); more moving parts than (a)
  for no real benefit.
- (c) Skip unit/component-level SSE coverage entirely; rely only on manual QA / a future
  Playwright e2e run against the real backend. Weakens the CI safety net for a feature whose
  correctness (terminal-event handling, cache invalidation, cleanup-on-unmount) is exactly the
  kind of thing a regression silently breaks.
Please confirm (a) (or name a preferred alternative) before the test-engineer's cycle starts.

**OQ4 — Is invalidating the QR-list query on every `updated` wasteful when the QR lightbox
isn't currently open?** Verified (Background): TanStack Query's `enabled`-gating already makes
this free for a visitor who has **never** clicked a QR button. The residual cost is a visitor
who clicked once, then closed the dialog (`qrEnabled === true`, `previewOpen === false`) — every
subsequent `updated` still triggers a background QR refetch for them. Options:
- **(a) Invalidate unconditionally regardless of `previewOpen` (recommended).** Simple, and
  correctly keeps the data fresh for the case that actually matters for correctness: the dialog
  is **open** when `updated` arrives (see the QR-lightbox correctness gap in Background) — a
  member's QR should reflect their current standing while someone is looking at it. The
  "opened-once-then-closed" background refetch is bounded (one member's small QR image list,
  only for visitors who showed interest at all) and arguably desirable (the data stays warm for
  a second look).
- (b) Additionally gate the QR invalidation on `previewOpen === true` (skip it when the dialog is
  currently closed, even if it was opened before). Eliminates the residual background refetch,
  but requires threading `previewOpen` (currently local state inside `PublicBalanceTable`) up to
  wherever the stream hook lives, coupling a transport-layer hook to a specific component's UI
  state for a marginal saving.
- (c) Never proactively invalidate the QR key on `updated`; only ever (re-)fetch lazily on the
  next QR-button click. Simplest, but reintroduces exactly the correctness gap this doc's
  Requirements call out: a visitor with the lightbox **open** when `updated` fires would keep
  looking at a stale QR/amount until they close and reopen it.
Please confirm (a), or specify a preference for the added complexity of (b).

> **RESOLVED 2026-08-26 (orchestrator).** All 4 Open Questions resolved per the web-feature-planner's
> own recommendations, verbatim: OQ1 → (a) distinct copy per reason, full-page replace; OQ2 → (a) leave
> `staleTime` as-is; OQ3 → (a) hand-rolled `FakeEventSource` via `vi.stubGlobal`; OQ4 → (a) invalidate the
> QR-list query unconditionally on `updated`, guarded by Step 4's lightbox effect. The Implementation Plan
> was already written against these choices — no changes needed.

## Assumptions

- The backend contract is exactly as shipped and reviewed in
  `FairShareMonApi/planning/public-share-sse-updates.md` (event names `connected`/`updated`/
  `revoked`/`expired`, trivial `data: {}` bodies, `16000` on an invalid token at connect time,
  20s default heartbeat) — no drift to reconcile.
- `env.apiBaseUrl` (`src/config/env.ts`) is a valid base for a native `EventSource` URL exactly
  as it already is for `fetch`: `/api` (relative, Vite-dev-proxied) in dev, an absolute origin +
  `/api` in prod. `EventSource` accepts both relative and absolute URLs identically to `fetch`/
  `<a href>`, so `${env.apiBaseUrl}/v1/public/shares/${token}/stream` is a correct URL in both
  environments with no new env accessor needed.
- Vite's dev proxy streams a long-lived SSE response without additional configuration (Node's
  `http-proxy`, which Vite's dev server proxy is built on, does not buffer by default) — assumed
  true from the proxy's default behavior since no prior feature in this repo has proxied a
  streamed response through it; **verify with one manual smoke check** (open `/share/:token` in
  a dev-server browser tab, confirm the Network panel shows a pending/streaming
  `text/event-stream` request through `/api/...`, not a buffered one) during implementation, and
  raise a new Open Question only if that check fails.
- `EventSource`'s native `error` event carries no machine-readable reason (no status code, no
  body) — a generic reconnect-failure is therefore never mapped to a specific UI message; the
  page instead relies on the two explicit terminal event names for a diagnosed state, and the
  plain report's own `staleTime`/next-refetch as the fallback for everything else (this is a
  platform limitation, not a preference call).
- No `retry:` field is sent by the server frames, so a transient dropped connection uses the
  browser's own default `EventSource` reconnect delay (~3s in most browsers) — out of this
  feature's control and not configured client-side.
- `PublicSharePage`/`PublicBalanceTable`'s existing props/data flow (report `data` passed down
  from `usePublicShareQuery`, QR `members` from `usePublicShareMemberQrsQuery`) already
  re-renders correctly off cache updates with zero other changes — confirmed by reading both
  components (Background); only the two edits called out in the Implementation Plan are needed.

## Implementation Plan

> Paths under `FairShareMonWeb/`. All user-facing strings via the existing `share` i18n
> namespace. No new dependency, no new route. Written against the **recommended** option for
> each Open Question; adjust the flagged spots (Step 3 primarily) if the checkpoint picks
> otherwise.

### Step 1 — `shareApi.ts`: a pure URL helper, not an `api.*` call

`src/features/share/api/shareApi.ts` — add:

```ts
/**
 * The anonymous SSE stream URL for a token. Deliberately NOT routed through the
 * centralized `api` client — native `EventSource` is a separate browser transport
 * that cannot attach custom headers at all (not just Authorization), and this
 * route sends none anyway (public, no auth). Reuses the same `env.apiBaseUrl`
 * base the `api` client itself builds every request URL from.
 */
publicStreamUrl: (token: string) =>
  `${env.apiBaseUrl}/v1/public/shares/${token}/stream`,
```

(Add the `import { env } from "@/config/env"` alongside the existing imports.)

### Step 2 — New hook `src/features/share/hooks/useEventShareStream.ts`

```ts
import { useEffect, useState } from "react";
import { queryClient } from "@/lib/query/queryClient";
import { shareApi } from "../api/shareApi";
import { shareKeys } from "./useShare";

export type ShareStreamTerminalReason = "revoked" | "expired";

/**
 * Subscribes to the public share's live-update stream while `enabled` (the
 * report is successfully loaded). `updated` invalidates the report + QR-list
 * query caches (TanStack Query's own `enabled`-gating decides whether either
 * actually refetches — see planning doc Background). `revoked`/`expired` close
 * the connection (the server does not suppress EventSource's auto-reconnect —
 * the client must call `.close()` itself) and are surfaced as a terminal reason
 * for the page to render a distinct "no longer live" state (OQ1).
 */
export function useEventShareStream(
  token: string,
  { enabled }: { enabled: boolean },
): { terminalReason: ShareStreamTerminalReason | null } {
  const [terminalReason, setTerminalReason] = useState<ShareStreamTerminalReason | null>(null);

  useEffect(() => {
    if (!enabled || !token) return;
    setTerminalReason(null); // reset on (re)connect — e.g. a token change while mounted

    const source = new EventSource(shareApi.publicStreamUrl(token));

    source.addEventListener("updated", () => {
      void queryClient.invalidateQueries({ queryKey: shareKeys.public(token) });
      void queryClient.invalidateQueries({ queryKey: shareKeys.publicQrs(token) });
    });
    source.addEventListener("revoked", () => {
      setTerminalReason("revoked");
      source.close();
    });
    source.addEventListener("expired", () => {
      setTerminalReason("expired");
      source.close();
    });
    // 'connected' is a harmless liveness ping — no listener needed (an unhandled
    // named event is simply not dispatched to anything, per EventTarget). The
    // native 'error' event is also intentionally unhandled: EventSource already
    // retries transient drops on its own, and a terminal (non-2xx) failure carries
    // no readable reason — see Assumptions on why that case falls back to the
    // plain report's own staleTime rather than a guessed message here.

    return () => source.close();
  }, [token, enabled]);

  return { terminalReason };
}
```

Note: uses `addEventListener` per **named** event type, never `onmessage`/a bare `"message"`
listener — every frame the backend sends carries an explicit `event:` line, so the default
unnamed-message channel never fires for this stream (a common `EventSource` gotcha worth calling
out for the test-engineer/reviewer).

### Step 3 — `PublicSharePage.tsx`: wire the hook + the terminal branch (OQ1)

`src/features/share/pages/PublicSharePage.tsx`:

1. `const stream = useEventShareStream(token, { enabled: query.isSuccess });` — placed after the
   existing `query` declaration.
2. Branch order in the render body: **terminal reason first** (it can only be true once
   `query.isSuccess` was already true), then the existing `isPending`/`isError`/success branches
   unchanged:
   ```tsx
   if (stream.terminalReason) {
     body = (
       <div className={styles.report} role="status" aria-live="polite">
         <ErrorState
           title={t(`share:stream.${stream.terminalReason}Title`)}
           description={t(`share:stream.${stream.terminalReason}Body`)}
         />
       </div>
     );
   } else if (query.isPending) {
     ...
   ```
3. The `aria-live="polite"` wrapper announces the terminal swap to screen-reader users, since it
   happens without any user action (Accessibility requirement below) — no such announcement is
   added for the silent `updated` refetch (consistent with the rest of the app: no other screen
   announces a background query refetch either).
4. (If OQ1 is resolved as (b) instead of (a): replace this whole-page swap with a banner
   component layered over the still-rendered `SuccessReport`, and skip the `ErrorState` reuse —
   flagged here as the one spot that changes shape depending on the OQ1 answer.)

### Step 4 — `PublicBalanceTable.tsx`: guard the QR-lightbox correctness gap

`src/features/share/components/PublicBalanceTable.tsx` — extend the existing `useEffect` that
reacts to `qrQuery` settling (the one currently handling `pendingOpen`) with a second effect (or
fold into the same one) that reacts to `qrQuery.data` changing **while `previewOpen` is true**:

```ts
useEffect(() => {
  if (!previewOpen || !targetMemberUuid) return;
  const stillPresent = members.some((m) => m.memberUuid === targetMemberUuid);
  if (!stillPresent) {
    setPreviewOpen(false);
    toast.push({
      tone: "info",
      title: t("share:stream.qrMemberSettledTitle"),
      description: t("share:stream.qrMemberSettledBody"),
    });
  }
}, [members, previewOpen, targetMemberUuid, toast, t]);
```

This only fires when a **live-update-triggered** refetch (Step 2's `invalidateQueries` on
`shareKeys.publicQrs(token)`) resolves with the currently-viewed member no longer in the list —
the ordinary "open the dialog for the first time" path never sees `members` change out from under
an already-open dialog, so this is additive and inert for every existing test/behavior.

### Step 5 — i18n (`share` namespace, both locales)

Add a `stream` block to `src/i18n/locales/vi-VN/share.json` and
`src/i18n/locales/en-US/share.json`:

- `stream.revokedTitle` / `stream.revokedBody` — distinct from `expired.title/body`, names the
  owner action explicitly (OQ1).
- `stream.expiredTitle` / `stream.expiredBody` — names the natural TTL elapse explicitly.
- `stream.qrMemberSettledTitle` / `stream.qrMemberSettledBody` — the QR-lightbox guard's info
  toast (Step 4).

Suggested vi-VN copy (en-US mirrors, kept in the Decision Log once OQ1 is resolved):

```json
"stream": {
  "revokedTitle": "Liên kết đã bị thu hồi",
  "revokedBody": "Chủ đợt đã thu hồi hoặc tạo lại liên kết này. Báo cáo bạn đang xem không còn được cập nhật — hãy xin liên kết mới từ chủ đợt.",
  "expiredTitle": "Liên kết đã hết hạn",
  "expiredBody": "Thời hạn của liên kết này đã hết. Báo cáo bạn đang xem không còn được cập nhật — hãy xin liên kết mới từ chủ đợt.",
  "qrMemberSettledTitle": "Thành viên đã được cập nhật",
  "qrMemberSettledBody": "Thành viên này không còn nợ nữa nên mã QR đã được đóng lại."
}
```

The existing `shareI18n.test.ts` parity test picks these up automatically once the keys exist in
both locale files (no test-file change needed for parity itself; new assertions for the new keys
are listed under Tests).

### Step 6 — Test infrastructure: `FakeEventSource` (OQ3)

New `src/test/fakeEventSource.ts` (shared test utility, alongside `src/test/utils.tsx`):

- A minimal class matching the `EventSource` surface the hook uses: `readyState`,
  `CONNECTING`/`OPEN`/`CLOSED` static-like constants, `addEventListener`/`removeEventListener`,
  `close()`.
- A module-level registry (e.g. `Map<string, FakeEventSource[]>` keyed by URL, or a simple
  array of all instances ever constructed) so a test can retrieve the instance the hook created
  and call a test-only `dispatch(eventName, data?)` helper that constructs a `MessageEvent` and
  invokes the matching listeners — simulating a server frame without any real network.
- Tests install it with `vi.stubGlobal("EventSource", FakeEventSource)` in a `beforeEach`
  (`vi.unstubAllGlobals()` in `afterEach`, or rely on the global `afterEach` in `src/test/setup.ts`
  if extended) — this is additive to the harness, not a change to MSW/`server.ts`, since MSW
  cannot intercept this transport at all (Background).

### API endpoints consumed (verb + path + DTO + codes)

| Screen/hook | Verb + Path | Request | Response | Notable codes |
| --- | --- | --- | --- | --- |
| `useEventShareStream` | `GET /v1/public/shares/{token}/stream` (native `EventSource`, not `api.*`) | — (no headers possible) | `text/event-stream`; named events `connected`/`updated`/`revoked`/`expired`, each `data: {}` | `16000` only as a plain pre-stream 404 JSON (never reaches `EventSource`'s streaming state) |
| (unchanged) `usePublicShareQuery` | `GET /v1/public/shares/{token}` | — (`anonymous`, `skipAuthRefresh`) | `PublicEventShareResponse` | `16000` |
| (unchanged) `usePublicShareMemberQrsQuery` | `GET /v1/public/shares/{token}/qr/members` | — (`anonymous`, `skipAuthRefresh`) | `MemberQrResponse[]` | `16000` |

The stream itself never goes through the `ApiResult<T>` envelope (per the backend doc, this is
by design — same precedent as the CSV/QR `File(...)`-returning export actions). The two plain
`GET`s it triggers refetches of are unchanged and still go through the centralized `api` client
exactly as `event-share-link.md` shipped them.

### Loading / empty / error states

- **Connecting**: silent — no visible "connecting…" indicator (the report is already showing;
  this is a background enhancement, not a blocking load state). No change to the existing
  `LoadingReport` skeleton (the stream only ever opens once that skeleton has already resolved
  to success).
- **`updated`**: silent background refetch via the existing query caches; the report/QR-lightbox
  simply re-render with fresh data exactly as any other TanStack Query cache update does
  elsewhere in the app today — no new loading affordance.
- **`revoked` / `expired`**: the new terminal state (Step 3) — distinct copy per OQ1's chosen
  option, replacing (or bannering over, if OQ1 = b) the report.
- **QR-lightbox guard (Step 4)**: an info toast + dialog auto-close, not an error state — the
  underlying data is fine, just no longer includes the member being viewed.
- **Connection-level `error` with no terminal frame**: no dedicated UI — falls back to the
  existing pre-load `16000`/generic-error branches the next time `usePublicShareQuery` itself
  refetches (window refocus, `staleTime` elapsing, or a future manual retry).

### Form / input validation

None — this feature adds no form, no user input. (Included for template completeness.)

### i18n keys (new; vi-VN + en-US, `share` namespace)

- `stream.revokedTitle`, `stream.revokedBody`
- `stream.expiredTitle`, `stream.expiredBody`
- `stream.qrMemberSettledTitle`, `stream.qrMemberSettledBody`

(Exact final Vietnamese/English copy is pending OQ1's resolution; the Step 5 draft above is the
starting point.)

### Accessibility requirements

- The terminal-state swap (Step 3) is wrapped in `role="status" aria-live="polite"` so a
  screen-reader user watching the report is told it stopped updating, without needing to notice
  a silent visual change — the only new a11y surface this feature adds (the silent `updated`
  refetch intentionally gets no live-region announcement, consistent with how every other
  background query refetch in this app already behaves).
- The QR-lightbox auto-close toast (Step 4) reuses the existing `useToast` mechanism, which
  already carries its own accessible live-region semantics (per `ToastHost`, unchanged here).
- No new focus traps, forms, or interactive controls are introduced.

### Tests the web-test-engineer should write

> **Flag: `EventSource` needs a genuinely new testing technique this suite hasn't used before**
> — see OQ3. Every test below assumes the `FakeEventSource` double from Step 6
> (`vi.stubGlobal("EventSource", FakeEventSource)`), never MSW, for anything that simulates the
> stream itself. MSW handlers are still used, unchanged, for the plain `GET` refetches the stream
> triggers.

**Unit — `useEventShareStream.test.ts` (pure hook test, `FakeEventSource` double):**
- `enabled: false` or an empty `token` never constructs an `EventSource`.
- `enabled: true` with a token constructs exactly one `EventSource` at
  `shareApi.publicStreamUrl(token)`.
- Dispatching `connected` is a no-op: no `terminalReason` set, no `invalidateQueries` call.
- Dispatching `updated` calls `queryClient.invalidateQueries` with both
  `shareKeys.public(token)` and `shareKeys.publicQrs(token)` (spy on `queryClient`).
- Dispatching `revoked` sets `terminalReason: "revoked"` AND calls `.close()` on the fake
  instance.
- Dispatching `expired` sets `terminalReason: "expired"` AND calls `.close()`.
- Unmounting (with no terminal event received) calls `.close()` exactly once — proves cleanup
  runs even for a still-open connection.
- Re-rendering with the same `token`/`enabled` does not open a second connection (effect
  dependency correctness).
- Changing `token` while mounted closes the old connection and opens a new one at the new URL,
  and resets `terminalReason` back to `null`.

**Integration — extend `publicSharePage.test.tsx` (real `usePublicShareQuery` + MSW for the
JSON refetches, `FakeEventSource` for the stream):**
- The stream is opened only **after** the report loads successfully — no `EventSource`
  constructed while `isPending`, and none constructed on the initial-load `16000` error path
  either (regression guard: the existing "identical copy for expired/revoked/missing" test must
  still pass unchanged — this feature must not touch that pre-load path at all).
- Dispatching a fake `updated` event causes the underlying MSW `GET .../shares/{token}` handler
  to be hit a second time (assert call count), and the rendered summary numbers update to a
  second payload's values once the handler starts returning it.
- Dispatching `revoked` swaps the page to the revoked terminal copy (asserted distinct from both
  the success report AND the pre-load `share:expired.title` copy — i.e. the two terminal copies
  and the pre-load copy are three genuinely different strings, per OQ1's resolution).
- Dispatching `expired` swaps to the expired terminal copy, likewise distinct from the other two.
- Unmounting the page (e.g. navigating away) closes the fake `EventSource` — no leak.

**Component — extend `publicBalanceTable.test.tsx`:**
- With the lightbox open on a given member, re-rendering with a `data` prop whose QR-list result
  no longer contains that member's `memberUuid` closes the dialog and shows the
  `share:stream.qrMemberSettledTitle` toast (the Step 4 guard) — this test can drive the prop
  change directly (no `EventSource`/stream involvement needed at this layer, since the component
  only ever reacts to its own query's data, which the parent's `invalidateQueries` call already
  refreshes independently of how it was triggered).
- (Regression) the lightbox opening/positioning behavior from `event-share-link.md`'s original
  test list is unaffected — the new effect from Step 4 only fires when the dialog is already
  open and the member set changes; it is inert on first-open.

**i18n — extend `shareI18n.test.ts`:**
- The three new key pairs (`stream.revokedTitle/Body`, `stream.expiredTitle/Body`,
  `stream.qrMemberSettledTitle/Body`) exist and are non-empty in both `vi-VN` and `en-US`, with
  the same interpolation tokens (none, in the current draft) on both sides.
- The three "terminal/settled" strings are asserted **not equal** to each other and **not
  equal** to the existing `expired.title`/`expired.body` strings — a cheap regression guard
  against accidentally collapsing OQ1's distinct-copy decision back into the generic pre-load
  screen.

All tests deterministic: pinned `TZ` (Asia/Ho_Chi_Minh) + locale (vi-VN), MSW for the plain JSON
refetches, `FakeEventSource` for the stream itself, no real network, no wall-clock waits (the
fake dispatches events synchronously/on-demand rather than waiting for a real heartbeat).

**Future (not this cycle):** a Playwright e2e run against a real running backend
(`FairShareMonApi` + MariaDB + Redis) that actually opens a native browser `EventSource` and
exercises a real settled-toggle mutation end-to-end — the unit/component tests above prove this
feature's own client-side logic in isolation, but only a real browser + real server can prove
the wire-level `EventSource` behavior (auto-reconnect, heartbeat tolerance) this plan assumes.

## Impact Analysis

- **APIs (consumed):** the new anonymous `GET /v1/public/shares/{token}/stream` (native
  `EventSource`, never the `api` client). No change to the request/response shape of either
  existing public endpoint — they are simply refetched more eagerly.
- **Routing:** none — no new route; `share/:token` is unchanged.
- **New source:** `src/features/share/hooks/useEventShareStream.ts`,
  `src/test/fakeEventSource.ts`.
- **Edited source:** `src/features/share/api/shareApi.ts` (+1 pure URL helper, no `api.*` call),
  `src/features/share/pages/PublicSharePage.tsx` (+1 hook call, +1 terminal branch),
  `src/features/share/components/PublicBalanceTable.tsx` (+1 guard effect for the QR-lightbox
  correctness gap), `src/i18n/locales/{vi-VN,en-US}/share.json` (+`stream.*` keys).
- **Reused (unchanged):** `usePublicShareQuery`, `usePublicShareMemberQrsQuery`, `shareKeys`,
  `queryClient`, `ErrorState`, `useToast`, `QrPreviewDialog`, `formatMoneyVnd`/`formatDateTime`,
  the entire owner-side `ShareEventDialog` (this feature is public-side only — the owner never
  needs a live stream of their own event).
- **New dependency:** none (native `EventSource`).
- **Test harness:** +1 new technique (`FakeEventSource` global stub) alongside the existing
  MSW-at-the-fetch-boundary convention — additive, does not replace or weaken MSW for anything
  else.
- **Infrastructure/backend:** none — purely consumes the already-shipped, already-reviewed
  contract as-is.
- **Docs:** this planning doc.
- **Security/privacy:** no new exposure — the stream carries no report data, and the terminal
  states (OQ1) only ever reveal a reason to a visitor who was already looking at a real,
  successfully-loaded report (never to someone probing an unknown token, which stays on the
  existing no-leak pre-load `16000` screen, untouched by this feature).

## Decision Log

> Locked decisions from `event-share-link.md` (ungated public route, identical pre-load
> "expired" copy for no-leak, `QrPreviewDialog`/`Table` reuse) are not reopened here. The backend
> contract's own locked decisions (`public-share-sse-updates.md` Decision Log 1–7) are likewise
> not reopened — this doc only decides how the *client* consumes that already-fixed contract.

1. **`EventSource` bypasses `src/lib/api/client.ts` entirely; a pure URL-builder helper is added
   to `shareApi.ts` instead of an `api.*` call.** **Reason:** native `EventSource` cannot attach
   any custom header (not a `client.ts` limitation to work around — a browser API constraint),
   and this anonymous route sends none anyway; routing it through the client would suggest a
   capability (interceptors, refresh, envelope unwrapping) that literally cannot apply here.
   **Alternative considered:** extending `client.ts` with an SSE mode — rejected, there is
   nothing for the client's cross-cutting concerns (auth header injection, 401 refresh, envelope
   parsing) to do on this transport.
2. **Named `addEventListener` per event type, never `onmessage`.** **Reason:** every frame the
   backend sends carries an explicit `event:` line (`connected`/`updated`/`revoked`/`expired`) —
   the default unnamed-message channel (`onmessage`/a bare `"message"` listener) never fires for
   any of them; using it would silently drop every frame.
3. **The client, not the server, is responsible for stopping reconnection on a terminal frame.**
   **Reason:** confirmed from the backend doc's own Impact Analysis note — native `EventSource`
   auto-reconnects on a dropped connection with no special server support, so `revoked`/`expired`
   are signals the client must act on by calling `.close()` itself, not events that already
   imply a stopped connection.
4. **The QR-list invalidation is unconditional on `updated` (subject to OQ4's confirmation), with
   a dedicated guard (Step 4) for the one correctness gap that unconditional invalidation
   creates** (a refetch resolving while the lightbox is open and the viewed member vanished from
   the list). **Reason:** the alternative (never proactively invalidating the QR key) leaves a
   visitor staring at a stale QR/amount for a member who just got settled while they were looking
   — a worse outcome than the bounded extra background fetch cost.
5. **No new query-key scheme.** The existing `shareKeys.public`/`shareKeys.publicQrs`
   (`useShare.ts`, shipped with `event-share-link.md`) are reused verbatim. **Reason:** they
   already key by `token` exactly as this feature needs; inventing a parallel key would just
   fragment the cache.

## Progress Log

### 2026-08-26

- Feature-planner: completed required reading — `FairShareMonApi/planning/public-share-sse-updates.md`
  in full (Objective, Requirements, Decision Log, the "Explicit dependency for the follow-up
  frontend planner" Impact Analysis note, Final Outcome incl. the code-review-found/fixed
  `PeriodicTimer`/channel-read-task bug), `FairShareMonApi/The-ideal.md` (confirmed this feature
  is not separately specced there — same as its parent `event-share-link.md`, which the backend
  built ahead of/alongside the spec), `FairShareMonWeb/CLAUDE.md`, `planning/frontend-foundation.md`
  (locked stack: React Router v7, TanStack Query v5, one centralized `api` client, error-code
  branching, i18n conventions), `planning/feature-roadmap.md` (milestone context — this sits
  inside the already-shipped M-adjacent `event-share-link.md` work, not a new roadmap milestone),
  and `planning/event-share-link.md` in full (the doc this feature extends, including its 8
  resolved Open Questions and the orchestrator's code-review closure notes).
- Read the live `src/`: `src/features/share/pages/PublicSharePage.tsx`,
  `src/features/share/hooks/useShare.ts`, `src/features/share/api/shareApi.ts`,
  `src/features/share/components/PublicBalanceTable.tsx`, `src/config/env.ts`,
  `src/lib/api/client.ts` (confirmed no header injection `EventSource` could reuse anyway),
  `vite.config.ts` (dev proxy has no buffering override — flagged as an Assumption to smoke-test,
  not a blocking Open Question, since Node's `http-proxy` streams by default), `package.json` +
  `src/test/setup.ts` (confirmed no `EventSource` polyfill/precedent and no jsdom-native support),
  and `src/i18n/locales/vi-VN/share.json` (existing `expired.*` no-leak copy, to design the new
  `stream.*` block against without duplicating it) plus the existing
  `src/features/share/publicSharePage.test.tsx` and the MSW handlers it drives, to match this
  doc's Tests section to the live test-authoring style.
- Found and documented one genuine correctness gap while verifying `PublicBalanceTable.tsx` that
  the task brief did not explicitly call out: a live QR-list refetch resolving while the
  lightbox is open, for a member who is no longer in the refreshed list, would silently
  recompute `startIndex` to a different member's slide rather than closing gracefully — added as
  a Requirement + Step 4 + a dedicated test, not left as a silent gap.
- Drafted the full Implementation Plan (6 steps: `shareApi.ts` URL helper,
  `useEventShareStream` hook, `PublicSharePage.tsx` wiring, `PublicBalanceTable.tsx` guard,
  `share.json` i18n additions, `FakeEventSource` test infrastructure), Impact Analysis, Decision
  Log, and the Tests section (flagging the `EventSource`-in-Vitest/jsdom gap the same way the
  backend doc flagged its own SSE integration-test technique).
- **Raised 4 Open Questions** (terminal-state UI/copy and its relationship to the existing
  no-leak pre-load screen; whether to touch the existing `staleTime` safety net; the
  `EventSource` testing technique; whether the QR-list invalidation should be gated on lightbox
  visibility), each with concrete options, trade-offs, and a recommendation. Awaiting the
  checkpoint before implementation starts.
- **Orchestrator resolved all 4 OQs** per the recommended options (see the RESOLVED note above
  the Assumptions section).
- Web-implementer: implemented Steps 1-6 as drafted — `shareApi.ts` URL helper,
  `useEventShareStream.ts`, `PublicSharePage.tsx` wiring + terminal branch,
  `PublicBalanceTable.tsx` QR-lightbox guard, both locales' `share:stream.*` keys,
  `src/test/fakeEventSource.ts`. Found during verification that the existing test suite broke
  (`EventSource is not defined`, jsdom has none) because nothing installed a default — fixed by
  wiring `FakeEventSource` into `src/test/setup.ts` as the harness default (same pattern as the
  file's other jsdom polyfills), which is test infrastructure, not a new test case. Also swapped
  the Step 4 guard's effect dependency from `members` to `qrQuery.data` (oxlint
  `react-hooks/exhaustive-deps`; `members ?? []` is a new array identity every render).
  `pnpm lint`/`tsc -b`/`pnpm build`/`pnpm test` all green (964/964 tests, no regressions). Ran a
  full manual smoke check against the real backend + real Vite dev server + real Playwright
  Chromium (no mocks): confirmed the dev-proxy streams the SSE response unbuffered, and drove the
  actual UI through settle-triggered `updated` (silent live refresh, no reload) and
  link-revoke-triggered `revoked` (full-page terminal swap) end-to-end. See Final Outcome for
  full detail.
- **Web-test-engineer:** wrote the full Tests section (OQ3's `FakeEventSource` technique
  throughout — no real network, no wall-clock waits, dispatches synchronous), across 4 files, 23
  new test cases:
  - **New `src/features/share/hooks/useEventShareStream.test.ts`** (10 tests, pure hook via
    `renderHook`, spying on the singleton `queryClient` the hook imports directly): `enabled:
    false`/empty-`token` never construct an `EventSource`
    (`UseEventShareStream_Disabled_NeverConstructsEventSource`,
    `UseEventShareStream_EmptyToken_NeverConstructsEventSourceEvenWhenEnabled`); enabled+token
    constructs exactly one at `shareApi.publicStreamUrl(token)`
    (`UseEventShareStream_EnabledWithToken_ConstructsExactlyOneEventSourceAtStreamUrl`); a
    same-props re-render opens no second connection
    (`UseEventShareStream_RerenderSameProps_DoesNotOpenSecondConnection`); unmount closes even a
    still-open connection (`UseEventShareStream_Unmount_ClosesConnectionEvenWithNoTerminalEvent`);
    a `token` change while mounted closes the old connection, opens exactly one new one at the new
    URL, and resets `terminalReason` to `null`
    (`UseEventShareStream_TokenChangesWhileMounted_ClosesOldOpensNewAndResetsTerminalReason`);
    `connected` is a no-op (`UseEventShareStream_ConnectedEvent_IsNoOp`); `updated` invalidates
    both `shareKeys.public(token)` and `shareKeys.publicQrs(token)` — exactly 2 calls
    (`UseEventShareStream_UpdatedEvent_InvalidatesReportAndQrListCaches`); `revoked`/`expired` each
    set the matching `terminalReason` AND call `.close()`
    (`UseEventShareStream_RevokedEvent_SetsTerminalReasonAndClosesConnection`,
    `UseEventShareStream_ExpiredEvent_SetsTerminalReasonAndClosesConnection`). Needed
    `act(() => source.dispatch(...))` around every dispatch that triggers a `setState` (React 19 —
    `renderHook`'s `result.current` does not observe a state update fired outside `act`); the
    `connected`/`updated` dispatches (no state change) needed no such wrapper.
  - **Extended `publicSharePage.test.tsx`** (+6 tests, new `describe("PublicSharePage live-update
    stream …")`): the stream never opens while `usePublicShareQuery` is pending
    (`PublicSharePage_ReportPending_NeverOpensEventSource`) nor on the pre-load `16000` path
    (`PublicSharePage_16000Path_NeverOpensEventSource` — regression guard, the existing
    identical-copy-for-expired/revoked/missing test is untouched); dispatching `updated` causes
    the MSW `GET .../shares/:token` handler to be hit a second time and the rendered summary
    number to flip from the first payload's ₫800.000 to a second payload's ₫950.000
    (`PublicSharePage_UpdatedEvent_RefetchesReportAndRendersNewNumbers`); `revoked`/`expired` each
    swap to their own terminal copy, asserted textually distinct from the success report, from
    the pre-load `share:expired.title` no-leak screen, AND from each other — four genuinely
    different strings in play across both tests
    (`PublicSharePage_RevokedEvent_SwapsToDistinctRevokedTerminalCopy`,
    `PublicSharePage_ExpiredEvent_SwapsToDistinctExpiredTerminalCopy`); unmounting closes the fake
    `EventSource` (`PublicSharePage_Unmount_ClosesTheFakeEventSource`). **One test-harness
    subtlety found and worked around, not a product bug:** `useEventShareStream`'s `updated`
    handler invalidates via the app's *singleton* `queryClient` (imported directly, per Decision
    Log #1's `EventSource`-bypasses-`client.ts` reasoning extended to the query-cache side too —
    the hook never calls `useQueryClient()`), while `usePublicShareQuery`'s `useQuery` reads
    whichever client the enclosing `QueryClientProvider` supplies. In production there is exactly
    one such provider, wired to that same singleton (`src/app/providers.tsx`) — they always
    coincide. `renderWithProviders`'s default behavior of minting a fresh, unrelated `QueryClient`
    per test (so most specs stay isolated from each other) breaks that coincidence purely as a
    test-harness artifact: invalidating the singleton would be a no-op for a query mounted against
    a different client instance, and the refetch would never fire (confirmed by a 5s test timeout
    before the fix). Fixed by passing the real singleton into `renderPage`'s new optional
    `queryClient` param for that one test (reproducing the real provider wiring exactly), with
    `appQueryClient.clear()` at the end of the test to keep the shared singleton clean for
    anything else that imports it. No other test in the file needed this, since none of the
    others depend on cross-instance cache invalidation.
  - **Extended `publicBalanceTable.test.tsx`** (+3 tests, new `describe("PublicBalanceTable
    QR-lightbox live-update guard …")`, new `renderTableWithClient` helper that hands back its
    `QueryClient` so a test can call `invalidateQueries` on the exact instance the mounted
    `usePublicShareMemberQrsQuery` reads from — deliberately bypassing `EventSource`/the stream at
    this component-test layer, per the planning doc's own note that the guard only ever reacts to
    its own query's data regardless of what triggered the refetch): opening the lightbox on Bình
    Trần, then invalidating the QR-list query after re-pointing the MSW handler to a list without
    her, closes the dialog and shows the `share:stream.qrMemberSettledTitle`/`Body` toast
    (`PublicBalanceTable_ViewedMemberDropsFromRefreshedQrList_ClosesDialogAndShowsToast`); an
    invalidation whose refetch still contains the viewed member leaves the dialog open with no
    toast (`PublicBalanceTable_QrListRefreshesWithViewedMemberStillPresent_DialogStaysOpenNoToast`
    — an extra case beyond the doc's literal list, guarding the guard's own condition rather than
    only its positive case); opening the lightbox for the very first time never trips the new
    effect (`PublicBalanceTable_FirstOpenOfLightbox_GuardEffectIsInert_NoToast`), alongside the
    untouched pre-existing `PublicBalanceTable_ClickQr_FetchesOnceAndOpensPreviewAtClickedMember`/
    `PublicBalanceTable_ClickFirstMemberQr_OpensAtIndexZero` tests confirming the original
    `event-share-link.md` open/position behavior is unaffected.
  - **Extended `shareI18n.test.ts`** (+4 tests, new `describe("share i18n stream keys …")`): the
    six `stream.*` leaves are non-empty in both locales
    (`ShareStreamKeys_ExistAndAreNonEmpty_InBothLocales`); their (currently empty) interpolation
    token sets match across locales, as an OQ-specific companion to the whole-namespace parity test
    (`ShareStreamKeys_InterpolationTokens_MatchAcrossLocales`); `revokedTitle`/`expiredTitle`/
    `qrMemberSettledTitle` (and their `*Body` counterparts) are pairwise distinct within each
    locale (`ShareStreamKeys_RevokedAndExpiredAndQrSettled_ArePairwiseDistinct`); and
    `stream.revoked*`/`stream.expired*` are each distinct from the pre-load `expired.title/body`
    no-leak copy in both locales — the direct regression guard against re-collapsing OQ1's
    distinct-copy decision (`ShareStreamKeys_RevokedAndExpired_AreDistinctFromThePreLoadExpiredCopy`).
  - **Quality gate:** `pnpm lint` clean (only the same 7 pre-existing unrelated
    `only-export-components` warnings); `tsc -b` clean; `pnpm test` — **987/987 passing, 115/115
    files** (964 pre-existing + 23 new, zero regressions), run twice back-to-back for determinism.
    No product bug found — the implementation matched the planning doc's Requirements/Decision Log
    exactly; the only issues encountered (`act()` wrapping for direct `dispatch()` state updates,
    and the singleton-vs-fresh-`QueryClient` test-harness mismatch above) were test-authoring
    concerns, not product defects. No coverage gaps against the doc's Tests section — every bullet
    listed there has a corresponding test; the Playwright e2e item is explicitly out of this
    cycle's scope (Future/already covered by the web-implementer's manual smoke run).

#### Code review + fixes (orchestrator, 2026-08-26)

- **Finding 1 (nit) — nested conflicting ARIA live-region roles on the terminal-state swap.**
  `ErrorState` (`src/components/ui/Feedback/ErrorState.tsx`) already renders `role="alert"`
  (assertive, self-announcing) on its own root. The terminal branch in `PublicSharePage.tsx`
  wrapped it in an outer `role="status" aria-live="polite"` `div` — inert in practice, since the
  inner `alert` fires assertively regardless, so the stated intent ("a polite announcement") did
  not match what assistive tech actually does. **Fix:** dropped the outer wrapper/attributes;
  `ErrorState`'s own `role="alert"` already satisfies the "announce the swap" requirement without
  the misleading `polite` framing.
- **Finding 2 (nit) — QR-lightbox guard could paint the wrong member's slide for one frame before
  closing.** The guard effect (Step 4) only runs after commit, so the same render that updates
  `qrQuery.data` also recomputes `startIndex` (clamped to `0` when the viewed member is gone) and
  passes it to an still-`open` `QrPreviewDialog` — a brief window showing an unrelated member's
  slide before the effect's next pass closes the dialog. This is exactly the "silently jumping to
  an unrelated member's slide" outcome the doc's Requirements call out to avoid. **Fix:** added a
  render-time `targetStillPresent` check (`PublicBalanceTable.tsx`) and gated `QrPreviewDialog`'s
  `open` prop on `previewOpen && targetStillPresent`, so the wrong slide is never painted even for
  one frame; the effect now only owns the bookkeeping (toast + `setPreviewOpen(false)`) once that
  render-time gate has already hidden the dialog.
- Both fixes verified: `pnpm lint` clean, `tsc -b` clean, full suite **987/987 passing, 115/115
  files** (no regressions from either fix).
- Everything else the reviewer checked (effect correctness/no-duplicate-connection/cleanup, the
  singleton-`queryClient` import pattern — confirmed pre-existing/intentional via `useShare.ts`'s
  `useCreateShareLink`/`useRevokeShareLink`, terminal-branch ordering vs. the pre-load `16000`
  screen, i18n distinctness, test quality) came back clean — no other findings.

## Final Outcome

Implemented exactly per the Implementation Plan (Steps 1-6), all 4 Open Questions resolved per
the orchestrator's note (all recommended options). Code review found two nits (see "Code review +
fixes" above), both fixed and re-verified — no other deviations:

- **`src/features/share/api/shareApi.ts`** — added `publicStreamUrl(token)`, a pure URL-builder
  (not an `api.*` call), plus the `import { env } from "@/config/env"`.
- **`src/features/share/hooks/useEventShareStream.ts`** (new) — the hook exactly as drafted:
  named `addEventListener` per event (`updated`/`revoked`/`expired`), `.close()` on either
  terminal event and on unmount/token-change, `terminalReason` state reset on (re)connect.
- **`src/features/share/pages/PublicSharePage.tsx`** — wired `useEventShareStream(token, {
  enabled: query.isSuccess })`; added the terminal-reason branch (checked first, above
  `isPending`/`isError`/success), reusing `ErrorState` (whose own `role="alert"` announces the
  swap — see Finding 1, the outer `role="status" aria-live="polite"` wrapper originally added here
  was removed as redundant/misleading) with new `share:stream.{revoked,expired}{Title,Body}` keys
  — distinct from the pre-load `share:expired.*` no-leak copy, per OQ1(a).
- **`src/features/share/components/PublicBalanceTable.tsx`** — added the QR-lightbox correctness
  guard effect: closes `QrPreviewDialog` + shows an info toast when the currently-viewed member
  drops out of a refreshed `qrQuery.data`, PLUS (see Finding 2) a render-time `targetStillPresent`
  gate on `QrPreviewDialog`'s `open` prop so the wrong member's slide is never painted even
  transiently. One deliberate deviation from the doc's literal snippet: the effect depends on
  `qrQuery.data` (not the `members = qrQuery.data ?? []` local), because the `?? []` fallback
  creates a new array identity every render and would otherwise re-run the effect on every render
  (oxlint `react-hooks/exhaustive-deps` flagged this on first pass) — functionally identical, just
  a stable dependency reference. Same pattern the file's pre-existing `pendingOpen` effect already
  uses (`members.length`, not `members`) for the exact
  same reason.
- **i18n** — added `share:stream.{revokedTitle,revokedBody,expiredTitle,expiredBody,
  qrMemberSettledTitle,qrMemberSettledBody}` to both `vi-VN` and `en-US`, using the doc's
  suggested vi-VN copy verbatim and a natural en-US translation.
- **`src/test/fakeEventSource.ts`** (new) — `FakeEventSource` class (readyState,
  `CONNECTING`/`OPEN`/`CLOSED`, `addEventListener`/`removeEventListener`/`close()`), a
  module-level instance registry (`fakeEventSourceInstances`, `latestFakeEventSource`), a
  test-only `dispatch(eventName, data?)`, and `resetFakeEventSources()`.
- **One addition beyond the doc's literal file list, required to keep the existing suite
  green:** `src/test/setup.ts` now installs `FakeEventSource` as the default global `EventSource`
  (the same `if (typeof globalThis.X === "undefined")` idiom already used there for
  `ResizeObserver`/`URL.createObjectURL`), and clears the instance registry in the global
  `afterEach` alongside `server.resetHandlers()`. Without this, every pre-existing test that
  renders `PublicSharePage` to a successful state (none of which know anything about SSE) threw
  `ReferenceError: EventSource is not defined` the moment `usePublicShareQuery` resolved, because
  jsdom has no `EventSource` at all and nothing installed a stand-in. This is squarely within
  "test infrastructure... you may need it to exist for anything that currently fails to
  compile/run" — no new test *cases* were authored (still the web-test-engineer's job per OQ3's
  Tests section), only the harness default that OQ3(a) itself says should "mirror how
  `src/test/setup.ts` already polyfills other jsdom-missing browser APIs."

**Quality gate:**
- `pnpm lint` — clean (only the 7 pre-existing `only-export-components` warnings, unrelated to
  this feature; zero warnings/errors from the new code after the `qrQuery.data` dependency fix).
- `tsc -b` (via `pnpm build`) — clean; `vite build` succeeds (only the pre-existing >500kB chunk
  warning, unrelated).
- `pnpm test` — **964/964 passing, 114/114 files**, including the previously-existing
  `publicSharePage.test.tsx` / `publicBalanceTable.test.tsx` / `shareI18n.test.ts` (20 tests)
  unchanged and still green. (The web-test-engineer's own new SSE-specific test cases per this
  doc's Tests section are a separate, later cycle.)
- **Manual smoke check — done against the real stack**, not just mocks: started the real backend
  (`dotnet run`, port 5200, MariaDB + Redis already running) and the real Vite dev server (port
  5173). Verified the dev-proxy Assumption directly: `curl -N` against
  `/api/v1/public/shares/{token}/stream` through the Vite proxy returned the `connected` frame
  immediately (not buffered until connection close), byte-for-byte matching a direct `curl` to
  the backend on 5200 — confirms Node's `http-proxy` streams unbuffered as assumed; no new Open
  Question needed. Then drove a full Playwright script (headless Chromium, real native
  `EventSource`, zero mocks) through the actual UI: registered a user, granted Premium via the
  seeded admin account, created a member/event/expense, closed the event, created a share link,
  and loaded `/share/:token` in the browser — confirmed initial "Còn nợ" (owing) state; called the
  real `PUT .../events/{uuid}/members/{uuid}/settled` mutation (which the backend fires
  `event: updated` for) and watched the page silently flip to "Đã trả" (settled) with **no
  reload/navigation** (`page.url()` unchanged) — the live `updated → invalidateQueries →
  TanStack refetch` pipeline confirmed end-to-end; then called `DELETE .../events/{uuid}/share`
  (revoke) and watched the page do a full-page replace to the "Liên kết đã bị thu hồi" terminal
  screen, textually distinct from both the success report and the pre-load `share:expired.*`
  copy. Zero browser console errors across the whole run. Screenshots taken at all three states.

No Open Questions added — all 4 pre-resolved ones were followed as-is. No deviation from the
Implementation Plan except the `qrQuery.data`-vs-`members` dependency-array detail (Step 4) and
the additive `setup.ts` default polyfill (both noted above, both required only to satisfy the
existing quality bar, not a change in behavior).

## Future Improvements

- A small "live" indicator (e.g. a quiet pulsing dot/badge near the report header) once the
  stream's `connected` frame is received, so a visitor can tell the page is actively watching
  for updates rather than a one-time snapshot — deliberately out of this MVP scope (the task's
  core ask is silent auto-refresh, not a connectivity indicator).
- Reconnect-on-`visibilitychange` handling for mobile browsers that aggressively suspend
  background-tab network connections, if real-world usage shows visitors missing updates after
  backgrounding the tab for a while.
- Surfacing a specific "you may be viewing a stale report" hint if a connection-level `error`
  persists for an extended period with no terminal frame (currently silently falls back to the
  existing `staleTime`/refocus path — see OQ2/Requirements).
- The Playwright e2e coverage against a real running backend, noted in the Tests section.
- If the backend ever adds a structured signal payload (its own Future Improvements list
  mentions a monotonic version counter or the specific changed member/expense UUID), the client
  could animate/highlight exactly what changed instead of a full silent re-fetch-and-diff.
