/**
 * Category DTOs — mirror `FairShareMonApi/Models/Categories/*`. Feature-local per
 * the feature-first convention.
 */
export interface CategoryResponse {
  uuid: string;
  name: string;
  /** Hex `#RRGGBB` — used for chart color; rendered verbatim, never re-computed. */
  color: string;
  /** Emoji glyph stored verbatim (🍜 🚗 …), or null/absent for no icon. */
  icon?: string | null;
  /** True for the ledger's single default category — never deletable, atomic swap. */
  isDefault: boolean;
  /** True for a soft-deleted category — only present when `includeDeleted=true`. */
  isDeleted: boolean;
  /** ISO-8601, offset-aware. */
  createdAt: string;
}

export interface CreateCategoryRequest {
  name: string;
  color: string;
  icon?: string | null;
}

export interface UpdateCategoryRequest {
  name: string;
  color: string;
  icon?: string | null;
}
