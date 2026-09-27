import { Moon, Sun } from 'lucide-react';
import { t, LANGS, useI18n } from '../i18n';
import { useTheme } from '../theme';

/**
 * Vitufe vya lugha (SW/EN) na mwonekano (light/dark). Vinakaa kwenye sehemu za "chrome"
 * (kichwa cha dashibodi na upande wa bluu wa kurasa za kuingia), kwa hiyo rangi ni za mandhari hiyo.
 * Chaguo zote zinakumbukwa kwenye kivinjari (localStorage).
 */
export function LanguageToggle({ className = '' }: { className?: string }) {
  const { lang, setLang } = useI18n();
  const next = LANGS.find((l) => l.id !== lang) ?? LANGS[0];
  return (
    <>
      {/* Simu: kitufe kimoja kinachobadilisha lugha (nafasi ni finyu) */}
      <button
        type="button"
        onClick={() => setLang(next.id)}
        aria-label={`${t('Lugha')}: ${next.label}`}
        title={next.label}
        className={`inline-flex h-8 items-center rounded-full border border-ink-700 px-2.5 text-xs font-semibold text-ink-100/80 transition-colors hover:text-ink-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-gold-500 sm:hidden ${className}`}
      >
        {next.short}
      </button>
    <div
      role="radiogroup"
      aria-label={t('Lugha')}
      className={`hidden items-center rounded-full border border-ink-700 p-0.5 text-xs font-semibold sm:inline-flex ${className}`}
    >
      {LANGS.map((l) => {
        const active = l.id === lang;
        return (
          <button
            key={l.id}
            type="button"
            role="radio"
            aria-checked={active}
            title={l.label}
            onClick={() => setLang(l.id)}
            className={`rounded-full px-2.5 py-1 transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-gold-500 ${
              active ? 'bg-gold-500 text-on-gold' : 'text-ink-100/80 hover:text-ink-50'
            }`}
          >
            {l.short}
          </button>
        );
      })}
    </div>
    </>
  );
}

export function ThemeToggle({ className = '' }: { className?: string }) {
  const { theme, toggle } = useTheme();
  const dark = theme === 'dark';
  const label = dark ? t('Badili kwenda mwanga') : t('Badili kwenda giza');
  return (
    <button
      type="button"
      onClick={toggle}
      aria-label={label}
      title={label}
      aria-pressed={dark}
      className={`inline-flex h-8 w-8 items-center justify-center rounded-full border border-ink-700 text-ink-100/80 transition-colors hover:text-ink-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-gold-500 ${className}`}
    >
      {dark ? <Sun size={15} /> : <Moon size={15} />}
    </button>
  );
}

export function Preferences({ className = '' }: { className?: string }) {
  return (
    <div className={`flex items-center gap-2 ${className}`}>
      <LanguageToggle />
      <ThemeToggle />
    </div>
  );
}
