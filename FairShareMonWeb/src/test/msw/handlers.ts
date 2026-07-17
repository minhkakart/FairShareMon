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
];
