# Public share report: live updates via Server-Sent Events (SSE)

## Objective

Extend the shipped "event share link" feature (`planning/event-share-link.md`) so the **anonymous**
public report (`GET api/v1/public/shares/{token}`) can **auto-update in real time** instead of
requiring the viewer to manually reload/poll. Add a new anonymous SSE endpoint
`GET api/v1/public/shares/{token}/stream` that holds the connection open and pushes a lightweight
"something changed" signal whenever the underlying settled/outstanding overlay of the shared CLOSED
event changes, or when the link itself is revoked/replaced. The client reacts to the signal by
re-fetching the existing plain-JSON endpoints (`.../shares/{token}` and, if shown,
`.../shares/{token}/qr/members`) — the SSE layer never carries the report payload itself.

## Background

Verified against the live, already-shipped code (2026-08-26):

- **The public report is a LIVE read, by design** (`event-share-link.md` Decision 3 — locked, not
  reopened): `EventShareService.GetPublicAsync` recomputes `statsService.GetEventBalanceAsync` +
  `expensesService.ListDetailedByEventAsync` on every call using the **owner's** UUID resolved from
  the token via `IEventShareLinkCache.LookupAsync`. A closed event's spend figures are frozen but the
  settled/outstanding overlay is not. There is currently **no push mechanism** — the frontend
  (`FairShareMonWeb`, per its own `planning/event-share-link.md`) fetches once via TanStack Query with
  a `15000`ms `staleTime` and never auto-refetches.
- **A share link only ever points at a CLOSED event** (`event-share-link.md` Decision 5 / §4.4), and a
  closed event allows exactly one class of write: the settled flag (§3.5/§4.4, "sole exception"). Three
  service methods perform that write today, all already shipped (`planning/settled-per-member.md`):
  1. `IEventsService.SetMemberSettledAsync(userUuid, eventUuid, memberUuid, request, ct)` — Layer B,
     per-member-per-event net clearance (`EventsController` `PUT
     {uuid}/members/{memberUuid}/settled`). Already has `eventUuid` as a direct parameter — no
     resolution needed.
  2. `IExpensesService.SetSettledAsync(userUuid, expenseUuid, request, ct)` — whole-expense settled,
     cascades to the expense's billable shares (`ExpensesController` `PUT {uuid}/settled`). Only has
     `expenseUuid`; the owning event (if any) must be resolved.
  3. `ISharesService.SetSettledAsync(userUuid, expenseUuid, shareUuid, request, ct)` — per-share settled
     (`ExpensesController` `PUT {uuid}/shares/{shareUuid}/settled`). Same: only `expenseUuid` is known
     to the service.
  - Verified in the repository layer (`Repositories/ExpenseRepository.cs` /
    `Repositories/ShareRepository.cs`): both `SetSettledAsync` methods load the tracked `Expense` with
    `.Include(entity => entity.Shares)` and read `expense.EventId` (a plain `ulong?` FK already on the
    entity, no extra `Include` needed) to drive the M2 credit-applier
    (`EventSettlementCreditApplier.ApplyAsync`) that was added by the still-in-flight
    `event-expense-settlement-sync` feature (block 17xxx). Neither method's return type
    (`Task<ExpenseWriteStatus>`) currently exposes the event's UUID (only the numeric `EventId`, and
    only inside the transaction) — the **service** layer (`ExpensesService`/`SharesService`) never sees
    it today.
  - Also verified: `EventShareService.RevokeAsync` soft-revokes the active link
    (`IEventShareLinkRepository.RevokeActiveByEventAsync`) and evicts the Redis cache entry
    (`IEventShareLinkCache.RemoveAsync`) — it does not currently notify anything else.
    `EventShareService.CreateAsync`'s `request.Regenerate` branch does the **exact same**
    revoke-then-evict on the OLD token before minting a new one — this is a second call site with the
    identical "this token is now dead" effect that the brief only mentioned for `RevokeAsync`; both must
    terminate any live stream on the old token.
- **Deployment is single-instance** — confirmed in `deployment/docker-compose.yml`: exactly one
  `fsm-api` container (`fairsharemon-api-1`), no replicas, no load balancer across API instances. An
  **in-process** broadcaster (a `Singleton` holding one `System.Threading.Channels.Channel<T>` per
  active subscriber) is correct for the current topology; a cross-instance broadcaster (Redis Pub/Sub —
  `IConnectionMultiplexer` is already wired, `Program.cs` line 159) would be premature. Documented as a
  Future Improvement, not built now.
- **nginx reverse-proxies the API** (`deployment/config/nginx/conf.d/api.conf`): the catch-all
  `location /` has no `proxy_buffering` directive, so nginx's default (`on`, confirmed — no
  `proxy_buffering` directive anywhere in `deployment/config/nginx/nginx.conf`'s `http {}` block either)
  applies, which buffers the entire response before forwarding — this breaks SSE. There is also no
  `proxy_read_timeout` override, so nginx's default `60s` would kill an idle stream. **Nuance found
  while verifying the file**: the `server {}` block sets four shared `proxy_set_header` directives
  (`Host`, `X-Real-IP`, `X-Forwarded-For`, `X-Forwarded-Proto`) with an explicit comment: *"inherited by
  every location below, which set none of their own — so inheritance applies."* nginx's inheritance rule
  for `proxy_set_header` is all-or-nothing per location: if a location block adds even one
  `proxy_set_header` of its own, it stops inheriting **all** of the parent's. The new stream location
  must therefore add **no** `proxy_set_header` of its own (only `proxy_buffering off` /
  `proxy_read_timeout` / `proxy_pass`), so it keeps inheriting the four shared headers automatically —
  this is the safest, smallest diff and avoids silently breaking real-client-IP/HTTPS-detection for this
  one route.
- **Bypassing the `[ResponseWrapped]` JSON envelope has an established precedent in this codebase**:
  `EventsController.ExportAsync` / `.../qr` and `ExpensesController.ExportAsync` / `.../qr` all
  `return File(content, contentType, fileName)`. `ResponseWrappedAttribute.OnResultExecuting`
  (`Attributes/ResponseWrappedAttribute.cs`) only rewraps when `context.Result is ObjectResult` — a
  `FileContentResult` (or, for this feature, manual writes to `HttpContext.Response` followed by
  `EmptyResult`) is left untouched. The **error** path for those same actions still throws
  `ErrorException` from the service *before* the action calls `File(...)` / writes anything, so it is
  caught by `Attributes/MvcFilters/ErrorHandlerFilter.cs`'s `OnException` (an `IExceptionFilter`, runs
  as part of the normal MVC filter pipeline, before any response bytes are written) and turned into the
  standard wrapped `ApiResult.Failure(...)` 404 JSON — this resolves, by direct precedent, the "can you
  404 after starting to stream?" question from the brief: **the check must happen (and, by this
  precedent, naturally does) before any byte is written**, and the existing filter machinery already
  gives that for free as long as the validity check happens before `Response.ContentType` is set / any
  `Response.WriteAsync` runs.
- **Config precedent**: `Share:LinkTtlHours` (default `24`) is already a configurable value read via
  `configuration.GetValue(...)` inside `EventShareService`'s primary-constructor field initializer. A
  heartbeat interval will follow the same pattern (`Share:StreamHeartbeatSeconds`).
