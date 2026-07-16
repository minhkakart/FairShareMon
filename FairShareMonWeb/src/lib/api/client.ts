import { env } from "@/config/env";
import { getSession } from "@/lib/auth/session";
import { ApiError, ErrorCodes } from "./errors";
import type { ApiEnvelope } from "./types/envelope";
import { getActiveLocale, getTimeZone, notifySessionExpired } from "./runtime";
import { refreshOnce } from "./refresh";

export type HttpMethod = "GET" | "POST" | "PUT" | "PATCH" | "DELETE";

export type QueryValue = string | number | boolean | null | undefined;

export interface RequestOptions {
  body?: unknown;
  query?: Record<string, QueryValue>;
  headers?: Record<string, string>;
  signal?: AbortSignal;
  /** Skip the Authorization header (anon endpoints: login / register / refresh). */
  anonymous?: boolean;
  /** Skip the 401 → refresh → retry loop (used by the refresh call itself, and retries). */
  skipAuthRefresh?: boolean;
}

/** Binary response (CSV export / QR PNG). */
export interface BlobResult {
  blob: Blob;
  filename: string | null;
  contentType: string | null;
}

function buildUrl(path: string, query?: Record<string, QueryValue>): string {
  let url = `${env.apiBaseUrl}${path}`;
  if (query) {
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null) {
        params.append(key, String(value));
      }
    }
    const qs = params.toString();
    if (qs) url += `?${qs}`;
  }
  return url;
}

/**
 * Injects `Authorization` (unless anonymous), `X-Time-Zone`, `Accept-Language`,
 * and `Content-Type` for JSON bodies. Wraps network failures as an
 * ApiError(Network) so callers get one error shape.
 */
async function rawFetch(
  method: HttpMethod,
  path: string,
  options: RequestOptions,
): Promise<Response> {
  const headers: Record<string, string> = {
    Accept: "application/json",
    "X-Time-Zone": getTimeZone(),
    "Accept-Language": getActiveLocale(),
    ...options.headers,
  };

  let body: BodyInit | undefined;
  if (options.body !== undefined) {
    headers["Content-Type"] = "application/json";
    body = JSON.stringify(options.body);
  }

  if (!options.anonymous) {
    const { accessToken } = getSession();
    if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
  }

  try {
    return await fetch(buildUrl(path, options.query), {
      method,
      headers,
      body,
      signal: options.signal,
    });
  } catch {
    throw ApiError.network("Network request failed");
  }
}

/**
 * A 401 on an authenticated request means the access token is invalid/expired
 * (anon endpoints are excluded, so this is never invalid-credentials). Refresh
 * once (shared/de-duped) then retry the original request exactly once. A missing
 * or failed refresh clears the session and signals the login redirect.
 */
async function requestWithRefresh(
  method: HttpMethod,
  path: string,
  options: RequestOptions,
): Promise<Response> {
  const response = await rawFetch(method, path, options);

  if (response.status !== 401 || options.anonymous || options.skipAuthRefresh) {
    return response;
  }

  const { refreshToken } = getSession();
  if (!refreshToken) {
    getSession().clearSession();
    notifySessionExpired();
    return response;
  }

  try {
    await refreshOnce();
  } catch {
    // refreshOnce already cleared the session + signaled the redirect.
    return response;
  }

  return rawFetch(method, path, { ...options, skipAuthRefresh: true });
}

async function parseEnvelope<T>(response: Response): Promise<ApiEnvelope<T>> {
  let json: unknown;
  try {
    json = await response.json();
  } catch {
    throw new ApiError(
      response.status === 401
        ? ErrorCodes.Unauthorized
        : ErrorCodes.InternalError,
      `HTTP ${response.status}`,
      response.status,
    );
  }

  if (
    typeof json !== "object" ||
    json === null ||
    typeof (json as ApiEnvelope<T>).isSuccess !== "boolean"
  ) {
    throw new ApiError(
      ErrorCodes.InternalError,
      `Unexpected response shape (HTTP ${response.status})`,
      response.status,
    );
  }

  return json as ApiEnvelope<T>;
}

/**
 * The centralized JSON request. Returns unwrapped `data` on success; throws a
 * typed ApiError (carrying the numeric code + optional field errors) otherwise.
 * The ONLY place `fetch` and the envelope are touched.
 */
export async function request<T>(
  method: HttpMethod,
  path: string,
  options: RequestOptions = {},
): Promise<T> {
  const response = await requestWithRefresh(method, path, options);
  const envelope = await parseEnvelope<T>(response);

  if (envelope.isSuccess) {
    return envelope.data as T;
  }

  const error = envelope.error ?? {
    code: ErrorCodes.InternalError,
    message: `HTTP ${response.status}`,
  };
  throw ApiError.fromPayload(error, response.status);
}

function parseFilename(contentDisposition: string | null): string | null {
  if (!contentDisposition) return null;
  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(contentDisposition);
  if (utf8?.[1]) return decodeURIComponent(utf8[1]);
  const ascii = /filename="?([^";]+)"?/i.exec(contentDisposition);
  return ascii?.[1] ?? null;
}

/**
 * Binary request path for CSV export + QR PNG (built now, consumed by later
 * feature cycles). Error responses still arrive as the JSON envelope.
 */
export async function requestBlob(
  method: HttpMethod,
  path: string,
  options: RequestOptions = {},
): Promise<BlobResult> {
  const response = await requestWithRefresh(method, path, options);
  if (!response.ok) {
    const envelope = await parseEnvelope<never>(response);
    const error = envelope.error ?? {
      code: ErrorCodes.InternalError,
      message: `HTTP ${response.status}`,
    };
    throw ApiError.fromPayload(error, response.status);
  }
  return {
    blob: await response.blob(),
    filename: parseFilename(response.headers.get("Content-Disposition")),
    contentType: response.headers.get("Content-Type"),
  };
}

/** Ergonomic verb helpers over `request`. */
export const api = {
  get: <T>(path: string, options?: RequestOptions) =>
    request<T>("GET", path, options),
  post: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    request<T>("POST", path, { ...options, body }),
  put: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    request<T>("PUT", path, { ...options, body }),
  patch: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    request<T>("PATCH", path, { ...options, body }),
  delete: <T>(path: string, options?: RequestOptions) =>
    request<T>("DELETE", path, options),
  blob: requestBlob,
};
