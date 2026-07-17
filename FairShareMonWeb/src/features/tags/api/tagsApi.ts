import { api } from "@/lib/api/client";
import type { MessageResponse } from "@/lib/api/types/envelope";
import type {
  CreateTagRequest,
  TagResponse,
  UpdateTagRequest,
} from "./types";

/**
 * Tag endpoints (`api/v1/tags`). All authenticated + resource-owned: a tag that
 * isn't the caller's yields 404 (code 5000). Envelope unwrap, auth, refresh, and
 * error typing all happen in the centralized client. The backend returns name
 * A→Z — rendered verbatim (no client re-sort).
 */
export const tagsApi = {
  list: (includeDeleted: boolean) =>
    api.get<TagResponse[]>("/v1/tags", { query: { includeDeleted } }),

  /** Reserved (OQ5a) — not consumed in M3; kept for a future detail route. */
  get: (uuid: string) => api.get<TagResponse>(`/v1/tags/${uuid}`),

  create: (body: CreateTagRequest) => api.post<TagResponse>("/v1/tags", body),

  rename: (uuid: string, body: UpdateTagRequest) =>
    api.put<TagResponse>(`/v1/tags/${uuid}`, body),

  remove: (uuid: string) => api.delete<MessageResponse>(`/v1/tags/${uuid}`),
};
