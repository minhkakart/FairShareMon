import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { registerTestProfile } from "@/test/msw/handlers";
import { sessionStore } from "@/lib/auth/session";
import { setActiveLocale } from "@/lib/api/runtime";
import { api } from "@/lib/api/client";
import { ApiError } from "@/lib/api/errors";
import { membersApi } from "@/features/members/api/membersApi";
import { eventsApi } from "./api/eventsApi";
import { expensesApi } from "@/features/expenses/api/expensesApi";

/**
 * QR "who still owes" (OQ8a) — an end-to-end contract test over the REAL committed
 * MSW handlers (no React): the closed-event QR is billed server-side to members
 * with `outstanding > 0`. Marking one of two owing members settled still bills the
 * remainder; once every owing member is cleared the QR yields `12003`
 * ("đã trả hết"). This proves the two Layer-B behaviors the QrDialog relies on —
 * the per-member settled write + the outstanding-driven QR filter — without any
 * QrDialog logic change. A fresh Premium user isolates the in-memory store.
 */

let seq = 0;

async function bootPremiumUser(): Promise<string> {
  seq += 1;
  const username = `qruser${seq}`;
  registerTestProfile(username, "PREMIUM");
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: `access-${username}-t`,
    accessTokenExpiresAt: future,
    refreshToken: `refresh-${username}-t`,
    refreshTokenExpiresAt: future,
    user: { username, tier: "PREMIUM", role: "USER" },
    profileStatus: "resolved",
  });
  return username;
}

/** Build a closed event with two OWING members (An 300.000, Bình 200.000) via the
 *  committed handlers, returning the event + owing member uuids. */
async function seedClosedEventWithTwoOwing() {
  const members = await membersApi.list(false);
  const an = members.find((m) => m.name === "An Nguyễn")!;
  const binh = members.find((m) => m.name === "Bình Trần")!;

  const event = await eventsApi.create({
    name: "Đà Lạt",
    description: null,
    startDate: "2026-07-15T12:00:00.000Z",
    endDate: "2026-07-15T12:00:00.000Z",
  });

  // Payer defaults to the owner-rep, who advances the whole 500.000; An + Bình
  // bear it → both owe (balance < 0).
  await expensesApi.create({
    name: "Thuê xe",
    expenseTime: "2026-07-15T10:00:00.000Z",
    eventUuid: event.uuid,
    shares: [
      { memberUuid: an.uuid, amount: 300000 },
      { memberUuid: binh.uuid, amount: 200000 },
    ],
  });

  await eventsApi.close(event.uuid);
  return { eventUuid: event.uuid, anUuid: an.uuid, binhUuid: binh.uuid };
}

/** Resolves to the QR error code, or 0 when the QR was generated (blob). */
async function qrCode(eventUuid: string): Promise<number> {
  try {
    const result = await api.blob("GET", `/v1/events/${eventUuid}/qr`);
    return result.blob.size >= 0 ? 0 : -1;
  } catch (error) {
    return error instanceof ApiError ? error.code : -1;
  }
}

beforeEach(() => {
  window.localStorage.clear();
  setActiveLocale("vi-VN");
});

afterEach(() => {
  sessionStore.getState().clearSession();
  setActiveLocale("vi-VN");
});

describe("Event QR outstanding filter (OQ8a)", () => {
  it("EventQr_TwoOwingMembers_IsBilledAndBalanceReportsOverlay", async () => {
    await bootPremiumUser();
    const { eventUuid } = await seedClosedEventWithTwoOwing();

    // The overlay: both members still owe (500.000 total còn nợ).
    const balance = await eventsApi.balance(eventUuid);
    expect(balance.owingMemberCount).toBe(2);
    expect(balance.totalOutstanding).toBe(500000);

    // Someone owes → the QR is generated (a blob, not 12003).
    expect(await qrCode(eventUuid)).toBe(0);
  });

  it("EventQr_OneOfTwoMarkedSettled_StillBillsTheRemainder", async () => {
    await bootPremiumUser();
    const { eventUuid, anUuid } = await seedClosedEventWithTwoOwing();

    await eventsApi.setMemberSettled(eventUuid, anUuid, { isSettled: true });

    const balance = await eventsApi.balance(eventUuid);
    // An cleared; Bình still owes 200.000.
    expect(balance.owingMemberCount).toBe(1);
    expect(balance.settledMemberCount).toBe(1);
    expect(balance.totalOutstanding).toBe(200000);

    // The remainder still owes → the QR is generated.
    expect(await qrCode(eventUuid)).toBe(0);
  });

  it("EventQr_AllOwingMembersSettled_Yields12003", async () => {
    await bootPremiumUser();
    const { eventUuid, anUuid, binhUuid } = await seedClosedEventWithTwoOwing();

    await eventsApi.setMemberSettled(eventUuid, anUuid, { isSettled: true });
    await eventsApi.setMemberSettled(eventUuid, binhUuid, { isSettled: true });

    const balance = await eventsApi.balance(eventUuid);
    expect(balance.owingMemberCount).toBe(0);
    expect(balance.totalOutstanding).toBe(0);

    // Everyone has cleared → nobody is billed → 12003 ("đã trả hết").
    expect(await qrCode(eventUuid)).toBe(12003);
  });

  it("EventMemberSettled_NonParticipant_Yields3000", async () => {
    await bootPremiumUser();
    const { eventUuid } = await seedClosedEventWithTwoOwing();

    // A member uuid that never participated in the event → resource-owned 3000.
    let code = -1;
    try {
      await eventsApi.setMemberSettled(eventUuid, "m-nonparticipant", {
        isSettled: true,
      });
    } catch (error) {
      code = error instanceof ApiError ? error.code : -1;
    }
    expect(code).toBe(3000);
  });
});
