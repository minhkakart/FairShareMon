/**
 * The backend's uniform response envelope (`Models/ApiResult.cs`):
 * `{ data, isSuccess, error: { code, message, fields? } }`.
 * HTTP status is derived from the error; the centralized client is the ONLY
 * place that reads this shape — feature code receives unwrapped `data`.
 */
export interface ApiErrorPayload {
  code: number;
  message: string;
  /** Per-field validation errors (camelCase field -> messages); only on 1001. */
  fields?: Record<string, string[]>;
}

export interface ApiEnvelope<T> {
  data: T | null;
  isSuccess: boolean;
  error: ApiErrorPayload | null;
}

/** Success-message endpoints (logout, change-password) return `{ message }`. */
export interface MessageResponse {
  message: string;
}
