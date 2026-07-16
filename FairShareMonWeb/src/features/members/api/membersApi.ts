import { api } from "@/lib/api/client";
import type { MessageResponse } from "@/lib/api/types/envelope";
import type {
  CreateMemberRequest,
  MemberResponse,
  UpdateMemberRequest,
} from "./types";

/**
 * Member endpoints (`api/v1/members`). All authenticated + resource-owned: a
 * member that isn't the caller's yields 404 (code 3000). Envelope unwrap, auth,
 * refresh, and error typing all happen in the centralized client.
 */
export const membersApi = {
  list: (includeDeleted: boolean) =>
    api.get<MemberResponse[]>("/v1/members", { query: { includeDeleted } }),

  /** Reserved (OQ2a) — not consumed in M2; kept for a future detail route. */
  get: (uuid: string) => api.get<MemberResponse>(`/v1/members/${uuid}`),

  create: (body: CreateMemberRequest) =>
    api.post<MemberResponse>("/v1/members", body),

  rename: (uuid: string, body: UpdateMemberRequest) =>
    api.put<MemberResponse>(`/v1/members/${uuid}`, body),

  remove: (uuid: string) => api.delete<MessageResponse>(`/v1/members/${uuid}`),
};
