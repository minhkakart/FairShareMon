import { useMutation, useQuery } from "@tanstack/react-query";
import { queryClient } from "@/lib/query/queryClient";
import { membersApi } from "../api/membersApi";
import type { UpdateMemberRequest } from "../api/types";

/**
 * Query-key factory for members. `all` is the invalidation root — every mutation
 * invalidates it so BOTH toggle states (active-only and include-deleted) refetch.
 */
export const membersKeys = {
  all: ["members"] as const,
  list: (includeDeleted: boolean) =>
    ["members", "list", includeDeleted] as const,
};

/** The caller's members (owner-rep first, then A→Z — backend order, rendered verbatim). */
export function useMembersQuery(includeDeleted: boolean) {
  return useQuery({
    queryKey: membersKeys.list(includeDeleted),
    queryFn: () => membersApi.list(includeDeleted),
  });
}

function invalidateMembers() {
  return queryClient.invalidateQueries({ queryKey: membersKeys.all });
}

/**
 * Create/rename/delete mutations. Each `onSuccess` invalidates `["members"]` so
 * the list reflects the change. Toast/close side-effects stay in the calling
 * component (the `useAuth.ts` convention). No optimistic updates in M2.
 */
export function useCreateMember() {
  return useMutation({
    mutationFn: membersApi.create,
    onSuccess: invalidateMembers,
  });
}

export function useRenameMember() {
  return useMutation({
    mutationFn: ({ uuid, body }: { uuid: string; body: UpdateMemberRequest }) =>
      membersApi.rename(uuid, body),
    onSuccess: invalidateMembers,
  });
}

export function useDeleteMember() {
  return useMutation({
    mutationFn: (uuid: string) => membersApi.remove(uuid),
    onSuccess: invalidateMembers,
  });
}
