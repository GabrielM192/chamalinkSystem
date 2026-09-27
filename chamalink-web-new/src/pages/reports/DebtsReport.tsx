import { useMemo, useState } from 'react';
import { CheckCircle2, Download, Search } from 'lucide-react';
import type { GroupTruth, MemberTruth } from '../../api/truth';
import { Button, Card, EmptyState, Input, Stat } from '../../components/ui';
import { Avatar } from '../../components/truth/MemberPicker';
import { matches, monthLabel, num, tsh } from '../../lib/truth';
import { useTruth } from '../truth/TruthContext';
import { downloadCsv } from './reportData';

import { t } from '../../i18n';
type Filter = 'all' | 'arrears' | 'joinfee';

/** Madeni ya michango + Kianzio kwa kila mwanachama (namba za seva, bila kuhesabu upya). */
export function DebtsReport({ truth, staleLabels = [] }: { truth: GroupTruth; staleLabels?: string[] }) {
  const { openMember } = useTruth();
  const [q, setQ] = useState('');
  const [filter, setFilter] = useState<Filter>('all');

  const all = truth.members;
  const jfDue = all.reduce((s, m) => s + m.totals.joinFeeDue, 0);
  const jfDone = all.filter((m) => m.totals.joinFeeDue > 0 && m.totals.joinFeeOutstanding === 0).length;
  const jfMembers = all.filter((m) => m.totals.joinFeeDue > 0).length;

  const counts = {
    all: all.filter((m) => m.totals.totalDebtIncludingJoinFee > 0).length,
    arrears: all.filter((m) => m.totals.arrears > 0).length,
    joinfee: all.filter((m) => m.totals.joinFeeOutstanding > 0).length,
  };

  const rows = useMemo(() => {
    const val = (m: MemberTruth) =>
      filter === 'arrears' ? m.totals.arrears : filter === 'joinfee' ? m.totals.joinFeeOutstanding : m.totals.totalDebtIncludingJoinFee;
    return all
      .filter((m) => val(m) > 0 && matches(q, m.fullName, m.memberNumber, ...m.phones))
      .sort((a, b) => val(b) - val(a) || a.fullName.localeCompare(b.fullName));
  }, [all, filter, q]);

  const sum = rows.reduce(
    (s, m) => ({ arrears: s.arrears + m.totals.arrears, jf: s.jf + m.totals.joinFeeOutstanding, total: s.total + m.totals.totalDebtIncludingJoinFee }),
    { arrears: 0, jf: 0, total: 0 }
  );

  function exportCsv() {
    downloadCsv(
      'ripoti_madeni.csv',
      [t('Jina'), t('Namba'), t('Simu'), t('Miezi yenye deni'), t('Deni la michango'), t('Kianzio kilicholipwa'), t('Kianzio kinadaiwa'), t('Akiba'), t('Jumla ya deni')],
      rows.map((m) => [
        m.fullName,
        m.memberNumber,
        m.phones.join(' '),
        m.totals.monthsInArrears.map((x) => monthLabel(x)).join(', '),
        m.totals.arrears,
        m.totals.joinFeePaid,
        m.totals.joinFeeOutstanding,
        m.totals.savings,
        m.totals.totalDebtIncludingJoinFee,
      ])
    );
  }

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-4">
        <Stat label={t("Deni la michango")} value={tsh(truth.totals.arrears)} hint={t("Wanachama {0}", { 0: counts.arrears })} tone={truth.totals.arrears > 0 ? 'overdue' : 'good'} />
        <Stat label={t("Kianzio kinadaiwa")} value={tsh(truth.totals.joinFeeOutstanding)} hint={t("Wanachama {0}", { 0: counts.joinfee })} tone={truth.totals.joinFeeOutstanding > 0 ? 'warn' : 'good'} />
        <Stat label={t("Kianzio kilicholipwa")} value={tsh(truth.totals.joinFeePaid)} hint={t("{0} kati ya {1} wamemaliza", { 0: jfDone, 1: jfMembers })} />
        <Stat label={t("Akiba ya wanachama")} value={tsh(truth.totals.savings)} hint={t("Ziada — inalipa mwezi ujao usipolipa")} />
      </div>

      {jfDue > 0 && (
        <div className="rounded-2xl border border-ink-100 bg-paper-raised px-4 py-3">
          <div className="flex items-center justify-between gap-3 text-sm">
            <span className="font-medium text-ink-900">{t("Maendeleo ya Kianzio cha kikundi")}</span>
            <span className="figure shrink-0 whitespace-nowrap text-ink-700">
              {num(truth.totals.joinFeePaid)} / {num(jfDue)}
            </span>
          </div>
          <div className="mt-2 h-2 overflow-hidden rounded-full bg-ink-50">
            <div className="h-full bg-gold-600" style={{ width: `${Math.min(100, (truth.totals.joinFeePaid / jfDue) * 100)}%` }} />
          </div>
          <p className="mt-1.5 text-xs text-ink-600">{t("Deni la Kianzio linakatwa kwanza kwenye malipo ya msiba/sherehe kabla mwanachama hajalipwa.")}</p>
        </div>
      )}

      <Card
        title={t("Orodha ya wanaodaiwa")}
        subtitle={t("Bofya mwanachama kuona kila mwezi na kila malipo yalikokwenda")}
        action={
          <Button size="sm" variant="secondary" icon={Download} onClick={exportCsv} disabled={rows.length === 0} className="print:hidden">
            CSV
          </Button>
        }
        bodyClassName="p-0"
      >
        <div className="flex flex-col gap-3 border-b border-ink-100 p-3 print:hidden sm:flex-row sm:items-center">
          <div className="relative flex-1">
            <Search size={16} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-500" />
            <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder={t("Tafuta jina au simu...")} className="pl-9" />
          </div>
          <div className="flex gap-1.5 overflow-x-auto">
            {(
              [
                ['all', t('Deni lolote')],
                ['arrears', t('Michango')],
                ['joinfee', t('Kianzio')],
              ] as [Filter, string][]
            ).map(([f, l]) => (
              <button
                key={f}
                onClick={() => setFilter(f)}
                className={`shrink-0 rounded-full px-3 py-1.5 text-xs font-medium transition-colors ${
                  filter === f ? 'bg-ink-900 text-ink-50' : 'bg-paper-raised text-ink-700 ring-1 ring-ink-100 hover:bg-ink-50'
                }`}
              >
                {l} <span className="opacity-70">{counts[f]}</span>
              </button>
            ))}
          </div>
        </div>

        {rows.length === 0 ? (
          <div className="p-4">
            {q ? (
              <EmptyState icon={Search} title={t("Hakuna anayelingana na utafutaji")} />
            ) : (
              <EmptyState icon={CheckCircle2} title={t("Hakuna deni hapa")}>
                {t("Wanachama wote wako safi kwa kichujio hiki.")}
              </EmptyState>
            )}
          </div>
        ) : (
          <>
            <div className="hidden overflow-x-auto md:block">
              <table className="w-full text-sm">
                <thead className="bg-ink-50 text-left text-[11px] uppercase tracking-wide text-ink-600">
                  <tr>
                    <th className="px-4 py-2.5">{t("Mwanachama")}</th>
                    <th className="px-3 py-2.5">{t("Miezi yenye deni")}</th>
                    <th className="px-3 py-2.5 text-right">{t("Michango")}</th>
                    <th className="px-3 py-2.5 text-right">{t("Kianzio")}</th>
                    <th className="px-4 py-2.5 text-right">{t("Jumla")}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-ink-100">
                  {rows.map((m) => (
                    <tr
                      key={m.memberId}
                      onClick={() => openMember(m.memberId)}
                      onKeyDown={(e) => e.key === 'Enter' && openMember(m.memberId)}
                      tabIndex={0}
                      className="cursor-pointer transition-colors hover:bg-ink-50/60 focus:bg-ink-50 focus:outline-none"
                    >
                      <td className="px-4 py-2.5">
                        <div className="flex items-center gap-2.5">
                          <Avatar name={m.fullName} size="sm" />
                          <div className="min-w-0">
                            <div className="truncate font-medium text-ink-950">{m.fullName}</div>
                            <div className="figure truncate text-xs text-ink-600">{m.phones[0] ?? t("Hana simu")}</div>
                          </div>
                        </div>
                      </td>
                      <td className="px-3 py-2.5">
                        <MonthChips months={m.totals.monthsInArrears} stale={staleLabels} />
                      </td>
                      <td className={`figure px-3 py-2.5 text-right ${m.totals.arrears > 0 ? 'text-standing-overdue' : 'text-ink-500'}`}>{num(m.totals.arrears)}</td>
                      <td className={`figure px-3 py-2.5 text-right ${m.totals.joinFeeOutstanding > 0 ? 'text-standing-warn' : 'text-ink-500'}`}>
                        {num(m.totals.joinFeeOutstanding)}
                      </td>
                      <td className="figure px-4 py-2.5 text-right font-semibold text-ink-950">{num(m.totals.totalDebtIncludingJoinFee)}</td>
                    </tr>
                  ))}
                </tbody>
                <tfoot className="border-t-2 border-ink-100 bg-ink-50/60 text-sm font-semibold">
                  <tr>
                    <td className="px-4 py-2.5" colSpan={2}>
                      {t("Jumla (")}{rows.length})
                    </td>
                    <td className="figure px-3 py-2.5 text-right text-standing-overdue">{num(sum.arrears)}</td>
                    <td className="figure px-3 py-2.5 text-right text-standing-warn">{num(sum.jf)}</td>
                    <td className="figure px-4 py-2.5 text-right">{num(sum.total)}</td>
                  </tr>
                </tfoot>
              </table>
            </div>

            <ul className="divide-y divide-ink-100 md:hidden">
              {rows.map((m) => (
                <li key={m.memberId}>
                  <button onClick={() => openMember(m.memberId)} className="w-full px-4 py-3 text-left active:bg-ink-50">
                    <div className="flex items-center gap-3">
                      <Avatar name={m.fullName} />
                      <div className="min-w-0 flex-1">
                        <div className="truncate font-medium text-ink-950">{m.fullName}</div>
                        <div className="figure text-xs text-ink-600">
                          {t("Michango")}{' '}{num(m.totals.arrears)}{' '}{t("· Kianzio")}{' '}{num(m.totals.joinFeeOutstanding)}
                        </div>
                      </div>
                      <span className="figure text-sm font-semibold text-standing-overdue">{num(m.totals.totalDebtIncludingJoinFee)}</span>
                    </div>
                    {m.totals.monthsInArrears.length > 0 && (
                      <div className="mt-2 pl-11">
                        <MonthChips months={m.totals.monthsInArrears} stale={staleLabels} />
                      </div>
                    )}
                  </button>
                </li>
              ))}
              <li className="flex items-center justify-between bg-ink-50/60 px-4 py-3 text-sm font-semibold">
                <span>{t("Jumla (")}{rows.length})</span>
                <span className="figure">{num(sum.total)}</span>
              </li>
            </ul>
          </>
        )}
      </Card>
    </div>
  );
}

function MonthChips({ months, stale }: { months: string[]; stale: string[] }) {
  if (months.length === 0) return <span className="text-xs text-ink-500">—</span>;
  return (
    <div className="flex flex-wrap gap-1">
      {months.map((x) => (
        <span
          key={x}
          title={stale.includes(x) ? t("PDF ya mwezi huu bado haijaingizwa — huenda amelipa") : undefined}
          className={`rounded-md px-1.5 py-0.5 text-[11px] font-medium ${
            stale.includes(x) ? 'border border-dashed border-standing-warn/60 bg-standing-warn-bg text-standing-warn' : 'bg-standing-overdue-bg text-standing-overdue'
          }`}
        >
          {monthLabel(x)}
          {stale.includes(x) && '?'}
        </span>
      ))}
    </div>
  );
}
