# Agent Dev Team & Orchestration Playbook

Set up a persistent agent dev team that implements FairShareMonApi feature-by-feature from `The-ideal.md`, with a user checkpoint after every feature.

## Objective

Define reusable role agents (planner, implementer, test engineer, reviewer) plus the orchestration protocol the main Claude Code session runs per feature, so every feature cycle follows the repo's Clarification-First and planning-doc-first rules mechanically.

## Background

- `The-ideal.md` (feature spec) is final; conventions live in `CLAUDE.md` / `AGENTS.md` / `.agents/rules/rules.md`.
- The infrastructure init plan (`planning/project-initialization.md`) is approved and unblocked; code is still the bare webapi template.
- User decisions 2026-07-13: scope = **API first, Web later**; team form = **persistent agents in `.claude/agents/` + orchestration**; cadence = **checkpoint per feature**.

## Requirements

- Four role agents defined at the workspace root `FairShareMon/.claude/agents/` (one level above this repo, so the same team later serves `FairShareMonWeb`):
  - `feature-planner` — drafts `/planning/<feature>.md` (template-compliant), read/plan only, never assumes: unknowns become Open Questions.
  - `api-implementer` — builds strictly per the approved doc; conventions embedded in its prompt; keeps the doc's Progress Log updated.
  - `test-engineer` — owns `FairShareMonApi.Tests`; unit + real-MariaDB integration tests (skippable, rollback-isolated); never fixes production code.
  - `code-reviewer` — read-only audit of the diff against business rules + conventions; blocking vs nit findings.
- Agents cannot ask the user directly → every unknown is written into the planning doc's Open Questions and surfaced at the checkpoint by the orchestrator.
- Git commits are made only by the orchestrator, in this repo.

## Orchestration protocol (per feature)

1. **Plan** — `feature-planner` drafts/updates `/planning/<feature>.md`.
2. **Clarify** — orchestrator brings Open Questions to the user; answers recorded in the doc's Decision Log.
3. **Implement** — `api-implementer` builds per the approved doc (incl. authoring the EF migration; `database update` only after review).
4. **Test** — `test-engineer` adds and runs tests; build + full suite must pass (DB tests may skip only when MariaDB is unreachable).
5. **Review** — `code-reviewer` audits the diff; blocking findings loop back to `api-implementer`; re-review until clean.
6. **Close** — Progress Log + Final Outcome updated; git commit; **user checkpoint** approves before the next feature starts.

## Roadmap (each item = one full cycle + checkpoint)

1. **Infrastructure init** — execute the approved `project-initialization.md` (planning already done → starts at step 3 of the protocol). Needs from user: real MariaDB credentials for `ConnectionStrings:Default`.
2. **Auth** — `users`/`auth_tokens` + first migration, register/login/refresh/logout, BCrypt, opaque-token whitelist (Redis + DB), delete DI stubs, password change revokes all tokens.
3. **Members** — CRUD + soft delete; owner-representative member auto-created on register. **(implementation done 2026-07-14)** — closed the M2 owner-rep backfill obligation and established the shared registration-bootstrap seam (`IRegistrationBootstrapStep` + `IUserRepository.CreateWithBootstrapAsync`) that M4 extends for suggested categories.
4. **Categories + Tags** — CRUD, unique active names, default-category invariant, tag reactivation on name reuse. **(implementation done 2026-07-14)** — reused the M3 `IRegistrationBootstrapStep` seam (`SuggestedCategoriesBootstrapStep` + a mirrored idempotent `SuggestedCategoriesBackfillHostedService`) for suggested-category seeding ("Ăn uống" default + 4 more); shipped the default-category always-exactly-one/not-deletable invariant with an atomic user-scoped swap, and accent/case-insensitive reactivation-on-name-reuse for both categories and tags.
5. **Expenses + Shares + Audit** — atomic expense+shares, share sub-routes, settled flag, filters, immutable audit log. **(implementation done 2026-07-14)** — closed the M4 OQ10 expense-linking deferral (built the expense↔category FK + expense↔tag join table + §4.2/§4.8 cross-user link validation); shipped the atomic expense+shares+audit transaction (one `ExecuteTransactionAsync`, hard-delete cascade with surviving audit), the immutable per-entity snapshot audit log (no-FK refs, no-op suppression, settled unaudited), the derived expense total, owner-rep 0đ auto-inject + protection (7002/7003), and the codebase's first money CHECK constraint (`ck_shares_amount_non_negative`, `decimal(18,2)`). Leaves the event relationship as the M6 seam (no `event_id`; dedicated `SetSettledAsync` for the closed-event exception).
6. **Events** — lifecycle, expense-date-within-event validation, one-way close, closed-event write blocking (settled exception).
7. **Debt balance + Stats** — per-event balance, overview + per-category stats.
8. **Export CSV** — expense + event exports, format-extensible design.
9. **Wallet + QR** — bank accounts, default account, per-expense QR, per-event composite QR image. Open: QR standard (likely VietQR) + image generation library.
10. **Tiers (Premium/Free)** — limits + paid upgrade. Open (deliberately, per spec §5): concrete Free limits, pricing/gateway/expiry.

