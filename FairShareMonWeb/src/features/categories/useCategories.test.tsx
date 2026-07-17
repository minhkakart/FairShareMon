import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { act, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import {
  categoriesKeys,
  useCategoriesQuery,
  useCreateCategory,
  useDeleteCategory,
  useSetDefaultCategory,
  useUpdateCategory,
} from "./hooks/useCategories";
import type { CategoryResponse } from "./api/types";

/**
 * Category hooks over MSW at the network boundary — exercises the REAL
 * centralized client + TanStack Query (never mocks the hook). The mutations
 * invalidate via the singleton `queryClient`, so these specs render against that
 * same client and assert a second GET fires (list refetch).
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}
function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}

const CATEGORY: CategoryResponse = {
  uuid: "c-1",
  name: "Ăn uống",
  color: "#F97316",
  icon: "🍜",
  isDefault: true,
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

describe("categoriesKeys", () => {
  it("CategoriesKeys_ListKey_IsScopedUnderTheInvalidationRoot", () => {
    // The mutation invalidation root ["categories"] must prefix every list key so
    // a single invalidate covers both toggle states + the default swap.
    expect(categoriesKeys.all).toEqual(["categories"]);
    expect(categoriesKeys.list(true)).toEqual(["categories", "list", true]);
    expect(categoriesKeys.list(false)).toEqual(["categories", "list", false]);
  });
});

describe("useCategoriesQuery param wiring", () => {
  it("UseCategoriesQuery_IncludeDeletedTrue_SendsIncludeDeletedTrue", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/categories", ({ request }) => {
        seenUrl = request.url;
        return ok([]);
      }),
    );

    function Probe() {
      useCategoriesQuery(true);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(seenUrl).not.toBe(""));
    expect(new URL(seenUrl).searchParams.get("includeDeleted")).toBe("true");
  });

  it("UseCategoriesQuery_IncludeDeletedFalse_SendsIncludeDeletedFalse", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/categories", ({ request }) => {
        seenUrl = request.url;
        return ok([]);
      }),
    );

    function Probe() {
      useCategoriesQuery(false);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(seenUrl).not.toBe(""));
    expect(new URL(seenUrl).searchParams.get("includeDeleted")).toBe("false");
  });
});

describe("mutation verb/path/body + invalidation", () => {
  function countingListHandler(counter: { n: number }) {
    return http.get("*/api/v1/categories", () => {
      counter.n += 1;
      return ok([CATEGORY]);
    });
  }

  it("UseCreateCategory_PostsBodyThenInvalidatesAndRefetches", async () => {
    const gets = { n: 0 };
    let postBody: unknown;
    const captured: { create?: ReturnType<typeof useCreateCategory> } = {};
    server.use(
      countingListHandler(gets),
      http.post("*/api/v1/categories", async ({ request }) => {
        postBody = await request.json();
        return ok(CATEGORY);
      }),
    );

    function Probe() {
      useCategoriesQuery(false);
      captured.create = useCreateCategory();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(gets.n).toBe(1));
    await act(async () => {
      await captured.create!.mutateAsync({
        name: "Cà phê",
        color: "#0EA5E9",
        icon: "☕",
      });
    });
    // The request carries the create body verbatim…
    expect(postBody).toEqual({ name: "Cà phê", color: "#0EA5E9", icon: "☕" });
    // …and onSuccess invalidated ["categories"] → the list query refetched.
    await waitFor(() => expect(gets.n).toBe(2));
  });

  it("UseUpdateCategory_PutsToUuidPathThenInvalidatesAndRefetches", async () => {
    const gets = { n: 0 };
    let seenPath = "";
    const captured: { update?: ReturnType<typeof useUpdateCategory> } = {};
    server.use(
      countingListHandler(gets),
      http.put("*/api/v1/categories/:uuid", ({ request }) => {
        seenPath = new URL(request.url).pathname;
        return ok(CATEGORY);
      }),
    );

    function Probe() {
      useCategoriesQuery(false);
      captured.update = useUpdateCategory();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(gets.n).toBe(1));
    await act(async () => {
      await captured.update!.mutateAsync({
        uuid: "c-9",
        body: { name: "Sửa", color: "#000000", icon: null },
      });
    });
    expect(seenPath).toBe("/api/v1/categories/c-9");
    await waitFor(() => expect(gets.n).toBe(2));
  });

  it("UseSetDefaultCategory_PutsToDefaultSubpathThenInvalidatesAndRefetches", async () => {
    const gets = { n: 0 };
    let seenPath = "";
    const captured: { setDefault?: ReturnType<typeof useSetDefaultCategory> } =
      {};
    server.use(
      countingListHandler(gets),
      http.put("*/api/v1/categories/:uuid/default", ({ request }) => {
        seenPath = new URL(request.url).pathname;
        return ok({ message: "ok" });
      }),
    );

    function Probe() {
      useCategoriesQuery(false);
      captured.setDefault = useSetDefaultCategory();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(gets.n).toBe(1));
    await act(async () => {
      await captured.setDefault!.mutateAsync("c-7");
    });
    expect(seenPath).toBe("/api/v1/categories/c-7/default");
    await waitFor(() => expect(gets.n).toBe(2));
  });

  it("UseDeleteCategory_DeletesUuidPathThenInvalidatesAndRefetches", async () => {
    const gets = { n: 0 };
    let seenPath = "";
    const captured: { remove?: ReturnType<typeof useDeleteCategory> } = {};
    server.use(
      countingListHandler(gets),
      http.delete("*/api/v1/categories/:uuid", ({ request }) => {
        seenPath = new URL(request.url).pathname;
        return ok({ message: "ok" });
      }),
    );

    function Probe() {
      useCategoriesQuery(false);
      captured.remove = useDeleteCategory();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(gets.n).toBe(1));
    await act(async () => {
      await captured.remove!.mutateAsync("c-3");
    });
    expect(seenPath).toBe("/api/v1/categories/c-3");
    await waitFor(() => expect(gets.n).toBe(2));
  });
});
