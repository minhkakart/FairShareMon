import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { act, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import {
  tagsKeys,
  useCreateTag,
  useDeleteTag,
  useRenameTag,
  useTagsQuery,
} from "./hooks/useTags";
import type { TagResponse } from "./api/types";

/**
 * Tag hooks over MSW at the network boundary — exercises the REAL centralized
 * client + TanStack Query (never mocks the hook). The mutations invalidate via
 * the singleton `queryClient`, so these specs render against that same client and
 * assert a second GET fires (list refetch).
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}
function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}

const TAG: TagResponse = {
  uuid: "t-1",
  name: "Công tác",
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

describe("tagsKeys", () => {
  it("TagsKeys_ListKey_IsScopedUnderTheInvalidationRoot", () => {
    expect(tagsKeys.all).toEqual(["tags"]);
    expect(tagsKeys.list(true)).toEqual(["tags", "list", true]);
    expect(tagsKeys.list(false)).toEqual(["tags", "list", false]);
  });
});

describe("useTagsQuery param wiring", () => {
  it("UseTagsQuery_IncludeDeletedTrue_SendsIncludeDeletedTrue", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/tags", ({ request }) => {
        seenUrl = request.url;
        return ok([]);
      }),
    );

    function Probe() {
      useTagsQuery(true);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(seenUrl).not.toBe(""));
    expect(new URL(seenUrl).searchParams.get("includeDeleted")).toBe("true");
  });

  it("UseTagsQuery_IncludeDeletedFalse_SendsIncludeDeletedFalse", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/tags", ({ request }) => {
        seenUrl = request.url;
        return ok([]);
      }),
    );

    function Probe() {
      useTagsQuery(false);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(seenUrl).not.toBe(""));
    expect(new URL(seenUrl).searchParams.get("includeDeleted")).toBe("false");
  });
});

describe("mutation verb/path/body + invalidation", () => {
  function countingListHandler(counter: { n: number }) {
    return http.get("*/api/v1/tags", () => {
      counter.n += 1;
      return ok([TAG]);
    });
  }

  it("UseCreateTag_PostsBodyThenInvalidatesAndRefetches", async () => {
    const gets = { n: 0 };
    let postBody: unknown;
    const captured: { create?: ReturnType<typeof useCreateTag> } = {};
    server.use(
      countingListHandler(gets),
      http.post("*/api/v1/tags", async ({ request }) => {
        postBody = await request.json();
        return ok(TAG);
      }),
    );

    function Probe() {
      useTagsQuery(false);
      captured.create = useCreateTag();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(gets.n).toBe(1));
    await act(async () => {
      await captured.create!.mutateAsync({ name: "Du lịch" });
    });
    expect(postBody).toEqual({ name: "Du lịch" });
    await waitFor(() => expect(gets.n).toBe(2));
  });

  it("UseRenameTag_PutsToUuidPathThenInvalidatesAndRefetches", async () => {
    const gets = { n: 0 };
    let seenPath = "";
    const captured: { rename?: ReturnType<typeof useRenameTag> } = {};
    server.use(
      countingListHandler(gets),
      http.put("*/api/v1/tags/:uuid", ({ request }) => {
        seenPath = new URL(request.url).pathname;
        return ok(TAG);
      }),
    );

    function Probe() {
      useTagsQuery(false);
      captured.rename = useRenameTag();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(gets.n).toBe(1));
    await act(async () => {
      await captured.rename!.mutateAsync({
        uuid: "t-9",
        body: { name: "Đổi" },
      });
    });
    expect(seenPath).toBe("/api/v1/tags/t-9");
    await waitFor(() => expect(gets.n).toBe(2));
  });

  it("UseDeleteTag_DeletesUuidPathThenInvalidatesAndRefetches", async () => {
    const gets = { n: 0 };
    let seenPath = "";
    const captured: { remove?: ReturnType<typeof useDeleteTag> } = {};
    server.use(
      countingListHandler(gets),
      http.delete("*/api/v1/tags/:uuid", ({ request }) => {
        seenPath = new URL(request.url).pathname;
        return ok({ message: "ok" });
      }),
    );

    function Probe() {
      useTagsQuery(false);
      captured.remove = useDeleteTag();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(gets.n).toBe(1));
    await act(async () => {
      await captured.remove!.mutateAsync("t-3");
    });
    expect(seenPath).toBe("/api/v1/tags/t-3");
    await waitFor(() => expect(gets.n).toBe(2));
  });
});
