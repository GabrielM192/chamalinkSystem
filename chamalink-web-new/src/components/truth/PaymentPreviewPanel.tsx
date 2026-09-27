import { ArrowRight, TrendingDown, TrendingUp } from 'lucide-react';
import { motion } from 'framer-motion';
import type { MoneySummary, PaymentPreview } from '../../api/truth';
import { allocationTarget, monthLabel, num, periodStatus } from '../../lib/truth';
import { StatusBadge } from '../StatusBadge';

import { t } from '../../i18n';
/**
 * "Nini kitatokea?" — mgawanyo wa malipo na hali kabla/baada.
 * Namba zote zinatoka kwa seva (replay ya injini), si hesabu ya kivinjari.
 */
export function PaymentPreviewPanel({ preview, amount }: { preview: PaymentPreview; amount: number }) {
  const allocs = preview.newAllocations.filter((a) => a.amount > 0);
  const total = allocs.reduce((s, a) => s + a.amount, 0) || 1;

  return (
    <div className="space-y-5">
      {/* 1. Mgawanyo */}
      <div>
        <h4 className="text-xs font-semibold uppercase tracking-wide text-ink-600">{t("Pesa itakwenda wapi")}</h4>
        {allocs.length === 0 ? (
          <p className="mt-2 text-sm text-ink-600">{t("Hakuna mgawanyo — angalia matatizo hapo juu.")}</p>
        ) : (
          <>
            <div className="mt-2 flex h-3 overflow-hidden rounded-full bg-ink-100" aria-hidden="true">
              {allocs.map((a, i) => (
                <motion.div
                  key={i}
                  className={`${allocationTarget(a.target).dot} border-r border-paper-raised last:border-0`}
                  initial={{ width: 0 }}
                  animate={{ width: `${(a.amount / total) * 100}%` }}
                  transition={{ duration: 0.4, delay: i * 0.05 }}
                />
              ))}
            </div>
            <ol className="mt-3 space-y-2">
              {allocs.map((a, i) => (
                <li key={i} className="flex items-start gap-3 rounded-lg bg-paper-raised px-3 py-2 ring-1 ring-ink-100">
                  <span className="figure mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-ink-50 text-[10px] font-bold text-ink-700">
                    {i + 1}
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-x-2 text-sm font-medium text-ink-950">
                      <span className={`h-2 w-2 rounded-full ${allocationTarget(a.target).dot}`} />
                      {allocationTarget(a.target).label}
                      {a.period && <span className="text-ink-600">· {monthLabel(a.period, true)}</span>}
                    </div>
                    <div className="mt-0.5 text-xs text-ink-600">{a.explanation}</div>
                  </div>
                  <span className="figure text-sm font-semibold">{num(a.amount)}</span>
                </li>
              ))}
            </ol>
            {Math.round(total) !== Math.round(amount) && amount > 0 && (
              <p className="mt-2 text-xs text-standing-warn">
                {t("Kiasi kilichogawanywa (")}{num(total)}{t(") si sawa na kiasi kilichoingizwa (")}{num(amount)}).
              </p>
            )}
          </>
        )}
      </div>

      {/* 2. Kabla -> Baada */}
      <div>
        <h4 className="text-xs font-semibold uppercase tracking-wide text-ink-600">{t("Hali ya mwanachama")}</h4>
        <div className="mt-2 overflow-hidden rounded-xl ring-1 ring-ink-100">
          <div className="grid grid-cols-[1fr_auto_auto_auto] items-center gap-x-2 bg-ink-50 px-3 py-1.5 text-[11px] font-semibold uppercase tracking-wide text-ink-600">
            <span />
            <span className="w-20 text-right">{t("Sasa")}</span>
            <span />
            <span className="w-20 text-right">{t("Baada")}</span>
          </div>
          <CompareRow label={t("Deni la michango")} b={preview.before} a={preview.after} k="arrears" />
          <CompareRow label={t("Kianzio kinadaiwa")} b={preview.before} a={preview.after} k="joinFeeOutstanding" />
          <CompareRow label={t("Akiba")} b={preview.before} a={preview.after} k="savings" goodWhenUp />
          <CompareRow label={t("Jumla ya deni")} b={preview.before} a={preview.after} k="totalDebtIncludingJoinFee" strong />
        </div>
        <div className="mt-2 flex flex-wrap items-center gap-1.5 text-xs text-ink-700">
          <span className="text-ink-600">{t("Miezi inayodaiwa:")}</span>
          {preview.after.monthsInArrears.length === 0 ? (
            <span className="font-medium text-standing-good">{t("hakuna")}</span>
          ) : (
            preview.after.monthsInArrears.map((m) => (
              <span key={m} className="rounded-full bg-standing-overdue-bg px-2 py-0.5 font-medium text-standing-overdue">
                {monthLabel(m)}
              </span>
            ))
          )}
          {preview.before.monthsInArrears
            .filter((m) => !preview.after.monthsInArrears.includes(m))
            .map((m) => (
              <span key={m} className="rounded-full bg-standing-good-bg px-2 py-0.5 font-medium text-standing-good line-through">
                {monthLabel(m)}
              </span>
            ))}
        </div>
      </div>

      {/* 3. Miezi iliyobadilika */}
      {preview.changedMonths.length > 0 && (
        <div>
          <h4 className="text-xs font-semibold uppercase tracking-wide text-ink-600">{t("Miezi itakayobadilika")}</h4>
          <ul className="mt-2 divide-y divide-ink-100 rounded-xl bg-paper-raised ring-1 ring-ink-100">
            {preview.changedMonths.map((c) => {
              const sb = c.statusBefore === '-' ? null : periodStatus(c.statusBefore);
              const sa = periodStatus(c.statusAfter);
              return (
                <li key={c.month} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2 text-sm">
                  <span className="w-20 font-medium">{monthLabel(c.month)}</span>
                  <span className="flex flex-1 flex-wrap items-center gap-1.5">
                    {sb && <StatusBadge label={sb.short} tone={sb.tone} />}
                    <ArrowRight size={13} className="text-ink-500" />
                    <StatusBadge label={sa.short} tone={sa.tone} />
                  </span>
                  <span className="figure text-xs text-ink-700">
                    {t("deni")}{' '}{num(c.outstandingBefore)} → <b className={c.outstandingAfter > 0 ? 'text-standing-overdue' : 'text-standing-good'}>{num(c.outstandingAfter)}</b>
                  </span>
                </li>
              );
            })}
          </ul>
          <p className="mt-1.5 text-xs text-ink-600">
            {t("Kumbuka: malipo ya zamani yanaweza kubadilisha hali ya miezi iliyofuata (mf. faini isitozwe kwa sababu deni lililipwa kabla ya tarehe ya mwisho).")}
          </p>
        </div>
      )}
    </div>
  );
}

