import { useQuery } from "@tanstack/react-query";
import type { BlobResult } from "@/lib/api/client";
import { qrApi } from "../api/qrApi";

/**
 * QR blob queries. `enabled` is driven by the dialog's open state + Premium tier
 * (a Free user's dialog shows the upgrade panel and never enables the query).
 * `retry: false` because the terminal codes (13003 / 12001 / 12002 / 12003 /
 * ownership 404) are not transient — retrying only re-hits the same error. The
 * dialog owns the object-URL lifecycle over the returned `BlobResult`. QR is a
 * read — there is no mutation here.
 */

const qrKeys = {
  expense: (uuid: string, bankAccountUuid?: string) =>
    ["qr", "expense", uuid, bankAccountUuid ?? null] as const,
  event: (uuid: string, bankAccountUuid?: string) =>
    ["qr", "event", uuid, bankAccountUuid ?? null] as const,
};

export function useExpenseQrQuery(
  uuid: string,
  bankAccountUuid: string | undefined,
  enabled: boolean,
) {
  return useQuery<BlobResult>({
    queryKey: qrKeys.expense(uuid, bankAccountUuid),
    queryFn: () => qrApi.expenseQr(uuid, bankAccountUuid),
    enabled,
    retry: false,
    gcTime: 0,
  });
}

export function useEventQrQuery(
  uuid: string,
  bankAccountUuid: string | undefined,
  enabled: boolean,
) {
  return useQuery<BlobResult>({
    queryKey: qrKeys.event(uuid, bankAccountUuid),
    queryFn: () => qrApi.eventQr(uuid, bankAccountUuid),
    enabled,
    retry: false,
    gcTime: 0,
  });
}
