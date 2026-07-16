/**
 * Member DTOs — mirror `FairShareMonApi/Models/Members/*`. Feature-local per the
 * feature-first convention (auth types under `lib/api/types` were a foundation
 * exception).
 */
export interface MemberResponse {
  uuid: string;
  name: string;
  /** True for the ledger owner's representative member — renamable, never deletable. */
  isOwnerRepresentative: boolean;
  /** True for a soft-deleted member — only present when `includeDeleted=true`. */
  isDeleted: boolean;
  /** ISO-8601, offset-aware. */
  createdAt: string;
}

export interface CreateMemberRequest {
  name: string;
}

export interface UpdateMemberRequest {
  name: string;
}
