import { http, HttpResponse } from "msw";
import type {
  ChangePasswordRequest,
  LoginRequest,
  RegisterRequest,
  RefreshRequest,
} from "@/lib/api/types/auth";

/**
 * Envelope-shaped auth handlers. Used to run the app against mocks when the
 * backend/DB is unreachable (VITE_ENABLE_MOCKS) AND by the Vitest harness. They
 * exercise the REAL client (envelope unwrap, refresh, error codes) at the
 * network boundary — the `*` origin prefix matches both same-origin (browser)
 * and jsdom (localhost). A tiny in-memory store makes the flow demonstrable.
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: {
    code: number;
    message: string;
    fields?: Record<string, string[]>;
  } | null;
}

function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}

function fail(
  code: number,
  message: string,
  status: number,
  fields?: Record<string, string[]>,
) {
  return HttpResponse.json<Envelope>(
    { data: null, isSuccess: false, error: { code, message, fields } },
    { status },
  );
}

interface Profile {
  uuid: string;
  tier: string;
  role: string;
  createdAt: string;
}

// username → password. `demo` is a Free USER, `admin` is a Premium ADMIN, and
// `degraded` exercises the OQ3a non-401 `/auth/me` failure (valid tokens, but the
// profile fetch 500s → stays authenticated, degraded).
const users = new Map<string, string>([
  ["demo", "password123"],
  ["admin", "password123"],
  ["degraded", "password123"],
]);
// username → profile served by /auth/me. A user without a profile (e.g. `degraded`)
// makes /auth/me fail with a non-401 server error.
const profiles = new Map<string, Profile>([
  [
    "demo",
    {
      uuid: "uuid-demo",
      tier: "FREE",
      role: "USER",
      createdAt: "2026-01-01T00:00:00+00:00",
    },
  ],
  [
    "admin",
    {
      uuid: "uuid-admin",
      tier: "PREMIUM",
      role: "ADMIN",
      createdAt: "2026-01-01T00:00:00+00:00",
    },
  ],
]);
const validRefreshTokens = new Set<string>();
let lastLoggedInUser: string | null = null;

function rand(): string {
  return Math.random().toString(36).slice(2);
}

// --- Members store (mock backend) -----------------------------------------
interface MemberRecord {
  uuid: string;
  name: string;
  isOwnerRepresentative: boolean;
  isDeleted: boolean;
  createdAt: string;
}

/** Free-tier active-member cap enforced by this mock so 13000 is demonstrable. */
const FREE_MEMBER_LIMIT = 5;

// username → their members. Seeded lazily on first access.
const membersByUser = new Map<string, MemberRecord[]>();

function seedMembers(): MemberRecord[] {
  const base = "2026-01-01T00:00:00+00:00";
  return [
    {
      uuid: `m-${rand()}`,
      name: "Bạn (chủ sổ)",
      isOwnerRepresentative: true,
      isDeleted: false,
      createdAt: base,
    },
    {
      uuid: `m-${rand()}`,
      name: "An Nguyễn",
      isOwnerRepresentative: false,
      isDeleted: false,
      createdAt: base,
    },
    {
      uuid: `m-${rand()}`,
      name: "Bình Trần",
      isOwnerRepresentative: false,
      isDeleted: false,
      createdAt: base,
    },
    {
      uuid: `m-${rand()}`,
      name: "Cũ (đã xóa)",
      isOwnerRepresentative: false,
      isDeleted: true,
      createdAt: base,
    },
  ];
}

function getMembers(username: string): MemberRecord[] {
  let list = membersByUser.get(username);
  if (!list) {
    list = seedMembers();
    membersByUser.set(username, list);
  }
  return list;
}

/** Owner-rep first, then name A→Z (vi collation) — matches the backend order. */
function sortMembers(list: MemberRecord[]): MemberRecord[] {
  return [...list].sort((a, b) => {
    if (a.isOwnerRepresentative !== b.isOwnerRepresentative) {
      return a.isOwnerRepresentative ? -1 : 1;
    }
    return a.name.localeCompare(b.name, "vi");
  });
}

function toResponse(m: MemberRecord): MemberRecord {
  return { ...m };
}

/** Validate a member name the way the backend validator does (1–100, trimmed). */
function validateName(raw: unknown): string | { code: number } {
  const name = typeof raw === "string" ? raw.trim() : "";
  if (name.length === 0 || name.length > 100) return { code: 1001 };
  return name;
}

/** Extract the username seeded into the `access-<username>-...` bearer token. */
function usernameFromAuthHeader(authorization: string | null): string | null {
  if (!authorization?.startsWith("Bearer ")) return null;
  return authorization.slice("Bearer ".length).split("-")[1] ?? null;
}

// --- Categories store (mock backend) --------------------------------------
interface CategoryRecord {
  uuid: string;
  name: string;
  color: string;
  icon: string | null;
  isDefault: boolean;
  isDeleted: boolean;
  createdAt: string;
}

const HEX_COLOR = /^#[0-9A-Fa-f]{6}$/;

// username → their categories. Seeded lazily on first access (mirrors the
// backend registration bootstrap: 5 categories, "Ăn uống" default).
const categoriesByUser = new Map<string, CategoryRecord[]>();

function seedCategories(): CategoryRecord[] {
  const base = "2026-01-01T00:00:00+00:00";
  return [
    {
      uuid: `c-${rand()}`,
      name: "Ăn uống",
      color: "#F97316",
      icon: "🍜",
      isDefault: true,
      isDeleted: false,
      createdAt: base,
    },
    {
      uuid: `c-${rand()}`,
      name: "Đi lại",
      color: "#3B82F6",
      icon: "🚗",
      isDefault: false,
      isDeleted: false,
      createdAt: base,
    },
    {
      uuid: `c-${rand()}`,
      name: "Khách sạn",
      color: "#8B5CF6",
      icon: "🏨",
      isDefault: false,
      isDeleted: false,
      createdAt: base,
    },
    {
      uuid: `c-${rand()}`,
      name: "Mua sắm",
      color: "#EC4899",
      icon: "🛍️",
      isDefault: false,
      isDeleted: false,
      createdAt: base,
    },
    {
      uuid: `c-${rand()}`,
      name: "Khác",
      color: "#6B7280",
      icon: "⋯",
      isDefault: false,
      isDeleted: false,
      createdAt: base,
    },
    {
      uuid: `c-${rand()}`,
      name: "Giải trí (cũ)",
      color: "#14A074",
      icon: "🎬",
      isDefault: false,
      isDeleted: true,
      createdAt: base,
    },
  ];
}

function getCategories(username: string): CategoryRecord[] {
  let list = categoriesByUser.get(username);
  if (!list) {
    list = seedCategories();
    categoriesByUser.set(username, list);
  }
  return list;
}

/** Default first, then name A→Z (vi collation) — matches the backend order. */
function sortCategories(list: CategoryRecord[]): CategoryRecord[] {
  return [...list].sort((a, b) => {
    if (a.isDefault !== b.isDefault) return a.isDefault ? -1 : 1;
    return a.name.localeCompare(b.name, "vi");
  });
}

function categoryResponse(c: CategoryRecord) {
  return {
    uuid: c.uuid,
    name: c.name,
    color: c.color,
    icon: c.icon,
    isDefault: c.isDefault,
    isDeleted: c.isDeleted,
    createdAt: c.createdAt,
  };
}

// --- Tags store (mock backend) --------------------------------------------
interface TagRecord {
  uuid: string;
  name: string;
  isDeleted: boolean;
  createdAt: string;
}

const tagsByUser = new Map<string, TagRecord[]>();

function seedTags(): TagRecord[] {
  const base = "2026-01-01T00:00:00+00:00";
  return [
    {
      uuid: `t-${rand()}`,
      name: "Công tác",
      isDeleted: false,
      createdAt: base,
    },
    {
      uuid: `t-${rand()}`,
      name: "Du lịch",
      isDeleted: false,
      createdAt: base,
    },
    {
      uuid: `t-${rand()}`,
      name: "Sinh nhật (cũ)",
      isDeleted: true,
      createdAt: base,
    },
  ];
}

function getTags(username: string): TagRecord[] {
  let list = tagsByUser.get(username);
  if (!list) {
    list = seedTags();
    tagsByUser.set(username, list);
  }
  return list;
}

function sortTags(list: TagRecord[]): TagRecord[] {
  return [...list].sort((a, b) => a.name.localeCompare(b.name, "vi"));
}

function tagResponse(tag: TagRecord) {
  return {
    uuid: tag.uuid,
    name: tag.name,
    isDeleted: tag.isDeleted,
    createdAt: tag.createdAt,
  };
}

// --- Expenses store (mock backend) ----------------------------------------
interface ShareRecord {
  uuid: string;
  memberUuid: string;
  amount: number;
  note: string | null;
  createdAt: string;
}
interface ExpenseRecord {
  uuid: string;
  name: string;
  description: string | null;
  expenseTime: string;
  payerMemberUuid: string;
  categoryUuid: string;
  tagUuids: string[];
  isSettled: boolean;
  settledAt: string | null;
  shares: ShareRecord[];
  eventUuid: string | null;
  createdAt: string;
}
interface AuditRecord {
  uuid: string;
  entityType: "Expense" | "Share";
  entityUuid: string;
  expenseUuid: string;
  action: "Create" | "Update" | "Delete";
  before: unknown;
  after: unknown;
  createdAt: string;
}

const expensesByUser = new Map<string, ExpenseRecord[]>();
// Audit logs survive expense hard-delete — keyed by expenseUuid, per user.
const auditByUser = new Map<string, AuditRecord[]>();
let auditSeq = 0;

function getExpenses(username: string): ExpenseRecord[] {
  let list = expensesByUser.get(username);
  if (!list) {
    list = [];
    expensesByUser.set(username, list);
  }
  return list;
}
function getAudit(username: string): AuditRecord[] {
  let list = auditByUser.get(username);
  if (!list) {
    list = [];
    auditByUser.set(username, list);
  }
  return list;
}

function memberByUuid(username: string, uuid: string): MemberRecord | undefined {
  return getMembers(username).find((m) => m.uuid === uuid);
}
function categoryByUuid(
  username: string,
  uuid: string,
): CategoryRecord | undefined {
  return getCategories(username).find((c) => c.uuid === uuid);
}

function auditNow(): string {
  // Strictly increasing timestamps so the history renders in a stable order.
  return new Date(Date.now() + auditSeq++).toISOString();
}