- **ErrorCodes**: `16xxx` (event share link) already defines `ShareLinkNotFoundOrExpired = 16000` and
  `EventNotClosedForShare = 16001`; `17xxx` is reserved by `event-expense-settlement-sync` (in flight);
  `18xxx` is claimed by `bank-callback-settlement`. This feature needs **zero new error codes** — an
  invalid/unknown/expired/revoked token on the new stream endpoint reuses `16000` verbatim, exactly like
  the plain `GET`. Confirmed next free block is `19xxx` (not needed here).
- **Test harness precedent**: `PublicShareEndpointTests.cs` (`[Collection("AuthIntegration")]`, extends
  `ExpenseApiTestBase`, `[SkippableFact]`, real MariaDB+Redis, `Factory.CreateClient()` with **no** auth
  header) is the direct model for a new `PublicShareStreamEndpointTests.cs`. All existing tests use a
  single request/response round trip (`PostAsJsonAsync`, `GetAsync`) — none hold a connection open and
  read incrementally. This is a genuinely new technique for this suite (see Tests section).

## Requirements

- A new anonymous endpoint `GET api/v1/public/shares/{token}/stream` (`text/event-stream`) that:
  - Validates the token exactly like the plain `GET` before writing anything; unknown/expired/revoked →
    404 `ShareLinkNotFoundOrExpired` (16000), same envelope as today.
  - On success, holds the connection open and emits an `event: updated` frame whenever the shared
    event's settled/outstanding overlay changes (any of the three settled-toggle mutations above, when
    they land on the event currently behind this token).
  - Emits a terminal frame and closes the stream when the link becomes invalid while connected — either
    because the owner explicitly revoked/regenerated it, or because its `ExpiresAt` naturally elapsed.
  - Sends periodic heartbeat comments so the connection survives the reverse proxy's idle-read timeout.
  - Never reads `AuthenticatedUser`; never re-gates Premium (mirrors the plain GET/QR routes, §4 rule 9).
- The three settled-mutation call sites (`EventsService.SetMemberSettledAsync`,
  `ExpensesService.SetSettledAsync`, `SharesService.SetSettledAsync`) notify the broadcaster **after**
  their own write has committed, and only when the affected event currently has an active share link —
  a best-effort, non-throwing side effect that must never fail (or slow down materially) the underlying
  settled-toggle request.
- `EventShareService.RevokeAsync` and the `Regenerate` branch of `EventShareService.CreateAsync` both
  terminate any live stream on the token they just invalidated.
- nginx is reconfigured so this one route is not buffered and survives longer than the default 60s idle
  read timeout.
- No EF migration, no new error codes, no new persisted state — this is a pure in-process, ephemeral
  notify pipe layered on the already-shipped share-link machinery.

## Open Questions

