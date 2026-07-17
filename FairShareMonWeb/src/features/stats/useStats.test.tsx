import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import {
  statsKeys,
  useByCategoryQuery,
  useOverviewQuery,
} from "./hooks/useStats";
import type {
  ByCategoryStatsRequest,
  ByCategoryStatsResponse,
  OverviewStatsResponse,
  StatsRangeRequest,
} from "./api/types";

/**
 * Stats query hooks over MSW at the network boundary — exercises the REAL
 * centralized client + TanStack Query (never mocks the hook). Proves: the query
 * key factory shape; `from`/`to`/`eventUuid` are sent ONLY when defined (the
 * client drops empty keys, so all-time omits both bounds); the `ApiResult<T>`
 * envelope unwraps to `data`; `enabled=false` fires no request; and an
 * identical-valued range dedupes to one network call (stable cache key).
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}
function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-statshook-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-statshook-t",
    refreshTokenExpiresAt: future,
    user: { username: "statshook", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

const OVERVIEW: OverviewStatsResponse = {
  from: null,
  to: null,
  totalSpending: 1250000,
  expenseCount: 7,
};
const BY_CATEGORY: ByCategoryStatsResponse = {
  eventUuid: null,
  from: null,
  to: null,
  rows: [],
};

beforeEach(() => {
  window.localStorage.clear();
  seedSession();
});

afterEach(() => {
  sessionStore.getState().clearSession();
});

describe("statsKeys", () => {
  it("StatsKeys_OverviewAndByCategory_AreScopedUnderTheStatsRoot", () => {
    // The root ["stats"] prefixes every sub-key (invalidation root).
    expect(statsKeys.all).toEqual(["stats"]);
    const range: StatsRangeRequest = { from: "a", to: "b" };
    expect(statsKeys.overview(range)).toEqual(["stats", "overview", range]);
    const req: ByCategoryStatsRequest = { from: "a" };
    expect(statsKeys.byCategory(req)).toEqual(["stats", "by-category", req]);
    expect(statsKeys.overview(range)[0]).toBe(statsKeys.all[0]);
  });
});

describe("useOverviewQuery wiring", () => {
  it("UseOverviewQuery_DefinedRange_SendsFromAndToAndUnwrapsData", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/stats/overview", ({ request }) => {
        seenUrl = request.url;
        return ok(OVERVIEW);
      }),
    );
    const captured: { data?: OverviewStatsResponse } = {};
    function Probe() {
      const q = useOverviewQuery({
        from: "2026-07-01T00:00:00.000Z",
        to: "2026-07-31T23:59:59.999Z",
      });
      captured.data = q.data;
      return null;
    }
    renderWithProviders(<Probe />);

    await waitFor(() => expect(captured.data).toBeDefined());
    const params = new URL(seenUrl).searchParams;
    expect(params.get("from")).toBe("2026-07-01T00:00:00.000Z");
    expect(params.get("to")).toBe("2026-07-31T23:59:59.999Z");
    // Envelope unwrapped → the raw `data`, verbatim.
    expect(captured.data).toEqual(OVERVIEW);
  });

  it("UseOverviewQuery_AllTimeEmptyRange_OmitsBothBounds", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/stats/overview", ({ request }) => {
        seenUrl = request.url;
        return ok(OVERVIEW);
      }),
    );
    function Probe() {
      useOverviewQuery({});
      return null;
    }
    renderWithProviders(<Probe />);

    await waitFor(() => expect(seenUrl).not.toBe(""));
    const params = new URL(seenUrl).searchParams;
    expect(params.has("from")).toBe(false);
    expect(params.has("to")).toBe(false);
  });

  it("UseOverviewQuery_Disabled_FiresNoRequest", async () => {
    let overviewCalls = 0;
    let sentinelCalls = 0;
    server.use(
      http.get("*/api/v1/stats/overview", () => {
        overviewCalls += 1;
        return ok(OVERVIEW);
      }),
      http.get("*/api/v1/stats/by-category", () => {
        sentinelCalls += 1;
        return ok(BY_CATEGORY);
      }),
    );
    function Probe() {
      useOverviewQuery({ from: "x" }, false); // disabled
      useByCategoryQuery({ from: "x" }, true); // enabled sentinel
      return null;
    }
    renderWithProviders(<Probe />);

    // Once the enabled sentinel has fired, the disabled query must still be silent.
    await waitFor(() => expect(sentinelCalls).toBe(1));
    expect(overviewCalls).toBe(0);
  });
});

describe("useByCategoryQuery wiring", () => {
  it("UseByCategoryQuery_DefinedKeys_SendsFromToEventUuidAndUnwrapsData", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/stats/by-category", ({ request }) => {
        seenUrl = request.url;
        return ok(BY_CATEGORY);
      }),
    );
    const captured: { data?: ByCategoryStatsResponse } = {};
    function Probe() {
      const q = useByCategoryQuery({
        from: "2026-07-01T00:00:00.000Z",
        to: "2026-07-31T23:59:59.999Z",
        eventUuid: "ev-1",
      });
      captured.data = q.data;
      return null;
    }
    renderWithProviders(<Probe />);

    await waitFor(() => expect(captured.data).toBeDefined());
    const params = new URL(seenUrl).searchParams;
    expect(params.get("from")).toBe("2026-07-01T00:00:00.000Z");
    expect(params.get("to")).toBe("2026-07-31T23:59:59.999Z");
    expect(params.get("eventUuid")).toBe("ev-1");
    expect(captured.data).toEqual(BY_CATEGORY);
  });

  it("UseByCategoryQuery_OnlyEventUuid_OmitsRangeBounds", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/stats/by-category", ({ request }) => {
        seenUrl = request.url;
        return ok(BY_CATEGORY);
      }),
    );
    function Probe() {
      useByCategoryQuery({ eventUuid: "ev-1" });
      return null;
    }
    renderWithProviders(<Probe />);

    await waitFor(() => expect(seenUrl).not.toBe(""));
    const params = new URL(seenUrl).searchParams;
    expect(params.get("eventUuid")).toBe("ev-1");
    expect(params.has("from")).toBe(false);
    expect(params.has("to")).toBe(false);
  });
});

describe("cache key stability per range", () => {
  it("UseOverviewQuery_SameValuedRangeTwoInstances_DedupeToOneCall", async () => {
    let calls = 0;
    server.use(
      http.get("*/api/v1/stats/overview", () => {
        calls += 1;
        return ok(OVERVIEW);
      }),
    );
    // Two DIFFERENT object instances with identical contents must hash to the
    // same query key → a single shared network request.
    function Probe() {
      useOverviewQuery({ from: "2026-07-01T00:00:00.000Z" });
      useOverviewQuery({ from: "2026-07-01T00:00:00.000Z" });
      return null;
    }
    renderWithProviders(<Probe />);

    await waitFor(() => expect(calls).toBe(1));
    // Give any stray duplicate a chance to (not) fire.
    await Promise.resolve();
    expect(calls).toBe(1);
  });
});