function CompareRow({
  label,
  b,
  a,
  k,
  strong,
  goodWhenUp,
}: {
  label: string;
  b: MoneySummary;
  a: MoneySummary;
  k: keyof Omit<MoneySummary, 'monthsInArrears'>;
  strong?: boolean;
  goodWhenUp?: boolean;
}) {
  const before = b[k] as number;
  const after = a[k] as number;
  const diff = after - before;
  const good = goodWhenUp ? diff > 0 : diff < 0;
  return (
    <div className={`grid grid-cols-[1fr_auto_auto_auto] items-center gap-x-2 border-t border-ink-100 bg-paper-raised px-3 py-2 text-sm ${strong ? 'font-semibold' : ''}`}>
      <span className="text-ink-800">{label}</span>
      <span className="figure w-20 text-right text-ink-600">{num(before)}</span>
      <span className="w-5">
        {diff !== 0 &&
          (diff < 0 ? (
            <TrendingDown size={14} className={good ? 'text-standing-good' : 'text-standing-overdue'} />
          ) : (
            <TrendingUp size={14} className={good ? 'text-standing-good' : 'text-standing-overdue'} />
          ))}
      </span>
      <span className={`figure w-20 text-right ${diff === 0 ? 'text-ink-950' : good ? 'text-standing-good' : 'text-standing-overdue'}`}>{num(after)}</span>
    </div>
  );
}
