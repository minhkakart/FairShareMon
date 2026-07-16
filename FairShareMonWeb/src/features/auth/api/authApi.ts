import { api } from "@/lib/api/client";
import type { MessageResponse } from "@/lib/api/types/envelope";
import type {
  ChangePasswordRequest,
  LoginRequest,
  RegisterRequest,
  TokenPairResponse,
  UserResponse,
} from "@/lib/api/types/auth";

/**
 * Auth endpoints. register/login are anonymous (no Bearer); logout and
 * change-password are authenticated. Refresh is internal to the API client
 * (see lib/api/refresh.ts) and is not exposed here.
 */
export const authApi = {
  register: (body: RegisterRequest) =>
    api.post<UserResponse>("/v1/auth/register", body, { anonymous: true }),

  login: (body: LoginRequest) =>
    api.post<TokenPairResponse>("/v1/auth/login", body, { anonymous: true }),

  /** Current-user profile (authenticated; rides the client's 401→refresh flow). */
  me: () => api.get<UserResponse>("/v1/auth/me"),

  logout: () => api.post<MessageResponse>("/v1/auth/logout"),

  changePassword: (body: ChangePasswordRequest) =>
    api.post<MessageResponse>("/v1/auth/change-password", body),
};
