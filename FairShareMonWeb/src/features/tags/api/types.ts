/**
 * Tag DTOs — mirror `FairShareMonApi/Models/Tags/*`. Name-only (no color/icon/
 * default). Feature-local per the feature-first convention.
 */
export interface TagResponse {
  uuid: string;
  name: string;
  /** True for a soft-deleted tag — only present when `includeDeleted=true`. */
  isDeleted: boolean;
  /** ISO-8601, offset-aware. */
  createdAt: string;
}

export interface CreateTagRequest {
  name: string;
}

export interface UpdateTagRequest {
  name: string;
}