## Open Questions

- (Milestone 1, blocking Step 3 of the init plan) Real MariaDB connection credentials — asked at kickoff.
- (Milestone 9) QR standard + image generation approach.
- (Milestone 10) Free-tier limit numbers; payment mechanism details.

## Assumptions

- Agent definitions live outside this repo (workspace root) and are therefore unversioned until the user decides whether `FairShareMon/` becomes its own repo — flagged, accepted for now.
- `FairShareMonWeb` untouched until the API contract stabilizes; the team gains frontend roles then.

## Impact Analysis

- **APIs/Database/Services:** none by this work item itself — it only creates agent definitions and this doc. All product impact flows through the per-feature cycles.
- **Documentation:** this doc; per-feature docs follow.

## Decision Log

### Decision
Four-role team (planner / implementer / test engineer / reviewer); orchestrator (main session) handles clarification, git, and checkpoints.

### Reason
Maps 1:1 onto the repo's mandatory process (planning doc → clarify → implement → verify) while keeping the pieces that need user interaction and repo-state authority in the main session, since subagents cannot prompt the user.

### Alternatives Considered
- Single do-everything agent per feature — loses the independent review gate.
- Separate doc-keeper agent — folded into planner (pre) and implementer/test-engineer (progress log) to avoid a low-value handoff.

