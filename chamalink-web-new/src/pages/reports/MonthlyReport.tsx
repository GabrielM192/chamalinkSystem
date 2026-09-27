import { Fragment, useState } from 'react';
import { ChevronDown, Download } from 'lucide-react';
import { Button, Card } from '../../components/ui';
import { num, periodStatus } from '../../lib/truth';
import { useTruth } from '../truth/TruthContext';
import { STATUS_ORDER, downloadCsv, pct, type ClosedStatus, type MonthAgg } from './reportData';

import { t } from '../../i18n';
const TONE_TEXT: Record<string, string> = {
  good: 'text-standing-good',
  warn: 'text-standing-warn',
  overdue: 'text-standing-overdue',
  neutral: 'text-ink-600',
};
const TONE_CHIP: Record<string, string> = {
  good: 'bg-standing-good-bg text-standing-good',
  warn: 'bg-standing-warn-bg text-standing-warn',
  overdue: 'bg-standing-overdue-bg text-standing-overdue',
  neutral: 'bg-ink-50 text-ink-700',
};

function rateCls(m: MonthAgg) {
  if (m.open || m.stale) return 'text-ink-600';
  return m.rate >= 0.9 ? 'text-standing-good' : m.rate >= 0.7 ? 'text-standing-warn' : 'text-standing-overdue';
}

/** Uzingatiaji wa kila mwezi: nani alilipa kwa wakati, nani alichelewa, nani bado. */
export function MonthlyReport({ months }: { months: MonthAgg[] }) {
  const [open, setOpen] = useState<string | null>(null);
  const rows = [...months].reverse(); // mwezi wa karibuni juu
  const toggle = (m: string) => setOpen((o) => (o === m ? null : m));

  function exportCsv() {
    downloadCsv(
      'ripoti_kila_mwezi.csv',
      [t('Mwezi'), t('Wanachama'), t('Kwa wakati'), t('Walichelewa'), t('Sehemu'), t('Hawajalipa'), t('Kinatarajiwa'), t('Kimelipwa'), t('Kiwango'), t('Faini zilizotozwa'), t('Faini zilizolipwa'), t('Misamaha'), t('Kinadaiwa')],
      months.map((m) => [
        m.label + (m.open ? t(' (unaendelea)') : m.stale ? t(' (PDF haijafika)') : ''),
        m.members,
        m.counts.PaidOnTime,
        m.counts.PaidLate,
        m.counts.PartiallyPaid,
        m.counts.Unpaid,
        m.expected,
        m.paid,
        pct(m.rate),
        m.fineCharged,
        m.finePaid,
        m.finesWaived,
        m.outstanding,
      ])
    );
  }

  return (
    <Card
      title={t("Uzingatiaji wa kila mwezi")}
      subtitle={t("Bofya mwezi kuona majina ya wanachama kwa kila hali")}
      action={
        <Button size="sm" variant="secondary" icon={Download} onClick={exportCsv} className="print:hidden">
          CSV
        </Button>
      }
      bodyClassName="p-0"
    >
      {/* Desktop */}
      <div className="hidden overflow-x-auto md:block">
        <table className="w-full text-sm">
          <thead className="bg-ink-50 text-left text-[11px] uppercase tracking-wide text-ink-600">
            <tr>
              <th className="px-4 py-2.5">{t("Mwezi")}</th>
              <th className="px-3 py-2.5 text-center" title={t("Kwa wakati")}>{t("✓ Wakati")}</th>
              <th className="px-3 py-2.5 text-center">{t("Walichelewa")}</th>
              <th className="px-3 py-2.5 text-center">{t("Sehemu")}</th>
              <th className="px-3 py-2.5 text-center">{t("Hawajalipa")}</th>
              <th className="px-3 py-2.5 text-right">{t("Kimelipwa / Kinatarajiwa")}</th>
              <th className="px-3 py-2.5 text-right">{t("Kiwango")}</th>
              <th className="px-3 py-2.5 text-right">{t("Faini (lipwa/tozwa)")}</th>
              <th className="px-4 py-2.5 text-right">{t("Kinadaiwa")}</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-ink-100">
            {rows.map((m) => (
              <Fragment key={m.month}>
                <tr
                  onClick={() => toggle(m.month)}
                  onKeyDown={(e) => e.key === 'Enter' && toggle(m.month)}
                  tabIndex={0}
                  aria-expanded={open === m.month}
                  className="cursor-pointer transition-colors hover:bg-ink-50/60 focus:bg-ink-50 focus:outline-none"
                >
                  <td className="whitespace-nowrap px-4 py-2.5 font-medium text-ink-950">
                    <span className="inline-flex items-center gap-1.5">
                      <ChevronDown size={14} className={`text-ink-500 transition-transform ${open === m.month ? 'rotate-180' : ''}`} />
                      {m.label}
                      <MonthBadge m={m} />
                    </span>
                  </td>
                  <td className="figure px-3 py-2.5 text-center text-standing-good">{m.counts.PaidOnTime}</td>
                  <td className="figure px-3 py-2.5 text-center text-standing-warn">{m.counts.PaidLate || '—'}</td>
                  <td className="figure px-3 py-2.5 text-center text-standing-warn">{m.counts.PartiallyPaid || '—'}</td>
                  <td className={`figure px-3 py-2.5 text-center ${m.open ? 'text-ink-600' : 'text-standing-overdue'}`}>{m.open ? m.counts.Open : m.counts.Unpaid || '—'}</td>
                  <td className="figure px-3 py-2.5 text-right">
                    {num(m.paid)} <span className="text-ink-500">/ {num(m.expected)}</span>
                  </td>
                  <td className={`figure px-3 py-2.5 text-right font-semibold ${rateCls(m)}`}>{pct(m.rate)}</td>
                  <td className="figure px-3 py-2.5 text-right text-ink-700">
                    {num(m.finePaid)} <span className="text-ink-500">/ {num(m.fineCharged)}</span>
                  </td>
                  <td
                    className={`figure px-4 py-2.5 text-right ${m.open ? 'text-ink-600' : m.outstanding > 0 ? 'font-semibold text-standing-overdue' : 'text-ink-500'}`}
                    title={m.open ? t("Mwezi unaendelea — bado si deni") : undefined}
                  >
                    {num(m.outstanding)}
                  </td>
                </tr>
                {open === m.month && (
                  <tr className="bg-ink-50/40">
                    <td colSpan={9} className="px-4 py-3">
                      <MonthPeople m={m} />
                    </td>
                  </tr>
                )}
              </Fragment>
            ))}
          </tbody>
        </table>
      </div>

      {/* Simu */}
      <ul className="divide-y divide-ink-100 md:hidden">
        {rows.map((m) => (
          <li key={m.month}>
            <button onClick={() => toggle(m.month)} aria-expanded={open === m.month} className="w-full px-4 py-3 text-left active:bg-ink-50">
              <div className="flex items-center justify-between gap-3">
                <span className="flex items-center gap-1.5 font-medium text-ink-950">
                  <ChevronDown size={14} className={`text-ink-500 transition-transform ${open === m.month ? 'rotate-180' : ''}`} />
                  {m.label}
                  <MonthBadge m={m} />
                </span>
                <span className={`figure text-sm font-semibold ${rateCls(m)}`}>{pct(m.rate)}</span>
              </div>
              <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-ink-50">
                <div className={`h-full ${m.open || m.stale ? 'bg-ink-500/50' : 'bg-ink-800'}`} style={{ width: `${Math.min(100, m.rate * 100)}%` }} />
              </div>
              <div className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-ink-600">
                <span className="text-standing-good">✓ {m.counts.PaidOnTime}</span>
                {m.counts.PaidLate > 0 && <span className="text-standing-warn">{t("Walichelewa")}{' '}{m.counts.PaidLate}</span>}
                {m.counts.PartiallyPaid > 0 && <span className="text-standing-warn">{t("Sehemu")}{' '}{m.counts.PartiallyPaid}</span>}
                {m.counts.Unpaid > 0 && <span className="text-standing-overdue">{t("Hawajalipa")}{' '}{m.counts.Unpaid}</span>}
                <span className="figure ml-auto">{num(m.paid)} / {num(m.expected)}</span>
              </div>
            </button>
            {open === m.month && (
              <div className="bg-ink-50/40 px-4 pb-4 pt-1">
                <MonthPeople m={m} />
              </div>
            )}
          </li>
        ))}
      </ul>
    </Card>
  );
}

