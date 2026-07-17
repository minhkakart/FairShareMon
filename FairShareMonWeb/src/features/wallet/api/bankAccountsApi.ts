import { api } from "@/lib/api/client";
import type { MessageResponse } from "@/lib/api/types/envelope";
import type {
  BankAccountResponse,
  CreateBankAccountRequest,
  UpdateBankAccountRequest,
} from "./types";

/**
 * Bank-account endpoints (`api/v1/bank-accounts`). All authenticated +
 * resource-owned: an account that isn't the caller's yields 404 (code 12000).
 * Reads (list/get) are Free; every mutation is Premium (403 13003). Envelope
 * unwrap, auth, refresh, and error typing all happen in the centralized client.
 */
export const bankAccountsApi = {
  /** Default account first, then most-recently-added (backend order). */
  list: () => api.get<BankAccountResponse[]>("/v1/bank-accounts"),

  /** Reserved — not consumed in M7; kept for a future detail route. */
  get: (uuid: string) =>
    api.get<BankAccountResponse>(`/v1/bank-accounts/${uuid}`),

  create: (body: CreateBankAccountRequest) =>
    api.post<BankAccountResponse>("/v1/bank-accounts", body),

  update: (uuid: string, body: UpdateBankAccountRequest) =>
    api.put<BankAccountResponse>(`/v1/bank-accounts/${uuid}`, body),

  /** Atomic default swap — server clears the old default and sets this one. */
  setDefault: (uuid: string) =>
    api.put<MessageResponse>(`/v1/bank-accounts/${uuid}/default`),

  remove: (uuid: string) =>
    api.delete<MessageResponse>(`/v1/bank-accounts/${uuid}`),
};
