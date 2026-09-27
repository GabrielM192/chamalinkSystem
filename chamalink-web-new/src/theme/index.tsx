/* eslint-disable react-refresh/only-export-components -- provider + t()/hook zinakaa pamoja kwa makusudi (kama AuthContext) */
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';

/**
 * Mwonekano: light (chaguo-msingi) au dark. Rangi zote ni tokens za CSS (src/index.css):
 * dark inabadilisha thamani za tokens chini ya  html[data-theme="dark"]  — hakuna rangi ya moja kwa moja kwenye kurasa.
 * index.html inaweka data-theme KABLA React haijaanza ili kusiwe na mmweko wa rangi.
 */
export type Theme = 'light' | 'dark';
const STORAGE_KEY = 'chamalink.theme';

function readTheme(): Theme {
  try {
    return localStorage.getItem(STORAGE_KEY) === 'dark' ? 'dark' : 'light';
  } catch {
    return 'light';
  }
}

function apply(theme: Theme) {
  const el = document.documentElement;
  el.dataset.theme = theme;
  el.style.colorScheme = theme; // scrollbars, date pickers, form controls za kivinjari
}

interface ThemeCtx {
  theme: Theme;
  setTheme: (t: Theme) => void;
  toggle: () => void;
}
const Ctx = createContext<ThemeCtx | null>(null);

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(readTheme);

  useEffect(() => apply(theme), [theme]);

  const setTheme = useCallback((t: Theme) => {
    try {
      localStorage.setItem(STORAGE_KEY, t);
    } catch {
      /* ignore */
    }
    setThemeState(t);
  }, []);
  const toggle = useCallback(() => setTheme(theme === 'dark' ? 'light' : 'dark'), [theme, setTheme]);

  const value = useMemo(() => ({ theme, setTheme, toggle }), [theme, setTheme, toggle]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useTheme(): ThemeCtx {
  const c = useContext(Ctx);
  if (!c) throw new Error('useTheme nje ya ThemeProvider');
  return c;
}
