import { afterEach, describe, expect, it } from "vitest";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { isApiError } from "@/lib/api/errors";
import { banksApi } from "./api/banksApi";
import type { Bank } from "./api/banksApi";

/**
 * banksApi — the bank directory over OUR `GET /v1/banks` through the centralized
 * client. The network is mocked at the client boundary (MSW). Asserts the list
 * call returns the unwrapped `Bank[]` (envelope handled centrally) and that a
 * failure surfaces as a typed `ApiError`. No normalization/drop-filter/logo-url
 * builder anymore — the backend owns the shape and builds `logoUrl` server-side.
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}

const BANK_VCB: Bank = {
  bin: "970436",
  code: "VCB",
  name: "Ngân hàng TMCP Ngoại Thương Việt Nam",
  shortName: "Vietcombank",
  logoUrl: "https://vietqr.vn/api/vietqr/images/img-vcb",
};
const BANK_TCB: Bank = {
  bin: "970407",
  code: "TCB",
  name: "Ngân hàng TMCP Kỹ thương Việt Nam",
  shortName: "Techcombank",
  logoUrl: "https://vietqr.vn/api/vietqr/images/img-tcb",
};

afterEach(() => server.resetHandlers());

describe("banksApi.list", () => {
  it("BanksApi_Success_ReturnsUnwrappedBankList", async () => {
    server.use(
      http.get("*/api/v1/banks", () =>
        HttpResponse.json<Envelope>({
          data: [BANK_VCB, BANK_TCB],
          isSuccess: true,
          error: null,
        }),
      ),
    );

    const banks = await banksApi.list();

    // The `ApiResult<T>` envelope is unwrapped by the central client → `Bank[]`.
    expect(banks).toHaveLength(2);
    expect(banks[0]).toEqual(BANK_VCB);
    expect(banks[1].bin).toBe("970407");
    expect(banks[1].shortName).toBe("Techcombank");
    expect(banks[1].logoUrl).toBe(
      "https://vietqr.vn/api/vietqr/images/img-tcb",
    );
  });

  it("BanksApi_Failure_ThrowsTypedApiError", async () => {
    server.use(
      http.get("*/api/v1/banks", () =>
        HttpResponse.json<Envelope>(
          { data: null, isSuccess: false, error: { code: 1000, message: "Đã xảy ra lỗi máy chủ." } },
          { status: 500 },
        ),
      ),
    );

    const error = await banksApi.list().then(
      () => {
        throw new Error("expected banksApi.list() to reject");
      },
      (err: unknown) => err,
    );
    expect(isApiError(error)).toBe(true);
    if (isApiError(error)) {
      expect(error.code).toBe(1000);
    }
  });
});
