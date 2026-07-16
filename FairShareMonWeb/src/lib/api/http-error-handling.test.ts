import { describe, expect, it, vi } from "vitest";
import { ApiError, ErrorCodes } from "./errors";
import {
  applyFieldErrors,
  classifyError,
  resolveErrorMessage,
} from "./http-error-handling";
import type { AppTFunction } from "@/i18n/useT";

/**
 * The error-code → UX-intent mapping is a business rule that ships this cycle
 * (consumed by feature screens/error boundaries). Verifies the classification,
 * verbatim message rendering, and field-error mapping onto RHF.
 */

// Minimal t stub: echoes the key so we can assert which fallback copy is chosen.
const t = ((key: string) => key) as unknown as AppTFunction;

function apiError(
  code: number,
  message = "msg",
  fields?: Record<string, string[]>,
) {
  return new ApiError(code, message, 400, fields);
}

describe("classifyError", () => {
  it("classifyError_NotFoundAndOwnershipCodes_ReturnNotFound", () => {
    expect(classifyError(apiError(ErrorCodes.NotFound))).toBe("notFound");
    expect(classifyError(apiError(ErrorCodes.MemberNotFound))).toBe("notFound");
    expect(classifyError(apiError(ErrorCodes.ExpenseNotFound))).toBe(
      "notFound",
    );
    expect(classifyError(apiError(ErrorCodes.EventNotFound))).toBe("notFound");
  });

  it("classifyError_PremiumFeatureRequired13003_ReturnsPremiumRequired", () => {
    expect(classifyError(apiError(ErrorCodes.PremiumFeatureRequired))).toBe(
      "premiumRequired",
    );
  });

  it("classifyError_FreeLimitCodes_ReturnLimit", () => {
    expect(classifyError(apiError(ErrorCodes.MemberLimitReached))).toBe(
      "limit",
    );
    expect(classifyError(apiError(ErrorCodes.OpenEventLimitReached))).toBe(
      "limit",
    );
    expect(classifyError(apiError(ErrorCodes.MonthlyExpenseLimitReached))).toBe(
      "limit",
    );
  });

  it("classifyError_ValidationFailed1001_ReturnsValidation", () => {
    expect(classifyError(apiError(ErrorCodes.ValidationFailed))).toBe(
      "validation",
    );
  });

  it("classifyError_Unauthorized1002_ReturnsUnauthorized", () => {
    expect(classifyError(apiError(ErrorCodes.Unauthorized))).toBe(
      "unauthorized",
    );
  });

  it("classifyError_NetworkError_ReturnsNetwork", () => {
    expect(classifyError(ApiError.network("offline"))).toBe("network");
  });

  it("classifyError_NonApiErrorOrUnknownCode_ReturnsUnexpected", () => {
    expect(classifyError(new Error("boom"))).toBe("unexpected");
    expect(classifyError(apiError(ErrorCodes.InternalError))).toBe(
      "unexpected",
    );
    // 13003 (Premium) is deliberately distinct from generic forbidden 1004.
    expect(classifyError(apiError(ErrorCodes.Forbidden))).toBe("unexpected");
  });
});

describe("resolveErrorMessage", () => {
  it("resolveErrorMessage_ApiError_RendersLocalizedServerMessageVerbatim", () => {
    expect(
      resolveErrorMessage(apiError(2001, "Sai thông tin đăng nhập."), t),
    ).toBe("Sai thông tin đăng nhập.");
  });

  it("resolveErrorMessage_NetworkError_FallsBackToClientCopy", () => {
    expect(resolveErrorMessage(ApiError.network("x"), t)).toBe(
      "errors:network",
    );
  });

  it("resolveErrorMessage_NonApiError_FallsBackToUnexpectedCopy", () => {
    expect(resolveErrorMessage(new Error("x"), t)).toBe("errors:unexpected");
  });
});

describe("applyFieldErrors", () => {
  it("applyFieldErrors_KnownFields_SetsRhfFieldErrorsCamelCased", () => {
    const setError = vi.fn();
    const formLevel = applyFieldErrors(
      apiError(ErrorCodes.ValidationFailed, "invalid", {
        Username: ["Tên đăng nhập đã tồn tại."],
      }),
      ["username", "password"],
      setError,
    );
    expect(setError).toHaveBeenCalledWith(
      "username",
      "Tên đăng nhập đã tồn tại.",
    );
    expect(formLevel).toEqual([]);
  });

  it("applyFieldErrors_UnknownField_ReturnedAsFormLevel", () => {
    const setError = vi.fn();
    const formLevel = applyFieldErrors(
      apiError(ErrorCodes.ValidationFailed, "invalid", {
        somethingElse: ["Lỗi tổng quát."],
      }),
      ["username", "password"],
      setError,
    );
    expect(setError).not.toHaveBeenCalled();
    expect(formLevel).toEqual(["Lỗi tổng quát."]);
  });

  it("applyFieldErrors_ErrorWithoutFields_ReturnsEmpty", () => {
    const setError = vi.fn();
    expect(applyFieldErrors(apiError(2001), ["username"], setError)).toEqual(
      [],
    );
    expect(setError).not.toHaveBeenCalled();
  });
});
