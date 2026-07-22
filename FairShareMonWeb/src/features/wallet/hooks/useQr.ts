import { useQuery } from "@tanstack/react-query";
import type { MemberQrResponse } from "../api/types";
import { qrApi } from "../api/qrApi";

/**
 * Per-member QR queries. `enabled` is driven by the dialog's open state + Premium
 * tier (a Free user's dialog shows the upgrade panel and never enables the query).
 * `retry: false` because the terminal codes (13003 / 12001 / 12002 / 12003 /
 * ownership 404) are not transient — retrying only re-hits the same error. The
 * response is a `MemberQrResponse[]` whose `image` fields are data URLs, so there
 * is no object-URL lifecycle to own. QR is a read — there is no mutation here.
 */

const qrKeys = {
  expense: (uuid: string, bankAccountUuid?: string) =>
    ["qr", "expense", "members", uuid, bankAccountUuid ?? null] as const,
  event: (uuid: string, bankAccountUuid?: string) =>
    ["qr", "event", "members", uuid, bankAccountUuid ?? null] as const,
};

export function useExpenseMemberQrsQuery(
  uuid: string,
  bankAccountUuid: string | undefined,
  enabled: boolean,
) {
  return useQuery<MemberQrResponse[]>({
    queryKey: qrKeys.expense(uuid, bankAccountUuid),
    queryFn: () => qrApi.expenseMemberQrs(uuid, bankAccountUuid),
    enabled,
    retry: false,
    gcTime: 0,
  });
}

export function useEventMemberQrsQuery(
  uuid: string,
  bankAccountUuid: string | undefined,
  enabled: boolean,
) {
  return useQuery<MemberQrResponse[]>({
    queryKey: qrKeys.event(uuid, bankAccountUuid),
    queryFn: () => qrApi.eventMemberQrs(uuid, bankAccountUuid),
    enabled,
    retry: false,
    gcTime: 0,
  });
}