function expenseSnapshot(username: string, e: ExpenseRecord) {
  const payer = memberByUuid(username, e.payerMemberUuid);
  const category = categoryByUuid(username, e.categoryUuid);
  const tags = e.tagUuids
    .map((uuid) => getTags(username).find((tg) => tg.uuid === uuid))
    .filter((tg): tg is TagRecord => Boolean(tg))
    .map((tg) => ({ uuid: tg.uuid, name: tg.name }));
  return {
    uuid: e.uuid,
    name: e.name,
    description: e.description,
    expenseTime: e.expenseTime,
    payerMemberUuid: e.payerMemberUuid,
    payerMemberName: payer?.name ?? "",
    categoryUuid: e.categoryUuid,
    categoryName: category?.name ?? "",
    tags,
    isSettled: e.isSettled,
  };
}
function shareSnapshot(username: string, expenseUuid: string, s: ShareRecord) {
  const member = memberByUuid(username, s.memberUuid);
  return {
    uuid: s.uuid,
    expenseUuid,
    memberUuid: s.memberUuid,
    memberName: member?.name ?? "",
    amount: s.amount,
    note: s.note,
  };
}
function pushAudit(
  username: string,
  entry: Omit<AuditRecord, "uuid" | "createdAt">,
) {
  getAudit(username).push({
    ...entry,
    uuid: `al-${rand()}`,
    createdAt: auditNow(),
  });
}

function memberResponse(username: string, uuid: string) {
  const m = memberByUuid(username, uuid);
  return m
    ? {
        uuid: m.uuid,
        name: m.name,
        isOwnerRepresentative: m.isOwnerRepresentative,
        isDeleted: m.isDeleted,
        createdAt: m.createdAt,
      }
    : {
        uuid,
        name: "(không rõ)",
        isOwnerRepresentative: false,
        isDeleted: true,
        createdAt: "2026-01-01T00:00:00+00:00",
      };
}
function shareResponse(username: string, s: ShareRecord) {
  return {
    uuid: s.uuid,
    member: memberResponse(username, s.memberUuid),
    amount: s.amount,
    note: s.note,
    createdAt: s.createdAt,
  };
}
function expenseTotal(e: ExpenseRecord): number {
  return e.shares.reduce((sum, s) => sum + s.amount, 0);
}
function eventLinkage(username: string, eventUuid: string | null) {
  if (!eventUuid) return { eventName: null, eventIsClosed: null };
  const ev = getEvents(username).find((x) => x.uuid === eventUuid);
  return {
    eventName: ev?.name ?? null,
    eventIsClosed: ev?.isClosed ?? null,
  };
}
function expenseResponse(username: string, e: ExpenseRecord) {
  return {
    uuid: e.uuid,
    name: e.name,
    description: e.description,
    expenseTime: e.expenseTime,
    total: expenseTotal(e),
    category: categoryResponse(
      categoryByUuid(username, e.categoryUuid) ?? seedCategories()[0],
    ),
    payer: memberResponse(username, e.payerMemberUuid),
    isSettled: e.isSettled,
    settledAt: e.settledAt,
    shares: e.shares.map((s) => shareResponse(username, s)),
    tags: e.tagUuids
      .map((uuid) => getTags(username).find((tg) => tg.uuid === uuid))
      .filter((tg): tg is TagRecord => Boolean(tg))
      .map(tagResponse),
    eventUuid: e.eventUuid,
    ...eventLinkage(username, e.eventUuid),
    createdAt: e.createdAt,
  };
}
function expenseSummary(username: string, e: ExpenseRecord) {
  return {
    uuid: e.uuid,
    name: e.name,
    expenseTime: e.expenseTime,
    total: expenseTotal(e),
    category: categoryResponse(
      categoryByUuid(username, e.categoryUuid) ?? seedCategories()[0],
    ),
    payer: memberResponse(username, e.payerMemberUuid),
    isSettled: e.isSettled,
    settledAt: e.settledAt,
    tagNames: e.tagUuids
      .map((uuid) => getTags(username).find((tg) => tg.uuid === uuid)?.name)
      .filter((n): n is string => Boolean(n)),
    shareCount: e.shares.length,
    eventUuid: e.eventUuid,
    ...eventLinkage(username, e.eventUuid),
    createdAt: e.createdAt,
  };
}

// --- Events store (mock backend) ------------------------------------------
interface EventRecord {
  uuid: string;
  name: string;
  description: string | null;
  /** Whole-day bounds (00:00:00.000Z .. 23:59:59.999Z), mirroring the backend. */
  startDate: string;
  endDate: string;
  isClosed: boolean;
  closedAt: string | null;
  createdAt: string;
}

/** Free-tier open-event cap enforced by this mock so 13001 is demonstrable. */
const FREE_OPEN_EVENT_LIMIT = 3;

const eventsByUser = new Map<string, EventRecord[]>();

function getEvents(username: string): EventRecord[] {
  let list = eventsByUser.get(username);
  if (!list) {
    list = [];
    eventsByUser.set(username, list);
  }
  return list;
}

function eventByUuid(username: string, uuid: string): EventRecord | undefined {
  return getEvents(username).find((e) => e.uuid === uuid);
}

/** Count of expenses assigned to the event. */
function eventExpenseCount(username: string, uuid: string): number {
  return getExpenses(username).filter((e) => e.eventUuid === uuid).length;
}

/** Normalize an incoming noon-anchored ISO to whole-day UTC bounds. */
function dayBounds(iso: string): { start: string; end: string } {
  const day = iso.slice(0, 10);
  return { start: `${day}T00:00:00.000Z`, end: `${day}T23:59:59.999Z` };
}

function eventSummaryResponse(username: string, ev: EventRecord) {
  return {
    uuid: ev.uuid,
    name: ev.name,
    startDate: ev.startDate,
    endDate: ev.endDate,
    isClosed: ev.isClosed,
    closedAt: ev.closedAt,
    expenseCount: eventExpenseCount(username, ev.uuid),
    createdAt: ev.createdAt,
  };
}

function eventResponse(username: string, ev: EventRecord) {
  return {
    uuid: ev.uuid,
    name: ev.name,
    description: ev.description,
    startDate: ev.startDate,
    endDate: ev.endDate,
    isClosed: ev.isClosed,
    closedAt: ev.closedAt,
    expenseCount: eventExpenseCount(username, ev.uuid),
    createdAt: ev.createdAt,
  };
}

/** The §3.7 debt-balance: advanced (paid) − owed (borne) per participating member. */
function computeBalance(username: string, ev: EventRecord) {
  const expenses = getExpenses(username).filter((e) => e.eventUuid === ev.uuid);
  const advanced = new Map<string, number>();
  const owed = new Map<string, number>();
  const seen = new Set<string>();
  const add = (map: Map<string, number>, uuid: string, amt: number) =>
    map.set(uuid, (map.get(uuid) ?? 0) + amt);

  for (const e of expenses) {
    add(advanced, e.payerMemberUuid, expenseTotal(e));
    seen.add(e.payerMemberUuid);
    for (const s of e.shares) {
      add(owed, s.memberUuid, s.amount);
      seen.add(s.memberUuid);
    }
  }
  // The owner-rep always participates (at 0đ if nothing else) — but only when
  // the event actually has expenses (empty event → empty rows).
  const ownerRep = getMembers(username).find((m) => m.isOwnerRepresentative);
  if (ownerRep && expenses.length > 0) seen.add(ownerRep.uuid);

  const rows = [...seen]
    .map((uuid) => {
      const m = memberByUuid(username, uuid);
      const a = advanced.get(uuid) ?? 0;
      const o = owed.get(uuid) ?? 0;
      return {
        memberUuid: uuid,
        memberName: m?.name ?? "(không rõ)",
        isOwnerRepresentative: m?.isOwnerRepresentative ?? false,
        isDeleted: m?.isDeleted ?? false,
        advanced: a,
        owed: o,
        balance: a - o,
      };
    })
    .sort((x, y) => {
      if (x.isOwnerRepresentative !== y.isOwnerRepresentative) {
        return x.isOwnerRepresentative ? -1 : 1;
      }
      return x.memberName.localeCompare(y.memberName, "vi");
    });

  return {
    eventUuid: ev.uuid,
    eventName: ev.name,
    isClosed: ev.isClosed,
    rows,
  };
}

// --- Bank accounts + QR store (mock backend, M7) --------------------------
interface BankAccountRecord {
  uuid: string;
  bankBin: string;
  bankName: string;
  accountNumber: string;
  accountHolderName: string;
  isDefault: boolean;
  createdAt: string;
}

const BANK_BIN_RE = /^\d{6}$/;
const ACCOUNT_NUMBER_RE = /^\d{6,19}$/;

// username → their bank accounts. Seeded lazily (both demo (Free, downgraded) and
// admin (Premium) get accounts so the read-only + managed splits are demoable).
const bankAccountsByUser = new Map<string, BankAccountRecord[]>();

function seedBankAccounts(): BankAccountRecord[] {
  const base = Date.parse("2026-01-01T00:00:00.000Z");
  return [
    {
      uuid: `ba-${rand()}`,
      bankBin: "970436",
      bankName: "Vietcombank",
      accountNumber: "0071001234567",
      accountHolderName: "NGUYEN VAN MINH",
      isDefault: true,
      createdAt: new Date(base).toISOString(),
    },
    {
      uuid: `ba-${rand()}`,
      bankBin: "970407",
      bankName: "Techcombank",
      accountNumber: "19024681012345",
      accountHolderName: "NGUYEN VAN MINH",
      isDefault: false,
      createdAt: new Date(base + 1000).toISOString(),
    },
  ];
}

function getBankAccounts(username: string): BankAccountRecord[] {
  let list = bankAccountsByUser.get(username);
  if (!list) {
    list = seedBankAccounts();
    bankAccountsByUser.set(username, list);
  }
  return list;
}

/** Default first, then most-recently-added — matches the backend order. */
function sortBankAccounts(list: BankAccountRecord[]): BankAccountRecord[] {
  return [...list].sort((a, b) => {
    if (a.isDefault !== b.isDefault) return a.isDefault ? -1 : 1;
    return a.createdAt < b.createdAt ? 1 : -1;
  });
}

function bankAccountResponse(r: BankAccountRecord) {
  return {
    uuid: r.uuid,
    bankBin: r.bankBin,
    bankName: r.bankName,
    accountNumber: r.accountNumber,
    accountHolderName: r.accountHolderName,
    isDefault: r.isDefault,
    createdAt: r.createdAt,
  };
}

function isPremiumUser(username: string): boolean {
  return (profiles.get(username)?.tier ?? "FREE").toUpperCase() === "PREMIUM";
}

/**
 * Test-only: register (or upgrade) a user's profile so the Premium-gated wallet +
 * QR handlers (`isPremiumUser`) treat them as the given tier. Additive — the
 * browser-mock `demo`/`admin` users are untouched. The M7 wallet specs seed a
 * fresh username per test (store isolation) and call this to exercise the REAL
 * committed mutation handlers (atomic default-swap, delete-promotion, validation).
 */
