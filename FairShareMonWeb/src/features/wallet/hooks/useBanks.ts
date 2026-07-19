import { useQuery } from "@tanstack/react-query";
import { banksApi } from "../api/banksApi";
import type { Bank } from "../api/banksApi";

/**
 * The bank directory from our own `GET /api/v1/banks` (via the centralized
 * client — auth, refresh, `X-Time-Zone`, `Accept-Language`, and envelope unwrap
 * handled there). A standard TanStack Query: the backend guarantees a non-empty
 * list, so there is no snapshot seed or offline fallback. Cached for a day.
 */
export function useBanks() {
  return useQuery({
    queryKey: ["banks"],
    queryFn: () => banksApi.list(),
    staleTime: 24 * 60 * 60 * 1000,
    gcTime: 7 * 24 * 60 * 60 * 1000,
  });
}

/**
 * Selector: the directory bank for a stored BIN (or `undefined` if unknown /
 * legacy). Used by the accounts table to re-derive the logo + short name without
 * a second fetch — reads the same cached `["banks"]` query.
 */
export function useBankByBin(bin: string | undefined): Bank | undefined {
  const { data } = useBanks();
  if (!bin) return undefined;
  return data?.find((b) => b.bin === bin);
}
