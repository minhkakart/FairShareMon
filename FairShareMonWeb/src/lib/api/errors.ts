import type { ApiErrorPayload } from "./types/envelope";

/**
 * TS mirror of `FairShareMonApi/Constants/ErrorCodes.cs`. This is the SINGLE
 * source for code-based branching in the frontend — never branch on message
 * text (messages are localized by the backend and may change). Values are part
 * of the public API contract; never renumber, only append.
 */
export const ErrorCodes = {
  // Client-synthetic (no HTTP round-trip completed). Not a backend code.
  Network: -1,

  // 1xxx — infrastructure
  InternalError: 1000,
  ValidationFailed: 1001,
  Unauthorized: 1002,
  NotFound: 1003,
  Forbidden: 1004,

  // 2xxx — auth
  UsernameTaken: 2000,
  InvalidCredentials: 2001,
  InvalidRefreshToken: 2002,
  CurrentPasswordIncorrect: 2003,

  // 3xxx members / 4xxx categories / 5xxx tags / 6xxx expenses / 7xxx shares
  MemberNotFound: 3000,
  OwnerRepresentativeNotDeletable: 3001,
  CategoryNotFound: 4000,
  CategoryNameDuplicate: 4001,
  DefaultCategoryNotDeletable: 4002,
  TagNotFound: 5000,
  TagNameDuplicate: 5001,
  ExpenseNotFound: 6000,
  ShareNotFound: 7000,

  // 9xxx — events
  EventNotFound: 9000,
  EventClosed: 9001,
  ExpenseTimeOutOfEventRange: 9002,
  EventRangeExcludesAssignedExpenses: 9003,

  // 12xxx — wallet / QR
  BankAccountNotFound: 12000,
  NoBankAccountForQr: 12001,
  EventNotClosedForQr: 12002,
  NoOutstandingDebtForQr: 12003,

  // 13xxx — tiers (Free create-limit 400s + Premium-gate 403)
  MemberLimitReached: 13000,
  OpenEventLimitReached: 13001,
  MonthlyExpenseLimitReached: 13002,
  PremiumFeatureRequired: 13003,

  // 14xxx — admin
  AdminUserNotFound: 14000,
  AdminCannotTargetSelf: 14001,
  AdminCannotTargetAdmin: 14002,
  AccountDisabled: 14003,
} as const;

export type ErrorCode = (typeof ErrorCodes)[keyof typeof ErrorCodes];

/** The Free-tier create-limit codes that map to the friendly LimitNotice UI. */
export const FREE_LIMIT_CODES: readonly number[] = [
  ErrorCodes.MemberLimitReached,
  ErrorCodes.OpenEventLimitReached,
  ErrorCodes.MonthlyExpenseLimitReached,
];

/**
 * The typed error every failed request throws. Carries the numeric `code`
 * (branch on this), the already-localized `message` (render this), optional
 * per-field `fields` (map onto form fields), and the raw `httpStatus`.
 */
export class ApiError extends Error {
  readonly code: number;
  readonly fields?: Record<string, string[]>;
  readonly httpStatus: number;

  constructor(
    code: number,
    message: string,
    httpStatus: number,
    fields?: Record<string, string[]>,
  ) {
    super(message);
    this.name = "ApiError";
    this.code = code;
    this.httpStatus = httpStatus;
    this.fields = fields;
  }

  static fromPayload(payload: ApiErrorPayload, httpStatus: number): ApiError {
    return new ApiError(
      payload.code,
      payload.message,
      httpStatus,
      payload.fields,
    );
  }

  /** No response arrived (offline, DNS, CORS, aborted). */
  static network(message: string): ApiError {
    return new ApiError(ErrorCodes.Network, message, 0);
  }

  get isNetwork(): boolean {
    return this.code === ErrorCodes.Network;
  }

  get isValidation(): boolean {
    return this.code === ErrorCodes.ValidationFailed;
  }
}

export function isApiError(value: unknown): value is ApiError {
  return value instanceof ApiError;
}
