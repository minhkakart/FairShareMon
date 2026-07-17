import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { useExpensesQuery } from "@/features/expenses/hooks/useExpenses";
import {
  eventsKeys,
  useCloseEvent,
  useCreateEvent,
  useDeleteEvent,
  useEventBalanceQuery,
  useEventQuery,
  useEventsQuery,
  useExportEvent,
  useUpdateEvent,
} from "./hooks/useEvents";
import type { EventFilter } from "./api/types";

/**
 * Event hooks over MSW at the network boundary — the REAL centralized client +
 * TanStack Query (never mocks the hook). The list query sends only a defined
 * `closed`; create/update/close/delete cross-invalidate BOTH the ["events"] root
 * (list + detail + balance) and, for the writes that change linkage/counts, the
 * ["expenses"] root. `downloadBlob` is mocked to assert the export mutation
 * triggers the browser download.
 */

const downloadSpy = vi.fn();
vi.mock("@/lib/download/downloadBlob", () => ({
  downloadBlob: (...args: unknown[]) => downloadSpy(...args),
}));

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}
function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}

const EVENT_UUID = "ev-1";

function minimalEvent() {
  return {
    uuid: EVENT_UUID,
    name: "Đà Lạt",
    startDate: "2026-07-12T00:00:00+07:00",
    endDate: "2026-07-18T23:59:59+07:00",
    isClosed: false,
    closedAt: null,
    expenseCount: 0,
    createdAt: "2026-07-01T00:00:00+00:00",
  };
}
function minimalBalance() {
  return { eventUuid: EVENT_UUID, eventName: "Đà Lạt", isClosed: false, rows: [] };
}

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-evhook-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-evhook-t",
    refreshTokenExpiresAt: future,
    user: { username: "evhook", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

beforeEach(() => {
  window.localStorage.clear();
  queryClient.clear();
  downloadSpy.mockClear();
  seedSession();
});

afterEach(() => {
  sessionStore.getState().clearSession();
});

describe("eventsKeys", () => {
  it("EventsKeys_ListDetailBalance_AreScopedUnderTheInvalidationRoot", () => {
    // The ["events"] root must prefix every sub-key so one invalidate reaches the
    // list, detail, and balance caches.
    expect(eventsKeys.all).toEqual(["events"]);
    const filter: EventFilter = { closed: false };
    expect(eventsKeys.list(filter)).toEqual(["events", "list", filter]);
    expect(eventsKeys.detail("ev-9")).toEqual(["events", "detail", "ev-9"]);
    expect(eventsKeys.balance("ev-9")).toEqual(["events", "balance", "ev-9"]);
    expect(eventsKeys.list(filter)[0]).toBe(eventsKeys.all[0]);
    expect(eventsKeys.detail("ev-9")[0]).toBe(eventsKeys.all[0]);
  });
});

describe("useEventsQuery filter wiring", () => {
  it("UseEventsQuery_ClosedFalse_SentAsQueryParam", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/events", ({ request }) => {
        seenUrl = request.url;
        return ok([]);
      }),
    );
    function Probe() {
      useEventsQuery({ closed: false });
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(seenUrl).not.toBe(""));
    expect(new URL(seenUrl).searchParams.get("closed")).toBe("false");
  });

  it("UseEventsQuery_UndefinedClosed_IsDropped", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/events", ({ request }) => {
        seenUrl = request.url;
        return ok([]);
      }),
    );
    function Probe() {
      useEventsQuery({});
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(seenUrl).not.toBe(""));
    // The client drops undefined keys — no empty `closed` is sent.
    expect(new URL(seenUrl).searchParams.has("closed")).toBe(false);
  });
});