### Decision
Migration apply timing (2026-07-13, resolves the reviewer's Milestone-2 process note): during **Implement**, the api-implementer authors the migration, self-reviews it, and applies it to the **local dev DB** when the orchestrator's assignment says so. The code-reviewer still audits the migration in **Review**; a defect found there produces a corrective migration (or a dev-DB reset) before the milestone closes. For any non-dev environment, applying always waits for Review.

### Reason
Integration/endpoint tests need the schema live during the Test step, which precedes Review in the protocol; the dev DB is disposable, so the cost of a post-review correction is near zero while blocking Test on Review would serialize the whole cycle.

### Alternatives Considered
- Apply only after Review (the auth plan's original wording) — would force Test to run before the tables exist or insert a second review round-trip per feature.

### Decision
Agent files at `FairShareMon/.claude/agents/` (workspace root), not inside this repo.

### Reason
Claude Code loads project agents from the working directory's `.claude/agents/`; sessions run at the workspace root, and the Web app will share the team later.

### Alternatives Considered
- Inside this repo (`FairShareMonApi/.claude/agents/`) — versioned, but not discovered when the session cwd is the workspace root.

## Progress Log

### 2026-07-13

- User approved scope (API first), team form (persistent agents + orchestration), cadence (checkpoint per feature).
- Created `feature-planner`, `api-implementer`, `test-engineer`, `code-reviewer` under `FairShareMon/.claude/agents/`; wrote this playbook.
- Next: Milestone 1 (infrastructure init) via the protocol, starting at Implement since `project-initialization.md` is already approved.
- **Milestone 1 executed end-to-end through the protocol** (implementer Steps 1–4 → test-engineer Step 5 → reviewer: 0 blocking + 1 should-fix + nits → implementer fix round → reviewer delta re-review: APPROVE → orchestrator Step 6 verification). Result: 42 tests (39 pass / 3 DB-skips), live boot verified. First full cycle of the team worked as designed.
- Environment note: MariaDB (3306) and Redis (6379) were both unreachable on this machine during the milestone — boot tolerates it by design; DB integration tests skip until the servers are started.

### 2026-07-14

- **Milestone 4 (Categories + Tags) completed the full protocol cycle:** planner drafted `planning/categories-and-tags.md` with 12 Open Questions → all 12 answered at the user checkpoint (11 at recommended option (a), OQ1 at option (b) — "Ăn uống" default seed set) → api-implementer built Steps 1–8 (entities + `AddCategoriesAndTags` migration applied to the dev DB, 4xxx/5xxx error blocks, repositories/services/validators/DTOs/controllers, the suggested-category bootstrap step on the shared M3 seam + a mirrored idempotent backfill hosted service) → test-engineer added 135 tests (unit + real-MariaDB integration + endpoint), **`dotnet test` 337/337 pass, 0 skipped**, deterministic, DB swept clean → code-reviewer **APPROVE, 0 blocking** (first pass, no fix loop; 4 non-blocking notes accepted).
- **Milestone 3 was also closed earlier this day** (implementation + tests + review APPROVE — see `planning/members.md`), establishing the shared registration-bootstrap seam that M4 reused.
- Committed by the orchestrator to `master` (feature commit follows this doc update).
- **Milestone 5 (Expenses + Shares + Audit) completed the full protocol cycle:** planner drafted `planning/expenses-shares-audit.md` with 20 Open Questions → all 20 answered at the user checkpoint (every recommended option (a); OQ9+OQ10 presented together) → api-implementer built Steps 1–10 (four entities + `AddExpensesSharesAndAudit` migration applied to the dev DB incl. the codebase's first CHECK constraint, 6xxx/7xxx error blocks + 8xxx reserved, three repositories + typed write results, pure `AuditLogFactory` + snapshots, `ExpensesService`/`SharesService`, profiles/DTOs/validators, `ExpensesController` with all ten `api/v1/expenses` routes; 43/43 live smoke) → test-engineer added 150 tests (suite 337 → **487**: unit + real-MariaDB integration + endpoint) which surfaced **2 production bugs** in audit no-op detection (ExpenseTime `DateTimeKind` mismatch; `Share.Amount` decimal-scale mismatch — false-positives violating OQ9) → api-implementer fix-loop (`AuditSnapshotCanonicalizer`: `SpecifyKind(Utc)` + `decimal.Round(v,2)`), **`dotnet test` 487/487 pass, 0 skipped**, deterministic, DB swept clean → code-reviewer **APPROVE, 0 blocking** (2 informational notes + 1 nit accepted). Closes the M4 OQ10 linking deferral; leaves the M6 event seam.

## Final Outcome

Team operational. Four role agents live in `FairShareMon/.claude/agents/` (loaded from the next session onward; this session ran the same role prompts on general-purpose agents). Milestone 1 (infrastructure) completed through the full protocol and committed. Checkpoint questions for the user: OQ4 `error.fields` envelope placement; optional rules.md `string? Uuid` → `string Uuid` one-word sync; start MariaDB/Redis to un-skip DB tests.

## Future Improvements

- Frontend roles (web-implementer, ui-reviewer) once `FairShareMonWeb` work starts.
- Consider making `FairShareMon/` a git repo (or moving agent files into a versioned location) so the team definitions are tracked.
