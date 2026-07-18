import { afterEach, describe, expect, it } from "vitest";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { bankLogoUrl, vietqrDirectoryApi } from "./api/vietqrDirectoryApi";

/**
 * vietqrDirectoryApi — THE sanctioned raw-`fetch` module (not the app client). The
 * network is mocked at the boundary (MSW), never the module. Asserts the normalize
 * contract from the plan: maps a raw array → VietqrBank[]; tolerates the
 * `{ data: [...] }` wrapper; drops entries whose caiValue is not exactly 6 digits;
 * throws on a non-ok response; and builds the public logo URL. Deterministic (the
 * VietQR origin is the env default `https://vietqr.vn`; no auth/app headers sent).
 */

const BANKS_URL = "https://vietqr.vn/api/vietqr/banks";

const RAW_VCB = {
  id: "vqr-vcb",
  bankCode: "VCB",
  bankName: "Ngân hàng TMCP Ngoại Thương Việt Nam",
  bankShortName: "Vietcombank",
  imageId: "img-vcb",
  status: 0,
  caiValue: "970436",
  unlinkedType: 0,
};
const RAW_TCB = {
  id: "vqr-tcb",
  bankCode: "TCB",
  bankName: "Ngân hàng TMCP Kỹ thương Việt Nam",
  bankShortName: "Techcombank",
  imageId: "img-tcb",
  status: 0,
  caiValue: "970407",
  unlinkedType: 0,
};
const RAW_INVALID = {
  id: "vqr-bad",
  bankCode: "BAD",
  bankName: "Ngân hàng không hợp lệ",
  bankShortName: "BadBank",
  imageId: "img-bad",
  status: 0,
  caiValue: "12AB", // not 6 digits → dropped
  unlinkedType: 0,
};

afterEach(() => server.resetHandlers());

describe("vietqrDirectoryApi.list", () => {
  it("VietqrDirectoryApi_RawArray_NormalizesAndMapsFields", async () => {
    server.use(http.get(BANKS_URL, () => HttpResponse.json([RAW_VCB, RAW_TCB])));

    const banks = await vietqrDirectoryApi.list();

    expect(banks).toHaveLength(2);
    // caiValue→bin, bankCode→code, bankName→name, bankShortName→shortName, imageId→imageId.
    expect(banks[0]).toEqual({
      bin: "970436",
      code: "VCB",
      name: "Ngân hàng TMCP Ngoại Thương Việt Nam",
      shortName: "Vietcombank",
      imageId: "img-vcb",
    });
    expect(banks[1].bin).toBe("970407");
    expect(banks[1].shortName).toBe("Techcombank");
  });

  it("VietqrDirectoryApi_DataWrapper_IsUnwrapped", async () => {
    server.use(
      http.get(BANKS_URL, () => HttpResponse.json({ data: [RAW_VCB] })),
    );

    const banks = await vietqrDirectoryApi.list();

    expect(banks).toHaveLength(1);
    expect(banks[0].bin).toBe("970436");
  });

  it("VietqrDirectoryApi_InvalidCaiValue_IsDropped", async () => {
    server.use(
      http.get(BANKS_URL, () =>
        HttpResponse.json([RAW_VCB, RAW_INVALID, RAW_TCB]),
      ),
    );

    const banks = await vietqrDirectoryApi.list();

    // The non-6-digit entry is filtered out; the two valid BINs remain.
    expect(banks).toHaveLength(2);
    expect(banks.map((b) => b.bin)).toEqual(["970436", "970407"]);
    expect(banks.some((b) => b.shortName === "BadBank")).toBe(false);
  });

  it("VietqrDirectoryApi_NonOkResponse_Throws", async () => {
    server.use(
      http.get(BANKS_URL, () => new HttpResponse(null, { status: 500 })),
    );

    await expect(vietqrDirectoryApi.list()).rejects.toThrow();
  });

  it("VietqrDirectoryApi_UnreadableShape_Throws", async () => {
    // Neither an array nor a { data: [] } wrapper → the hook's snapshot fallback.
    server.use(http.get(BANKS_URL, () => HttpResponse.json({ nope: true })));

    await expect(vietqrDirectoryApi.list()).rejects.toThrow();
  });

  it("VietqrDirectoryApi_AbortedSignal_RejectsWithAbortError", async () => {
    // The signal is wired into fetch, so a cancellation propagates as an
    // AbortError — the precondition for the hook rethrowing instead of
    // resolving a cancelled refetch as a snapshot "success".
    server.use(http.get(BANKS_URL, () => HttpResponse.json([RAW_VCB])));
    const controller = new AbortController();
    controller.abort();

    await expect(
      vietqrDirectoryApi.list(controller.signal),
    ).rejects.toMatchObject({ name: "AbortError" });
  });
});

describe("bankLogoUrl", () => {
  it("BankLogoUrl_BuildsPublicImageUrlFromImageId", () => {
    expect(bankLogoUrl("img-vcb")).toBe(
      "https://vietqr.vn/api/vietqr/images/img-vcb",
    );
  });
});
