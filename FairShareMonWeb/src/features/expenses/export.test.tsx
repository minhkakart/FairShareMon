import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { ExpenseDetailPage } from "./pages/ExpenseDetailPage";
import type { ExpenseResponse } from "./api/types";

/**
 * CSV export (B6) — the detail-header "Xuất CSV" button drives useExportExpense →
 * api.blob (the binary path, not the JSON envelope) → the shared downloadBlob
 * helper. downloadBlob is spied so we assert the browser download is triggered
 * with the server-provided filename; the export request itself is counted to prove
 * the blob path is used.
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
function fail(code: number, message: string, status: number) {
  return HttpResponse.json<Envelope>(
    { data: null, isSuccess: false, error: { code, message } },
    { status },
  );
}

const UUID = "e-export";

function makeExpense(): ExpenseResponse {
  return {
    uuid: UUID,
    name: "Thuê xe",
    description: null,
    expenseTime: "2026-07-16T03:00:00+00:00",
    total: 0,
    category: {
      uuid: "c-1",
      name: "Đi lại",
      color: "#3B82F6",
      icon: "🚗",
      isDefault: false,
      isDeleted: false,
      createdAt: "2026-01-01T00:00:00+00:00",
    },
    payer: {
      uuid: "m-1",
      name: "An Nguyễn",
      isOwnerRepresentative: false,
      isDeleted: false,
      createdAt: "2026-01-01T00:00:00+00:00",
    },
    isSettled: false,
    settledAt: null,
    shares: [],
    tags: [],
    eventUuid: null,
    eventName: null,
    eventIsClosed: null,
    createdAt: "2026-07-16T03:00:00+00:00",
  };
}

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-exp-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-exp-t",
    refreshTokenExpiresAt: future,
    user: { username: "exp", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

function renderDetail() {
  return renderWithProviders(
    <Routes>
      <Route path="/expenses/:uuid" element={<ExpenseDetailPage />} />
    </Routes>,
    { initialPath: `/expenses/${UUID}`, queryClient },
  );
}

beforeEach(async () => {
  window.localStorage.clear();
  queryClient.clear();
  downloadSpy.mockClear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
  seedSession();
  server.use(http.get(`*/api/v1/expenses/${UUID}`, () => ok(makeExpense())));
});

afterEach(async () => {
  sessionStore.getState().clearSession();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("Expense CSV export", () => {
  it("ExpenseDetail_ExportButton_CallsBlobEndpointAndDownloadsWithServerFilename", async () => {
    let exportRequests = 0;
    let exportUrl = "";
    server.use(
      http.get(`*/api/v1/expenses/${UUID}/export`, ({ request }) => {
        exportRequests += 1;
        exportUrl = request.url;
        return new HttpResponse("﻿Thuê xe\r\ncol\r\n", {
          headers: {
            "Content-Type": "text/csv; charset=utf-8",
            "Content-Disposition": 'attachment; filename="expense-e-export.csv"',
          },
        });
      }),
    );
    const user = userEvent.setup();
    renderDetail();
    await screen.findByRole("heading", { level: 1, name: "Thuê xe" });

    await user.click(screen.getByRole("button", { name: "Xuất CSV" }));

    // The blob endpoint was hit (binary path)…
    await vi.waitFor(() => expect(exportRequests).toBe(1));
    expect(new URL(exportUrl).searchParams.get("format")).toBe("csv");
    // …and the download helper was invoked with the server-provided filename.
    await vi.waitFor(() => expect(downloadSpy).toHaveBeenCalledTimes(1));
    const [result] = downloadSpy.mock.calls[0];
    expect(result).toHaveProperty("blob");
    expect((result as { filename?: string }).filename).toBe(
      "expense-e-export.csv",
    );
  });

  it("ExpenseDetail_ExportError_ToastsAndDoesNotDownload", async () => {
    server.use(
      http.get(`*/api/v1/expenses/${UUID}/export`, () =>
        fail(1000, "Đã xảy ra lỗi máy chủ.", 500),
      ),
    );
    const user = userEvent.setup();
    renderDetail();
    await screen.findByRole("heading", { level: 1, name: "Thuê xe" });

    await user.click(screen.getByRole("button", { name: "Xuất CSV" }));

    expect(
      await screen.findByText("Đã xảy ra lỗi máy chủ."),
    ).toBeInTheDocument();
    expect(downloadSpy).not.toHaveBeenCalled();
  });
});
