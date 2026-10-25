import type { ReactNode } from 'react';
import { Preferences } from '../../components/Preferences';
import { BrandLockup } from '../../components/Brand';
import { t } from '../../i18n';

/**
 * Ganda la kurasa za kuingia, kujisajili na kurejesha neno la siri (SASISHO22).
 *
 * Muonekano: paneli moja ya bluu-giza yenye gradient tulivu (ile ile ya kichwa cha mfumo),
 * maandishi yake YAMEELEA KATIKATI ya urefu wote. Hakuna mistari ya daftari, hakuna risiti,
 * hakuna vibaji vya kutia moyo: kitu kinachoaminisha ni uwazi wa maneno, si picha.
 *
 * Maneno hayataji chanzo chochote cha pesa (M-Koba, benki, mtandao wa simu) kwa makusudi:
 * kikundi kinaweza kutumia chanzo chochote, na ukurasa huu ni wa kila kikundi.
 */
export function AuthShell({
  title,
  subtitle,
  children,
}: {
  title: string;
  subtitle: string;
  children: ReactNode;
}) {
  return (
    <div className="flex min-h-screen flex-col bg-paper lg:flex-row">
      <aside className="chrome-panel theme-chrome relative overflow-hidden px-6 pt-6 pb-9 text-ink-50 sm:px-10 lg:flex lg:w-[46%] lg:flex-col lg:justify-center lg:px-14 lg:py-16">
        {/* Kichwa cha paneli: nembo na lugha/mwonekano */}
        <div className="relative flex items-center justify-between gap-3 lg:absolute lg:inset-x-14 lg:top-12">
          <BrandLockup size={30} />
          <Preferences />
        </div>

        {/* Maandishi yaliyoelea katikati */}
        <div className="relative mt-14 max-w-[27rem] sm:mt-16 lg:mt-0 lg:max-w-[26rem]">
          <h2 className="text-[22px] leading-[1.3] font-semibold tracking-tight text-white sm:text-[26px] lg:text-[30px]">
            {t('Kila shilingi inaonekana. Kila mwanachama anaweza kuithibitisha.')}
          </h2>
          <p className="mt-3.5 text-[13px] leading-relaxed text-ink-100/75 sm:text-sm">
            {t('Michango, faini, kianzio na mikopo kwenye kumbukumbu moja wazi. Viongozi na wanachama wote wanaona kitu kimoja.')}
          </p>
        </div>

        <div className="relative mt-auto hidden pt-10 text-xs text-ink-100/60 lg:absolute lg:inset-x-14 lg:bottom-10 lg:mt-0 lg:pt-0 lg:block">
          © {new Date().getFullYear()} ChamaLink
        </div>
      </aside>

      <main className="flex flex-1 items-start justify-center px-5 py-9 sm:px-8 sm:py-12 lg:items-center lg:py-16">
        <div className="w-full max-w-[26rem]">
          <h1 className="text-2xl font-semibold tracking-tight text-ink-950">{title}</h1>
          <p className="mt-1.5 mb-7 text-sm text-ink-700">{subtitle}</p>
          {children}
        </div>
      </main>
    </div>
  );
}

/** Sehemu za fomu: mpaka mmoja mwembamba, hakuna kivuli, mwanga wa gold wakati wa kuingiza. */
export const authInputCls =
  'w-full rounded-xl border border-ink-100 bg-paper-raised px-3.5 py-2.5 text-ink-950 shadow-[0_1px_2px_rgb(15_27_48/0.04)] placeholder:text-ink-500/60 transition-colors focus:border-gold-500 focus:outline-none focus:ring-2 focus:ring-gold-500/25';

/** Kitufe kikuu cha ukurasa wa kuingia: dhahabu ya nembo, si bluu ya menyu. */
export const authButtonCls =
  'w-full rounded-xl bg-gold-500 py-2.5 font-semibold text-on-gold transition-colors hover:bg-gold-600 focus:outline-none focus-visible:ring-2 focus-visible:ring-gold-600 focus-visible:ring-offset-2 focus-visible:ring-offset-paper disabled:opacity-60';

/** Kiungo cha pembeni (Umesahau?), rangi tulivu isiyoshindana na kitufe. */
export const authLinkCls = 'text-xs font-medium text-ink-600 underline decoration-ink-100 underline-offset-4 transition-colors hover:text-ink-900';

/** Kiungo cha chini cha ukurasa (Jisajili). */
export const authFootLinkCls = 'font-medium text-ink-900 underline decoration-gold-500 underline-offset-4 transition-colors hover:text-ink-700';
