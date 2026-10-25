import { apiClient, refreshSession, type SessionPayload } from './client';
import { setAccessToken } from './session';

// Inalingana na ChamaLink.Application.DTOs.AuthResponseDto.
export type AuthResponse = SessionPayload;

export interface LoginPayload {
  email: string;
  password: string;
}

export interface RegisterPayload {
  fullName: string;
  email: string;
  phoneNumber: string;
  password: string;
}

// Njia za /auth kwa herufi ndogo: njia ya cookie (Path=/api/auth) ni case-sensitive kwenye browser.
export async function login(payload: LoginPayload): Promise<AuthResponse> {
  const response = await apiClient.post<AuthResponse>('/auth/login', payload);
  setAccessToken(response.data.token);
  return response.data;
}

/** Usajili unarudisha ujumbe wa jumla TU (hakuna token) — mtumiaji anaingia kupitia ukurasa wa login. */
export async function register(payload: RegisterPayload): Promise<string> {
  const response = await apiClient.post<{ message: string }>('/auth/register', payload);
  return response.data.message;
}

export async function logout(): Promise<void> {
  try {
    await apiClient.post('/auth/logout');
  } finally {
    setAccessToken(null);
  }
}

export async function revokeAllSessions(): Promise<void> {
  await apiClient.post('/auth/revoke-all');
  setAccessToken(null);
}

export { refreshSession };
