import { useMemo, useState } from 'react';
import { csvCell } from '../../lib/csv';
import { AlertTriangle, ArrowDownUp, Download, Search, Users } from 'lucide-react';
import { getGroupTruth, type MemberTruth } from '../../api/truth';
import { useApi } from '../../lib/useApi';
import { Avatar } from '../../components/truth/MemberPicker';
import { Button, EmptyState, Input, Loading, Notice, Skeleton, Stat } from '../../components/ui';
import { matches, monthLabel, num, periodStatus, tsh, wallDateTime } from '../../lib/truth';
import { useTruth } from './TruthContext';

import { t } from '../../i18n';
type Filter = 'all' | 'debt' | 'joinfee' | 'clean';
type Sort = 'name' | 'debt';

const DOT: Record<string, string> = {
  good: 'bg-standing-good',
  warn: 'bg-standing-warn',
  overdue: 'bg-standing-overdue',
  neutral: 'bg-ink-100',
};

export function OverviewTab() {
  const { groupId, version, openMember, goTab, settings } = useTruth();
  const { data, error } = useApi(settings ? () => getGroupTruth(groupId) : null, groupId, [version, settings]);
  const [q, setQ] = useState('');
  const [filter, setFilter] = useState<Filter>('all');
  const [sort, setSort] = useState<Sort>('debt');

  const rows = useMemo(() => {
    if (!data) return [];
    let r = data.members.filter((m) => matches(q, m.fullName, m.memberNumber, ...m.phones));
    if (filter === 'debt') r = r.filter((m) => m.totals.arrears > 0);
    if (filter === 'joinfee') r = r.filter((m) => m.totals.joinFeeOutstanding > 0);
    if (filter === 'clean') r = r.filter((m) => m.totals.totalDebtIncludingJoinFee === 0);
    return [...r].sort((a, b) =>
      sort === 'name'
        ? a.fullName.localeCompare(b.fullName)
        : b.totals.totalDebtIncludingJoinFee - a.totals.totalDebtIncludingJoinFee || a.fullName.localeCompare(b.fullName)
    );
  }, [data, q, filter, sort]);

  if (!settings) return null;
  if (error) return <Notice tone="overdue">{error}</Notice>;
  if (!data)
    return (
      <div className="space-y-3">
        <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-20" />
          ))}
        </div>
        <Loading label={t("Injini inahesabu upya taarifa za wanachama wote...")} />
      </div>
    );

  const tot = data.totals;
  const counts = {
    all: data.members.length,
    debt: data.members.filter((m) => m.totals.arrears > 0).length,
    joinfee: data.members.filter((m) => m.totals.joinFeeOutstanding > 0).length,
    clean: data.members.filter((m) => m.totals.totalDebtIncludingJoinFee === 0).length,
  };
  const broken = data.members.filter((m) => !m.invariantOk);

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-3">
        <Stat label={t("Michango iliyolipwa")} value={tsh(tot.contributionPaid)} hint={t("M-Koba {0} · Manual {1}", { 0: num(tot.acceptedFromMkoba), 1: num(tot.acceptedManual) })} />
        <Stat label={t("Faini zilizolipwa")} value={tsh(tot.finePaid)} />
        <Stat label={t("Kianzio kilicholipwa")} value={tsh(tot.joinFeePaid)} hint={t("Kinadaiwa {0}", { 0: num(tot.joinFeeOutstanding) })} tone={tot.joinFeeOutstanding > 0 ? 'warn' : 'good'} />
        <Stat label={t("Deni la michango")} value={tsh(tot.arrears)} hint={t("Wanachama {0} wanadaiwa", { 0: counts.debt })} tone={tot.arrears > 0 ? 'overdue' : 'good'} />
        <Stat label={t("Akiba ya wanachama")} value={tsh(tot.savings)} />
        <Stat label={t("Mikopo inayodaiwa")} value={tsh(tot.loanOutstanding)} hint={t("Imerejeshwa {0}", { 0: num(tot.loanRepaid) })} />
      </div>

      {tot.statementGapCount > 0 && (
        <Notice
          tone="overdue"
          title={t("Balance ya PDF ya M-Koba imeruka mara {0}", { 0: tot.statementGapCount })}
          action={
            <Button size="sm" variant="secondary" onClick={() => goTab('mapengo')}>
              {t("Tazama mapengo")}
            </Button>
          }
        >
          {tot.statementGapAmount > 0 && (
            <>
              <b className="figure">{tsh(tot.statementGapAmount)}</b>{' '}{t("ziliingia bila mstari kwenye PDF.")}{' '}
            </>
          )}
          {tot.statementGapOutflow > 0 && (
            <>
              <b className="figure">{tsh(tot.statementGapOutflow)}</b>{' '}{t("zilitoka bila mstari.")}{' '}
            </>
          )}
          {t("Miamala ya kabla na baada ya kila pengo ni sahihi; pesa ya pengo haihesabiwi kwa mtu yeyote mpaka mwenyewe atambuliwe kwa ushahidi na viongozi wawili.")}
        </Notice>
      )}
      {tot.unattributedMkobaMoney > 0 && (
        <Notice
          tone="warn"
          title={t("{0} za M-Koba hazijulikani ni za nani", { 0: tsh(tot.unattributedMkobaMoney) })}
          action={
            <div className="flex flex-wrap gap-2">
              <Button size="sm" variant="secondary" onClick={() => goTab('simu')}>
                {t("Namba za simu")}
              </Button>
              <Button size="sm" variant="secondary" onClick={() => goTab('mapengo')}>
                {t("Mapengo")}
              </Button>
            </div>
          }
        >
          {t("Ni pesa kutoka namba zisizounganishwa na mwanachama, au mistari iliyokosekana kwenye PDF. Hazihesabiwi kwa mtu yeyote hadi zitatuliwe.")}
        </Notice>
      )}
      {broken.length > 0 && (
        <Notice tone="overdue" title={t("Hesabu hazilingani kwa baadhi ya wanachama")}>
          {broken.map((b) => b.fullName).join(', ')}{' '}{t("— tafadhali ripoti kwa msimamizi wa mfumo.")}
        </Notice>
      )}

      <div className="flex flex-col gap-3 lg:flex-row lg:items-center">
        <div className="relative flex-1">
          <Search size={16} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-500" />
          <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder={t("Tafuta jina, namba au simu...")} className="pl-9" />
        </div>
        <div className="-mx-4 flex gap-1.5 overflow-x-auto px-4 lg:mx-0 lg:px-0">
          {(
            [
              ['all', t('Wote')],
              ['debt', t('Wanadaiwa')],
              ['joinfee', t('Kianzio')],
              ['clean', t('Safi')],
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
          <button
            onClick={() => setSort(sort === 'debt' ? 'name' : 'debt')}
            className="inline-flex shrink-0 items-center gap-1 rounded-full bg-paper-raised px-3 py-1.5 text-xs font-medium text-ink-700 ring-1 ring-ink-100 hover:bg-ink-50"
            title={t("Badilisha mpangilio")}
          >
            <ArrowDownUp size={12} /> {sort === 'debt' ? t("Deni kubwa kwanza") : t("Kwa jina")}
          </button>
          <button
            onClick={() => downloadCsv(rows, data.asOf)}
            className="inline-flex shrink-0 items-center gap-1 rounded-full bg-paper-raised px-3 py-1.5 text-xs font-medium text-ink-700 ring-1 ring-ink-100 hover:bg-ink-50"
          >
            <Download size={12} /> CSV
          </button>
        </div>
      </div>

      {rows.length === 0 ? (
        <EmptyState icon={Users} title={t("Hakuna mwanachama")}>
          {t("Hakuna anayelingana na utafutaji au kichujio.")}
        </EmptyState>
      ) : (
        <>
          {/* Desktop */}
          <div className="hidden overflow-hidden rounded-2xl border border-ink-100 bg-paper-raised md:block">
            <table className="w-full text-sm">
              <thead className="bg-ink-50 text-left text-xs uppercase tracking-wide text-ink-600">
                <tr>
                  <th className="px-4 py-2.5">{t("Mwanachama")}</th>
                  <th className="px-4 py-2.5">{t("Miezi")}</th>
                  <th className="px-4 py-2.5 text-right">{t("Deni")}</th>
                  <th className="px-4 py-2.5 text-right">{t("Kianzio kinadaiwa")}</th>
                  <th className="px-4 py-2.5 text-right">{t("Akiba")}</th>
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
                          <div className="truncate font-medium text-ink-950">
                            {m.fullName}
                            {!m.invariantOk && <AlertTriangle size={13} className="ml-1 inline text-standing-overdue" />}
                          </div>
                          <div className="figure truncate text-xs text-ink-600">{m.phones[0] ?? t("Hana simu")}</div>
                        </div>
                      </div>
                    </td>
                    <td className="px-4 py-2.5">
                      <MonthDots m={m} />
                    </td>
                    <td className={`figure px-4 py-2.5 text-right ${m.totals.arrears > 0 ? 'font-semibold text-standing-overdue' : 'text-ink-500'}`}>
                      {num(m.totals.arrears)}
                    </td>
                    <td className={`figure px-4 py-2.5 text-right ${m.totals.joinFeeOutstanding > 0 ? 'text-standing-warn' : 'text-ink-500'}`}>
                      {num(m.totals.joinFeeOutstanding)}
                    </td>
                    <td className="figure px-4 py-2.5 text-right text-ink-700">{num(m.totals.savings)}</td>
                    <td className="figure px-4 py-2.5 text-right font-semibold">{num(m.totals.totalDebtIncludingJoinFee)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Simu */}
          <ul className="space-y-2 md:hidden">
            {rows.map((m) => (
              <li key={m.memberId}>
                <button
                  onClick={() => openMember(m.memberId)}
                  className="w-full rounded-2xl border border-ink-100 bg-paper-raised p-3 text-left active:bg-ink-50"
                >
                  <div className="flex items-center gap-3">
                    <Avatar name={m.fullName} />
                    <div className="min-w-0 flex-1">
                      <div className="truncate font-medium text-ink-950">{m.fullName}</div>
                      <div className="figure truncate text-xs text-ink-600">{m.phones[0] ?? t("Hana simu")}</div>
                    </div>
                    <div className="text-right">
                      <div className={`figure text-sm font-semibold ${m.totals.totalDebtIncludingJoinFee > 0 ? 'text-standing-overdue' : 'text-standing-good'}`}>
                        {num(m.totals.totalDebtIncludingJoinFee)}
                      </div>
                      <div className="text-[10px] text-ink-600">{t("jumla ya deni")}</div>
                    </div>
                  </div>
                  <div className="mt-2.5 flex items-center justify-between gap-2">
                    <MonthDots m={m} />
                    <span className="figure text-[11px] text-ink-600">
                      {t("Deni")}{' '}{num(m.totals.arrears)}{' '}{t("· Kianzio")}{' '}{num(m.totals.joinFeeOutstanding)}
                    </span>
                  </div>
                </button>
              </li>
            ))}
          </ul>
        </>
      )}
      <p className="text-center text-xs text-ink-600">{t("Imehesabiwa")}{' '}{wallDateTime(data.asOf)}{' '}{t("· Bofya mwanachama kuona kila shilingi ilikokwenda.")}</p>
    </div>
  );
}

function MonthDots({ m }: { m: MemberTruth }) {
  const months = m.monthlyStatement.filter((x) => x.status !== 'NotYetOpen').slice(-9);
  return (
    <div className="flex items-center gap-1">
      {months.map((x) => {
        const st = periodStatus(x.status);
        return (
          <span
            key={x.month}
            title={`${monthLabel(x.month)}: ${st.label}${x.outstanding > 0 ? t(' (deni {0})', { 0: num(x.outstanding) }) : ''}`}
            className={`h-3 w-3 rounded-sm ${DOT[st.tone]} ${x.status === 'Open' ? 'ring-1 ring-ink-500 ring-offset-1' : ''}`}
          />
        );
      })}
    </div>
  );
}

function downloadCsv(rows: MemberTruth[], asOf: string | null) {
  const months = rows[0]?.monthlyStatement.filter((x) => x.status !== 'NotYetOpen').map((x) => x.month) ?? [];
  const head = [t('Jina'), t('Namba'), t('Simu'), ...months.map((m) => monthLabel(m)), t('Deni'), t('Kianzio kilicholipwa'), t('Kianzio kinadaiwa'), t('Akiba'), t('Jumla ya deni')];
  const esc = csvCell; // SASISHO16: salama dhidi ya fomula
  const lines = rows.map((m) => {
    const cells = months.map((mo) => m.monthlyStatement.find((x) => x.month === mo)?.excelCell ?? '');
    return [m.fullName, m.memberNumber, m.phones.join(' '), ...cells, m.totals.arrears, m.totals.joinFeePaid, m.totals.joinFeeOutstanding, m.totals.savings, m.totals.totalDebtIncludingJoinFee]
      .map(esc)
      .join(',');
  });
  const blob = new Blob(['\ufeff' + [head.map(esc).join(','), ...lines].join('\n')], { type: 'text/csv;charset=utf-8' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = `ukweli_${(asOf ?? '').slice(0, 10) || 'leo'}.csv`;
  a.click();
  URL.revokeObjectURL(a.href);
}
