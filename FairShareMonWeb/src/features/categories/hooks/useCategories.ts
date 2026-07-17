import { useMutation, useQuery } from "@tanstack/react-query";
import { queryClient } from "@/lib/query/queryClient";
import { categoriesApi } from "../api/categoriesApi";
import type { UpdateCategoryRequest } from "../api/types";

/**
 * Query-key factory for categories. `all` is the invalidation root — every
 * mutation invalidates it so BOTH toggle states (active-only and include-deleted)
 * and the default swap refetch.
 */
export const categoriesKeys = {
  all: ["categories"] as const,
  list: (includeDeleted: boolean) =>
    ["categories", "list", includeDeleted] as const,
};

/** The caller's categories (default-first, then A→Z — backend order, verbatim). */
export function useCategoriesQuery(includeDeleted: boolean) {
  return useQuery({
    queryKey: categoriesKeys.list(includeDeleted),
    queryFn: () => categoriesApi.list(includeDeleted),
  });
}

function invalidateCategories() {
  return queryClient.invalidateQueries({ queryKey: categoriesKeys.all });
}

/**
 * Create/update/set-default/delete mutations. Each `onSuccess` invalidates
 * `["categories"]` so the list reflects the change (including a reactivated row
 * on name reuse, and the moved default marker). Toast/close side-effects stay in
 * the calling component. No optimistic updates in M3.
 */
export function useCreateCategory() {
  return useMutation({
    mutationFn: categoriesApi.create,
    onSuccess: invalidateCategories,
  });
}

export function useUpdateCategory() {
  return useMutation({
    mutationFn: ({ uuid, body }: { uuid: string; body: UpdateCategoryRequest }) =>
      categoriesApi.update(uuid, body),
    onSuccess: invalidateCategories,
  });
}

export function useSetDefaultCategory() {
  return useMutation({
    mutationFn: (uuid: string) => categoriesApi.setDefault(uuid),
    onSuccess: invalidateCategories,
  });
}

export function useDeleteCategory() {
  return useMutation({
    mutationFn: (uuid: string) => categoriesApi.remove(uuid),
    onSuccess: invalidateCategories,
  });
}
