/* eslint-disable react-refresh/only-export-components -- provider + t()/hook zinakaa pamoja kwa makusudi (kama AuthContext) */
import { createContext, Fragment, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { en } from './en';

/**
 * Lugha za kiolesura (frontend pekee — ujumbe wa server bado ni Kiswahili; utabadilishwa kwa misimbo baadaye).
 *
 * Mbinu: maandishi ya Kiswahili ndiyo CHANZO na ndiyo ufunguo:  t('Hifadhi')  →  'Save' (en)  |  'Hifadhi' (sw)
 *  - Tafsiri ikikosekana, Kiswahili kinaonekana — ukurasa hauvunjiki.
 *  - `npm run i18n:check` inahakikisha kila t('...') ina tafsiri ya Kiingereza.
 *  - Vigeu: t('Wanachama {n} wanadaiwa', { n: 3 }).
 *
 * t() ni function ya kawaida (si hook) ili itumike pia kwenye helpers (tarehe, miezi). Lugha ikibadilika,
 * mti wa kurasa unajengwa upya (key={lang}) ili kila maandishi yasomwe upya kwa lugha mpya.
 */
export type Lang = 'sw' | 'en';
export const LANGS: { id: Lang; label: string; short: string }[] = [
  { id: 'sw', label: 'Kiswahili', short: 'SW' },
  { id: 'en', label: 'English', short: 'EN' },
];

const STORAGE_KEY = 'chamalink.lang';
const DEFAULT_LANG: Lang = 'sw';

function initialLang(): Lang {
  try {
    const v = localStorage.getItem(STORAGE_KEY);
    if (v === 'sw' || v === 'en') return v;
  } catch {
    /* localStorage haipatikani — tumia chaguo-msingi */
  }
  return DEFAULT_LANG;
}

let current: Lang = initialLang();

/** Lugha ya sasa (kwa helpers zisizo React, mf. majina ya miezi). */
export function getLang(): Lang {
  return current;
}

/** Locale ya Intl kwa lugha ya sasa. */
export function getLocale(): string {
  return current === 'en' ? 'en-GB' : 'sw-TZ';
}

const dictionaries: Record<Lang, Record<string, string> | null> = { sw: null, en };

export function t(sw: string, vars?: Record<string, string | number | null | undefined>): string {
  const dict = dictionaries[current];
  let s = (dict && dict[sw]) || sw;
  if (vars) s = s.replace(/\{(\w+)\}/g, (m, k: string) => (k in vars ? String(vars[k] ?? '') : m));
  return s;
}

/**
 * Alama ya maandishi yaliyo kwenye data ya ngazi ya moduli (tabo, hali, orodha) — inarudisha maandishi yaleyale.
 * Yanatafsiriwa kwa t() pale yanapoonyeshwa; alama hii inaruhusu `npm run i18n:check` kuyaona.
 */
export function tk(sw: string): string {
  return sw;
}

/** Chagua kati ya maandishi mawili kwa idadi (mf. "mwanachama 1" / "wanachama 3"). */
export function tn(n: number, one: string, many: string, vars?: Record<string, string | number | null | undefined>): string {
  return t(n === 1 ? one : many, { n, ...vars });
}

interface I18nCtx {
  lang: Lang;
  setLang: (l: Lang) => void;
}
const Ctx = createContext<I18nCtx | null>(null);

export function I18nProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Lang>(current);

  const setLang = useCallback((l: Lang) => {
    current = l;
    try {
      localStorage.setItem(STORAGE_KEY, l);
    } catch {
      /* ignore */
    }
    setLangState(l);
  }, []);

  useEffect(() => {
    document.documentElement.lang = lang;
  }, [lang]);

  const value = useMemo(() => ({ lang, setLang }), [lang, setLang]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

/**
 * Hujenga upya kurasa zilizo ndani yake lugha ikibadilika (key={lang}), ili kila t('...') isomwe upya.
 * Huwekwa CHINI ya providers za kuingia/kikundi — hali hizo hazipotei wakati wa kubadili lugha.
 */
export function LangBoundary({ children }: { children: ReactNode }) {
  const { lang } = useI18n();
  return <Fragment key={lang}>{children}</Fragment>;
}

export function useI18n(): I18nCtx {
  const c = useContext(Ctx);
  if (!c) throw new Error('useI18n nje ya I18nProvider');
  return c;
}