1. **Heartbeat / re-validation interval.** The stream needs a periodic tick both to keep the proxy
   connection alive (nginx's default `proxy_read_timeout` is 60s) and to detect a link's **natural**
   expiry while nobody is actively mutating anything (revoke/regenerate push an explicit terminal signal
   immediately, but a link simply ageing past `ExpiresAt` with the tab left open needs the loop to
   re-check `IEventShareLinkCache.LookupAsync` on its own). There's no single "correct" number — it
   trades server-side resource cost (Redis `LookupAsync` calls × open connections) against how quickly
   an expired tab notices and against safety margin under the proxy timeout. Options:
   - **(a) 20 seconds (recommended).** Comfortably under the 60s proxy timeout (3 heartbeats of margin
     even if one tick is delayed), a viewer notices natural expiry within ~20s of it happening, and the
     added Redis load is one cheap `GET` per open tab per 20s — negligible at this feature's expected
     scale (a handful of anonymous viewers per shared event).
   - **(b) 30 seconds.** Half the load of (a), still under 60s but with less margin if the process is
     briefly under load (e.g. GC pause) — a missed tick could brush against the proxy timeout.
   - **(c) 10 seconds.** Snappier expiry detection, doubles the idle Redis chatter for no functional gain
     since updates are already pushed immediately by the mutation path — the interval only matters for
     the natural-expiry case and for keeping the pipe alive.
   **Recommendation: (a) 20s**, exposed as `Share:StreamHeartbeatSeconds` (config, like
   `Share:LinkTtlHours`) so it can be tuned without a redeploy and overridden to a tiny value in tests.
   Please confirm or pick another value.

2. **Distinguish "revoked/regenerated" from "naturally expired" in the terminal SSE event, or use one
   generic name?** Both end the stream, but they mean different things to a viewer ("the owner took the
   link down" vs. "this link's 24h window ran out") and the follow-up frontend doc needs one fixed
   contract to build against. Options:
   - **(a) Two distinct event names: `event: revoked` (owner explicitly revoked/regenerated) vs.
     `event: expired` (heartbeat-detected natural TTL elapse) (recommended).** Costs nothing extra to
     implement (the broadcaster already knows *why* it's tearing down a token — the revoke/regenerate
     call sites publish one signal kind, the heartbeat's own re-validation publishes the other) and lets
     the frontend show an accurate final message instead of a generic "no longer available".
   - **(b) One generic `event: expired` for both.** Simpler contract, but the frontend can't tell a
     viewer *why* the report vanished; it would have to re-fetch the plain GET and infer from the
     resulting 404 anyway (which also doesn't distinguish the two).
   **Recommendation: (a).** Please confirm — this fixes the exact event-name contract the frontend
   planner will build against.

3. **Does the per-member QR list (`.../shares/{token}/qr/members`) need its own live signal, or does the
   single report stream's `updated` event cover it too?** A member's QR set changes exactly when the
   overlay's outstanding set changes (same underlying trigger as the report), so re-fetching the QR list
   on the same `updated` signal the report reacts to is sufficient information-wise. Options:
   - **(a) One unified stream; `updated` means "re-fetch whichever of the report/QR panel is currently
     shown" (recommended).** Simplest: one endpoint, one broadcaster, one notifier call per mutation. The
     QR images are comparatively expensive to regenerate (calls into `IWalletQrService`/VietQR rendering
     per member); a client that isn't showing the QR panel simply doesn't re-fetch it on the signal, so
     there's no wasted work either way.
   - **(b) A second dedicated stream / a distinguishable signal payload for "QR changed" vs "report
     changed".** No real information gain (they change together) for real extra plumbing (a second
     broadcaster key, or a signal-type field the client must branch on) — rejected unless the planner
     finds a case where they diverge, and none was found.
   **Recommendation: (a).** Please confirm — the frontend doc needs to know there is exactly one stream
   endpoint per token.

> **RESOLVED 2026-08-26 (orchestrator).** All 3 Open Questions resolved per the feature-planner's own
> recommendations, verbatim: OQ1 → (a) 20s heartbeat (`Share:StreamHeartbeatSeconds`); OQ2 → (a) distinct
> `revoked` vs `expired` terminal event names; OQ3 → (a) one unified stream, no separate QR-only signal.
> No changes to the Implementation Plan are needed — it was already written against these choices.

## Assumptions

- The plain `GET api/v1/public/shares/{token}` and `.../qr/members` endpoints, their DTOs, and the
  existing `IEventShareLinkCache`/`IEventShareLinkRepository` methods are **unchanged** — this feature
  only adds a notify pipe on top of the already-shipped read path. No existing method signature on
  `IEventShareLinkRepository` or `IEventShareLinkCache` is modified (only reused as-is).
- "The affected event currently has an active share link" is resolved with
  `IEventShareLinkRepository.GetActiveByEventAsync(userUuid, eventUuid, ct)` (existing, unmodified,
  owner-scoped) — the same method the owner-facing `GetActiveAsync`/`RevokeAsync` already use. When it
  returns null (no active link for that event), the mutation is a normal no-op for this feature: nothing
  to notify.
- A settled mutation on an event **without** an active link still succeeds exactly as it does today;
  this feature never blocks, slows materially, or changes the outcome of the underlying write — the
  notify step is additive and best-effort (wrapped so it can never surface as a failure of the mutation
  request).
- The SSE stream carries **no report data** — only a signal (event name, empty/trivial `data:` body,
  since the SSE spec requires a non-empty data buffer for native `EventSource` to actually dispatch the
  event to listeners — a comment-only frame is silently ignored by the client). The client is expected to
  re-fetch the plain JSON endpoint(s) on every signal; this preserves the single "live, recomputed on
  read" source of truth and avoids a second serialization/consistency path.
- One open SSE connection == one subscriber == one `Channel<EventShareStreamSignal>`; multiple tabs on
  the same token are multiple independent subscribers (fan-out), which the broadcaster supports natively
  by keying subscribers under the token.
- Abuse/DoS controls beyond the reverse proxy's existing `limit_conn perip 20;` (already applies to every
  route, including the new one, since it's set at the `server {}` level) are out of scope, consistent
  with `event-share-link.md`'s own Future Improvements ("abuse controls on the anonymous routes... if
  they see abuse") — not reopened here.

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/` unless noted. No EF migration. Vietnamese for all
> Swagger text. Written against the OQ recommendations (20s heartbeat, distinct revoked/expired event
> names, one unified stream) — adjust the two flagged spots if the checkpoint picks otherwise.

### Step 1 — In-process broadcaster (new, Singleton)

New file `Services/Api/Share/EventShareStreamBroadcaster.cs`:

```csharp
public enum EventShareStreamSignalType { Updated, Revoked, Expired }

public readonly record struct EventShareStreamSignal(EventShareStreamSignalType Type);

public interface IEventShareStreamSubscription : IDisposable
{
    ChannelReader<EventShareStreamSignal> Reader { get; }
}

public interface IEventShareStreamBroadcaster
{
    IEventShareStreamSubscription Subscribe(string token);
    void PublishUpdated(string token);
    void PublishRevoked(string token);   // OQ2a — explicit owner action (Revoke / Regenerate)
    void PublishExpired(string token);   // OQ2a — heartbeat-detected natural TTL elapse
}
```

`[SingletonService(typeof(IEventShareStreamBroadcaster))] sealed class EventShareStreamBroadcaster`
holds `ConcurrentDictionary<string /*token*/, ConcurrentDictionary<Guid /*subscriptionId*/,
ChannelWriter<EventShareStreamSignal>>>`.

- `Subscribe(token)`: creates `Channel.CreateBounded<EventShareStreamSignal>(new
  BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true,
  SingleWriter = false })` (small capacity + drop-oldest is safe because signals are pure "something
  changed" notifications, not payloads — a dropped duplicate loses nothing the client wouldn't already
  learn from the next one, and this guarantees a publish from a mutation-service request path never
  blocks on a slow/stalled reader). Registers the writer under `(token, subscriptionId)`; returns a small
  `IEventShareStreamSubscription` implementation whose `Dispose()` removes the entry (and the token's
  bucket if now empty).
- `PublishUpdated`/`PublishRevoked`/`PublishExpired(token)`: if the token has subscribers, `TryWrite` the
  corresponding signal to each; the two terminal variants additionally `TryComplete()` each writer after
  writing (the controller's read loop breaks on a terminal signal before attempting another read, so this
  is pure resource hygiene, not required for correctness).
- No dependency on any other service — deliberately leaf-level so `[SingletonService]` has no scoped
  dependencies to worry about (DiDecoration/ASP.NET Core would reject a singleton depending on a scoped
  service).

### Step 2 — Resolve an expense's owning event (new repository read)

`Repositories/ExpenseRepository.cs` — add to `IExpenseRepository`:

```csharp
/// <summary>Resource-owned lookup of the expense's owning event UUID (null for a loose expense or an
/// ownership miss). Backs the SSE notify seam (planning/public-share-sse-updates.md) — a plain read, no
/// change to any write method's signature.</summary>
Task<string?> GetEventUuidAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default);
```

Impl: `ExecuteQueryAsync((_, ct) => Query().Where(expense => expense.Uuid == expenseUuid &&
expense.User.Uuid == userUuid).Select(expense => expense.Event != null ? expense.Event.Uuid :
null).FirstOrDefaultAsync(ct), cancellationToken)`. Deliberately a **new read method**, not a change to
`SetSettledAsync`'s return type — keeps the existing write methods (and every existing caller/test of
them) untouched, and matches the rules.md convention "perform post-commit side-effects... after the
transaction delegate returns, not inside it": the notify step is a separate, best-effort read that runs
after the write has already committed.

### Step 3 — Update notifier (new, Scoped seam for the three mutation services)

New file `Services/Api/Share/EventShareUpdateNotifier.cs`:

```csharp
public interface IEventShareUpdateNotifier
{
    /// The event's UUID is already known to the caller (EventsService). No-op if it has no active link.
    Task NotifyEventChangedAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default);

    /// The caller only knows an expenseUuid (ExpensesService/SharesService). Resolves the owning event
    /// first (no-op for a loose expense), then behaves like NotifyEventChangedAsync.
    Task NotifyExpenseChangedAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default);
}
```

`[ScopedService(typeof(IEventShareUpdateNotifier))] sealed class EventShareUpdateNotifier(
    IEventShareLinkRepository shareLinkRepository, IExpenseRepository expenseRepository,
    IEventShareStreamBroadcaster broadcaster, ILogger<EventShareUpdateNotifier> logger) :
    IEventShareUpdateNotifier`:

- `NotifyEventChangedAsync`: wrapped in `try/catch` (logs a Warning, never rethrows — this must never
  fail an already-committed settled-toggle request). Calls
  `shareLinkRepository.GetActiveByEventAsync(userUuid, eventUuid, ct)` (existing, unmodified signature);
  if non-null, `broadcaster.PublishUpdated(active.Token)`.
- `NotifyExpenseChangedAsync`: `var eventUuid = await expenseRepository.GetEventUuidAsync(userUuid,
  expenseUuid, ct); if (eventUuid is not null) await NotifyEventChangedAsync(userUuid, eventUuid, ct);`
  (loose expense → no-op, no repository call to `GetActiveByEventAsync` at all).

### Step 4 — Wire the three mutation services

- `Services/Api/Events/EventsService.cs` — inject `IEventShareUpdateNotifier shareUpdateNotifier`
  (primary-constructor param). In `SetMemberSettledAsync`, after the `switch` reaches the `Success` case
  (i.e. only on an actual committed toggle), `await shareUpdateNotifier.NotifyEventChangedAsync(userUuid,
  eventUuid, cancellationToken);` before returning.
- `Services/Api/Expenses/ExpensesService.cs` — inject the same interface. In `SetSettledAsync`, after
  `expenseRepository.SetSettledAsync` returns `ExpenseWriteStatus.Success`, `await
  shareUpdateNotifier.NotifyExpenseChangedAsync(userUuid, expenseUuid, cancellationToken);`.
- `Services/Api/Shares/SharesService.cs` — inject the same interface. In `SetSettledAsync`, after
  `shareRepository.SetSettledAsync` returns `ExpenseWriteStatus.Success`, same call with the share's
  owning `expenseUuid` (already a parameter).
- None of the three call sites notify on a failed/no-op toggle (404/validation) — only on a real,
  committed state change, matching "perform post-commit side effects after the transaction returns".

### Step 5 — Wire link revoke/regenerate into the broadcaster

`Services/Api/Share/EventShareService.cs` — inject `IEventShareStreamBroadcaster streamBroadcaster`
(primary-constructor param; direct dependency, not through the notifier, since this service already has
the token in hand from `RevokeActiveByEventAsync`'s `(bool Revoked, string? Token)` result — no
event→link resolution needed):

- `RevokeAsync`: right after `if (revoked && token is not null) await shareLinkCache.RemoveAsync(token,
  ct);`, add `streamBroadcaster.PublishRevoked(token);` (OQ2a naming).
- `CreateAsync`'s `request.Regenerate` branch: right after `if (revoked && oldToken is not null) await
  shareLinkCache.RemoveAsync(oldToken, ct);`, add `streamBroadcaster.PublishRevoked(oldToken);` — verified
  against the live code that this branch performs the exact same "this token is now dead" transition as
  `RevokeAsync` and was not otherwise covered.

### Step 6 — The SSE endpoint

`Controllers/PublicSharesController.cs` — add two more primary-constructor params:
`IEventShareLinkCache shareLinkCache, IEventShareStreamBroadcaster streamBroadcaster` (alongside the
existing `IEventShareService shareService`; still `[AllowAnonymous]`, still never reads
`AuthenticatedUser`).

```csharp
[HttpGet("{token}/stream")]
[Produces("text/event-stream", "application/json")]
[SwaggerOperation(
    Summary = "Luồng cập nhật trực tiếp của báo cáo chia sẻ (Server-Sent Events)",
    Description = "Giữ kết nối mở và gửi sự kiện text/event-stream mỗi khi tổng quan đã trả/còn nợ của đợt được chia sẻ thay đổi (event: updated) - client tự gọi lại GET .../shares/{token} (và QR nếu đang hiển thị) khi nhận sự kiện; luồng không mang theo dữ liệu báo cáo. Khi liên kết bị chủ sổ thu hồi/tạo lại (event: revoked) hoặc tự hết hạn (event: expired), gửi sự kiện kết thúc rồi đóng kết nối. Có bình luận giữ-kết-nối định kỳ. Không cần token đăng nhập. Token không tồn tại/đã hết hạn/đã thu hồi ngay khi kết nối trả về 404 (chưa ghi byte nào).")]
