/**
 * Auth DTOs mirroring `FairShareMonApi/Models/Auth/**`. Datetimes arrive as
 * offset-aware ISO-8601 strings (never parsed for math — formatted for display).
 */

export interface RegisterRequest {
  username: string;
  password: string;
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface RefreshRequest {
  refreshToken: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

/** `UserResponse { uuid, username, tier, createdAt }`. */
export interface UserResponse {
  uuid: string;
  username: string;
  tier: string;
  createdAt: string;
  /**
   * ADMIN-guard seam. The backend `UserResponse` does NOT currently expose a
   * role field (see planning/frontend-foundation.md Assumptions + OQ), so this
   * is always undefined today and the admin route fails safe (denies). Confirm
   * the role source against the live payload during the admin cycle.
   */
  role?: string;
}

/** `TokenPairResponse` — returned once by login/refresh; the client persists it. */
export interface TokenPairResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}
