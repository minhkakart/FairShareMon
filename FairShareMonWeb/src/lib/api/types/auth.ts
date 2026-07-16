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

/**
 * `UserResponse { uuid, username, tier, role, createdAt }` — the caller's own
 * profile from `GET /v1/auth/me`. `role` is `USER` | `ADMIN` (the backend always
 * populates it); the admin guard branches on it.
 */
export interface UserResponse {
  uuid: string;
  username: string;
  tier: string;
  role: string;
  createdAt: string;
}

/** `TokenPairResponse` — returned once by login/refresh; the client persists it. */
export interface TokenPairResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}
