import { useMutation, useQuery } from "@tanstack/react-query";
import { queryClient } from "@/lib/query/queryClient";
import { tagsApi } from "../api/tagsApi";
import type { UpdateTagRequest } from "../api/types";

/**
 * Query-key factory for tags. `all` is the invalidation root — every mutation
 * invalidates it so BOTH toggle states (active-only and include-deleted) refetch
 * (including a reactivated row on name reuse).
 */
export const tagsKeys = {
  all: ["tags"] as const,
  list: (includeDeleted: boolean) => ["tags", "list", includeDeleted] as const,
};

/** The caller's tags (name A→Z — backend order, rendered verbatim). */
export function useTagsQuery(includeDeleted: boolean) {
  return useQuery({
    queryKey: tagsKeys.list(includeDeleted),
    queryFn: () => tagsApi.list(includeDeleted),
  });
}

function invalidateTags() {
  return queryClient.invalidateQueries({ queryKey: tagsKeys.all });
}

/**
 * Create/rename/delete mutations. Each `onSuccess` invalidates `["tags"]` so the
 * list reflects the change. Toast/close side-effects stay in the calling
 * component. No optimistic updates in M3.
 */
export function useCreateTag() {
  return useMutation({
    mutationFn: tagsApi.create,
    onSuccess: invalidateTags,
  });
}

export function useRenameTag() {
  return useMutation({
    mutationFn: ({ uuid, body }: { uuid: string; body: UpdateTagRequest }) =>
      tagsApi.rename(uuid, body),
    onSuccess: invalidateTags,
  });
}

export function useDeleteTag() {
  return useMutation({
    mutationFn: (uuid: string) => tagsApi.remove(uuid),
    onSuccess: invalidateTags,
  });
}
