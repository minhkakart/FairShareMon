import { useQuery } from "@tanstack/react-query";
import { vietqrDirectoryApi } from "../api/vietqrDirectoryApi";
import type { VietqrBank } from "../api/vietqrDirectoryApi";
import { VIETQR_BANKS_SNAPSHOT } from "../data/vietqrBanks";

/**
 * The VietQR bank directory (external, unversioned). Seeded instantly by the
 * committed snapshot so the picker is populated on first paint, then refreshed
 * once in the background from the live endpoint. The `queryFn` CATCHES fetch
 * failures (CORS/offline — likely in the static-SPA prod) and returns the
 * snapshot so `data` is never empty. A stale `initialDataUpdatedAt: 0` forces
 * that one background refresh; `staleTime` then holds it for a day.
 */
export function useVietqrBanks() {
  return useQuery({
    queryKey: ["vietqr-banks"],
    queryFn: async ({ signal }): Promise<VietqrBank[]> => {
      try {
        const banks = await vietqrDirectoryApi.list(signal);
        return banks.length > 0 ? banks : VIETQR_BANKS_SNAPSHOT;
      } catch (error) {
        // A cancellation (refetch-cancel / unmount) must propagate so Query
        // treats it as aborted — never resolve a cancelled refetch as a
        // snapshot "success" that could overwrite fresher live data.
        if (
          signal?.aborted ||
          (error instanceof DOMException && error.name === "AbortError")
        ) {
          throw error;
        }
        // A real fetch failure (CORS/offline) → never empty the picker.
        return VIETQR_BANKS_SNAPSHOT;
      }
    },
    initialData: VIETQR_BANKS_SNAPSHOT,
    initialDataUpdatedAt: 0,
    staleTime: 24 * 60 * 60 * 1000,
    gcTime: 7 * 24 * 60 * 60 * 1000,
    retry: 1,
  });
}

/**
 * Selector: the directory bank for a stored BIN (or `undefined` if unknown /
 * legacy). Used by the accounts table to re-derive the logo + short name without
 * a second fetch — reads the same cached `["vietqr-banks"]` query.
 */
export function useBankByBin(bin: string | undefined): VietqrBank | undefined {
  const { data } = useVietqrBanks();
  if (!bin) return undefined;
  return data?.find((b) => b.bin === bin);
}
