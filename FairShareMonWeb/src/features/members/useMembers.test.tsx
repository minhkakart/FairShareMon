import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { act, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import {
  membersKeys,
  useCreateMember,
  useDeleteMember,
  useMembersQuery,
  useRenameMember,
} from "./hooks/useMembers";
import type { MemberResponse } from "./api/types";

/**
 * Member hooks over MSW at the network boundary — exercises the REAL centralized
 * client + TanStack Query (never mocks the hook). The hooks invalidate via the
 * singleton `queryClient` (the `useAuth`/`invalidateCurrentUser` convention), so
 * these specs render against that same client and assert a second GET fires.
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}
function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}

const MEMBER: MemberResponse = {
  uuid: "m-1",
  name: "An Nguyễn",
  isOwnerRepresentative: false,
  isDeleted: false,
  createdAt: "2026-01-01T00:00:00+00:00",
};

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-demo-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-demo-t",
    refreshTokenExpiresAt: future,
    user: { username: "demo", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

beforeEach(() => {
  window.localStorage.clear();
  queryClient.clear();
  seedSession();
});

afterEach(() => {
  sessionStore.getState().clearSession();
});

describe("membersKeys", () => {
  it("MembersKeys_ListKey_IsScopedUnderTheInvalidationRoot", () => {
    // The mutation invalidation root ["members"] must prefix every list key so a
    // single invalidate covers both toggle states.
    expect(membersKeys.all).toEqual(["members"]);
    expect(membersKeys.list(true)).toEqual(["members", "list", true]);
    expect(membersKeys.list(false)).toEqual(["members", "list", false]);
  });
});

describe("useMembersQuery param wiring", () => {
  it("UseMembersQuery_IncludeDeletedTrue_SendsIncludeDeletedTrue", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/members", ({ request }) => {
        seenUrl = request.url;
        return ok([]);
      }),
    );

    function Probe() {
      useMembersQuery(true);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(seenUrl).not.toBe(""));
    expect(new URL(seenUrl).searchParams.get("includeDeleted")).toBe("true");
  });

  it("UseMembersQuery_IncludeDeletedFalse_SendsIncludeDeletedFalse", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/members", ({ request }) => {
        seenUrl = request.url;
        return ok([]);
      }),
    );

    function Probe() {
      useMembersQuery(false);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(seenUrl).not.toBe(""));
    expect(new URL(seenUrl).searchParams.get("includeDeleted")).toBe("false");
  });
});

describe("mutation invalidation", () => {
  function countingListHandler(counter: { n: number }) {
    return http.get("*/api/v1/members", () => {
      counter.n += 1;
      return ok([MEMBER]);
    });
  }

  it("UseCreateMember_OnSuccess_InvalidatesMembersAndRefetchesList", async () => {
    const gets = { n: 0 };
    const captured: { create?: ReturnType<typeof useCreateMember> } = {};
    server.use(
      countingListHandler(gets),
      http.post("*/api/v1/members", () => ok(MEMBER)),
    );

    function Probe() {
      useMembersQuery(false);
      captured.create = useCreateMember();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(gets.n).toBe(1));
    await act(async () => {
      await captured.create!.mutateAsync({ name: "New" });
    });
    // onSuccess invalidated ["members"] → the list query refetched.
    await waitFor(() => expect(gets.n).toBe(2));
  });

  it("UseRenameMember_OnSuccess_InvalidatesMembersAndRefetchesList", async () => {
    const gets = { n: 0 };
    const captured: { rename?: ReturnType<typeof useRenameMember> } = {};
    server.use(
      countingListHandler(gets),
      http.put("*/api/v1/members/:uuid", () => ok(MEMBER)),
    );

    function Probe() {
      useMembersQuery(false);
      captured.rename = useRenameMember();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(gets.n).toBe(1));
    await act(async () => {
      await captured.rename!.mutateAsync({
        uuid: "m-1",
        body: { name: "Renamed" },
      });
    });
    await waitFor(() => expect(gets.n).toBe(2));
  });

  it("UseDeleteMember_OnSuccess_InvalidatesMembersAndRefetchesList", async () => {
    const gets = { n: 0 };
    const captured: { remove?: ReturnType<typeof useDeleteMember> } = {};
    server.use(
      countingListHandler(gets),
      http.delete("*/api/v1/members/:uuid", () =>
        ok({ message: "Đã xóa thành viên." }),
      ),
    );

    function Probe() {
      useMembersQuery(false);
      captured.remove = useDeleteMember();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(gets.n).toBe(1));
    await act(async () => {
      await captured.remove!.mutateAsync("m-1");
    });
    await waitFor(() => expect(gets.n).toBe(2));
  });
});
