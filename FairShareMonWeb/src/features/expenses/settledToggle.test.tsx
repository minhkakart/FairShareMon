import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { useEventBalanceQuery } from "@/features/events/hooks/useEvents";
import { SettledToggle } from "./components/SettledToggle";

/**
 * SettledToggle regression (OQ1a) — the shipped whole-expense settled toggle now
 * renders on the shared presentational `SettledSwitch`; its public behavior must
 * be UNCHANGED. It is a color-independent `role="switch"` (icon + đã trả/chưa trả
 * text, never color alone), flips via `PUT /v1/expenses/{uuid}/settled`, toasts
 * the verbatim outcome, and surfaces a server error verbatim. It carries a
 * distinct accessible name. Network mocked at the client boundary (MSW).
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}
function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}
function fail(code: number, message: string, status: number) {
  return HttpResponse.json<Envelope>(
    { data: null, isSuccess: false, error: { code, message } },
    { status },
  );
}

const UUID = "e-1";
const EVENT_UUID = "ev-toggle";

/** Subscribes to the event balance query so its invalidation (or lack thereof)
 *  is observable via the mocked GET's request count — mirrors the
 *  `useEvents.test.tsx`/`memberSettled.test.tsx` counters+Probe pattern. */
function BalanceProbe({ uuid }: { uuid: string }) {
  useEventBalanceQuery(uuid);
  return null;
}

