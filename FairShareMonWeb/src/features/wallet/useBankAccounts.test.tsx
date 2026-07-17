import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { act, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import {
  bankAccountsKeys,
  useBankAccountsQuery,
  useCreateBankAccount,
  useDeleteBankAccount,
  useSetDefaultBankAccount,
  useUpdateBankAccount,
} from "./hooks/useBankAccounts";
import type { BankAccountResponse } from "./api/types";

/**
 * Bank-account hooks over MSW at the network boundary — exercises the REAL
 * centralized client + TanStack Query (never mocks the hook). Mutations invalidate
 * via the singleton `queryClient` (the established convention), so these specs
 * render against that same client and assert a second list GET fires. The
 * mutation handlers here are overridden to succeed regardless of tier so the test
 * targets invalidation, not the Premium gate (that is covered by walletPage +
 * qrDialog specs).
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}
function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}

const ACCOUNT: BankAccountResponse = {
  uuid: "ba-1",
  bankBin: "970436",
  bankName: "Vietcombank",
  accountNumber: "0071001234567",
  accountHolderName: "NGUYEN VAN MINH",
  isDefault: true,
  createdAt: "2026-01-01T00:00:00+00:00",
};

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-bahook-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-bahook-t",
    refreshTokenExpiresAt: future,
    user: { username: "bahook", tier: "PREMIUM", role: "USER" },
    profileStatus: "resolved",
  });
}

beforeEach(() => {
  window.localStorage.clear();
  queryClient.clear();
  seedSession();
});

afterEach(() => {
  sessionStore.getState().clearSession();
});

describe("bankAccountsKeys", () => {
  it("BankAccountsKeys_ListKey_IsScopedUnderTheInvalidationRoot", () => {
    // The mutation invalidation root ["bank-accounts"] must prefix the list key so
    // one invalidate re-reflects the backend's isDefault after any mutation.
    expect(bankAccountsKeys.all).toEqual(["bank-accounts"]);
    expect(bankAccountsKeys.list()).toEqual(["bank-accounts", "list"]);
    expect(bankAccountsKeys.list()[0]).toBe(bankAccountsKeys.all[0]);
  });
});

describe("useBankAccountsQuery", () => {
  it("UseBankAccountsQuery_Enabled_ResolvesTheList", async () => {
    server.use(http.get("*/api/v1/bank-accounts", () => ok([ACCOUNT])));
    const captured: { q?: ReturnType<typeof useBankAccountsQuery> } = {};
    function Probe() {
      captured.q = useBankAccountsQuery();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(captured.q?.isSuccess).toBe(true));
    expect(captured.q?.data).toEqual([ACCOUNT]);
  });

  it("UseBankAccountsQuery_DisabledFlag_DefersTheRead", async () => {
    let gets = 0;
    server.use(
      http.get("*/api/v1/bank-accounts", () => {
        gets += 1;
        return ok([ACCOUNT]);
      }),
    );
    function Probe() {
      useBankAccountsQuery(false);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    // Give any (unwanted) request a chance to fire, then assert none did.
    await new Promise((r) => setTimeout(r, 30));
    expect(gets).toBe(0);
  });
});

describe("bank-account mutation invalidation", () => {
  function countingList(counter: { n: number }) {
    return http.get("*/api/v1/bank-accounts", () => {
      counter.n += 1;
      return ok([ACCOUNT]);
    });
  }

  it("UseCreateBankAccount_OnSuccess_InvalidatesRootAndRefetchesList", async () => {
    const gets = { n: 0 };
    const captured: { create?: ReturnType<typeof useCreateBankAccount> } = {};
    server.use(
      countingList(gets),
      http.post("*/api/v1/bank-accounts", () => ok(ACCOUNT)),
    );
    function Probe() {
      useBankAccountsQuery();
      captured.create = useCreateBankAccount();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(gets.n).toBe(1));
    await act(async () => {
      await captured.create!.mutateAsync({
        bankBin: "970407",
        bankName: "Techcombank",
        accountNumber: "19024681012345",
        accountHolderName: "NGUYEN VAN MINH",
      });
    });
    await waitFor(() => expect(gets.n).toBe(2));
  });

  it("UseUpdateBankAccount_OnSuccess_InvalidatesRootAndRefetchesList", async () => {
    const gets = { n: 0 };
    const captured: { update?: ReturnType<typeof useUpdateBankAccount> } = {};
    server.use(
      countingList(gets),
      http.put("*/api/v1/bank-accounts/:uuid", () => ok(ACCOUNT)),
    );
    function Probe() {
      useBankAccountsQuery();
      captured.update = useUpdateBankAccount();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(gets.n).toBe(1));
    await act(async () => {
      await captured.update!.mutateAsync({
        uuid: "ba-1",
        body: {
          bankBin: "970436",
          bankName: "Vietcombank",
          accountNumber: "0071001234567",
          accountHolderName: "NGUYEN VAN A",
        },
      });
    });
    await waitFor(() => expect(gets.n).toBe(2));
  });

  it("UseSetDefaultBankAccount_OnSuccess_InvalidatesRootAndRefetchesList", async () => {
    const gets = { n: 0 };
    const captured: { setDefault?: ReturnType<typeof useSetDefaultBankAccount> } =
      {};
    server.use(
      countingList(gets),
      http.put("*/api/v1/bank-accounts/:uuid/default", () =>
        ok({ message: "ok" }),
      ),
    );
    function Probe() {
      useBankAccountsQuery();
      captured.setDefault = useSetDefaultBankAccount();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(gets.n).toBe(1));
    await act(async () => {
      await captured.setDefault!.mutateAsync("ba-2");
    });
    await waitFor(() => expect(gets.n).toBe(2));
  });

  it("UseDeleteBankAccount_OnSuccess_InvalidatesRootAndRefetchesList", async () => {
    const gets = { n: 0 };
    const captured: { remove?: ReturnType<typeof useDeleteBankAccount> } = {};
    server.use(
      countingList(gets),
      http.delete("*/api/v1/bank-accounts/:uuid", () => ok({ message: "ok" })),
    );
    function Probe() {
      useBankAccountsQuery();
      captured.remove = useDeleteBankAccount();
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(gets.n).toBe(1));
    await act(async () => {
      await captured.remove!.mutateAsync("ba-1");
    });
    await waitFor(() => expect(gets.n).toBe(2));
  });
});
