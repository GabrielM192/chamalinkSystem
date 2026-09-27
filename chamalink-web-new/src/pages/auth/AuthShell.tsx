import { Preferences } from '../../components/Preferences';
import type { ReactNode } from 'react';

import { t } from '../../i18n';
/**
 * Ganda la kurasa za kuingia na kujisajili. Upande wa kushoto ni "daftari": mistari ya mlalo
 * inayokumbusha daftari la michango la mtunza-hazina — wazo moja la muonekano kwa kurasa zote mbili.
 */
export function AuthShell({ title, subtitle, children }: { title: string; subtitle: string; children: ReactNode }) {
  return (
    <div className="min-h-screen flex flex-col lg:flex-row">
      <div className="theme-chrome relative lg:w-[44%] bg-ink-900 text-ink-50 px-8 py-10 lg:px-14 lg:py-16 flex flex-col justify-between gap-8 overflow-hidden">
        <div
          className="pointer-events-none absolute inset-0 opacity-[0.15]"
          style={{
            backgroundImage:
              'repeating-linear-gradient(to bottom, transparent, transparent 47px, currentColor 47px, currentColor 48px)',
          }}
          aria-hidden="true"
        />
        <div className="relative flex items-center justify-between gap-3">
          <span className="font-semibold text-lg tracking-tight text-gold-500">ChamaLink</span>
          <Preferences />
        </div>
        <div className="relative max-w-sm">
          <p className="text-2xl lg:text-[28px] leading-snug font-medium text-ink-50">
            {t("Usimamizi wa fedha za kikundi, wazi kwa kila mwanachama.")}
          </p>
          <p className="mt-4 text-sm text-ink-100/80 leading-relaxed">
            {t("Michango, faini, kianzio na mikopo — yote kutoka M-Koba, kwenye kumbukumbu moja ambayo viongozi na wanachama wanaweza kuiona.")}
          </p>
        </div>
        <div className="relative hidden text-xs text-ink-100/50 lg:block">© {new Date().getFullYear()} ChamaLink</div>
      </div>

      <div className="flex-1 flex items-center justify-center bg-paper px-6 py-12 lg:py-16">
        <div className="w-full max-w-sm">
          <h1 className="text-2xl font-semibold text-ink-950 mb-1">{title}</h1>
          <p className="text-sm text-ink-700 mb-8">{subtitle}</p>
          {children}
        </div>
      </div>
    </div>
  );
}

export const authInputCls =
  'w-full rounded-lg border border-ink-100 bg-paper-raised px-3.5 py-2.5 text-ink-950 placeholder:text-ink-500/60 focus:outline-none focus:ring-2 focus:ring-ink-700 focus:border-transparent';
export const authButtonCls =
  'w-full rounded-lg bg-ink-900 text-ink-50 font-medium py-2.5 hover:bg-ink-800 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-ink-700 disabled:opacity-60 transition-colors';