export function registerTestProfile(username: string, tier = "PREMIUM"): void {
  profiles.set(username, {
    uuid: `uuid-${username}`,
    tier,
    role: "USER",
    createdAt: "2026-01-01T00:00:00+00:00",
  });
}

/** The shared Premium-gate failure (403 13003) used by wallet mutations + QR. */
function premiumGate() {
  return fail(
    13003,
    "Tính năng này chỉ dành cho tài khoản Premium. Nâng cấp để sử dụng.",
    403,
  );
}

/** Validate a bank-account body the way the backend validator does. */
function validateBankAccount(body: {
  bankBin?: unknown;
  bankName?: unknown;
  accountNumber?: unknown;
  accountHolderName?: unknown;
}): Record<string, string[]> | null {
  const bankBin = typeof body.bankBin === "string" ? body.bankBin.trim() : "";
  const bankName = typeof body.bankName === "string" ? body.bankName.trim() : "";
  const accountNumber =
    typeof body.accountNumber === "string" ? body.accountNumber.trim() : "";
  const holder =
    typeof body.accountHolderName === "string"
      ? body.accountHolderName.trim()
      : "";
  if (!BANK_BIN_RE.test(bankBin)) {
    return { bankBin: ["BIN gồm đúng 6 chữ số."] };
  }
  if (bankName.length === 0 || bankName.length > 100) {
    return { bankName: ["Tên ngân hàng không được để trống."] };
  }
  if (!ACCOUNT_NUMBER_RE.test(accountNumber)) {
    return { accountNumber: ["Số tài khoản gồm 6–19 chữ số."] };
  }
  if (holder.length === 0 || holder.length > 100) {
    return { accountHolderName: ["Tên chủ tài khoản không được để trống."] };
  }
  return null;
}

/** A tiny 1×1 PNG so the QR blob path (fetch → object URL → <img> → download) is
 *  exercisable end-to-end without a real image generator. */
const PNG_1x1_BASE64 =
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HBwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

function pngResponse(name: string) {
  const binary = atob(PNG_1x1_BASE64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) bytes[i] = binary.charCodeAt(i);
  return new HttpResponse(bytes, {
    headers: {
      "Content-Type": "image/png",
      "Content-Disposition": `attachment; filename="${name}"`,
    },
  });
}

function issueTokens(username: string) {
  const now = Date.now();
  const refreshToken = `refresh-${username}-${now}-${rand()}`;
  validRefreshTokens.add(refreshToken);
  return {
    accessToken: `access-${username}-${now}-${rand()}`,
    accessTokenExpiresAt: new Date(now + 30 * 60_000).toISOString(),
    refreshToken,
    refreshTokenExpiresAt: new Date(now + 30 * 86_400_000).toISOString(),
  };
}

// --- Admin store (mock backend, M8) ---------------------------------------
// Account metadata + tier-grant records ONLY — deliberately NO ledger fields, so
// the R10 privacy test is meaningful (asserts no ledger key ever appears).
interface AdminGrantRecord {
  uuid: string;
  tier: "FREE" | "PREMIUM";
  action: "GRANT" | "REVOKE";
  amount: number;
  currency: string;
  reference: string | null;
  note: string | null;
  grantedByUsername: string;
  createdAt: string;
}
interface AdminUserRecord {
  uuid: string;
  username: string;
  tier: "FREE" | "PREMIUM";
  role: "USER" | "ADMIN";
  status: "ACTIVE" | "DISABLED";
  createdAt: string;
  grants: AdminGrantRecord[];
}

function seedAdminUsers(): AdminUserRecord[] {
  const day = (m: number, d: number) =>
    `2026-${String(m).padStart(2, "0")}-${String(d).padStart(2, "0")}T09:00:00.000Z`;
  const records: AdminUserRecord[] = [
    {
      uuid: "uuid-admin",
      username: "admin",
      tier: "PREMIUM",
      role: "ADMIN",
      status: "ACTIVE",
      createdAt: day(1, 5),
      grants: [],
    },
    {
      uuid: "uuid-pham-admin",
      username: "pham.admin",
      tier: "PREMIUM",
      role: "ADMIN",
      status: "ACTIVE",
      createdAt: day(1, 8),
      grants: [
        {
          uuid: `tg-${rand()}`,
          tier: "PREMIUM",
          action: "GRANT",
          amount: 500000,
          currency: "VND",
          reference: "MB-20260305-1190",
          note: "Cấp nội bộ",
          grantedByUsername: "admin",
          createdAt: day(3, 5),
        },
      ],
    },
    {
      uuid: "uuid-nguyen-a",
      username: "nguyen.van.a",
      tier: "PREMIUM",
      role: "USER",
      status: "ACTIVE",
      createdAt: day(2, 2),
      grants: [
        {
          uuid: `tg-${rand()}`,
          tier: "PREMIUM",
          action: "GRANT",
          amount: 200000,
          currency: "VND",
          reference: "VCB-20260716-8842",
          note: "Gia hạn 1 năm",
          grantedByUsername: "admin",
          createdAt: day(7, 16),
        },
        {
          uuid: `tg-${rand()}`,
          tier: "PREMIUM",
          action: "GRANT",
          amount: 200000,
          currency: "VND",
          reference: "VCB-20260115-1001",
          note: null,
          grantedByUsername: "admin",
          createdAt: day(1, 15),
        },
      ],
    },
    {
      uuid: "uuid-le-b",
      username: "le.thi.b",
      tier: "FREE",
      role: "USER",
      status: "ACTIVE",
      createdAt: day(3, 11),
      grants: [],
    },
    {
      uuid: "uuid-tran-d",
      username: "tran.d",
      tier: "FREE",
      role: "USER",
      status: "DISABLED",
      createdAt: day(5, 20),
      grants: [],
    },
  ];
  // Filler users so the list pages (25 total > pageSize 20).
  for (let i = 1; i <= 20; i += 1) {
    const month = (i % 6) + 1;
    const isPremium = i % 3 === 0;
    records.push({
      uuid: `uuid-user-${i}`,
      username: `user.${String(i).padStart(3, "0")}`,
      tier: isPremium ? "PREMIUM" : "FREE",
      role: "USER",
      status: "ACTIVE",
      createdAt: day(month, (i % 27) + 1),
      grants: isPremium
        ? [
            {
              uuid: `tg-${rand()}`,
              tier: "PREMIUM",
              action: "GRANT",
              amount: 200000,
              currency: "VND",
              reference: `TCB-2026${String(month).padStart(2, "0")}-${1000 + i}`,
              note: null,
              grantedByUsername: "admin",
              createdAt: day(month, (i % 27) + 1),
            },
          ]
        : [],
    });
  }
  return records;
}

let adminUsers: AdminUserRecord[] | null = null;
function getAdminUsers(): AdminUserRecord[] {
  if (!adminUsers) adminUsers = seedAdminUsers();
  return adminUsers;
}

/**
 * Test-only: reset the admin user/grant store back to its deterministic seed.
 * The store is a module-level singleton mutated by the admin action handlers
 * (grant/revoke/disable/enable/role), so specs that drive those against the
 * committed handlers call this in `beforeEach` for per-test isolation. Additive
 * and inert for the browser-mock path (which never calls it).
 */
export function resetAdminStore(): void {
  adminUsers = null;
}

type Gate =
  | { ok: true; username: string; uuid: string }
  | { ok: false; response: ReturnType<typeof fail> };

/** ADMIN-only gate: 401 (no token) / 500 (degraded) / 403 1004 (non-admin). */
function adminGate(request: Request): Gate {
  const username = usernameFromAuthHeader(request.headers.get("Authorization"));
  if (!username) {
    return {
      ok: false,
      response: fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401),
    };
  }
  const profile = profiles.get(username);
  if (!profile) {
    return { ok: false, response: fail(1000, "Đã xảy ra lỗi máy chủ.", 500) };
  }
  if (profile.role !== "ADMIN") {
    return {
      ok: false,
      response: fail(1004, "Bạn không có quyền truy cập khu vực quản trị.", 403),
    };
  }
  return { ok: true, username, uuid: profile.uuid };
}

function adminUserRow(u: AdminUserRecord) {
  const grantRows = u.grants.filter((g) => g.action === "GRANT");
  const lastGrant = grantRows.reduce<string | null>(
    (acc, g) => (acc === null || g.createdAt > acc ? g.createdAt : acc),
    null,
  );
  return {
    uuid: u.uuid,
    username: u.username,
    tier: u.tier,
    role: u.role,
    status: u.status,
    createdAt: u.createdAt,
    grantCount: grantRows.length,
    lastGrantAt: lastGrant,
  };
}

function grantRowResponse(g: AdminGrantRecord) {
  return {
    uuid: g.uuid,
    tier: g.tier,
    action: g.action,
    amount: g.amount,
    currency: g.currency,
    reference: g.reference,
    note: g.note,
    grantedByUsername: g.grantedByUsername,
    createdAt: g.createdAt,
  };
}

function inRange(iso: string, from: string | null, to: string | null): boolean {
  if (from && iso < from) return false;
  if (to && iso > to) return false;
  return true;
}

/** Guard destructive actions the way the backend does: self → 14001; admin → 14002. */
function ensureDestructiveAllowed(
  actingUuid: string,
  target: AdminUserRecord,
): ReturnType<typeof fail> | null {
  if (target.uuid === actingUuid) {
    return fail(14001, "Không thể thực hiện hành động này với chính bạn.", 400);
  }
  if (target.role === "ADMIN") {
    return fail(
      14002,
      "Không thể thực hiện hành động này với một quản trị viên khác.",
      400,
    );
  }
  return null;
}

