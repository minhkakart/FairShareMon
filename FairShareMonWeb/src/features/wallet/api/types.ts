/**
 * Bank-account (ví) DTOs — mirror `FairShareMonApi/Models/Wallet/**`
 * (`BankAccountResponse`, `Create/UpdateBankAccountRequest`). Feature-local per
 * the feature-first convention. All resource-owned: a miss yields 404 (code
 * 12000), never 403. Wallet reads are Free; every mutation is Premium (403
 * 13003). Datetimes are offset-aware ISO-8601 strings.
 */
export interface BankAccountResponse {
  uuid: string;
  /** NAPAS BIN — exactly 6 digits. */
  bankBin: string;
  bankName: string;
  accountNumber: string;
  accountHolderName: string;
  /** True for the default receiving account (the implicit QR destination). */
  isDefault: boolean;
  createdAt: string;
}

/** `CreateBankAccountRequest` — all four fields required. */
export interface CreateBankAccountRequest {
  bankBin: string;
  bankName: string;
  accountNumber: string;
  accountHolderName: string;
}

/** `UpdateBankAccountRequest` — same four fields; never touches the default flag. */
export interface UpdateBankAccountRequest {
  bankBin: string;
  bankName: string;
  accountNumber: string;
  accountHolderName: string;
}
