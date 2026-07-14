# The-ideal.md Feature-Spec Rewrite

Restore `The-ideal.md` (emptied by the reset commit `3431eaf`) and rewrite it as a pure feature/use-case specification, removing all implementation detail.

## Objective

Turn `The-ideal.md` from a technical design doc (schema, endpoints, SQL, auth mechanics) into a *what*-only document: features, use cases, and business rules. Implementation detail lives in CLAUDE.md / AGENTS.md / rules.md / planning docs instead.

## Background

- The original technical spec was written at commit `6b19f01`, then emptied by `3431eaf` ("reset"). Restored 2026-07-10 via `git checkout 6b19f01 -- The-ideal.md`.
- A review of the restored spec (this session) found ~18 issues: staleness vs. decided stack (Memcached→Redis, UUID-vs-ulong IDs), missing endpoints, MariaDB-incompatible unique constraints, and feature-level ambiguities.
- User direction: "remove all detail implementation, keep only feature description, use cases."

## Requirements

- Keep Vietnamese (original language; matches project convention).
- Preserve every specified behavior at feature level: registration seeding, owner member, default category, soft-delete semantics, voucher+records atomicity, batch lifecycle/window rule, balance model, audit immutability, stats, export, future features.
- Remove: table schemas, SQL, endpoint lists, HTTP codes, token/hash/cache mechanics, tech-stack section, pagination params, index/partition advice.
- Surface the *feature-level* ambiguities from the review as an explicit "Điểm còn mở" section (batch reopen/delete/remove-voucher, balance scope outside batches, deleted members as payer/participant, export format, deleted-tag link removal vs. category behavior, the auto-inserted 0-amount owner record).
- Point readers to commit `6b19f01` for the old technical version.

## Open Questions