export const handlers = [
  http.post("*/api/v1/auth/register", async ({ request }) => {
    const body = (await request.json()) as RegisterRequest;
    if (users.has(body.username)) {
      return fail(2000, "Tên đăng nhập đã tồn tại.", 400);
    }
    users.set(body.username, body.password);
    const profile: Profile = {
      uuid: `uuid-${rand()}`,
      tier: "FREE",
      role: "USER",
      createdAt: new Date().toISOString(),
    };
    profiles.set(body.username, profile);
    return ok({ username: body.username, ...profile });
  }),

  http.get("*/api/v1/auth/me", ({ request }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const profile = profiles.get(username);
    if (!profile) {
      // Valid token but no profile → simulate a non-401 server error (OQ3a).
      return fail(1000, "Đã xảy ra lỗi máy chủ.", 500);
    }
    return ok({ username, ...profile });
  }),

  http.post("*/api/v1/auth/login", async ({ request }) => {
    const body = (await request.json()) as LoginRequest;
    if (users.get(body.username) === body.password) {
      lastLoggedInUser = body.username;
      return ok(issueTokens(body.username));
    }
    return fail(2001, "Tên đăng nhập hoặc mật khẩu không đúng.", 401);
  }),

  http.post("*/api/v1/auth/refresh", async ({ request }) => {
    const body = (await request.json()) as RefreshRequest;
    if (!validRefreshTokens.has(body.refreshToken)) {
      // Reuse/expired: 2002 (terminal — client hard-clears the session).
      return fail(2002, "Mã gia hạn phiên không hợp lệ hoặc đã hết hạn.", 401);
    }
    validRefreshTokens.delete(body.refreshToken); // full pair rotation
    const username = body.refreshToken.split("-")[1] ?? "demo";
    return ok(issueTokens(username));
  }),

  http.post("*/api/v1/auth/logout", () => ok({ message: "Đã đăng xuất." })),

  http.post("*/api/v1/auth/change-password", async ({ request }) => {
    const body = (await request.json()) as ChangePasswordRequest;
    const username = lastLoggedInUser ?? "demo";
    if (users.get(username) !== body.currentPassword) {
      return fail(2003, "Mật khẩu hiện tại không đúng.", 400);
    }
    users.set(username, body.newPassword);
    validRefreshTokens.clear(); // change-password revokes ALL tokens
    return ok({ message: "Đổi mật khẩu thành công." });
  }),

  // --- Members ------------------------------------------------------------
  http.get("*/api/v1/members", ({ request }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const includeDeleted =
      new URL(request.url).searchParams.get("includeDeleted") === "true";
    const list = getMembers(username).filter(
      (m) => includeDeleted || !m.isDeleted,
    );
    return ok(sortMembers(list).map(toResponse));
  }),

  http.post("*/api/v1/members", async ({ request }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const body = (await request.json()) as { name?: unknown };
    const name = validateName(body.name);
    if (typeof name !== "string") {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        name: ["Tên thành viên không được để trống."],
      });
    }
    const list = getMembers(username);
    const profile = profiles.get(username);
    const isFree = (profile?.tier ?? "FREE").toUpperCase() === "FREE";
    const activeCount = list.filter((m) => !m.isDeleted).length;
    if (isFree && activeCount >= FREE_MEMBER_LIMIT) {
      return fail(
        13000,
        `Tài khoản Free chỉ có thể có tối đa ${FREE_MEMBER_LIMIT} thành viên đang hoạt động. Nâng cấp Premium để bỏ giới hạn.`,
        400,
      );
    }
    const record: MemberRecord = {
      uuid: `m-${rand()}`,
      name,
      isOwnerRepresentative: false,
      isDeleted: false,
      createdAt: new Date().toISOString(),
    };
    list.push(record);
    return ok(toResponse(record));
  }),

  http.put("*/api/v1/members/:uuid", async ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const body = (await request.json()) as { name?: unknown };
    const name = validateName(body.name);
    if (typeof name !== "string") {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        name: ["Tên thành viên không được để trống."],
      });
    }
    const member = getMembers(username).find(
      (m) => m.uuid === params.uuid && !m.isDeleted,
    );
    if (!member) {
      return fail(3000, "Không tìm thấy thành viên.", 404);
    }
    member.name = name;
    return ok(toResponse(member));
  }),

  http.delete("*/api/v1/members/:uuid", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const member = getMembers(username).find(
      (m) => m.uuid === params.uuid && !m.isDeleted,
    );
    if (!member) {
      return fail(3000, "Không tìm thấy thành viên.", 404);
    }
    if (member.isOwnerRepresentative) {
      return fail(3001, "Không thể xóa thành viên đại diện chủ sổ.", 400);
    }
    member.isDeleted = true;
    return ok({ message: "Đã xóa thành viên." });
  }),

  // --- Categories ---------------------------------------------------------
  http.get("*/api/v1/categories", ({ request }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const includeDeleted =
      new URL(request.url).searchParams.get("includeDeleted") === "true";
    const list = getCategories(username).filter(
      (c) => includeDeleted || !c.isDeleted,
    );
    return ok(sortCategories(list).map(categoryResponse));
  }),

  http.post("*/api/v1/categories", async ({ request }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const body = (await request.json()) as {
      name?: unknown;
      color?: unknown;
      icon?: unknown;
    };
    const name = validateName(body.name);
    if (typeof name !== "string") {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        name: ["Tên danh mục không được để trống."],
      });
    }
    const color = typeof body.color === "string" ? body.color : "";
    if (!HEX_COLOR.test(color)) {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        color: ["Màu phải có dạng #RRGGBB."],
      });
    }
    const icon = typeof body.icon === "string" && body.icon ? body.icon : null;
    const list = getCategories(username);
    // Reactivation: a name matching a soft-deleted category revives it and
    // overwrites its color/icon (default flag untouched); returns 200.
    const deletedMatch = list.find(
      (c) => c.isDeleted && c.name.localeCompare(name, "vi", { sensitivity: "accent" }) === 0,
    );
    if (deletedMatch) {
      deletedMatch.isDeleted = false;
      deletedMatch.name = name;
      deletedMatch.color = color;
      deletedMatch.icon = icon;
      return ok(categoryResponse(deletedMatch));
    }
    // Active-name collision → 4001.
    const activeDup = list.find(
      (c) => !c.isDeleted && c.name.localeCompare(name, "vi", { sensitivity: "accent" }) === 0,
    );
    if (activeDup) {
      return fail(4001, "Tên danh mục đã tồn tại.", 400);
    }
    const record: CategoryRecord = {
      uuid: `c-${rand()}`,
      name,
      color,
      icon,
      isDefault: false,
      isDeleted: false,
      createdAt: new Date().toISOString(),
    };
    list.push(record);
    return ok(categoryResponse(record));
  }),

  http.put("*/api/v1/categories/:uuid/default", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const list = getCategories(username);
    const target = list.find((c) => c.uuid === params.uuid && !c.isDeleted);
    if (!target) {
      return fail(4000, "Không tìm thấy danh mục.", 404);
    }
    // Atomic swap: clear the old default, set this one.
    for (const c of list) c.isDefault = false;
    target.isDefault = true;
    return ok({ message: "Đã đặt danh mục mặc định." });
  }),

  http.put("*/api/v1/categories/:uuid", async ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const body = (await request.json()) as {
      name?: unknown;
      color?: unknown;
      icon?: unknown;
    };
    const name = validateName(body.name);
    if (typeof name !== "string") {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        name: ["Tên danh mục không được để trống."],
      });
    }
    const color = typeof body.color === "string" ? body.color : "";
    if (!HEX_COLOR.test(color)) {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        color: ["Màu phải có dạng #RRGGBB."],
      });
    }
    const icon = typeof body.icon === "string" && body.icon ? body.icon : null;
    const list = getCategories(username);
    const category = list.find((c) => c.uuid === params.uuid && !c.isDeleted);
    if (!category) {
      return fail(4000, "Không tìm thấy danh mục.", 404);
    }
    const dup = list.find(
      (c) =>
        c.uuid !== category.uuid &&
        !c.isDeleted &&
        c.name.localeCompare(name, "vi", { sensitivity: "accent" }) === 0,
    );
    if (dup) {
      return fail(4001, "Tên danh mục đã tồn tại.", 400);
    }
    category.name = name;
    category.color = color;
    category.icon = icon;
    return ok(categoryResponse(category));
  }),

  http.delete("*/api/v1/categories/:uuid", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const category = getCategories(username).find(
      (c) => c.uuid === params.uuid && !c.isDeleted,
    );
    if (!category) {
      return fail(4000, "Không tìm thấy danh mục.", 404);
    }
    if (category.isDefault) {
      return fail(4002, "Không thể xóa danh mục mặc định.", 400);
    }
    category.isDeleted = true;
    return ok({ message: "Đã xóa danh mục." });
  }),

  // --- Tags ---------------------------------------------------------------
  http.get("*/api/v1/tags", ({ request }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const includeDeleted =
      new URL(request.url).searchParams.get("includeDeleted") === "true";
    const list = getTags(username).filter((tag) => includeDeleted || !tag.isDeleted);
    return ok(sortTags(list).map(tagResponse));
  }),

  http.post("*/api/v1/tags", async ({ request }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const body = (await request.json()) as { name?: unknown };
    const name = validateName(body.name);
    if (typeof name !== "string") {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        name: ["Tên nhãn không được để trống."],
      });
    }
    const list = getTags(username);
    // Reactivation: a name matching a soft-deleted tag revives it (keeps uuid +
    // history); returns 200.
    const deletedMatch = list.find(
      (tag) => tag.isDeleted && tag.name.localeCompare(name, "vi", { sensitivity: "accent" }) === 0,
    );
    if (deletedMatch) {
      deletedMatch.isDeleted = false;
      deletedMatch.name = name;
      return ok(tagResponse(deletedMatch));
    }
    const activeDup = list.find(
      (tag) => !tag.isDeleted && tag.name.localeCompare(name, "vi", { sensitivity: "accent" }) === 0,
    );
    if (activeDup) {
      return fail(5001, "Tên nhãn đã tồn tại.", 400);
    }
    const record: TagRecord = {
      uuid: `t-${rand()}`,
      name,
      isDeleted: false,
      createdAt: new Date().toISOString(),
    };
    list.push(record);
    return ok(tagResponse(record));
  }),

  http.put("*/api/v1/tags/:uuid", async ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const body = (await request.json()) as { name?: unknown };
    const name = validateName(body.name);
    if (typeof name !== "string") {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        name: ["Tên nhãn không được để trống."],
      });
    }
    const list = getTags(username);
    const tag = list.find((tg) => tg.uuid === params.uuid && !tg.isDeleted);
    if (!tag) {
      return fail(5000, "Không tìm thấy nhãn.", 404);
    }
    const dup = list.find(
      (tg) =>
        tg.uuid !== tag.uuid &&
        !tg.isDeleted &&
        tg.name.localeCompare(name, "vi", { sensitivity: "accent" }) === 0,
    );
    if (dup) {
      return fail(5001, "Tên nhãn đã tồn tại.", 400);
    }
    tag.name = name;
    return ok(tagResponse(tag));
  }),

  http.delete("*/api/v1/tags/:uuid", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const tag = getTags(username).find(
      (tg) => tg.uuid === params.uuid && !tg.isDeleted,
    );
    if (!tag) {
      return fail(5000, "Không tìm thấy nhãn.", 404);
    }
    tag.isDeleted = true;
    return ok({ message: "Đã xóa nhãn." });
  }),

  // --- Expenses -----------------------------------------------------------
  http.get("*/api/v1/expenses", ({ request }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const url = new URL(request.url);
    const from = url.searchParams.get("from");
    const to = url.searchParams.get("to");
    const categoryUuid = url.searchParams.get("categoryUuid");
    const tagUuid = url.searchParams.get("tagUuid");
    const settled = url.searchParams.get("settled");
    const looseOnly = url.searchParams.get("looseOnly");
    const eventUuid = url.searchParams.get("eventUuid");

    let list = getExpenses(username).slice();
    if (from) list = list.filter((e) => e.expenseTime >= from);
    if (to) list = list.filter((e) => e.expenseTime <= to);
    if (categoryUuid) list = list.filter((e) => e.categoryUuid === categoryUuid);
    if (tagUuid) list = list.filter((e) => e.tagUuids.includes(tagUuid));
    if (settled === "true") list = list.filter((e) => e.isSettled);
    if (settled === "false") list = list.filter((e) => !e.isSettled);
    if (looseOnly === "true") list = list.filter((e) => !e.eventUuid);
    if (eventUuid) list = list.filter((e) => e.eventUuid === eventUuid);
    // expenseTime DESC.
    list.sort((a, b) => (a.expenseTime < b.expenseTime ? 1 : -1));
    return ok(list.map((e) => expenseSummary(username, e)));
  }),

  http.get("*/api/v1/expenses/:uuid/history", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const logs = getAudit(username)
      .filter((a) => a.expenseUuid === params.uuid)
      .sort((a, b) => (a.createdAt < b.createdAt ? -1 : 1));
    return ok(logs);
  }),

  http.get("*/api/v1/expenses/:uuid/export", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const expense = getExpenses(username).find((e) => e.uuid === params.uuid);
    if (!expense) {
      return fail(6000, "Không tìm thấy phiếu chi tiêu.", 404);
    }
    const rows = [
      ["Thành viên", "Số tiền", "Ghi chú"],
      ...expense.shares.map((s) => [
        memberByUuid(username, s.memberUuid)?.name ?? "",
        String(s.amount),
        s.note ?? "",
      ]),
    ];
    const csv =
      `﻿${expense.name}\r\n\r\n` +
      rows.map((r) => r.join(",")).join("\r\n") +
      "\r\n";
    return new HttpResponse(csv, {
      headers: {
        "Content-Type": "text/csv; charset=utf-8",
        "Content-Disposition": `attachment; filename="expense-${expense.uuid}.csv"`,
      },
    });
  }),

  http.get("*/api/v1/expenses/:uuid", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const expense = getExpenses(username).find((e) => e.uuid === params.uuid);
    if (!expense) {
      return fail(6000, "Không tìm thấy phiếu chi tiêu.", 404);
    }
    return ok(expenseResponse(username, expense));
  }),

  http.post("*/api/v1/expenses", async ({ request }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const body = (await request.json()) as {
      name?: unknown;
      description?: unknown;
      expenseTime?: unknown;
      payerMemberUuid?: string;
      categoryUuid?: string;
      tagUuids?: string[];
      shares?: { memberUuid: string; amount: number; note?: string | null }[];
    };
    const name = typeof body.name === "string" ? body.name.trim() : "";
    if (name.length === 0 || name.length > 200) {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        name: ["Tên phiếu không được để trống."],
      });
    }
    if (!body.expenseTime || typeof body.expenseTime !== "string") {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        expenseTime: ["Vui lòng chọn thời điểm chi."],
      });
    }
    const ownerRep = getMembers(username).find((m) => m.isOwnerRepresentative);
    const payerUuid = body.payerMemberUuid || ownerRep?.uuid || "";
    const payer = memberByUuid(username, payerUuid);
    if (!payer || payer.isDeleted) {
      return fail(6001, "Người trả không hợp lệ.", 400);
    }
    const defaultCat = getCategories(username).find((c) => c.isDefault);
    const catUuid = body.categoryUuid || defaultCat?.uuid || "";
    const category = categoryByUuid(username, catUuid);
    if (!category || category.isDeleted) {
      return fail(6002, "Danh mục không hợp lệ.", 400);
    }
    const tagUuids = body.tagUuids ?? [];
    for (const tu of tagUuids) {
      const tg = getTags(username).find((x) => x.uuid === tu);
      if (!tg || tg.isDeleted) return fail(6003, "Nhãn không hợp lệ.", 400);
    }
    const inputShares = body.shares ?? [];
    const seen = new Set<string>();
    for (const s of inputShares) {
      if (seen.has(s.memberUuid))
        return fail(7003, "Trùng thành viên phần gánh.", 400);
      seen.add(s.memberUuid);
      const m = memberByUuid(username, s.memberUuid);
      if (!m || m.isDeleted) return fail(7001, "Thành viên không hợp lệ.", 400);
    }
    // Auto-inject the owner-rep 0đ share if missing.
    if (ownerRep && !seen.has(ownerRep.uuid)) {
      inputShares.unshift({ memberUuid: ownerRep.uuid, amount: 0, note: null });
    }
    const now = new Date().toISOString();
    const record: ExpenseRecord = {
      uuid: `e-${rand()}`,
      name,
      description:
        typeof body.description === "string" && body.description
          ? body.description
          : null,
      expenseTime: body.expenseTime,
      payerMemberUuid: payerUuid,
      categoryUuid: catUuid,
      tagUuids,
      isSettled: false,
      settledAt: null,
      shares: inputShares.map((s) => ({
        uuid: `s-${rand()}`,
        memberUuid: s.memberUuid,
        amount: s.amount ?? 0,
        note: s.note ?? null,
        createdAt: now,
      })),
      eventUuid: null,
      createdAt: now,
    };
    getExpenses(username).push(record);
    pushAudit(username, {
      entityType: "Expense",
      entityUuid: record.uuid,
      expenseUuid: record.uuid,
      action: "Create",
      before: null,
      after: expenseSnapshot(username, record),
    });
    for (const s of record.shares) {
      pushAudit(username, {
        entityType: "Share",
        entityUuid: s.uuid,
        expenseUuid: record.uuid,
        action: "Create",
        before: null,
        after: shareSnapshot(username, record.uuid, s),
      });
    }
    return ok(expenseResponse(username, record));
  }),

  http.put("*/api/v1/expenses/:uuid/settled", async ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const expense = getExpenses(username).find((e) => e.uuid === params.uuid);
    if (!expense) return fail(6000, "Không tìm thấy phiếu chi tiêu.", 404);
    const body = (await request.json()) as { isSettled?: boolean };
    expense.isSettled = Boolean(body.isSettled);
    expense.settledAt = expense.isSettled ? new Date().toISOString() : null;
    return ok({ message: "Đã cập nhật trạng thái đã trả." });
  }),

  http.put("*/api/v1/expenses/:uuid", async ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const expense = getExpenses(username).find((e) => e.uuid === params.uuid);
    if (!expense) return fail(6000, "Không tìm thấy phiếu chi tiêu.", 404);
    const body = (await request.json()) as {
      name?: unknown;
      description?: unknown;
      expenseTime?: unknown;
      payerMemberUuid?: string;
      categoryUuid?: string;
      tagUuids?: string[];
    };
    const name = typeof body.name === "string" ? body.name.trim() : "";
    if (name.length === 0 || name.length > 200) {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        name: ["Tên phiếu không được để trống."],
      });
    }
    const ownerRep = getMembers(username).find((m) => m.isOwnerRepresentative);
    const payerUuid = body.payerMemberUuid || ownerRep?.uuid || "";
    const payer = memberByUuid(username, payerUuid);
    if (!payer || payer.isDeleted) return fail(6001, "Người trả không hợp lệ.", 400);
    const defaultCat = getCategories(username).find((c) => c.isDefault);
    const catUuid = body.categoryUuid || defaultCat?.uuid || "";
    const category = categoryByUuid(username, catUuid);
    if (!category || category.isDeleted)
      return fail(6002, "Danh mục không hợp lệ.", 400);
    const tagUuids = body.tagUuids ?? [];
    for (const tu of tagUuids) {
      const tg = getTags(username).find((x) => x.uuid === tu);
      if (!tg || tg.isDeleted) return fail(6003, "Nhãn không hợp lệ.", 400);
    }
    const before = expenseSnapshot(username, expense);
    expense.name = name;
    expense.description =
      typeof body.description === "string" && body.description
        ? body.description
        : null;
    if (typeof body.expenseTime === "string") expense.expenseTime = body.expenseTime;
    expense.payerMemberUuid = payerUuid;
    expense.categoryUuid = catUuid;
    expense.tagUuids = tagUuids;
    const after = expenseSnapshot(username, expense);
    if (JSON.stringify(before) !== JSON.stringify(after)) {
      pushAudit(username, {
        entityType: "Expense",
        entityUuid: expense.uuid,
        expenseUuid: expense.uuid,
        action: "Update",
        before,
        after,
      });
    }
    return ok(expenseResponse(username, expense));
  }),

  http.delete("*/api/v1/expenses/:uuid", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const list = getExpenses(username);
    const idx = list.findIndex((e) => e.uuid === params.uuid);
    if (idx === -1) return fail(6000, "Không tìm thấy phiếu chi tiêu.", 404);
    const expense = list[idx];
    for (const s of expense.shares) {
      pushAudit(username, {
        entityType: "Share",
        entityUuid: s.uuid,
        expenseUuid: expense.uuid,
        action: "Delete",
        before: shareSnapshot(username, expense.uuid, s),
        after: null,
      });
    }
    pushAudit(username, {
      entityType: "Expense",
      entityUuid: expense.uuid,
      expenseUuid: expense.uuid,
      action: "Delete",
      before: expenseSnapshot(username, expense),
      after: null,
    });
    list.splice(idx, 1);
    return ok({ message: "Đã xóa phiếu chi tiêu." });
  }),

  http.post("*/api/v1/expenses/:uuid/shares", async ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const expense = getExpenses(username).find((e) => e.uuid === params.uuid);
    if (!expense) return fail(6000, "Không tìm thấy phiếu chi tiêu.", 404);
    const body = (await request.json()) as {
      memberUuid?: string;
      amount?: number;
      note?: string | null;
    };
    const member = memberByUuid(username, body.memberUuid ?? "");
    if (!member || member.isDeleted)
      return fail(7001, "Thành viên không hợp lệ.", 400);
    if (expense.shares.some((s) => s.memberUuid === body.memberUuid))
      return fail(7003, "Trùng thành viên phần gánh.", 400);
    const share: ShareRecord = {
      uuid: `s-${rand()}`,
      memberUuid: body.memberUuid ?? "",
      amount: body.amount ?? 0,
      note: body.note ?? null,
      createdAt: new Date().toISOString(),
    };
    expense.shares.push(share);
    pushAudit(username, {
      entityType: "Share",
      entityUuid: share.uuid,
      expenseUuid: expense.uuid,
      action: "Create",
      before: null,
      after: shareSnapshot(username, expense.uuid, share),
    });
    return ok(shareResponse(username, share));
  }),

  http.put(
    "*/api/v1/expenses/:uuid/shares/:shareUuid",
    async ({ request, params }) => {
      const username = usernameFromAuthHeader(
        request.headers.get("Authorization"),
      );
      if (!username) {
        return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
      }
      const expense = getExpenses(username).find((e) => e.uuid === params.uuid);
      if (!expense) return fail(7000, "Không tìm thấy phần gánh.", 404);
      const share = expense.shares.find((s) => s.uuid === params.shareUuid);
      if (!share) return fail(7000, "Không tìm thấy phần gánh.", 404);
      const body = (await request.json()) as {
        memberUuid?: string;
        amount?: number;
        note?: string | null;
      };
      const member = memberByUuid(username, body.memberUuid ?? "");
      if (!member || member.isDeleted)
        return fail(7001, "Thành viên không hợp lệ.", 400);
      if (
        body.memberUuid !== share.memberUuid &&
        expense.shares.some((s) => s.memberUuid === body.memberUuid)
      ) {
        return fail(7003, "Trùng thành viên phần gánh.", 400);
      }
      const before = shareSnapshot(username, expense.uuid, share);
      share.memberUuid = body.memberUuid ?? share.memberUuid;
      share.amount = body.amount ?? 0;
      share.note = body.note ?? null;
      const after = shareSnapshot(username, expense.uuid, share);
      if (JSON.stringify(before) !== JSON.stringify(after)) {
        pushAudit(username, {
          entityType: "Share",
          entityUuid: share.uuid,
          expenseUuid: expense.uuid,
          action: "Update",
          before,
          after,
        });
      }
      return ok(shareResponse(username, share));
    },
  ),

  http.delete(
    "*/api/v1/expenses/:uuid/shares/:shareUuid",
    ({ request, params }) => {
      const username = usernameFromAuthHeader(
        request.headers.get("Authorization"),
      );
      if (!username) {
        return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
      }
      const expense = getExpenses(username).find((e) => e.uuid === params.uuid);
      if (!expense) return fail(7000, "Không tìm thấy phần gánh.", 404);
      const share = expense.shares.find((s) => s.uuid === params.shareUuid);
      if (!share) return fail(7000, "Không tìm thấy phần gánh.", 404);
      const member = memberByUuid(username, share.memberUuid);
      if (member?.isOwnerRepresentative) {
        return fail(
          7002,
          "Không thể xóa phần gánh của thành viên đại diện chủ sổ.",
          400,
        );
      }
      expense.shares = expense.shares.filter((s) => s.uuid !== share.uuid);
      pushAudit(username, {
        entityType: "Share",
        entityUuid: share.uuid,
        expenseUuid: expense.uuid,
        action: "Delete",
        before: shareSnapshot(username, expense.uuid, share),
        after: null,
      });
      return ok({ message: "Đã xóa phần gánh." });
    },
  ),

  // --- Expense ↔ event assign / remove (M5) -------------------------------
  http.put("*/api/v1/expenses/:uuid/event", async ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const expense = getExpenses(username).find((e) => e.uuid === params.uuid);
    if (!expense) return fail(6000, "Không tìm thấy phiếu chi tiêu.", 404);
    // Moving out of a closed source event is blocked (defensive).
    if (expense.eventUuid) {
      const source = eventByUuid(username, expense.eventUuid);
      if (source?.isClosed) {
        return fail(9001, "Đợt chi tiêu đã chốt.", 400);
      }
    }
    const body = (await request.json()) as { eventUuid?: string };
    const target = eventByUuid(username, body.eventUuid ?? "");
    if (!target) return fail(9000, "Không tìm thấy đợt chi tiêu.", 404);
    if (target.isClosed) return fail(9001, "Đợt chi tiêu đã chốt.", 400);
    if (
      expense.expenseTime < target.startDate ||
      expense.expenseTime > target.endDate
    ) {
      return fail(
        9002,
        "Thời điểm chi của phiếu nằm ngoài khoảng thời gian của đợt.",
        400,
      );
    }
    expense.eventUuid = target.uuid;
    return ok(expenseResponse(username, expense));
  }),

  http.delete("*/api/v1/expenses/:uuid/event", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const expense = getExpenses(username).find((e) => e.uuid === params.uuid);
    if (!expense) return fail(6000, "Không tìm thấy phiếu chi tiêu.", 404);
    if (!expense.eventUuid) return ok({ message: "Phiếu không thuộc đợt nào." });
    const source = eventByUuid(username, expense.eventUuid);
    if (source?.isClosed) return fail(9001, "Đợt chi tiêu đã chốt.", 400);
    expense.eventUuid = null;
    return ok({ message: "Đã gỡ phiếu khỏi đợt." });
  }),

  // --- Events -------------------------------------------------------------
  http.get("*/api/v1/events", ({ request }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const closed = new URL(request.url).searchParams.get("closed");
    let list = getEvents(username).slice();
    if (closed === "true") list = list.filter((e) => e.isClosed);
    if (closed === "false") list = list.filter((e) => !e.isClosed);
    // startDate DESC, then createdAt DESC.
    list.sort((a, b) => {
      if (a.startDate !== b.startDate) return a.startDate < b.startDate ? 1 : -1;
      return a.createdAt < b.createdAt ? 1 : -1;
    });
    return ok(list.map((e) => eventSummaryResponse(username, e)));
  }),

  http.post("*/api/v1/events", async ({ request }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const body = (await request.json()) as {
      name?: unknown;
      description?: unknown;
      startDate?: unknown;
      endDate?: unknown;
    };
    const name = typeof body.name === "string" ? body.name.trim() : "";
    if (name.length === 0 || name.length > 200) {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        name: ["Tên đợt không được để trống."],
      });
    }
    if (typeof body.startDate !== "string" || typeof body.endDate !== "string") {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        startDate: ["Vui lòng chọn ngày bắt đầu."],
      });
    }
    const start = dayBounds(body.startDate);
    const end = dayBounds(body.endDate);
    if (end.end < start.start) {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        endDate: ["Ngày kết thúc phải sau hoặc bằng ngày bắt đầu."],
      });
    }
    const list = getEvents(username);
    const profile = profiles.get(username);
    const isFree = (profile?.tier ?? "FREE").toUpperCase() === "FREE";
    const openCount = list.filter((e) => !e.isClosed).length;
    if (isFree && openCount >= FREE_OPEN_EVENT_LIMIT) {
      return fail(
        13001,
        `Tài khoản Free chỉ có thể có tối đa ${FREE_OPEN_EVENT_LIMIT} đợt đang mở. Nâng cấp Premium để bỏ giới hạn.`,
        400,
      );
    }
    const record: EventRecord = {
      uuid: `ev-${rand()}`,
      name,
      description:
        typeof body.description === "string" && body.description
          ? body.description
          : null,
      startDate: start.start,
      endDate: end.end,
      isClosed: false,
      closedAt: null,
      createdAt: new Date().toISOString(),
    };
    list.push(record);
    return ok(eventResponse(username, record));
  }),

  http.get("*/api/v1/events/:uuid/balance", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const ev = eventByUuid(username, String(params.uuid));
    if (!ev) return fail(9000, "Không tìm thấy đợt chi tiêu.", 404);
    return ok(computeBalance(username, ev));
  }),

  http.get("*/api/v1/events/:uuid/export", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const format = new URL(request.url).searchParams.get("format") ?? "csv";
    if (format !== "csv") {
      return fail(1001, "Định dạng xuất không được hỗ trợ.", 400);
    }
    const ev = eventByUuid(username, String(params.uuid));
    if (!ev) return fail(9000, "Không tìm thấy đợt chi tiêu.", 404);
    const balance = computeBalance(username, ev);
    const rows = [
      ["Thành viên", "Đã ứng", "Phải gánh", "Cân bằng"],
      ...balance.rows.map((r) => [
        r.memberName,
        String(r.advanced),
        String(r.owed),
        String(r.balance),
      ]),
    ];
    const csv =
      `﻿${ev.name}\r\n\r\n` +
      rows.map((r) => r.join(",")).join("\r\n") +
      "\r\n";
    return new HttpResponse(csv, {
      headers: {
        "Content-Type": "text/csv; charset=utf-8",
        "Content-Disposition": `attachment; filename="event-${ev.uuid}.csv"`,
      },
    });
  }),

  http.put("*/api/v1/events/:uuid/close", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const ev = eventByUuid(username, String(params.uuid));
    if (!ev) return fail(9000, "Không tìm thấy đợt chi tiêu.", 404);
    if (ev.isClosed) return fail(9001, "Đợt chi tiêu đã chốt.", 400);
    ev.isClosed = true;
    ev.closedAt = new Date().toISOString();
    return ok({ message: "Đã chốt đợt chi tiêu." });
  }),

  http.get("*/api/v1/events/:uuid", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const ev = eventByUuid(username, String(params.uuid));
    if (!ev) return fail(9000, "Không tìm thấy đợt chi tiêu.", 404);
    return ok(eventResponse(username, ev));
  }),

  http.put("*/api/v1/events/:uuid", async ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const ev = eventByUuid(username, String(params.uuid));
    if (!ev) return fail(9000, "Không tìm thấy đợt chi tiêu.", 404);
    if (ev.isClosed) return fail(9001, "Đợt chi tiêu đã chốt.", 400);
    const body = (await request.json()) as {
      name?: unknown;
      description?: unknown;
      startDate?: unknown;
      endDate?: unknown;
    };
    const name = typeof body.name === "string" ? body.name.trim() : "";
    if (name.length === 0 || name.length > 200) {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        name: ["Tên đợt không được để trống."],
      });
    }
    if (typeof body.startDate !== "string" || typeof body.endDate !== "string") {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        startDate: ["Vui lòng chọn ngày bắt đầu."],
      });
    }
    const start = dayBounds(body.startDate);
    const end = dayBounds(body.endDate);
    if (end.end < start.start) {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        endDate: ["Ngày kết thúc phải sau hoặc bằng ngày bắt đầu."],
      });
    }
    // A range that would exclude an already-assigned expense → 9003.
    const assigned = getExpenses(username).filter((e) => e.eventUuid === ev.uuid);
    const excludes = assigned.some(
      (e) => e.expenseTime < start.start || e.expenseTime > end.end,
    );
    if (excludes) {
      return fail(
        9003,
        "Khoảng thời gian mới loại một phiếu đã gán ra ngoài đợt.",
        400,
      );
    }
    ev.name = name;
    ev.description =
      typeof body.description === "string" && body.description
        ? body.description
        : null;
    ev.startDate = start.start;
    ev.endDate = end.end;
    return ok(eventResponse(username, ev));
  }),

  http.delete("*/api/v1/events/:uuid", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const list = getEvents(username);
    const idx = list.findIndex((e) => e.uuid === params.uuid);
    if (idx === -1) return fail(9000, "Không tìm thấy đợt chi tiêu.", 404);
    if (list[idx].isClosed) return fail(9001, "Đợt chi tiêu đã chốt.", 400);
    // Expenses become loose (SET NULL) — never deleted.
    for (const e of getExpenses(username)) {
      if (e.eventUuid === list[idx].uuid) e.eventUuid = null;
    }
    list.splice(idx, 1);
    return ok({ message: "Đã xóa đợt chi tiêu." });
  }),

  // --- Stats (M6) ---------------------------------------------------------
  http.get("*/api/v1/stats/overview", ({ request }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const url = new URL(request.url);
    const from = url.searchParams.get("from");
    const to = url.searchParams.get("to");
    if (from && to && from > to) {
      return fail(1001, "Khoảng thời gian không hợp lệ.", 400);
    }
    let list = getExpenses(username).slice();
    if (from) list = list.filter((e) => e.expenseTime >= from);
    if (to) list = list.filter((e) => e.expenseTime <= to);
    const totalSpending = list.reduce((sum, e) => sum + expenseTotal(e), 0);
    return ok({
      from: from ?? null,
      to: to ?? null,
      totalSpending,
      expenseCount: list.length,
    });
  }),

  http.get("*/api/v1/stats/by-category", ({ request }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    const url = new URL(request.url);
    const from = url.searchParams.get("from");
    const to = url.searchParams.get("to");
    const eventUuid = url.searchParams.get("eventUuid");
    // Time-range XOR event.
    if (eventUuid && (from || to)) {
      return fail(1001, "Không dùng đồng thời đợt và khoảng thời gian.", 400);
    }
    if (from && to && from > to) {
      return fail(1001, "Khoảng thời gian không hợp lệ.", 400);
    }
    let list = getExpenses(username).slice();
    if (eventUuid) {
      const ev = eventByUuid(username, eventUuid);
      if (!ev) return fail(9000, "Không tìm thấy đợt chi tiêu.", 404);
      list = list.filter((e) => e.eventUuid === eventUuid);
    } else {
      if (from) list = list.filter((e) => e.expenseTime >= from);
      if (to) list = list.filter((e) => e.expenseTime <= to);
    }
    // Group by category (deleted-with-history categories are included).
    const groups = new Map<string, { total: number; count: number }>();
    for (const e of list) {
      const g = groups.get(e.categoryUuid) ?? { total: 0, count: 0 };
      g.total += expenseTotal(e);
      g.count += 1;
      groups.set(e.categoryUuid, g);
    }
    const rows = [...groups.entries()]
      .map(([uuid, g]) => {
        const c = categoryByUuid(username, uuid);
        return {
          categoryUuid: uuid,
          categoryName: c?.name ?? "(không rõ)",
          color: c?.color ?? "#6B7280",
          icon: c?.icon ?? null,
          isDeleted: c?.isDeleted ?? false,
          total: g.total,
          expenseCount: g.count,
        };
      })
      // total DESC → count DESC → name (vi collation), matching the backend.
      .sort(
        (a, b) =>
          b.total - a.total ||
          b.expenseCount - a.expenseCount ||
          a.categoryName.localeCompare(b.categoryName, "vi"),
      );
    return ok({
      eventUuid: eventUuid ?? null,
      from: eventUuid ? null : (from ?? null),
      to: eventUuid ? null : (to ?? null),
      rows,
    });
  }),

  // --- Bank accounts (M7) — reads Free, mutations Premium (403 13003) ------
  http.get("*/api/v1/bank-accounts", ({ request }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    return ok(sortBankAccounts(getBankAccounts(username)).map(bankAccountResponse));
  }),

  http.post("*/api/v1/bank-accounts", async ({ request }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    if (!isPremiumUser(username)) return premiumGate();
    const body = (await request.json()) as Record<string, unknown>;
    const fields = validateBankAccount(body);
    if (fields) return fail(1001, "Dữ liệu không hợp lệ.", 400, fields);
    const list = getBankAccounts(username);
    const record: BankAccountRecord = {
      uuid: `ba-${rand()}`,
      bankBin: String(body.bankBin).trim(),
      bankName: String(body.bankName).trim(),
      accountNumber: String(body.accountNumber).trim(),
      accountHolderName: String(body.accountHolderName).trim(),
      isDefault: list.length === 0, // first account auto-default
      createdAt: new Date().toISOString(),
    };
    list.push(record);
    return ok(bankAccountResponse(record));
  }),

  http.put("*/api/v1/bank-accounts/:uuid/default", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    if (!isPremiumUser(username)) return premiumGate();
    const list = getBankAccounts(username);
    const target = list.find((a) => a.uuid === params.uuid);
    if (!target) return fail(12000, "Không tìm thấy tài khoản ngân hàng.", 404);
    for (const a of list) a.isDefault = false;
    target.isDefault = true;
    return ok({ message: "Đã đặt tài khoản ngân hàng mặc định." });
  }),

  http.put("*/api/v1/bank-accounts/:uuid", async ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    if (!isPremiumUser(username)) return premiumGate();
    const account = getBankAccounts(username).find((a) => a.uuid === params.uuid);
    if (!account) return fail(12000, "Không tìm thấy tài khoản ngân hàng.", 404);
    const body = (await request.json()) as Record<string, unknown>;
    const fields = validateBankAccount(body);
    if (fields) return fail(1001, "Dữ liệu không hợp lệ.", 400, fields);
    account.bankBin = String(body.bankBin).trim();
    account.bankName = String(body.bankName).trim();
    account.accountNumber = String(body.accountNumber).trim();
    account.accountHolderName = String(body.accountHolderName).trim();
    return ok(bankAccountResponse(account));
  }),

  http.delete("*/api/v1/bank-accounts/:uuid", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    if (!isPremiumUser(username)) return premiumGate();
    const list = getBankAccounts(username);
    const idx = list.findIndex((a) => a.uuid === params.uuid);
    if (idx === -1) return fail(12000, "Không tìm thấy tài khoản ngân hàng.", 404);
    const wasDefault = list[idx].isDefault;
    list.splice(idx, 1);
    // Delete-of-default promotes the most-recently-added remaining account.
    if (wasDefault && list.length > 0) {
      const newest = list.reduce((a, b) => (a.createdAt >= b.createdAt ? a : b));
      newest.isDefault = true;
    }
    return ok({ message: "Đã xóa tài khoản ngân hàng." });
  }),

  // --- QR (M7) — both endpoints Premium (403 13003) -----------------------
  http.get("*/api/v1/expenses/:uuid/qr", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    if (!isPremiumUser(username)) return premiumGate();
    const expense = getExpenses(username).find((e) => e.uuid === params.uuid);
    if (!expense) return fail(6000, "Không tìm thấy phiếu chi tiêu.", 404);
    const override = new URL(request.url).searchParams.get("bankAccountUuid");
    const accounts = getBankAccounts(username);
    if (accounts.length === 0) {
      return fail(12001, "Chưa có tài khoản ngân hàng nhận tiền.", 400);
    }
    if (override && !accounts.some((a) => a.uuid === override)) {
      return fail(12000, "Không tìm thấy tài khoản ngân hàng.", 404);
    }
    return pngResponse(`expense-qr-${expense.uuid}.png`);
  }),

  http.get("*/api/v1/events/:uuid/qr", ({ request, params }) => {
    const username = usernameFromAuthHeader(
      request.headers.get("Authorization"),
    );
    if (!username) {
      return fail(1002, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", 401);
    }
    if (!isPremiumUser(username)) return premiumGate();
    const ev = eventByUuid(username, String(params.uuid));
    if (!ev) return fail(9000, "Không tìm thấy đợt chi tiêu.", 404);
    if (!ev.isClosed) return fail(12002, "Đợt chưa được chốt.", 400);
    const balance = computeBalance(username, ev);
    const someoneOwes = balance.rows.some((r) => r.balance < 0);
    if (!someoneOwes) {
      return fail(12003, "Không còn ai nợ trong đợt này.", 400);
    }
    const override = new URL(request.url).searchParams.get("bankAccountUuid");
    const accounts = getBankAccounts(username);
    if (accounts.length === 0) {
      return fail(12001, "Chưa có tài khoản ngân hàng nhận tiền.", 400);
    }
    if (override && !accounts.some((a) => a.uuid === override)) {
      return fail(12000, "Không tìm thấy tài khoản ngân hàng.", 404);
    }
    return pngResponse(`event-qr-${ev.uuid}.png`);
  }),

  // --- VietQR bank directory (external, raw fetch — NOT our envelope) -------
  // The bank picker fetches this directly (not via client.ts). Returns a RAW
  // array (the third party doesn't speak ApiResult<T>), including the seeded BINs
  // 970436 (Vietcombank) + 970407 (Techcombank) so the pick→store→table
  // round-trip is demonstrable, plus one entry with an invalid caiValue to
  // exercise the normalize drop-filter. onUnhandledRequest:"error" means these
  // absolute-URL handlers are required for the tests; they also make dev
  // mock-mode work offline.
  http.get("https://vietqr.vn/api/vietqr/banks", () =>
    HttpResponse.json([
      {
        id: "vqr-vcb",
        bankCode: "VCB",
        bankName: "Ngân hàng TMCP Ngoại Thương Việt Nam",
        bankShortName: "Vietcombank",
        imageId: "d0e196fc-3d4c-4501-b453-ac8c3df968cf",
        status: 0,
        caiValue: "970436",
        unlinkedType: 0,
      },
      {
        id: "vqr-tcb",
        bankCode: "TCB",
        bankName: "Ngân hàng TMCP Kỹ thương Việt Nam",
        bankShortName: "Techcombank",
        imageId: "97c7b39e-812c-48b5-8126-16e187cfe91b",
        status: 0,
        caiValue: "970407",
        unlinkedType: 0,
      },
      {
        id: "vqr-bidv",
        bankCode: "BIDV",
        bankName: "Ngân hàng TMCP Đầu tư và Phát triển Việt Nam",
        bankShortName: "BIDV",
        imageId: "cb18c1b3-d661-4695-b2e8-dba8e887abd6",
        status: 0,
        caiValue: "970418",
        unlinkedType: 0,
      },
      {
        id: "vqr-mb",
        bankCode: "MB",
        bankName: "Ngân hàng TMCP Quân đội",
        bankShortName: "MBBank",
        imageId: "58b7190b-a294-4b14-968f-cd365593893e",
        status: 0,
        caiValue: "970422",
        unlinkedType: 0,
      },
      {
        // Invalid caiValue (not 6 digits) — MUST be dropped by normalize().
        id: "vqr-bad",
        bankCode: "BAD",
        bankName: "Ngân hàng không hợp lệ",
        bankShortName: "BadBank",
        imageId: "00000000-0000-0000-0000-000000000000",
        status: 0,
        caiValue: "12AB",
        unlinkedType: 0,
      },
    ]),
  ),

  http.get("https://vietqr.vn/api/vietqr/images/:imageId", ({ params }) =>
    pngResponse(`bank-logo-${String(params.imageId)}.png`),
  ),

  // --- Admin (M8) — account metadata + tier-grant/revenue ONLY (R10) -------
  http.get("*/api/v1/admin/dashboard", ({ request }) => {
    const gate = adminGate(request);
    if (!gate.ok) return gate.response;
    const url = new URL(request.url);
    const from = url.searchParams.get("from");
    const to = url.searchParams.get("to");
    const bucket = url.searchParams.get("bucket") === "day" ? "day" : "month";
    const bucketKey = (iso: string) =>
      bucket === "day" ? iso.slice(0, 10) : iso.slice(0, 7);

    const users = getAdminUsers();
    const countBy = (pick: (u: AdminUserRecord) => string) => {
      const map = new Map<string, number>();
      for (const u of users) map.set(pick(u), (map.get(pick(u)) ?? 0) + 1);
      return [...map.entries()].map(([key, count]) => ({ key, count }));
    };

    const signupMap = new Map<string, number>();
    for (const u of users) {
      if (!inRange(u.createdAt, from, to)) continue;
      const key = bucketKey(u.createdAt);
      signupMap.set(key, (signupMap.get(key) ?? 0) + 1);
    }
    const signups = [...signupMap.entries()]
      .sort((a, b) => (a[0] < b[0] ? -1 : 1))
      .map(([periodLabel, count]) => ({ periodLabel, count }));

    return ok({
      from,
      to,
      totalUsers: users.length,
      tierDistribution: countBy((u) => u.tier),
      roleDistribution: countBy((u) => u.role),
      statusDistribution: countBy((u) => u.status),
      signups,
    });
  }),

  http.get("*/api/v1/admin/revenue", ({ request }) => {
    const gate = adminGate(request);
    if (!gate.ok) return gate.response;
    const url = new URL(request.url);
    const from = url.searchParams.get("from");
    const to = url.searchParams.get("to");
    const bucket = url.searchParams.get("bucket") === "day" ? "day" : "month";
    const bucketKey = (iso: string) =>
      bucket === "day" ? iso.slice(0, 10) : iso.slice(0, 7);

    const grants = getAdminUsers()
      .flatMap((u) => u.grants)
      .filter((g) => g.action === "GRANT" && inRange(g.createdAt, from, to));

    const bucketMap = new Map<string, { total: number; grantCount: number }>();
    for (const g of grants) {
      const key = bucketKey(g.createdAt);
      const cur = bucketMap.get(key) ?? { total: 0, grantCount: 0 };
      cur.total += g.amount;
      cur.grantCount += 1;
      bucketMap.set(key, cur);
    }
    const buckets = [...bucketMap.entries()]
      .sort((a, b) => (a[0] < b[0] ? -1 : 1))
      .map(([periodLabel, v]) => ({ periodLabel, ...v }));

    const references = grants
      .filter((g) => g.reference)
      .sort((a, b) => (a.createdAt < b.createdAt ? 1 : -1))
      .map((g) => g.reference as string);

    return ok({
      from,
      to,
      bucket,
      buckets,
      totalRevenue: grants.reduce((sum, g) => sum + g.amount, 0),
      grantCount: grants.length,
      references,
    });
  }),

  http.get("*/api/v1/admin/users", ({ request }) => {
    const gate = adminGate(request);
    if (!gate.ok) return gate.response;
    const url = new URL(request.url);
    const tier = url.searchParams.get("tier");
    const status = url.searchParams.get("status");
    const role = url.searchParams.get("role");
    const search = (url.searchParams.get("search") ?? "").toLowerCase();
    const page = Math.max(1, Number(url.searchParams.get("page")) || 1);
    const pageSize = Math.min(
      100,
      Math.max(1, Number(url.searchParams.get("pageSize")) || 20),
    );
    const sort = url.searchParams.get("sort") ?? "createdAt";
    const direction = url.searchParams.get("direction") === "asc" ? "asc" : "desc";

    let list = getAdminUsers().slice();
    if (tier) list = list.filter((u) => u.tier === tier);
    if (status) list = list.filter((u) => u.status === status);
    if (role) list = list.filter((u) => u.role === role);
    if (search) list = list.filter((u) => u.username.toLowerCase().includes(search));

    list.sort((a, b) => {
      let cmp = 0;
      if (sort === "username") cmp = a.username.localeCompare(b.username, "vi");
      else if (sort === "tier") cmp = a.tier.localeCompare(b.tier);
      else if (sort === "status") cmp = a.status.localeCompare(b.status);
      else cmp = a.createdAt < b.createdAt ? -1 : a.createdAt > b.createdAt ? 1 : 0;
      return direction === "asc" ? cmp : -cmp;
    });

    const totalCount = list.length;
    const start = (page - 1) * pageSize;
    const items = list.slice(start, start + pageSize).map(adminUserRow);
    return ok({
      items,
      page,
      pageSize,
      totalCount,
      totalPages: Math.ceil(totalCount / pageSize),
    });
  }),

  http.get("*/api/v1/admin/users/:uuid", ({ request, params }) => {
    const gate = adminGate(request);
    if (!gate.ok) return gate.response;
    const user = getAdminUsers().find((u) => u.uuid === params.uuid);
    if (!user) return fail(14000, "Không tìm thấy người dùng.", 404);
    return ok({
      uuid: user.uuid,
      username: user.username,
      tier: user.tier,
      role: user.role,
      status: user.status,
      createdAt: user.createdAt,
      grants: [...user.grants]
        .sort((a, b) => (a.createdAt < b.createdAt ? 1 : -1))
        .map(grantRowResponse),
    });
  }),

  http.post("*/api/v1/admin/users/:uuid/tier/grant", async ({ request, params }) => {
    const gate = adminGate(request);
    if (!gate.ok) return gate.response;
    const user = getAdminUsers().find((u) => u.uuid === params.uuid);
    if (!user) return fail(14000, "Không tìm thấy người dùng.", 404);
    const body = (await request.json()) as {
      amount?: number;
      currency?: string;
      reference?: string;
      note?: string;
    };
    if (typeof body.amount !== "number" || body.amount < 0) {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        amount: ["Số tiền không được âm."],
      });
    }
    const grant: AdminGrantRecord = {
      uuid: `tg-${rand()}`,
      tier: "PREMIUM",
      action: "GRANT",
      amount: body.amount,
      currency: body.currency || "VND",
      reference: body.reference || null,
      note: body.note || null,
      grantedByUsername: gate.username,
      createdAt: new Date().toISOString(),
    };
    user.grants.push(grant);
    user.tier = "PREMIUM";
    return ok(grantRowResponse(grant));
  }),

  http.post("*/api/v1/admin/users/:uuid/tier/revoke", async ({ request, params }) => {
    const gate = adminGate(request);
    if (!gate.ok) return gate.response;
    const user = getAdminUsers().find((u) => u.uuid === params.uuid);
    if (!user) return fail(14000, "Không tìm thấy người dùng.", 404);
    const body = (await request.json()) as { note?: string };
    const grant: AdminGrantRecord = {
      uuid: `tg-${rand()}`,
      tier: "FREE",
      action: "REVOKE",
      amount: 0,
      currency: "VND",
      reference: null,
      note: body.note || null,
      grantedByUsername: gate.username,
      createdAt: new Date().toISOString(),
    };
    user.grants.push(grant);
    user.tier = "FREE";
    return ok(grantRowResponse(grant));
  }),

  http.post("*/api/v1/admin/users/:uuid/disable", ({ request, params }) => {
    const gate = adminGate(request);
    if (!gate.ok) return gate.response;
    const user = getAdminUsers().find((u) => u.uuid === params.uuid);
    if (!user) return fail(14000, "Không tìm thấy người dùng.", 404);
    const guard = ensureDestructiveAllowed(gate.uuid, user);
    if (guard) return guard;
    user.status = "DISABLED";
    return ok({ message: "Đã khóa tài khoản." });
  }),

  http.post("*/api/v1/admin/users/:uuid/enable", ({ request, params }) => {
    const gate = adminGate(request);
    if (!gate.ok) return gate.response;
    const user = getAdminUsers().find((u) => u.uuid === params.uuid);
    if (!user) return fail(14000, "Không tìm thấy người dùng.", 404);
    user.status = "ACTIVE";
    return ok({ message: "Đã mở khóa tài khoản." });
  }),

  http.post("*/api/v1/admin/users/:uuid/revoke-tokens", ({ request, params }) => {
    const gate = adminGate(request);
    if (!gate.ok) return gate.response;
    const user = getAdminUsers().find((u) => u.uuid === params.uuid);
    if (!user) return fail(14000, "Không tìm thấy người dùng.", 404);
    const guard = ensureDestructiveAllowed(gate.uuid, user);
    if (guard) return guard;
    return ok({ message: "Đã thu hồi toàn bộ phiên." });
  }),

  http.post("*/api/v1/admin/users/:uuid/reset-password", async ({ request, params }) => {
    const gate = adminGate(request);
    if (!gate.ok) return gate.response;
    const user = getAdminUsers().find((u) => u.uuid === params.uuid);
    if (!user) return fail(14000, "Không tìm thấy người dùng.", 404);
    const guard = ensureDestructiveAllowed(gate.uuid, user);
    if (guard) return guard;
    const body = (await request.json()) as { newPassword?: string };
    const newPassword = body.newPassword ?? "";
    if (newPassword.length < 8) {
      return fail(1001, "Dữ liệu không hợp lệ.", 400, {
        newPassword: ["Mật khẩu tối thiểu 8 ký tự."],
      });
    }
    return ok({ username: user.username, password: newPassword });
  }),

  http.post("*/api/v1/admin/users/:uuid/role", async ({ request, params }) => {
    const gate = adminGate(request);
    if (!gate.ok) return gate.response;
    const user = getAdminUsers().find((u) => u.uuid === params.uuid);
    if (!user) return fail(14000, "Không tìm thấy người dùng.", 404);
    const body = (await request.json()) as { role?: "USER" | "ADMIN" };
    const role = body.role === "ADMIN" ? "ADMIN" : "USER";
    // Demotion is destructive: guard self / another admin (14001/14002).
    if (role === "USER") {
      const guard = ensureDestructiveAllowed(gate.uuid, user);
      if (guard) return guard;
    }
    user.role = role;
    return ok({ message: "Đã cập nhật vai trò." });
  }),
];
