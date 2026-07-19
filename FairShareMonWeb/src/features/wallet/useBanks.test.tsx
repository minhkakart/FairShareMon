import { afterEach, describe, expect, it } from "vitest";
import { waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { useBankByBin, useBanks } from "./hooks/useBanks";
import type { Bank } from "./api/banksApi";

/**
 * useBanks — the TanStack Query directory hook over OUR `GET /v1/banks`. The
 * network is mocked at the client boundary (MSW). No snapshot seed / offline
 * fallback anymore (the backend guarantees a non-empty list), so `data` resolves
 * from the endpoint. Asserts the loaded list, and that `useBankByBin` selects a
 * seeded BIN / returns undefined for an unknown or undefined BIN. Each render uses
 * a fresh per-render QueryClient (isolated, retries off).
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}
function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}

const LIVE: Bank[] = [
  {
    bin: "970436",
    code: "VCB",
    name: "Ngân hàng TMCP Ngoại Thương Việt Nam",
    shortName: "Vietcombank",
    logoUrl: "https://vietqr.vn/api/vietqr/images/img-vcb",
  },
  {
    bin: "970407",
    code: "TCB",
    name: "Ngân hàng TMCP Kỹ thương Việt Nam",
    shortName: "Techcombank",
    logoUrl: "https://vietqr.vn/api/vietqr/images/img-tcb",
  },
];

afterEach(() => server.resetHandlers());

describe("useBanks", () => {
  it("UseBanks_Success_LoadsListFromEndpoint", async () => {
    server.use(http.get("*/api/v1/banks", () => ok(LIVE)));
    const captured: { data?: Bank[] } = {};
    function Probe() {
      captured.data = useBanks().data;
      return null;
    }
    renderWithProviders(<Probe />);

    await waitFor(() => expect(captured.data).toHaveLength(2));
    expect(captured.data?.find((b) => b.bin === "970436")?.shortName).toBe(
      "Vietcombank",
    );
  });
});

describe("useBankByBin", () => {
  it("UseBankByBin_SeededBin_ReturnsDirectoryBank", async () => {
    server.use(http.get("*/api/v1/banks", () => ok(LIVE)));
    const captured: { bank?: Bank } = {};
    function Probe() {
      captured.bank = useBankByBin("970436");
      return null;
    }
    renderWithProviders(<Probe />);

    await waitFor(() =>
      expect(captured.bank?.shortName).toBe("Vietcombank"),
    );
  });

  it("UseBankByBin_UnknownBin_ReturnsUndefined", async () => {
    server.use(http.get("*/api/v1/banks", () => ok(LIVE)));
    const captured: { bank?: Bank | undefined; loaded?: boolean } = {
      bank: undefined,
    };
    function Probe() {
      const all = useBanks();
      captured.loaded = all.isSuccess;
      captured.bank = useBankByBin("999999");
      return null;
    }
    renderWithProviders(<Probe />);

    // Even once the list has loaded, an unknown BIN selects nothing.
    await waitFor(() => expect(captured.loaded).toBe(true));
    expect(captured.bank).toBeUndefined();
  });

  it("UseBankByBin_UndefinedBin_ReturnsUndefined", () => {
    server.use(http.get("*/api/v1/banks", () => ok(LIVE)));
    const captured: { bank?: Bank | undefined } = { bank: undefined };
    function Probe() {
      captured.bank = useBankByBin(undefined);
      return null;
    }
    renderWithProviders(<Probe />);
    expect(captured.bank).toBeUndefined();
  });
});