function MonthPeople({ m }: { m: MonthAgg }) {
  const { openMember } = useTruth();
  const groups = STATUS_ORDER.filter((s) => m.byStatus[s].length > 0);
  return (
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
      {groups.map((s: ClosedStatus) => {
        const st = periodStatus(s);
        return (
          <div key={s}>
            <div className={`mb-1.5 text-xs font-semibold ${TONE_TEXT[st.tone]}`}>
              {st.label} · {m.byStatus[s].length}
            </div>
            <div className="flex flex-wrap gap-1.5">
              {m.byStatus[s].map((p) => (
                <button
                  key={p.memberId}
                  onClick={(e) => {
                    e.stopPropagation();
                    openMember(p.memberId);
                  }}
                  className={`rounded-full px-2.5 py-1 text-xs transition-opacity hover:opacity-80 ${TONE_CHIP[st.tone]}`}
                  title={p.outstanding > 0 ? t("Anadaiwa {0}", { 0: num(p.outstanding) }) : undefined}
                >
                  {p.name}
                  {p.outstanding > 0 && <span className="figure ml-1 opacity-80">{num(p.outstanding)}</span>}
                </button>
              ))}
            </div>
          </div>
        );
      })}
    </div>
  );
}

function MonthBadge({ m }: { m: MonthAgg }) {
  if (m.open) return <span className="rounded-full bg-ink-50 px-2 py-0.5 text-[10px] font-medium text-ink-600">{t("unaendelea")}</span>;
  if (m.stale)
    return (
      <span className="rounded-full bg-standing-warn-bg px-2 py-0.5 text-[10px] font-medium text-standing-warn" title={t("Malipo ya baada ya PDF ya mwisho hayajaingizwa")}>
        {t("PDF haijafika")}
      </span>
    );
  return null;
}