function emptyBalance() {
  return {
    eventUuid: EVENT_UUID,
    eventName: "Đà Lạt",
    isClosed: false,
    rows: [],
    totalOutstanding: 0,
    owingMemberCount: 0,
    settledMemberCount: 0,
    partiallySettledMemberCount: 0,
  };
}

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-settled-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-settled-t",
    refreshTokenExpiresAt: future,
    user: { username: "settled", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

function renderToggle(isSettled = false) {
  return renderWithProviders(
    <SettledToggle uuid={UUID} isSettled={isSettled} contextName="Thuê xe" />,
    { queryClient },
  );
}

beforeEach(async () => {
  window.localStorage.clear();
  queryClient.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
  seedSession();
});

afterEach(async () => {
  sessionStore.getState().clearSession();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("SettledToggle regression", () => {
  it("SettledToggle_Unsettled_RendersColorIndependentSwitchWithNamedContext", () => {
    renderToggle(false);
    const toggle = screen.getByRole("switch", {
      name: "Trạng thái đã trả của Thuê xe",
    });
    expect(toggle).toHaveAttribute("aria-checked", "false");
    // State is carried by TEXT (not color alone): the unsettled label is visible.
    expect(toggle).toHaveTextContent("Chưa trả");
    expect(toggle).toBeEnabled();
  });

  it("SettledToggle_ClickWhenUnsettled_PutsSettledTrueThenToasts", async () => {
    let body: { isSettled?: boolean } | null = null;
    let path = "";
    server.use(
      http.put(`*/api/v1/expenses/${UUID}/settled`, async ({ request }) => {
        path = new URL(request.url).pathname;
        body = (await request.json()) as typeof body;
        return ok({ message: "Đã cập nhật trạng thái đã trả." });
      }),
    );
    const user = userEvent.setup();
    renderToggle(false);

    await user.click(screen.getByRole("switch"));

    expect(await screen.findByText("Đã đánh dấu là đã trả.")).toBeInTheDocument();
    expect(path).toBe(`/api/v1/expenses/${UUID}/settled`);
    expect(body).toEqual({ isSettled: true });
  });

  it("SettledToggle_ClickWhenSettled_PutsSettledFalseThenToastsOff", async () => {
    let body: { isSettled?: boolean } | null = null;
    server.use(
      http.put(`*/api/v1/expenses/${UUID}/settled`, async ({ request }) => {
        body = (await request.json()) as typeof body;
        return ok({ message: "Đã bỏ đánh dấu đã trả." });
      }),
    );
    const user = userEvent.setup();
    renderToggle(true);

    // Starts settled (color-independent: "Đã trả" text + aria-checked).
    const toggle = screen.getByRole("switch");
    expect(toggle).toHaveAttribute("aria-checked", "true");
    expect(toggle).toHaveTextContent("Đã trả");

    await user.click(toggle);

    expect(await screen.findByText("Đã bỏ đánh dấu đã trả.")).toBeInTheDocument();
    expect(body).toEqual({ isSettled: false });
  });

  it("SettledToggle_ServerError_ShowsToastVerbatim", async () => {
    server.use(
      http.put(`*/api/v1/expenses/${UUID}/settled`, () =>
        fail(6000, "Không tìm thấy phiếu chi tiêu.", 404),
      ),
    );
    const user = userEvent.setup();
    renderToggle(false);

    await user.click(screen.getByRole("switch"));

    // The verbatim server message is toasted (branch on code, render message).
    expect(
      await screen.findByText("Không tìm thấy phiếu chi tiêu."),
    ).toBeInTheDocument();
    // No success toast leaked.
    expect(
      screen.queryByText("Đã đánh dấu là đã trả."),
    ).not.toBeInTheDocument();
    // The switch is usable again after the failed mutation settles.
    await waitFor(() => expect(screen.getByRole("switch")).toBeEnabled());
  });
});

describe("SettledToggle event cross-invalidation (event-expense-settlement-sync M2.2/M2.5)", () => {
  it("SettledToggle_MarkSettled_OnEventExpense_AlsoInvalidatesEventBalance", async () => {
    // The M2.2 regression: with `eventUuid` set, a successful whole-expense
    // settle now also credits/claws back the event balance overlay's
    // `clearedAmount`, so the mutation's onSuccess must additionally invalidate
    // `eventsKeys.balance(eventUuid)`. Mount a live `useEventBalanceQuery`
    // subscriber (the counters+Probe pattern) so that invalidation has an
    // ACTIVE query to refetch, and count the GETs it fires.
    let balanceRequests = 0;
    server.use(
      http.put(`*/api/v1/expenses/${UUID}/settled`, () =>
        ok({ message: "Đã cập nhật trạng thái đã trả." }),
      ),
      http.get(`*/api/v1/events/${EVENT_UUID}/balance`, () => {
        balanceRequests += 1;
        return ok(emptyBalance());
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(
      <>
        <SettledToggle
          uuid={UUID}
          isSettled={false}
          contextName="Thuê xe"
          eventUuid={EVENT_UUID}
        />
        <BalanceProbe uuid={EVENT_UUID} />
      </>,
      { queryClient },
    );

    await waitFor(() => expect(balanceRequests).toBe(1));

    await user.click(screen.getByRole("switch"));

    // The "synced" toast copy (distinct from the plain toastOn/toastOff pair)
    // confirms the `eventUuid`-gated toast branch fired.
    expect(
      await screen.findByText(
        "Đã cập nhật đã trả — số dư còn nợ của đợt đã được đồng bộ tương ứng.",
      ),
    ).toBeInTheDocument();
    // Before the M2.2 fix, `eventsKeys.balance` was never reached — the fix's
    // whole point is that a second GET now fires.
    await waitFor(() => expect(balanceRequests).toBeGreaterThanOrEqual(2));
  });

  it("SettledToggle_LooseExpense_NoEventUuid_NeverRequestsEventBalance", async () => {
    // Regression for "no event, no invalidation": a loose expense (`eventUuid`
    // undefined) must not touch `eventsKeys.balance` at all — the plain
    // toastOn/toastOff pair stays, and the probe's balance query is never
    // refetched by this mutation.
    let balanceRequests = 0;
    server.use(
      http.put(`*/api/v1/expenses/${UUID}/settled`, () =>
        ok({ message: "Đã cập nhật trạng thái đã trả." }),
      ),
      http.get(`*/api/v1/events/${EVENT_UUID}/balance`, () => {
        balanceRequests += 1;
        return ok(emptyBalance());
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(
      <>
        <SettledToggle uuid={UUID} isSettled={false} contextName="Thuê xe" />
        <BalanceProbe uuid={EVENT_UUID} />
      </>,
      { queryClient },
    );

    await waitFor(() => expect(balanceRequests).toBe(1));

    await user.click(screen.getByRole("switch"));

    expect(
      await screen.findByText("Đã đánh dấu là đã trả."),
    ).toBeInTheDocument();
    // No `eventUuid` prop → the mutation's `if (eventUuid)` branch never runs →
    // the balance probe's request count stays exactly at its initial mount GET.
    expect(balanceRequests).toBe(1);
  });
});