- ~~The 6 "Điểm còn mở" items inside the new spec~~ — 5 of 6 answered by user 2026-07-10 and folded into the spec; tag-deletion behavior stays open (now open point #1, three options A/B/C — analysis below). Two new open points added with the bonus features: QR amount semantics (#2) and the tier/limit matrix (#3).

### Tag-deletion options (open point #1) — impact analysis — **RESOLVED: user chose (B), 2026-07-10**

- **(A) Gỡ liên kết khỏi mọi phiếu** (original spec): simplest to build (delete rows in the link table). Impact: silently mutates history — old vouchers, exports, and past-period tag filters lose the tag with no trace (audit doesn't cover tag links); contradicts rule 7's "lịch sử bất khả xâm phạm" spirit and is inconsistent with category behavior.
- **(B) Giữ liên kết lịch sử như danh mục**: deleted tag stays visible on old vouchers, not selectable for new ones. Impact: consistent with rule 7 and the category precedent; costs a name-collision policy when re-creating a same-named tag (recommend reactivate-the-old-one) and queries must distinguish active vs historical tags. **Recommended.**
- **(C) Chặn xóa khi nhãn còn đang gắn**: strongest integrity, no history questions. Impact: worst UX — a widely-used tag can effectively never be removed from pickers without manually detaching it everywhere; ends up needing an "archive" state which is (B) by another name.

## Assumptions

- DB-level review findings (filtered-unique-index workaround, exactly-one-default enforcement) are implementation concerns → deliberately *not* in the spec; they must be addressed in the auth/members/categories feature planning docs.
- Auth mechanism (opaque token, SHA-256 whitelist, Redis) is now documented solely in CLAUDE.md / AGENTS.md / rules.md; the spec only states session behavior (expiring sessions, renewal, revoke-all on password change).

## Implementation Plan

1. Restore file from `6b19f01`. ✔
2. Review; report findings to user. ✔
3. Rewrite as feature spec (overview + scenario, concepts table, features & use cases, mandatory business rules, open points, future features). ✔
4. Sync references: CLAUDE.md (spec description, endpoint-groups wording), AGENTS.md (domain-flows attribution). ✔
5. Mark the "restore The-ideal.md" item done in project-initialization.md. ✔

## Impact Analysis

- **Documentation only.** No code, DB, or API changes.
- CLAUDE.md/AGENTS.md wording updated where they described The-ideal.md as containing data model/endpoints.
- Future feature planning docs must now define their own endpoint/schema design (spec no longer prescribes it) and resolve the relevant "Điểm còn mở" items first.

## Progress Log

### 2026-07-10

- Restored `The-ideal.md` from `6b19f01` (23.6 KB technical spec).
- Reviewed it: staleness (Memcached, UUID strategy), missing detail/change-password/batch-management endpoints, MariaDB filtered-unique-index problem, audit blind spots, ambiguities (balance scope, close semantics, money precision, export format).
- Rewrote as Vietnamese feature/use-case spec per user direction; feature-level ambiguities recorded as "Điểm còn mở" 1–6; technical version referenced at `6b19f01`.
- Synced CLAUDE.md and AGENTS.md references; updated project-initialization.md.
- User answered open points 1–4 & 6 (no batch reopen; delete-batch/remove-voucher only while OPEN; never auto-close; balance batch-only + mark-as-paid for loose/debt vouchers; deleted members blocked for new data but shown on old; export CSV-first with open design; keep the 0đ owner record) — folded into sections 3.2–3.9 and rules 4/8. Point 5 (deleted tags) kept open with options A/B/C per user request.
- Added two feature areas from user: **Ví QR** (bank-account CRUD + default) & **QR chuyển khoản** (per voucher; per batch only after close, all QRs merged into one image) as 3.10, and **user tiers** Primary/Regular (extended features + unlimited vs. basic + quotas; limits never touch existing data) as 3.11 + rule 9. New open points: QR amount semantics (#2, proposed per-member share for vouchers / negative balance for batches) and the tier feature/limit matrix (#3).
- Interpretation notes (flagged to user): "đánh dấu đã trả" specced at voucher level (per-member settlement listed as future improvement); QR destination account = an account from the owner's ví QR (default unless picked), so settlements flow to the owner.
- **Terminology decision** (user challenged "voucher" as misnamed; all four of my recommendations accepted via question prompt): **expense** replaces voucher, **share** replaces record, **event** replaces batch, tiers renamed **Premium/Free** (was Primary/Regular); additionally "Ví QR" → **Ví (wallet)** holding **bank accounts**, and the settled flag's English term fixed as **settled**. Applied across The-ideal.md, CLAUDE.md, AGENTS.md, rules.md, project-initialization.md; future tables named `expenses`, `expense_shares`, `expense_events`, `expense_tags`, `bank_accounts`. Terms recorded in the spec's section 5 and CLAUDE.md's Project section.
- User answered the remaining three points: **#1 → option B** (deleted tags keep historical links like categories; recreating a same-named tag reactivates the old one) — folded into 3.4 and rule 8, section 5's A/B/C menu removed; **#2 →** batch = one QR per indebted member merged into one image, voucher = manually-triggered single QR representing the whole voucher (amount = voucher total); **#3 →** tier upgrade is **by payment**, with pricing/gateway/renewal details deliberately left open for the tier feature's own planning doc. Section 5 renamed to "Các quyết định đã chốt & chi tiết để mở có chủ đích" — the spec has no blocking open questions left.

## Final Outcome

`The-ideal.md` is now a feature-only specification with **all feature-level decisions resolved**: overview & scenario, 11 concept definitions, 11 feature areas with use cases (including Ví QR/QR chuyển khoản and Primary/Regular tiers with payment-based upgrade), 9 mandatory business rules, a decision-record section (5) listing what was settled and which details are deliberately deferred (Regular quota numbers, payment mechanics), and future features. All implementation detail removed; the old technical spec is recoverable at `6b19f01`.

## Future Improvements

- Per-member settlement marking (finer than voucher-level "đã trả") — listed in the spec's future features.
- The deliberately-deferred details (Regular quota numbers, extended-feature list finalization, payment pricing/gateway/renewal) get decided in the tier/wallet feature planning docs.
