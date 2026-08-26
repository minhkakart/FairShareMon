# Bank Callback → Automatic Settlement Trigger

## Objective

Add an abstract, provider-pluggable layer that receives **inbound bank-transaction webhooks** from a
bank-transaction aggregator (first provider: **SePay**) and automatically fires the **already-shipped**
settlement cascade (`ShareRepository.SetSettledAsync` / `ExpenseRepository.SetSettledAsync` /
`EventMemberSettlementRepository.SetMemberSettledAsync`, wired through `SettlementReconciler` /
`EventSettlementClassifier` / `EventSettlementCreditApplier` per `planning/event-expense-settlement-sync.md`,
Milestones 1 and 2, both shipped) instead of requiring the owner to notice a bank transfer and click the
manual "đã trả" toggle themselves.

**This feature invents no new settlement math.** It is a new *trigger* in front of the exact same write
paths the owner's manual toggle already drives — including their existing Direction 1/Direction 2
side-effects (cascade to shares, partial credit to the event balance). Nothing about `advanced`/`owed`/
`balance`, `ClearedAmount`, or the eligibility/cascade rules changes.

The mechanism:
1. At QR-generation time, embed a short, unique **correlation code** into the transfer memo (VietQR
   `addInfo`) for every billed member, and persist a mapping `code → (user, event?, member, expense?,
   expected amount)`.
2. When SePay (or a future provider) posts a webhook for an incoming bank transaction, verify the
   provider's signature/API key, normalize the payload, extract the correlation code from the transfer
   content, resolve the target, and — on a confident (exact-amount) match — call the **existing** settled
   toggle for that target, exactly as if the owner had clicked it.
3. Every inbound transaction is recorded (idempotency dedup + audit) so retries never double-apply and
   unmatched/mismatched transactions are visible to the owner instead of silently dropped.

## Background

Confirmed against the live code (2026-08-26):

