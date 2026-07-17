import { useMutation, useQuery } from "@tanstack/react-query";
import { queryClient } from "@/lib/query/queryClient";
import { bankAccountsApi } from "../api/bankAccountsApi";
import type { UpdateBankAccountRequest } from "../api/types";

/**
 * Query-key factory for bank accounts. `all` is the invalidation root — every
 * mutation invalidates it so the list re-reflects the backend's `isDefault`
 * (the client never computes the default; set-default + delete-promotion are
 * entirely server-side).
 */
export const bankAccountsKeys = {
  all: ["bank-accounts"] as const,
  list: () => ["bank-accounts", "list"] as const,
};

/** The caller's bank accounts (default first — backend order, rendered verbatim).
 *  A Free-safe read: enabled regardless of tier. `enabled` lets the QR dialog
 *  defer the read until it opens. */
export function useBankAccountsQuery(enabled = true) {
  return useQuery({
    queryKey: bankAccountsKeys.list(),
    queryFn: () => bankAccountsApi.list(),
    enabled,
  });
}

function invalidateBankAccounts() {
  return queryClient.invalidateQueries({ queryKey: bankAccountsKeys.all });
}

/**
 * Create / update / set-default / delete mutations. Each `onSuccess` invalidates
 * `["bank-accounts"]` so the list refetches. Toast/close side-effects stay in
 * the calling component (the established convention). No optimistic updates.
 */
export function useCreateBankAccount() {
  return useMutation({
    mutationFn: bankAccountsApi.create,
    onSuccess: invalidateBankAccounts,
  });
}

export function useUpdateBankAccount() {
  return useMutation({
    mutationFn: ({
      uuid,
      body,
    }: {
      uuid: string;
      body: UpdateBankAccountRequest;
    }) => bankAccountsApi.update(uuid, body),
    onSuccess: invalidateBankAccounts,
  });
}

export function useSetDefaultBankAccount() {
  return useMutation({
    mutationFn: (uuid: string) => bankAccountsApi.setDefault(uuid),
    onSuccess: invalidateBankAccounts,
  });
}

export function useDeleteBankAccount() {
  return useMutation({
    mutationFn: (uuid: string) => bankAccountsApi.remove(uuid),
    onSuccess: invalidateBankAccounts,
  });
}
