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
];
