import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { endSession, getAccessToken, setAccessToken } from './session';

// Anwani ya API inatoka kwenye environment (.env / VITE_API_BASE_URL) — hakuna anwani iliyoandikwa ndani ya code.
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5168/api';

// withCredentials: cookie ya refresh (HttpOnly, SameSite=Strict, Path=/api/auth) inatumwa kwenye /auth/* tu.
export const apiClient = axios.create({ baseURL: API_BASE_URL, withCredentials: true });

apiClient.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = getAccessToken();
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

// Refresh moja tu kwa wakati (maombi mengi yakipata 401 pamoja, yote yanasubiri refresh ile ile).
let refreshing: Promise<string | null> | null = null;

export interface SessionPayload {
  token: string;
  userId: string;
  fullName: string;
  email: string;
  expiresInSeconds?: number;
}

/** Omba access token mpya kwa cookie ya refresh. Inarudisha null kama hakuna kikao halali. */
export function refreshSession(): Promise<SessionPayload | null> {
  const run = axios
    .post<SessionPayload>(`${API_BASE_URL}/auth/refresh`, null, { withCredentials: true })
    .then((r) => {
      setAccessToken(r.data.token);
      return r.data;
    })
    .catch(() => {
      setAccessToken(null);
      return null;
    });
  refreshing = run.then((d) => d?.token ?? null).finally(() => {
    refreshing = null;
  });
  return run;
}

const isAuthCall = (url?: string) => !!url && /\/auth\//i.test(url);

apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const original = error.config as (InternalAxiosRequestConfig & { _retried?: boolean }) | undefined;
    if (error.response?.status === 401 && original && !original._retried && !isAuthCall(original.url)) {
      original._retried = true;
      const token = await (refreshing ?? refreshSession().then((d) => d?.token ?? null));
      if (token) {
        original.headers.Authorization = `Bearer ${token}`;
        return apiClient(original);
      }
      endSession();
    }
    return Promise.reject(error);
  }
);
