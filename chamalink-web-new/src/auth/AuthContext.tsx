import { createContext, useContext, useState, useCallback, useEffect, type ReactNode } from 'react';
import {
  login as loginRequest,
  logout as logoutRequest,
  revokeAllSessions,
  refreshSession,
  register as registerRequest,
  type AuthResponse,
  type LoginPayload,
  type RegisterPayload,
} from '../api/auth';
import { setSessionEndedHandler } from '../api/session';

interface SessionUser {
  userId: string;
  fullName: string;
  email: string;
}

interface AuthContextValue {
  user: SessionUser | null;
  /** false mpaka jaribio la kwanza la kurejesha kikao (cookie) likamilike */
  ready: boolean;
  login: (payload: LoginPayload) => Promise<void>;
  /** Inarudisha ujumbe wa jumla wa seva; HAIMwingizi mtumiaji (aingie kupitia login). */
  register: (payload: RegisterPayload) => Promise<string>;
  logout: () => Promise<void>;
  /** SASISHO15: toka kwenye vifaa VYOTE (kila refresh token inafutwa, access token zote zinabatilishwa kwa `tv`). */
  logoutEverywhere: () => Promise<void>;
}

/** Muda wa kuendeleza kikao (ms) — chini ya muda wa access token wa seva (Jwt:AccessTokenMinutes, chaguo-msingi dakika 15). */
const SESSION_REFRESH_MS = 10 * 60 * 1000;

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

const toUser = (r: AuthResponse): SessionUser => ({ userId: r.userId, fullName: r.fullName, email: r.email });

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SessionUser | null>(null);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    // SASISHO15: token za zamani zilizokaa localStorage zinafutwa (sasa zinakaa kwenye kumbukumbu tu)
    localStorage.removeItem('token');
    localStorage.removeItem('user');
    setSessionEndedHandler(() => setUser(null));
    let alive = true;
    refreshSession().then((s) => {
      if (!alive) return;
      if (s) setUser(toUser(s));
      setReady(true);
    });
    // Endeleza kikao kabla access token haijaisha (dakika ~15), ili ombi lisikatike katikati
    const timer = window.setInterval(() => {
      refreshSession().then((s) => {
        if (alive && !s) setUser(null);
      });
    }, SESSION_REFRESH_MS);
    return () => {
      alive = false;
      window.clearInterval(timer);
      setSessionEndedHandler(null);
    };
  }, []);

  const login = useCallback(async (payload: LoginPayload) => {
    setUser(toUser(await loginRequest(payload)));
  }, []);

  const register = useCallback((payload: RegisterPayload) => registerRequest(payload), []);

  const logout = useCallback(async () => {
    try {
      await logoutRequest();
    } finally {
      setUser(null);
    }
  }, []);

  const logoutEverywhere = useCallback(async () => {
    try {
      await revokeAllSessions();
    } finally {
      setUser(null);
    }
  }, []);

  return <AuthContext.Provider value={{ user, ready, login, register, logout, logoutEverywhere }}>{children}</AuthContext.Provider>;
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