- **The settlement cascade is fully shipped**, both milestones, per `planning/event-expense-settlement-sync.md`
  (Progress Log confirms Milestone 1 and the repo's live `Repositories/EventSettlementCreditApplier.cs` /
  `Repositories/EventSettlementClassifier.cs` confirm Milestone 2 also shipped). The three write entry
  points this feature must trigger are the **application-service** methods the existing controllers call
  (not the repositories directly — respects `Controller → Service → Repository`):
  - `ISharesService.SetSettledAsync(userUuid, expenseUuid, shareUuid, SetSettledRequest{ IsSettled }, ct)`
    (`Services/Api/Shares/SharesService.cs:100`) → `ShareRepository.SetSettledAsync`, which already fires
    Direction 2's credit step as a side effect (`Repositories/ShareRepository.cs:165-207`).
  - `IEventsService.SetMemberSettledAsync(userUuid, eventUuid, memberUuid, SetSettledRequest{ IsSettled }, ct)`
    (`Services/Api/Events/EventsService.cs:100`) → `EventMemberSettlementRepository.SetMemberSettledAsync`,
    which already fires Direction 1's cascade (`Repositories/EventMemberSettlementRepository.cs:76-152`).
  - (`IExpensesService.SetSettledAsync` exists for the whole-expense toggle but is **not** a target of this
    feature — SePay/bank transfers pay one member's share or one member's event balance, never "this whole
    multi-member expense in one transfer"; see Assumptions.)
  - Both target services are **plain DI services** already composed cross-service elsewhere in this exact
    way — `WalletQrService` already injects `IExpensesService`/`IStatsService` directly
    (`Services/Api/Wallet/WalletQrService.cs:62-70`), confirming "a service calls a sibling service" is an
    established, safe pattern here, not a new layering exception.
- **`SetSettledRequest`** (`Models/Expenses/SetSettledRequest.cs`) is the one shared `{ bool IsSettled }`
  DTO all three toggle routes already use — reused as-is, no new request shape needed for the internal call.
- **QR generation is the embed point.** `Services/Api/Wallet/WalletQrService.cs`: `GenerateExpenseQrAsync`/
  `GenerateEventQrAsync` (composite PNG, Premium-gated via `tierService.EnsurePremiumFeature(MessageKeys.
  Feature.Qr)`) and `GenerateExpenseMemberQrsAsync`/`GenerateEventMemberQrsAsync` (per-member data-URL QRs,
  also Premium-gated) all funnel through two **static, pure** helpers — `CollectExpenseBillables` (per
  billable share: unsettled, `Amount > 0`, non-payer) and `CollectEventBillables` (per member with
  `Outstanding > 0`, the settlement-sync-aware overlay) — which build a `BilledMember { MemberUuid,
  MemberName, Amount, Description }` per still-owing member. `Description` is today's memo/`addInfo`
  content: `"{expense.Name} - {member.Name}"` or `"{event.Name} - {member.Name}"` — **this is the exact
  field `wallet-and-qr.md` OQ9 fixed and this feature now reopens/extends** (adding the correlation code as
  a prefix, not changing the human-readable suffix's intent). A fifth method,
  `GenerateEventMemberQrsForShareAsync`, reuses the same `CollectEventBillables` helper for the
  **anonymous, non-Premium-gated** public share-link QR (`planning/event-share-link.md`) — see Open
  Question OQ3 for whether correlation codes are embedded there too.
- **The memo has a hard 25-character budget after folding.** `Services/Api/Wallet/VietQrPayloadBuilder.cs`
  `FoldMemo`: ASCII-folds (strips diacritics, maps đ/Đ), collapses whitespace, then **right-truncates** to
  `MemoMaxLength = 25`. A correlation code prepended to the memo (`"{code} {description}"`) survives this
  truncation intact as long as `code.Length + 1 <= 25` — the description suffix absorbs any truncation
  instead. This requires no change to `FoldMemo` itself, only to what content is handed to it.
- **The provider-abstraction precedent to mirror** is `planning/bank-directory-provider.md`'s
  `IQrContentProvider`/`QrContentProviderResolver` (`Services/Api/Banks/QrContentProviderResolver.cs`):
  `Multiple = true` DI registration, a `Key`-based resolver, always-fallback-safe design, and **standard
  .NET** `AddHttpClient<T>()`/`Configure<TOptions>(...)` wiring in `Program.cs` — **not** the DiDecoration
  `[HttpClientService]`/`[Option]` scanners (confirmed still unused in the live `Program.cs`). This
  feature's resolver differs in one respect: it is keyed by a **route segment** (`{provider}`), not a
  config value, and — unlike `QrContentProviderResolver`, which always falls back to `"local"` — has **no
  sensible default provider** to fall back to, so an unknown `{provider}` is a real failure (404), not a
  silent substitution.
- **The anonymous-endpoint precedent** is `Controllers/PublicSharesController.cs`: `[AllowAnonymous]` +
  `[Route("api/v{version:apiVersion}/...")]` override, deriving `AppController` (still gets the versioned
  route + `[ResponseWrapped]` envelope), never reading `AuthenticatedUser`. `EventShareLinkRepository.
  GetByTokenAsync` (`Repositories/EventShareLinkRepository.cs:125-129`) is the precedent for an
  **anonymous, non-user-scoped lookup by an opaque value** — the exact shape `QrCorrelationCodeRepository`'s
  code lookup needs (find by `Code` alone, no `userUuid` filter, because the caller — the webhook — has no
  authenticated user).
- **Audit today covers only `Expense`/`Share` diffs, by a human actor.** `Database/Entities/AuditLog.cs`:
  `AuditEntityType { Expense = 0, Share = 1 }`, `ActorUserId` (a real authenticated user, FK to `users`),
  `BeforeData`/`AfterData` (full entity snapshots). This shape does not fit a webhook-triggered boolean
  toggle with no human actor — see Decision Log entry 3 for why this feature adds a **separate** table
  instead of extending `AuditLog`/`AuditEntityType`.
- **Error-code block.** `Constants/ErrorCodes.cs`: blocks `1xxx`-`17xxx` are all claimed (17xxx by
  `event-expense-settlement-sync.md`, reserved-only, no codes defined). **18xxx is the next free block.**
- **Auth pipeline.** `Program.cs:172-178`: `FallbackPolicy = RequireAuthenticatedUser()` — every controller
  action requires the opaque-token auth **unless** `[AllowAnonymous]`. This feature's webhook receiver is
  the first endpoint whose "authentication" is not the app's own opaque-token scheme at all, but a
  third-party's own credential (an API key SePay attaches to the request) — verified entirely inside the
  action/service, not via the ASP.NET auth pipeline.
- **DiDecoration `Multiple = true`** (`Services/Api/Banks/LocalQrContentProvider.cs`/
  `VietQrRemoteQrContentProvider.cs`) is the pattern for registering more than one implementation of
  `IBankCallbackParser` (SePay today, a future provider later) without one `TryAdd`-ing out the other.
- The dev DB holds no real product data beyond disposable smoke rows; no real SePay account exists yet in
  this environment.

## Requirements

- **Correlation-code embedding (Decision 1, locked).** At QR-generation time, every still-owing member's
  transfer memo carries a short, unique, memo-safe correlation code that survives `FoldMemo`'s ASCII-fold +
  25-char truncation. A new stored mapping `code → (user, event?, member, expense?, expected amount)` is
  created at that time.
- **Confident-match auto-apply, no confirmation step (Decision 2, locked).** A transaction whose
  correlation code resolves unambiguously to a target fires the existing settled toggle immediately. An
  unmatched/ambiguous/failed-verification transaction is **held back**: not applied, logged, and visible to
  the owner (concrete design below, since the orchestrator's brief leaves "visible somewhere" to this doc).
- **First provider = SePay (Decision 3, locked).** A `POST /api/v1/bank-callbacks/{provider}` route,
  provider-abstracted (`IBankCallbackParser` + resolver), so a second aggregator can be added later without
  touching the matching/applying logic.
- **Bank-triggered settlement writes ARE audited; manual toggles stay unaudited (Decision 4, locked).** A
  new, dedicated record of every inbound transaction — matched or not — captures the raw callback and which
  settlement row (if any) it changed. This is scoped **only** to the bank-callback trigger path; the manual
  toggle routes (`PUT .../settled`) are untouched and remain audit-free per the locked `OQ-G`/`OQ10`
  decision in `event-expense-settlement-sync.md`/`settled-per-member.md`.
- **Idempotency is mandatory.** A webhook retried/duplicated by the provider must not double-apply. Dedup
  key: `(provider_key, provider_transaction_id)`, unique-indexed.
- **No new settlement math.** The applier never computes a partial credit or writes `ClearedAmount`
  directly — it only calls the existing `ISharesService.SetSettledAsync`/`IEventsService.
  SetMemberSettledAsync` methods, letting Direction 1/2's already-shipped side effects run exactly as they
  do for a manual click.
- **New authorization surface, not the opaque-token scheme.** The webhook receiver is `[AllowAnonymous]`
  with its own per-provider verification (API key/signature) done inside the action, never via the app's
  fallback authorization policy.
- Vietnamese Swagger/user-facing strings for every new endpoint and error code, per repo convention (a
  webhook has no human UI, but the owner-facing review-list endpoint does).
- EF migration(s) for the new tables; DB CHECK constraints on money/non-negative fields per §4.3.

## Open Questions

> The four items in the hand-off brief (matching strategy, auto-apply, first provider, audit) are **locked
> Decision Log entries below, not reopened.** Every item here is a genuinely undecided sub-point this doc
> cannot resolve with a single defensible answer — each has real trade-offs and, per several, real security
> implications (Human Confirmation Policy trigger 6). Options + a recommendation are given for each; the
> orchestrator brings these to the user.
>
> **RESOLVED 2026-08-26 — the user accepted the recommended option (a) for ALL ten items below** ("implement
> it, use all recommended"). Nothing here is silently defaulted: each option (a) was presented with its
> trade-offs before acceptance. The Implementation Plan already assumed these options throughout, so no
> further doc changes are needed before implementation starts.

**OQ1 — Correlation-code format: exact alphabet + length.** → **RESOLVED (a): `FSM` + 6 chars from
`A-Z2-9` minus visually-ambiguous characters.**
The code must (a) survive `FoldMemo`'s 25-char budget alongside a human-readable suffix, (b) be
unambiguous when hand-typed/read (some bank apps show the memo back to the payer), (c) have a
collision-safe generation strategy.
- **(a) [recommended]** Prefix `FSM` + 6 random characters from a 30-symbol alphabet (`A-Z2-9` minus
  `O`/`0`/`I`/`1`/`L` to avoid visual ambiguity), uppercase only, e.g. `FSM8K2QX7` (9 chars total, well
  under the 25-char budget, leaving ~15 chars for the human-readable suffix). Generation retries on a
  unique-index collision (probability space `30^6 ≈ 7.3×10^8`, negligible collision rate at this feature's
  scale). Trade-off: a fixed `FSM` prefix makes the code trivially greppable/recognizable in a bank
  statement, at the cost of 3 of the 25 chars.
- **(b)** Shorter (e.g. 6 total chars, no prefix) — more memo budget left for the human-readable suffix, but
  higher risk of a payer's own text (or another app's auto-generated reference code) accidentally
  containing a 6-char alnum run that collides with the extraction regex, and less obviously "ours" if a
  human ever inspects a transaction list.
- **(c)** Longer (12+ chars) for a larger collision-safety margin — unnecessary at this feature's expected
  volume and eats further into the 25-char memo budget, shrinking the human-readable suffix to almost
  nothing.

**OQ2 — Correlation-code lifecycle: always insert a new row, or find-and-reuse per (target, amount)?** →
**RESOLVED (a): find-or-create, 90-day TTL.**
QR generation is on-demand and **never cached** (`wallet-and-qr.md` OQ17) — every QR view/regen re-runs
`GenerateExpenseQrAsync`/etc. If code creation always inserts, repeatedly viewing the same QR screen grows
`qr_correlation_codes` unboundedly for no benefit (the target/amount are identical every time).
- **(a) [recommended]** **Find-or-create**: before inserting, look up the most recent still-valid (see
  OQ2b) code for the exact same `(user, event?, member, expense?, expected amount)` tuple; reuse its `Code`
  if found, else create one. Bounds table growth from repeated regeneration; a member re-scanning an older
  screenshot of the same QR still resolves to the same code. Trade-off: one extra read per billed member at
  generation time (cheap, indexed).
- **(b)** Always insert a new row per generation. Simpler code, but every QR-screen view/refresh (which has
  no caching, per OQ17) creates a fresh row and a fresh code — a member who scans an older, previously
  generated QR image would use a **different, still-valid** code than the one currently displayed, which is
  harmless for matching (both resolve to the same target) but leaves an unbounded, ever-growing table with
  no cleanup mechanism.
- **Related sub-question (expiry):** should a code expire (e.g. 30/90 days) so `qr_correlation_codes` can
  eventually be pruned, mirroring `EventShareLink.ExpiresAt`? Recommended: **yes, a generous TTL (90 days)**,
  since a share/expense amount rarely stays unpaid that long and an expired code simply degrades to
  "unmatched" (held-back, safe) rather than blocking anything.

**OQ3 — Does correlation-code embedding extend to the per-member QR variants and the anonymous public
share-link QR, or only the two composite (`GenerateExpenseQrAsync`/`GenerateEventQrAsync`) methods the
locked decision names?** → **RESOLVED (a): the four owner-initiated, Premium-gated methods; the anonymous
share-link QR is excluded.**
All five `WalletQrService` methods share the same `CollectExpenseBillables`/`CollectEventBillables` +
`Description` shape, so extending embedding to any of them is mechanically identical. The decision is about
**scope/exposure**, not mechanism.
- **(a) [recommended]** Embed codes in **every owner-initiated, Premium-gated** method: the two composite
  routes named in the decision **plus** `GenerateExpenseMemberQrsAsync`/`GenerateEventMemberQrsAsync`
  (per-member data-URL QRs — same owner, same Premium gate, same on-demand call). **Exclude**
  `GenerateEventMemberQrsForShareAsync` (the anonymous public share-link QR, `event-share-link.md`) from
  code embedding in this first version. Reason: the share-link surface deliberately has **no** Premium gate
  and **no** authenticated caller (`PublicSharesController` never reads `AuthenticatedUser`) — enabling a
  real ledger-mutating side effect (even one that only ever touches the *owner's own* data) to be triggered
  purely by an anonymous read is a materially different trust boundary than "the owner generated a QR",
  and is safer to add later, deliberately, than to include implicitly now.
- **(b)** Embed codes everywhere, including the share-link QR. Matches the literal "at QR-generation time"
  wording of the locked decision most broadly and means a member paying via a link-shared QR also gets
  auto-settled — but a Free-tier owner (who cannot use the Premium wallet/QR feature directly) could still
  get this automation for free through the always-ungated share link, and it's one more thing that can
  happen from a page nobody had to log in to load.
- **(c)** Composite routes only (the literal decision text, no extension to per-member QRs). Simplest reading
  of the locked decision, but means a member who scans their **individual** QR (not the composited group
  image) never gets auto-settled even though that QR bills the exact same amount — an inconsistent
  experience between the two QR presentation styles that §3.10 itself treats as interchangeable ("cách trình
  bày là tùy chọn hiển thị; quy tắc... không đổi").

**OQ4 — Amount-match tolerance: exact match only, or an allowed variance?** → **RESOLVED (a): exact match
required, re-resolved live at apply time.**
The QR bills an exact amount (a share's `Amount`, or a member's current `Outstanding`); a real bank
transfer for VND has no sub-đồng fractional risk, but a payer could still transcribe/edit the amount.
- **(a) [recommended]** **Exact match required** (`transaction.Amount == currentExpectedAmount`, re-resolved
  live at apply time, not the stale snapshot taken at QR-generation time — mirrors the settlement-sync
  feature's own "recompute live" precedent, OQ1 there). Any difference (over or under) is held back as
  `AmountMismatch`, visible to the owner, who can settle manually as today. Trade-off: a payer who
  transfers 1đ more/less (rare for VND, which has no minor unit in practice) needs the owner's manual
  fallback instead of an imperfect auto-credit.
- **(b)** Allow the incoming amount to be `>= currentExpectedAmount` (auto-apply the boolean settle on any
  sufficient payment, silently absorbing an overpayment). Trade-off: more forgiving, but an overpayment is
  silently un-refunded/unaccounted-for (this feature calls only the existing **boolean** toggle — it has no
  mechanism to record "member overpaid by X" anywhere), which could misstate what the member actually
  transferred if the owner later reviews raw bank statements against the ledger.
- **(c)** Allow a small absolute tolerance (e.g. ±1.000đ) to survive minor manual edits. Trade-off: same
  under-accounting risk as (b) in miniature, plus one more tunable with no strong justification for VND
  (which doesn't have fractional-đồng transfer noise the way, say, USD cents might).

**OQ5 — Is an owner-facing "held-back transactions" review endpoint in scope for this version, or is
server-side logging alone sufficient for v1?** → **RESOLVED (a): ship the minimal authenticated
`GET api/v1/bank-callbacks` review endpoint.**
Decision 2 requires a held-back transaction to be "visible somewhere for the owner," which this doc must
concretely design (not leave unspecified).
- **(a) [recommended]** Ship a minimal, read-only, **authenticated** `GET api/v1/bank-callbacks` endpoint
  (paginated, scoped to the current user's `ResolvedUserId`) listing recent inbound transactions —
  applied, already-settled-no-op, amount-mismatched, and verification-failed-but-resolved rows — so the
  owner can see *why* an expected auto-settle didn't happen, without any new action beyond what they can
  already do (manually toggle settled). No new mutation surface. Trade-off: one new endpoint + DTO for a
  first version whose primary ask was the trigger, not a review UI.
- **(b)** Server-side logging only (NLog) for v1; defer any owner-facing surface to Future Improvements.
  Simpler for a first ship, but a held-back transaction with an unresolvable correlation code (unknown/
  expired) has **no** resolved user at all — even option (a)'s endpoint can never show it to anyone — so
  under (b) *every* held-back case is invisible to the owner, which arguably fails Decision 2's own
  requirement more completely than (a) does.

**OQ6 — Destination-account cross-check: soft/logged-only, or a hard block on mismatch?** → **RESOLVED
(a): soft/logged only, never blocks.**
If the SePay payload includes the receiving account number/BIN, should the applier verify it matches the
correlation code's intended destination bank account before applying?
- **(a) [recommended]** **Soft/logged only** — if the payload's destination account is present and does
  **not** match, log a warning but still apply the settle on a confident code+amount match. Reason: Decision
  1 explicitly frames the correlation code (not account heuristics) as *the* matching mechanism; using the
  account as a hard blocking factor risks false-negative holds if SePay's per-transaction account field is
  unreliable/differently formatted than the stored `bank_accounts.account_number` (an assumption not yet
  verified against SePay's real payload, see Assumptions) — a false block defeats the entire point of
  automation more than a false pass does (the code itself is already the unguessable, unique proof of
  intent).
- **(b)** **Hard block** — treat a destination mismatch as `VerificationFailed`, held back, not applied.
  Stronger defense-in-depth against a code being replayed against the wrong receiving account (e.g. if a
  code somehow leaked and someone paid a *different* one of the owner's own accounts, or a malformed/
  malicious payload), at the cost of depending on payload-field reliability this doc cannot verify without
  a real SePay account.

**OQ7 — Raw callback payload retention: how long, and any redaction?** → **RESOLVED (a): retain
indefinitely; excluded from the owner-facing response, server-side only.**
`bank_transaction_callbacks.raw_payload` stores the verbatim webhook body for audit/dispute resolution,
which likely includes the **payer's** own bank account number/name (a third party who is not our user).
- **(a) [recommended]** Retain indefinitely for v1 (mirrors `AuditLog`'s own "immutable, never deleted"
  philosophy for expenditure history), **but** exclude the raw payload from the owner-facing `GET
  api/v1/bank-callbacks` response (only structured fields — amount, outcome, matched target — are
  returned; the raw JSON stays server-side only, queryable directly against the DB if ever needed for
  support/dispute). No redaction of the stored payload itself. Trade-off: retains third-party payer PII
  indefinitely with no purge mechanism, deferred to a future data-retention policy pass across the whole
  app (this app has no general PII-retention policy yet, so scoping one narrowly to this one table would be
  inconsistent).
- **(b)** Time-boxed retention (e.g. purge `raw_payload` after 180 days, keep the structured/aggregate
  fields). Reduces standing PII exposure, but needs a new purge job (background worker) this feature would
  be the first to introduce for data lifecycle reasons, not spelled out anywhere else in the repo.

**OQ8 — Rate limiting / abuse protection on the anonymous `POST api/v1/bank-callbacks/{provider}` route.**
→ **RESOLVED (a): no app-level rate limiting in v1.**
- **(a) [recommended]** **No app-level rate limiting in v1.** A request with an invalid API key is rejected
  immediately (header compare, no DB hit) — cheap to reject at volume — and a request with a valid key but
  an unresolvable code is also cheap (one indexed lookup, no write beyond the audit row). Rate limiting for
  an internet-facing webhook is conventionally handled at the infra/reverse-proxy/WAF layer, which this
  repo has never modeled in application code (no precedent for `Microsoft.AspNetCore.RateLimiting` or
  similar anywhere today). Trade-off: no defense if the app is deployed without an infra-level limiter.
- **(b)** Add ASP.NET Core's built-in rate-limiting middleware scoped to this one route. First use of that
  middleware in the repo — a real new piece of infrastructure to introduce, own, and test for a risk that
  (a) already mitigates cheaply at the app layer via the fast-reject API-key check.

**OQ9 — Is the new `GET api/v1/bank-callbacks` review-list endpoint Premium-gated (mirrors the wallet/QR
"mở rộng" group) or ungated (mirrors "reads are never tier-gated," §4.9)?** → **RESOLVED (a): ungated.**
- **(a) [recommended]** **Ungated.** A Free-tier owner will simply always see an empty list, since
  correlation codes are only ever created behind the already-Premium-gated QR-generation calls (§4.9 "reads
  are never limit/tier-gated" — the same reasoning `bank-directory-provider.md` OQ-A used for `GET
  api/v1/banks`). No new gate call needed.
- **(b)** Premium-gate it via `tierService.EnsurePremiumFeature(MessageKeys.Feature.Qr)` for consistency
  with "this whole area is Premium." Redundant given (a)'s reasoning (the list would be empty for Free
  anyway), but makes the boundary explicit rather than incidental.

**OQ10 — SePay ack response shape: does SePay require a specific response body/status to consider the
webhook delivered, or is any 2xx sufficient?** → **RESOLVED (a): standard `[ResponseWrapped]` envelope,
assumed 2xx-with-JSON is accepted; flagged in Assumptions as unverified against real SePay docs.**
- **(a) [recommended]** Return the standard `AppController`/`[ResponseWrapped]` envelope
  (`ApiResult.SuccessMessage(...)`) like every other endpoint in this repo, on the assumption any 2xx with
  a parseable JSON body is accepted (common for most webhook aggregators, and specifically not contradicted
  by anything in the assumed SePay contract below). Trade-off: unverified against SePay's real
  documentation — flagged in Assumptions, easy single-line fix if SePay turns out to require an exact ack
  shape.
- **(b)** Return a bespoke minimal `{ "success": true }` shape matching common webhook-ack conventions,
  bypassing `[ResponseWrapped]` the way the QR image routes bypass it for `FileContentResult` (M8 pattern).
  More defensive against an unknown exact SePay contract, at the cost of being the first JSON (non-file)
  response in the repo to intentionally not use the standard envelope.

## Assumptions

> Third-party contract details that cannot be verified against SePay's real documentation from inside this
> repo — same caveat pattern already used for the VietQR `generate` endpoint's assumed contract in
> `planning/bank-directory-provider.md`'s Assumptions section. **Must be verified against SePay's actual
> published docs before the SePay-specific parser is trusted in production**; the provider abstraction
> means only `SePayBankCallbackParser` needs correction if any of these are wrong.

- **SePay webhook auth = a static API key in a request header**, e.g. `Authorization: Apikey {key}`
  (configured per-integration in SePay's dashboard), verified via a **constant-time** comparison
  (`CryptographicOperations.FixedTimeEquals`) against a configured secret — **not** an HMAC signature over
  the request body. If SePay in fact signs the body, only `SePayBankCallbackParser.Verify` needs to change
  (the controller/resolver/service shape is unaffected).
- **SePay webhook payload shape** (JSON body), approximately:
  ```json
  {
    "id": 92704,
    "gateway": "Vietcombank",
    "transactionDate": "2026-08-26 14:02:37",
    "accountNumber": "0123499999",
    "code": null,
    "content": "FSM8K2QX7 chuyen tien",
    "transferType": "in",
    "transferAmount": 500000,
    "accumulated": 19077000,
    "subAccount": null,
    "referenceCode": "MBVCB.3278907687",
    "description": ""
  }
  ```
  `id` = the provider transaction id (dedup key); `transferType` = `"in"`/`"out"` (only `"in"` is
  processed); `transferAmount` = the VND amount (integer, no minor unit); `content` = the free-text transfer
  memo the correlation code is extracted from; `code` = SePay's **own** optional auto-extracted field (SePay
  supports configuring a prefix pattern server-side and pre-extracting a matching substring from `content`
  into this field) — the parser **prefers `code` when non-null/non-empty**, falling back to an app-side
  regex (`FSM[A-Z2-9]{6}` or whatever OQ1 finalizes) over `content` otherwise, so the feature works whether
  or not SePay's own extraction feature is configured for this integration.
- Some bank apps let the payer **edit** the QR-prefilled transfer content before confirming, which can
  strip or corrupt the correlation code. This is an accepted, inherent limitation of memo-based correlation
  (already implicit in the locked Decision 1, not reopened here) — the held-back design (OQ5) is the
  mitigation: a stripped code degrades to `UnmatchedCode`, safe and visible, never a wrong auto-apply.
- The webhook is delivered over HTTPS directly to this API (no intermediary queue/relay) — matches this
  repo's existing "first outbound HTTP call" precedent being a direct call, and the app has no message-queue
  infrastructure to receive through instead.
- `IExpensesService.SetSettledAsync` (the **whole-expense** toggle) is intentionally **not** a bank-callback
  trigger target — a single bank transfer pays one member's one share, or clears one member's event
  balance; it never simultaneously represents "every billable member on this expense just paid," so there is
  no confident single-transaction mapping to that endpoint. (An expense with exactly one billable member
  will still end up fully settled once that member's share is toggled, via the existing `SettlementReconciler.
  ReconcileExpense` cascade already wired into `ShareRepository.SetSettledAsync` — no gap.)
- Users are never hard-deleted in this app (no delete-account feature exists), so `ResolvedUserId`'s FK
  cascade-delete behavior on `bank_transaction_callbacks` is presently unreachable in practice; `Cascade` is
  chosen only for consistency with every other `UserId` FK in the codebase (e.g. `BankAccount`, `Category`).

## Implementation Plan

> Paths relative to `FairShareMonApi/FairShareMonApi/`. New services/repositories use DiDecoration
> `[ScopedService]`; the typed nothing-new-HTTP is needed (this feature receives inbound HTTP, it makes no
> outbound calls). All user-facing/Swagger strings are Vietnamese. Concrete option letters below assume the
> recommended options from every Open Question above; if the user picks a different option the affected
> step is called out inline.

### Step 1 — Entities + EF migration

1. `Database/Entities/QrCorrelationCode.cs` (POCO, `partial`, `IEntity`): `ulong Id`, `string Uuid`,
   `ulong UserId`, `ulong? EventId`, `ulong MemberId`, `ulong? ExpenseId`, `required string Code`,
   `decimal ExpectedAmountSnapshot`, `DateTime? ExpiresAt` (OQ2), `DateTime CreatedAt`, `DateTime UpdatedAt`;
   nav `User User`, `Event? Event`, `Member Member`, `Expense? Expense`. XML doc: the snapshot amount is
   **display/debug only**; the applier always re-resolves the CURRENT expected amount live (mirrors
   `event-expense-settlement-sync.md` OQ1's "recompute live" precedent) rather than trusting a possibly
   stale value.
2. `Database/Entities/Partials/QrCorrelationCode.cs`: ctor (`Uuid = Uuid.NewV7()`, `CreatedAt =
   AppDateTime.Now`); `CodeMaxLength = 16` (per OQ1's ~9-char format, roomy); static
   `ConfigureModel(ModelBuilder)`:
   - Table `qr_correlation_codes`; check constraint `ck_qr_correlation_codes_amount_non_negative`
     (`expected_amount_snapshot >= 0`).
   - `id` PK; `uuid` (max 64, unique index); `code` (max `CodeMaxLength`, **unique index** — the anonymous
     lookup key, mirrors `EventShareLink.Token`); `user_id` (indexed, FK → `users.id` cascade); `event_id`
     (nullable, indexed, FK → `events.id` cascade); `member_id` (FK → `members.id` restrict — mirrors
     `EventMemberSettlement.MemberId`); `expense_id` (nullable, indexed, FK → `expenses.id` cascade);
     `expected_amount_snapshot` `decimal(18,2)`; `expires_at` (nullable); `created_at`/`updated_at`
     (`ValueGeneratedOnAddOrUpdate` + `current_timestamp(6) ON UPDATE ...`, the standard pattern).
3. `Database/Entities/BankTransactionCallback.cs` (POCO, `partial`, `IEntity`): `ulong Id`, `string Uuid`,
   `required string ProviderKey`, `required string ProviderTransactionId`, `bool IsIncoming`,
   `decimal Amount`, `string? BankBin`, `string? DestinationAccountNumber`, `required string Content`,
   `string? ExtractedCode`, `DateTime TransactionAt`, `required string RawPayload`, `ulong?
   MatchedCorrelationCodeId`, `ulong? ResolvedUserId`, `BankCallbackOutcome Outcome`, `string? FailureNote`,
   `DateTime? AppliedAt`, `DateTime CreatedAt`, `DateTime UpdatedAt`; nav `QrCorrelationCode?
   MatchedCorrelationCode`, `User? ResolvedUser`.
4. `Database/Entities/BankCallbackOutcome.cs` (or inline in the entity file, mirrors `AuditAction`'s
   placement): `public enum BankCallbackOutcome { Ignored = 0, UnmatchedCode = 1, AmountMismatch = 2,
   VerificationFailed = 3, AlreadySettledNoOp = 4, Applied = 5 }`.
5. `Database/Entities/Partials/BankTransactionCallback.cs`: ctor; length consts (`ProviderKeyMaxLength =
   32`, `ProviderTransactionIdMaxLength = 128`, `BankBinMaxLength = 16`, `AccountNumberMaxLength = 32`,
   `ContentMaxLength = 500`, `ExtractedCodeMaxLength = 16`, `FailureNoteMaxLength = 500`); static
   `ConfigureModel(ModelBuilder)`:
   - Table `bank_transaction_callbacks`; check constraint `ck_bank_transaction_callbacks_amount_non_negative`
     (`amount >= 0`).
   - `id` PK; `uuid` (unique); `provider_key`/`provider_transaction_id` with a **composite unique index**
     `ux_bank_transaction_callbacks_provider_tx` (the idempotency dedup key, Requirements); `is_incoming`;
     `amount` `decimal(18,2)`; `bank_bin`/`destination_account_number` (nullable); `content` (max 500);
     `extracted_code` (nullable, max 16, indexed — supports an "all callbacks for this code" debug query);
     `transaction_at`; `raw_payload` (`longtext`, mirrors `AuditLog.BeforeData`'s `longtext` mapping);
     `matched_correlation_code_id` (nullable FK → `qr_correlation_codes.id`, `OnDelete(SetNull)`);
     `resolved_user_id` (nullable, indexed, FK → `users.id`, `OnDelete(Cascade)` — the list-endpoint scope
     column); `outcome` (`HasConversion<int>()`, mirrors `AuditLog.EntityType`); `failure_note` (nullable,
     max 500); `applied_at` (nullable); `created_at`/`updated_at`.
6. `Database/AppDbContext.cs`: add `DbSet<QrCorrelationCode> QrCorrelationCodes => Set<QrCorrelationCode>();`
   and `DbSet<BankTransactionCallback> BankTransactionCallbacks => Set<BankTransactionCallback>();`; invoke
   both `ConfigureModel(modelBuilder)` calls in `OnModelCreating`. Neither is `IEntityDeletable` — no
   soft-delete filter needed (`AppDbContext.partial.cs` untouched).
7. **Migration:** `dotnet ef migrations add AddBankCallbackSettlement --project
   .\FairShareMonApi\FairShareMonApi.csproj` (offline via the pinned design-time factory). Review: two new
   tables, both CHECK constraints, the two unique indexes (`code`, `(provider_key, provider_transaction_id)`),
   all FKs with the cascade/restrict/set-null behaviors listed above. Keep the model snapshot in sync.

### Step 2 — Correlation-code repository (owner-scoped create, anonymous lookup)

`Repositories/QrCorrelationCodeRepository.cs` — `IQrCorrelationCodeRepository : IBaseRepository` + sealed
impl (`[ScopedService(typeof(IQrCorrelationCodeRepository))]`, extends `BaseRepository`), mirroring
`EventShareLinkRepository`'s owner-scoped-write / anonymous-read split:
- `Task<QrCorrelationCode> GetOrCreateAsync(string userUuid, string? eventUuid, string memberUuid, string?
  expenseUuid, decimal expectedAmount, CancellationToken ct)` — resolves the owner-scoped `User`/`Event?`/
  `Member`/`Expense?` ids (defensive; the caller already resolved these via existing services); per **OQ2**,
  first looks for a still-valid (`ExpiresAt == null || ExpiresAt > now`) existing row matching the exact
  `(UserId, EventId, MemberId, ExpenseId, ExpectedAmountSnapshot)` tuple; if found, returns it unchanged;
  else generates a fresh `Code` (retry-on-unique-collision, OQ1's alphabet/length) and inserts a new row
  with `ExpiresAt = now.AddDays(90)` (OQ2). One `ExecuteTransactionAsync`.
- `Task<CorrelationTarget?> ResolveCurrentTargetAsync(string code, CancellationToken ct)` — the **anonymous**
  lookup the webhook path uses (mirrors `EventShareLinkRepository.GetByTokenAsync`, no `userUuid` filter):
  loads the `QrCorrelationCode` row by `Code` with `.Include(c => c.User).Include(c => c.Member)
  .Include(c => c.Event).Include(c => c.Expense).ThenInclude(e => e!.Shares)`; if the row is null or
  `ExpiresAt <= now`, returns `null` (→ `UnmatchedCode`). Otherwise builds a `CorrelationTarget` record
  (`Kind` = `Share` when `ExpenseId != null` else `EventMember`; `UserUuid`, `EventUuid?`, `MemberUuid`,
  `ExpenseUuid?`, `ShareUuid?` (resolved from `Expense.Shares.FirstOrDefault(s => s.MemberId ==
  code.MemberId)`, since the unique `(expense_id, member_id)` share index — noted in
  `event-expense-settlement-sync.md`'s own Step M1.2 — guarantees at most one), `CurrentExpectedAmount`
  (the **live** share `Amount` for a `Share` target, or the **live** `NetOwed` for an `EventMember` target
  via `EventSettlementClassifier.ClassifyAsync(db, evt.Id, [member.Id], ct)` — the exact same canonical
  helper Direction 1/2 already gate on, reused here rather than reimplemented), `IsAlreadySettled` (the
  share's `IsSettled` for a `Share` target, or `NetOwed <= 0m || ClearedAmount >= NetOwed` for an
  `EventMember` target)). A `Share` target whose resolved share is somehow `null` (defensive — should not
  happen given the unique-index guarantee) also returns `null` from this method (→ `VerificationFailed` at
  the service layer, not a crash).

### Step 3 — Bank-transaction callback repository (dedup + audit)

`Repositories/BankTransactionCallbackRepository.cs` — `IBankTransactionCallbackRepository : IBaseRepository`
+ sealed impl:
- `Task<BankTransactionCallback?> FindByProviderTransactionAsync(string providerKey, string
  providerTransactionId, CancellationToken ct)` — the idempotency pre-check (fast path, indexed).
- `Task<BankTransactionCallback> RecordAsync(BankTransactionCallbackData data, CancellationToken ct)` —
  inserts the row (all fields from Step 1.3); relies on the unique `(provider_key, provider_transaction_id)`
  index as a DB-level backstop against a race between the pre-check and the insert (a duplicate-key
  exception on insert is caught and treated as "already recorded," returning the existing row — never
  surfaced as a 500).
- `Task<(IReadOnlyList<BankTransactionCallback> Items, int Total)> ListByUserAsync(string userUuid, int
  limit, int offset, CancellationToken ct)` — scoped by `ResolvedUserId`'s owning `User.Uuid`, newest first,
  the standard `?limit=&offset=` pagination convention (`AGENTS.md`).

### Step 4 — Provider abstraction (`Services/Api/BankCallbacks/`)

`Services/Api/BankCallbacks/IBankCallbackParser.cs`:
```csharp
public sealed record BankTransactionEvent(
    string ProviderTransactionId, bool IsIncoming, decimal Amount, string Content,
    string? ExtractedCode, DateTime TransactionAt, string? BankBin, string? DestinationAccountNumber);

public interface IBankCallbackParser
{
    string ProviderKey { get; }
    bool Verify(HttpRequest request, JsonElement payload);
    BankTransactionEvent? Parse(JsonElement payload);
}
```
`Services/Api/BankCallbacks/SePayBankCallbackParser.cs` —
`[ScopedService(typeof(IBankCallbackParser))] { Multiple = true }`, `ProviderKey => "sepay"`; primary ctor
`(IOptions<BankCallbacksOptions> options)`:
- `Verify`: reads the configured header (`Authorization`, expected value `$"Apikey
  {options.Value.SePay.ApiKey}"`), constant-time-compares (OQ10 assumption); missing/blank configured key →
  always fails closed (never "no key configured = allow").
- `Parse`: deserializes the SePay shape (Assumptions); ignores `transferType != "in"` by returning an event
  with `IsIncoming = false` (the service short-circuits on this, Step 5); `ExtractedCode` = SePay's own
  `code` field when non-empty, else a `[GeneratedRegex]` match (`{Prefix}[A-Z2-9]{6}` per OQ1/config) over
  `content`, else `null`.

`Services/Api/BankCallbacks/BankCallbackParserResolver.cs`:
```csharp
public interface IBankCallbackParserResolver { IBankCallbackParser? Resolve(string providerKey); }
```
`[ScopedService(typeof(IBankCallbackParserResolver))]`, primary ctor `(IEnumerable<IBankCallbackParser>
parsers)`; `Resolve` matches `ProviderKey` case-insensitively; **returns `null` on no match** (unlike
`QrContentProviderResolver`'s always-fallback-to-local design — there is no sensible default aggregator for
an inbound webhook, Background).

### Step 5 — Applier service (the orchestrator, no new settlement math)

`Services/Api/BankCallbacks/BankCallbackService.cs`:
```csharp
public interface IBankCallbackService
{
    Task<BankCallbackOutcome> ProcessAsync(string providerKey, BankTransactionEvent transactionEvent,
        string rawPayload, CancellationToken cancellationToken = default);
}
```
`[ScopedService(typeof(IBankCallbackService))]`, primary ctor `(IBankTransactionCallbackRepository
callbackRepository, IQrCorrelationCodeRepository correlationRepository, ISharesService sharesService,
IEventsService eventsService, ILogger<BankCallbackService> logger)`. `ProcessAsync`:
1. **Idempotency:** `existing = await callbackRepository.FindByProviderTransactionAsync(providerKey,
   transactionEvent.ProviderTransactionId, ct)`; if found, return `existing.Outcome` — no reprocessing.
2. If `!transactionEvent.IsIncoming` → record `Outcome = Ignored`, return.
3. If `transactionEvent.ExtractedCode` is null/blank → record `Outcome = UnmatchedCode` (`ResolvedUserId =
   null`), return.
4. `target = await correlationRepository.ResolveCurrentTargetAsync(transactionEvent.ExtractedCode, ct)`; if
   `null` → record `Outcome = UnmatchedCode` (`ResolvedUserId = null`), return.
5. (OQ6, soft check) If `transactionEvent.DestinationAccountNumber` is present and does not match the
   target's expected destination, `logger.LogWarning(...)` — does **not** block.
6. If `target.IsAlreadySettled` → record `Outcome = AlreadySettledNoOp` (`ResolvedUserId` = the target's
   user), return — an intentional idempotent no-op (a retried/duplicate transfer for an already-cleared
   target).
7. If `transactionEvent.Amount != target.CurrentExpectedAmount` (OQ4, exact match) → record `Outcome =
   AmountMismatch` (`ResolvedUserId` set — the owner CAN see this one, Requirements/OQ5), return.
8. **Apply** — the one call into the existing, unmodified settlement surface:
   - `target.Kind == Share` → `await sharesService.SetSettledAsync(target.UserUuid, target.ExpenseUuid!,
     target.ShareUuid!, new SetSettledRequest { IsSettled = true }, ct)`.
   - `target.Kind == EventMember` → `await eventsService.SetMemberSettledAsync(target.UserUuid,
     target.EventUuid!, target.MemberUuid, new SetSettledRequest { IsSettled = true }, ct)`.
   - A resource-owned `ErrorException` from either call (e.g. the target was deleted between step 4 and
     step 8 — rare race) is caught, logged, and recorded as `Outcome = VerificationFailed` rather than
     propagated as a 500 (Decision Log entry 6).
9. Record `Outcome = Applied` (`ResolvedUserId` set, `AppliedAt = now`), return.

Each `Outcome` write is a **separate** `BankTransactionCallbackRepository.RecordAsync` transaction from the
settle-service call in step 8 (Decision Log entry 6 explains why this two-phase, non-atomic design is safe:
the underlying settlement writes are themselves structurally idempotent no-ops on an unchanged flag, per
`event-expense-settlement-sync.md`'s own "a share whose flag does not change contributes no delta" —
so a retried webhook that re-reaches step 8 after a step-9 failure just repeats a no-op, never
double-credits).

### Step 6 — Wire correlation-code embedding into `WalletQrService`

**[MOD]** `Services/Api/Wallet/WalletQrService.cs`:
- Inject `IQrCorrelationCodeRepository correlationCodeRepository` in the primary ctor.
- Add a private helper:
  ```csharp
  private async Task<IReadOnlyList<BilledMember>> AttachCorrelationCodesAsync(
      string userUuid, string? eventUuid, string? expenseUuid,
      IReadOnlyList<BilledMember> billed, CancellationToken ct)
  {
      var result = new List<BilledMember>(billed.Count);
      foreach (var member in billed)
      {
          var code = await correlationCodeRepository.GetOrCreateAsync(
              userUuid, eventUuid, member.MemberUuid, expenseUuid, member.Amount, ct);
          result.Add(member with { Description = $"{code.Code} {member.Description}" });
      }
      return result;
  }
  ```
- Call it right after `CollectExpenseBillables`/`CollectEventBillables` in `GenerateExpenseQrAsync`,
  `GenerateEventQrAsync`, `GenerateExpenseMemberQrsAsync`, `GenerateEventMemberQrsAsync` (OQ3, option (a) —
  the four owner-initiated, Premium-gated methods). **`GenerateEventMemberQrsForShareAsync` (the anonymous
  share-link QR) is left unchanged** — no correlation code, no auto-settle from that surface (OQ3).
  `CollectExpenseBillables`/`CollectEventBillables` themselves stay pure/unchanged (still directly
  unit-testable without a DB).
- No change to `VietQrPayloadBuilder`/`FoldMemo` — the code-first ordering alone guarantees survival within
  the existing 25-char budget (Background).

### Step 7 — Options + DTOs + error codes + message keys

1. `Models/BankCallbacks/BankCallbacksOptions.cs`:
   ```csharp
   public class BankCallbacksOptions
   {
       public const string SectionName = "BankCallbacks";
       public SePayCallbackOptions SePay { get; set; } = new();
   }
   public class SePayCallbackOptions
   {
       public string ApiKey { get; set; } = "";
       public string CodePrefix { get; set; } = "FSM";
   }
   ```
2. `Models/BankCallbacks/BankTransactionCallbackResponse.cs` — `{ string Uuid; string ProviderKey; decimal
   Amount; string Content; string Outcome; DateTime TransactionAt; DateTime? AppliedAt; string?
   MatchedTargetType; string? MatchedExpenseUuid; string? MatchedEventUuid; string? MatchedMemberUuid;
   string? MemberName; DateTime CreatedAt; }` — **no `RawPayload` field** (OQ7). `Outcome` is `string`
   (`.ToString()` on the enum), matching the `EventSettlementStatus`/`UserResponse.Tier` precedent
   (`event-expense-settlement-sync.md` Decision Log entry 6 — no `JsonStringEnumConverter` registered, so a
   raw enum would serialize as an integer).
3. Append to `Constants/ErrorCodes.cs`, **18xxx block = Bank callback settlement**:

   | Code | Name | HTTP | Message (Vietnamese) |
   |---|---|---|---|
   | `18000` | `BankCallbackVerificationFailed` | 401 | "Xác thực webhook ngân hàng không hợp lệ." |
   | `18001` | `BankCallbackPayloadInvalid` | 400 | "Dữ liệu giao dịch ngân hàng không hợp lệ." |
   | `18002` | `BankCallbackProviderUnknown` | 404 | "Không hỗ trợ nhà cung cấp giao dịch ngân hàng này." |

   Extend `ErrorException.GetDefaultHttpStatus`: `18000`→401, `18001`→400, `18002`→404. No codes are
   needed for `UnmatchedCode`/`AmountMismatch`/`VerificationFailed`/`Ignored`/`AlreadySettledNoOp` — those
   are internal `Outcome` values, always ACK'd 200 to the provider (a webhook delivery is not "in error"
   just because the app couldn't resolve/apply it).
4. `Constants/MessageKeys.cs`: `Error.BankCallbackVerificationFailed`, `Error.BankCallbackPayloadInvalid`,
   `Error.BankCallbackProviderUnknown`, `Success.BankCallbackReceived` ("Đã nhận giao dịch ngân hàng.").
   Add matching entries to `Localization/Resources/StringResources.resx` (Vietnamese, default) and
   `StringResources.en-US.resx` (English) per the existing per-key convention.

### Step 8 — Controller

`Controllers/BankCallbacksController.cs` (new; derives `AppController`; explicit
`[Route("api/v{version:apiVersion}/bank-callbacks")]`; primary ctor `(IBankCallbackParserResolver
parserResolver, IBankCallbackService bankCallbackService, IBankTransactionCallbackRepository
callbackRepository, IStringLocalizer<StringResources> localizer)`):

| Verb + Route | Auth | Request → Response | Notes |
|---|---|---|---|
| `POST api/v1/bank-callbacks/{provider}` | `[AllowAnonymous]` (method-level, Decision Log entry 5) | `[FromRoute] string provider`, `[FromBody] JsonElement payload` → `ApiResult` success message | unknown provider → 18002; verify fail → 18000; parse fail → 18001; else delegates to `IBankCallbackService.ProcessAsync`, always 200 regardless of internal `Outcome` |
| `GET api/v1/bank-callbacks` | authenticated (default fallback policy) | `[FromQuery] int limit = 20, int offset = 0` → `ApiResult<IReadOnlyList<BankTransactionCallbackResponse>>` | scoped to `AuthenticatedUser.Id`; OQ9 no Premium gate; OQ5's review surface |

`ReceiveAsync` body: resolve parser → `Verify` → `Parse` → `bankCallbackService.ProcessAsync(provider,
parsed, payload.GetRawText(), ct)` → `ApiResult.SuccessMessage(localizer[MessageKeys.Success.
BankCallbackReceived].Value)`. Vietnamese `[SwaggerOperation]`/`[SwaggerResponse]` on both actions
(`401`/`400`/`404` on the POST; `401` on the GET, inherited from the fallback policy).

### Step 9 — Program.cs + config wiring

Mirror `bank-directory-provider.md`'s Decision 3 (standard .NET, not the DiDecoration `[Option]` scanner):
```csharp
builder.Services.Configure<FairShareMonApi.Models.BankCallbacks.BankCallbacksOptions>(
    builder.Configuration.GetSection(FairShareMonApi.Models.BankCallbacks.BankCallbacksOptions.SectionName));
```
placed alongside the existing `Banks` options block. Add a `BankCallbacks` section to `appsettings.json` +
`appsettings.Development.json` (`SePay: { ApiKey: "", CodePrefix: "FSM" }` — the real key supplied via
environment/`appsettings.Development.local.json`, never committed, mirroring how connection strings/secrets
are already handled per the config-precedence note in `bank-directory-provider.md`).

### Step 10 — Tests (owned by the test-engineer; definitive list)

Reuse the shipped harness: `[Collection("AuthIntegration")]`; DB-dependent tests `[SkippableFact]` (skip
when MariaDB unreachable), never EF InMemory; unique lowercase username prefix per class; dispose-time
cleanup.

**Unit (no DB):**
- `SePayBankCallbackParserTests` — `Verify` accepts the exact configured header value, rejects
  missing/wrong/blank; `Parse` maps every field; `transferType != "in"` → `IsIncoming = false`; prefers the
  payload's own `code` field over the fallback regex when non-empty; falls back to the prefix regex over
  `content` when `code` is null; no match → `ExtractedCode = null`.
- `BankCallbackParserResolverTests` — resolves by case-insensitive `ProviderKey`; unknown key → `null`.
- Correlation-code generator (wherever OQ1's format lives) — always starts with the configured prefix,
  correct length, alphabet-restricted, no ambiguous characters.
- `WalletQrServiceTests` (extend existing suite) — `AttachCorrelationCodesAsync`'s composed `Description`
  always places the code first; the resulting `VietQrPayloadBuilder.FoldMemo` output (25-char truncation)
  still starts with the full, untruncated code even for a long expense/member name (the core memo-safety
  regression this feature depends on); `GenerateEventMemberQrsForShareAsync` output is **unchanged**
  (byte-for-byte, no code) — the OQ3 exclusion regression.
- `BankCallbackServiceTests` (fakes for both repositories + `ISharesService`/`IEventsService`) — replaying
  the identical `(providerKey, providerTransactionId)` returns the cached outcome, calls neither settle
  service nor `RecordAsync` again; `IsIncoming = false` → `Ignored`, no lookups; null/blank
  `ExtractedCode` → `UnmatchedCode`, `ResolvedUserId` null; unknown code → `UnmatchedCode`; over/under
  amount → `AmountMismatch`, settle service never called; already-settled target → `AlreadySettledNoOp`,
  settle service never called; exact match on a `Share` target → `ISharesService.SetSettledAsync` called
  exactly once with `IsSettled = true`, `Outcome = Applied`; exact match on an `EventMember` target →
  `IEventsService.SetMemberSettledAsync` called exactly once; a resource-owned `ErrorException` thrown by
  either settle service is caught → `VerificationFailed`, not rethrown.

**Integration (real MariaDB):**
- `QrCorrelationCodeRepositoryTests` — `GetOrCreateAsync` reuses an existing valid code for an identical
  `(user, event?, member, expense?, amount)` tuple (OQ2) and creates a fresh one when any field differs or
  the prior code expired; `ResolveCurrentTargetAsync` resolves a `Share` target's **live** `Amount` (not a
  stale snapshot after the share was edited) and an `EventMember` target's **live** `NetOwed` via
  `EventSettlementClassifier`; expired code → `null`.
- `BankTransactionCallbackRepositoryTests` — the unique `(provider_key, provider_transaction_id)` index is
  enforced at the DB level (a concurrent-insert race resolves to "return the existing row," never a 500);
  `ListByUserAsync` is scoped correctly (another user's rows never appear).
- `BankCallbacksEndpointTests` (`WebApplicationFactory`):
  - Generate an expense QR for a real seeded share (via the existing authenticated QR route) → extract the
    embedded code from the response payload/memo → `POST /api/v1/bank-callbacks/sepay` with a
    fabricated-but-realistic SePay body (matching `content`/amount, valid configured API key) → 200; a
    subsequent `GET /expenses/{uuid}` shows the share `IsSettled: true`, **and** Direction 2's existing
    credit cascade fired exactly as it would from `PUT .../shares/{shareUuid}/settled` (parity regression
    against the manual route, using the same fixture the settlement-sync suite already uses).
  - The identical webhook body (same `id`) POSTed twice → 200 both times, no double-credit (assert
    `EventMemberSettlement.ClearedAmount` is unchanged between the two calls).
  - Wrong API key → 401 `18000`. Unknown `{provider}` segment → 404 `18002`. Malformed body → 400 `18001`.
  - An amount that doesn't match the live share amount → 200 (ack'd), but the share stays unsettled, and
    the owner's `GET /api/v1/bank-callbacks` lists the row with `outcome: "AmountMismatch"`.
  - Event-level target: generate an event QR (closed event) → matching webhook settles the member's event
    balance with parity to `PUT .../events/{e}/members/{m}/settled` (Direction 1's cascade fires
    identically).
  - Anonymous `GET /api/v1/bank-callbacks` (no token) → 401 (the review endpoint is NOT the anonymous
    route — only the POST is).
  - An `UnmatchedCode` transaction (garbage `content`, no extractable code) → 200, a row is recorded with
    `ResolvedUserId = null` — confirms it is genuinely invisible to every owner's list (OQ5's known
    trade-off for the fully-unresolvable case), distinct from the visible `AmountMismatch`/
    `VerificationFailed` cases.

## Impact Analysis

**APIs:**
- **Two new endpoints:** `POST api/v1/bank-callbacks/{provider}` (`[AllowAnonymous]`, new authorization
  surface — Background) and `GET api/v1/bank-callbacks` (authenticated, OQ9 no Premium gate).
- **No change to any existing endpoint's contract.** The three settled-toggle routes (`PUT .../settled`)
  are called internally by the new applier through their **service** layer, unchanged; their own HTTP
  contracts, response shapes, and manual-caller behavior are untouched.
- **Reopens/extends `wallet-and-qr.md` OQ9** (memo/`addInfo` content) — the memo now carries a
  machine-readable prefix ahead of the existing human-readable suffix; the visual composite labels
  (member name + amount, drawn separately by `QrImageService`) are **unchanged**, since those are rendered
  from `BilledMember.MemberName`/`Amount`, not from `Description`.

**Database:**
- **REQUIRES MIGRATION** (`AddBankCallbackSettlement`): two new tables, `qr_correlation_codes` and
  `bank_transaction_callbacks`, both with DB CHECK non-negative-money constraints, per §4.3. No change to
  any existing table.

**Infrastructure:**
- **No outbound HTTP** (this feature only *receives* webhooks — no typed `HttpClient` needed, unlike
  `bank-directory-provider.md`'s outbound VietQR calls).
- New `BankCallbacks` config section (`appsettings.json`/`appsettings.Development.json`), standard
  `Configure<TOptions>(...)` wiring (no DiDecoration `[Option]` scanner, Decision 4).
- New anonymous, internet-facing attack surface (the webhook route) — mitigated by the API-key check
  (fail-closed) and, per OQ8, deliberately left to infra-layer rate limiting rather than new app-layer
  middleware.

**Services:**
- **New:** `Services/Api/BankCallbacks/IBankCallbackParser.cs` (+ `SePayBankCallbackParser`),
  `BankCallbackParserResolver.cs`, `BankCallbackService.cs`; `Repositories/QrCorrelationCodeRepository.cs`,
  `Repositories/BankTransactionCallbackRepository.cs`; `Database/Entities/QrCorrelationCode.cs` (+
  `Partials/`), `Database/Entities/BankTransactionCallback.cs` (+ `Partials/`, + `BankCallbackOutcome`
  enum); `Models/BankCallbacks/*`; `Controllers/BankCallbacksController.cs`.
- **Modified:** `Services/Api/Wallet/WalletQrService.cs` (new ctor dependency
  `IQrCorrelationCodeRepository`; four of its five public methods gain an `AttachCorrelationCodesAsync`
  call) — **MEDIUM risk**: the `IWalletQrService` interface/method signatures are unchanged, so
  `ExpensesController`/`EventsController`/`PublicSharesController` call sites are unaffected; the only
  behavior change is the memo/`addInfo` content, verified by the dedicated memo-safety regression test
  (Step 10). `Database/AppDbContext.cs` (two new `DbSet`s); `Constants/ErrorCodes.cs` (18xxx block);
  `Constants/MessageKeys.cs`; `Program.cs` (options wiring); `appsettings.json`/`appsettings.Development.json`.
- **Left intact / reused, LOW risk:** `ISharesService`/`IEventsService` (called, not modified);
  `SettlementReconciler`/`EventSettlementClassifier`/`EventSettlementCreditApplier` (reused verbatim, zero
  changes — the entire point of this feature); `VietQrPayloadBuilder`/`QrImageService` (unchanged).

**Documentation:** this planning doc; Vietnamese Swagger on both new endpoints; new `Localization/
Resources/StringResources*.resx` entries; a short annotation to be added to `planning/wallet-and-qr.md`
(OQ9 reopened/extended) once implementation lands.

## Decision Log

> Entries 1-4 are the orchestrator's locked, pre-confirmed decisions (verbatim from the Human Confirmation
> Policy exchange the orchestrator already ran with the user) — recorded here for the record, **not
> reopened**. Entries 5+ are this doc's own implementation-level design decisions (not preference calls —
> either directly implied by locked decisions/existing conventions, or a security-neutral engineering
> choice with no material trade-off worth a separate Open Question).

1. **Matching strategy = correlation code embedded in the QR memo.** A new stored mapping
   `qr_correlation_codes` (code → user/event?/member/expense?/expected-amount) is generated at
   QR-generation time; the callback matcher looks up the target by this code alone, not by amount/account/
   time heuristics. *(Locked by the orchestrator's pre-confirmed exchange with the user.)*
2. **Auto-apply on confident match, no owner confirmation step.** A transaction whose code resolves
   unambiguously fires the existing settlement cascade immediately; only unmatched/ambiguous/
   failed-verification transactions are held back (this doc's own concrete "held back" design: logged,
   recorded, and — where a user can be resolved — visible via `GET api/v1/bank-callbacks`, OQ5). *(Locked.)*
3. **First provider implementation = SePay**, behind a provider abstraction (`IBankCallbackParser` +
   resolver) so a second aggregator needs no change to the matching/applying logic. *(Locked.)*
4. **Bank-triggered settlement writes ARE audited; manual toggles stay unaudited, unchanged.** A new,
   dedicated `bank_transaction_callbacks` table records every inbound transaction and its outcome, scoped
   only to this trigger path. *(Locked.)*
5. **The applier reuses the existing application-SERVICE methods (`ISharesService.SetSettledAsync`,
   `IEventsService.SetMemberSettledAsync`), not the repositories directly.** *Reason:* respects
   `Controller → Service → Repository`; a service calling a sibling service is already an established
   pattern in this exact area (`WalletQrService` already injects `IExpensesService`/`IStatsService`
   directly). *Alternative considered:* call the repositories (`ShareRepository.SetSettledAsync`/
   `EventMemberSettlementRepository.SetMemberSettledAsync`) directly from the new applier — rejected,
   since the applier is not itself a repository and bypassing the service layer would be a fresh, avoidable
   layering exception.
6. **Audit is a NEW, separate table (`bank_transaction_callbacks`), not a new `AuditEntityType` variant on
   the existing `AuditLog`.** *Reason:* `AuditLog` is shaped around a human `ActorUserId` and full
   before/after entity snapshots for `Expense`/`Share` diffs; a webhook has no human actor, and this
   feature's audit need — "which raw transaction produced which boolean settle" plus idempotency dedup — is
   a different shape entirely (closer to a transaction ledger than an entity diff). *Alternative
   considered:* add `AuditEntityType.BankTransaction` and reuse `AuditLog.BeforeData`/`AfterData` for a
   before/after settled-flag snapshot — rejected: would force `ActorUserId` to be non-null for a
   non-human actor (a schema lie), and duplicates the idempotency-dedup index this feature needs anyway
   (which `AuditLog` doesn't have and doesn't need for its own purpose).
7. **The record step (Step 5.9) and the apply step (Step 5.8) are two separate, non-atomic
   transactions.** *Reason:* proven safe because the underlying settlement writes are themselves
   structurally idempotent no-ops when the flag doesn't change (`event-expense-settlement-sync.md`'s own
   finding) — a retry after a record-step failure just repeats a safe no-op, never double-credits.
   *Alternative considered:* wrap both in one outer transaction spanning two different repositories' own
   `ExecuteTransactionAsync` calls — rejected as unnecessary complexity (would require a new
   cross-repository transaction primitive this codebase doesn't have) for a risk that's already covered by
   structural idempotency.
8. **`IBankCallbackParserResolver.Resolve` returns `null` on an unknown provider, unlike
   `QrContentProviderResolver`'s always-fallback-to-`local` design.** *Reason:* there is no sensible
   default bank-transaction aggregator to fall back to for an inbound webhook — the two situations are not
   analogous despite the shared resolver shape.
9. **`[AllowAnonymous]` is applied at the POST action, not the controller**, so the same controller can
   also host the authenticated `GET api/v1/bank-callbacks` review endpoint. *Alternative considered:* a
   second controller purely for the review endpoint — rejected as an unnecessary split for one GET action
   that is conceptually part of the same feature surface.

## Progress Log

### 2026-08-26

- Read `The-ideal.md` (§3.5 settled/payment metadata, §3.7 balance, §3.8 audit scope, §3.10 wallet/QR, §4
  business rules, §5 locked decisions/fixed terms) and `FairShareMonApi/CLAUDE.md`,
  `.agents/rules/rules.md`, `AGENTS.md` for conventions and the mandatory planning template.
- Read `planning/event-expense-settlement-sync.md` (both milestones, Decision Log 1-6) and its upstream BA
  doc `planning/ba/event-expense-settlement-sync-business-analysis.md` in full — confirmed against the LIVE
  code that both milestones are shipped (`Repositories/EventSettlementClassifier.cs`,
  `EventSettlementCreditApplier.cs`, `EventMemberSettlement.ClearedAmount` all present and wired into
  `ShareRepository`/`ExpenseRepository`/`EventMemberSettlementRepository`), so this feature can safely
  treat that cascade as a black box to trigger, not something to re-derive.
- Read `planning/wallet-and-qr.md` (all 17 OQs, esp. OQ9 memo content and OQ17 no-caching) and
  `planning/bank-directory-provider.md` in full (the closest architectural precedent: provider abstraction,
  standard-.NET HTTP/options wiring, always-fallback-safe design) — noted where THIS feature's design must
  deliberately diverge from that precedent (no outbound HTTP; resolver has no fallback; route-keyed not
  config-keyed).
- Read the live code grounding every touch point: `WalletQrService.cs` (all five QR-generation methods,
  `CollectExpenseBillables`/`CollectEventBillables`, the exact `Description`/memo construction to change),
  `VietQrPayloadBuilder.cs` (`FoldMemo`'s 25-char truncation — confirmed a code-first memo survives it
  intact), `ShareRepository.cs`/`ExpenseRepository.cs`/`EventMemberSettlementRepository.cs`/
  `EventSettlementCreditApplier.cs` (the exact write paths + their existing side effects), `SharesService.cs`/
  `EventsService.cs`/`ExpensesService.cs` (confirmed the SERVICE-layer methods to call, not repositories
  directly), `SetSettledRequest.cs` (the reusable request DTO), `AuditLog.cs`/`AuditEntityType` (confirmed
  the two-variant enum and human-actor shape that doesn't fit this feature, motivating a separate table),
  `PublicSharesController.cs`/`EventShareLinkRepository.cs` (the `[AllowAnonymous]` + anonymous-lookup-by-
  opaque-value precedent), `Program.cs` (auth pipeline, existing options/HTTP-client wiring pattern),
  `ErrorCodes.cs`/`MessageKeys.cs` (confirmed 18xxx is next free; existing key-naming convention).
- Designed the three-seam abstraction (`IBankCallbackParser`/resolver → `BankCallbackService` orchestrator
  → the existing `ISharesService`/`IEventsService` write paths), the two new entities
  (`QrCorrelationCode`, `BankTransactionCallback`) combining idempotency-dedup and audit into one table
  (Decision Log entry 6), and the memo-embedding change to `WalletQrService` that keeps
  `CollectExpenseBillables`/`CollectEventBillables` pure while adding an async correlation-code step around
  them.
- Identified and wrote up ten genuinely undecided implementation-level Open Questions (code format/
  lifecycle, QR-surface scope, amount-match tolerance, held-back visibility, destination-account
  cross-check, payload retention, rate limiting, review-endpoint tier gating, ack response shape), each
  with options + a recommendation, per the Human Confirmation Policy — none silently defaulted.
- Recorded the four orchestrator-locked decisions verbatim in the Decision Log plus five of this doc's own
  implementation-level design decisions that are NOT preference calls (service-vs-repository call target,
  separate-table-vs-AuditLog-extension, two-phase-transaction safety argument, resolver fallback
  philosophy, `[AllowAnonymous]` placement).
- Wrote the full Implementation Plan (concrete files, the one migration, DI wiring, error codes, message
  keys) and the Impact Analysis. Did not write any code, migration, or DTO — planning only, per role.

### 2026-08-26 (implementation)

- Read the full approved doc (all 10 OQs RESOLVED (a), Decision Log 1–9 locked), `FairShareMonApi/CLAUDE.md`,
  and `.agents/rules/rules.md`. Read every live precedent the doc cites before writing code:
  `EventShareLinkRepository`/`EventShareLink` (owner-scoped write / anonymous read split, entity
  partial-file pattern, CHECK-constraint pattern), `AuditLog`/`AuditEntityType` (why a separate table),
  `ShareRepository.SetSettledAsync`/`EventMemberSettlementRepository.SetMemberSettledAsync`/
  `EventSettlementClassifier` (the exact existing write paths + the canonical eligibility/NetOwed helper
  reused, never re-derived), `SharesService`/`EventsService` (the SERVICE-layer methods actually called),
  `WalletQrService` (all five QR methods, `CollectExpenseBillables`/`CollectEventBillables`,
  `BilledMember.Description`), `VietQrPayloadBuilder.FoldMemo` (confirmed a code-first memo survives the
  25-char truncation), `QrContentProviderResolver`/`LocalQrContentProvider` (the `Multiple = true` +
  resolver precedent), `PublicSharesController` (the `[AllowAnonymous]` precedent), `ErrorCodes.cs`/
  `MessageKeys.cs`/both `.resx` files (confirmed 18xxx free, key-naming convention), `Program.cs` (the
  standard-.NET options-wiring precedent for `Banks`, auth/authorization pipeline), `BaseRepository`/
  `IBaseRepository`/`DatabaseExtensions` (`ExecuteTransactionAsync`/`NoCommit` semantics).
- **Step 1:** Added `Database/Entities/QrCorrelationCode.cs` (+ `Partials/QrCorrelationCode.cs`:
  ctor, `CodeMaxLength`/`CodePrefix`/`CodeRandomLength`/`CodeAlphabet` consts, `ConfigureModel`) and
  `Database/Entities/BankTransactionCallback.cs` (+ `BankCallbackOutcome` enum in its own file, +
  `Partials/BankTransactionCallback.cs`). Wired both into `AppDbContext.cs` (two new `DbSet`s + the two
  `ConfigureModel` calls). Generated `dotnet ef migrations add AddBankCallbackSettlement
  --project .\FairShareMonApi\FairShareMonApi.csproj` (offline, via the pinned design-time factory) and
  reviewed the output against Step 1.7's checklist: both tables, both CHECK constraints
  (`ck_qr_correlation_codes_amount_non_negative`, `ck_bank_transaction_callbacks_amount_non_negative`),
  both unique indexes (`code`; `ux_bank_transaction_callbacks_provider_tx` on
  `(provider_key, provider_transaction_id)`), and every FK's cascade/restrict/set-null behavior exactly as
  specified — all confirmed present. Model snapshot regenerated in the same command.
- **Step 2:** Added `Repositories/QrCorrelationCodeRepository.cs` (`IQrCorrelationCodeRepository` +
  sealed impl): `GetOrCreateAsync` (defensive owner-scoped User/Event?/Member/Expense? resolution,
  find-or-reuse per OQ2's exact tuple, 90-day TTL, retry-on-collision code generation) and
  `ResolveCurrentTargetAsync` (anonymous lookup-by-code, live re-resolution via
  `EventSettlementClassifier.ClassifyAsync` for an `EventMember` target, `Expense.Shares` for a `Share`
  target). Added `CorrelationTargetKind` enum and the `CorrelationTarget` record in the same file
  (extended with `CorrelationCodeId`/`UserId` beyond the doc's literal field list — a mechanical
  necessity so `BankCallbackService` can populate `BankTransactionCallback.MatchedCorrelationCodeId`/
  `ResolvedUserId` without a second DB round-trip; not a scope/behavior change).
- **Step 3:** Added `Repositories/BankTransactionCallbackRepository.cs` (`IBankTransactionCallbackRepository`
  + sealed impl + `BankTransactionCallbackData` record): `FindByProviderTransactionAsync`, `RecordAsync`
  (duplicate-key exception on insert caught and resolved to the existing row, never a 500), and
  `ListByUserAsync` (owner-scoped, newest-first, with the `MatchedCorrelationCode`→Member/Event/Expense
  includes the GET response needs).
- **Step 4:** Added `Services/Api/BankCallbacks/IBankCallbackParser.cs` (+ `BankTransactionEvent` record),
  `SePayBankCallbackParser.cs` (`[ScopedService(Multiple = true)]`; constant-time API-key `Verify`; `Parse`
  prefers the payload's own `code` field, falls back to a prefix regex over `content`), and
  `BankCallbackParserResolver.cs` (`IBankCallbackParserResolver`, returns null on no match — Decision Log
  entry 8).
- **Step 5:** Added `Services/Api/BankCallbacks/BankCallbackService.cs` (`IBankCallbackService`): the
  9-step orchestrator exactly as specified — idempotency short-circuit, Ignored/UnmatchedCode short
  circuits, live target resolution, OQ6 soft destination log (never blocks), AlreadySettledNoOp,
  OQ4 exact-amount check, the ONE call into `ISharesService.SetSettledAsync`/
  `IEventsService.SetMemberSettledAsync` wrapped in a catch for a resource-owned `ErrorException` ->
  `VerificationFailed`, then the final `Applied` record. `SettlementReconciler`/`EventSettlementClassifier`/
  `EventSettlementCreditApplier` were not touched — confirmed zero edits to those three files.
- **Step 6:** Modified `Services/Api/Wallet/WalletQrService.cs`: added the `IQrCorrelationCodeRepository`
  ctor dependency and the private `AttachCorrelationCodesAsync` helper; wired it into
  `GenerateExpenseQrAsync`, `GenerateEventQrAsync`, `GenerateExpenseMemberQrsAsync`,
  `GenerateEventMemberQrsAsync` (right after each method's `CollectExpenseBillables`/
  `CollectEventBillables` call); `GenerateEventMemberQrsForShareAsync` (the anonymous share-link QR) left
  byte-for-byte unchanged, per OQ3. `CollectExpenseBillables`/`CollectEventBillables` themselves untouched
  (still pure). Ran `impact()`/`detect_changes()` via the GitNexus MCP tools before/after editing per
  CLAUDE.md's mandatory step; both calls failed with a tool-side index-version mismatch
  (`Database file version: 42, Current build storage version: 40`) even after a full `analyze` reindex —
  an environment/tool incompatibility, not something fixable from this session. Substituted a manual
  verification instead: read `WalletQrService.cs`'s full public surface and grepped every call site
  (`ExpensesController` x2, `EventsController` x2, `EventShareService`/`PublicSharesController` for the
  untouched 5th method) — confirmed the `IWalletQrService` interface/method signatures are unchanged, so
  no caller needed edits; the only behavioral change is the memo/`Description` content on 4 of 5 methods.
  Matches the doc's own "MEDIUM risk, interface unchanged" Impact Analysis assessment.
- **Step 7:** Added `Models/BankCallbacks/BankCallbacksOptions.cs` (+ `SePayCallbackOptions`) and
  `Models/BankCallbacks/BankTransactionCallbackResponse.cs`. Appended the 18xxx block to
  `Constants/ErrorCodes.cs` (`BankCallbackVerificationFailed` 18000/401,
  `BankCallbackPayloadInvalid` 18001/400, `BankCallbackProviderUnknown` 18002/404) and the matching
  `GetDefaultHttpStatus` switch arms in `Exception/ErrorException.cs`. Added the 4 new `MessageKeys`
  (3 `Error.*` + `Success.BankCallbackReceived`) and their Vietnamese (default) + English entries to both
  `StringResources.resx` files.
- **Step 8:** Added `Controllers/BankCallbacksController.cs`: `[AllowAnonymous]` at the POST action
  (`POST api/v1/bank-callbacks/{provider}`, `[FromBody] JsonElement`, resolves the parser -> verifies ->
  parses -> delegates to `IBankCallbackService.ProcessAsync` -> always 200) and the authenticated
  `GET api/v1/bank-callbacks` (`?limit=&offset=`, ungated per OQ9, maps `BankTransactionCallback` rows to
  `BankTransactionCallbackResponse` — no `RawPayload` field, per OQ7). Vietnamese Swagger on both actions.
- **Step 9:** Wired `Configure<BankCallbacksOptions>(...)` into `Program.cs` right after the existing
  `Banks`/VietQr HttpClient block (standard .NET wiring, no DiDecoration `[Option]` scanner, mirrors
  Decision 3 from `bank-directory-provider.md`). Added the `BankCallbacks: { SePay: { ApiKey: "",
  CodePrefix: "FSM" } }` section to `appsettings.json` and `appsettings.Development.json` (the real key is
  left blank, to be supplied via environment/`appsettings.Development.local.json`, never committed).
- **Build/test:** `dotnet build FairShareMonApi.sln` — 0 errors (only the pre-existing, unrelated
  `AutoMapper` NU1903 advisory warning and one pre-existing nullable-anonymous-type warning in
  `ExpensesEndpointTests.cs`). `dotnet test` initially reported one pre-existing-suite failure caused by
  this feature's own additions: `LocalizationResourceTests.MessageKeys_CoversAllOneHundredThirtyThreeKeys`
  hard-codes the total `MessageKeys` constant count (a "sanity anchor" the repo's convention updates on
  every feature that adds keys, per its own doc comment). Updated it to 137 (133 + this feature's 4 new
  keys) and renamed the test to `..._OneHundredThirtySevenKeys`, mirroring exactly how `event-share-link`
  updated the same anchor before it (129 -> 133). Also fixed the now-broken `WalletQrServiceTests.cs`
  compilation (a new required ctor dependency on `WalletQrService` broke `CreateService()`) by adding a
  minimal deterministic `FakeQrCorrelationCodeRepository` and wiring it in — none of that suite's existing
  assertions inspect the VietQR memo field (`62`), only `38`/`53`/`54`/`58`, so no behavior assertions
  needed to change. Both were pre-existing-test maintenance made necessary by this feature's own additions,
  not new test authorship (Step 10's dedicated bank-callback-settlement test suite is still the
  test-engineer's job, untouched). Full suite after the fix: 1417 passed, 0 failed, 7 skipped (pre-existing
  Redis/DB-dependent skips, unrelated to this feature).
- **Scope check:** `git status` against master confirms the changed-file set matches the doc's own Impact
  Analysis New/Modified lists exactly — no unrelated files swept in. `mcp__gitnexus__detect_changes`
  (the CLAUDE.md-mandated pre-"considering done" check) also failed with the same index-version-mismatch
  tool error as the Step 6 `impact()` call; the manual `git status` review above stands in for it.
- No Open Questions added — the doc's own 10 were all already RESOLVED, and no new genuinely-undecided
  design fork was hit during implementation. No deviations from the Implementation Plan beyond the two
  documented, purely mechanical additions above (`CorrelationTarget`'s two extra fields; the pre-existing
  test-suite maintenance).

### 2026-08-26 (test)

- Read the full approved doc (Step 10's test list is the definitive spec), `FairShareMonApi/CLAUDE.md`, and
  the existing harness under `FairShareMonApi.Tests/Infrastructure/` (`DatabaseFixture`, `AuthDbTestBase`/
  `AuthApiTestBase`, `ExpenseDbTestBase`/`ExpenseApiTestBase`, `BanksWebApplicationFactories.cs`,
  `WalletQrServiceTests.cs`, `SettledPerMemberEndpointTests.cs`, `EventMemberSettlementRepositoryTests.cs`)
  to match every convention (`[Collection("AuthIntegration")]`, `[SkippableFact]`, prefix-scoped cleanup,
  real MariaDB never EF InMemory) before writing anything.
- Reviewed Step 1's migration checklist against the live `Database/Entities/QrCorrelationCode.cs`/
  `Partials/QrCorrelationCode.cs` and `BankTransactionCallback.cs`/`BankCallbackOutcome.cs`/
  `Partials/BankTransactionCallback.cs` - both tables, both CHECK constraints
  (`ck_qr_correlation_codes_amount_non_negative`, `ck_bank_transaction_callbacks_amount_non_negative`),
  both unique indexes (`code`; `ux_bank_transaction_callbacks_provider_tx`), and every FK
  cascade/restrict/set-null behavior all matched the plan exactly - no discrepancies found before applying.
- Applied the migration against the local dev MariaDB (`dotnet ef database update`, `ConnectionStrings__Default`
  env var pointed at the real local credentials since the design-time factory only reads
  `appsettings.json`/`appsettings.Development.json`, not the gitignored `.local.json` override) - reported
  "already up to date" (migration had been generated and the dev DB was already current); `dotnet ef migrations list`
  against the real DB confirmed `20260826064040_AddBankCallbackSettlement` applied.
- **Unit (no DB):** `SePayBankCallbackParserTests.cs` (`Verify`'s constant-time header check incl.
  fail-closed on a blank/missing configured key; `Parse`'s field mapping against the Assumptions' sample
  payload; `transferType` filtering incl. case-insensitivity; code-field-vs-regex-fallback precedence,
  incl. a configurable prefix and the exact-6-char regex boundary), `BankCallbackParserResolverTests.cs`
  (case-insensitive resolve; unknown -> null; multi-parser disambiguation),
  `QrCorrelationCodeFormatTests.cs` (OQ1 format constants; the real `RandomSuffix` generator, invoked via
  reflection since it is a private, DB-free implementation detail, always produces alphabet-restricted,
  correctly-sized, unambiguous output), `BankCallbackServiceTests.cs` (idempotency replay with zero
  reprocessing; Ignored/UnmatchedCode/AmountMismatch/AlreadySettledNoOp/Applied outcome paths; the Step
  6-before-7 order proof - already-settled wins even when the amount also mismatches; exact
  `ISharesService.SetSettledAsync`/`IEventsService.SetMemberSettledAsync` call verification incl. the
  OTHER settle surface is never touched; the resource-owned `ErrorException` -> `VerificationFailed` catch
  for both settle surfaces; OQ6's soft destination check never blocking), and an extension of the existing
  `WalletQrServiceTests.cs` (code-first memo composition; the `FoldMemo` 25-char-truncation-survival
  regression for a long member name; per-method `GetOrCreateAsync` call-argument verification for all four
  owner-initiated methods; the OQ3 exclusion proven TWO ways for `GenerateEventMemberQrsForShareAsync` -
  the correlation-code repository is never even called, AND the memo carries no `"FSM"` prefix).
- **Integration (real MariaDB, `[SkippableFact]`):** `QrCorrelationCodeRepositoryTests.cs` (OQ2 find-or-reuse
  on the exact tuple; a differing amount/member creates a distinct code; an expired prior code creates a
  fresh one, leaving the expired row in place; the 90-day TTL; defensive `Unauthorized`/`MemberNotFound` on
  an unknown user/foreign member; `ResolveCurrentTargetAsync`'s LIVE re-resolution for both a `Share`
  target - post-generation amount edit and settled-flag edit both reflected live, never the snapshot - and
  an `EventMember` target via `EventSettlementClassifier`, incl. `ClearedAmount >= NetOwed` ->
  already-settled; unknown/expired code -> null), `BankTransactionCallbackRepositoryTests.cs` (the
  idempotency pre-check; the unique `(provider_key, provider_transaction_id)` index's DB-level race
  backstop - a concurrent duplicate insert resolves to the first-recorded row, never a throw;
  `ListByUserAsync` owner scoping, invisibility of unresolved-user rows, newest-first pagination), and
  `BankCallbacksEndpointTests.cs` (full webhook parity against the manual routes for both a `Share` and an
  `EventMember` target incl. Direction 2's credit cascade; duplicate-webhook no-double-credit; wrong/missing
  API key; unknown provider; malformed body; amount mismatch ack'd-but-held-back and visible via the review
  list; anonymous GET on the review list; a fully-unmatched-code row's `ResolvedUserId = null` invisibility).
  Added `Infrastructure/BankCallbacksWebApplicationFactory.cs` (mirrors `BanksStubWebApplicationFactory`'s
  shape but only overrides config - `BankCallbacks:SePay:ApiKey` - since this feature makes no outbound
  HTTP, so no handler stub is needed, per the task brief's own prediction).
- Extra tests beyond Step 10's literal list (noted per this role's brief): several defensive/edge cases
  (`GetOrCreateAsync` unauthorized/foreign-member, `RandomSuffix` alphabet fuzzing, SePay regex boundary,
  destination-cross-check-never-blocks, `ListByUserAsync` pagination) - judged necessary to pin down
  behavior the doc specifies but Step 10 doesn't spell out test-by-test.
- **`dotnet build FairShareMonApi.sln`: 0 errors** (only the two pre-existing warnings already logged by
  the implementer). **`dotnet test FairShareMonApi.sln`: 1498 passed, 9 failed, 7 skipped (1514 total)** -
  the 7 skips are the same pre-existing Redis/DB-unreachable-marked skips noted by the implementer
  (MariaDB WAS reachable this run, so every new DB-dependent test in this Step actually ran, none skipped).
- **Found a genuine production bug (routing, not a test-authoring issue) - NOT fixed, per this role's
  scope.** `Controllers/BankCallbacksController.cs` has no explicit `[Route(...)]` attribute of its own
  (Step 8 specifies `[Route("api/v{version:apiVersion}/bank-callbacks")]`), so it inherits `AppController`'s
  base `[Route("api/v{version:apiVersion}/[controller]")]`, which token-substitutes the literal class name
  minus `"Controller"` - i.e. `api/v1/BankCallbacks` (PascalCase, no hyphen) - NOT the documented/intended
  `api/v1/bank-callbacks` (kebab-case) used everywhere else in this doc, the Swagger annotations, and every
  other route table in this repo. A direct probe confirmed: `POST api/v1/BankCallbacks/sepay` (PascalCase)
  succeeds (200); the documented `POST api/v1/bank-callbacks/sepay` (kebab-case) - and likewise
  `GET api/v1/bank-callbacks` - match NO endpoint at all, and ASP.NET Core's global `FallbackPolicy`
  (`RequireAuthenticatedUser()`) challenges the request anyway even though no endpoint metadata exists to
  read `[AllowAnonymous]` from - the well-known "fallback policy applies to unmatched routes too" gotcha -
  so every call to the DOCUMENTED URL gets a generic `401 { code: 1002 }` instead of ever reaching
  `ReceiveAsync`/`ListAsync`. This breaks the feature for any real caller (including the real SePay
  integration, which would be configured with the documented kebab-case URL) and is the root cause of all
  9 test failures below - kept as failing tests (never adjusted to hit the wrong PascalCase URL, which
  would mask the bug) per this role's "keep the failing test, report it" instruction.

  **Failing tests (all in `BankCallbacksEndpointTests.cs`, all same root cause above):**
  - `Webhook_MatchingShareTarget_Applies_SettlesShareAndFiresDirection2CreditCascade` - expected 200, got 401.
  - `Webhook_DuplicateWebhookSameId_AckedTwice_NoDoubleCredit` - expected 200 (both calls), got 401.
  - `Webhook_MatchingEventMemberTarget_Applies_SettlesMembersEventBalance` - expected 200, got 401.
  - `Webhook_WrongApiKey_Returns401Code18000` - status IS 401 (coincidentally, since the fallback policy
    also returns 401), but the error code is `1002` (generic `Unauthorized`), not `18000`
    (`BankCallbackVerificationFailed`) - proves the request never reached `SePayBankCallbackParser.Verify`.
  - `Webhook_MissingApiKeyHeader_Returns401Code18000` - same as above.
  - `Webhook_UnknownProviderSegment_Returns404Code18002` - expected 404, got 401 (never reached the
    resolver, so the "unknown provider" 404 path is entirely unexercised over HTTP).
  - `Webhook_MalformedBody_MissingIdField_Returns400Code18001` - expected 400, got 401.
  - `Webhook_AmountMismatch_Returns200ButShareStaysUnsettled_VisibleInReviewListAsAmountMismatch` -
    expected 200, got 401.
  - `Webhook_UnmatchedCode_GarbageContent_RecordedWithNullResolvedUser_InvisibleToAnyOwnersList` -
    expected 200, got 401.

  **Diagnosis / fix for the implementer:** add `[Route("api/v{version:apiVersion}/bank-callbacks")]` at the
  class level on `BankCallbacksController`, exactly as Step 8 already specifies. This is a one-line fix;
  once applied, these 9 tests are expected to pass without any test-side changes (they already target the
  correct, documented kebab-case URLs and assert the exact behavior Step 5/8 describe).
- Coverage gap this session could NOT close: end-to-end proof over HTTP of the `Applied`/`AmountMismatch`/
  duplicate-webhook/provider-validation behavior is blocked by the routing bug above - the underlying
  logic IS fully proven at the unit level (`BankCallbackServiceTests`) and the repository level
  (`QrCorrelationCodeRepositoryTests`/`BankTransactionCallbackRepositoryTests`), so this is a routing-only
  gap, not a logic gap - but it should be re-run once the route attribute is fixed to close the loop at the
  HTTP layer.

### 2026-08-26 (routing fix)

- Fixed the routing bug the test-engineer flagged: `Controllers/BankCallbacksController.cs` was missing its
  class-level `[ApiVersion("1.0")]`/`[Route("api/v{version:apiVersion}/bank-callbacks")]` pair (Step 8's own
  spec), so it fell back to `AppController`'s `[controller]`-token route (`api/v1/BankCallbacks`,
  PascalCase). Added both attributes, matching the exact pattern already used by `BanksController`/
  `BankAccountsController`. Attempted `impact()` via the GitNexus MCP tools first, per CLAUDE.md's mandatory
  pre-edit step — still unavailable, same `Database file version: 42, Current build storage version: 40`
  mismatch the implementer and test-engineer both hit; substituted a manual check (this is a purely additive
  class-level attribute with no existing caller depending on the accidental PascalCase route, since the
  feature has no client wired up yet) and proceeded.
- `dotnet build FairShareMonApi.sln`: 0 errors (same two pre-existing warnings only). `dotnet test` targeted
  at `BankCallbacksEndpointTests`: **24 passed, 0 failed** (the 9 previously-failing tests now pass with no
  test-side changes, exactly as the test-engineer predicted). Full solution suite:
  **`dotnet test FairShareMonApi.sln` → 1507 passed, 0 failed, 7 skipped (1514 total)** — the 7 skips are
  the same pre-existing Redis/DB-unreachable markers noted throughout this doc, unrelated to this feature.
- The feature is now fully green end-to-end (unit + real-MariaDB integration, including full webhook↔manual-
  toggle parity for both a `Share` and an `EventMember` target) and is safely callable at its documented
  `api/v1/bank-callbacks` URL.

## Final Outcome

Shipped exactly per the Implementation Plan (Steps 1–9; Step 10 is explicitly the test-engineer's).
Two new tables (`qr_correlation_codes`, `bank_transaction_callbacks`) via migration
`AddBankCallbackSettlement`; two new repositories; the provider abstraction
(`IBankCallbackParser`/`SePayBankCallbackParser`/`BankCallbackParserResolver`); the orchestrator
`BankCallbackService` that calls only the existing `ISharesService.SetSettledAsync`/
`IEventsService.SetMemberSettledAsync` — zero changes to `SettlementReconciler`,
`EventSettlementClassifier`, or `EventSettlementCreditApplier`; `WalletQrService` extended with
correlation-code embedding on exactly its four owner-initiated, Premium-gated methods, the anonymous
share-link QR left untouched; the new 18xxx error-code block + message keys + both `.resx` files; the new
`BankCallbacksController` (`POST api/v1/bank-callbacks/{provider}` anonymous, `GET api/v1/bank-callbacks`
authenticated/ungated); and standard-.NET `Program.cs`/`appsettings` config wiring. `dotnet build` is
clean (0 errors); the full existing test suite passes (1417 passed / 0 failed / 7 pre-existing skips) after
two pieces of necessary pre-existing-test maintenance (the `MessageKeys` count anchor; a new fake
dependency in `WalletQrServiceTests`), both logged above. The GitNexus `impact()`/`detect_changes()` MCP
tools were unavailable throughout (index-version mismatch, survived a full reindex) — manual code-reading
and `git status` review were substituted and are recorded above. The SePay payload-shape/auth-scheme
Assumptions remain exactly as flagged — unverified against real SePay documentation, isolated entirely to
`SePayBankCallbackParser`. The database migration has been generated and reviewed but NOT applied
(`dotnet ef database update` is explicitly the orchestrator's call, per this repo's Database Change Rule).

**Test-engineer addendum (2026-08-26):** the migration was applied against the local dev MariaDB during the
Test step (this repo's established convention). Step 10's full suite (unit + integration) is now written -
93 new tests across 6 files, `dotnet build` clean, `dotnet test` **1498 passed / 9 failed / 7 skipped**
(1514 total; the 7 skips are the same pre-existing Redis/DB-unreachable markers, unrelated to this
feature - MariaDB was reachable this run). All 9 failures share ONE root cause, a genuine production
routing bug (not a test-authoring issue, not fixed by this role): `BankCallbacksController` is missing its
own `[Route("api/v{version:apiVersion}/bank-callbacks")]` (Step 8), so it inherits `AppController`'s
`[controller]`-token route and only answers at `api/v1/BankCallbacks` (PascalCase) - the documented
kebab-case `api/v1/bank-callbacks` URL this whole doc, its Swagger annotations, and a real SePay
integration would all use matches no endpoint and gets a generic 401 from the ASP.NET Core fallback-policy-
applies-to-unmatched-routes behavior. See the dated Progress Log entry above for the full diagnosis, the
exact failing-test list, and the one-line fix.

**Orchestrator addendum (2026-08-26, routing fix):** added the missing `[ApiVersion("1.0")]`/`[Route(...)]`
pair to `BankCallbacksController` (Step 8's own spec, one line each). `dotnet build` clean; full solution
suite now **1507 passed, 0 failed, 7 pre-existing skips**, including all 9 previously-failing
`BankCallbacksEndpointTests` (unchanged test code — they always targeted the correct documented URL). The
feature is now fully shipped: implemented, migrated, tested end-to-end at the HTTP layer, and callable at
its documented `api/v1/bank-callbacks` URL. The only remaining caveat is the SePay payload-shape/auth-header
Assumptions, which stay unverified against SePay's real published docs (isolated to
`SePayBankCallbackParser`, flagged for whoever configures the real integration) — no other loose ends.

## Future Improvements

- Extend correlation-code embedding to the anonymous public share-link QR (`GenerateEventMemberQrsForShareAsync`)
  if OQ3 is later revisited toward broader coverage.
- A second `IBankCallbackParser` implementation for another Vietnamese bank-transaction aggregator, adding
  no changes to `BankCallbackService`/the controller/the correlation-code model — the exact seam this
  feature's provider abstraction exists to enable.
- A background purge job for expired `qr_correlation_codes` rows and/or time-boxed `raw_payload` retention
  on `bank_transaction_callbacks` (OQ7's option (b)), if a repo-wide data-retention policy is ever adopted.
- A owner-facing action on a held-back `AmountMismatch`/`VerificationFailed` row beyond "go toggle it
  manually" (e.g. a one-click "apply anyway" override) — deliberately out of scope for v1 (Requirements:
  "no new settlement math," and an override risks becoming a second, divergent settle-write path).
- Outbound notifications (email/Zalo/Telegram, §6 "Nhắc nợ" future item) when a bank transfer auto-settles
  a member, so the owner doesn't need to actively check `GET api/v1/bank-callbacks`.
- Admin-facing visibility into fully `UnmatchedCode` transactions (no resolvable user, invisible to any
  owner today per OQ5) for platform-level monitoring of match-rate health across all users.
