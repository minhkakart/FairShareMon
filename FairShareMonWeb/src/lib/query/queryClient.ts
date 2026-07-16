import { QueryClient } from "@tanstack/react-query";
import { isApiError } from "@/lib/api/errors";

/**
 * Never retry a resolved API failure (4xx/business codes) — the client already
 * owns the single 401 → refresh → retry. Only genuinely transient network
 * errors get one retry. Feature hooks opt out of retry as needed.
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: (failureCount, error) =>
        isApiError(error) && error.isNetwork && failureCount < 1,
      staleTime: 30_000,
      refetchOnWindowFocus: false,
    },
    mutations: {
      retry: false,
    },
  },
});
