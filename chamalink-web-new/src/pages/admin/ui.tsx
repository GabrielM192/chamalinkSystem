import type { ReactNode } from 'react';

/**
 * Vipande vidogo vya UI vya console ya msimamizi (SASISHO20) — components TU.
 * Maandishi/madarasa yako kwenye labels.ts (Fast Refresh).
 */

/** Kizuizi cha sehemu: kichwa + mwili. */
export function Section({ title, children, actions }: { title: string; children: ReactNode; actions?: ReactNode }) {
  return (
    <section className="rounded-xl border border-ink-100 bg-paper-raised">
      <header className="flex flex-wrap items-center justify-between gap-3 border-b border-ink-100 px-4 py-3">
        <h2 className="text-sm font-semibold uppercase tracking-wide text-ink-700">{title}</h2>
        {actions}
      </header>
      <div className="p-4">{children}</div>
    </section>
  );
}

export function Stat({ label, value, tone }: { label: string; value: string; tone?: 'warn' | 'overdue' }) {
  const toneCls = tone === 'overdue' ? 'text-standing-overdue' : tone === 'warn' ? 'text-standing-warn' : 'text-ink-950';
  return (
    <div className="rounded-xl border border-ink-100 bg-paper-raised px-4 py-3">
      <div className="text-xs uppercase tracking-wide text-ink-600">{label}</div>
      <div className={`text-2xl font-semibold tabular-nums ${toneCls}`}>{value}</div>
    </div>
  );
}

export function Kv({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-ink-100 bg-paper px-3 py-2">
      <div className="text-xs text-ink-600">{label}</div>
      <div className="text-sm font-medium text-ink-900 tabular-nums">{value}</div>
    </div>
  );
}
