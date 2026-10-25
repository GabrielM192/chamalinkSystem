/**
 * Nembo ya ChamaLink (SASISHO22).
 *
 * Alama ni monogram ya herufi mbili: "C" ya dhahabu (mduara wazi, kama sarafu isiyofungwa)
 * na "L" ndogo ya rangi ya karatasi iliyokaa ndani ya mlango wa C. Maana yake:
 * pesa inaonekana na kila mwanachama anaweza kuifungua na kuithibitisha.
 *
 * Kanuni tatu za nembo hii:
 *  1. Rangi zake hazibadiliki kwenye dark mode (tile inabaki bluu-giza, C inabaki dhahabu) —
 *     brand haipaswi kuwa rangi nyingine usiku.
 *  2. Inasomeka kwa ukubwa wa 16px (favicon) bila maelezo ya ziada: herufi moja kubwa + mwanya.
 *  3. `mono` ni toleo la rangi moja (currentColor) kwa kuchapisha na kwa mahali penye rangi nyingi.
 */
import { useId } from 'react';

type MarkProps = {
  size?: number;
  className?: string;
  /** Toleo la rangi moja (currentColor) — printa, watermark, au sehemu yenye rangi nyingi. */
  mono?: boolean;
  /** Nembo pekee bila maandishi kwa wasomaji wa skrini (kwenye lockup maandishi yapo karibu). */
  decorative?: boolean;
};

export function BrandMark({ size = 32, className = '', mono = false, decorative = false }: MarkProps) {
  // useId inahakikisha kila alama ina gradient yake (SVG za kurasa moja hazigongani)
  const sheenId = `brand-sheen-${useId().replace(/[^a-zA-Z0-9]/g, '')}`;
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 32 32"
      className={className}
      role={decorative ? undefined : 'img'}
      aria-label={decorative ? undefined : 'ChamaLink'}
      aria-hidden={decorative ? true : undefined}
      focusable="false"
    >
      {mono ? (
        <>
          <rect x="0.9" y="0.9" width="30.2" height="30.2" rx="8.6" fill="none" stroke="currentColor" strokeWidth="1.4" />
          <path d="M21.4 10.6a7.4 7.4 0 1 0 0 10.8" fill="none" stroke="currentColor" strokeWidth="3.1" strokeLinecap="round" />
          <path d="M16.8 12.6v7h3.2" fill="none" stroke="currentColor" strokeWidth="2.1" strokeLinecap="round" strokeLinejoin="round" />
        </>
      ) : (
        <>
          <defs>
            <linearGradient id={sheenId} x1="0" y1="0" x2="0.9" y2="1">
              <stop offset="0" stopColor="var(--color-brand-tile)" />
              <stop offset="1" stopColor="var(--color-brand-tile-2)" />
            </linearGradient>
          </defs>
          <rect width="32" height="32" rx="9" fill={`url(#${sheenId})`} />
          <path d="M21.4 10.6a7.4 7.4 0 1 0 0 10.8" fill="none" stroke="var(--color-brand-gold)" strokeWidth="3.1" strokeLinecap="round" />
          <path d="M16.8 12.6v7h3.2" fill="none" stroke="var(--color-brand-paper)" strokeWidth="2.1" strokeLinecap="round" strokeLinejoin="round" />
        </>
      )}
    </svg>
  );
}

/**
 * Nembo + jina. `tone` inabadilisha rangi ya jina pekee, si ya alama.
 * onDark = juu ya paneli ya bluu-giza (kichwa, ukurasa wa kuingia)
 * onLight = juu ya karatasi (nyaraka, printa)
 */
export function BrandLockup({
  size = 28,
  className = '',
  tone = 'onDark',
  showWordmark = true,
}: {
  size?: number;
  className?: string;
  tone?: 'onDark' | 'onLight';
  showWordmark?: boolean;
}) {
  return (
    <span className={`inline-flex min-w-0 items-center gap-2.5 ${className}`}>
      <BrandMark size={size} decorative />
      {showWordmark && (
        <span
          className={`truncate text-[17px] leading-none font-semibold tracking-tight ${
            tone === 'onLight' ? 'text-ink-950' : 'text-ink-50'
          }`}
        >
          Chama<span className="text-gold-500">Link</span>
        </span>
      )}
    </span>
  );
}
