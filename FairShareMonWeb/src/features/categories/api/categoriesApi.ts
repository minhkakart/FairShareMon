import { api } from "@/lib/api/client";
import type { MessageResponse } from "@/lib/api/types/envelope";
import type {
  CategoryResponse,
  CreateCategoryRequest,
  UpdateCategoryRequest,
} from "./types";

/**
 * Category endpoints (`api/v1/categories`). All authenticated + resource-owned: a
 * category that isn't the caller's yields 404 (code 4000). Envelope unwrap, auth,
 * refresh, and error typing all happen in the centralized client. The backend
 * returns default-first then name A→Z — rendered verbatim (no client re-sort).
 */
export const categoriesApi = {
  list: (includeDeleted: boolean) =>
    api.get<CategoryResponse[]>("/v1/categories", { query: { includeDeleted } }),

  /** Reserved (OQ5a) — not consumed in M3; kept for a future detail route. */
  get: (uuid: string) => api.get<CategoryResponse>(`/v1/categories/${uuid}`),

  create: (body: CreateCategoryRequest) =>
    api.post<CategoryResponse>("/v1/categories", body),

  update: (uuid: string, body: UpdateCategoryRequest) =>
    api.put<CategoryResponse>(`/v1/categories/${uuid}`, body),

  /** Atomic default swap (no body) — clears the old default, sets this one. */
  setDefault: (uuid: string) =>
    api.put<MessageResponse>(`/v1/categories/${uuid}/default`),

  remove: (uuid: string) => api.delete<MessageResponse>(`/v1/categories/${uuid}`),
};
