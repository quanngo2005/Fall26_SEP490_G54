export interface LoginRequest {
  email: string;
  password: string;
  rememberMe: boolean;
}

export interface AuthenticatedUser {
  id: string;
  email: string;
  fullName: string;
  roles: string[];
  permissions: string[];
  /** True while the account still uses a one-time password and must choose a new one before using the app. */
  mustChangePassword?: boolean;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  refreshTokenExpiresAt: string;
  user: AuthenticatedUser;
}

export type StoredAuthSession = AuthResponse;
