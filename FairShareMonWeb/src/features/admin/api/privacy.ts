/**
 * Privacy-boundary tripwire (M8 OQ2a, layer 3 of the R10 defense-in-depth).
 *
 * The backend already guarantees admin endpoints return ONLY account metadata +
 * tier-grant/revenue data (R10, §4.1). This is a cheap DEV-only belt: every admin
 * response is deep-scanned for a ledger key; if one ever appears (a future backend
 * change leaking a field), `assertNoLedgerKeys` throws in `import.meta.env.DEV`
 * so it surfaces immediately in development/tests. In production it is a no-op —
 * we never withhold data that did arrive, we only trip the alarm in DEV.
 *
 * Layers 1 + 2 (typed admin DTOs limited to account/grant shapes; a dedicated
 * privacy test asserting no ledger key ever reaches the DOM) live in `types.ts`
 * and the web-test-engineer's suite respectively.
 */

/**
 * Ledger structural keys that must NEVER appear in an admin payload. Deliberately
 * excludes `amount`/`total`/`currency` — those are legitimate tier-grant/revenue
 * fields. It targets the ledger ENTITIES + their cross-links (a member/expense/
 * event/share/bank-account reference), which have no business in the admin scope.
 */
export const LEDGER_KEYS: readonly string[] = [
  "members",
  "member",
  "expenses",
  "expense",
  "events",
  "event",
  "eventUuid",
  "eventName",
  "shares",
  "share",
  "shareUuid",
  "bankAccounts",
  "bankAccount",
  "payerMemberId",
  "payerMemberUuid",
  "payerMemberName",
  "categoryUuid",
  "categoryName",
  "expenseTime",
  "isSettled",
  "settledAt",
];

const LEDGER_KEY_SET = new Set(LEDGER_KEYS);

/**
 * Recursively assert no forbidden ledger key appears anywhere in `value`. Throws
 * in DEV only; returns `value` unchanged so it can wrap a response inline:
 * `return assertNoLedgerKeys(await api.get(...))`.
 */
export function assertNoLedgerKeys<T>(value: T): T {
  if (import.meta.env.DEV) scan(value, "response");
  return value;
}

function scan(value: unknown, path: string): void {
  if (value === null || typeof value !== "object") return;
  if (Array.isArray(value)) {
    for (let i = 0; i < value.length; i += 1) scan(value[i], `${path}[${i}]`);
    return;
  }
  for (const [key, child] of Object.entries(value)) {
    if (LEDGER_KEY_SET.has(key)) {
      throw new Error(
        `[admin privacy boundary] forbidden ledger key "${key}" found at ${path}.${key} — admin responses must carry account metadata + tier-grant data only (R10).`,
      );
    }
    scan(child, `${path}.${key}`);
  }
}