// ─── Cross-cache invalidation reach ───────────────────────────────────────────
describe("event mutation invalidation", () => {
  type Captured = {
    create?: ReturnType<typeof useCreateEvent>;
    update?: ReturnType<typeof useUpdateEvent>;
    close?: ReturnType<typeof useCloseEvent>;
    remove?: ReturnType<typeof useDeleteEvent>;
  };

  /** Mount the events list/detail/balance + the expenses list, count each GET. */
  function setup() {
    const counters = { list: 0, detail: 0, balance: 0, expenses: 0 };
    server.use(
      http.get("*/api/v1/events", () => {
        counters.list += 1;
        return ok([]);
      }),
      http.get(`*/api/v1/events/${EVENT_UUID}/balance`, () => {
        counters.balance += 1;
        return ok(minimalBalance());
      }),
      http.get(`*/api/v1/events/${EVENT_UUID}`, () => {
        counters.detail += 1;
        return ok(minimalEvent());
      }),
      http.get("*/api/v1/expenses", () => {
        counters.expenses += 1;
        return ok([]);
      }),
      http.post("*/api/v1/events", () => ok(minimalEvent())),
      http.put(`*/api/v1/events/${EVENT_UUID}/close`, () => ok({ message: "ok" })),
      http.put(`*/api/v1/events/${EVENT_UUID}`, () => ok(minimalEvent())),
      http.delete(`*/api/v1/events/${EVENT_UUID}`, () => ok({ message: "ok" })),
    );

    const captured: Captured = {};
    function Probe() {
      useEventsQuery({});
      useEventQuery(EVENT_UUID);
      useEventBalanceQuery(EVENT_UUID);
      useExpensesQuery({});
      captured.create = useCreateEvent();
      captured.update = useUpdateEvent();
      captured.close = useCloseEvent();
      captured.remove = useDeleteEvent();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });
    return { counters, captured };
  }

  async function waitInitialLoad(counters: {
    list: number;
    detail: number;
    balance: number;
    expenses: number;
  }) {
    await waitFor(() => {
      expect(counters.list).toBe(1);
      expect(counters.detail).toBe(1);
      expect(counters.balance).toBe(1);
      expect(counters.expenses).toBe(1);
    });
  }

  it("UseUpdateEvent_OnSuccess_InvalidatesEventsAndExpenses", async () => {
    const { counters, captured } = setup();
    await waitInitialLoad(counters);
    await act(async () => {
      await captured.update!.mutateAsync({
        uuid: EVENT_UUID,
        body: {
          name: "X",
          startDate: "2026-07-12T05:00:00.000Z",
          endDate: "2026-07-18T05:00:00.000Z",
        },
      });
    });
    // The events root reaches list + detail + balance; the expenses root refetches too.
    await waitFor(() => {
      expect(counters.list).toBeGreaterThanOrEqual(2);
      expect(counters.detail).toBeGreaterThanOrEqual(2);
      expect(counters.balance).toBeGreaterThanOrEqual(2);
      expect(counters.expenses).toBeGreaterThanOrEqual(2);
    });
  });

  it("UseCloseEvent_OnSuccess_InvalidatesEventsAndExpenses", async () => {
    const { counters, captured } = setup();
    await waitInitialLoad(counters);
    await act(async () => {
      await captured.close!.mutateAsync(EVENT_UUID);
    });
    await waitFor(() => {
      expect(counters.list).toBeGreaterThanOrEqual(2);
      expect(counters.detail).toBeGreaterThanOrEqual(2);
      expect(counters.balance).toBeGreaterThanOrEqual(2);
      expect(counters.expenses).toBeGreaterThanOrEqual(2);
    });
  });

  it("UseDeleteEvent_OnSuccess_InvalidatesEventsAndExpenses", async () => {
    const { counters, captured } = setup();
    await waitInitialLoad(counters);
    await act(async () => {
      await captured.remove!.mutateAsync(EVENT_UUID);
    });
    await waitFor(() => {
      expect(counters.list).toBeGreaterThanOrEqual(2);
      expect(counters.detail).toBeGreaterThanOrEqual(2);
      expect(counters.balance).toBeGreaterThanOrEqual(2);
      expect(counters.expenses).toBeGreaterThanOrEqual(2);
    });
  });

  it("UseCreateEvent_OnSuccess_InvalidatesEventsRootOnly", async () => {
    const { counters, captured } = setup();
    await waitInitialLoad(counters);
    await act(async () => {
      await captured.create!.mutateAsync({
        name: "New",
        startDate: "2026-07-12T05:00:00.000Z",
        endDate: "2026-07-18T05:00:00.000Z",
      });
    });
    // Create reaches the events list (root)…
    await waitFor(() => expect(counters.list).toBeGreaterThanOrEqual(2));
    // …but NOT the expenses root (a brand-new event has no expenses).
    expect(counters.expenses).toBe(1);
  });
});

// ─── CSV export → downloadBlob ────────────────────────────────────────────────
describe("useExportEvent", () => {
  it("UseExportEvent_OnSuccess_TriggersDownloadBlobWithServerFilenameAndFallback", async () => {
    server.use(
      http.get(`*/api/v1/events/${EVENT_UUID}/export`, () =>
        new HttpResponse("col1,col2\r\n", {
          headers: {
            "Content-Type": "text/csv; charset=utf-8",
            "Content-Disposition": 'attachment; filename="event-ev-1.csv"',
          },
        }),
      ),
    );
    const captured: { exp?: ReturnType<typeof useExportEvent> } = {};
    function Probe() {
      captured.exp = useExportEvent();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(captured.exp).toBeDefined());
    await act(async () => {
      await captured.exp!.mutateAsync({
        uuid: EVENT_UUID,
        fallbackName: "dot.csv",
      });
    });

    expect(downloadSpy).toHaveBeenCalledTimes(1);
    const [result, fallback] = downloadSpy.mock.calls[0];
    expect(fallback).toBe("dot.csv");
    expect(result).toHaveProperty("blob");
    expect((result as { filename?: string }).filename).toBe("event-ev-1.csv");
  });
});
