import { afterEach, describe, expect, it } from "vitest";
import { waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { useBankByBin, useVietqrBanks } from "./hooks/useVietqrBanks";
import { VIETQR_BANKS_SNAPSHOT } from "./data/vietqrBanks";
import type { VietqrBank } from "./api/vietqrDirectoryApi";

/**
 * useVietqrBanks — the TanStack Query directory hook seeded by the committed
 * snapshot. The raw VietQR fetch is mocked at the boundary (MSW absolute-URL
 * handler). Asserts: a live success replaces the snapshot with the mapped live
 * list; a fetch failure falls back to the snapshot so `data` is NEVER empty; and
 * `useBankByBin` selects a seeded BIN / returns undefined for an unknown one. Each
 * render uses a fresh per-render QueryClient (isolated, retries off).
 */

const BANKS_URL = "https://vietqr.vn/api/vietqr/banks";

// A small deterministic live payload (raw shape) distinct in size from the
// committed 58-bank snapshot, so "live replaced the snapshot" is observable.
const LIVE_RAW = [
  {
    id: "vqr-vcb",
    bankCode: "VCB",
    bankName: "Ngân hàng TMCP Ngoại Thương Việt Nam",
    bankShortName: "Vietcombank",
    imageId: "img-vcb",
    status: 0,
    caiValue: "970436",
    unlinkedType: 0,
  },
  {
    id: "vqr-tcb",
    bankCode: "TCB",
    bankName: "Ngân hàng TMCP Kỹ thương Việt Nam",
    bankShortName: "Techcombank",
    imageId: "img-tcb",
    status: 0,
    caiValue: "970407",
    unlinkedType: 0,
  },
];

afterEach(() => server.resetHandlers());

describe("useVietqrBanks", () => {
  it("UseVietqrBanks_LiveSuccess_ReplacesSnapshotWithMappedList", async () => {
    server.use(http.get(BANKS_URL, () => HttpResponse.json(LIVE_RAW)));
    const captured: { data?: VietqrBank[] } = {};
    function Probe() {
      captured.data = useVietqrBanks().data;
      return null;
    }
    renderWithProviders(<Probe />);

    // The background refresh (initialDataUpdatedAt:0 → stale) resolves to the
    // 2-bank live payload, replacing the 58-bank snapshot seed.
    await waitFor(() => expect(captured.data).toHaveLength(2));
    expect(captured.data?.find((b) => b.bin === "970436")?.shortName).toBe(
      "Vietcombank",
    );
  });

  it("UseVietqrBanks_FetchError_FallsBackToSnapshotNeverEmpty", async () => {
    server.use(http.get(BANKS_URL, () => HttpResponse.error()));
    const captured: {
      data?: VietqrBank[];
      isFetching?: boolean;
    } = {};
    function Probe() {
      const q = useVietqrBanks();
      captured.data = q.data;
      captured.isFetching = q.isFetching;
      return null;
    }
    renderWithProviders(<Probe />);

    // After the failed fetch settles, the queryFn's catch returns the snapshot —
    // the picker is never emptied.
    await waitFor(() => expect(captured.isFetching).toBe(false));
    expect(captured.data).toHaveLength(VIETQR_BANKS_SNAPSHOT.length);
    expect(captured.data?.length).toBeGreaterThan(0);
    expect(captured.data?.some((b) => b.bin === "970436")).toBe(true);
  });

  it("UseVietqrBanks_EmptyLiveList_KeepsSnapshot", async () => {
    // A live 200 with an empty array must not empty the picker either.
    server.use(http.get(BANKS_URL, () => HttpResponse.json([])));
    const captured: { data?: VietqrBank[]; isFetching?: boolean } = {};
    function Probe() {
      const q = useVietqrBanks();
      captured.data = q.data;
      captured.isFetching = q.isFetching;
      return null;
    }
    renderWithProviders(<Probe />);

    await waitFor(() => expect(captured.isFetching).toBe(false));
    expect(captured.data).toHaveLength(VIETQR_BANKS_SNAPSHOT.length);
  });
});

describe("useBankByBin", () => {
  it("UseBankByBin_SeededBin_ReturnsDirectoryBank", () => {
    const captured: { bank?: VietqrBank } = {};
    function Probe() {
      captured.bank = useBankByBin("970436");
      return null;
    }
    // initialData (snapshot) is available on first paint — no wait needed.
    renderWithProviders(<Probe />);
    expect(captured.bank?.shortName).toBe("Vietcombank");
  });

  it("UseBankByBin_UnknownBin_ReturnsUndefined", () => {
    const captured: { bank?: VietqrBank | undefined } = { bank: undefined };
    function Probe() {
      captured.bank = useBankByBin("999999");
      return null;
    }
    renderWithProviders(<Probe />);
    expect(captured.bank).toBeUndefined();
  });

  it("UseBankByBin_UndefinedBin_ReturnsUndefined", () => {
    const captured: { bank?: VietqrBank | undefined } = { bank: undefined };
    function Probe() {
      captured.bank = useBankByBin(undefined);
      return null;
    }
    renderWithProviders(<Probe />);
    expect(captured.bank).toBeUndefined();
  });
});