[SwaggerResponse(StatusCodes.Status200OK, "Kết nối SSE thành công.")]
[SwaggerResponse(StatusCodes.Status404NotFound, "Liên kết chia sẻ không tồn tại hoặc đã hết hạn.", typeof(ApiResult))]
public async Task<IActionResult> StreamPublicAsync([FromRoute] string token, CancellationToken cancellationToken)
{
    // Validate BEFORE writing anything (same LookupAsync the plain GET uses). Thrown before any byte
    // is written, so ErrorHandlerFilter still wraps this into the normal 404 16000 JSON envelope -
    // verified against the File()-returning export/QR actions, which throw from the service the same
    // way before ever calling File(...).
    _ = await shareLinkCache.LookupAsync(token, cancellationToken)
        ?? throw new ErrorException(ErrorCodes.ShareLinkNotFoundOrExpired, MessageKeys.Error.ShareLinkNotFoundOrExpired);

    Response.ContentType = "text/event-stream";
    Response.Headers.CacheControl = "no-cache";
    Response.Headers["X-Accel-Buffering"] = "no"; // defense in depth; nginx also gets a dedicated location (Step 8)

    using var subscription = streamBroadcaster.Subscribe(token);
    await WriteFrameAsync("connected", cancellationToken); // OQ small, harmless "the pipe is live" ping

    var heartbeat = TimeSpan.FromSeconds(configuration.GetValue("Share:StreamHeartbeatSeconds", 20));
    using var timer = new PeriodicTimer(heartbeat);

    while (!cancellationToken.IsCancellationRequested)
    {
        var signalTask = subscription.Reader.ReadAsync(cancellationToken).AsTask();
        var tickTask = timer.WaitForNextTickAsync(cancellationToken).AsTask();
        if (await Task.WhenAny(signalTask, tickTask) == signalTask)
        {
            var signal = await signalTask;
            var name = signal.Type switch
            {
                EventShareStreamSignalType.Revoked => "revoked",
                EventShareStreamSignalType.Expired => "expired",
                _ => "updated"
            };
            await WriteFrameAsync(name, cancellationToken);
            if (signal.Type != EventShareStreamSignalType.Updated)
                break; // terminal
        }
        else
        {
            // Heartbeat tick doubles as the natural-expiry re-check (OQ1) - nobody has to actively
            // revoke for an aged-out link to close a still-open tab.
            if (await shareLinkCache.LookupAsync(token, cancellationToken) is null)
            {
                await WriteFrameAsync("expired", cancellationToken);
                break;
            }
            await Response.WriteAsync(": keep-alive\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    return new EmptyResult();

    async Task WriteFrameAsync(string eventName, CancellationToken ct)
    {
        await Response.WriteAsync($"event: {eventName}\ndata: {{}}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
```

Notes on why this is safe against the `[ResponseWrapped]` envelope: the action returns `EmptyResult`
(or the request is cancelled mid-loop, in which case `OperationCanceledException` propagates and is
re-thrown by `ErrorHandlerMiddleware` exactly like today — see its explicit `if (exception is
OperationCanceledException && context.RequestAborted.IsCancellationRequested) throw;` guard, already
present) — never an `ObjectResult`, so `ResponseWrappedAttribute.OnResultExecuting` is a no-op, same as
every `File(...)`-returning action already in this codebase.

### Step 7 — Config

`appsettings.json` — add `"StreamHeartbeatSeconds": 20` under the existing `"Share": { "LinkTtlHours": 24
}` block. No other config file needs it (no separate value for Production; the default is fine, and it's
overridable via `appsettings.Production.local.json` the same way `LinkTtlHours` is, if ever needed).

### Step 8 — nginx: unbuffered proxying for the stream path only

`deployment/config/nginx/conf.d/api.conf` — add a dedicated regex location **before** the catch-all
`location /`, matching only the stream path so every other route (including the plain
`.../shares/{token}` GET) is unaffected:

```nginx
    # Public share SSE stream: must not be buffered, and needs a read timeout well past the
    # heartbeat interval (planning/public-share-sse-updates.md). Deliberately sets NO
    # proxy_set_header here - a location that sets any proxy_set_header of its own stops
    # inheriting ALL of the server block's shared ones (Host/X-Real-IP/X-Forwarded-For/
    # X-Forwarded-Proto, set above) - leaving this block header-free keeps that inheritance intact.
    location ~ ^/api/v1/public/shares/[^/]+/stream$ {
        limit_req zone=api_general burst=20 nodelay;
        proxy_buffering off;
        proxy_read_timeout 3600s;
        proxy_pass http://api:8080;
    }
```

(Regex locations are matched in declaration order and take priority over a plain-prefix `location /`
per nginx's location-selection algorithm, so this narrows correctly without needing `^~` anywhere.)

### Step 9 — Documentation

Keep this doc's Progress Log / Final Outcome synchronized. No change needed to
`planning/event-share-link.md` or `planning/settled-per-member.md` (their locked decisions are not
reopened — this feature is additive). Note the frontend contract dependency explicitly (Impact
Analysis, below) for the follow-up `web-feature-planner` doc.

## Impact Analysis

- **APIs:**
  - NEW `GET api/v1/public/shares/{token}/stream` **[AllowAnonymous]** — `text/event-stream`, no
    `ApiResult<T>` payload (see Step 6 for why). Errors (404 16000) still go through the standard wrapped
    envelope, unchanged shape.
  - No change to the request/response shape of any existing endpoint (`.../shares/{token}`,
    `.../shares/{token}/qr/members`, `PUT {uuid}/settled`, `PUT {uuid}/shares/{shareUuid}/settled`, `PUT
    {uuid}/members/{memberUuid}/settled`, `POST/GET/DELETE {uuid}/share`) — all three settled-mutation
    endpoints gain an internal, invisible-to-the-caller post-commit side effect only.
- **Database:** none. No new entity, no migration, no schema change. The feature is entirely in-process
  and ephemeral (a revoked/expired token simply stops having live subscribers; nothing new is persisted).
- **Infrastructure:**
  - `deployment/config/nginx/conf.d/api.conf` — new regex `location` block (Step 8); no other location
    changed.
  - New in-process **Singleton** `EventShareStreamBroadcaster` — bounded per-subscriber memory (a handful
    of small channels; capacity 4 each), no external dependency, no persistence, dies with the process
    (acceptable: a page reload simply re-subscribes and gets a fresh `connected` frame).
  - No new NuGet package (`System.Threading.Channels` is part of the BCL since .NET Core; `PeriodicTimer`
    is BCL since .NET 6).
  - Redis load: one extra `LookupAsync` (already-existing method) per open stream per heartbeat tick
    (default 20s) — negligible at this feature's expected scale.
- **Services:**
  - NEW `Services/Api/Share/EventShareStreamBroadcaster.cs` (`IEventShareStreamBroadcaster`, Singleton).
  - NEW `Services/Api/Share/EventShareUpdateNotifier.cs` (`IEventShareUpdateNotifier`, Scoped).
  - `Repositories/ExpenseRepository.cs` / `IExpenseRepository`: +1 new READ method
    (`GetEventUuidAsync`); no existing method signature changed.
  - `Services/Api/Events/EventsService.cs`, `Services/Api/Expenses/ExpensesService.cs`,
    `Services/Api/Shares/SharesService.cs`: each gains one injected dependency
    (`IEventShareUpdateNotifier`) and one call after a successful settled toggle.
  - `Services/Api/Share/EventShareService.cs`: gains one injected dependency
    (`IEventShareStreamBroadcaster`) and one call in `RevokeAsync` + one in `CreateAsync`'s regenerate
    branch. `IEventShareLinkRepository` / `IEventShareLinkCache` — **zero signature changes**, only
    reused as-is (per the constraint in the task brief).
  - `Controllers/PublicSharesController.cs`: +2 injected dependencies, +1 action. `AppController`
    untouched (LOCKED).
- **Documentation:** this planning doc. **Explicit dependency for the follow-up frontend planner**: the
  web-feature-planner's doc must be written against this API's exact contract —
  `GET api/v1/public/shares/{token}/stream`, event names `connected` / `updated` / `revoked` / `expired`
  (pending OQ2), trivial `data: {}` bodies (never a payload), reconnect semantics are the client's own
  responsibility (native `EventSource` auto-reconnects on a dropped connection with no special server
  support required; a `revoked`/`expired` terminal frame is the server's signal to the client to **stop**
  reconnecting and show the "link no longer available" state instead of retrying).

## Decision Log

> Inherited, locked decisions from `event-share-link.md` (LIVE read, closed-events-only, token stored
> plain, Premium-gated only at creation) and `settled-per-member.md` (Layer A/B semantics, no audit, no
> tier gate, closed-event exception) are **not reopened** here.

1. **Lightweight "changed" signal, never the full payload, over SSE.** The stream carries only an event
   name + trivial `data: {}` body; the client re-fetches the existing plain-JSON endpoint(s) on every
   signal. **Reason:** the public report is explicitly a LIVE read recomputed on every call (locked
   Decision 3 of `event-share-link.md`) — pushing the payload inline would require either duplicating
   that recomputation+serialization logic in the broadcast path or caching a payload that would then
   immediately go stale relative to the "always live" guarantee it's supposed to preserve. A pure notify
   pipe keeps the SSE layer trivial and leaves exactly one source of truth for the report shape.
   **Alternative considered:** push the full `PublicEventShareResponse` per event — rejected (duplicate
   serialization path, consistency risk, no benefit since the client must call the plain GET anyway to
   get a byte-identical shape to what it renders on first load).
2. **Post-commit resolve via a new read (`GetEventUuidAsync`), not a changed write-method return type.**
   The three settled-mutation repositories' write methods keep their existing signatures; the notifier
   resolves an expense's owning event via a separate, new read method. **Reason:** rules.md: "Perform
   post-commit side-effects (cache invalidation, notifications) after the transaction delegate returns,
   not inside it" — this is exactly a post-commit side effect, and keeping it decoupled means zero
   existing callers/tests of `SetSettledAsync` need to change. **Alternative considered:** thread the
   event UUID through `ExpenseWriteResult<T>`/a richer status — rejected as a wider, unnecessary blast
   radius for a value only the new notifier needs.
3. **Notifier is best-effort and never throws.** A failure to resolve the active link, reach Redis, or
   publish to the broadcaster is logged at Warning and swallowed — it must never turn an already-committed
   settled toggle into a failed HTTP request. **Reason:** the write already succeeded; the notify step is
   pure UX enhancement for anonymous viewers, not a correctness requirement.
4. **`EventShareService.CreateAsync`'s `Regenerate` branch also publishes a terminal signal on the OLD
   token**, not just `RevokeAsync`. **Reason:** verified against the live code that both call sites
   perform the identical "revoke the active link" transition; the brief only named `RevokeAsync`
   explicitly, but leaving the regenerate path unwired would leave an open stream on the just-invalidated
   old token hanging until its next heartbeat's natural-expiry re-check (still correct, just slower and
   inconsistent with the immediate teardown `RevokeAsync` gets).
5. **Bounded (capacity 4), drop-oldest channel per subscriber.** **Reason:** signals are idempotent/
   coalescible (multiple "updated" pings collapse into "re-fetch once"), so a slow/stalled reader must
   never be able to block a mutation-service request thread trying to publish, and must never grow
   unbounded. **Alternative considered:** unbounded channel — rejected (unbounded memory growth is the
   one thing a notify pipe must never risk).
6. **The new nginx location sets no `proxy_set_header` of its own.** **Reason:** verified nginx's
   inheritance rule (a location that sets any `proxy_set_header` stops inheriting all of the parent's) —
   leaving the block header-free is both the smallest diff and the only way to avoid silently breaking
   `Host`/`X-Real-IP`/`X-Forwarded-For`/`X-Forwarded-Proto` for this one route.
7. **No new ErrorCodes block.** The stream endpoint reuses `ShareLinkNotFoundOrExpired` (16000) verbatim
   for an invalid token on connect. **Reason:** the failure mode is identical to the plain GET's; no new
   distinct error state exists.

## Progress Log

### 2026-08-26

- Created this planning doc. Read `The-ideal.md` §3.5/§3.6/§3.7/§3.10/§4.4/§6, `CLAUDE.md`, `AGENTS.md`,
  `.agents/rules/rules.md`, `.claude/rules/rule.md`, and the two prerequisite shipped-feature docs
  (`planning/event-share-link.md`, `planning/settled-per-member.md`) in full.
- Verified against the live code: `PublicSharesController`, `EventShareService`,
  `IEventShareLinkRepository`/`EventShareLinkRepository`, `IEventShareLinkCache`/`EventShareLinkCache`,
  `EventsService.SetMemberSettledAsync`, `ExpensesService.SetSettledAsync`,
  `SharesService.SetSettledAsync`, `ExpenseRepository.SetSettledAsync`, `ShareRepository.SetSettledAsync`
  (confirmed `expense.EventId` is a plain scalar FK, no `Include` needed; confirmed neither write method's
  return type exposes it to the service layer today), `AppController` (LOCKED, untouched),
  `ResponseWrappedAttribute` + `ErrorHandlerFilter` + `ErrorHandlerMiddleware` (confirmed the
  `File(...)`-returning export/QR actions are the live precedent for bypassing the `ApiResult` envelope,
  and that an `ErrorException` thrown before any byte is written is still caught and wrapped normally),
  `ErrorCodes.cs` (confirmed 16000/16001 claimed, 17xxx/18xxx reserved/claimed by other in-flight
  features, 19xxx next free — not needed here), `appsettings.json` (`Share:LinkTtlHours` precedent),
  `Program.cs` (confirmed `IConnectionMultiplexer` is already a Singleton; confirmed no response
  compression middleware to worry about), `deployment/docker-compose.yml` (confirmed single `fsm-api`
  instance), `deployment/config/nginx/conf.d/api.conf` + `nginx.conf` (confirmed no `proxy_buffering`
  override anywhere, default 60s `proxy_read_timeout`, and the shared-`proxy_set_header`-inheritance
  comment/gotcha), `planning/cors-configuration.md` (confirmed a single global CORS policy already covers
  every controller — no CORS change needed for the new anonymous route), and
  `FairShareMonApi.Tests/PublicShareEndpointTests.cs` (confirmed the existing test harness has no
  precedent yet for a held-open streaming response — flagged in Tests).
- Found and corrected one gap in the task brief while verifying: `EventShareService.CreateAsync`'s
  `Regenerate` branch revokes the old link exactly like `RevokeAsync` does, but the brief only mentioned
  wiring `RevokeAsync` into the broadcaster — added the same call there (Decision 4).
- Drafted the full Implementation Plan, Impact Analysis, and Decision Log. Raised 3 Open Questions
  (heartbeat interval; revoked-vs-expired terminal event naming; one unified stream vs a second QR-only
  signal) with a recommendation for each. Awaiting the checkpoint before implementation starts.
- Orchestrator resolved all 3 Open Questions per the feature-planner's recommendations (20s heartbeat,
  distinct `revoked`/`expired` event names, one unified stream) - see the RESOLVED note above.
- Implemented Steps 1-8 exactly as written in the Implementation Plan:
  - Step 1: `Services/Api/Share/EventShareStreamBroadcaster.cs` (`EventShareStreamSignalType`,
    `EventShareStreamSignal`, `IEventShareStreamSubscription`, `IEventShareStreamBroadcaster`,
    `[SingletonService]` `EventShareStreamBroadcaster` - bounded capacity-4 drop-oldest channel per
    subscriber, keyed by token, terminal signals `TryComplete()` the writer).
  - Step 2: `Repositories/ExpenseRepository.cs` - added `IExpenseRepository.GetEventUuidAsync` (new read,
    no existing signature touched).
  - Step 3: `Services/Api/Share/EventShareUpdateNotifier.cs` (`IEventShareUpdateNotifier`,
    `[ScopedService]` `EventShareUpdateNotifier` - both methods wrapped in try/catch, log Warning, never
    rethrow).
  - Step 4: wired `IEventShareUpdateNotifier` into `EventsService.SetMemberSettledAsync`,
    `ExpensesService.SetSettledAsync`, `SharesService.SetSettledAsync` - notify only fires on the
    `Success` branch of each.
  - Step 5: wired `IEventShareStreamBroadcaster` into `EventShareService` - `RevokeAsync` and the
    `Regenerate` branch of `CreateAsync` both call `PublishRevoked` on the token they just invalidated.
  - Step 6: `Controllers/PublicSharesController.cs` - added `StreamPublicAsync` (`GET
    {token}/stream`), matching the plan's sketch verbatim (pre-write 404 validation, `connected` frame,
    `PeriodicTimer` heartbeat racing the subscription reader via `Task.WhenAny`, natural-expiry re-check
    on each heartbeat tick, `EmptyResult` return so `[ResponseWrapped]` is a no-op).
  - Step 7: `appsettings.json` - added `Share:StreamHeartbeatSeconds: 20`.
  - Step 8: `deployment/config/nginx/conf.d/api.conf` - added the regex `location` for
    `^/api/v1/public/shares/[^/]+/stream$` before the catch-all `location /`, with no `proxy_set_header`
    of its own (preserves inheritance per Decision 6).
  - Fixed a compile break the new `IExpenseRepository.GetEventUuidAsync` member caused in two existing
    test fakes (`ExpensesServiceTests.FakeExpenseRepository`, `TierServiceTests.FakeExpenseCounter`), and
    wired a new no-op `IEventShareUpdateNotifier`/real `EventShareStreamBroadcaster` into the
    `EventsServiceTests`/`ExpensesServiceTests`/`SharesServiceTests`/`EventShareServiceTests` constructor
    calls that now require the extra parameter - required just to keep the existing suite compiling and
    green; the test-engineer still owns writing the new assertions listed under Tests.
  - `dotnet build FairShareMonApi.sln` - Build succeeded (0 errors; only pre-existing NU1903/CS8619
    warnings unrelated to this feature). `dotnet test FairShareMonApi.sln` - 1507 passed, 7 skipped
    (Redis unreachable in this environment, pre-existing), 0 failed.
- No EF migration needed (confirmed - no new persisted state, matches the Requirements/Impact Analysis).
- Deliberately left the SSE integration tests, broadcaster/notifier unit tests, and the extended
  Events/Expenses/Shares/EventShare service tests to the test-engineer per the doc's own Tests section
  and this agent's remit (writing NEW tests is the test-engineer's job).

#### Test results (test-engineer, 2026-08-26)

- Added the full test list from the **Tests** section (39 new tests) across 5 new/extended files plus 4
  extended existing files; the whole suite is **1546 passed, 0 failed, 7 skipped** (up from the 1507/7
  baseline the api-implementer left it at). The 7 skips are the same pre-existing Redis-unreachable-in-
  this-environment skips called out in the api-implementer's Progress Log entry (Admin/EventShareLinkCache/
  TokenWhitelistStore cache-first tests) - unrelated to this feature, and every new integration test in
  `PublicShareStreamEndpointTests.cs` (including the two that talk to Redis directly to bust the cache)
  RAN, not skipped. No production code changed; **no product bug found**.
- **`EventShareStreamBroadcasterTests.cs`** (new, 8, pure unit - no DB/Redis). Proves: subscribe+publish
  delivery (exactly one `Updated` signal); two subscribers on the same token both receive a publish
  (fan-out); a publish on a different token never reaches an unrelated subscriber (isolation);
  `PublishRevoked`/`PublishExpired` deliver the right signal type AND complete the channel afterward (a
  further `ReadAsync` throws `ChannelClosedException` instead of hanging); publishing 50 `Updated` signals
  into the capacity-4 drop-oldest channel without ever draining never throws, and at most 4 survive: a
  disposed subscription is a silent no-op for a later publish (no exception, no delivery to the disposed
  reader), while a second subscriber on the same token is unaffected by the first one's disposal.
- **`EventShareUpdateNotifierTests.cs`** (new, 7, pure unit - fakes for `IEventShareLinkRepository`/
  `IExpenseRepository`/`IEventShareStreamBroadcaster`, `CapturingLogger<T>` for the log assertion). Proves:
  `NotifyEventChangedAsync` publishes exactly once with the active link's token when one exists, never
  publishes when none exists, and swallows-and-logs (Warning) a repository exception without rethrowing;
  `NotifyExpenseChangedAsync` resolves the owning event then delegates correctly, short-circuits for a
  loose expense WITHOUT ever calling `GetActiveByEventAsync`, stays silent for an event with no active
  link (while proving the lookup DID happen), and likewise swallows-and-logs a resolver exception.
- **`EventsServiceTests.cs`** (+3, extended). `SetMemberSettledAsync`: a committed toggle calls
  `IEventShareUpdateNotifier.NotifyEventChangedAsync` exactly once with the event UUID passed straight
  through; an event-miss (9000) or non-participant-member (3000) failure never calls it.
- **`ExpensesServiceTests.cs`** (+2, extended). `SetSettledAsync`: a committed toggle calls
  `NotifyExpenseChangedAsync` exactly once with the expense UUID; an expense-miss (6000) never calls it.
- **`SharesServiceTests.cs`** (+3, extended). `SetSettledAsync`: a committed toggle calls
  `NotifyExpenseChangedAsync` exactly once with the share's OWNING expense UUID (not the share UUID); an
  expense-miss or share-miss failure never calls it.
- **`EventShareServiceTests.cs`** (+3, extended - added a `RecordingStreamBroadcaster` fake that wraps a
  REAL `EventShareStreamBroadcaster` so `Subscribe`/fan-out semantics stay production-accurate while also
  counting `PublishRevoked` calls). `RevokeAsync` on an existing active link publishes `Revoked` with that
  exact token; the idempotent no-active-link no-op never publishes. `CreateAsync` with `Regenerate = true`
  publishes `Revoked` on the OLD token before returning the new one.
- **`PublicShareStreamEndpointTests.cs`** (new, 13, integration - real MariaDB+Redis, mirrors
  `PublicShareEndpointTests.cs`'s seeding helpers) plus new **`Infrastructure/SseTestClient.cs`** (the
  planning doc's flagged new technique: `HttpCompletionOption.ResponseHeadersRead` + incremental
  `StreamReader` parsing of the SSE wire format, every read bounded by an explicit timeout via a
  `CancellationTokenSource`, so a regression hangs the test, never the run). Proves: unknown/expired/
  revoked token on connect still 404s as normal wrapped JSON (never a half-opened stream) - the expired
  case forces `ExpiresAt` into the past directly in the DB AND busts the Redis cache key
  (`EventShareLinkCache.CacheKey`) since a live cached entry from creation would otherwise mask the DB-side
  expiry; a valid token gets `Content-Type: text/event-stream` and an `event: connected` first frame; each
  of the three settled-mutation routes (`PUT events/{uuid}/members/{memberUuid}/settled`,
  `PUT expenses/{uuid}/settled`, `PUT expenses/{uuid}/shares/{shareUuid}/settled`) pushes `event: updated`
  to an open stream on the SAME event; the same member-settled mutation on a second closed event with NO
  active link leaves the first event's stream silent (negative assertion, bounded); an owner revoke pushes
  `event: revoked` and the connection then hits EOF (no further frame); an owner regenerate pushes
  `event: revoked` on the OLD token while a fresh connection on the NEW token still works; two concurrent
  subscribers on the same token both receive the same `updated` signal (fan-out proven at the HTTP level).
  The heartbeat/expiry pair overrides `Share:StreamHeartbeatSeconds` to 1 via
  `Factory.WithWebHostBuilder(...).ConfigureAppConfiguration(...)` (with dedicated per-factory
  `CreatePremiumClientAsync`/tier-setting helpers so the seeding client and the SSE client share the SAME
  app instance/singleton broadcaster as the override): an idle stream emits a `: keep-alive` comment within
  the bound, and a link forced-expired mid-stream (DB + Redis, as above) gets `event: expired` from the
  heartbeat's own re-check within one tick, independent of any explicit owner action.
- No coverage gaps against the planning doc's Tests checklist - every listed bullet has a corresponding
  test. Extras added beyond the checklist: `Dispose_OneOfTwoSubscribers_TheOtherStillReceivesPublishes`
  (proves disposal doesn't corrupt a shared token's other subscribers) and the explicit
  `StreamPublic_ExpiredToken_Returns404Json` case (the doc's checklist bullet says "unknown/expired/
  revoked" together; expiry gets its own dedicated test since it's the only one of the three needing the
  DB+Redis force-expire helper).

#### Code review + fix (orchestrator, 2026-08-26)

- **Blocking finding**: `code-reviewer` found that `StreamPublicAsync`'s loop recreated BOTH
  `subscription.Reader.ReadAsync(...)` and `timer.WaitForNextTickAsync(...)` on every iteration, regardless
  of which one had won the previous race. `PeriodicTimer.WaitForNextTickAsync()` throws
  `InvalidOperationException` if called again while a prior call on the same timer is still pending -
  reviewer reproduced this against a real `Channel`+`PeriodicTimer` pair. Practical effect: the very first
  time an `updated` signal arrived before a heartbeat tick (the ordinary case - a settled toggle happens
  far faster than the 20s heartbeat), the loop's *next* iteration crashed, aborting the connection - the
  stream never survived past its first real update. A second, related failure mode: an abandoned
  `ReadAsync` from an iteration the heartbeat won became a "zombie" that could steal a later signal out from
  under the live await, silently dropping or delaying delivery. Root cause of the gap: every existing
  integration test read exactly one frame after `connected` then disposed the stream, so the crash - which
  only surfaces on a second iteration - never showed up despite a fully green suite.
- **Fix** (`Controllers/PublicSharesController.cs`, `StreamPublicAsync`): hoisted `signalTask`/`tickTask` out
  of the loop; only the task that actually completed gets replaced with a fresh one for the next iteration,
  the other stays pending and is awaited again next time round.
- **Added regression test**: `PublicShareStreamEndpointTests.StreamPublic_TwoSequentialMutations_
  BothUpdatedFramesArriveInOrderOnTheSameStream` - two settled-toggle mutations issued back-to-back on the
  same open connection, asserting both `updated` frames arrive in order. This is exactly the shape of test
  that would have caught the bug (a single-frame-then-dispose test cannot, by construction, reach the loop's
  second iteration).
- Re-ran `dotnet build` (0 errors) and the full suite: **1547 passed, 0 failed, 7 skipped** (same
  pre-existing Redis-unreachable skips as before; the new regression test ran and passed, along with the
  rest of `PublicShareStreamEndpointTests` - now 14 tests in that file).
- Everything else the reviewer checked (pre-write 404 validation ordering, subscription cleanup on
  disconnect, notifier best-effort/post-commit-only contract, bounded drop-oldest channel never losing a
  terminal signal, DI lifetimes, the nginx location's regex/ordering/header-inheritance, resource-owned
  scoping, test quality) came back clean - no other findings.

## Final Outcome

Implemented as planned, with one blocking bug found by code review and fixed before closing out (see
"Code review + fix" above): the SSE loop recreated its `PeriodicTimer`/channel-read tasks every iteration,
which crashed the stream on its first real update. Fixed by hoisting both tasks out of the loop and only
replacing whichever one completed; a regression test (two sequential mutations on one open stream) now
guards this.

New: `EventShareStreamBroadcaster` (Singleton in-process broadcaster), `EventShareUpdateNotifier` (Scoped
best-effort notify seam), one new SSE endpoint (`GET api/v1/public/shares/{token}/stream`), one new
repository read (`ExpenseRepository.GetEventUuidAsync`). Changed: `EventsService`, `ExpensesService`,
`SharesService` (each +1 injected dependency, +1 post-commit notify call), `EventShareService` (+1 injected
dependency, +1 call in `RevokeAsync`, +1 in `CreateAsync`'s regenerate branch), `PublicSharesController` (+2
injected dependencies, +1 action, +1 post-review loop fix), `deployment/config/nginx/conf.d/api.conf` (+1
location block), `appsettings.json` (+1 config value). No existing method signature changed; no EF
migration; build green; full test suite green (**1547 passed / 7 pre-existing Redis-unreachable skips / 0
failed** - the test-engineer's +39 tests plus the orchestrator's +1 post-review regression test, no
remaining product bug, no test-only gaps left open).

## Future Improvements

- **Multi-instance broadcast.** If the API is ever scaled to N replicas behind nginx, the in-process
  `Channel<T>` broadcaster stops working across instances (a subscriber connected to instance A never
  sees a publish that happened on instance B). At that point, swap `EventShareStreamBroadcaster`'s
  internals for Redis Pub/Sub (`IConnectionMultiplexer.GetSubscriber()`, already wired) keyed by the same
  `share:event:{token}`-style channel name, keeping the public `IEventShareStreamBroadcaster` interface
  unchanged so no caller needs to change.
- **Per-token/per-IP concurrent-SSE-connection caps** beyond the existing blanket `limit_conn perip 20;`,
  if the anonymous stream endpoint sees abuse (mirrors the abuse-controls item already deferred in
  `event-share-link.md`).
- **Reduce the natural-expiry detection window** below one heartbeat interval (e.g. a lightweight
  background sweep that proactively calls `PublishExpired` for links crossing `ExpiresAt`, instead of
  relying on each open connection's own next heartbeat tick to notice).
- **Structured signal payload** (e.g. a monotonic version counter or the specific member/expense UUID
  that changed) if a future UI wants to animate/highlight exactly what changed instead of a full
  re-fetch-and-diff.

## Tests (for the test-engineer)

Reuse the shipped harness (`[Collection("AuthIntegration")]`, `[SkippableFact]` against real
MariaDB+Redis, `WebApplicationFactory<Program>`). **Flag: the SSE endpoint tests need a genuinely new
technique this suite hasn't used before** — every existing endpoint test does one request/one response.
Recommend a small `Infrastructure/SseTestClient.cs` (or inline helper) that: sends the stream request via
`httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)` (so headers
return without waiting for the body to complete), reads `response.Content.ReadAsStreamAsync()`
incrementally via a `StreamReader`, and parses lines into `(eventName, data)` frames (blank line =
dispatch, `:`-prefixed = comment/heartbeat, per the SSE wire format). **Every** read in these tests must be
wrapped in an explicit timeout (e.g. `.WaitAsync(TimeSpan.FromSeconds(5))`) so a regression that stops
publishing hangs the *test*, not the whole CI run. Override `Share:StreamHeartbeatSeconds` to a small value
(e.g. `1`) via `WebApplicationFactory.WithWebHostBuilder(...).ConfigureAppConfiguration(...)` for the
heartbeat-specific tests so they don't need to wait 20 real seconds.

**Unit — `EventShareStreamBroadcasterTests` (pure in-memory, no DB/Redis):**
- Subscribe then `PublishUpdated(token)` → the subscriber's reader receives exactly one `Updated` signal.
- Two subscribers on the same token both receive the same publish (fan-out); a publish on a different
  token never reaches either (isolation).
- `PublishRevoked`/`PublishExpired(token)` → the subscriber receives the correct signal type AND the
  channel completes afterward (no further `ReadAsync` hangs).
- Publishing far more `Updated` signals than the bounded capacity without draining → no exception
  (`TryWrite` degrades to a drop under `DropOldest`, proving a publisher is never blocked).
- Disposing a subscription removes it; a later publish for that token is a silent no-op (no exception,
  no delivery to the disposed reader).

**Unit — `EventShareUpdateNotifierTests` (fakes for `IEventShareLinkRepository` / `IExpenseRepository` /
`IEventShareStreamBroadcaster`):**
- `NotifyEventChangedAsync`: an active link exists → `PublishUpdated` called once with that link's token;
  no active link → `PublishUpdated` never called; the repository lookup throwing → swallowed (logged),
  never rethrown.
- `NotifyExpenseChangedAsync`: expense in an event with an active link → resolves then delegates
  correctly; a loose expense (`GetEventUuidAsync` → null) → no-op, `GetActiveByEventAsync` never even
  called; an event with no active link → `PublishUpdated` never called.

**Unit — extend `EventsServiceTests` / `ExpensesServiceTests` / `SharesServiceTests` (fakes):**
- A successful settled toggle calls the injected `IEventShareUpdateNotifier` fake exactly once with the
  right arguments (event path passes `eventUuid` straight through; expense/share paths pass
  `expenseUuid`).
- A failed toggle (404 / validation) never calls the notifier.

**Unit — extend `EventShareServiceTests`:**
- `RevokeAsync` on an existing active link calls `streamBroadcaster.PublishRevoked` with that token;
  idempotent no-op (no active link) never calls it.
- `CreateAsync` with `Regenerate = true` while an active link exists calls `PublishRevoked` with the OLD
  token before returning the new one (in addition to the already-tested cache-eviction behavior).

**Integration — new `PublicShareStreamEndpointTests.cs` (real MariaDB+Redis, mirrors
`PublicShareEndpointTests.cs`'s seeding helpers):**
- Unknown / expired / revoked token → connecting returns 404 `ShareLinkNotFoundOrExpired` (16000) as the
  normal wrapped JSON envelope (not a stream) — proves the pre-stream validation ordering.
- A valid token → response headers show `Content-Type: text/event-stream`; the first frame received is
  `event: connected`.
- Mark a member/expense/share settled (authenticated) on the SAME event behind an open stream → the open
  connection receives `event: updated` within a bounded wait, for **each** of the three mutation routes
  (`PUT .../members/{memberUuid}/settled`, `PUT {uuid}/settled`, `PUT {uuid}/shares/{shareUuid}/settled`).
- The same mutation on an event **without** an active share link → no signal arrives within a short bound
  (negative assertion — proves no false positives / no crash when nobody is listening).
- Owner revokes the link (`DELETE {uuid}/share`) while a client is streaming → the stream receives
  `event: revoked` and the connection ends (no further bytes).
- Owner regenerates (`POST {uuid}/share` with `regenerate: true`) while a client streams the OLD token →
  that stream receives `event: revoked`; a fresh connection on the NEW token still works.
- With `Share:StreamHeartbeatSeconds` overridden small, an idle stream (no mutation) emits at least one
  `: keep-alive` comment line within the bound.
- With the heartbeat overridden small and a link's `ExpiresAt` forced into the past directly in the test
  DB, an idle open stream (no explicit revoke) receives `event: expired` on its own within one heartbeat
  tick — proves the natural-expiry re-check path independent of any explicit owner action.
- Two concurrent subscribers on the same token both receive the same `updated` signal (fan-out proven at
  the HTTP level, not just the broadcaster unit level).
